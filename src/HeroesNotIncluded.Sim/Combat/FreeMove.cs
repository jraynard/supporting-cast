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
/// Free movement, the prototype (<c>--arena</c> turns it on; the village still walks cell to
/// cell). A free mover's position is continuous: it eases toward the speed its keys ask for, slides
/// along walls and fences (a small circle against the cells it may stand in, on its own level), and
/// its cell is the one under its feet. A dodge is a short dash. The switch is on the sim
/// (<see cref="Enabled"/>, saved): with it on, blows land by where they reach and when
/// (<see cref="Blows"/>), not by a roll.
/// </summary>
public sealed class FreeMoveSystem : ISimSystem, ISaveState
{
    public string Name => "FreeMove";
    public int Interval => 1;

    /// <summary>Free movement and blows by reach and timing, for every player's character.</summary>
    public bool Enabled { get; private set; }

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

    /// <summary>Turn it on (the arena) and make every player's character a free mover.</summary>
    public void Enable(Simulation sim)
    {
        Enabled = true;
        foreach (var e in sim.World.Query<Avatar>())
            Join(sim, e);
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
        if (!Enabled)
            return;
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
        var (pos, cell, vel) = Slide(sim, p.Pos, p.Cell, m.Vel);
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
        else if (w.TryGet<Guard>(e, out var g) && g.RaisedAt >= 0)
            speed *= GuardSlow;
        else if (drawn)
            speed *= DrawnSlow;
        return wish * speed;
    }

    /// <summary>
    /// One tick's move by <paramref name="vel"/> from <paramref name="pos"/>: each axis on its own,
    /// so a wall stops one and you slide along the other; levels change as the walking links allow
    /// (<see cref="Into"/>). Returns where it ends up and what's left of the velocity. Pure.
    /// </summary>
    public static (float3 Pos, int3 Cell, float2 Vel) Slide(Simulation sim, float3 pos, int3 cell, float2 vel)
    {
        var at = new float2(pos.X, pos.Y);
        if (Into(sim, cell, new float2(at.X + vel.X, at.Y)) is { } intoX)
            (at, cell) = (new float2(at.X + vel.X, at.Y), intoX);
        else
            vel = new float2(0, vel.Y);
        if (Into(sim, cell, new float2(at.X, at.Y + vel.Y)) is { } intoY)
            (at, cell) = (new float2(at.X, at.Y + vel.Y), intoY);
        else
            vel = new float2(vel.X, 0);
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
        if (!sim.Map.Passable(cell) || !Fits(sim, new float2(x, y), z))
            return;
        if (cell != p.Cell && w.TryGet<Perched>(e, out var perch) && perch.Cell != cell)
            w.Remove<Perched>(e);
        p.Pos = new float3(x, y, z);
        p.Cell = cell;
        if (stopped)
            w.Get<FreeMover>(e).Vel = float2.Zero;
    }

    /// <summary>Could a free mover stand with its middle at <paramref name="at"/>: every cell its circle touches passable (on the level, or a step up or down)?</summary>
    public static bool Fits(Simulation sim, float2 at, int z)
    {
        var map = sim.Map;
        for (var dy = -1; dy <= 1; dy += 2)
        for (var dx = -1; dx <= 1; dx += 2)
        {
            var cell = new int3((int)MathF.Floor(at.X + dx * Radius), (int)MathF.Floor(at.Y + dy * Radius), z);
            if (!map.Passable(cell) && !map.Passable(cell with { Z = z + 1 }) && !map.Passable(cell with { Z = z - 1 }))
                return false;
        }
        return true;
    }

    /// <summary>
    /// The cell a free mover in <paramref name="from"/> stands in with its middle moved to
    /// <paramref name="at"/>, or null if it can't go there. Into a neighbouring cell only as the
    /// walking links allow (<see cref="NavGraph"/>: on the level, else up or down a stair or slope),
    /// so levels change just where walking would change them, and a drop or a wall stops you.
    /// </summary>
    public static int3? Into(Simulation sim, int3 from, float2 at)
    {
        var to = new int3((int)MathF.Floor(at.X), (int)MathF.Floor(at.Y), from.Z);
        if (to.X == from.X && to.Y == from.Y)
            return Fits(sim, at, from.Z) ? from : null;
        var map = sim.Map;
        if (!map.InBounds(from))
            return null;
        Span<NavGraph.Step> steps = stackalloc NavGraph.Step[NavGraph.MaxSteps];
        var count = NavGraph.Neighbors(new Maps.MapNav(map), map.Idx(from), steps);
        int3? found = null;
        for (var k = 0; k < count; k++)
        {
            var c = map.CellOf(steps[k].Index);
            if (c.X != to.X || c.Y != to.Y)
                continue;
            if (c.Z == from.Z)
            {
                found = c;
                break;
            }
            found ??= c;
        }
        return found is { } next && Fits(sim, at, next.Z) ? next : null;
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

    public void Save(SaveWriter w, Simulation sim) => w.Bool(Enabled);

    public void Load(SaveReader r, Simulation sim) => Enabled = r.Bool();
}
