using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Combat;

/// <summary>
/// How an AI fights with free movement on (<see cref="FreeMoveSystem"/>): it joins free movement
/// for the fight (<see cref="Join"/>) and steers each tick (<see cref="Steer"/>). It closes to its
/// weapon's reach and keeps it, holds a little way off circling while it waits its turn
/// (<see cref="Engagement"/>), stands clear of anyone else near, and now and then steps back after a
/// blow, and steps round a friend in its blow's way. Hurt (<see cref="Wary"/>), it gives its turn
/// to a fresher friend and hangs back; broken, it backs off facing the foe (<see cref="BackAway"/>)
/// before it turns and runs. Something big (the dragon) holds further off. An archer keeps to its
/// bow's best range and backs off from anyone who closes, and leads a moving target. Far off, or with something in the way, it walks the path there instead. When the
/// fight's over it's put back on its cell and walks the grid again (<see cref="Leave"/>).
/// </summary>
public static class FreeFight
{
    /// <summary>How far off one waiting its turn holds, and how hard it circles meanwhile (share of its speed).</summary>
    public const float HoldDistance = 2.6f, Circle = 0.55f;

    /// <summary>How hard it steps sideways round a friend in its blow's way (share of its speed).</summary>
    public const float Sidestep = 0.9f;

    /// <summary>Beyond this (cells), or with no clear line, it walks the path rather than steering straight.</summary>
    public const float SteerWithin = 3.5f;

    /// <summary>Anyone nearer than this is pushed off from, so a band doesn't pile up.</summary>
    public const float Personal = 0.9f;

    /// <summary>Of blows struck, the share after which it steps back for a moment.</summary>
    public const float BackOffShare = 0.35f;
    public const int BackOffTicks = SimClock.TicksPerSecond / 2;

    /// <summary>
    /// Hurt past this pain (or moving below <see cref="WaryMoving"/>), a fighter is wary: it gives its
    /// turn to a fresher friend and hangs back at <see cref="WaryHold"/>. Short of breaking
    /// (<c>RaiderBrain.WantsToFlee</c>), so a fight winds down rather than ending all at once.
    /// </summary>
    public const float WaryPain = 0.3f, WaryMoving = 0.7f, WaryHold = 3.5f;

    /// <summary>Broken, how long it backs off facing the foe (<see cref="BackAway"/>) before it turns to run.</summary>
    public const int WithdrawTicks = SimClock.TicksPerSecond * 3 / 2;

    /// <summary>An archer backs away from anyone nearer than this.</summary>
    public const float ArcherTooClose = 3f;

    /// <summary>An arrow's flight, cells per tick, for leading a moving target.</summary>
    public const float ArrowSpeed = 0.5f;

    /// <summary>Hurt enough to hang back, short of running (<see cref="WaryPain"/>).</summary>
    public static bool Wary(Simulation sim, Entity e) =>
        sim.World.TryGet<Health>(e, out var h) && (h.Pain > WaryPain || h.Capacity(Capacity.Moving) < WaryMoving);

