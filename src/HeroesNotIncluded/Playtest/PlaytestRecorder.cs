using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Narsil;
using HeroesNotIncluded.Net;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Round;

namespace HeroesNotIncluded.Playtest;

/// <summary>
/// What a playtest round leaves behind for tuning, so the numbers don't live only in the reveal
/// and memory: a folder per session under the logs folder (<c>logs/rounds/&lt;stamp&gt;</c>) with
/// the message log, the crises and the link as they happen (streamed, so a crash still leaves
/// them), the summary at the reveal, and the testers' notes. Game-side and read-only: nothing here
/// touches the sim. Each peer writes its own.
/// </summary>
/// <remarks>
/// It writes every message, not only those this player witnessed. That's fine without traitors;
/// before M4 lets allegiances through, it must keep to what the player is entitled to until the reveal.
/// </remarks>
public sealed class PlaytestRecorder
{
    /// <summary>Where every session's folder goes.</summary>
    public static string RoundsDirectory => Path.Combine(BuildInfo.LogDirectory, "rounds");

    /// <summary>Session folders kept; the oldest are pruned when a new one is made.</summary>
    public const int Keep = 30;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _root;
    private readonly string _stamp;
    private readonly string _mode;
    private string? _folder;
    private bool _failed;

    private long _messagesSeen = -1;
    private readonly HashSet<ICrisis> _crisesSeen = [];
    private int _crisesEnded;
    private long _lastNetHour = -1;
    private int _shots;
    private bool _summaryWritten;

    /// <param name="mode">What's being played ("round", "practice", "arena", "village"): it names the folder.</param>
    /// <param name="root">Where session folders go; <see cref="RoundsDirectory"/> unless a test says otherwise.</param>
    public PlaytestRecorder(string mode, string? root = null)
    {
        _mode = mode;
        _root = root ?? RoundsDirectory;
        _stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    }

    /// <summary>This session's folder, or null till something's been written.</summary>
    public string? Folder => _folder;

    /// <summary>
    /// Once a frame: new messages, crises begun and ended, and the link's health each game hour.
    /// Only while a round is live or over: practice, the ring and the lobby have nothing to tune.
    /// </summary>
    public void Update(Simulation sim, CoopSession? coop)
    {
        if (sim.Round.Phase is RoundPhase.None or RoundPhase.Lobby || sim.Round.Practice)
            return;
        var log = sim.Messages;
        if (_messagesSeen < 0)
            _messagesSeen = log.Total - log.Messages.Count; // what's kept from before we arrived, too
        if (log.Total > _messagesSeen)
        {
            var sb = new StringBuilder();
            for (var i = log.IndexAfter(_messagesSeen); i < log.Messages.Count; i++)
            {
                var m = log.Messages[i];
                sb.Append(When(sim, m.Tick)).Append(' ').Append(m.Kind.ToString().ToLowerInvariant()).Append(": ");
                if (m.Headline != null)
                    sb.Append('[').Append(m.Headline).Append("] ");
                if (m.To is { } to && sim.World.IsAlive(to) && sim.World.TryGet<Sim.Components.Creature>(to, out var c))
                    sb.Append("(to ").Append(c.Name).Append(") ");
                sb.AppendLine(m.Text);
            }
            _messagesSeen = log.Total;
            Append("messages.log", sb.ToString());
        }

        foreach (var c in sim.Director.Active)
            if (_crisesSeen.Add(c))
                Append("crises.log", $"{When(sim, c.Started)} began: {c.Kind}\n");
        var ended = sim.Reckoning.Crises;
        for (; _crisesEnded < ended.Count; _crisesEnded++)
            Append("crises.log", $"{When(sim, sim.Clock.Tick)} ended: {ended[_crisesEnded].Kind}, {(ended[_crisesEnded].Met ? "met" : "unmet")}\n");

        var hour = sim.Clock.Tick / SimClock.TicksPerHour;
        if (coop != null && hour != _lastNetHour && sim.Round.Live)
        {
            _lastNetHour = hour;
            var sb = new StringBuilder(When(sim, sim.Clock.Tick));
            if (coop.ClientStats is { } c)
                sb.Append($" queued {c.Buffered} (keeps {c.Target}), jitter {c.JitterMs:F0} ms, ran dry {c.Starvations}x;");
            if (coop.Host is { } host)
                sb.Append($" desyncs {host.Desyncs};");
            foreach (var p in coop.Players)
                sb.Append($" {p.Name}{(p.IsHost ? " (host)" : "")} {p.PingMs} ms;");
            Append("net.log", sb.Append('\n').ToString());
        }
    }

