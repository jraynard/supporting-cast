using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Combat;

/// <summary>
/// A player's character moving freely rather than cell to cell (<see cref="FreeMove"/>): where
/// they're heading (<see cref="Wish"/>, from the keys), how fast they're going, which way they face
/// (<see cref="Aim"/>, from the mouse: radians, 0 along +x, turning toward +y), and a dodge's dash.
/// </summary>
public struct FreeMover
{
    /// <summary>Wanted direction on the map, length 0..1.</summary>
    public float2 Wish;

    /// <summary>Cells per tick.</summary>
    public float2 Vel;

    public float Aim;

    /// <summary>A dodge's dash: its direction (unit) until <see cref="DashUntil"/>.</summary>
    public float2 DashDir;
    public long DashUntil;

    /// <summary>An AI in a fight (<see cref="FreeFight"/>), not a player's character: it goes back to the grid when the fight's done.</summary>
    public bool Fighting;

    /// <summary>
    /// Weapon drawn: in combat mode. The mouse fights (light and heavy blows, the way you aim) and you
    /// go a little slower; sheathed, clicks walk and act as ever (<see cref="DrawWeaponCommand"/>).
    /// </summary>
    public bool Drawn;
}

/// <summary>
/// How players move: freely, not cell to cell. A free mover's position is continuous: it eases
/// toward the speed its keys ask for, slides along walls and fences (a small circle against the
/// cells it may stand in), changes level where walking would, and its cell is the one under its
/// feet; a job's walk follows its path. A dodge is a short dash. Every player's character is one;
/// an AI is while it fights (<see cref="FreeFight"/>).
/// </summary>
public sealed class FreeMoveSystem : ISimSystem, ISaveState
{
    public string Name => "FreeMove";
    public int Interval => 1;

    /// <summary>How wide a free mover is: the circle kept clear of walls.</summary>
    public const float Radius = 0.28f;

    /// <summary>Share of the way to the wanted speed covered each tick: quick to start and stop, not instant.</summary>
    public const float Ease = 0.25f;

    /// <summary>Cells per tick below which someone easing to a stop has stopped.</summary>
    public const float StopBelow = 0.002f;

    /// <summary>A dodge's dash: how far, and over how long.</summary>
    public const float DashCells = 2.2f;
    public const int DashTicks = SimClock.TicksPerSecond * 3 / 10;

    /// <summary>Slower while swinging and while guarding, and a little with a weapon drawn.</summary>
    public const float SwingSlow = 0.45f, GuardSlow = 0.6f, DrawnSlow = 0.85f;

    /// <summary>A player's character's free movement, joined now if it hasn't been yet (a command can come before the first tick). False if it isn't a player's.</summary>
    public static bool Ensure(Simulation sim, Entity e)
    {
        var w = sim.World;
        if (!w.IsAlive(e) || !w.Has<Avatar>(e))
            return false;
        if (!w.Has<FreeMover>(e))
            Join(sim, e);
        return true;
    }

    /// <summary>A player's character starts moving freely, facing the way it faced.</summary>
    public static void Join(Simulation sim, Entity e)
    {
        var w = sim.World;
        if (w.Has<FreeMover>(e))
            return;
        var f = w.Get<Avatar>(e).Facing;
        w.Get<Mover>(e).Stop();
        w.Add(e, new FreeMover { Aim = f == int2.Zero ? 0f : DetMath.Atan2(f.Y, f.X) });
    }

    public void Update(Simulation sim)
    {
        var w = sim.World;
        // Whoever's a player's character moves freely; one handed back to the AI walks the grid again.
        foreach (var e in w.Query<Avatar>())
            if (!w.Has<FreeMover>(e))
                Join(sim, e);
        foreach (var e in w.Query<FreeMover>())
            if (!w.Has<Avatar>(e) && !w.Get<FreeMover>(e).Fighting)
                w.Remove<FreeMover>(e);
        foreach (var e in w.Each<FreeMover>())
            Step(sim, e);
    }

