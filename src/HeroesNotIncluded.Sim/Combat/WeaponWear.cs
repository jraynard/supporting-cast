using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;

namespace SupportingCast.Sim.Combat;

/// <summary>
/// Weapons wear in two ways that need different mending (docs/DESIGN.md, "Trade notes"): a blade's
/// edge dulls with use (<see cref="Edge"/>, put right on a whetstone), and every weapon chips and
/// bends, worse against armor (its HP, hammered out at the forge). Hammering flattens an edge, so
/// a chipped blade is hammered first and sharpened after. No random draws: wear never changes
/// what the fight rolls.
/// </summary>
public static class WeaponWear
{
    /// <summary>Dullness a blade gains landing a blow on flesh, and on armor that turns it.</summary>
    public const float DullPerHit = 0.025f, DullPerDeflect = 0.06f;

    /// <summary>HP a weapon loses per blow, and the share of a turned blow's damage it takes on top.</summary>
    public const float ChipPerHit = 0.3f, ChipFromDeflect = 0.15f;

    /// <summary>A blunt blade cuts this much worse at full dullness.</summary>
    public const float DullDamageLoss = 0.5f;

    /// <summary>What an adventurer will accept back as mended.</summary>
    public const float MendedHp = 0.9f, MendedDullness = 0.15f;

    /// <summary>Wear at which an adventurer wants it seen to.</summary>
    public const float WantsMendHp = 0.75f, WantsMendDullness = 0.4f;

    /// <summary>Hammering out chips leaves a blade at least this dull.</summary>
    public const float DullAfterHammering = 0.5f;

    /// <summary>Chance a cracked blade (<see cref="Work.PieceFlaw.Crack"/>) snaps when a blow is turned.</summary>
    public const float CrackSnapChance = 0.04f;

    /// <summary>A melee weapon with a cutting or stabbing edge (bows and clubs have none).</summary>
    public static bool Edged(ThingDef def) => def.Weapon is { Ranged: false, DamageType: DamageType.Cut or DamageType.Pierce };

    public static float Dullness(Simulation sim, Entity item) =>
        sim.World.TryGet<Edge>(item, out var e) ? e.Dullness : 0f;

    public static void SetDullness(Simulation sim, Entity item, float dullness)
    {
        dullness = Math.Clamp(dullness, 0f, 1f);
        if (sim.World.Has<Edge>(item))
            sim.World.Get<Edge>(item).Dullness = dullness;
        else if (dullness > 0)
            sim.World.Add(item, new Edge { Dullness = dullness });
    }

    public static float HpFraction(Simulation sim, Entity item)
    {
        var t = sim.World.Get<Thing>(item);
        return t.MaxHp > 0 ? Math.Clamp(t.Hp / t.MaxHp, 0f, 1f) : 1f;
    }

    /// <summary>What wear does to the weapon's damage.</summary>
    public static float DamageFactor(Simulation sim, Entity weapon) =>
        Edged(sim.World.Get<Thing>(weapon).Def) ? 1f - DullDamageLoss * Dullness(sim, weapon) : 1f;

