namespace DragonWorld.Sim.Ecs;

/// <summary>
/// Entity registry and component storage. Components are plain structs (or classes for
/// data needing reference semantics). Destruction requested during iteration should go
/// through <see cref="DestroyDeferred"/> and is applied by <see cref="FlushDeferred"/>.
/// </summary>
public sealed class World
{
    private int[] _generations = new int[256];
    private bool[] _alive = new bool[256];
    private readonly Stack<int> _free = new();
    private int _next;
    private readonly List<IComponentStore> _storeList = new();
    private readonly List<Entity> _pendingDestroy = new();

    public int AliveCount { get; private set; }

    /// <summary>Every component store, for save games.</summary>
    internal IReadOnlyList<IComponentStore> Stores => _storeList;

    /// <summary>
    /// The id allocator's state, for save games: restoring it keeps every saved <see cref="Entity"/>
    /// valid and makes later <see cref="Create"/> calls hand out the same ids as the original run.
    /// </summary>
    internal (int Next, int[] Generations, bool[] Alive, int[] Free) ExportAllocator() =>
        (_next, _generations[.._next], _alive[.._next], _free.ToArray()); // ToArray: top of the stack first

    internal void ImportAllocator(int next, int[] generations, bool[] alive, int[] free)
    {
        if (AliveCount != 0)
            throw new InvalidOperationException("ImportAllocator needs an empty world");
        var size = Math.Max(256, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(next, 1)));
        _generations = new int[size];
        _alive = new bool[size];
        generations.CopyTo(_generations, 0);
        alive.CopyTo(_alive, 0);
        _next = next;
        _free.Clear();
        for (var i = free.Length - 1; i >= 0; i--)
            _free.Push(free[i]);
        AliveCount = alive.Count(a => a);
    }

    /// <summary>Raised just before an entity is destroyed (components are still readable).</summary>
    public event Action<Entity>? Destroying;

    public Entity Create()
    {
        int index;
        if (_free.Count > 0)
        {
            index = _free.Pop();
        }
        else
        {
            index = _next++;
            if (index >= _generations.Length)
            {
                Array.Resize(ref _generations, _generations.Length * 2);
                Array.Resize(ref _alive, _alive.Length * 2);
            }
        }
        _alive[index] = true;
        AliveCount++;
        return new Entity(index, _generations[index]);
    }

    public bool IsAlive(Entity e) =>
        e.Index >= 0 && e.Index < _next && _alive[e.Index] && _generations[e.Index] == e.Generation;

    public void Destroy(Entity e)
    {
        if (!IsAlive(e))
            return;
        Destroying?.Invoke(e);
        foreach (var store in _storeList)
            store.Remove(e.Index);
        _alive[e.Index] = false;
        _generations[e.Index]++;
        _free.Push(e.Index);
        AliveCount--;
    }

    public void DestroyDeferred(Entity e) => _pendingDestroy.Add(e);

    public void FlushDeferred()
    {
        // Destroy handlers may queue more destruction.
        for (var i = 0; i < _pendingDestroy.Count; i++)
            Destroy(_pendingDestroy[i]);
        _pendingDestroy.Clear();
    }

    /// <summary>A process-wide small integer per component type, so store lookup is an array index.</summary>
    private static class TypeId<T>
    {
        public static readonly int Value = Interlocked.Increment(ref _nextTypeId) - 1;
    }

    private static int _nextTypeId;
    private IComponentStore?[] _byTypeId = new IComponentStore?[32];

    public ComponentStore<T> Store<T>()
    {
        var id = TypeId<T>.Value;
        if (id < _byTypeId.Length && _byTypeId[id] is { } s)
            return (ComponentStore<T>)s;
        return CreateStore<T>(id);
    }

    private ComponentStore<T> CreateStore<T>(int id)
    {
        if (id >= _byTypeId.Length)
            Array.Resize(ref _byTypeId, Math.Max(id + 1, _byTypeId.Length * 2));
        var store = new ComponentStore<T>();
        _byTypeId[id] = store;
        _storeList.Add(store); // creation order, so Destroy visits stores in a stable order
        return store;
    }

    public void Add<T>(Entity e, in T component)
    {
        AssertAlive(e);
        Store<T>().Set(e, component);
    }

    public ref T Get<T>(Entity e)
    {
        AssertAlive(e);
        return ref Store<T>().Get(e.Index);
    }

    public bool Has<T>(Entity e) => IsAlive(e) && Store<T>().Has(e.Index);

    public bool TryGet<T>(Entity e, out T component)
    {
        if (IsAlive(e))
        {
            ref var c = ref Store<T>().TryGet(e.Index, out var found);
            if (found)
            {
                component = c;
                return true;
            }
        }
        component = default!;
        return false;
    }

    public bool Remove<T>(Entity e) => IsAlive(e) && Store<T>().Remove(e.Index);

    /// <summary>All components on an entity, boxed. For inspectors and debugging only.</summary>
    public IEnumerable<object> DebugComponents(Entity e)
    {
        if (!IsAlive(e))
            yield break;
        foreach (var s in _storeList)
            if (s.Has(e.Index))
                yield return s.GetBoxed(e.Index);
    }

    /// <summary>
    /// Entities with <typeparamref name="T"/>, without allocating. For read-only loops (rendering,
    /// UI, searches); see <see cref="StoreView{T}"/> for what may change while iterating.
    /// </summary>
    public StoreView<T> Each<T>() => new(Store<T>());

    /// <summary>
    /// Snapshot of entities that have <typeparamref name="T"/>. Safe to modify components or
    /// add/remove entities while iterating the returned list.
    /// </summary>
    public List<Entity> Query<T>()
    {
        var result = new List<Entity>(Store<T>().Count);
        result.AddRange(Store<T>().Entities);
        return result;
    }

    /// <summary>Snapshot of entities with both components, iterating the smaller store.</summary>
    public List<Entity> Query<T1, T2>()
    {
        var a = Store<T1>();
        var b = Store<T2>();
        var result = new List<Entity>();
        if (a.Count <= b.Count)
        {
            foreach (var e in a.Entities)
                if (b.Has(e.Index))
                    result.Add(e);
        }
        else
        {
            foreach (var e in b.Entities)
                if (a.Has(e.Index))
                    result.Add(e);
        }
        return result;
    }

    public int Count<T>() => Store<T>().Count;

    private void AssertAlive(Entity e)
    {
        if (!IsAlive(e))
            throw new InvalidOperationException($"{e} is not alive");
    }
}
