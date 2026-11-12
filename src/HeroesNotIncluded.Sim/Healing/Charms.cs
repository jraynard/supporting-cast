using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Healing;

/// <summary>A charm as it went out on a quest with its bearer (<c>Expeditions.MemberRecord</c>).</summary>
public sealed record CharmRecord(MendingDef Mending, int Extra, float Quality, Entity Maker);

/// <summary>
/// Weaving ahead (docs/DESIGN.md, "Healer"): an empath weaves a mending into a strip of linen
/// instead of onto a wound (<see cref="CastSession.IntoCharm"/>), and it keeps. Torn, it lands on
/// whoever tears it as if they'd cast it, the gift or no. Carried on a quest it's a ward: it
/// spends itself on the first wound it fits, and may turn a death into a bad wound. Charms go on
/// show and sell like the smith's gear, so the healer has work before anyone's hurt.
/// </summary>
public static class Charms
{
    /// <summary>The thing def charms are made as.</summary>
    public const string Def = "Charm";

    /// <summary>The most charms an adventurer will carry; past that they won't buy another.</summary>
    public const int MostCarried = 2;

    /// <summary>What a charm's worth to a buyer against a plain piece of gear (before its strength).</summary>
    public const float Worth = 2f;

    public static bool IsCharm(Simulation sim, Entity item) => sim.World.Has<Charm>(item);

    /// <summary>Knot a woven mending into a charm (using up a bandage to hand), into the maker's pack. Entity.Null if there was no linen.</summary>
    public static Entity Make(Simulation sim, Entity maker, MendingDef mending, int extra, float quality)
    {
        if (!Bandaging.TakeBandage(sim, maker))
            return Entity.Null;
        var item = sim.CreateItem(sim.Defs.Thing(Def), 1, null);
        sim.World.Add(item, new Charm { Mending = mending, Extra = extra, Quality = Math.Clamp(quality, 0f, 1f), Maker = maker });
        Inventory.Stow(sim, maker, item);
        return item;
    }

    /// <summary>The charms someone has on them (in hand or in the pack), in a fixed order.</summary>
    public static List<Entity> Carried(Simulation sim, Entity who)
    {
        var list = new List<Entity>();
        var w = sim.World;
        if (w.TryGet<Carrying>(who, out var c) && !c.Item.IsNull && w.Has<Charm>(c.Item))
            list.Add(c.Item);
        if (sim.PackOf(who) is { IsNull: false } pack)
            foreach (var item in sim.StoredIn(pack))
                if (w.Has<Charm>(item))
                    list.Add(item);
        return list;
    }

    /// <summary>Does this mending take hold of a wound of this kind (or its festering)?</summary>
    public static bool Fits(MendingDef m, DamageType type, bool festering = false) =>
        m.Heals.Contains(type) || (m.Cleanses && festering);

    /// <summary>
    /// The wound a torn charm lands on: the worst the mending fits, or 0 for one that needs none
    /// (or when nothing fits: it's spent for nothing).
    /// </summary>
    public static int Target(Simulation sim, Entity who, MendingDef m)
    {
        if (!sim.World.TryGet<Health>(who, out var h))
            return 0;
        Injury? best = null;
        foreach (var inj in h.Injuries)
            if (Fits(m, inj.Type, inj.Infection != null) && (best == null || inj.Damage > best.Damage))
                best = inj;
        return best == null ? 0 : Empathy.KeyOf(best);
    }

    /// <summary>Tear a charm: its mending lands on the one who tears it. Returns whether it did anything.</summary>
    public static bool Tear(Simulation sim, Entity who, Entity item)
    {
        var w = sim.World;
        if (!w.TryGet<Charm>(item, out var charm) || !Inventory.OnPerson(sim, who, item))
            return false;
        var label = charm.Mending.Label.ToLowerInvariant();
        var key = Target(sim, who, charm.Mending);
        ref var carrying = ref w.Get<Carrying>(who);
        if (carrying.Item == item)
            carrying.Item = Entity.Null;
        sim.DestroyThing(item);
        var at = sim.LocationOf(who);
        var did = Empathy.Cast(sim, who, key, charm.Mending, charm.Quality, charm.Extra);
        sim.Messages.Add(did ? MessageKind.Good : MessageKind.Info, did
            ? $"{w.Get<Creature>(who).Name} tears the charm, and the {label} takes hold."
            : $"{w.Get<Creature>(who).Name} tears the charm. The {label} finds nothing to take hold of.", at);
        return did;
    }

    // ---------------- On a quest ----------------

    /// <summary>The charms someone carries, as records to go out with them.</summary>
    public static List<CharmRecord> Records(Simulation sim, Entity who) =>
        Carried(sim, who).Select(e => sim.World.Get<Charm>(e)).Select(c => new CharmRecord(c.Mending, c.Extra, c.Quality, c.Maker)).ToList();

    /// <summary>Charms back in a returning bearer's pack.</summary>
    public static void Restore(Simulation sim, Entity who, IEnumerable<CharmRecord> charms)
    {
        foreach (var c in charms)
        {
            var item = sim.CreateItem(sim.Defs.Thing(Def), 1, null);
            sim.World.Add(item, new Charm { Mending = c.Mending, Extra = c.Extra, Quality = c.Quality, Maker = c.Maker });
            Inventory.Stow(sim, who, item);
        }
    }

    /// <summary>How much a perfect cast of this charm heals.</summary>
    public static float Heals(CharmRecord c) => c.Mending.HealAt(c.Extra) * (0.25f + 0.75f * Math.Clamp(c.Quality, 0f, 1f));

    /// <summary>A wound taken on a quest, against the bearer's charms: the first that fits spends itself on it. The damage left.</summary>
    public static float Ward(List<CharmRecord> charms, DamageType type, float damage)
    {
        var i = charms.FindIndex(c => Fits(c.Mending, type));
        if (i < 0)
            return damage;
        var left = Math.Max(0f, damage - Heals(charms[i]));
        charms.RemoveAt(i);
        return left;
    }

    /// <summary>
    /// A death on a quest, against the bearer's charms: the strongest that heals anything spends
    /// itself, and with a well-woven one they live (<paramref name="roll"/> 0..1). The charm if it
    /// spared them, else null (it's spent either way).
    /// </summary>
    public static CharmRecord? Spare(List<CharmRecord> charms, float roll)
    {
        var best = charms.Where(c => c.Mending.Heals.Count > 0).OrderByDescending(Heals).FirstOrDefault();
        if (best == null)
            return null;
        charms.Remove(best);
        return roll < 0.4f + 0.5f * best.Quality ? best : null;
    }

    /// <summary>How much a bearer's charms add to their strength for a quest: a little, for up to three that heal.</summary>
    public static float StrengthFactor(List<CharmRecord> charms) =>
        1f + 0.05f * Math.Min(3, charms.Count(c => c.Mending.Heals.Count > 0));
}