    private static void Step(Simulation sim, Entity e)
    {
        var w = sim.World;
        ref var m = ref w.Get<FreeMover>(e);
        var c = w.Get<Creature>(e);
        var now = sim.Clock.Tick;
        if (w.Has<Carried>(e))
            return; // MovementSystem keeps them with whoever's carrying them
        // A job's walk (a clicked walk, the way to a station): along its cells, as the pathfinder
        // planned it, up stairs and ladders, through doors, onto furniture.
        var mover = w.Get<Mover>(e);
        if (m.Wish == float2.Zero && now >= m.DashUntil && mover.Moving && c.Able)
        {
            FollowPath(sim, e, mover);
            m.Vel = float2.Zero;
            return;
        }
        var dashing = now < m.DashUntil;
        var want = Wanted(sim, e, m.Wish, m.Drawn, dashing ? m.DashDir : null);
        m.Vel = dashing ? want : m.Vel + (want - m.Vel) * Ease;
        // Easing never quite gets there: near enough standing still is standing still.
        // Weapon sheathed, you face the way you walk; drawn, the way you aim.
        if (!m.Drawn && now >= m.DashUntil && m.Wish != float2.Zero)
            m.Aim = DetMath.Atan2(m.Wish.Y, m.Wish.X);
        if (want == float2.Zero && m.Vel.X * m.Vel.X + m.Vel.Y * m.Vel.Y < StopBelow * StopBelow)
        {
            m.Vel = float2.Zero;
            return;
        }
        ref var p = ref w.Get<Position>(e);
        var (pos, cell, vel) = Slide(sim, p.Pos, p.Cell, m.Vel, w.TryGet<Perched>(e, out var up) ? up.Cell : null);
        m.Vel = vel;
        if (cell != p.Cell && w.TryGet<Perched>(e, out var perch) && perch.Cell != cell)
            w.Remove<Perched>(e); // stepped down off the furniture
        p.Pos = pos;
        p.Cell = cell;
        // F's "in front" is the way you face, to the nearest of eight.
        var (cx, cy) = (DetMath.Cos(m.Aim), DetMath.Sin(m.Aim));
        if (w.Has<Avatar>(e))
            w.Get<Avatar>(e).Facing = new int2(cx > 0.38f ? 1 : cx < -0.38f ? -1 : 0, cy > 0.38f ? 1 : cy < -0.38f ? -1 : 0);
    }

    /// <summary>
    /// The velocity (cells per tick) a free mover's keys ask for now: its walking speed, slower
    /// swinging, guarding or with a weapon drawn; nothing while it's down, asleep or at work; a dash's
    /// speed along <paramref name="dash"/>. Shared with co-op's prediction (it reads the sim, writes nothing).
    /// </summary>
    public static float2 Wanted(Simulation sim, Entity e, float2 wish, bool drawn, float2? dash)
    {
        var w = sim.World;
        var c = w.Get<Creature>(e);
        if (!c.Able || w.Get<Mind>(e).Asleep || w.Has<Work.WorkSession>(e))
            return float2.Zero;
        if (dash is { } d)
            return d * (DashCells / DashTicks);
        var speed = MovementSystem.WalkSpeedPerTick(sim, e);
        if (w.Has<Windup>(e))
            speed *= SwingSlow;
        else if ((w.TryGet<Guard>(e, out var g) && g.RaisedAt >= 0) || w.Has<Aiming>(e))
            speed *= GuardSlow; // guarding, or a bow drawn
        else if (drawn)
            speed *= DrawnSlow;
        return wish * speed;
    }

    /// <summary>
    /// One tick's move by <paramref name="vel"/> from <paramref name="pos"/>: each axis on its own,
    /// so a wall stops one and you slide along the other; levels change as the walking links allow
    /// (<see cref="Into"/>). Up on furniture (<paramref name="perch"/>), that cell's somewhere to
    /// stand. Returns where it ends up and what's left of the velocity. Pure.
    /// </summary>
    public static (float3 Pos, int3 Cell, float2 Vel) Slide(Simulation sim, float3 pos, int3 cell, float2 vel, int3? perch = null)
    {
        // Across and then along, or along and then across: whichever gets further (onto a stair
        // met at an angle, through a doorway), across first when it's a tie.
        var a = SlideAxes(sim, pos, cell, vel, perch, xFirst: true);
        if (a.Vel == vel)
            return a;
        var b = SlideAxes(sim, pos, cell, vel, perch, xFirst: false);
        return Travel(b.Pos, pos) > Travel(a.Pos, pos) + 1e-5f || (b.Cell.Z != cell.Z && a.Cell.Z == cell.Z) ? b : a;

        static float Travel(float3 to, float3 from) => (to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y);
    }

