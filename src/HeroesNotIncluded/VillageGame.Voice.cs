using Brutal.ImGuiApi;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Ui;
using HeroesNotIncluded.Voice;
using Narsil.Speech;
using Narsil.Ui;

namespace HeroesNotIncluded;

// Speaking aloud (Voice/): hold the talk key and speak; the words are made out on this machine
// and said as your character's line, at the volume you spoke them. Only the words reach the sim,
// as the same SayCommand a typed line is, so who hears it is decided exactly as for typing.
public sealed partial class VillageGame
{
    private readonly Dictation _dictation = new();
    private readonly List<float> _micSamples = [];

    /// <summary>Someone to speak as, in a round, with speaking aloud on.</summary>
    private bool CanSpeakAloud => Settings.Current.VoiceInput && _sim.Round.Live && (HasAvatar || Ghosting);

    private Entity Speaker => HasAvatar ? _avatar : _ghost;

    /// <summary>
    /// <c>--voice-clip FILE</c> (dev): a recording spoken into the game as if through the
    /// microphone, once the model's loaded: the whole way from the meter to the line said, with
    /// no microphone (tests, screenshots, a machine without one). Turns speaking aloud on.
    /// </summary>
    public static string? VoiceClip { get; set; }

    private bool _voiceClipPlayed;

    private void PlayVoiceClip()
    {
        if (_voiceClipPlayed || VoiceClip == null || !_sim.Round.Live || (!HasAvatar && !Ghosting))
            return;
        Settings.Current.VoiceInput = true;
        if (VoiceService.Status(Settings.Current) != VoiceStatus.Ready)
            return;
        _voiceClipPlayed = true;
        float[] clip;
        try
        {
            clip = SpeechAudio.Load(VoiceClip);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            Narsil.Log.Engine.Warning($"--voice-clip: {ex.Message}");
            return;
        }
        var s = Settings.Current;
        _dictation.Begin();
        _dictation.Feed(clip);
        _dictation.End(VoiceService.Recognizer, VoicePrompt(), s.VoiceCalibration ?? VoiceCalibration.Default, s.VoicePreviewSeconds);
    }

    private void UpdateVoice(float dt)
    {
        var s = Settings.Current;
        var map = Controls.Map;
        var typing = ImGui.GetIO().WantTextInput;
        PlayVoiceClip();
        if (!CanSpeakAloud)
        {
            if (_dictation.State != DictationState.Idle)
                StopListening(discard: true);
            VoiceService.Ducking = false;
            return;
        }

        if (!typing && map.Pressed(Act.Talk))
        {
            // Again while a line waits: it's said now, and whatever comes next is listened for.
            if (_dictation.State == DictationState.Preview)
                SayLines(_dictation.SendNow());
            if (VoiceService.Status(s) is var status && status != VoiceStatus.Ready)
                _dictation.Tell(NotReady(status));
            else if (VoiceService.Mic is not { } mic)
                _dictation.Tell("No microphone found. Check Settings, Voice.");
            else
            {
                mic.Start();
                _dictation.Begin();
            }
        }

        if (_dictation.State == DictationState.Listening)
        {
            var mic = VoiceService.Mic;
            if (mic != null)
            {
                _micSamples.Clear();
                mic.ReadAll(_micSamples);
                _dictation.Feed(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_micSamples));
            }
            if (mic == null || !map.Down(Act.Talk) || _dictation.Full)
                StopListening(discard: false);
        }

        if (!typing && map.Pressed(Act.Unsay) && _dictation.State is DictationState.Preview or DictationState.Transcribing)
            _dictation.Discard();

