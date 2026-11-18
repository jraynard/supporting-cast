using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Combat;

/// <summary>
/// A quiver as the carpenter fletched it (<c>Carpentry.FletchSession</c>). Its quality shows (as
/// <see cref="Crafted"/>); <see cref="Warped"/> doesn't: a shaft sighted near enough but not true
/// looks like the rest in the quiver, and only flies wide on the road. Read through
/// <c>PerceptionView.Quiver</c>, never directly.
/// </summary>
public struct Fletched
{
    public float Quality;
    public Entity Maker;
    public bool Warped;
}

/// <summary>A quiver as it went out on a quest with its archer (<c>Expeditions.MemberRecord</c>).</summary>
public sealed record QuiverRecord(float Quality, bool Warped, Entity Maker);

/// <summary>
/// Arrows for the road (docs/DESIGN.md, "Carpenter"): the carpenter's work that leaves town, as the
/// cook's rations and the empath's charms do. Fletched at the carpenter's bench from a plank and a
/// bundle of feathers; set out for sale in the woodshop like any gear; bought, one to an archer, by
/// adventurers who shoot. Shot away on the quest: a true quiver makes its archer count for more,
/// a warped one for a little less.
/// </summary>
public static class Fletching
{
    /// <summary>The thing def a quiver is made as.</summary>
    public const string Def = "Quiver";

    /// <summary>The most an archer carries; past that they won't buy another.</summary>
    public const int MostCarried = 1;

    /// <summary>What a quiver is worth to a buyer against a plain piece of gear (before how well it's made).</summary>
    public const float Worth = 0.6f;

    /// <summary>How much stronger an archer is for a quest with a quiver, at best (perfectly fletched).</summary>
    public const float ArrowBonus = 0.1f;

    /// <summary>How much weaker with a warped quiver: shots that fly wide, and a bowman who trusted them.</summary>
    public const float WarpedFactor = 0.96f;

    public static bool IsQuiver(Simulation sim, Entity item) => sim.World.Has<Fletched>(item);

    /// <summary>Someone who fights at range (their weapon is a bow or a crossbow): the only buyers of a quiver.</summary>
    public static bool Shoots(Simulation sim, Entity who) =>
        sim.World.TryGet<Equipment>(who, out var eq) && sim.World.IsAlive(eq.Weapon) && sim.World.Get<Thing>(eq.Weapon).Def.Weapon is { Ranged: true };

    /// <summary>A new quiver, unspawned, as fletched.</summary>
    public static Entity Make(Simulation sim, Entity maker, float quality, bool warped)
    {
        var item = sim.CreateItem(sim.Defs.Thing(Def), 1);
        Mark(sim, item, maker, quality, warped);
        return item;
    }

    /// <summary>Give a made quiver its fletching (quality, and the hidden warp).</summary>
    public static void Mark(Simulation sim, Entity item, Entity maker, float quality, bool warped)
    {
        var w = sim.World;
        quality = Math.Clamp(quality, 0f, 1f);
        w.Add(item, new Fletched { Quality = quality, Maker = maker, Warped = warped });
        if (w.Has<Crafted>(item))
            w.Remove<Crafted>(item);
        w.Add(item, new Crafted { Quality = Crafting.QualityFrom(quality), Maker = maker });
    }

    /// <summary>The quivers someone has on them (in hand or in the pack), in a fixed order.</summary>
    public static List<Entity> Carried(Simulation sim, Entity who)
    {
        var list = new List<Entity>();
        var w = sim.World;
        if (w.TryGet<Carrying>(who, out var c) && !c.Item.IsNull && w.Has<Fletched>(c.Item))
            list.Add(c.Item);
        if (sim.PackOf(who) is { IsNull: false } pack)
            foreach (var item in sim.StoredIn(pack))
                if (w.Has<Fletched>(item))
                    list.Add(item);
        return list;
    }

    // ---------------- On a quest ----------------

    /// <summary>The quivers someone carries, as records to go out with them.</summary>
    public static List<QuiverRecord> Records(Simulation sim, Entity who) =>
        Carried(sim, who).Select(e =>
        {
            var f = sim.World.Get<Fletched>(e);
            return new QuiverRecord(f.Quality, f.Warped, f.Maker);
        }).ToList();

    /// <summary>How much an archer's quiver adds to (or, warped, takes from) their strength for a quest. 1 without one, or for someone who doesn't shoot.</summary>
    public static float StrengthFactor(bool shoots, List<QuiverRecord> quivers)
    {
        if (!shoots || quivers.Count == 0)
            return 1f;
        var q = quivers[0];
        return q.Warped ? WarpedFactor : 1f + ArrowBonus * (0.5f + 0.5f * q.Quality);
    }

    /// <summary>Quivers not shot away (someone carrying one who doesn't shoot): back in the pack.</summary>
    public static void Restore(Simulation sim, Entity who, IEnumerable<QuiverRecord> quivers)
    {
        foreach (var q in quivers)
            Inventory.Stow(sim, who, Make(sim, q.Maker, q.Quality, q.Warped));
    }
}