    /// <summary>
    /// Once, at the reveal: the round as the reveal shows it, and what the tuning pass needs besides
    /// (the build, the setup, the crises in order, the festival). JSON for tools, Markdown to read.
    /// </summary>
    public void WriteSummary(Simulation sim, RoundSummary s, RoundSetup setup)
    {
        if (_summaryWritten)
            return;
        _summaryWritten = true;
        var festival = sim.Festival;
        var record = new RoundRecord(
            BuildInfo.Label, DateTime.Now.ToString("yyyy-MM-dd HH:mm"), setup,
            Log.File?.Path is { } path ? Path.GetFileName(path) : null,
            s,
            sim.Reckoning.Crises.Select(c => new CrisisRow(c.Kind.ToString(), c.Met)).ToList(),
            festival.Toasted.Count,
            festival.Games.Select(g => new GameRow(g.Game.Label, g.BestName, g.BestScore, g.Turns)).ToList(),
            new QuestTally(sim.Expeditions.Succeeded, sim.Expeditions.Failed, sim.Expeditions.Dead));
        Write("summary.json", JsonSerializer.Serialize(record, Json));
        Write("summary.md", Markdown(record));
    }

    /// <summary>Where the next note's screenshot goes (making the folder), or null if it can't be made.</summary>
    public string? NextShot() => EnsureFolder() is { } folder ? Path.Combine(folder, $"shot-{++_shots:D3}.png") : null;

    /// <summary>A tester's note, stamped with the game time (and the screenshot taken with it).</summary>
    public void Note(Simulation sim, string text, string? shot)
    {
        var line = $"- {When(sim, sim.Clock.Tick)} {text.Trim()}";
        if (shot != null)
            line += $" ({Path.GetFileName(shot)})";
        Append("notes.md", line + "\n");
    }

    /// <summary>"Day 2 14:30 (t 81234)": a tick as the town's clock read it.</summary>
    public static string When(Simulation sim, long tick)
    {
        var total = tick + sim.Clock.StartHour * (long)SimClock.TicksPerHour;
        var day = total / SimClock.TicksPerDay + 1;
        var minutes = total % SimClock.TicksPerDay * 60 / SimClock.TicksPerHour;
        return $"Day {day} {minutes / 60:D2}:{minutes % 60:D2} (t {tick})";
    }