        if (_dictation.Update(dt) is { } lines)
            SayLines(lines);
        VoiceService.Ducking = _dictation.State == DictationState.Listening && s.DuckWhileTalking;
    }

    private void StopListening(bool discard)
    {
        var mic = VoiceService.Mic;
        if (mic != null && !discard)
        {
            _micSamples.Clear();
            mic.ReadAll(_micSamples);
            _dictation.Feed(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_micSamples));
        }
        mic?.Stop();
        if (discard)
        {
            _dictation.Discard();
            return;
        }
        var s = Settings.Current;
        _dictation.End(VoiceService.Recognizer, VoicePrompt(), s.VoiceCalibration ?? VoiceCalibration.Default, s.VoicePreviewSeconds);
    }

    private static string NotReady(VoiceStatus status) => status switch
    {
        VoiceStatus.NeedsModel => "Speaking aloud needs its speech model: Settings, Voice.",
        VoiceStatus.Downloading => "The speech model is still downloading.",
        VoiceStatus.Loading => "One moment: the speech model is loading.",
        VoiceStatus.NoMicrophone => "No microphone found. Check Settings, Voice.",
        VoiceStatus.Failed => VoiceService.Problem ?? "The speech model wouldn't load.",
        _ => "Speaking aloud is off.",
    };

    private void SayLines(IReadOnlyList<(string Text, VoiceLevel Level)> lines)
    {
        if (!CanSpeakAloud)
            return;
        foreach (var (text, level) in lines)
            Issue(new SayCommand(Speaker, text, VoiceUi.ToVolume(level)));
    }

    /// <summary>
    /// Names the speaker may say, nearest first, to steer the recognizer's spelling: the people in
    /// sight (named over their heads already), whoever's been heard lately, and the shops' signs.
    /// All of it is what the player perceives, never more.
    /// </summary>
    private string VoicePrompt()
    {
        var w = _sim.World;
        var names = new List<string>();
        var here = w.IsAlive(Speaker) && w.TryGet<Position>(Speaker, out var me) ? me.Pos : default;
        names.AddRange(View.Creatures()
            .Where(e => w.Has<Creature>(e) && w.Has<Position>(e))
            .OrderBy(e => Distance(w.Get<Position>(e).Pos, here))
            .Select(e => w.Get<Creature>(e).Name));
        names.AddRange(View.Heard().Reverse().Take(30).Select(l => l.Who));
        foreach (var b in w.Query<PresetBuilding>())
            if (ShopName.Of(_sim, b) is { } shop)
                names.Add(shop);
        return Transcript.Prompt(names);

        static float Distance(float3 a, float3 b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
    }

    /// <summary>
    /// The card while you speak: a meter as you talk, a moment while it's made out, then the words
    /// with the volume they'll be said at (click one to change it), counting down to being said.
    /// </summary>
    private void DrawVoice()
    {
        if (!CanSpeakAloud || (_dictation.State == DictationState.Idle && _dictation.Note == null))
            return;
        _hud.Next(Anchor.Bottom);
        Panel.Begin("##voice", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDecoration);
        _hud.Took();
        var width = Gui.S(380);
        var (talk, unsay) = (VoiceUi.Key(Act.Talk), VoiceUi.Key(Act.Unsay));
        switch (_dictation.State)
        {
            case DictationState.Listening:
                TextC(Theme.Accent, Ghosting ? "Listening... (only the dead will hear)" : "Listening...");
                VoiceUi.Meter(_dictation.LevelDb, Settings.Current.VoiceCalibration, width);
                TextC(Theme.Dim, $"Let go {(talk == null ? "" : $"of {talk} ")}when you're done. {Dictation.MaxSeconds - _dictation.Seconds:F0} s left.");
                break;
            case DictationState.Transcribing:
                TextC(Theme.Dim, "Making out what you said...");
                if (ImGui.SmallButton(unsay == null ? "Never mind"u8 : $"Never mind [{unsay}]"))
                    _dictation.Discard();
                break;
            case DictationState.Preview when _dictation.Pending is { } p:
                DrawPendingLine(p, width, talk, unsay);
                break;
            default:
                if (_dictation.Note is { } note)
                    TextC(Theme.Dim, note);
                break;
        }
        Panel.End();
    }

    private void DrawPendingLine(PendingLine p, float width, string? talk, string? unsay)
    {
        if (Ghosting)
            TextC(Theme.AccentSoft, "Among the dead:");
        else
        {
            // The volume it'll be said at, any of the three a click (or a tap on a Deck's screen) away.
            foreach (var level in new[] { VoiceLevel.Quiet, VoiceLevel.Normal, VoiceLevel.Loud })
            {
                var on = p.Level == level;
                if (on)
                    ImGui.PushStyleColor(ImGuiCol.Button, Theme.OakLight);
                if (ImGui.SmallButton($"{VoiceUi.Word(level)}##voice-{level}"))
                    _dictation.SetLevel(level);
                if (on)
                    ImGui.PopStyleColor(1);
                ImGui.SameLine();
            }
            if (p.Confidence < 0.5f && !p.Changed)
                TextC(Theme.Dim, "(not sure how loud: check it)");
            else
                ImGui.NewLine();
        }
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGuiText.Colored(Ghosting ? Theme.AccentSoft : VoiceUi.Colour(p.Level), $"\"{p.Text}\"");
        ImGui.PopTextWrapPos();
        if (float.IsFinite(p.Left) && !p.Changed)
            VoiceUi.Bar(p.Left / Math.Max(0.01f, p.Wait), Theme.Dim, width, 4);
        if (ImGui.Button(talk == null ? "Say it" : $"Say it [{talk}]"))
            SayLines(_dictation.SendNow());
        ImGui.SameLine();
        var spoken = p.Level;
        if (ImGui.Button("Edit"u8) && _dictation.TakeForEditing() is { } text)
        {
            // Into the speech box as typed, its volume kept the way the box writes one.
            var prefix = Ghosting ? "" : spoken switch { VoiceLevel.Quiet => "/w ", VoiceLevel.Loud => "/s ", _ => "" };
            _speechInput.SetValue((prefix + text).AsSpan());
            _showHeard = true;
            _focusSpeech = true;
        }
        ImGui.SameLine();
        if (ImGui.Button(unsay == null ? "Take it back" : $"Take it back [{unsay}]"))
            _dictation.Discard();
    }
}
