using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Combat;

/// <summary>A player's bow drawn and aimed (<see cref="Archery"/>), since <see cref="Start"/>.</summary>
public struct Aiming
{
    public long Start;
}

/// <summary>
/// A crossbow cranked and ready (<see cref="Defs.AttackDef.HoldsLoad"/>): on the weapon itself, so it
/// stays loaded let go, sheathed or handed to someone else, until it's loosed.
/// </summary>
public struct Loaded;

/// <summary>
/// A player's bow: held drawn and aimed (right button), and loosed (left) once it's fully drawn,
/// along the aim at that moment (<see cref="Reach"/>: the first one in its line, short of a wall).
/// Keep the draw held and the next arrow's drawn after each. Moving drawn is slower; a guard lets it
/// down. A crossbow is cranked the same way (right button held), but once it's cranked it stays
/// loaded (<see cref="Loaded"/>) let go or not, and fires with a click whenever it's wanted. An
/// AI's bow is a windup like any blow (<see cref="Blows"/>).
/// </summary>
public static class Archery
{
    /// <summary>The bow in hand, if it's a bow (a ranged attack); null otherwise.</summary>
    public static AttackDef? Bow(Simulation sim, Entity e) =>
        CombatRules.Attacks(sim, e).FirstOrDefault() is { Ranged: true } bow ? bow : null;

    /// <summary>How long it takes <paramref name="e"/> to draw fully: the bow's draw, quicker with skill.</summary>
    public static int DrawTicks(Simulation sim, Entity e, AttackDef bow) =>
        Math.Max(1, (int)(bow.WindupTicks * Reach.WindupFactor(sim, e, bow)));

    /// <summary>Fully drawn (or a crossbow loaded): an arrow would fly now.</summary>
    public static bool Ready(Simulation sim, Entity e) => IsLoaded(sim, e) || Drawn(sim, e);

    /// <summary>The draw (or the crank) held all the way.</summary>
    private static bool Drawn(Simulation sim, Entity e) =>
        sim.World.TryGet<Aiming>(e, out var a) && Bow(sim, e) is { } bow && sim.Clock.Tick - a.Start >= DrawTicks(sim, e, bow);

    /// <summary>The weapon in hand, if it's a crossbow (<see cref="Defs.AttackDef.HoldsLoad"/>); Entity.Null otherwise.</summary>
    public static Entity Crossbow(Simulation sim, Entity e) =>
        sim.World.TryGet<Equipment>(e, out var eq) && sim.World.IsAlive(eq.Weapon) && sim.World.Get<Thing>(eq.Weapon).Def.Weapon is { HoldsLoad: true }
            ? eq.Weapon : Entity.Null;

    /// <summary>A crossbow in hand, cranked and loaded.</summary>
    public static bool IsLoaded(Simulation sim, Entity e) => Crossbow(sim, e) is { IsNull: false } c && sim.World.Has<Loaded>(c);

    /// <summary>A crossbow's crank done: it's loaded now, held or not.</summary>
    private static void Settle(Simulation sim, Entity e)
    {
        if (Crossbow(sim, e) is { IsNull: false } c && !sim.World.Has<Loaded>(c) && Drawn(sim, e))
            sim.World.Add(c, new Loaded());
    }

    /// <summary>Start drawing (as of <paramref name="at"/>), or let it down. False if it can't be drawn now.</summary>
    public static bool Draw(Simulation sim, Entity e, bool up, long at)
    {
        var w = sim.World;
        if (!up)
        {
            Settle(sim, e); // a crossbow cranked all the way stays loaded; a bow's draw is let down
            if (w.Has<Aiming>(e))
                w.Remove<Aiming>(e);
            return true;
        }
        if (w.Has<Aiming>(e) || Bow(sim, e) is null || !w.Get<Creature>(e).Able || w.Has<Windup>(e)
            || !(w.TryGet<FreeMover>(e, out var free) && free.Drawn))
            return false;
        ref var g = ref Blows.GuardOf(sim, e);
        if (g.StaggeredUntil > sim.Clock.Tick || g.RaisedAt >= 0)
            return false;
        w.Add(e, new Aiming { Start = Math.Max(at, g.NextSwing) });
        return true;
    }

    /// <summary>
    /// Loose along <paramref name="aim"/>, if it's fully drawn and there's wind for it: it flies at
    /// once (a windup landing next tick), and the next draw starts. False if it wasn't ready.
    /// </summary>
    public static bool Loose(Simulation sim, Entity e, float aim, long? pressed)
    {
        var w = sim.World;
        if (!Ready(sim, e) || Bow(sim, e) is not { } bow)
            return false;
        ref var g = ref Blows.GuardOf(sim, e);
        if (g.Stamina < Blows.SwingCost)
            return false;
        g.Spend(Blows.SwingCost, sim.Clock.Tick);
        var now = sim.Clock.Tick;
        var loaded = IsLoaded(sim, e);
        var windup = new Windup
        {
            Target = Entity.Null,
            Attack = CombatRules.Attacks(sim, e).IndexOf(bow),
            Start = w.TryGet<Aiming>(e, out var aiming) && !loaded ? aiming.Start : now,
            Impact = now + 1,
            Aim = aim,
            Lag = pressed is { } at ? (int)(now - Blows.Stamp(sim, at)) : 0,
        };
        w.Add(e, windup);
        if (w.Has<Skills>(e))
            Systems.SkillRules.Learn(sim, e, Skill.Shooting, Systems.SkillRules.XpPerAttack);
        if (w.Has<FreeMover>(e))
            w.Get<FreeMover>(e).Aim = aim;
        // A crossbow's loosed its bolt: empty till it's cranked again.
        if (Crossbow(sim, e) is { IsNull: false } crossbow && w.Has<Loaded>(crossbow))
            w.Remove<Loaded>(crossbow);
        // Still drawn: the next arrow's nocked and drawn (the crossbow cranked) from now.
        if (w.Has<Aiming>(e))
            w.Get<Aiming>(e) = new Aiming { Start = now + 1 };
        return true;
    }
}
