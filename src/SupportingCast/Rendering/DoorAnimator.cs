using Narsil.Rendering;
using SupportingCast.Sim;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Ecs;

namespace SupportingCast.Rendering;

/// <summary>
/// Doors that swing. A door is drawn per frame (not baked into the chunk meshes) as a slab hinged
/// at one end, swinging open, away from whoever pushes it, while a creature is in its doorway or
/// about to step into it, and falling shut once they're through. A lockable gate (the Escape
/// cage's) swings by its lock instead: shut with a padlock on it while <see cref="Locked"/>,
/// swung open and left standing open while not. Presentation only: what's passable is the sim's
/// business, and nothing here is saved or hashed. Times are animation time (game speed; frozen
/// when paused).
/// </summary>
public sealed class DoorAnimator
{
    /// <summary>How far an open door swings, in radians (a little short of square to the wall).</summary>
    private const float SwingAngle = 85f * MathF.PI / 180f;

    /// <summary>Opening and closing speeds, in full swings per second: quick to open, slower to fall shut.</summary>
    private const float OpenSpeed = 5f, CloseSpeed = 2.2f;

    /// <summary>A creature this close (cells, horizontally) to the doorway's middle is in it.</summary>
    private const float InDoorway = 0.95f;

    private const float Thickness = 0.2f;

    /// <summary>A gate of bars is thinner than a plank door.</summary>
    private const float GateThickness = 0.08f;

    /// <summary>A gate swings open more slowly when it's unlocked (it's heavy, and someone's pulling it).</summary>
    private const float GateSpeed = 1.6f;

    private sealed class Door
    {
        public Entity Entity;
        public int3 Cell;

        /// <summary>0 shut, 1 wide open.</summary>
        public float Open;

        /// <summary>Which way it swings this time (+1 or -1), picked as it starts to open.</summary>
        public float Sign = 1;
    }

    private readonly Dictionary<int, Door> _doors = [];
    private readonly List<int> _stale = [];
    private int _version = -1;

    /// <summary>How tall each door stands (world z): 1, or a stub where the walls are cut away (<see cref="WorldMesher.IsStub"/>).</summary>
    public Func<int3, float> HeightOf { get; set; } = _ => 1f;

    /// <summary>Whether the viewer can see a cell; null when they see everything.</summary>
    public Func<int3, bool>? CanSee { get; set; }

    /// <summary>Animate and draw every finished door in view.</summary>
    /// <param name="padlock">Draws the padlock on a locked gate: where (its middle) and the cell level's flags.</param>
    public void Draw(QuadRenderer quads, Simulation sim, Func<int3, bool> inView, float dt, Func<Thing, (Sprite Sprite, uint Tint)> art,
        Action<float3, uint> padlock)
    {
        Refresh(sim);
        var w = sim.World;
        foreach (var door in _doors.Values)
        {
            if (!w.IsAlive(door.Entity))
                continue;
            var thing = w.Get<Thing>(door.Entity);
            var gate = thing.Def.Building?.Lockable == true;
            var (want, side) = Wanted(sim, door);
            if (gate)
                want = !w.Has<Locked>(door.Entity); // a gate goes by its lock, not by who's near
            else if (CanSee != null && !CanSee(door.Cell))
                want = false; // a door swinging out of sight would give away who's behind it
            if (want && door.Open < 0.05f && side != 0)
                door.Sign = side > 0 ? -1f : 1f; // pushed open, away from them
            var speed = gate ? GateSpeed : want ? OpenSpeed : CloseSpeed;
            door.Open = want ? Math.Min(1f, door.Open + dt * speed) : Math.Max(0f, door.Open - dt * speed);
            if (!inView(door.Cell))
                continue;
            var thickness = gate ? GateThickness : Thickness;
            Emit(quads, sim, door, art(thing), thickness, HeightOf(door.Cell));
            if (gate && w.Has<Locked>(door.Entity) && door.Open < 0.02f)
                padlock(LockSpot(sim, door, thickness), Quad.Level(door.Cell.Z));
        }
    }

    /// <summary>Rebuild the door list when structures change (doors built, burned, taken apart).</summary>
    private void Refresh(Simulation sim)
    {
        if (_version == sim.Map.StructureVersion)
            return;
        _version = sim.Map.StructureVersion;
        var w = sim.World;
        var seen = new HashSet<int>();
        foreach (var e in w.Each<Thing>())
        {
            var t = w.Get<Thing>(e);
            if (!t.Spawned || t.Def.Building?.IsDoor != true || w.Has<Construction>(e))
                continue;
            var i = sim.Map.Idx(t.Cell);
            seen.Add(i);
            if (!_doors.TryGetValue(i, out var door) || door.Entity != e)
                _doors[i] = new Door { Entity = e, Cell = t.Cell };
        }
        _stale.Clear();
        foreach (var i in _doors.Keys)
            if (!seen.Contains(i))
                _stale.Add(i);
        foreach (var i in _stale)
            _doors.Remove(i);
    }

