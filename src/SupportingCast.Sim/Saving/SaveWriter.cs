using System.Runtime.InteropServices;
using System.Text;
using SupportingCast.Sim.Defs;

namespace SupportingCast.Sim.Saving;

/// <summary>A save file that can't be read: corrupt, from a newer game, or naming defs that no longer exist.</summary>
public sealed class SaveException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Sim state that lives outside components and the map (a system's bookkeeping, a private random
/// stream). <see cref="SaveGame"/> writes it per system, by <see cref="ISimSystem.Name"/>.
/// </summary>
public interface ISaveState
{
    void Save(SaveWriter w, Simulation sim);
    void Load(SaveReader r, Simulation sim);

    /// <summary>Called once the whole game is loaded and derived data (regions, rooms) is rebuilt.</summary>
    void AfterLoad(Simulation sim) { }
}

/// <summary>
/// Binary writer for save games. Defs are written by name the first time they appear and by a
/// small number after that, so a file survives defs being reordered (but not renamed).
/// </summary>
public sealed class SaveWriter(Stream stream, DefDatabase defs) : IDisposable
{
    public DefDatabase Defs => defs;

    private readonly BinaryWriter _w = new(stream, Encoding.UTF8, leaveOpen: true);
    private readonly Dictionary<Def, int> _defs = new(ReferenceEqualityComparer.Instance);

    public void Int(int v) => _w.Write(v);
    public void UInt(uint v) => _w.Write(v);
    public void Long(long v) => _w.Write(v);
    public void ULong(ulong v) => _w.Write(v);
    public void Float(float v) => _w.Write(v);
    public void Bool(bool v) => _w.Write(v);
    public void Byte(byte v) => _w.Write(v);
    public void String(string v) => _w.Write(v);

    public void NullableString(string? v)
    {
        Bool(v != null);
        if (v != null)
            String(v);
    }

    public void Int3(int3 v)
    {
        Int(v.X);
        Int(v.Y);
        Int(v.Z);
    }

    public void Float3(float3 v)
    {
        Float(v.X);
        Float(v.Y);
        Float(v.Z);
    }

    public void Entity(Entity e)
    {
        Int(e.Index);
        Int(e.Generation);
    }

    public void Def(Def? def)
    {
        if (def == null)
        {
            Int(-1);
            return;
        }
        if (_defs.TryGetValue(def, out var id))
        {
            Int(id);
            return;
        }
        id = _defs.Count;
        _defs[def] = id;
        Int(id);
        String(def.Name);
    }

    /// <summary>Raw values, native byte order (every platform the game runs on is little-endian).</summary>
    public void Raw<T>(ReadOnlySpan<T> values) where T : unmanaged => _w.Write(MemoryMarshal.AsBytes(values));

    public void Ints(ReadOnlySpan<int> values)
    {
        Int(values.Length);
        Raw(values);
    }

    /// <summary>A length-prefixed chunk, so a reader that doesn't know it can skip it.</summary>
    public void Chunk(Action<SaveWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var inner = new SaveWriter(buffer, defs))
            write(inner);
        Int((int)buffer.Length);
        _w.Write(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    public void Dispose() => _w.Dispose();
}

/// <summary>Reads what <see cref="SaveWriter"/> wrote, resolving def names against the loaded defs.</summary>
public sealed class SaveReader(Stream stream, DefDatabase defs, int version = SaveGame.FormatVersion) : IDisposable
{
    /// <summary>The file's format version, for codecs that read older layouts.</summary>
    public int Version => version;

    private readonly BinaryReader _r = new(stream, Encoding.UTF8, leaveOpen: true);
    private readonly List<Def> _defs = [];

    public DefDatabase Defs => defs;

    /// <summary>The entity whose component is being read (component codecs that need it).</summary>
    internal Entity CurrentEntity { get; set; } = Ecs.Entity.Null;

    /// <summary>Creatures that were in the middle of a job when saved (their jobs are dropped on load).</summary>
    internal List<Entity> PawnsWithJobs { get; } = [];

    public int Int() => _r.ReadInt32();
    public uint UInt() => _r.ReadUInt32();
    public long Long() => _r.ReadInt64();
    public ulong ULong() => _r.ReadUInt64();
    public float Float() => _r.ReadSingle();
    public bool Bool() => _r.ReadBoolean();
    public byte Byte() => _r.ReadByte();
    public string String() => _r.ReadString();
    public string? NullableString() => Bool() ? String() : null;
    public int3 Int3() => new(Int(), Int(), Int());
    public float3 Float3() => new(Float(), Float(), Float());
    public Entity Entity() => new(Int(), Int());

    /// <summary>A count read from the file, checked so a corrupt one fails cleanly instead of allocating gigabytes.</summary>
    public int Count(int max = 1 << 26)
    {
        var n = Int();
        if (n < 0 || n > max)
            throw new SaveException($"Corrupt save: bad count {n}");
        return n;
    }

    public T Def<T>() where T : Def =>
        DefOrNull<T>() ?? throw new SaveException($"Corrupt save: missing {typeof(T).Name}");

    public T? DefOrNull<T>() where T : Def
    {
        var id = Int();
        if (id < 0)
            return null;
        if (id < _defs.Count)
            return _defs[id] as T ?? throw new SaveException($"Corrupt save: def #{id} is a {_defs[id].GetType().Name}, expected {typeof(T).Name}");
        if (id != _defs.Count)
            throw new SaveException($"Corrupt save: def #{id} out of order");
        var def = Resolve<T>(String());
        _defs.Add(def);
        return def;
    }

    private T Resolve<T>(string name) where T : Def
    {
        try
        {
            Def def = typeof(T) switch
            {
                var t when t == typeof(ThingDef) => defs.Thing(name),
                var t when t == typeof(BlockDef) => defs.Block(name),
                var t when t == typeof(CreatureDef) => defs.Creature(name),
                var t when t == typeof(MaterialDef) => defs.Material(name),
                var t when t == typeof(BodyDef) => defs.Body(name),
                var t when t == typeof(NeedDef) => defs.Need(name),
                var t when t == typeof(DifficultyDef) => defs.Difficulty(name),
                var t when t == typeof(RaiderKindDef) => defs.RaiderKind(name),
                var t when t == typeof(ThoughtDef) => defs.Thought(name),
                var t when t == typeof(InteractionDef) => defs.Interaction(name),
                var t when t == typeof(TraitDef) => defs.Trait(name),
                var t when t == typeof(RecipeDef) => defs.Recipe(name),
                var t when t == typeof(BuildingDef) => defs.Building(name),
                var t when t == typeof(TradeDef) => defs.Trade(name),
                var t when t == typeof(AdventurerKindDef) => defs.AdventurerKind(name),
                var t when t == typeof(PartyDef) => defs.Party(name),
                var t when t == typeof(ErrandKindDef) => defs.ErrandKind(name),
                _ => throw new SaveException($"Defs of type {typeof(T).Name} can't be saved"),
            };
            return (T)def;
        }
        catch (Exception ex) when (ex is not SaveException)
        {
            throw new SaveException($"The save uses {typeof(T).Name.Replace("Def", "").ToLowerInvariant()} '{name}', which this version of the game doesn't have.", ex);
        }
    }

    public void Raw<T>(Span<T> into) where T : unmanaged => _r.BaseStream.ReadExactly(MemoryMarshal.AsBytes(into));

    public int[] Ints()
    {
        var values = new int[Count()];
        Raw<int>(values);
        return values;
    }

    /// <summary>Read a chunk written by <see cref="SaveWriter.Chunk"/>; null reads it only to skip it.</summary>
    public void Chunk(Action<SaveReader>? read)
    {
        var length = Count();
        var bytes = _r.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException();
        if (read == null)
            return;
        using var inner = new SaveReader(new MemoryStream(bytes), defs, version);
        read(inner);
    }

    public void Dispose() => _r.Dispose();
}
