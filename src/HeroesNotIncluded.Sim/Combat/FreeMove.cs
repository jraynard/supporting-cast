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

    /// <summary>Slower while swinging and while guarding.</summary>
    public const float SwingSlow = 0.45f, GuardSlow = 0.6f;

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
        foreach (var e in w.Each<FreeMover>())
            Step(sim, e);
    }

    private static void Step(Simulation sim, Entity e)
    {
        var w = sim.World;
        ref var m = ref w.Get<FreeMover>(e);
        var c = w.Get<Creature>(e);
        var now = sim.Clock.Tick;
        float2 want;
        if (!c.Able || w.Get<Mind>(e).Asleep)
            want = float2.Zero;
        else if (now < m.DashUntil)
            want = m.DashDir * (DashCells / DashTicks);
        else
        {
            var speed = MovementSystem.WalkSpeedPerTick(sim, e);
            if (w.Has<Windup>(e))
                speed *= SwingSlow;
            else if (w.TryGet<Guard>(e, out var g) && g.RaisedAt >= 0)
                speed *= GuardSlow;
            want = m.Wish * speed;
        }
        m.Vel = now < m.DashUntil ? want : m.Vel + (want - m.Vel) * Ease;
        // Easing never quite gets there: near enough standing still is standing still.
        if (want == float2.Zero && m.Vel.X * m.Vel.X + m.Vel.Y * m.Vel.Y < StopBelow * StopBelow)
        {
            m.Vel = float2.Zero;
            return;
        }
        ref var p = ref w.Get<Position>(e);
        var z = p.Cell.Z;
        // Each axis on its own, so a wall stops one and you slide along it on the other.
        var at = new float2(p.Pos.X, p.Pos.Y);
        var x = new float2(at.X + m.Vel.X, at.Y);
        if (Fits(sim, x, z))
            at = x;
        else
            m.Vel = new float2(0, m.Vel.Y);
        var y = new float2(at.X, at.Y + m.Vel.Y);
        if (Fits(sim, y, z))
            at = y;
        else
            m.Vel = new float2(m.Vel.X, 0);
        p.Pos = new float3(at.X, at.Y, p.Pos.Z);
        p.Cell = new int3((int)MathF.Floor(at.X), (int)MathF.Floor(at.Y), z);
        // F's "in front" is the way you face, to the nearest of eight.
        var (cx, cy) = (DetMath.Cos(m.Aim), DetMath.Sin(m.Aim));
        w.Get<Avatar>(e).Facing = new int2(cx > 0.38f ? 1 : cx < -0.38f ? -1 : 0, cy > 0.38f ? 1 : cy < -0.38f ? -1 : 0);
    }

    /// <summary>Could a free mover stand with its middle at <paramref name="at"/>: every cell its circle touches passable?</summary>
    public static bool Fits(Simulation sim, float2 at, int z)
    {
        var map = sim.Map;
        for (var dy = -1; dy <= 1; dy += 2)
        for (var dx = -1; dx <= 1; dx += 2)
        {
            var cell = new int3((int)MathF.Floor(at.X + dx * Radius), (int)MathF.Floor(at.Y + dy * Radius), z);
            if (!map.Passable(cell))
                return false;
        }
        return true;
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