    /// <summary>Should the door be open (someone in or stepping into the doorway), and which side of it are they on?</summary>
    private static (bool Want, float Side) Wanted(Simulation sim, Door door)
    {
        var w = sim.World;
        var c = door.Cell;
        var center = new float3(c.X + 0.5f, c.Y + 0.5f, c.Z);
        var normal = DoorRunsAlongX(sim, c) ? new float2(0, 1) : new float2(-1, 0);
        foreach (var e in w.Each<Creature>())
        {
            if (w.Get<Creature>(e).Dead)
                continue;
            var p = w.Get<Position>(e).Pos;
            if (MathF.Abs(p.Z - c.Z) > 0.6f)
                continue;
            var d = new float2(p.X - center.X, p.Y - center.Y);
            var stepping = w.TryGet<Mover>(e, out var mover) && mover.Moving
                && (mover.Path![mover.PathIndex] == c || (mover.PathIndex + 1 < mover.Path.Count && mover.Path[mover.PathIndex + 1] == c && d.X * d.X + d.Y * d.Y < 2.5f));
            if (stepping || d.X * d.X + d.Y * d.Y < InDoorway * InDoorway)
                return (true, d.X * normal.X + d.Y * normal.Y);
        }
        return (false, 0);
    }

    /// <summary>Runs east-west if there's a wall (or rock) east or west of it, else north-south (as <see cref="WorldMesher"/> lines walls up).</summary>
    public static bool DoorRunsAlongX(Simulation sim, int3 c) =>
        WorldMesher.Occludes(sim, c.X - 1, c.Y, c.Z) || WorldMesher.Occludes(sim, c.X + 1, c.Y, c.Z)
        || !(WorldMesher.Occludes(sim, c.X, c.Y - 1, c.Z) || WorldMesher.Occludes(sim, c.X, c.Y + 1, c.Z));

    /// <summary>
    /// The slab: hinged at its west (or south) end, rotated by the swing. Shut, it's exactly the
    /// slab the mesher used to bake: two faces with the door art, plain edges and top.
    /// </summary>
    private static void Emit(QuadRenderer quads, Simulation sim, Door door, (Sprite Sprite, uint Tint) art, float thickness, float height = 1f)
    {
        var c = door.Cell;
        var alongX = DoorRunsAlongX(sim, c);
        // Shut: running along d, its faces towards n (d x n points up).
        var d0 = alongX ? new float2(1, 0) : new float2(0, 1);
        var hinge = alongX ? new float2(c.X, c.Y + 0.5f) : new float2(c.X + 0.5f, c.Y);
        var angle = door.Sign * SwingAngle * Ease(door.Open);
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        var d2 = new float2(d0.X * cos - d0.Y * sin, d0.X * sin + d0.Y * cos);
        var d = new float3(d2.X, d2.Y, 0);
        var n = new float3(-d2.Y, d2.X, 0);
        var top = new float3(hinge.X, hinge.Y, c.Z + height);
        var half = n * (thickness / 2);
        var uv = art.Sprite.Uv;
        var edgeUv = new float4(uv.X, uv.Y, uv.X + (uv.Z - uv.X) * thickness, uv.W);
        var level = Quad.Level(c.Z) | Quad.Occluder;

        Side(top + half, d, alongX ? 0.88f : 0.66f, uv);                    // the face towards n
        Side(top + d - half, -d, alongX ? 0.6f : 0.78f, uv);                // the face away from n
        Face(top - half, d, n * thickness, 1f, edgeUv);                     // top
        Side(top + d + half, -n * thickness, 0.72f, edgeUv);                // the swinging end
        if (door.Open > 0.01f)
            Side(top - half, n * thickness, 0.7f, edgeUv);                  // the hinge end, once it's clear of the wall

        // A face a level tall: door art drawn as tall as the level looks shows once, square art
        // stacks. Cut down to a stub with the walls, it shows the bottom of the door.
        void Side(float3 origin, float3 u, float shade, float4 faceUv)
        {
            var b = (uint)(shade * 255f);
            var template = new Quad { Flags = level, Color = art.Tint, Ao = b | b << 8 | b << 16 | b << 24 };
            var tiles = WorldScale.TilesPerLevel(art.Sprite);
            for (var k = 0; k < WorldScale.TileCount(height, tiles); k++)
                quads.Add(WorldScale.Tile(template, origin, u, height, faceUv, tiles, k, fromBottom: height < 1f));
        }

        void Face(float3 origin, float3 u, float3 v, float shade, float4 faceUv)
        {
            var b = (uint)(shade * 255f);
            quads.Add(new Quad
            {
                Origin = origin,
                AxisU = u,
                AxisV = v,
                Flags = level,
                Color = art.Tint,
                Ao = b | b << 8 | b << 16 | b << 24,
                Uv = faceUv,
            });
        }
    }

    /// <summary>
    /// Where a shut gate's padlock hangs: by its free end (where it'd latch to the post), a little
    /// out from the face the camera sees, at chest height.
    /// </summary>
    private static float3 LockSpot(Simulation sim, Door door, float thickness)
    {
        var c = door.Cell;
        var alongX = DoorRunsAlongX(sim, c);
        var d = alongX ? new float3(1, 0, 0) : new float3(0, 1, 0);
        var n = alongX ? new float3(0, 1, 0) : new float3(-1, 0, 0);
        var hinge = alongX ? new float3(c.X, c.Y + 0.5f, c.Z) : new float3(c.X + 0.5f, c.Y, c.Z);
        return hinge + d * 0.85f + n * (thickness / 2 + 0.03f) + new float3(0, 0, 0.45f);
    }

    /// <summary>Smoothstep, so the swing starts and settles gently.</summary>
    private static float Ease(float t) => t * t * (3f - 2f * t);
}
