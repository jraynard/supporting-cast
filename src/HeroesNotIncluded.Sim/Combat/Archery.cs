using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Combat;

/// <summary>A player's bow drawn and aimed (<see cref="Archery"/>), since <see cref="Start"/>.</summary>
public struct Aiming
{
    public long Start;
}

/// <summary>
/// A player's bow: held drawn and aimed (right button), and loosed (left) once it's fully drawn,
/// along the aim at that moment (<see cref="Reach"/>: the first one in its line, short of a wall).
/// Keep the draw held and the next arrow's drawn after each. Moving drawn is slower; a guard lets it
/// down. An AI's bow is a windup like any blow (<see cref="Blows"/>).
/// </summary>
public static class Archery
{
    /// <summary>The bow in hand, if it's a bow (a ranged attack); null otherwise.</summary>
    public static AttackDef? Bow(Simulation sim, Entity e) =>
        CombatRules.Attacks(sim, e).FirstOrDefault() is { Ranged: true } bow ? bow : null;

    /// <summary>How long it takes <paramref name="e"/> to draw fully: the bow's draw, quicker with skill.</summary>
    public static int DrawTicks(Simulation sim, Entity e, AttackDef bow) =>
        Math.Max(1, (int)(bow.WindupTicks * Reach.WindupFactor(sim, e, bow)));

    /// <summary>Fully drawn: an arrow would fly now.</summary>
    public static bool Ready(Simulation sim, Entity e) =>
        sim.World.TryGet<Aiming>(e, out var a) && Bow(sim, e) is { } bow && sim.Clock.Tick - a.Start >= DrawTicks(sim, e, bow);

    /// <summary>Start drawing (as of <paramref name="at"/>), or let it down. False if it can't be drawn now.</summary>
    public static bool Draw(Simulation sim, Entity e, bool up, long at)
    {
        var w = sim.World;
        if (!up)
        {
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
        g.Stamina -= Blows.SwingCost;
        var now = sim.Clock.Tick;
        var windup = new Windup
        {
            Target = Entity.Null,
            Attack = CombatRules.Attacks(sim, e).IndexOf(bow),
            Start = w.Get<Aiming>(e).Start,
            Impact = now + 1,
            Aim = aim,
            Lag = pressed is { } at ? (int)(now - Blows.Stamp(sim, at)) : 0,
        };
        w.Add(e, windup);
        if (w.Has<Skills>(e))
            Systems.SkillRules.Learn(sim, e, Skill.Shooting, Systems.SkillRules.XpPerAttack);
        if (w.Has<FreeMover>(e))
            w.Get<FreeMover>(e).Aim = aim;
        // Still drawn: the next arrow's nocked and drawn from now.
        w.Get<Aiming>(e) = new Aiming { Start = now + 1 };
        return true;
    }
}
