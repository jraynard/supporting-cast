using Brutal.ImGuiApi;
using Narsil;
using Narsil.Ui;
using HeroesNotIncluded.Replay;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Replay;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Net;

namespace HeroesNotIncluded;

// Recording each round for a replay (Sim.Replay): begun before a new sim's first tick, or straight
// after a load or a joiner's snapshot (when no job is running, which a snapshot needs), the crises
// and deaths marked as they come, and kept at the reveal for "Watch the replay". Nothing of it is
// shown before the round's over: a replay sees everything.
public sealed partial class VillageGame
{
    private Simulation? _replayFor;     // the sim instance recording was last set up for
    private readonly HashSet<ICrisis> _replayCrises = [];
    private readonly HashSet<Entity> _replayDead = [];
    private bool _replayFestival;
    private string? _replayPath;        // kept at the reveal
    private string? _replayWhyNot;      // why there's none to watch

    /// <summary>Once a frame, before anything this frame touches the sim: start recording a new sim, and mark what's happened.</summary>
    private void TrackReplay()
    {
        if (!ReferenceEquals(_replayFor, _sim))
        {
            _replayFor = _sim;
            _replayCrises.Clear();
            _replayDead.Clear();
            _replayFestival = false;
            if (_sim.Recorder == null && _sim.Round.Phase != RoundPhase.None && ArenaFoe == null && !Practice)
            {
                // Solo: make sure no job is running (a new sim or a load has none; --skip-hours
                // may). In co-op every peer's sim is at a snapshot point already, and stopping jobs
                // on one peer alone would split it from the rest.
                if (_coop == null)
                    AiSystem.InterruptAll(_sim);
                if (ReplayRecorder.Begin(_sim, out var why) == null)
                    _replayWhyNot = $"Recording couldn't start: {why}";
            }
        }
        if (_sim.Recorder is not { } recorder)
            return;
        foreach (var c in _sim.Director.Active)
            if (_replayCrises.Add(c))
                recorder.Mark(c.Started, CrisisLabel(c.Kind));
        if (_sim.Round.Phase == RoundPhase.Festival && !_replayFestival)
        {
            _replayFestival = true;
            recorder.Mark(_sim.Clock.Tick, "The festival");
        }
        if (_sim.Clock.Tick % 60 == 0)
            foreach (var e in _sim.World.Query<PlayerCharacter>())
                if (_sim.World.TryGet<Creature>(e, out var body) && body.Dead && _replayDead.Add(e))
                    recorder.Mark(_sim.Clock.Tick, $"{body.Name} dies");
        if (RoundOver && _replayPath == null && _replayWhyNot == null)
            KeepReplay(recorder);
        if (KeepReplayAt >= 0 && _time >= KeepReplayAt && _replayPath == null)
        {
            KeepReplay(recorder); // --keep-replay-at: once
            Notify(MessageKind.Info, _replayPath != null ? $"Replay kept: {Path.GetFileName(_replayPath)}" : _replayWhyNot ?? "No replay.");
        }
    }

    /// <summary>Debug (<c>--keep-replay-at N</c>): keep the replay so far after N seconds, round over or not.</summary>
    public float KeepReplayAt { get; init; } = -1f;

    /// <summary>The Debug window's "Keep a replay now" (dev only: a replay sees everything, secrets and all).</summary>
    private void DrawKeepReplayButton()
    {
        if (_sim.Recorder is not { } recorder)
        {
            ImGui.TextDisabled(_replayWhyNot ?? "Not recording (no round).");
            return;
        }
        if (ImGui.Button("Keep a replay now"u8))
        {
            _replayPath = null;
            KeepReplay(recorder);
            Notify(MessageKind.Info, _replayPath != null ? $"Replay kept: {Path.GetFileName(_replayPath)}" : _replayWhyNot ?? "No replay.");
        }
        ImGui.SameLine(0, Gui.S(8));
        ImGui.TextDisabled($"{recorder.Events.Count} events{(recorder.Spoiled is { } why ? $"; spoiled: {why}" : "")}");
    }

    private static string CrisisLabel(CrisisKind kind) => HeroesNotIncluded.Sim.Director.CrisisTypes.Label(kind);

    /// <summary>The round's over: write the recording to the replays folder.</summary>
    private void KeepReplay(ReplayRecorder recorder)
    {
        if (recorder.Spoiled is { } spoiled)
        {
            _replayWhyNot = $"No replay this time: {spoiled}.";
            return;
        }
        // The map's name is known only for a village started here, not a loaded or joined one.
        var mapName = Loaded == null && Coop == null ? MapName : null;
        var meta = new Dictionary<string, string>
        {
            ["date"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ["headline"] = _sim.Round.Summary?.Headline ?? "",
            ["players"] = string.Join(", ", _sim.Round.Summary?.People.Where(p => p.Player).Select(p => $"{p.Name} ({p.Trade})") ?? []),
        };
        if (mapName != null)
            meta["map"] = mapName;
        var file = recorder.ToFile(CoopSession.BuildTag(_sim.Defs), meta);
        var name = $"{DateTime.Now:yyyyMMdd-HHmmss}-{(mapName ?? "round").ToLowerInvariant()}";
        _replayPath = ReplayLibrary.Save(file, name);
        if (_replayPath == null)
            _replayWhyNot = "The replay couldn't be written (see the log).";
        else
            Log.Engine.Info($"replay: kept {_replayPath} ({file.Events.Length} events, {SimClock.TicksToDays(file.EndTick - file.StartTick) * 24:0.0} h)");
    }

    /// <summary>The reveal's replay button: watch it (leaving the game), or why there isn't one.</summary>
    private void DrawReplayButton()
    {
        if (_replayPath is { } path)
        {
            ImGui.SameLine(0, Gui.S(8));
            var label = _coop != null ? "Watch the replay (leaves the game)" : "Watch the replay";
            if (ImGui.Button(label))
            {
                try
                {
                    var file = ReplayLibrary.Load(path);
                    if (_coop != null)
                        LeaveCoop();
                    Host.SwitchTo(new Ui.LoadingScreen(() => new ReplayGame(file, path), error => new TitleGame { StartError = error }));
                }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    _replayWhyNot = $"The replay couldn't be read: {e.Message}";
                    _replayPath = null;
                }
            }
        }
        else if (_replayWhyNot is { } why)
            ImGuiText.Colored(Ui.Theme.Dim, why);
    }
}
