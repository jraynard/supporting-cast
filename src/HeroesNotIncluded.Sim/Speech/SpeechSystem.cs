using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;

namespace HeroesNotIncluded.Sim.Speech;

/// <summary>How loudly something is said.</summary>
public enum Volume : byte
{
    Whisper,
    Say,
    Shout,
}

/// <summary>How well a listener made it out.</summary>
public enum Clarity : byte
{
    Clear,
    Muffled,
}

/// <summary>One person's hearing of an utterance. <see cref="Identified"/>: they could tell whose voice it was.</summary>
public readonly record struct Hearing(Entity Listener, Clarity Clarity, bool Identified);

/// <summary>Something said aloud, with who caught it, worked out once when it was spoken.</summary>
public sealed class Utterance
{
    public long Id { get; init; }
    public long Tick { get; init; }
    public Entity Speaker { get; init; }
    public string SpeakerName { get; init; } = "";
    public Volume Volume { get; init; }
    public string Text { get; init; } = "";
    public int3 At { get; init; }
    public List<Hearing> Listeners { get; init; } = [];
}

/// <summary>
/// In-world speech (docs/DESIGN.md, "Speech is sim state"). Who hears what is decided here, from
/// positions and rooms only, on every peer alike. It must never read light or anything else a
/// peer might not have recomputed after loading a snapshot, because the result is saved state.
/// </summary>
public sealed class SpeechSystem : ISimSystem, ISaveState
{
    /// <summary>Longest line accepted.</summary>
    public const int MaxLength = 300;

    /// <summary>Utterances kept: enough for a scrollback, small enough for every join snapshot.</summary>
    public const int MaxKept = 200;

    /// <summary>Lines one speaker may say per <see cref="RateWindowTicks"/>.</summary>
    public const int RateLimit = 5;

    public const int RateWindowTicks = SimClock.TicksPerSecond * 5;

    public const float WhisperRange = 1.5f, SayRange = 10f, ShoutRange = 28f;

    /// <summary>How far a spoken voice carries through a wall or shut door.</summary>
    public const float MuffledSayRange = 5f;

    private readonly List<Utterance> _kept = [];
    private long _nextId = 1;

    public string Name => "Speech";
    public int Interval => SimClock.TicksPerDay;

    /// <summary>Recent utterances, oldest first.</summary>
    public IReadOnlyList<Utterance> Kept => _kept;

    public void Update(Simulation sim) { }

    /// <summary>Text from the chat box: <c>/w</c> whispers, <c>/s</c> shouts, anything else is said.</summary>
    public static (Volume Volume, string Text) Parse(string input)
    {
        var s = (input ?? "").TrimStart();
        if (s.StartsWith("/w ", StringComparison.OrdinalIgnoreCase))
            return (Volume.Whisper, s[3..]);
        if (s.StartsWith("/s ", StringComparison.OrdinalIgnoreCase))
            return (Volume.Shout, s[3..]);
        return (Volume.Say, s);
    }

    /// <summary>Say something. Null if they can't speak, it's empty, or they've said too much too fast.</summary>
    public Utterance? Say(Simulation sim, Entity speaker, string? text, Volume volume)
    {
        var w = sim.World;
        if (!Enum.IsDefined(volume) || !CanAct(sim, speaker))
            return null;
        var line = Clean(text);
        if (line.Length == 0)
            return null;
        var since = sim.Clock.Tick - RateWindowTicks;
        if (_kept.Count(u => u.Speaker == speaker && u.Tick > since) >= RateLimit)
            return null;

        var at = w.Get<Position>(speaker).Cell;
        // Drink makes a voice carry.
        var reach = volume == Volume.Say && Hospitality.DrinkingSystem.LevelOf(sim, speaker) >= Hospitality.DrinkingSystem.LoudAt ? Hospitality.DrinkingSystem.LoudReach : 1f;
        var utterance = new Utterance
        {
            Id = _nextId++, Tick = sim.Clock.Tick, Speaker = speaker, SpeakerName = w.Get<Creature>(speaker).Name,
            Volume = volume, Text = line, At = at,
        };
        foreach (var e in w.Query<Creature>())
            if (e == speaker || (CanAct(sim, e) && w.Get<Creature>(e).Def.Race == Race.Human))
                if (Hear(sim, at, w.Get<Position>(e).Cell, volume, reach) is { } clarity)
                    utterance.Listeners.Add(new Hearing(e, clarity, e == speaker || Recognised(at, w.Get<Position>(e).Cell, clarity)));

        _kept.Add(utterance);
        if (_kept.Count > MaxKept)
            _kept.RemoveRange(0, _kept.Count - MaxKept);
        return utterance;
    }

    /// <summary>How well someone at <paramref name="to"/> catches a voice from <paramref name="from"/>, or null if they don't.</summary>
    public static Clarity? Hear(Simulation sim, int3 from, int3 to, Volume volume, float reach = 1f)
    {
        var distance = Distance(from, to) / reach;
        var together = SameSpace(sim, from, to);
        return volume switch
        {
            Volume.Whisper => together && distance <= WhisperRange ? Clarity.Clear : null,
            Volume.Say => together && distance <= SayRange ? Clarity.Clear : distance <= MuffledSayRange ? Clarity.Muffled : null,
            Volume.Shout => distance > ShoutRange ? null : together ? Clarity.Clear : Clarity.Muffled,
            _ => null,
        };
    }

