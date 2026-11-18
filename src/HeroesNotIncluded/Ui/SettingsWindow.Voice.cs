using Brutal.ImGuiApi;
using HeroesNotIncluded.Voice;
using Narsil.Audio;
using Narsil.Speech;
using Narsil.Ui;

namespace HeroesNotIncluded.Ui;

// Speaking aloud: turning it on, fetching the speech model, the microphone, how long a line waits,
// and calibrating your whisper, word and shout. Everything is heard on this machine.
public sealed partial class SettingsWindow
{
    private static readonly float[] PreviewChoices = [0f, 1f, 2f, 3f, 5f, -1f];

    private readonly VoiceCalibrator _calibrator = new();
    private readonly Dictation _trial = new();
    private readonly List<float> _trialSamples = [];
    private bool _calibrating, _trialHeld;
    private IReadOnlyList<string>? _mics;
    private (string Text, VoiceLevel Level)? _trialHeard;

    private void LeaveVoiceTab()
    {
        if (_calibrating || _trialHeld)
            VoiceService.Mic?.Stop();
        _calibrating = false;
        _trialHeld = false;
        _trial.Discard();
        VoiceService.Calibrating = false;
        _mics = null;
    }

    /// <summary>The talk key in the draft's controls (they may be changed here before Apply), or null on a pad without one.</summary>
    private string? DraftKey(string id) =>
        Controls.UsingPad && _controls.Bindings(id).All(b => !b.IsPad) ? null : _controls.Glyph(id);

    private bool DraftPadTalk => _controls.Bindings(Act.Talk).Any(b => b.IsPad);

    private void VoiceTab()
    {
        // Scrolls within the screen, as the Controls tab does, so the footer stays on a Deck's.
        var screen = ImGui.GetIO().DisplaySize;
        float2? size = new float2(MathF.Min(S(640), screen.X - S(80)), Math.Clamp(screen.Y - S(330), S(160), S(480)));
        if (ImGui.BeginChild("voice"u8, in size, ImGuiChildFlags.None, ImGuiWindowFlags.None))
            VoiceTabRows();
        ImGui.EndChild();
    }

    private void VoiceTabRows()
    {
        var gap = new float2(1, S(4));
        ImGui.Dummy(in gap);
        var talk = DraftKey(Act.Talk) ?? "Talk";
        Row("Speak aloud", $"Hold {talk} and speak: your words become your character's line, whispered, said or shouted as you spoke them. Off by default.");
        var on = _draft.VoiceInput;
        if (ImGui.Checkbox("##voice"u8, ref on))
            _draft.VoiceInput = on;
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + S(450));
        ImGuiText.Colored(Theme.Dim, "Everything you say is made out on this machine. The sound never leaves it: only the words, as if you'd typed them.");
        ImGui.PopTextWrapPos();
        if (!_draft.VoiceInput)
        {
            LeaveVoiceTab();
            return;
        }
        VoiceService.Calibrating = true; // the microphone opens for the meter and calibration, before Apply too

        ImGui.Dummy(in gap);
        ModelRows();
        MicrophoneRow();

        // A hand-edited value joins the list rather than being lost.
        IReadOnlyList<float> previews = PreviewChoices.Contains(_draft.VoicePreviewSeconds) ? PreviewChoices : [.. PreviewChoices, _draft.VoicePreviewSeconds];
        var previewIndex = Math.Max(0, previews.ToList().IndexOf(_draft.VoicePreviewSeconds));
        Row("Before it's said", "How long your words show, with how loud they'll be said, before your character says them: time to change the volume or take them back.");
        if (Combo("##voice-preview", previews, ref previewIndex, v => v == 0f ? "Say it at once" : v < 0f ? "Wait for me" : $"{v:0.#} seconds"))
            _draft.VoicePreviewSeconds = previews[previewIndex];

        Row("Quieten the game", $"Turn the game down while you hold {talk}, so the microphone hears you and not your speakers.");
        var duck = _draft.DuckWhileTalking;
        if (ImGui.Checkbox("##voice-duck"u8, ref duck))
            _draft.DuckWhileTalking = duck;

        ImGui.Dummy(in gap);
        ImGui.Separator();
        ControlRows();
        ImGui.Dummy(in gap);
        ImGui.Separator();
        CalibrationRows();
        ImGui.Dummy(in gap);
        ImGui.Separator();
        TrialRows();

