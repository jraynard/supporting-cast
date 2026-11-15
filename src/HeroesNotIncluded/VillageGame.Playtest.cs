using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Playtest;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

/// <summary>
/// Playtesting from inside the game: the recorder that keeps each round's numbers for tuning
/// (<see cref="PlaytestRecorder"/>), and the note key, which takes a screenshot and asks for a line
/// about it, so a tester can flag a moment without leaving the round.
/// </summary>
public sealed partial class VillageGame
{
    private PlaytestRecorder? _recorder;
    private ImInputString? _noteInput;
    private string? _noteShot;
    private int _noteFrames;
    private string? _playtestError;

    private PlaytestRecorder Recorder => _recorder ??= new PlaytestRecorder(
        ArenaFoe != null ? "arena" : _sim.Round.Practice ? "practice" : _sim.Round.Phase != RoundPhase.None ? "round" : "village");

    /// <summary>Once a frame, after <see cref="RecordRound"/>: the round's log, and its summary at the reveal.</summary>
    private void UpdatePlaytest()
    {
        if (!Settings.Current.KeepPlaytestLogs)
            return;
        Recorder.Update(_sim, _coop);
        if (RoundOver && _sawRoundLive && _sim.Round.Summary is { } s && !_sim.Round.Practice)
            Recorder.WriteSummary(_sim, s, new RoundSetup(
                _coop == null ? "solo" : _coop.IsHost ? "host" : "client",
                _sim.Seed,
                _coop is { IsHost: false } ? null : MapName,
                _sim.Director.Difficulty?.Name,
                _sim.Round.Options.FriendlyFire,
                _coop?.Players.Count ?? 1));
    }

    /// <summary>The note key: a screenshot now, then a line about it (the box is left out of the shot).</summary>
    private void HandlePlaytestNoteKey()
    {
        if (_noteInput != null || ImGui.GetIO().WantTextInput || !Controls.Map.Pressed(Act.PlaytestNote))
            return;
        _noteShot = Recorder.NextShot();
        if (_noteShot != null)
            Host.Screenshot(_noteShot);
        _noteInput = new ImInputString(400, ""u8);
        _noteFrames = 0;
    }

    private void DrawPlaytestNote()
    {
        if (_noteInput == null || ++_noteFrames < 3) // the screenshot is taken without it
            return;
        var screen = ImGui.GetIO().DisplaySize;
        var pos = new float2(screen.X * 0.5f, screen.Y * 0.75f);
        float2? middle = new float2(0.5f, 0.5f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Appearing, in middle);
        Panel.Begin("Playtest note###playtest-note", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse);
        TextC(Theme.Dim, _noteShot != null ? "Screenshot taken. What happened here?" : "What happened here?");
        ImGui.SetNextItemWidth(Gui.S(360));
        if (_noteFrames == 3)
            ImGui.SetKeyboardFocusHere(0);
        var done = ImGui.InputTextWithHint("##note"u8, "Felt off, too hard, a bug, a good moment..."u8, _noteInput, ImGuiInputTextFlags.EnterReturnsTrue, null, IntPtr.Zero);
        OnScreenKeyboard.AfterTextField();
        done |= ImGui.Button("Keep it"u8, in _autoSize);
        ImGui.SameLine(0, Gui.S(8));
        var skip = ImGui.Button("Just the screenshot"u8, in _autoSize);
        if (done || skip)
        {
            var text = done ? ImGuiText.Value(_noteInput) : "";
            if (text.Trim().Length > 0 || _noteShot != null)
                Recorder.Note(_sim, text.Trim().Length > 0 ? text : "(screenshot)", _noteShot);
            Notify(Sim.Core.MessageKind.Info, Recorder.Folder != null ? "Noted for the playtest." : "Couldn't save the note (see the log).");
            _noteInput = null;
            _noteShot = null;
        }
        Panel.End();
    }

    /// <summary>On the reveal: where this round's playtest files went, for sending back.</summary>
    private void DrawPlaytestFolderButton()
    {
        if (_recorder?.Folder is not { } folder)
            return;
        ImGui.SameLine(0, Gui.S(8));
        if (ImGui.Button("Open playtest folder"u8, in _autoSize))
            _playtestError = SettingsWindow.OpenFolder(folder);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip($"This round's log, summary and notes, for the tuning pass: {folder}");
        if (_playtestError != null)
            TextC(Theme.Warn, _playtestError);
    }
}
