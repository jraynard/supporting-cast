using DragonWorld.Sim.Maps;

namespace DragonWorld.Sim.Environment;

public sealed class Room
{
    public int Id;
    public int TileCount;
    /// <summary>Reaches the map edge or open sky (an inactive air cell), so it's outdoors.</summary>
    public bool TouchesMapEdge;

    /// <summary>Enclosed, small enough to roof: counts as indoors.</summary>
    public bool Indoors;

    public float AverageTemperature;
    public readonly List<int> Tiles = [];
}

/// <summary>
/// Splits each Z level into rooms: 4-connected areas of air cells that aren't walls or doors.
/// Enclosed rooms up to <see cref="MaxRoofedRoomSize"/> tiles are automatically roofed
/// (as are their bounding walls/doors), which is what lets them hold heat. Cells with a floor or
/// rock somewhere above are roofed too, enclosed or not (under a platform, in a tunnel).
/// </summary>
public sealed class Rooms
{
    public const int MaxRoofedRoomSize = 400;

    private readonly Simulation _sim;
    private int _builtVersion = -1;
    private readonly Queue<int> _queue = new();

    /// <summary>Last build's <see cref="Map.Roofed"/>, to tell the renderer which roofs it must redraw.</summary>
    private bool[] _wasRoofed = [];

    public List<Room> All { get; } = [];

    public Rooms(Simulation sim) => _sim = sim;

    public Room? RoomAt(int3 tile)
    {
        var map = _sim.Map;
        if (!map.InBounds(tile))
            return null;
        var id = map.RoomId[map.Idx(tile)];
        return id > 0 ? All[id - 1] : null;
    }

    /// <summary>Rebuilds if the map's structure changed since the last build; true if it did.</summary>
    public bool UpdateIfNeeded()
    {
        if (_builtVersion == _sim.Map.StructureVersion)
            return false;
        _sim.EnsurePathCosts(); // rooms read Map.Active, which the path-cost pass fills in
        Rebuild();
        return true;
    }

    public void Rebuild()
    {
        var map = _sim.Map;
        All.Clear();
        Array.Fill(map.RoomId, 0);

        // Only active cells form rooms; open sky around them just makes a room outdoors.
        foreach (var i in map.ActiveCells)
        {
            if (map.RoomId[i] != 0 || IsBoundary(i))
                continue;
            var room = new Room { Id = All.Count + 1 };
            All.Add(room);
            Flood(i, room);
            room.Indoors = !room.TouchesMapEdge && room.TileCount <= MaxRoofedRoomSize;
        }

        // Roof: open tiles of indoor rooms, and boundaries that touch an indoor room.
        if (_wasRoofed.Length != map.Count)
            _wasRoofed = new bool[map.Count];
        Array.Copy(map.Roofed, _wasRoofed, map.Count);
        Array.Fill(map.Roofed, false);
        foreach (var i in map.ActiveCells)
        {
            var id = map.RoomId[i];
            if (map.CoveredAbove(i))
            {
                map.Roofed[i] = true;
                continue;
            }
            if (id > 0)
            {
                map.Roofed[i] = All[id - 1].Indoors;
                continue;
            }
            var t = map.CellOf(i);
            var roofed = false;
            if (map.IsSolid(i))
            {
                map.Roofed[i] = false;
                continue;
            }
            foreach (var d in Map.Neighbors8)
            {
                var n = Map.Offset(t, d);
                if (!map.InBounds(n))
                    continue;
                var nid = map.RoomId[map.Idx(n)];
                if (nid > 0 && All[nid - 1].Indoors)
                {
                    roofed = true;
                    break;
                }
            }
            map.Roofed[i] = roofed;
        }

        // The game draws a roof over enclosed rooms: where that changed, its chunk remeshes.
        if (!map.Roofed.AsSpan().SequenceEqual(_wasRoofed))
            for (var i = 0; i < map.Count; i++)
                if (map.Roofed[i] != _wasRoofed[i])
                    map.MarkCellChanged(i);

        _builtVersion = map.StructureVersion;
    }

    /// <summary>Solid blocks, walls and doors split rooms.</summary>
    public bool IsBoundary(int idx)
    {
        if (_sim.Map.IsSolid(idx))
            return true;
        var e = _sim.Map.Structure[idx];
        if (e.IsNull)
            return false;
        var def = _sim.World.Get<Components.Thing>(e).Def;
        // Construction sites don't count until finished.
        if (_sim.World.Has<Components.Construction>(e))
            return false;
        // Fences and palisades (and their gates) wall in a yard, never a room.
        return def.Building is { Fence: false } b && (b.IsWall || b.IsDoor);
    }

    private void Flood(int seed, Room room)
    {
        var map = _sim.Map;
        map.RoomId[seed] = room.Id;
        _queue.Enqueue(seed);
        while (_queue.TryDequeue(out var i))
        {
            room.TileCount++;
            room.Tiles.Add(i);
            var t = map.CellOf(i);
            if (t.X == 0 || t.Y == 0 || t.X == map.Width - 1 || t.Y == map.Height - 1)
                room.TouchesMapEdge = true;
            foreach (var d in Map.Neighbors4)
            {
                var n = Map.Offset(t, d);
                if (!map.InBounds(n))
                    continue;
                var ni = map.Idx(n);
                if (map.RoomId[ni] != 0 || IsBoundary(ni))
                    continue;
                if (!map.Active[ni])
                {
                    room.TouchesMapEdge = true;
                    continue;
                }
                map.RoomId[ni] = room.Id;
                _queue.Enqueue(ni);
            }
        }
    }

    /// <summary>Refresh per-room average temperatures (for display).</summary>
    public void UpdateTemperatures()
    {
        var temps = _sim.Map.Temperature;
        foreach (var room in All)
        {
            if (room.TileCount > 2000)
            {
                room.AverageTemperature = _sim.Climate.OutdoorTemperature;
                continue;
            }
            var sum = 0f;
            foreach (var i in room.Tiles)
                sum += temps[i];
            room.AverageTemperature = sum / room.TileCount;
        }
    }
}