    private static (float3 Pos, int3 Cell, float2 Vel) SlideAxes(Simulation sim, float3 pos, int3 cell, float2 vel, int3? perch, bool xFirst)
    {
        var at = new float2(pos.X, pos.Y);
        for (var axis = 0; axis < 2; axis++)
        {
            var alongX = (axis == 0) == xFirst;
            var to = alongX ? new float2(at.X + vel.X, at.Y) : new float2(at.X, at.Y + vel.Y);
            if (Into(sim, cell, to, perch) is { } into)
                (at, cell) = (to, into);
            else
                vel = alongX ? new float2(0, vel.Y) : new float2(vel.X, 0);
        }
        return (new float3(at.X, at.Y, cell.Z), cell, vel);
    }

    /// <summary>How far (cells) a player's claimed position may be from where the sim has them.</summary>
    public const float MaxClaim = 2.5f;

    /// <summary>
    /// Co-op: a player says where they are (<see cref="Core.FreeMoveCommand"/>'s At, from their own
    /// screen, where their walk is predicted). Taken if it's near where the sim has them, on their level
    /// or one off, and somewhere they could stand; else ignored. <paramref name="stopped"/> also stops them dead there.
    /// </summary>
    public static void Claim(Simulation sim, Entity e, float x, float y, int z, bool stopped)
    {
        var w = sim.World;
        if (!float.IsFinite(x) || !float.IsFinite(y) || !w.Has<FreeMover>(e) || w.Get<Mover>(e).Moving || w.Has<Carried>(e))
            return;
        ref var p = ref w.Get<Position>(e);
        var dx = x - p.Pos.X;
        var dy = y - p.Pos.Y;
        if (dx * dx + dy * dy > MaxClaim * MaxClaim || Math.Abs(z - p.Cell.Z) > 1 || !w.Get<Creature>(e).Able)
            return;
        var cell = new int3((int)MathF.Floor(x), (int)MathF.Floor(y), z);
        if (!sim.Map.Passable(cell) || !Fits(sim, new float2(x, y), z) || !Walkable(sim, p.Cell, cell))
            return;
        if (cell != p.Cell && w.TryGet<Perched>(e, out var perch) && perch.Cell != cell)
            w.Remove<Perched>(e);
        p.Pos = new float3(x, y, z);
        p.Cell = cell;
        if (stopped)
            w.Get<FreeMover>(e).Vel = float2.Zero;
    }

    /// <summary>
    /// Can <paramref name="to"/> be walked to from <paramref name="from"/> in a few steps by the walking
    /// links (<see cref="NavGraph"/>)? A claim within <see cref="MaxClaim"/> may have rounded a corner,
    /// but never dropped off a ledge or a bridge's side.
    /// </summary>
    private static bool Walkable(Simulation sim, int3 from, int3 to)
    {
        if (from == to)
            return true;
        var map = sim.Map;
        if (!map.InBounds(from) || !map.InBounds(to))
            return false;
        var goal = map.Idx(to);
        Span<int> seen = stackalloc int[256]; // every cell three steps out, on three levels
        seen[0] = map.Idx(from);
        int count = 1, start = 0;
        Span<NavGraph.Step> steps = stackalloc NavGraph.Step[NavGraph.MaxSteps];
        for (var depth = 0; depth < 3; depth++)
        {
            var end = count;
            for (var i = start; i < end; i++)
            {
                var n = NavGraph.Neighbors(new Maps.MapNav(map), seen[i], steps);
                for (var k = 0; k < n; k++)
                {
                    var next = steps[k].Index;
                    if (next == goal)
                        return true;
                    if (count < seen.Length && !seen[..count].Contains(next))
                        seen[count++] = next;
                }
            }
            start = end;
        }
        return false;
    }

