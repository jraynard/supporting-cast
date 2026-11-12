namespace HeroesNotIncluded.Sim.Ai;

/// <summary>
/// Claims on entities and tiles so two pawns don't haul the same item or build on the same spot.
/// Released when the claiming pawn's job ends (every job end, so <see cref="ReleaseAll"/> uses a
/// per-pawn index instead of scanning every claim), and when a claimed entity is destroyed.
/// </summary>
public sealed class Reservations
{
    private readonly Dictionary<Entity, Entity> _things = new();
    private readonly Dictionary<int3, Entity> _tiles = new();
    private readonly Dictionary<Entity, List<Entity>> _thingsByPawn = new();
    private readonly Dictionary<Entity, List<int3>> _tilesByPawn = new();

    public bool IsReserved(Entity target) => _things.ContainsKey(target);

    public bool IsReservedByOther(Entity target, Entity pawn) =>
        _things.TryGetValue(target, out var by) && by != pawn;

    public bool IsTileReservedByOther(int3 tile, Entity pawn) =>
        _tiles.TryGetValue(tile, out var by) && by != pawn;

    public Entity ReservedBy(Entity target) => _things.TryGetValue(target, out var by) ? by : Entity.Null;

    public bool TryReserve(Entity target, Entity pawn)
    {
        if (_things.TryGetValue(target, out var by))
            return by == pawn;
        _things[target] = pawn;
        ListFor(_thingsByPawn, pawn).Add(target);
        return true;
    }

    public bool TryReserveTile(int3 tile, Entity pawn)
    {
        if (_tiles.TryGetValue(tile, out var by))
            return by == pawn;
        _tiles[tile] = pawn;
        ListFor(_tilesByPawn, pawn).Add(tile);
        return true;
    }

    public void Release(Entity target)
    {
        if (_things.Remove(target, out var by) && _thingsByPawn.TryGetValue(by, out var list))
            list.Remove(target);
    }

    public void ReleaseAll(Entity pawn)
    {
        if (_thingsByPawn.TryGetValue(pawn, out var things))
        {
            foreach (var t in things)
                if (_things.TryGetValue(t, out var by) && by == pawn)
                    _things.Remove(t);
            things.Clear();
        }
        if (_tilesByPawn.TryGetValue(pawn, out var tiles))
        {
            foreach (var t in tiles)
                if (_tiles.TryGetValue(t, out var by) && by == pawn)
                    _tiles.Remove(t);
            tiles.Clear();
        }
    }

    /// <summary>An entity is gone: drop claims on it and any it still held, and its index entries.</summary>
    public void Forget(Entity e)
    {
        Release(e);
        ReleaseAll(e);
        _thingsByPawn.Remove(e);
        _tilesByPawn.Remove(e);
    }

    private static List<T> ListFor<T>(Dictionary<Entity, List<T>> byPawn, Entity pawn)
    {
        if (!byPawn.TryGetValue(pawn, out var list))
            byPawn[pawn] = list = [];
        return list;
    }

    public IEnumerable<(Entity Target, Entity By)> All => _things.Select(kv => (kv.Key, kv.Value));
    public IEnumerable<(int3 Tile, Entity By)> AllTiles => _tiles.Select(kv => (kv.Key, kv.Value));
}
