using DragonWorld.Sim.Defs;

namespace DragonWorld.Sim.Maps;

[Flags]
public enum Designation : byte
{
    None = 0,
    Chop = 1,
    Mine = 2,
    Harvest = 4,
    Deconstruct = 8,

    /// <summary>On a solid block: dig it out.</summary>
    Dig = 16,

    /// <summary>
    /// Clear with fire: dragons that can breathe fire torch the cell until nothing in it will burn,
    /// and villagers leave the flames alone.
    /// </summary>
    Burn = 32,

    /// <summary>On a bank cell next to water: fish from here.</summary>
    Fish = 64,
}

/// <summary>
/// The cell grid. Struct-of-arrays layers indexed by <c>(z * Height + y) * Width + x</c>.
/// Each cell holds a block (solid ground or air), at most one floor, at most one structure
/// (building, plant, resource, construction site) and at most one item stack. Creatures stand
/// in an air cell with solid ground below, a floor, or a ladder below; they aren't stored per cell.
/// </summary>
public sealed class Map
{
    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }

    /// <summary>Cells per Z level.</summary>
    public int LayerSize => Width * Height;

    public int Count => Width * Height * Depth;

    public readonly BlockDef[] Block;
    public readonly Entity[] Structure;

    /// <summary>Floor (or floor construction site) at the bottom of each cell.</summary>
    public readonly Entity[] Floor;

    /// <summary>A finished floor is in the cell. Kept in sync by <c>Simulation.Things</c>.</summary>
    public readonly bool[] HasFloor;

    /// <summary>
    /// A finished roof is in the cell (<see cref="BuildingProps.IsRoof"/>, in the floor layer): it
    /// shelters the cells below like a floor, but nobody stands on it. Kept in sync by
    /// <c>Simulation.Things</c>; not saved (derived from the floor layer on load).
    /// </summary>
    public readonly bool[] HasRoof;

    /// <summary>A finished floor or roof at the bottom of the cell: what stops smoke, light and flyers between levels.</summary>
    public bool Ceiling(int idx) => HasFloor[idx] || HasRoof[idx];
    public readonly Entity[] Item;
    public readonly bool[] Roofed;
    public readonly float[] Temperature;
    public readonly float[] Fire;

    /// <summary>Smoke density per cell (any air cell, including open sky). Maintained by <c>SmokeSystem</c>.</summary>
    public readonly float[] Smoke;
    public readonly float[] Light;
    private readonly Designation[] _designations;
    private readonly int[] _zone;

    // One sorted index per designation flag, plus "any" and zones, so work searches visit only
    // marked cells (ascending index, the same order a full scan would) instead of every cell.
    private readonly SortedSet<int>[] _designated = new SortedSet<int>[DesignationBits];
    private readonly SortedSet<int> _anyDesignated = [];
    private readonly SortedSet<int> _zoned = [];
    private const int DesignationBits = 8;

    /// <summary>Orders per cell. Change them through <see cref="Designate"/> and friends so the indexes stay in sync.</summary>
    public ReadOnlySpan<Designation> Designations => _designations;

    /// <summary>Stockpile zone per cell; 0 means none. Change through <see cref="SetZone"/>.</summary>
    public ReadOnlySpan<int> Zone => _zone;

    /// <summary>
    /// Cells carrying <paramref name="flag"/> (a single flag), in ascending index order. Live view:
    /// don't change designations while iterating it; collect cells to clear and clear them after.
    /// </summary>
    public IReadOnlyCollection<int> Designated(Designation flag) => _designated[FlagBit(flag)];

    /// <summary>Cells with any designation, ascending (overlays).</summary>
    public IReadOnlyCollection<int> AnyDesignated => _anyDesignated;

    /// <summary>Cells in a stockpile zone, ascending.</summary>
    public IReadOnlyCollection<int> ZonedCells => _zoned;

    /// <summary>Add <paramref name="flags"/> to a cell's orders.</summary>
    public void Designate(int i, Designation flags) => SetDesignations(i, _designations[i] | flags);

    /// <summary>Remove <paramref name="flags"/> from a cell's orders.</summary>
    public void ClearDesignation(int i, Designation flags) => SetDesignations(i, _designations[i] & ~flags);

    /// <summary>Replace a cell's orders.</summary>
    public void SetDesignations(int i, Designation value)
    {
        var old = _designations[i];
        if (old == value)
            return;
        _designations[i] = value;
        var changed = (int)(old ^ value);
        for (var bit = 0; bit < DesignationBits; bit++)
        {
            if ((changed & (1 << bit)) == 0)
                continue;
            if (((int)value & (1 << bit)) != 0)
                _designated[bit].Add(i);
            else
                _designated[bit].Remove(i);
        }
        if (value == Designation.None)
            _anyDesignated.Remove(i);
        else
            _anyDesignated.Add(i);
    }

    public void SetZone(int i, int zone)
    {
        _zone[i] = zone;
        if (zone > 0)
            _zoned.Add(i);
        else
            _zoned.Remove(i);
    }

    private static int FlagBit(Designation flag)
    {
        var v = (int)flag;
        if (v == 0 || (v & (v - 1)) != 0)
            throw new ArgumentException($"Expected a single designation flag, got {flag}", nameof(flag));
        return System.Numerics.BitOperations.TrailingZeroCount(v);
    }

    /// <summary>Room id per cell; 0 for walls and solid blocks. Maintained by <see cref="Rooms"/>.</summary>
    public readonly int[] RoomId;

    /// <summary>Connectivity region per cell for fast reachability; -1 for impassable.</summary>
    public readonly int[] Region;

    /// <summary>Movement cost per cell; 0 means impassable. Rebuilt on structure/block change.</summary>
    public readonly float[] PathCost;

    /// <summary>Finished ramps/stairs/ladders per cell. Rebuilt with <see cref="PathCost"/>.</summary>
    public readonly ClimbKind[] Climb;

    /// <summary>
    /// Air cells where anything can be: standable, or holding a structure. Environment systems only
    /// simulate these; open sky away from the ground just follows the outdoor climate. Rebuilt with
    /// <see cref="PathCost"/>.
    /// </summary>
    public readonly bool[] Active;

    /// <summary>Rock or a floor somewhere above the cell (a real roof). Rebuilt with <see cref="PathCost"/>.</summary>
    public readonly bool[] Covered;

    /// <summary>Indices of <see cref="Active"/> cells, ascending.</summary>
    public readonly List<int> ActiveCells = [];

    /// <summary>Incremented whenever blocks/walls/doors/passability change. Systems watch it to rebuild caches.</summary>
    public int StructureVersion { get; private set; }

    /// <summary>Columns per side of a render chunk.</summary>
    public const int ChunkSize = 16;

    public int ChunksX => (Width + ChunkSize - 1) / ChunkSize;
    public int ChunksY => (Height + ChunkSize - 1) / ChunkSize;

    /// <summary>
    /// Per render chunk (full height), bumped when anything a renderer meshes there changes: blocks,
    /// structures, block surfaces. Pure bookkeeping for the view; not part of the sim state.
    /// </summary>
    public readonly int[] ChunkVersion;

    public Map(int width, int height, int depth, BlockDef fill)
    {
        Width = width;
        Height = height;
        Depth = depth;
        var n = width * height * depth;
        Block = new BlockDef[n];
        Array.Fill(Block, fill);
        Structure = new Entity[n];
        Array.Fill(Structure, Entity.Null);
        Floor = new Entity[n];
        Array.Fill(Floor, Entity.Null);
        HasFloor = new bool[n];
        HasRoof = new bool[n];
        Item = new Entity[n];
        Array.Fill(Item, Entity.Null);
        Roofed = new bool[n];
        Temperature = new float[n];
        Fire = new float[n];
        Smoke = new float[n];
        Light = new float[n];
        _designations = new Designation[n];
        _zone = new int[n];
        for (var b = 0; b < DesignationBits; b++)
            _designated[b] = [];
        RoomId = new int[n];
        Region = new int[n];
        PathCost = new float[n];
        Climb = new ClimbKind[n];
        Active = new bool[n];
        Covered = new bool[n];
        ChunkVersion = new int[ChunksX * ChunksY];
    }

    public bool InBounds(int3 c) => InBounds(c.X, c.Y, c.Z);

    public bool InBounds(int x, int y, int z) =>
        x >= 0 && y >= 0 && z >= 0 && x < Width && y < Height && z < Depth;

    public int Idx(int3 c) => (c.Z * Height + c.Y) * Width + c.X;
    public int Idx(int x, int y, int z) => (z * Height + y) * Width + x;

    public int3 CellOf(int idx)
    {
        var layer = LayerSize;
        var z = idx / layer;
        var rest = idx - z * layer;
        return new int3(rest % Width, rest / Width, z);
    }

    public bool Passable(int3 c) => InBounds(c) && PathCost[Idx(c)] > 0;

    public bool IsSolid(int idx) => Block[idx].Solid;

    /// <summary>The solid block a creature in this cell would stand on, or null if there is none.</summary>
    public BlockDef? GroundUnder(int idx)
    {
        if (idx < LayerSize)
            return null;
        var below = Block[idx - LayerSize];
        return below.Solid ? below : null;
    }

    /// <summary>Air cell with a finished floor, solid ground below, or a ladder below.</summary>
    public bool Standable(int idx) =>
        !Block[idx].Solid
        && (HasFloor[idx] || idx >= LayerSize && (Block[idx - LayerSize].Solid || Climb[idx - LayerSize] == ClimbKind.Ladder));

    /// <summary>Something solid or a floor somewhere above the cell: it's under a real roof (as of the last rebuild).</summary>
    public bool CoveredAbove(int idx) => Covered[idx];

    /// <summary>
    /// What a player sees in column (x, y) with the view cut at <paramref name="cutZ"/>: the
    /// highest solid or standable cell at or below the cut. Area orders act on it.
    /// </summary>
    public int3 VisibleCell(int x, int y, int cutZ)
    {
        for (var z = Math.Min(cutZ, Depth - 1); z > 0; z--)
        {
            var i = Idx(x, y, z);
            if (IsSolid(i) || Standable(i))
                return new int3(x, y, z);
        }
        return new int3(x, y, 0);
    }

    /// <summary>
    /// The standable cell in column (x, y) closest to level <paramref name="z"/> (preferring the
    /// same level, then below, then above), within <paramref name="maxDz"/> levels. Null if none.
    /// </summary>
    public int3? NearestStandable(int x, int y, int z, int maxDz = 3)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            return null;
        for (var dz = 0; dz <= maxDz; dz++)
        {
            if (z - dz >= 0 && z - dz < Depth && Standable(Idx(x, y, z - dz)))
                return new int3(x, y, z - dz);
            if (dz > 0 && z + dz < Depth && z + dz >= 0 && Standable(Idx(x, y, z + dz)))
                return new int3(x, y, z + dz);
        }
        return null;
    }

    /// <summary>Highest standable cell in a column, or -1 (the outdoor surface).</summary>
    public int SurfaceZ(int x, int y)
    {
        for (var z = Depth - 1; z >= 1; z--)
            if (Standable(Idx(x, y, z)))
                return z;
        return -1;
    }

    /// <summary>Something changed passability or walls somewhere (unknown where): rebuild everything.</summary>
    public void MarkStructureChanged()
    {
        StructureVersion++;
        for (var i = 0; i < ChunkVersion.Length; i++)
            ChunkVersion[i]++;
    }

    /// <summary>The block or structure in one cell changed in a way that may affect passability or walls.</summary>
    public void MarkStructureChanged(int idx)
    {
        StructureVersion++;
        MarkCellChanged(idx);
    }

    /// <summary>
    /// A cell changed only in how it looks (a block's surface burned). Bumps its chunk, and the
    /// neighboring chunk too at a chunk border since faces there depend on both sides.
    /// </summary>
    public void MarkCellChanged(int idx)
    {
        var c = CellOf(idx);
        int cx = c.X / ChunkSize, cy = c.Y / ChunkSize;
        ChunkVersion[cy * ChunksX + cx]++;
        int lx = c.X % ChunkSize, ly = c.Y % ChunkSize;
        if (lx == 0 && cx > 0) ChunkVersion[cy * ChunksX + cx - 1]++;
        if (lx == ChunkSize - 1 && cx + 1 < ChunksX) ChunkVersion[cy * ChunksX + cx + 1]++;
        if (ly == 0 && cy > 0) ChunkVersion[(cy - 1) * ChunksX + cx]++;
        if (ly == ChunkSize - 1 && cy + 1 < ChunksY) ChunkVersion[(cy + 1) * ChunksX + cx]++;
    }

    /// <summary>
    /// Walk the cells a ray passes through (Amanatides–Woo voxel traversal), nearest first, until
    /// <paramref name="hit"/> accepts one. <paramref name="normal"/> is the face the ray entered
    /// through (zero if it started inside). Rays starting outside the map are clipped to it.
    /// </summary>
    public bool Raycast(float3 origin, float3 dir, float maxDistance, Func<int, bool> hit, out int3 cell, out int3 normal)
    {
        cell = default;
        normal = default;

        // Clip to the map box so rays from a camera far outside start at the boundary.
        float tMin = 0f, tMax = maxDistance;
        int3 entryNormal = default;
        Span<float> o = [origin.X, origin.Y, origin.Z];
        Span<float> d = [dir.X, dir.Y, dir.Z];
        Span<float> size = [Width, Height, Depth];
        for (var a = 0; a < 3; a++)
        {
            if (MathF.Abs(d[a]) < 1e-9f)
            {
                if (o[a] < 0 || o[a] >= size[a])
                    return false;
                continue;
            }
            var t0 = (0 - o[a]) / d[a];
            var t1 = (size[a] - o[a]) / d[a];
            if (t0 > t1)
                (t0, t1) = (t1, t0);
            if (t0 > tMin)
            {
                tMin = t0;
                entryNormal = AxisNormal(a, -Math.Sign(d[a]));
            }
            tMax = Math.Min(tMax, t1);
            if (tMin > tMax)
                return false;
        }

        var p = origin + dir * (tMin + 1e-4f);
        int x = Math.Clamp((int)MathF.Floor(p.X), 0, Width - 1);
        int y = Math.Clamp((int)MathF.Floor(p.Y), 0, Height - 1);
        int z = Math.Clamp((int)MathF.Floor(p.Z), 0, Depth - 1);
        int sx = Math.Sign(dir.X), sy = Math.Sign(dir.Y), sz = Math.Sign(dir.Z);
        float Next(float pos, int cellPos, int step, float dd) =>
            step == 0 ? float.PositiveInfinity : ((step > 0 ? cellPos + 1 : cellPos) - pos) / dd;
        float tx = tMin + Next(p.X, x, sx, dir.X), ty = tMin + Next(p.Y, y, sy, dir.Y), tz = tMin + Next(p.Z, z, sz, dir.Z);
        float dx = sx == 0 ? float.PositiveInfinity : MathF.Abs(1f / dir.X);
        float dy = sy == 0 ? float.PositiveInfinity : MathF.Abs(1f / dir.Y);
        float dz = sz == 0 ? float.PositiveInfinity : MathF.Abs(1f / dir.Z);
        normal = entryNormal;

        while (true)
        {
            if (hit(Idx(x, y, z)))
            {
                cell = new int3(x, y, z);
                return true;
            }
            float t;
            if (tx <= ty && tx <= tz)
            {
                t = tx;
                x += sx;
                tx += dx;
                normal = new int3(-sx, 0, 0);
            }
            else if (ty <= tz)
            {
                t = ty;
                y += sy;
                ty += dy;
                normal = new int3(0, -sy, 0);
            }
            else
            {
                t = tz;
                z += sz;
                tz += dz;
                normal = new int3(0, 0, -sz);
            }
            if (t > tMax || !InBounds(x, y, z))
                return false;
        }
    }

    private static int3 AxisNormal(int axis, int sign) =>
        axis == 0 ? new int3(sign, 0, 0) : axis == 1 ? new int3(0, sign, 0) : new int3(0, 0, sign);

    public static readonly int2[] Neighbors4 = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    public static readonly int2[] Neighbors8 =
    [
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(-1, 1), new(1, -1), new(-1, -1),
    ];

    /// <summary>A cell offset horizontally on the same Z level.</summary>
    public static int3 Offset(int3 c, int2 d) => new(c.X + d.X, c.Y + d.Y, c.Z);

    /// <summary>Chebyshev distance on the horizontal plane plus the Z difference.</summary>
    public static int Distance(int3 a, int3 b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)) + Math.Abs(a.Z - b.Z);

    /// <summary>Cells in a filled horizontal circle on the center's Z level. Nearest first is NOT guaranteed.</summary>
    public IEnumerable<int3> CellsInRadius(int3 center, float radius)
    {
        var r = (int)MathF.Ceiling(radius);
        var r2 = radius * radius;
        for (var dy = -r; dy <= r; dy++)
        for (var dx = -r; dx <= r; dx++)
        {
            if (dx * dx + dy * dy > r2)
                continue;
            var c = new int3(center.X + dx, center.Y + dy, center.Z);
            if (InBounds(c))
                yield return c;
        }
    }
}
