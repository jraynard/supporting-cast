using HeroesNotIncluded.Sim.Components;

namespace HeroesNotIncluded.Sim.Combat;

/// <summary>
/// This attacker has its turn pressing <see cref="Target"/> (<see cref="Engagement"/>). Taken when
/// it closes in to strike, given back when its fight ends (<c>AiSystem.ReleasePawn</c>).
/// </summary>
public struct Pressing
{
    public Entity Target;
    public long Since;
}

/// <summary>
/// Who may press whom. A band doesn't all hack at one person at once: only so many press a target
/// at a time (one on a player's character, who has to be able to answer each of them; two on
/// anyone else), and the rest circle a little way off and wait their turn (<see cref="FreeFight"/>), or go for someone else.
/// </summary>
public static class Engagement
{
    /// <summary>How many may press a player's character at once.</summary>
    public const int OnPlayer = 1;

    /// <summary>How many may press anyone else at once.</summary>
    public const int OnOthers = 2;

    /// <summary>How long one who's waiting (circling, <see cref="FreeFight"/>) stays waiting before it thinks again (another target).</summary>
    public const int WaitTicks = SimClock.TicksPerSecond * 6;

    /// <summary>The pause between taking a turn and the first blow: a beat to see them coming.</summary>
    public const int FirstBlowTicks = SimClock.TicksPerSecond * 3 / 4;

    /// <summary>Ticks between <paramref name="attacker"/>'s blows at <paramref name="target"/>: the attack's own pace.</summary>
    public static int Cooldown(Simulation sim, Defs.AttackDef attack, Entity target) => attack.CooldownTicks;

    public static int Limit(Simulation sim, Entity target) => sim.World.Has<Avatar>(target) ? OnPlayer : OnOthers;

    /// <summary>Is <paramref name="attacker"/> pressing <paramref name="target"/> already?</summary>
    public static bool Presses(Simulation sim, Entity attacker, Entity target) =>
        sim.World.TryGet<Pressing>(attacker, out var p) && p.Target == target;

    /// <summary>Those on their feet pressing <paramref name="target"/>, other than <paramref name="except"/>.</summary>
    public static int Pressers(Simulation sim, Entity target, Entity except)
    {
        var w = sim.World;
        var n = 0;
        foreach (var e in w.Each<Pressing>())
            if (e != except && w.Get<Pressing>(e).Target == target && w.TryGet<Creature>(e, out var c) && c.Able)
                n++;
        return n;
    }

    /// <summary>Could <paramref name="attacker"/> press <paramref name="target"/> now (already is, or there's room)?</summary>
    public static bool HasRoom(Simulation sim, Entity attacker, Entity target) =>
        Presses(sim, attacker, target) || Pressers(sim, target, attacker) < Limit(sim, target);

    /// <summary>Take a turn pressing <paramref name="target"/> if there's room. True if it's this attacker's turn.</summary>
    public static bool TryPress(Simulation sim, Entity attacker, Entity target)
    {
        if (Presses(sim, attacker, target))
            return true;
        if (Pressers(sim, target, attacker) >= Limit(sim, target))
            return false;
        var pressing = new Pressing { Target = target, Since = sim.Clock.Tick };
        if (sim.World.Has<Pressing>(attacker))
            sim.World.Get<Pressing>(attacker) = pressing;
        else
            sim.World.Add(attacker, pressing);
        return true;
    }

    /// <summary>Give the turn back (the fight's over for this attacker, or it's moved on).</summary>
    public static void Release(Simulation sim, Entity attacker)
    {
        if (sim.World.Has<Pressing>(attacker))
            sim.World.Remove<Pressing>(attacker);
    }
}