        if (Settings.Current.VoiceInput != _draft.VoiceInput || Settings.Current.VoiceModel != _draft.VoiceModel || Settings.Current.MicDevice != _draft.MicDevice)
        {
            ImGui.Dummy(in gap);
            ImGuiText.Colored(Theme.Warn, "Press Apply to use these.");
        }
    }

    /// <summary>Talk and its take-back, rebindable here as on the Controls tab.</summary>
    private void ControlRows()
    {
        ImGui.Text("Keys and buttons"u8);
        _bindings.DrawRows(_controls, ImGui.GetIO().DeltaTime, Act.Talk, Act.Unsay);
        _draft.Bindings = _controls.Overrides.Count == 0 ? null : _controls.Save();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + S(450));
        if (!DraftPadTalk)
            ImGuiText.Colored(Controls.UsingPad ? Theme.Warn : Theme.Dim,
                "No gamepad button is set for Talk: every one already does something. Click its Gamepad cell and press one to share, "
                + $"or on a Steam Deck put a back grip on the keyboard's {_controls.Glyph(Act.Talk, Narsil.Platform.InputDevice.KeyboardMouse)} in Steam Input "
                + "(Steam button, Controller Settings, Edit Layout, Back Grip Buttons). The game can't see the back grips itself.");
        ImGui.PopTextWrapPos();
    }

    private void ModelRows()
    {
        var models = SpeechModels.All;
        var index = Math.Max(0, models.ToList().FindIndex(m => m.Id == _draft.VoiceModel));
        Row("Speech model", "Quick runs well on a Steam Deck. Careful makes out more, especially names and mumbling, but takes about three times as long.");
        if (Combo("##voice-model", models, ref index, m => $"{(m == SpeechModels.BaseEnglish ? "Quick" : "Careful")} ({m.Megabytes:F0} MB){(VoiceService.ModelPresent(m) ? "" : ", not downloaded")}"))
            _draft.VoiceModel = models[index].Id;
        var model = models[index];
        if (VoiceService.Download is { } d)
        {
            Row("");
            VoiceUi.Bar(d.Progress, Theme.Good, S(200), 10);
            ImGui.SameLine();
            ImGui.Text($"{d.Progress * 100:F0}% of {d.Model.Megabytes:F0} MB");
            ImGui.SameLine();
            if (ImGui.SmallButton("Stop"u8))
                VoiceService.CancelDownload();
        }
        else if (!VoiceService.ModelPresent(model))
        {
            Row("", "Fetched once, from Hugging Face (the whisper.cpp project's models), into your settings folder.");
            if (ImGui.Button($"Download it ({model.Megabytes:F0} MB)"))
                VoiceService.StartDownload(model);
        }
        if (VoiceService.Problem is { } problem)
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + S(450));
            ImGuiText.Colored(Theme.Bad, problem);
            ImGui.PopTextWrapPos();
            if (ImGui.SmallButton("Try again"u8))
                VoiceService.Retry();
        }
    }

    private void MicrophoneRow()
    {
        _mics ??= MicCapture.Devices();
        IReadOnlyList<string?> choices = [null, .. _mics];
        var index = Math.Max(0, choices.ToList().IndexOf(_draft.MicDevice));
        Row("Microphone", "Which microphone to listen to.");
        if (Combo("##voice-mic", choices, ref index, m => m ?? $"The system's ({MicCapture.DefaultDevice() ?? "none found"})"))
            _draft.MicDevice = choices[index];
        if (_mics.Count == 0)
        {
            ImGuiText.Colored(Theme.Warn, "No microphone found.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Look again"u8))
            {
                _mics = null;
                VoiceService.Retry();
            }
        }
    }

    private void CalibrationRows()
    {
        var current = _draft.VoiceCalibration;
        ImGui.Text("Your voice"u8);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + S(450));
        ImGuiText.Colored(Theme.Dim, current == null
            ? "Not set: whispers and shouts are judged against a typical voice. Read three lines so the game knows yours."
            : $"Set: a whisper about {current.QuietDb:F0} dB, talking {current.NormalDb:F0} dB, a shout {current.LoudDb:F0} dB.");
        if (current is { Flattened: true })
            ImGuiText.Colored(Theme.Warn, "Your shout came out barely louder than your talking. The microphone may be levelling itself (automatic gain): turn that off in your system's sound settings for shouts to tell, or pick Shout on the card after speaking.");
        ImGui.PopTextWrapPos();

        var mic = VoiceService.Mic;
        if (!_calibrating)
        {
            ImGui.BeginDisabled(mic == null);
            if (ImGui.Button(current == null ? "Calibrate"u8 : "Calibrate again"u8))
            {
                _calibrator.Restart();
                _calibrating = true;
            }
            ImGui.EndDisabled();
            if (current != null)
            {
                ImGui.SameLine();
                if (ImGui.Button("Forget it"u8))
                    _draft.VoiceCalibration = null;
            }
            return;
        }

        if (_calibrator.Done)
        {
            _calibrating = false;
            if (_calibrator.Result is { } result)
                _draft.VoiceCalibration = result;
            return;
        }
        var (ask, line) = VoiceCalibrator.Steps[_calibrator.Step];
        ImGuiText.Colored(Theme.Text, $"{_calibrator.Step + 1} of {VoiceCalibrator.Steps.Length}. {ask}");
        ImGuiText.Colored(Theme.Accent, $"\"{line}\"");
        ImGui.Button(DraftKey(Act.Talk) is { } key ? $"Hold to speak [{key}]" : "Hold to speak");
        // The button held (click, tap, or A on it), or Talk itself, the pad's counted though the window has it.
        var held = ImGui.IsItemActive() || (!ImGui.GetIO().WantTextInput && !_bindings.Capturing && _controls.DownRaw(Act.Talk));
        ImGui.SameLine();
        if (ImGui.SmallButton("Stop"u8))
        {
            _calibrating = false;
            mic?.Stop();
            return;
        }
        _calibrator.Update(held, mic);
        VoiceUi.Meter(_calibrator.LevelDb, null, S(450));
        if (_calibrator.Note is { } note)
            ImGuiText.Colored(Theme.Warn, note);
    }

    /// <summary>Hold and speak to see what it makes of you, with the settings in use (Apply first).</summary>
    private void TrialRows()
    {
        ImGui.Text("Try it"u8);
        var status = VoiceService.Status(Settings.Current);
        if (status != VoiceStatus.Ready)
        {
            ImGuiText.Colored(Theme.Dim, status switch
            {
                VoiceStatus.Off or VoiceStatus.NeedsModel => "Download the model and press Apply, then try it here.",
                VoiceStatus.Downloading => "Once the model's downloaded.",
                VoiceStatus.Loading => "The model's loading...",
                VoiceStatus.NoMicrophone => "No microphone.",
                _ => "It isn't working: see above.",
            });
            return;
        }
        var mic = VoiceService.Mic;
        ImGui.BeginDisabled(_calibrating || mic == null);
        ImGui.Button(DraftKey(Act.Talk) is { } key ? $"Hold and say something [{key}]" : "Hold and say something");
        var held = !_calibrating && (ImGui.IsItemActive() || (!ImGui.GetIO().WantTextInput && !_bindings.Capturing && _controls.DownRaw(Act.Talk)));
        ImGui.EndDisabled();
        if (held && !_trialHeld && mic != null)
        {
            mic.Start();
            _trial.Begin();
            _trialHeard = null;
        }
        if (_trialHeld && mic != null)
        {
            _trialSamples.Clear();
            mic.ReadAll(_trialSamples);
            _trial.Feed(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_trialSamples));
            if (!held)
            {
                mic.Stop();
                _trial.End(VoiceService.Recognizer, "", _draft.VoiceCalibration ?? VoiceCalibration.Default, -1f);
            }
        }
        _trialHeld = held && mic != null;
        _trial.Update(ImGui.GetIO().DeltaTime);
        if (_trial.State == DictationState.Preview && _trial.Pending is { } p)
        {
            _trialHeard = (p.Text, p.Level);
            _trial.Discard();
        }
        if (_trial.State == DictationState.Listening)
            VoiceUi.Meter(_trial.LevelDb, _draft.VoiceCalibration, S(450));
        else if (_trial.State == DictationState.Transcribing)
            ImGuiText.Colored(Theme.Dim, "Making it out...");
        else if (_trialHeard is { } heard)
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + S(450));
            ImGuiText.Colored(VoiceUi.Colour(heard.Level), $"{VoiceUi.Word(heard.Level)}: \"{heard.Text}\"");
            ImGui.PopTextWrapPos();
            if (_trial.LastTook is { } took)
                ImGuiText.Colored(Theme.Dim, $"Made out in {took.TotalSeconds:F1} s.");
        }
        else if (_trial.Note is { } note)
            ImGuiText.Colored(Theme.Dim, note);
    }
}
