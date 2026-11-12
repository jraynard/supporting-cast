namespace HeroesNotIncluded.Sim.Core;

public enum MessageKind
{
    Info,
    Good,
    Warning,
    Danger,
}

public readonly record struct Message(long Tick, MessageKind Kind, string Text, int3? Location);

/// <summary>
/// Player-facing event log ("The egg is getting cold!"). Keeps the newest <see cref="MaxKept"/>:
/// the log goes into every save and every co-op join snapshot. Code that wants "what's new" counts
/// by <see cref="Total"/>, not by index, since old messages drop off the front.
/// </summary>
public sealed class MessageLog
{
    private readonly List<Message> _messages = [];
    private readonly Dictionary<string, long> _cooldowns = [];

    /// <summary>Messages kept; older ones are dropped (in batches of <see cref="TrimBatch"/>).</summary>
    public const int MaxKept = 500, TrimBatch = 100;

    /// <summary>Throttle keys older than this are forgotten when the log trims (the longest throttle is 12 h).</summary>
    public const long CooldownKeptTicks = SimClock.TicksPerDay;

    /// <summary>The kept messages, oldest first.</summary>
    public IReadOnlyList<Message> Messages => _messages;

    /// <summary>Messages ever added, dropped ones included.</summary>
    public long Total { get; private set; }

    /// <summary>
    /// The kept messages added after the log's <see cref="Total"/> was <paramref name="seen"/>,
    /// as a start index into <see cref="Messages"/>.
    /// </summary>
    public int IndexAfter(long seen) => (int)Math.Clamp(_messages.Count - (Total - seen), 0, _messages.Count);

    internal Func<long> Now { get; set; } = () => 0;

    /// <summary>Last post per throttle key, for save games.</summary>
    internal Dictionary<string, long> Cooldowns => _cooldowns;

    /// <summary>Restore a saved log (save games).</summary>
    internal void Restore(IEnumerable<Message> messages, long? total = null)
    {
        _messages.Clear();
        _messages.AddRange(messages);
        Total = Math.Max(total ?? _messages.Count, _messages.Count);
    }

    public void Add(MessageKind kind, string text, int3? location = null)
    {
        _messages.Add(new Message(Now(), kind, text, location));
        Total++;
        if (_messages.Count < MaxKept + TrimBatch)
            return;
        _messages.RemoveRange(0, _messages.Count - MaxKept);
        var now = Now();
        foreach (var key in _cooldowns.Where(kv => now - kv.Value > CooldownKeptTicks).Select(kv => kv.Key).ToList())
            _cooldowns.Remove(key);
    }

    /// <summary>
    /// Add a message unless the same key was posted within <paramref name="cooldownTicks"/>.
    /// Use for conditions that persist (cold egg) so the log isn't spammed.
    /// </summary>
    public void AddThrottled(string key, int cooldownTicks, MessageKind kind, string text, int3? location = null)
    {
        var now = Now();
        if (_cooldowns.TryGetValue(key, out var last) && now - last < cooldownTicks)
            return;
        _cooldowns[key] = now;
        Add(kind, text, location);
    }
}
