using System.IO.Compression;
using System.Text;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Saving;

/// <summary>What a save file says about itself, readable without loading the whole game (load menus).</summary>
public sealed record SaveHeader(
    int Version,
    ulong Seed,
    long Tick,
    string Time,
    int Width,
    int Height,
    int Depth,
    int Villagers,
    IReadOnlyDictionary<string, string> Meta);

/// <summary>
/// Saving and loading a whole <see cref="Simulation"/>. A file is a small uncompressed header
/// (<see cref="SaveHeader"/>) followed by a Brotli-compressed body: clock and random state, the
/// entity allocator, the map layers, every component store, the message log and
/// each system's own <see cref="ISaveState"/>. Derived data (path costs, regions, rooms) is rebuilt.
/// <para>
/// Jobs are coroutines and aren't saved: loading drops them and creatures re-plan, exactly as if
/// every job had been interrupted (<see cref="AiSystem.InterruptAll"/>). A loaded game therefore
/// continues tick for tick like the original after <c>InterruptAll</c>, which is what the tests check.
/// </para>
/// </summary>
public static class SaveGame
{
    /// <summary>Bump this and migrate in the codecs when a release has saves worth keeping. Format 1 is the fork from DragonWorld; 2 added the upper storey build phases; 3 forging a heat at a time; 4 the empath's runes, draw feels and touch; 5 charms; 6 meditation's labyrinth; 7 the tinker (door locks picked, contraptions and wagons on quests).
    public const int FormatVersion = 7;
    private static ReadOnlySpan<byte> Magic => "SCSV"u8;

    public static byte[] Write(Simulation sim, IReadOnlyDictionary<string, string>? meta = null) => Capture(sim, meta).Encode();

    /// <summary>
    /// The whole game for a co-op peer (a joiner, a resync, a desync dump).
    /// </summary>
    public static byte[] Snapshot(Simulation sim) => Capture(sim, null).Encode();

    public static void Write(Stream stream, Simulation sim, IReadOnlyDictionary<string, string>? meta = null) =>
        Capture(sim, meta).Encode(stream);

    /// <summary>
    /// A game serialized but not yet compressed. Capturing has to happen between ticks on the
    /// thread that owns the sim; <see cref="Encode()"/> only touches these bytes, so it can run
    /// on another thread while the game plays on (autosave), keeping the slow part off the frame.
    /// </summary>
    public sealed class Image(byte[] header, byte[] body)
    {
        public byte[] Encode()
        {
            using var stream = new MemoryStream(header.Length + body.Length / 4);
            Encode(stream);
            return stream.ToArray();
        }

        public void Encode(Stream stream)
        {
            stream.Write(header);
            using var compressed = new BrotliStream(stream, CompressionLevel.Fastest, leaveOpen: true);
            compressed.Write(body);
        }
    }

    /// <summary>Serialize <paramref name="sim"/> as it is now (see <see cref="Image"/>). Throws <see cref="SaveException"/> during combat.</summary>
    public static Image Capture(Simulation sim, IReadOnlyDictionary<string, string>? meta = null)
    {

        using var head = new MemoryStream();
        using (var header = new BinaryWriter(head, Encoding.UTF8, leaveOpen: true))
        {
            header.Write(Magic);
            header.Write(FormatVersion);
            WriteHeader(header, sim, meta ?? new Dictionary<string, string>());
        }
        using var raw = new MemoryStream(1 << 20);
        using (var w = new SaveWriter(raw, sim.Defs))
            WriteBody(w, sim);
        return new Image(head.ToArray(), raw.ToArray());
    }

    /// <summary>
    /// The same game on a different set of defs (def hot reload): saved and read back, so every def
    /// reference is looked up by name in <paramref name="defs"/>. Jobs are dropped as on any load.
    /// Throws <see cref="SaveException"/> during combat or if the world uses a def that's gone.
    /// </summary>
    public static Simulation Rebind(Simulation sim, DefDatabase defs) => Read(Write(sim), defs).Sim;

