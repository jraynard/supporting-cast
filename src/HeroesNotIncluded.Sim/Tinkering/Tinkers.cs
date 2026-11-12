using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tinkering;

/// <summary>
/// The tinker's bench (docs/DESIGN.md, "Tinker"): what's made at it by hand, and what an AI's
/// ordinary craft makes there. A player in a round pins a lock (<see cref="PinSession"/>) or fits a
/// contraption together (<see cref="FitSession"/>) once the bench has what it needs; an AI villager
/// works the bill the plain way, the lock cut and the parts fitted as their skill allows.
/// </summary>
public static class Tinkers
{
    public const string BenchDef = "TinkerBench";
    public const string BarDef = "IronBar";

    public static bool IsBench(Simulation sim, Entity e) =>
        sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Name == BenchDef && !sim.World.Has<Construction>(e);

    /// <summary>An iron bar on someone (in hand or in the pack): a key blank's metal. Entity.Null if none.</summary>
    public static Entity Bar(Simulation sim, Entity who)
    {
        var w = sim.World;
        bool IsBar(Entity e) => w.IsAlive(e) && w.TryGet<Thing>(e, out var t) && t.Def.Name == BarDef;
        var held = w.Get<Carrying>(who).Item;
        if (IsBar(held))
            return held;
        return sim.PackOf(who) is { IsNull: false } pack ? sim.StoredIn(pack).FirstOrDefault(IsBar, Entity.Null) : Entity.Null;
    }

    public static bool MakesLock(RecipeDef r) => r.Products.Exists(p => p.ThingDef.Name == Locksmithing.LockDef);

    public static ContraptionProps? MakesContraption(RecipeDef r) => r.Products.Find(p => p.ThingDef.Contraption != null)?.ThingDef.Contraption;

    /// <summary>Should a player's work on this recipe at this station be done by hand? In a round, at the tinker's bench, for a lock or a contraption.</summary>
    public static bool Applies(Simulation sim, Entity worker, Entity station, RecipeDef recipe) =>
        sim.Round.Phase == Round.RoundPhase.Playing && sim.World.Has<Avatar>(worker) && IsBench(sim, station)
        && (MakesLock(recipe) || MakesContraption(recipe) != null);

    /// <summary>A player works a stocked bench: the hand game for the recipe (<see cref="Applies"/>).</summary>
    public static void WorkAt(Simulation sim, Entity worker, Entity station, RecipeDef recipe)
    {
        if (sim.World.Has<Work.WorkSession>(worker))
            return;
        if (MakesLock(recipe))
            PinSession.StartLock(sim, worker, station);
        else if (MakesContraption(recipe) is { } c)
            FitSession.StartMaking(sim, worker, station, c);
    }

    /// <summary>
    /// What F offers at a door or a wagon, as its prompt (the game opens a window of what can be
    /// done): a door that's locked, or one you carry a lock to fit to or a key that turns; a
    /// party's wagon. Null for anything else, so an unlocked door is walked through as ever.
    /// </summary>
    public static string? ActionAt(Simulation sim, Entity who, Entity site)
    {
        var w = sim.World;
        if (Wagons.IsWagon(sim, site))
            return "Look the wagon over";
        if (!Locksmithing.IsDoor(sim, site))
            return null;
        var label = w.Get<Thing>(site).Label;
        if (w.TryGet<LockCut>(site, out var cut))
            return sim.IsLocked(site) ? $"The lock on the {label}"
                : !Locksmithing.KeyFor(sim, who, cut.Seed).IsNull ? $"Lock the {label}" : null;
        return CarriedLock(sim, who).IsNull ? null : $"Fit a lock to the {label}";
    }

    /// <summary>A lock item someone carries (not yet fitted); Entity.Null if none.</summary>
    public static Entity CarriedLock(Simulation sim, Entity who)
    {
        var w = sim.World;
        var held = w.Get<Carrying>(who).Item;
        if (Locksmithing.IsLock(sim, held))
            return held;
        return sim.PackOf(who) is { IsNull: false } pack ? sim.StoredIn(pack).FirstOrDefault(e => Locksmithing.IsLock(sim, e), Entity.Null) : Entity.Null;
    }

    /// <summary>
    /// Products just made the ordinary way (an AI's bill, not played): a lock is cut and keyed as
    /// the maker's skill allows, and a contraption is put together sound. The keys join the products.
    /// </summary>
    public static void Made(Simulation sim, List<Entity> made, Entity maker)
    {
        var w = sim.World;
        var level = SkillRules.LevelOf(sim, maker, Skill.Crafting);
        var skill = Math.Clamp(0.3f + 0.05f * level, 0.3f, 0.8f);
        foreach (var item in made.ToList())
        {
            var def = w.Get<Thing>(item).Def;
            if (def.Name == Locksmithing.LockDef && !w.Has<LockCut>(item))
                made.Add(Locksmithing.CutLock(sim, item, sim.Rand.NextUInt() | 1u, Locksmithing.PinsFor(level), skill, 0.4f + 0.6f * skill, maker));
            else if (def.Contraption != null && !w.Has<Contraption>(item))
                w.Add(item, new Contraption { Maker = maker });
        }
    }
}
