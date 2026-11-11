using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;

namespace SupportingCast.Sim.Combat;

/// <summary>
/// How a ranged attack is made: a quick snap shot or a careful aimed one (X-COM style). Melee
/// blows and fire breath are <see cref="Normal"/>. Reactions are always snap shots.
/// </summary>
public enum ShotMode
{
    Normal,
    Snap,
    Aimed,
}

/// <summary>
/// Combat's action economy (old X-COM / Xenonauts): every unit has a pool of time units (TU) each
/// turn (<see cref="CreatureDef.TimeUnits"/>), spent on walking (per cell, by speed, terrain and
/// leg wounds) and on actions (a share of the pool each), in any order, as often as they last.
/// What's left at the end of the turn stays for reactions in the enemy's turn (<see cref="Reactions"/>).
/// </summary>
public static class TimeUnits
{
    /// <summary>TU per cell walked at the reference speed (a healthy human), on open ground. Diagonals cost √2 as much.</summary>
    public const float PerCell = 4f;

    /// <summary>Speed (cells per game second) that walks at <see cref="PerCell"/>; faster creatures pay less per cell.</summary>
    public const float ReferenceSpeed = 4.5f;

    /// <summary>Action costs, as shares of the unit's TU per turn.</summary>
    public const float Melee = 0.35f, Snap = 0.3f, Aimed = 0.55f, Breath = 0.5f, PickUp = 0.15f, Interact = 0.25f, Drag = 0.2f, Bandage = 0.3f;

    /// <summary>Letting go of what's in hand (it falls at your feet), and drawing another weapon from the pack.</summary>
    public const float Drop = 0.05f, Wield = 0.2f;

    /// <summary>Accuracy multipliers of the shot modes.</summary>
    public const float SnapAccuracy = 0.85f, AimedAccuracy = 1.15f;

    /// <summary>A unit's TU for a whole turn.</summary>
    public static float PerTurn(Simulation sim, Entity e) => sim.World.Get<Creature>(e).Def.TimeUnits;

    /// <summary>A share of the unit's TU, rounded up to whole TU.</summary>
    public static float Share(Simulation sim, Entity e, float share) => MathF.Ceiling(PerTurn(sim, e) * share);

    public static float AttackCost(Simulation sim, Entity e, AttackDef attack, ShotMode mode) =>
        Share(sim, e, !attack.Ranged ? Melee : mode == ShotMode.Aimed ? Aimed : Snap);

    public static float BreathCost(Simulation sim, Entity e) => Share(sim, e, Breath);

    /// <summary>The mode an attack is made in by default: melee is Normal; ranged, Snap (what a reaction uses too).</summary>
    public static ShotMode DefaultMode(AttackDef attack) => attack.Ranged ? ShotMode.Snap : ShotMode.Normal;

    public static float Accuracy(ShotMode mode) => mode switch
    {
        ShotMode.Snap => SnapAccuracy,
        ShotMode.Aimed => AimedAccuracy,
        _ => 1f,
    };

    /// <summary>
    /// TU per cell walked (before terrain): faster creatures pay less, leg wounds more. Infinite
    /// for a rider (their mount does the walking) or someone who can't walk.
    /// </summary>
    public static float WalkCost(Simulation sim, Entity e)
    {
        if (sim.World.Has<Riding>(e))
            return float.PositiveInfinity;
        var c = sim.World.Get<Creature>(e);
        var moving = sim.World.TryGet<Health>(e, out var h) ? h.Capacity(Capacity.Moving) : 1f;
        var speed = c.Def.MoveSpeed * c.Stage(sim.Clock.Tick).SpeedFactor * moving;
        // Heavy armor (steel plate) weighs on every step.
        var load = 1f;
        foreach (var worn in sim.Worn(e))
            load += sim.World.Get<Thing>(worn).Def.Apparel?.Encumbrance ?? 0f;
        // Breath can hold a unit where it stands (thorns) or slow every step (frost).
        if (sim.World.TryGet<Components.Afflictions>(e, out var a))
        {
            if (a.Rooted(sim.Clock.Tick))
                return float.PositiveInfinity;
            if (a.Chilled(sim.Clock.Tick))
                speed *= 1f - a.Chill;
        }
        return speed < 0.05f ? float.PositiveInfinity : PerCell * ReferenceSpeed / speed * load;
    }

    /// <summary>The cheapest attack (in its default mode) the unit could make, or null if it has none.</summary>
    public static float? CheapestAttack(Simulation sim, Entity e)
    {
        float? best = null;
        foreach (var a in CombatRules.Attacks(sim, e))
        {
            var cost = AttackCost(sim, e, a, DefaultMode(a));
            if (best == null || cost < best)
                best = cost;
        }
        return best;
    }
}