    /// <summary>What a listener makes of a line: the words, with gaps when muffled.</summary>
    public static string TextFor(Utterance u, Hearing h)
    {
        if (h.Clarity == Clarity.Clear)
            return u.Text;
        var words = u.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
            // Per listener, so two people behind a wall catch different words. A hash, not
            // sim.Rand: this is called while drawing.
            if ((SimRandom.Hash((int)u.Id, i * 31 + h.Listener.Index, 0x5EEC) & 3) != 0)
                words[i] = "...";
        return string.Join(' ', words);
    }

    private static bool Recognised(int3 from, int3 to, Clarity clarity) => clarity == Clarity.Clear && Distance(from, to) <= SayRange;

    private static float Distance(int3 a, int3 b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Awake, conscious and on the map: able to speak and to hear.</summary>
    public static bool CanAct(Simulation sim, Entity e)
    {
        var w = sim.World;
        return w.IsAlive(e) && w.TryGet<Creature>(e, out var c) && !c.Dead && !c.Downed
            && w.Has<Position>(e) && sim.Map.InBounds(w.Get<Position>(e).Cell)
            && !(w.TryGet<Mind>(e, out var mind) && mind.Asleep);
    }

    /// <summary>Text as it would be said: control characters out, trimmed, at most <see cref="MaxLength"/>.</summary>
    public static string Clean(string? text)
    {
        var s = new string((text ?? "").Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return s.Length > MaxLength ? s[..MaxLength].Trim() : s;
    }

    /// <summary>
    /// Are two cells in one open space? Outdoors counts as a single space whatever the level. A
    /// doorway belongs to no room (doors divide them), so it counts as part of those on both sides.
    /// </summary>
    private static bool SameSpace(Simulation sim, int3 a, int3 b)
    {
        Span<int> roomsA = stackalloc int[4], roomsB = stackalloc int[4];
        var countA = RoomsAt(sim, a, roomsA, out var outdoorsA);
        var countB = RoomsAt(sim, b, roomsB, out var outdoorsB);
        if (outdoorsA && outdoorsB)
            return true;
        for (var i = 0; i < countA; i++)
            for (var j = 0; j < countB; j++)
                if (roomsA[i] == roomsB[j])
                    return true;
        return false;
    }

    private static int RoomsAt(Simulation sim, int3 cell, Span<int> rooms, out bool outdoors)
    {
        var map = sim.Map;
        outdoors = false;
        var count = 0;
        var own = map.RoomId[map.Idx(cell)];
        if (own > 0)
        {
            rooms[count++] = own;
            outdoors = !sim.Rooms.All[own - 1].Indoors;
            // At a serving hatch, you're heard (and hear) through it, as in a doorway.
            foreach (var d in Maps.Map.Neighbors4)
            {
                var n = Maps.Map.Offset(cell, d);
                if (!map.InBounds(n) || map.Structure[map.Idx(n)] is not { IsNull: false } s
                    || sim.World.Get<Components.Thing>(s).Def.Building is not { Hatch: true })
                    continue;
                var far = Maps.Map.Offset(n, d);
                if (!map.InBounds(far))
                    continue;
                var beyond = map.RoomId[map.Idx(far)];
                if (beyond > 0 && beyond != own && count < rooms.Length)
                    rooms[count++] = beyond;
            }
            return count;
        }
        foreach (var d in Maps.Map.Neighbors4)
        {
            var n = Maps.Map.Offset(cell, d);
            if (n.Z != cell.Z || !map.InBounds(n) || count == rooms.Length)
                continue;
            var id = map.RoomId[map.Idx(n)];
            if (id <= 0)
                continue;
            rooms[count++] = id;
            outdoors |= !sim.Rooms.All[id - 1].Indoors;
        }
        return count;
    }

    public void Save(SaveWriter w, Simulation sim)
    {
        w.Long(_nextId);
        w.Int(_kept.Count);
        foreach (var u in _kept)
        {
            w.Long(u.Id);
            w.Long(u.Tick);
            w.Entity(u.Speaker);
            w.String(u.SpeakerName);
            w.Int((int)u.Volume);
            w.String(u.Text);
            w.Int3(u.At);
            w.Int(u.Listeners.Count);
            foreach (var h in u.Listeners)
            {
                w.Entity(h.Listener);
                w.Int((int)h.Clarity);
                w.Bool(h.Identified);
            }
        }
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _nextId = r.Long();
        _kept.Clear();
        for (var n = r.Count(MaxKept * 2); n > 0; n--)
        {
            var u = new Utterance
            {
                Id = r.Long(), Tick = r.Long(), Speaker = r.Entity(), SpeakerName = r.String(),
                Volume = (Volume)r.Int(), Text = r.String(), At = r.Int3(),
            };
            for (var k = r.Count(100000); k > 0; k--)
                u.Listeners.Add(new Hearing(r.Entity(), (Clarity)r.Int(), r.Bool()));
            _kept.Add(u);
        }
    }
}

/// <summary>A character says something aloud.</summary>
public sealed record SayCommand(Entity Actor, string Text, Volume Volume) : ISimCommand, IActorCommand
{
    public void Apply(Simulation sim) => sim.Speech.Say(sim, Actor, Text, Volume);
}
