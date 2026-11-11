namespace DragonWorld.Sim.Ecs;

internal interface IComponentStore
{
    bool Has(int index);
    bool Remove(int index);
    int Count { get; }
    Type ComponentType { get; }
    object GetBoxed(int index);
    ReadOnlySpan<Entity> Entities { get; }
}

/// <summary>
/// Sparse set: dense arrays of components and owning entities, plus a sparse index map.
/// Iteration is over the dense arrays; removal swaps the last element into the hole.
/// </summary>
public sealed class ComponentStore<T> : IComponentStore
{
    private int[] _sparse = new int[256];
    private Entity[] _entities = new Entity[64];
    private T[] _data = new T[64];
    private int _count;

    public ComponentStore() => Array.Fill(_sparse, -1);

    /// <summary>Bumped whenever an entity joins or leaves the store, so live iteration can detect it.</summary>
    public int Version { get; private set; }

    public int Count => _count;
    public Type ComponentType => typeof(T);
    public ReadOnlySpan<Entity> Entities => _entities.AsSpan(0, _count);
    public Span<T> Components => _data.AsSpan(0, _count);

    public bool Has(int index) => index < _sparse.Length && _sparse[index] >= 0;

    public ref T Get(int index)
    {
        var dense = index < _sparse.Length ? _sparse[index] : -1;
        if (dense < 0)
            throw new InvalidOperationException($"Entity index {index} has no {typeof(T).Name}");
        return ref _data[dense];
    }

    public ref T TryGet(int index, out bool found)
    {
        var dense = index < _sparse.Length ? _sparse[index] : -1;
        found = dense >= 0;
        return ref found ? ref _data[dense] : ref _dummy;
    }

    private T _dummy = default!;

    public void Set(Entity e, in T value)
    {
        if (e.Index >= _sparse.Length)
        {
            var old = _sparse.Length;
            Array.Resize(ref _sparse, Math.Max(e.Index + 1, old * 2));
            Array.Fill(_sparse, -1, old, _sparse.Length - old);
        }

        var dense = _sparse[e.Index];
        if (dense >= 0)
        {
            _data[dense] = value;
            _entities[dense] = e;
            return;
        }

        if (_count == _data.Length)
        {
            Array.Resize(ref _data, _count * 2);
            Array.Resize(ref _entities, _count * 2);
        }
        _sparse[e.Index] = _count;
        _entities[_count] = e;
        _data[_count] = value;
        _count++;
        Version++;
    }

    public bool Remove(int index)
    {
        if (!Has(index))
            return false;
        var dense = _sparse[index];
        var last = _count - 1;
        if (dense != last)
        {
            _data[dense] = _data[last];
            _entities[dense] = _entities[last];
            _sparse[_entities[dense].Index] = dense;
        }
        _data[last] = default!;
        _entities[last] = default;
        _sparse[index] = -1;
        _count--;
        Version++;
        return true;
    }

    public object GetBoxed(int index) => Get(index)!;
}

/// <summary>
/// Allocation-free view over the entities in one store, from <see cref="World.Each{T}"/>.
/// Reading and writing component values while iterating is fine; adding or removing the
/// component (or destroying an entity that has it) throws, because the dense arrays reorder.
/// Use <see cref="World.Query{T}"/> for loops that do that.
/// </summary>
public readonly struct StoreView<T>(ComponentStore<T> store)
{
    public int Count => store.Count;

    public Enumerator GetEnumerator() => new(store);

    public struct Enumerator(ComponentStore<T> store)
    {
        private readonly int _version = store.Version;
        private int _index = -1;

        public readonly Entity Current => store.Entities[_index];

        public bool MoveNext()
        {
            if (store.Version != _version)
                throw new InvalidOperationException($"{typeof(T).Name} store changed during Each<{typeof(T).Name}>(); use Query<{typeof(T).Name}>() to add or remove while iterating");
            return ++_index < store.Count;
        }
    }
}