    public static SaveHeader ReadHeader(Stream stream)
    {
        try
        {
            using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            Span<byte> magic = stackalloc byte[4];
            stream.ReadExactly(magic);
            if (!magic.SequenceEqual(Magic))
                throw new SaveException("Not a SupportingCast save");
            var version = r.ReadInt32();
            if (version > FormatVersion)
                throw new SaveException($"This save is from a newer version of the game (format {version}, this game reads up to {FormatVersion}).");
            if (version < 1)
                throw new SaveException($"Corrupt save: format {version}");
            return ReadHeaderFields(r, version);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException)
        {
            throw new SaveException("The save file is truncated.", ex);
        }
    }

    public static (Simulation Sim, SaveHeader Header) Read(byte[] bytes, DefDatabase defs)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return Read(stream, defs);
    }

    public static (Simulation Sim, SaveHeader Header) Read(Stream stream, DefDatabase defs)
    {
        var header = ReadHeader(stream);
        try
        {
            using var body = new BrotliStream(stream, CompressionMode.Decompress, leaveOpen: true);
            using var r = new SaveReader(body, defs, header.Version);
            var sim = Simulation.CreateStandard(defs, header.Width, header.Height, header.Seed, header.Depth);
            ReadBody(r, sim);
            return (sim, header);
        }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or IOException)
        {
            throw new SaveException("The save file is damaged or truncated.", ex);
        }
    }

    // ---------------- Header ----------------

    private static void WriteHeader(BinaryWriter w, Simulation sim, IReadOnlyDictionary<string, string> meta)
    {
        w.Write(sim.Seed);
        w.Write(sim.Clock.Tick);
        w.Write(sim.Clock.TimeString());
        w.Write(sim.Map.Width);
        w.Write(sim.Map.Height);
        w.Write(sim.Map.Depth);
        var village = sim.Creatures().Where(e => sim.World.Get<Creature>(e).Faction == Faction.Village).ToList();
        w.Write(village.Count(e => sim.World.Get<Creature>(e).Def.Race == Race.Human));
        w.Write(meta.Count);
        foreach (var (key, value) in meta.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            w.Write(key);
            w.Write(value);
        }
    }

    private static SaveHeader ReadHeaderFields(BinaryReader r, int version)
    {
        var seed = r.ReadUInt64();
        var tick = r.ReadInt64();
        var time = r.ReadString();
        int width = r.ReadInt32(), height = r.ReadInt32(), depth = r.ReadInt32();
        if (width <= 0 || height <= 0 || depth <= 0 || (long)width * height * depth > 1 << 26)
            throw new SaveException($"Corrupt save: map size {width}x{height}x{depth}");
        var villagers = r.ReadInt32();
        var meta = new Dictionary<string, string>();
        for (int i = 0, n = Bounded(r.ReadInt32()); i < n; i++)
            meta[r.ReadString()] = r.ReadString();
        return new SaveHeader(version, seed, tick, time, width, height, depth, villagers, meta);
    }

    private static int Bounded(int n) => n is >= 0 and <= 10_000 ? n : throw new SaveException($"Corrupt save: bad count {n}");

    /// <summary>
    /// A fingerprint of everything a save holds: every component (through its codec, so a new
    /// component counts as soon as it can be saved), the RNGs, the entity allocator, each system's
    /// own state, and with <paramref name="withMap"/> the map layers and the message log. Unlike
    /// <see cref="Simulation.StateHash"/>'s hand-picked fields, nothing that a load would restore can
    /// differ unnoticed. Jobs aren't saved, so they aren't in it (compare after
    /// <see cref="AiSystem.InterruptAll"/>).
    /// </summary>
    public static ulong Hash(Simulation sim, bool withMap = true)
    {
        using var hash = new HashStream();
        using (var w = new SaveWriter(hash, sim.Defs))
            WriteState(w, sim, withMap);
        return hash.Value;
    }

    // ---------------- Body ----------------

    private static void WriteBody(SaveWriter w, Simulation sim) => WriteState(w, sim, withMap: true);

    /// <summary>The save body; <see cref="Hash"/> may leave out the bulky parts.</summary>
    private static void WriteState(SaveWriter w, Simulation sim, bool withMap)
    {
        w.Long(sim.Clock.Tick);
        w.Int(sim.Clock.StartHour);
        var (a, b, c, d) = sim.Rand.State;
        w.UInt(a); w.UInt(b); w.UInt(c); w.UInt(d);
        w.Float(sim.Climate.BaseTemperature);
        w.Float(sim.Climate.DailySwing);
        w.Float(sim.Climate.Offset);

        var (next, generations, alive, free) = sim.World.ExportAllocator();
        w.Int(next);
        w.Raw<int>(generations);
        w.Raw<bool>(alive);
        w.Ints(free);

        if (withMap)
            WriteMap(w, sim.Map);
        ComponentCodecs.WriteAll(w, sim.World);
        if (withMap)
            WriteMessages(w, sim.Messages);

        var states = sim.Systems.OfType<ISaveState>().ToList();
        w.Int(states.Count);
        foreach (var state in states)
        {
            w.String(((ISimSystem)state).Name);
            w.Chunk(chunk => state.Save(chunk, sim));
        }
    }

    private static void ReadBody(SaveReader r, Simulation sim)
    {
        sim.Clock.Tick = r.Long();
        sim.Clock.StartHour = r.Int();
        sim.Rand.State = (r.UInt(), r.UInt(), r.UInt(), r.UInt());
        sim.Climate.BaseTemperature = r.Float();
        sim.Climate.DailySwing = r.Float();
        sim.Climate.Offset = r.Float();

        var next = r.Count();
        var generations = new int[next];
        var alive = new bool[next];
        r.Raw<int>(generations);
        r.Raw<bool>(alive);
        sim.World.ImportAllocator(next, generations, alive, r.Ints());

        ReadMap(r, sim.Map);
        ComponentCodecs.ReadAll(r, sim.World);
        foreach (var e in sim.World.Query<Skills>())
            if (Array.IndexOf(sim.World.Get<Skills>(e).Level, -1) >= 0)
                SkillRules.RollAdded(sim.Seed, e, sim.World.Get<Skills>(e));
        // Buildings that became stations since the save was made (a campfire that now cooks).
        foreach (var e in sim.World.Query<Thing>())
            if (!sim.World.Has<Construction>(e))
                Crafting.EnsureBench(sim, e);
        // Structures whose def changed size since the save (beds became two cells long).
        sim.RefitFootprints();
        sim.PushItemsOffFurniture();
        ReadMessages(r, sim.Messages);

        var n = r.Count(1000);
        for (var i = 0; i < n; i++)
        {
            var name = r.String();
            // A system this version doesn't have any more: skip its state.
            var state = sim.Systems.FirstOrDefault(s => s.Name == name) as ISaveState;
            r.Chunk(state == null ? null : chunk => state.Load(chunk, sim));
        }

        sim.Map.MarkStructureChanged();
        sim.RebuildDerivedIfNeeded();
        foreach (var state in sim.Systems.OfType<ISaveState>())
            state.AfterLoad(sim);

        // Jobs weren't saved: leave the world as ending them would have (see AiSystem.InterruptAll).
        var busy = r.PawnsWithJobs.ToHashSet();
        foreach (var e in sim.World.Query<Mind>())
            if (busy.Contains(e))
                AiSystem.ReleasePawn(sim, e);
    }

    private static void WriteMap(SaveWriter w, Map map)
    {
        // Blocks as a palette plus one short per cell.
        var palette = new List<BlockDef>();
        var ids = new Dictionary<BlockDef, ushort>(ReferenceEqualityComparer.Instance);
        var cells = new ushort[map.Count];
        for (var i = 0; i < map.Count; i++)
        {
            var block = map.Block[i];
            if (!ids.TryGetValue(block, out var id))
            {
                id = (ushort)palette.Count;
                ids[block] = id;
                palette.Add(block);
            }
            cells[i] = id;
        }
        w.Int(palette.Count);
        foreach (var block in palette)
            w.Def(block);
        w.Raw<ushort>(cells);

        WriteEntityLayer(w, map.Floor);
        WriteEntityLayer(w, map.Structure);
        WriteEntityLayer(w, map.Item);
        w.Raw<bool>(map.HasFloor);
        w.Raw<float>(map.Temperature);
        w.Raw<float>(map.Fire);
        w.Raw<float>(map.Smoke);
        w.Raw<float>(map.Light);
        w.Raw(map.Designations);
        var zones = map.Zone;
        w.Int(map.ZonedCells.Count);
        foreach (var i in map.ZonedCells)
        {
            w.Int(i);
            w.Int(zones[i]);
        }
    }

    private static void ReadMap(SaveReader r, Map map)
    {
        var palette = new BlockDef[r.Count(ushort.MaxValue)];
        for (var i = 0; i < palette.Length; i++)
            palette[i] = r.Def<BlockDef>();
        var cells = new ushort[map.Count];
        r.Raw<ushort>(cells);
        for (var i = 0; i < map.Count; i++)
            map.Block[i] = cells[i] < palette.Length ? palette[cells[i]] : throw new SaveException($"Corrupt save: block {cells[i]}");

        ReadEntityLayer(r, map, map.Floor);
        ReadEntityLayer(r, map, map.Structure);
        ReadEntityLayer(r, map, map.Item);
        r.Raw<bool>(map.HasFloor);
        r.Raw<float>(map.Temperature);
        r.Raw<float>(map.Fire);
        r.Raw<float>(map.Smoke);
        r.Raw<float>(map.Light);
        var designations = new Designation[map.Count];
        r.Raw<Designation>(designations);
        for (var i = 0; i < map.Count; i++)
            if (designations[i] != Designation.None)
                map.SetDesignations(i, designations[i]);
        var zoned = r.Count(map.Count);
        for (var k = 0; k < zoned; k++)
        {
            var i = r.Int();
            var zone = r.Int();
            if ((uint)i >= (uint)map.Count)
                throw new SaveException($"Corrupt save: zone cell {i}");
            map.SetZone(i, zone);
        }
    }

    // Sparse: most cells hold nothing.
    private static void WriteEntityLayer(SaveWriter w, Entity[] layer)
    {
        var count = 0;
        foreach (var e in layer)
            if (!e.IsNull)
                count++;
        w.Int(count);
        for (var i = 0; i < layer.Length; i++)
            if (!layer[i].IsNull)
            {
                w.Int(i);
                w.Entity(layer[i]);
            }
    }

    private static void ReadEntityLayer(SaveReader r, Map map, Entity[] layer)
    {
        var count = r.Count(map.Count);
        for (var k = 0; k < count; k++)
        {
            var i = r.Int();
            if ((uint)i >= (uint)map.Count)
                throw new SaveException($"Corrupt save: cell {i}");
            layer[i] = r.Entity();
        }
    }

    private static void WriteMessages(SaveWriter w, MessageLog log)
    {
        w.Int(log.Messages.Count);
        foreach (var m in log.Messages)
        {
            w.Long(m.Tick);
            w.Int((int)m.Kind);
            w.String(m.Text);
            w.Bool(m.Location.HasValue);
            if (m.Location is { } at)
                w.Int3(at);
        }
        w.Int(log.Cooldowns.Count);
        foreach (var (key, tick) in log.Cooldowns.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            w.String(key);
            w.Long(tick);
        }
        w.Long(log.Total);
    }

    private static void ReadMessages(SaveReader r, MessageLog log)
    {
        var n = r.Count();
        var messages = new List<Message>(n);
        for (var i = 0; i < n; i++)
        {
            var tick = r.Long();
            var kind = (MessageKind)r.Int();
            var text = r.String();
            int3? at = r.Bool() ? r.Int3() : null;
            messages.Add(new Message(tick, kind, text, at));
        }
        log.Cooldowns.Clear();
        n = r.Count();
        for (var i = 0; i < n; i++)
            log.Cooldowns[r.String()] = r.Long();
        log.Restore(messages, r.Long()); // the running count came with format 11
    }
}
