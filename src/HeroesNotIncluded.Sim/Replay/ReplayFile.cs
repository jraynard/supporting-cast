using System.IO.Compression;
using System.Text;

namespace HeroesNotIncluded.Sim.Replay;

/// <summary>
/// A recorded game (<see cref="ReplayRecorder"/>) as kept on disk. Commands are encoded by
/// <see cref="Core.CommandCodec"/>, whose tags hold only within one build, so a replay carries the
/// build it was made on and plays only on that one (<see cref="BuildTag"/>). Meta is the game's to
/// fill (the map, the date, who played): the sim never reads a clock.
/// </summary>
public sealed record ReplayFile(
    string BuildTag,
    Dictionary<string, string> Meta,
    long StartTick,
    long EndTick,
    byte[] Start,
    ReplayEvent[] Events,
    (long Tick, ulong Hash)[] Checks,
    ReplayMarker[] Markers)
{
    public const string Extension = ".hnireplay";

    private static ReadOnlySpan<byte> Magic => "HNIR"u8;
    private const int FormatVersion = 1;

    /// <summary>What a replay list shows, read without the rest.</summary>
    public sealed record Header(string BuildTag, Dictionary<string, string> Meta, long StartTick, long EndTick);

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteHeader(w, new Header(BuildTag, Meta, StartTick, EndTick));
        w.Write(Start.Length);
        w.Write(Start);
        w.Flush();
        using var body = new BrotliStream(stream, CompressionLevel.Optimal, leaveOpen: true);
        using var b = new BinaryWriter(body, Encoding.UTF8);
        b.Write(Events.Length);
        foreach (var e in Events)
        {
            b.Write(e.Tick);
            b.Write((byte)e.Kind);
            b.Write(e.Player.ToByteArray());
            b.Write(e.IsHost);
            b.Write(e.Command?.Length ?? -1);
            if (e.Command != null)
                b.Write(e.Command);
        }
        b.Write(Checks.Length);
        foreach (var (tick, hash) in Checks)
        {
            b.Write(tick);
            b.Write(hash);
        }
        b.Write(Markers.Length);
        foreach (var m in Markers)
        {
            b.Write(m.Tick);
            b.Write(m.Label);
        }
    }

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        Write(ms);
        return ms.ToArray();
    }

    /// <summary>Read a replay; <see cref="InvalidDataException"/> if it isn't one or is damaged.</summary>
    public static ReplayFile Read(Stream stream)
    {
        try
        {
            using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            var header = ReadHeader(r);
            var start = r.ReadBytes(Count(r.ReadInt32(), 1 << 30));
            using var body = new BrotliStream(stream, CompressionMode.Decompress, leaveOpen: true);
            using var b = new BinaryReader(body, Encoding.UTF8);
            var events = new ReplayEvent[Count(b.ReadInt32(), ReplayRecorder.MaxEvents)];
            for (var i = 0; i < events.Length; i++)
            {
                var tick = b.ReadInt64();
                var kind = (ReplayEventKind)b.ReadByte();
                if (kind > ReplayEventKind.Barrier)
                    throw new InvalidDataException($"unknown event {kind}");
                var player = new Guid(b.ReadBytes(16));
                var isHost = b.ReadBoolean();
                var length = b.ReadInt32();
                var command = length < 0 ? null : b.ReadBytes(Count(length, 1 << 20));
                events[i] = new ReplayEvent(tick, kind, player, isHost, command);
            }
            var checks = new (long, ulong)[Count(b.ReadInt32(), int.MaxValue)];
            for (var i = 0; i < checks.Length; i++)
                checks[i] = (b.ReadInt64(), b.ReadUInt64());
            var markers = new ReplayMarker[Count(b.ReadInt32(), 100_000)];
            for (var i = 0; i < markers.Length; i++)
                markers[i] = new ReplayMarker(b.ReadInt64(), b.ReadString());
            return new ReplayFile(header.BuildTag, header.Meta, header.StartTick, header.EndTick, start, events, checks, markers);
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or ArgumentException or OverflowException)
        {
            throw new InvalidDataException($"Not a readable replay: {e.Message}", e);
        }
    }

    public static ReplayFile Read(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes, writable: false);
        return Read(ms);
    }

    /// <summary>The header alone, for a list of replays.</summary>
    public static Header ReadHeader(Stream stream)
    {
        try
        {
            using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            return ReadHeader(r);
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or ArgumentException)
        {
            throw new InvalidDataException($"Not a readable replay: {e.Message}", e);
        }
    }

    private static void WriteHeader(BinaryWriter w, Header h)
    {
        w.Write(Magic);
        w.Write(FormatVersion);
        w.Write(h.BuildTag);
        w.Write(h.Meta.Count);
        foreach (var (k, v) in h.Meta.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            w.Write(k);
            w.Write(v);
        }
        w.Write(h.StartTick);
        w.Write(h.EndTick);
    }

    private static Header ReadHeader(BinaryReader r)
    {
        Span<byte> magic = stackalloc byte[4];
        r.BaseStream.ReadExactly(magic);
        if (!magic.SequenceEqual(Magic))
            throw new InvalidDataException("Not a Heroes Not Included replay.");
        var version = r.ReadInt32();
        if (version != FormatVersion)
            throw new InvalidDataException($"A replay from another version of the game (format {version}).");
        var build = r.ReadString();
        var meta = new Dictionary<string, string>();
        var n = Count(r.ReadInt32(), 1000);
        for (var i = 0; i < n; i++)
            meta[r.ReadString()] = r.ReadString();
        return new Header(build, meta, r.ReadInt64(), r.ReadInt64());
    }

    private static int Count(int n, int max) =>
        n >= 0 && n <= max ? n : throw new InvalidDataException($"a count of {n} is out of range");
}