    private static string Markdown(RoundRecord r)
    {
        var s = r.Summary;
        var sb = new StringBuilder();
        sb.AppendLine($"# {s.Headline}").AppendLine();
        sb.AppendLine($"{r.Build}, {r.Played}. {r.Setup.Mode}, {r.Setup.Players} player(s), map {r.Setup.Map ?? "?"}, difficulty {r.Setup.Difficulty ?? "?"}, seed {r.Setup.Seed}, friendly fire {(r.Setup.FriendlyFire ? "on" : "off")}.");
        if (r.SessionLog != null)
            sb.AppendLine($"Session log: `{r.SessionLog}`.");
        sb.AppendLine();
        sb.AppendLine($"- **Outcome:** {s.Outcome}, ended day {s.EndedTick / SimClock.TicksPerDay + 1}");
        sb.AppendLine($"- **Renown** {s.Renown:F1} of {s.RenownTarget:F0}; **Dread** {s.Dread:F1} of {s.DreadLimit:F0}");
        sb.AppendLine($"- **Parties** {s.Parties}, served {s.AdventurersServed}; quests cleared {s.QuestsCleared}, failed {s.QuestsFailed}; adventurers lost {s.AdventurersLost}");
        sb.AppendLine($"- **Crises:** {(r.Crises.Count == 0 ? "none" : string.Join(", ", r.Crises.Select(c => $"{c.Kind} ({(c.Met ? "met" : "unmet")})")))}");
        sb.AppendLine($"- **Festival:** {r.Toasted} adventurers toasted; {string.Join(", ", r.Games.Select(g => $"{g.Game}: {(g.Best.Length > 0 ? $"{g.Best} {g.Score}" : "nobody")}, {g.Turns} turns"))}");
        sb.AppendLine().AppendLine("| Hour | Renown | Dread |").AppendLine("|---:|---:|---:|");
        for (var h = 0; h < Math.Max(s.RenownByHour.Count, s.DreadByHour.Count); h++)
            sb.AppendLine($"| {h} | {At(s.RenownByHour, h)} | {At(s.DreadByHour, h)} |");
        sb.AppendLine().AppendLine("| Who | Trade | Player | Earned | Fate |").AppendLine("|---|---|---|---:|---|");
        foreach (var p in s.People)
        {
            var fate = !p.Alive ? $"died day {p.DiedOnDay}" : p.CameBack ? "came back" : "";
            if (p.Errands > 0)
                fate += $" {p.Errands} errand(s)";
            sb.AppendLine($"| {p.Name} | {p.Trade} | {(p.Player ? "yes" : "")} | {p.Earned} | {fate.Trim()} |");
        }
        return sb.ToString();

        static string At(List<float> v, int i) => i < v.Count ? v[i].ToString("F1") : "";
    }

    private string? EnsureFolder()
    {
        if (_folder != null || _failed)
            return _folder;
        try
        {
            Directory.CreateDirectory(_root);
            Prune(_root);
            var folder = Path.Combine(_root, $"{_stamp}-{_mode}");
            Directory.CreateDirectory(folder);
            _folder = folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _failed = true;
            Log.Engine.Warning($"playtest: couldn't make a folder under {_root}: {ex.Message}");
        }
        return _folder;
    }

    private static void Prune(string root)
    {
        var old = Directory.GetDirectories(root).Order(StringComparer.Ordinal).ToList();
        for (var i = 0; i < old.Count - (Keep - 1); i++)
            Directory.Delete(old[i], recursive: true);
    }

    private void Append(string file, string text) => Do(file, path => File.AppendAllText(path, text));

    private void Write(string file, string text) => Do(file, path => File.WriteAllText(path, text));

    private void Do(string file, Action<string> write)
    {
        if (EnsureFolder() is not { } folder)
            return;
        try
        {
            write(Path.Combine(folder, file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _failed = true; // one warning, not one a frame
            _folder = null;
            Log.Engine.Warning($"playtest: couldn't write {file}: {ex.Message}");
        }
    }
}

/// <summary>How a recorded round was set up, from the game's side (the sim doesn't know its map's name).</summary>
public sealed record RoundSetup(string Mode, ulong Seed, string? Map, string? Difficulty, bool FriendlyFire, int Players);

public sealed record CrisisRow(string Kind, bool Met);

public sealed record GameRow(string Game, string Best, int Score, int Turns);

public sealed record QuestTally(int Succeeded, int Failed, int Dead);

/// <summary>Everything <c>summary.json</c> holds.</summary>
public sealed record RoundRecord(
    string Build, string Played, RoundSetup Setup, string? SessionLog, RoundSummary Summary,
    List<CrisisRow> Crises, int Toasted, List<GameRow> Games, QuestTally Quests);
