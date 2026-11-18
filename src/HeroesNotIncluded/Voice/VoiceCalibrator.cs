using Narsil.Audio;
using Narsil.Speech;

namespace HeroesNotIncluded.Voice;

/// <summary>
/// Three lines read aloud, quietly, as you'd talk, and called out, each held like the talk key;
/// from them a <see cref="VoiceCalibration"/> for this voice on this microphone. A whisper and a
/// shout differ more between people and headsets than between one person's whisper and their own
/// word, which is why the game measures yours rather than guessing.
/// </summary>
public sealed class VoiceCalibrator
{
    public static readonly (string Ask, string Line)[] Steps =
    [
        ("Whisper this, as you would to someone at your elbow:", "Meet me behind the mill after dark."),
        ("Now say this as you'd talk across a table:", "Two bowls of stew and a jug of ale, please."),
        ("Now call this out, as if across the square:", "Fire! The inn's on fire! Bring buckets!"),
    ];

    private readonly List<float> _clip = [];
    private readonly ClipLoudness[] _measured = new ClipLoudness[Steps.Length];

    public int Step { get; private set; }
    public bool Listening { get; private set; }
    public bool Done => Step >= Steps.Length;
    public float LevelDb { get; private set; } = VoiceLoudness.FloorDb;

    /// <summary>The calibration, once all three lines are in.</summary>
    public VoiceCalibration? Result { get; private set; }

    /// <summary>What went wrong with the last line, if anything.</summary>
    public string? Note { get; private set; }

    public void Restart()
    {
        Step = 0;
        Listening = false;
        Result = null;
        Note = null;
        _clip.Clear();
    }

    /// <summary>Once a frame, with whether the player is holding to speak.</summary>
    public void Update(bool held, MicCapture? mic)
    {
        if (Done || mic == null)
        {
            Listening = false;
            return;
        }
        if (held && !Listening)
        {
            mic.Start();
            _clip.Clear();
            Listening = true;
            Note = null;
        }
        if (!Listening)
            return;
        mic.ReadAll(_clip);
        var n = Math.Min(_clip.Count, SpeechAudio.Rate / 16);
        LevelDb = n == 0 ? VoiceLoudness.FloorDb : VoiceLoudness.Db(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_clip)[^n..]);
        if (held && _clip.Count < Dictation.MaxSeconds * SpeechAudio.Rate)
            return;
        mic.Stop();
        Listening = false;
        LevelDb = VoiceLoudness.FloorDb;
        Take(VoiceLoudness.Measure(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_clip), SpeechAudio.Rate));
        _clip.Clear();
    }

    /// <summary>One line's measurement in (the tests skip the microphone).</summary>
    internal void Take(ClipLoudness clip)
    {
        if (Done)
            return;
        if (clip.Silent)
        {
            Note = "Nothing heard. Hold the button the whole time you speak.";
            return;
        }
        if (Step == 1 && clip.SpeechDb < _measured[0].SpeechDb)
        {
            Note = "That came out quieter than the whisper. Try it again, at your usual voice.";
            return;
        }
        if (Step == 2 && clip.SpeechDb < _measured[1].SpeechDb)
        {
            Note = "That came out quieter than your talking voice. Try it again, louder.";
            return;
        }
        _measured[Step++] = clip;
        if (Done)
            Result = VoiceCalibration.From(_measured[0], _measured[1], _measured[2]);
    }
}