    /// <summary>How far off it holds while waiting its turn: further for something big (a dragon's half again its size).</summary>
    public static float HoldFor(Simulation sim, Entity e) =>
        HoldDistance + 0.5f * Math.Max(0f, sim.World.Get<Creature>(e).Stage(sim.Clock.Tick).Size - 1f);

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
    /// circles). Returns the distance between them. <paramref name="quitsBeyond"/>: further than this
    /// the caller gives up and its job ends this tick (<see cref="Ai.AssaultJob"/>), which drops any
    /// path planned here (<see cref="Leave"/>), so none is searched for.
    /// </summary>
    public static float Steer(Simulation sim, Entity e, Entity target, AttackDef attack, bool pressing, long backOffUntil,
        float quitsBeyond = float.PositiveInfinity)
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
                // About to give up on them: a raider too far off ran a whole-town search every tick to
                // throw it away. What's left of a dropped path is where it stepped from, set when a walker
                // finds a way, which it does exactly when the regions say there is one (FindPath's own test).
                if (dist > quitsBeyond && !w.Has<Ai.Rampage>(e) && Ai.Navigation.FlyingStage(sim, e) == null)
                {
                    if (sim.Regions.CanReach(me.Cell, them.Cell, PathEnd.Touch))
                        mover.StepFrom = me.Cell;
                    return dist;
                }
                // A dragon down on the town (Ai.Rampage) chases on foot through the streets, never
                // through a door or a house: no such way, and it doesn't come.
                List<int3>? path;
                if (w.Has<Ai.Rampage>(e))
                {
                    var asked = (me.Cell, them.Cell, sim.Map.StructureVersion);
                    if (sim.NoWayOnFoot.TryGetValue(e, out var failed) && failed == asked)
                        path = null; // the same search as last time, on the same map: no way still
                    else
                    {
                        path = sim.Pathfinder.FindPath(me.Cell, them.Cell, PathEnd.Touch) is { } walked && !Ai.DragonBrain.Squeezes(sim, walked) ? walked : null;
                        if (path == null)
                            sim.NoWayOnFoot[e] = asked;
                        else
                            sim.NoWayOnFoot.Remove(e);
                    }
                }
                else
                    path = Ai.Navigation.PlanRoute(sim, e, me.Cell, them.Cell, PathEnd.Touch);
                if (path != null)
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
            want = Wary(sim, e) ? Math.Max(WaryHold, HoldFor(sim, e)) : HoldFor(sim, e);
        else
            want = Reach.ShapeOf(sim, e, attack).Length * 0.8f;
        if (sim.Clock.Tick < backOffUntil)
            want = Math.Max(want, HoldFor(sim, e));
        var radial = Math.Clamp((dist - want) * 2f, -1f, 1f);
        var wish = toward * radial;
        // A friend in the blow's way (Blows.Begin holds it): step round them, to the far side from them.
        var side2 = new float2(-toward.Y, toward.X);
        if (pressing && Reach.FirstFriendInTheWay(sim, e, attack, aim, target) is { IsNull: false } friend)
        {
            var fp = w.Get<Position>(friend).Pos;
            var rel = new float2(fp.X - me.Pos.X, fp.Y - me.Pos.Y);
            var friendSide = toward.X * rel.Y - toward.Y * rel.X >= 0 ? 1f : -1f;
            wish += side2 * (-friendSide * Sidestep);
        }
        // Waiting its turn (or keeping a bow's distance), it circles: one way or the other by who it is.
        else if (!pressing || attack.Ranged)
        {
            var side = (e.Index & 1) == 0 ? 1f : -1f;
            wish += side2 * (Circle * side * (attack.Ranged ? 0.5f : 1f));
        }
        wish += Room(sim, e, target, me);
        var len = MathF.Sqrt(wish.X * wish.X + wish.Y * wish.Y);
        m.Wish = len > 1f ? wish * (1f / len) : len < 0.08f ? float2.Zero : wish;
        return dist;
    }

    /// <summary>
    /// One tick of a broken fighter's withdrawal: stepping back from <paramref name="foe"/> while
    /// still facing it (its guard toward the blows), rather than turning its back at arm's length.
    /// Mid-swing it stands. Returns the distance between them.
    /// </summary>
    public static float BackAway(Simulation sim, Entity e, Entity foe)
    {
        var w = sim.World;
        ref var m = ref w.Get<FreeMover>(e);
        var me = w.Get<Position>(e);
        var them = w.Get<Position>(foe).Pos;
        var d = new float2(them.X - me.Pos.X, them.Y - me.Pos.Y);
        var dist = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        if (w.Has<Windup>(e))
        {
            m.Wish = float2.Zero;
            return dist;
        }
        w.Get<Mover>(e).Stop();
        m.Aim = DetMath.Atan2(d.Y, d.X);
        var wish = (dist > 1e-3f ? d * (-1f / dist) : new float2(-1, 0)) + Room(sim, e, foe, me);
        var len = MathF.Sqrt(wish.X * wish.X + wish.Y * wish.Y);
        m.Wish = len > 1e-3f ? wish * (1f / len) : float2.Zero;
        return dist;
    }

    /// <summary>Room: a push away from anyone else close by (not <paramref name="target"/>), so a band doesn't pile up.</summary>
    private static float2 Room(Simulation sim, Entity e, Entity target, Position me)
    {
        var w = sim.World;
        var wish = float2.Zero;
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
        return wish;
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
