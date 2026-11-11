using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;

namespace SupportingCast.Sim.Systems;

/// <summary>
/// The status effects dragon breath leaves (<see cref="Components.Afflictions"/>, from an
/// <see cref="ElementDef.Status"/>): one component read the same way in real time (walking speed,
/// <see cref="Combat.RealtimeAttacks"/>) and in turn-based fights (<see cref="Combat.Encounter"/>'s
/// time units, <see cref="Combat.TimeUnits.WalkCost"/>). Nothing here draws from
/// <see cref="Simulation.Rand"/>: applying and wearing off are plain arithmetic, so an affliction
/// never shifts anyone else's rolls.
/// </summary>
public static class AfflictionRules
{
    /// <summary>
    /// Leave a status on <paramref name="e"/>: each kind lasts the longer of what it had and what's
    /// applied, at the stronger of the two strengths. Dead creatures and resisters are skipped by callers.
    /// </summary>
    public static void Apply(Simulation sim, Entity e, StatusProps? status, Entity source)
    {
        var w = sim.World;
        if (status is not { Kind: not StatusKind.None, Ticks: > 0 } s || !w.TryGet<Creature>(e, out var c) || c.Dead)
            return;
        if (!w.Has<Components.Afflictions>(e))
            w.Add(e, new Components.Afflictions());
        ref var a = ref w.Get<Components.Afflictions>(e);
        var until = sim.Clock.Tick + s.Ticks;
        switch (s.Kind)
        {
            case StatusKind.Chill:
                a.ChillUntil = Math.Max(a.ChillUntil, until);
                a.Chill = Math.Clamp(Math.Max(a.Chill, s.Strength), 0f, 0.9f);
                break;
            case StatusKind.Stun:
                a.StunUntil = Math.Max(a.StunUntil, until);
                a.StunTu = Math.Clamp(Math.Max(a.StunTu, s.TuShare), 0f, 1f);
                break;
            case StatusKind.Venom:
                a.VenomUntil = Math.Max(a.VenomUntil, until);
                a.VenomPerHour = Math.Max(a.VenomPerHour, s.PerHour);
                a.VenomFrom = source;
                break;
            case StatusKind.Root:
                a.RootUntil = Math.Max(a.RootUntil, until);
                break;
        }
    }

    /// <summary>How fast a creature can walk now, as a factor: 0 rooted or stunned, slower chilled.</summary>
    public static float MoveFactor(Simulation sim, Entity e)
    {
        if (!sim.World.TryGet<Components.Afflictions>(e, out var a))
            return 1f;
        var now = sim.Clock.Tick;
        // In a fight a stun costs time units instead (the clock stands still through a phase, so a
        // frozen walker would never arrive); a root keeps it off the move range altogether.
        if (a.Rooted(now) || (a.Stunned(now) && !sim.Combat.Active))
            return 0f;
        return a.Chilled(now) ? 1f - a.Chill : 1f;
    }

    /// <summary>Whether it's held where it stands (rooted): no walking, no taking off.</summary>
    public static bool Rooted(Simulation sim, Entity e) =>
        sim.World.TryGet<Components.Afflictions>(e, out var a) && a.Rooted(sim.Clock.Tick);

    /// <summary>Whether it's reeling from a lightning strike: it can't attack out of combat for a moment.</summary>
    public static bool Stunned(Simulation sim, Entity e) =>
        sim.World.TryGet<Components.Afflictions>(e, out var a) && a.Stunned(sim.Clock.Tick);

    /// <summary>
    /// A unit's time units for a new turn, from its full allowance: fewer while chilled, and a
    /// stun's share taken off once (it's spent here, so it never counts twice).
    /// </summary>
    public static float TurnTimeUnits(Simulation sim, Entity e, float perTurn)
    {
        var w = sim.World;
        if (!w.Has<Components.Afflictions>(e))
            return perTurn;
        ref var a = ref w.Get<Components.Afflictions>(e);
        var tu = a.Chilled(sim.Clock.Tick) ? perTurn * (1f - a.Chill) : perTurn;
        if (a.StunTu > 0)
        {
            tu -= perTurn * a.StunTu;
            a.StunTu = 0;
        }
        return Math.Max(0f, tu);
    }

    /// <summary>What's on a creature now, for the inspector ("chilled, poisoned"), or null.</summary>
    public static string? Describe(Simulation sim, Entity e)
    {
        if (!sim.World.TryGet<Components.Afflictions>(e, out var a))
            return null;
        var now = sim.Clock.Tick;
        var parts = new List<string>();
        if (a.Chilled(now)) parts.Add("chilled");
        if (a.Stunned(now) || a.StunTu > 0) parts.Add("stunned");
        if (a.Poisoned(now)) parts.Add("poisoned");
        if (a.Rooted(now)) parts.Add("rooted");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}

/// <summary>Venom does its damage over time; everything wears off, and the component goes once it has.</summary>
public sealed class AfflictionSystem : ISimSystem
{
    public string Name => "Afflictions";
    public int Interval => 30;

    private readonly List<Entity> _done = [];

    public void Update(Simulation sim)
    {
        var w = sim.World;
        var now = sim.Clock.Tick;
        foreach (var e in w.Query<Components.Afflictions>())
        {
            var a = w.Get<Components.Afflictions>(e);
            if (!w.TryGet<Creature>(e, out var c) || c.Dead)
            {
                _done.Add(e);
                continue;
            }
            if (a.Poisoned(now))
            {
                // Always the torso, so it's one wound that worsens rather than a new one every update.
                var part = w.TryGet<Health>(e, out var h) ? Math.Max(0, h.Body.Parts.FindIndex(p => p.Name == "Torso")) : -1;
                HealthSystem.Damage(sim, e, a.VenomPerHour * Interval / SimClock.TicksPerHour, DamageType.Venom, part);
            }
            if (!w.IsAlive(e) || !w.Has<Components.Afflictions>(e))
                continue;
            ref var left = ref w.Get<Components.Afflictions>(e);
            if (left.StunTu > 0 && !left.Stunned(now) && !sim.Combat.Active)
                left.StunTu = 0; // out of a fight there's no next turn for a stun to cost
            if (left.Over(now))
                _done.Add(e);
        }
        foreach (var e in _done)
            if (w.IsAlive(e))
                w.Remove<Components.Afflictions>(e);
        _done.Clear();
    }
}
