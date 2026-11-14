using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Combat;

/// <summary>
/// How an AI fights with free movement on (<see cref="FreeMoveSystem"/>): it joins free movement
/// for the fight (<see cref="Join"/>) and steers each tick (<see cref="Steer"/>). It closes to its
/// weapon's reach and keeps it, holds a little way off circling while it waits its turn
/// (<see cref="Engagement"/>), stands clear of anyone else near, and now and then steps back after a
/// blow. An archer keeps to its bow's best range and backs off from anyone who closes, and leads a
/// moving target. Far off, or with something in the way, it walks the path there instead. When the
/// fight's over it's put back on its cell and walks the grid again (<see cref="Leave"/>).
/// </summary>
public static class FreeFight
{
    /// <summary>How far off one waiting its turn holds, and how hard it circles meanwhile (share of its speed).</summary>
    public const float HoldDistance = 2.6f, Circle = 0.55f;

    /// <summary>Beyond this (cells), or with no clear line, it walks the path rather than steering straight.</summary>
    public const float SteerWithin = 3.5f;

    /// <summary>Anyone nearer than this is pushed off from, so a band doesn't pile up.</summary>
    public const float Personal = 0.9f;

    /// <summary>Of blows struck, the share after which it steps back for a moment.</summary>
    public const float BackOffShare = 0.35f;
    public const int BackOffTicks = SimClock.TicksPerSecond / 2;

    /// <summary>An archer backs away from anyone nearer than this.</summary>
    public const float ArcherTooClose = 3f;

    /// <summary>An arrow's flight, cells per tick, for leading a moving target.</summary>
    public const float ArrowSpeed = 0.5f;

    /// <summary>An AI starts fighting freely: off the grid till <see cref="Leave"/>, weapon out.</summary>
    public static void Join(Simulation sim, Entity e)
    {
        var w = sim.World;
        if (w.Has<FreeMover>(e))
            return;
        w.Get<Mover>(e).Stop();
        w.Add(e, new FreeMover { Drawn = true, Fighting = true });
    }

    /// <summary>
    /// The fight's over for an AI: back on the grid, in the middle of the cell it stands in. A
    /// player's character keeps moving freely.
    /// </summary>
    public static void Leave(Simulation sim, Entity e)
    {
        var w = sim.World;
        if (!w.TryGet<FreeMover>(e, out var m) || !m.Fighting || w.Has<Avatar>(e))
            return;
        w.Remove<FreeMover>(e);
        w.Get<Mover>(e).Stop();
        var cell = w.Get<Position>(e).Cell;
        if (sim.Map.Passable(cell))
            w.Get<Position>(e) = Position.At(cell);
    }

    /// <summary>
    /// One tick of an AI's footwork against <paramref name="target"/>: where it wants to go and
    /// which way it faces. <paramref name="pressing"/>: it's its turn (else it holds off and
    /// circles). Returns the distance between them.
    /// </summary>
    public static float Steer(Simulation sim, Entity e, Entity target, AttackDef attack, bool pressing, long backOffUntil)
    {
        var w = sim.World;
        ref var m = ref w.Get<FreeMover>(e);
        var me = w.Get<Position>(e);
        var them = w.Get<Position>(target);
        var aimAt = new float2(them.Pos.X, them.Pos.Y);
        var d = aimAt - new float2(me.Pos.X, me.Pos.Y);
        var dist = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        if (attack.Ranged && w.TryGet<FreeMover>(target, out var tm))
            aimAt += tm.Vel * (dist / ArrowSpeed); // lead them
        var toward = dist > 1e-3f ? d * (1f / dist) : new float2(1, 0);
        var aim = DetMath.Atan2(aimAt.Y - me.Pos.Y, aimAt.X - me.Pos.X);
        var mover = w.Get<Mover>(e);
        // Mid-swing it commits: the blow's aimed, and it stands.
        if (w.Has<Windup>(e))
        {
            m.Wish = float2.Zero;
            return dist;
        }
        m.Aim = aim;
        // Far off, a level apart, or something in the way: the path, as walking would.
        var clear = them.Cell.Z == me.Cell.Z
            && CombatRules.LineOfFire(sim, me.Pos + new float3(0, 0, 0.5f), them.Pos + new float3(0, 0, 0.5f));
        // A bow steers within its range; a blade only once close (else it walks the way there).
        var steerWithin = attack.Ranged ? Math.Max(SteerWithin, attack.Range) : SteerWithin;
        if (dist > steerWithin || !clear)
        {
            m.Wish = float2.Zero;
            // A fresh path when there's none, or they've moved off the end of the last.
            if (!mover.Moving || Maps.Map.Distance(mover.Path![^1], them.Cell) > 1)
            {
                if (Ai.Navigation.PlanRoute(sim, e, me.Cell, them.Cell, PathEnd.Touch) is { } path)
                {
                    mover.Path = path;
                    mover.PathIndex = 0;
                    mover.StepProgress = 0;
                    mover.StepFrom = me.Cell;
                }
            }
            return dist;
        }
        mover.Stop();
        float want;
        if (attack.Ranged)
            want = dist < ArcherTooClose ? ArcherTooClose + 1f : Math.Max(2f, attack.OptimalRange);
        else if (!pressing)
            want = HoldDistance;
        else
            want = Reach.ShapeOf(sim, e, attack).Length * 0.8f;
        if (sim.Clock.Tick < backOffUntil)
            want = Math.Max(want, HoldDistance);
        var radial = Math.Clamp((dist - want) * 2f, -1f, 1f);
        var wish = toward * radial;
        // Waiting its turn (or keeping a bow's distance), it circles: one way or the other by who it is.
        if (!pressing || attack.Ranged)
        {
            var side = (e.Index & 1) == 0 ? 1f : -1f;
            wish += new float2(-toward.Y, toward.X) * (Circle * side * (attack.Ranged ? 0.5f : 1f));
        }
        // Room: away from anyone else close by.
        foreach (var other in sim.Creatures())
        {
            if (other == e || other == target || w.Get<Creature>(other).Dead)
                continue;
            var op = w.Get<Position>(other);
            if (op.Cell.Z != me.Cell.Z)
                continue;
            var away = new float2(me.Pos.X - op.Pos.X, me.Pos.Y - op.Pos.Y);
            var gap = MathF.Sqrt(away.X * away.X + away.Y * away.Y);
            if (gap < Personal && gap > 1e-3f)
                wish += away * ((Personal - gap) / (gap * Personal));
        }
        var len = MathF.Sqrt(wish.X * wish.X + wish.Y * wish.Y);
        m.Wish = len > 1f ? wish * (1f / len) : len < 0.08f ? float2.Zero : wish;
        return dist;
    }

    /// <summary>Is <paramref name="target"/> where a blow from <paramref name="e"/> aimed at it now would reach?</summary>
    public static bool InReach(Simulation sim, Entity e, Entity target, AttackDef attack, float dist)
    {
        if (attack.Ranged)
            return dist <= attack.Range && CombatRules.LineOfFire(sim, sim.World.Get<Position>(e).Pos + new float3(0, 0, 0.7f),
                sim.World.Get<Position>(target).Pos + new float3(0, 0, 0.5f));
        return dist - Reach.BodyRadius <= Reach.ShapeOf(sim, e, attack).Length * 0.9f
            && Math.Abs(sim.World.Get<Position>(e).Cell.Z - sim.World.Get<Position>(target).Cell.Z) <= 1;
    }
}
