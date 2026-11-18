using Narsil;
using Narsil.Speech;

namespace HeroesNotIncluded.Voice;

public enum DictationState
{
    Idle,
    Listening,
    Transcribing,
    Preview,
}

/// <summary>A line made out from speech, waiting to be said: shown with its volume, counting down.</summary>
public sealed class PendingLine(string text, VoiceLevel level, float confidence, float wait)
{
    public string Text { get; } = text;
    public VoiceLevel Level { get; set; } = level;

    /// <summary>How sure the volume reading was, 0..1: shown so a doubtful whisper can be checked.</summary>
    public float Confidence { get; } = confidence;

    /// <summary>Seconds it waits in all; infinite when it waits to be sent.</summary>
    public float Wait { get; } = wait;

    public float Left { get; set; } = wait;

    /// <summary>The player changed its volume: it's theirs now, and waits for them.</summary>
    public bool Changed { get; set; }
}

/// <summary>
/// Push to talk, one line at a time: listening while the key is held, transcribing once it's let
/// go (off the main thread), then the words shown with the volume they were spoken at for a
/// moment before they're said (<see cref="Settings.VoicePreviewSeconds"/>), unless they're sent,
/// changed or taken back first. The village feeds it the microphone and says what it gives back;
/// nothing here touches the sim, so a spoken line reaches it as an ordinary typed one would.
/// </summary>
public sealed class Dictation
{
    /// <summary>Whisper hears thirty seconds at a time: a clip stops a little short of that.</summary>
    public const float MaxSeconds = 28f;

    public const float NoteSeconds = 3f;

    private const int Rate = SpeechAudio.Rate;
    private const int MeterSamples = Rate / 16; // the meter follows the last 60 ms

    private readonly List<float> _clip = [];
    private Task<Transcription>? _transcribing;
    private CancellationTokenSource? _cancel;
    private VoiceReading _reading;
    private float _wait;
    private float _noteLeft;

    public DictationState State { get; private set; }
    public PendingLine? Pending { get; private set; }

    /// <summary>The microphone's level just now, in dBFS, while listening.</summary>
    public float LevelDb { get; private set; } = VoiceLoudness.FloorDb;

    public float Seconds => _clip.Count / (float)Rate;
    public bool Full => Seconds >= MaxSeconds;

    /// <summary>A word about the last try ("Nothing heard."), shown for a few seconds.</summary>
    public string? Note { get; private set; }

    /// <summary>How long the last transcription took, for the log and the settings.</summary>
    public TimeSpan? LastTook { get; private set; }

    /// <summary>Start listening. Anything still waiting is the caller's to send first (<see cref="SendNow"/>).</summary>
    public void Begin()
    {
        Cancel();
        _clip.Clear();
        LevelDb = VoiceLoudness.FloorDb;
        State = DictationState.Listening;
    }

    public void Feed(ReadOnlySpan<float> samples)
    {
        if (State != DictationState.Listening || Full)
            return;
        var room = (int)(MaxSeconds * Rate) - _clip.Count;
        var take = samples[..Math.Min(samples.Length, room)];
        foreach (var s in take)
            _clip.Add(s);
        if (_clip.Count > 0)
        {
            var n = Math.Min(MeterSamples, _clip.Count);
            LevelDb = VoiceLoudness.Db(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_clip)[^n..]);
        }
    }

    /// <summary>
    /// The key's let go: measure how it was said and send it off to be made out. Silence, or no
    /// recognizer to hand, comes to nothing with a note why.
    /// </summary>
    public void End(ISpeechRecognizer? recognizer, string prompt, VoiceCalibration calibration, float previewSeconds)
    {
        if (State != DictationState.Listening)
            return;
        var pcm = _clip.ToArray();
        _clip.Clear();
        LevelDb = VoiceLoudness.FloorDb;
        var loudness = VoiceLoudness.Measure(pcm, Rate);
        if (loudness.Silent)
        {
            Idle("Nothing heard. Hold the key while you speak.");
            return;
        }
        if (recognizer == null)
        {
            Idle("Not ready to listen yet.");
            return;
        }
        _reading = calibration.Classify(loudness);
        _wait = previewSeconds < 0f ? float.PositiveInfinity : previewSeconds;
        _cancel = new CancellationTokenSource();
        var clip = pcm[loudness.Start..loudness.End];
        _transcribing = recognizer.TranscribeAsync(clip, prompt, _cancel.Token);
        State = DictationState.Transcribing;
    }

    /// <summary>
    /// Once a frame: the transcription's arrival, the countdown and the note. Returns the lines to
    /// say when the countdown runs out (or there was none), else null.
    /// </summary>
    public IReadOnlyList<(string Text, VoiceLevel Level)>? Update(float dt)
    {
        if (_noteLeft > 0f && (_noteLeft -= dt) <= 0f)
            Note = null;
        if (State == DictationState.Transcribing && _transcribing is { IsCompleted: true } done)
        {
            _transcribing = null;
            _cancel?.Dispose();
            _cancel = null;
            if (!done.IsCompletedSuccessfully)
            {
                if (!done.IsCanceled)
                    Log.Engine.Warning($"Transcription failed: {done.Exception?.InnerException?.Message}");
                Idle(done.IsCanceled ? null : "Couldn't make that out.");
                return null;
            }
            var t = done.Result;
            LastTook = t.Took;
            Log.Engine.Info($"Heard in {t.Took.TotalMilliseconds:F0} ms ({_reading.Level}, {_reading.Confidence:P0} sure)");
            if (t.Text.Length == 0)
            {
                Idle("Couldn't make out any words.");
                return null;
            }
            Pending = new PendingLine(t.Text, _reading.Level, _reading.Confidence, _wait);
            State = DictationState.Preview;
            if (_wait <= 0f)
                return SendNow();
        }
        if (State == DictationState.Preview && Pending is { } p && !p.Changed && float.IsFinite(p.Left))
        {
            p.Left -= dt;
            if (p.Left <= 0f)
                return SendNow();
        }
        return null;
    }

    /// <summary>Say what's waiting now, split into lines the sim takes; empty if nothing was.</summary>
    public IReadOnlyList<(string Text, VoiceLevel Level)> SendNow()
    {
        if (State != DictationState.Preview || Pending is not { } p)
            return [];
        Pending = null;
        State = DictationState.Idle;
        return Transcript.Lines(p.Text, Sim.Speech.SpeechSystem.MaxLength).Select(line => (line, p.Level)).ToList();
    }

    /// <summary>Change a waiting line's volume; it then waits to be sent.</summary>
    public void SetLevel(VoiceLevel level)
    {
        if (Pending is not { } p)
            return;
        p.Level = level;
        p.Changed = true;
    }

    /// <summary>Take back what's waiting (or stop listening or transcribing), saying nothing.</summary>
    public void Discard()
    {
        Cancel();
        Pending = null;
        State = DictationState.Idle;
        _clip.Clear();
    }

    /// <summary>Hand the waiting words over to be typed into, unsaid; null if there were none.</summary>
    public string? TakeForEditing()
    {
        var text = Pending?.Text;
        Discard();
        return text;
    }

    /// <summary>Show a word about why nothing's happening (no microphone, the model still loading).</summary>
    public void Tell(string note)
    {
        Note = note;
        _noteLeft = NoteSeconds;
    }

    private void Cancel()
    {
        _cancel?.Cancel(); // not disposed: the recognizer may still be looking at its token
        _cancel = null;
        _transcribing = null;
    }

    private void Idle(string? note)
    {
        State = DictationState.Idle;
        Pending = null;
        Note = note;
        _noteLeft = note == null ? 0f : NoteSeconds;
    }
}