    /// <summary>A melee blow landed (on flesh, or turned by armor): the weapon pays for it, and breaks at 0 HP.</summary>
    public static void Struck(Simulation sim, Entity attacker, Entity weapon, bool turned, float damage)
    {
        var def = sim.World.Get<Thing>(weapon).Def;
        if (def.Weapon is not { Ranged: false })
            return;
        // A hidden flaw from the forge shows only here: soft steel dulls fast, brittle steel chips,
        // and a cracked blade may let go on a blow that's turned.
        var flaw = sim.World.TryGet<Work.Flawed>(weapon, out var f) ? f.Flaw : Work.PieceFlaw.None;
        if (Edged(def))
            SetDullness(sim, weapon, Dullness(sim, weapon) + (turned ? DullPerDeflect : DullPerHit) * (flaw == Work.PieceFlaw.Soft ? 2f : 1f));
        ref var t = ref sim.World.Get<Thing>(weapon);
        t.Hp -= (ChipPerHit + (turned ? damage * ChipFromDeflect : 0f)) * (flaw == Work.PieceFlaw.Brittle ? 2f : 1f);
        if (flaw == Work.PieceFlaw.Crack && turned && sim.Rand.NextFloat() < CrackSnapChance)
            t.Hp = 0f;
        if (t.Hp > 0)
            return;
        var label = t.Label;
        sim.World.Remove<Equipment>(attacker);
        sim.DestroyThing(weapon);
        var c = sim.World.Get<Creature>(attacker);
        sim.Messages.Add(c.Faction == Faction.Village ? MessageKind.Warning : MessageKind.Info,
            $"{c.Name}'s {label} breaks.", sim.LocationOf(attacker));
    }

    /// <summary>
    /// Mended by hand at a forge: made at one (a station that wants hand work), so its chips can be
    /// hammered out there. A club or a bow is a woodworker's, not the smith's.
    /// </summary>
    public static bool Forged(Simulation sim, ThingDef def)
    {
        foreach (var r in sim.Defs.AllRecipes)
            if (r.Products.Exists(p => p.ThingDef == def) && r.Station.Length > 0
                && sim.Defs.TryThing(r.Station, out var station) && station.Building?.Workbench is { HandWork: true })
                return true;
        return false;
    }

    /// <summary>Worn in a way the smith can put right: chips in forged gear, or a dull edge.</summary>
    public static bool SmithCanMend(Simulation sim, Entity item)
    {
        var def = sim.World.Get<Thing>(item).Def;
        return (Forged(sim, def) && HpFraction(sim, item) < WantsMendHp) || (Edged(def) && Dullness(sim, item) > WantsMendDullness);
    }

    /// <summary>Worn enough that its owner wants it seen to.</summary>
    public static bool NeedsMending(Simulation sim, Entity item)
    {
        var def = sim.World.Get<Thing>(item).Def;
        if (def.Weapon == null && def.Apparel is not { Sharp: > 0 } and not { Blunt: > 0 })
            return false;
        return HpFraction(sim, item) < WantsMendHp || (Edged(def) && Dullness(sim, item) > WantsMendDullness);
    }

    /// <summary>Good enough to hand back: sound, and keen if it's a blade.</summary>
    public static bool Mended(Simulation sim, Entity item) =>
        HpFraction(sim, item) >= MendedHp && (!Edged(sim.World.Get<Thing>(item).Def) || Dullness(sim, item) <= MendedDullness);

    /// <summary>What's wrong with it, as the owner would put it ("still dull"); empty if nothing.</summary>
    public static string Faults(Simulation sim, Entity item)
    {
        var faults = new List<string>();
        if (HpFraction(sim, item) < MendedHp)
            faults.Add("chipped");
        if (Edged(sim.World.Get<Thing>(item).Def) && Dullness(sim, item) > MendedDullness)
            faults.Add("dull");
        return string.Join(" and ", faults);
    }

    /// <summary>The state of a weapon or armor as someone holding it can tell (<c>PerceptionView.Condition</c>).</summary>
    public static string Describe(Simulation sim, Entity item)
    {
        var def = sim.World.Get<Thing>(item).Def;
        var hp = HpFraction(sim, item);
        var body = hp >= 0.95f ? "sound" : hp >= MendedHp ? "nicked" : hp >= 0.5f ? "chipped" : hp >= 0.25f ? "badly chipped and bent" : "close to breaking";
        if (!Edged(def))
            return body;
        var dull = Dullness(sim, item);
        var edge = dull <= MendedDullness ? "keen" : dull <= WantsMendDullness ? "a little dull" : dull <= 0.75f ? "dull" : "blunt as a spoon";
        return $"{edge}, {body}";
    }
}