    /// <summary>Could a free mover stand with its middle at <paramref name="at"/>: every cell its circle touches passable (on the level, or a step up or down)?</summary>
    public static bool Fits(Simulation sim, float2 at, int z, int3? perch = null)
    {
        var map = sim.Map;
        for (var dy = -1; dy <= 1; dy += 2)
        for (var dx = -1; dx <= 1; dx += 2)
        {
            var cell = new int3((int)MathF.Floor(at.X + dx * Radius), (int)MathF.Floor(at.Y + dy * Radius), z);
            if (cell == perch)
                continue;
            if (!map.Passable(cell) && !map.Passable(cell with { Z = z + 1 }) && !map.Passable(cell with { Z = z - 1 }))
                return false;
        }
        return true;
    }

    /// <summary>
    /// The cell a free mover in <paramref name="from"/> stands in with its middle moved to
    /// <paramref name="at"/>, or null if it can't go there. Into a neighbouring cell only as the
    /// walking links allow (<see cref="NavGraph"/>: on the level, else up or down a stair or slope),
    /// so levels change just where walking would change them, and a drop or a wall stops you. Where
    /// both levels are open, you stay on yours, except stepping off the top of a stair the way it
    /// climbs (<see cref="Map.ClimbOf"/>): that's the floor above, not the room under it.
    /// </summary>
    public static int3? Into(Simulation sim, int3 from, float2 at, int3? perch = null)
    {
        var to = new int3((int)MathF.Floor(at.X), (int)MathF.Floor(at.Y), from.Z);
        if (to.X == from.X && to.Y == from.Y)
            return Fits(sim, at, from.Z, perch) ? from : null;
        var map = sim.Map;
        if (!map.InBounds(from))
            return null;
        var upward = map.Climb[map.Idx(from)] == ClimbKind.Ramp && map.ClimbOf(from.X, from.Y, from.Z) == new int2(to.X - from.X, to.Y - from.Y);
        Span<NavGraph.Step> steps = stackalloc NavGraph.Step[NavGraph.MaxSteps];
        var count = NavGraph.Neighbors(new Maps.MapNav(map), map.Idx(from), steps);
        int3? found = null;
        for (var k = 0; k < count; k++)
        {
            var c = map.CellOf(steps[k].Index);
            if (c.X != to.X || c.Y != to.Y)
                continue;
            if (upward ? c.Z == from.Z + 1 : c.Z == from.Z)
            {
                found = c;
                break;
            }
            found ??= c;
        }
        return found is { } next && Fits(sim, at, next.Z, perch) ? next : null;
    }

    /// <summary>Walk a job's path, cell middle to cell middle, at walking pace.</summary>
    private static void FollowPath(Simulation sim, Entity e, Mover mover)
    {
        var w = sim.World;
        ref var p = ref w.Get<Position>(e);
        var next = mover.Path![mover.PathIndex];
        var goal = Position.CenterOf(next);
        var d = goal - p.Pos;
        var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
        var speed = MovementSystem.WalkSpeedPerTick(sim, e);
        mover.StepFrom = p.Cell;
        if (len <= speed)
        {
            p.Pos = goal;
            if (w.TryGet<Perched>(e, out var perch) && perch.Cell != next)
                w.Remove<Perched>(e);
            p.Cell = next;
            mover.PathIndex++;
            mover.StepProgress = 0;
            mover.StepFrom = next;
            return;
        }
        p.Pos += d * (speed / len);
        mover.StepProgress = Math.Clamp(1f - len, 0f, 0.99f);
        // The cell under your feet: past halfway, the next one.
        if (MathF.Abs(p.Pos.X - goal.X) < 0.5f && MathF.Abs(p.Pos.Y - goal.Y) < 0.5f)
            p.Cell = next;
    }

    /// <summary>A dodge's dash along <paramref name="dir"/> (or straight back from the aim, standing still), from <paramref name="at"/>.</summary>
    public static void Dash(Simulation sim, Entity e, float2 dir, long at)
    {
        var w = sim.World;
        if (!w.Has<FreeMover>(e))
            return;
        ref var m = ref w.Get<FreeMover>(e);
        var len = MathF.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
        m.DashDir = len > 0.1f ? dir * (1f / len) : new float2(-DetMath.Cos(m.Aim), -DetMath.Sin(m.Aim));
        m.DashUntil = at + DashTicks;
    }

    // Saves from when it was a switch kept whether it was on; it always is now.
    public void Save(SaveWriter w, Simulation sim) => w.Bool(true);

    public void Load(SaveReader r, Simulation sim) => r.Bool();
}
