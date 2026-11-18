using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Voice;
using Narsil.Speech;

namespace HeroesNotIncluded.Tests;

/// <summary>Speaking aloud, game side: push to talk through preview to the lines said, with a recognizer stood in.</summary>
public class VoiceTests
{
    private const int Rate = SpeechAudio.Rate;

    /// <summary>Says whatever it's told to, at once.</summary>
    private sealed class FakeRecognizer(string text) : ISpeechRecognizer
    {
        public string Name => "fake";
        public string? LastPrompt { get; private set; }
        public int Clips { get; private set; }

        public Task<Transcription> TranscribeAsync(float[] pcm, string? prompt, CancellationToken cancel = default)
        {
            LastPrompt = prompt;
            Clips++;
            return Task.FromResult(new Transcription(Transcript.Clean(text), 0.9f, TimeSpan.FromMilliseconds(5)));
        }

        public void Dispose() { }
    }

    /// <summary>A second of a 140 Hz buzz with harmonics at <paramref name="db"/>, between quiet: a voice to the meter.</summary>
    private static float[] Voice(float db)
    {
        var pcm = new float[Rate * 2];
        var gain = MathF.Pow(10, db / 20) * 1.6f;
        for (var i = Rate / 2; i < Rate * 3 / 2; i++)
        {
            var t = i / (double)Rate;
            var v = 0.0;
            for (var h = 1; h <= 6; h++)
                v += Math.Sin(2 * Math.PI * 140 * h * t) / h;
            pcm[i] = (float)v * gain;
        }
        return pcm;
    }

    private static IReadOnlyList<(string Text, VoiceLevel Level)>? Speak(Dictation d, ISpeechRecognizer r, float[] pcm, float preview, VoiceCalibration? cal = null)
    {
        d.Begin();
        d.Feed(pcm);
        d.End(r, "Arin, Bea.", cal ?? VoiceCalibration.Default, preview);
        return d.Update(0f);
    }

    [Fact]
    public void ASpokenLineWaitsThenIsSaid()
    {
        var d = new Dictation();
        var r = new FakeRecognizer(" Two bowls of stew, please. ");
        Assert.Null(Speak(d, r, Voice(-28f), preview: 2f));
        Assert.Equal(DictationState.Preview, d.State);
        Assert.Equal("Two bowls of stew, please.", d.Pending!.Text);
        Assert.Equal(VoiceLevel.Normal, d.Pending.Level);
        Assert.Equal("Arin, Bea.", r.LastPrompt);

        Assert.Null(d.Update(1.5f));
        var said = d.Update(1f);
        Assert.NotNull(said);
        Assert.Equal([("Two bowls of stew, please.", VoiceLevel.Normal)], said);
        Assert.Equal(DictationState.Idle, d.State);
    }

    [Fact]
    public void NoWaitSaysItAtOnce()
    {
        var said = Speak(new Dictation(), new FakeRecognizer("Fire!"), Voice(-12f), preview: 0f);
        Assert.Equal([("Fire!", VoiceLevel.Loud)], said);
    }

    [Fact]
    public void WaitingForMeNeverRunsOut()
    {
        var d = new Dictation();
        Speak(d, new FakeRecognizer("Hello."), Voice(-28f), preview: -1f);
        Assert.Null(d.Update(1000f));
        Assert.Single(d.SendNow());
    }

    [Fact]
    public void ChangingTheVolumeHoldsTheLine()
    {
        var d = new Dictation();
        Speak(d, new FakeRecognizer("Meet me behind the mill."), Voice(-28f), preview: 2f);
        d.SetLevel(VoiceLevel.Quiet);
        Assert.Null(d.Update(10f)); // changed: it waits for the player now
        Assert.Equal([("Meet me behind the mill.", VoiceLevel.Quiet)], d.SendNow());
    }

    [Fact]
    public void TakenBackIsNeverSaid()
    {
        var d = new Dictation();
        Speak(d, new FakeRecognizer("Something rude."), Voice(-28f), preview: 2f);
        d.Discard();
        Assert.Null(d.Update(5f));
        Assert.Empty(d.SendNow());
        Assert.Equal(DictationState.Idle, d.State);
    }

    [Fact]
    public void SilenceAndNoWordsComeToNothing()
    {
        var d = new Dictation();
        var r = new FakeRecognizer("Hello.");
        Assert.Null(Speak(d, r, new float[Rate], preview: 0f));
        Assert.Equal(DictationState.Idle, d.State);
        Assert.NotNull(d.Note);
        Assert.Equal(0, r.Clips); // silence isn't sent to be made out (Whisper invents words for it)

        var tags = new Dictation();
        Assert.Null(Speak(tags, new FakeRecognizer("[BLANK_AUDIO]"), Voice(-28f), preview: 0f));
        Assert.Equal(DictationState.Idle, tags.State);
        Assert.NotNull(tags.Note);
    }

    [Fact]
    public void ALongSpeechIsSeveralLines()
    {
        var text = string.Join(" ", Enumerable.Repeat("The bandits are coming down the north road, so get your spears.", 8));
        var said = Speak(new Dictation(), new FakeRecognizer(text), Voice(-28f), preview: 0f)!;
        Assert.True(said.Count > 1);
        Assert.All(said, l => Assert.True(l.Text.Length <= SpeechSystem.MaxLength));
    }

    [Fact]
    public void AClipStopsShortOfWhispersWindow()
    {
        var d = new Dictation();
        d.Begin();
        for (var i = 0; i < 40; i++)
            d.Feed(new float[Rate]);
        Assert.True(d.Full);
        Assert.InRange(d.Seconds, Dictation.MaxSeconds - 0.01f, Dictation.MaxSeconds + 0.01f);
    }

    [Fact]
    public void VolumesMapToTheSims()
    {
        Assert.Equal(Volume.Whisper, VoiceUi.ToVolume(VoiceLevel.Quiet));
        Assert.Equal(Volume.Say, VoiceUi.ToVolume(VoiceLevel.Normal));
        Assert.Equal(Volume.Shout, VoiceUi.ToVolume(VoiceLevel.Loud));
    }

    [Fact]
    public void CalibrationTakesThreeLinesInOrder()
    {
        var c = new VoiceCalibrator();
        c.Take(VoiceLoudness.Measure(new float[Rate]));
        Assert.Equal(0, c.Step);
        Assert.NotNull(c.Note);
        c.Take(VoiceLoudness.Measure(Voice(-40f)));
        c.Take(VoiceLoudness.Measure(Voice(-45f))); // quieter than the whisper: asked again
        Assert.Equal(1, c.Step);
        c.Take(VoiceLoudness.Measure(Voice(-28f)));
        c.Take(VoiceLoudness.Measure(Voice(-14f)));
        Assert.True(c.Done);
        Assert.NotNull(c.Result);
        Assert.InRange(c.Result!.NormalDb, -31f, -25f);
    }

    [Fact]
    public void VoiceSettingsRoundTripAndAreCleaned()
    {
        var s = new Settings
        {
            VoiceInput = true, VoiceModel = "small.en", MicDevice = "USB headset", VoicePreviewSeconds = -1f,
            VoiceCalibration = new VoiceCalibration(-40f, -27f, -14f, 0.1f, 0.7f),
        };
        var back = Settings.Deserialize(Settings.Serialize(s));
        Assert.True(back.VoiceInput);
        Assert.Equal("small.en", back.VoiceModel);
        Assert.Equal("USB headset", back.MicDevice);
        Assert.Equal(-1f, back.VoicePreviewSeconds);
        Assert.Equal(s.VoiceCalibration, back.VoiceCalibration);

        var odd = Settings.Deserialize("""{ "VoiceModel": "huge", "VoicePreviewSeconds": 99, "MicDevice": "", "VoiceCalibration": { "QuietDb": 9999, "NormalDb": 0, "LoudDb": 0, "QuietVoiced": 0, "NormalVoiced": 0 } }""");
        Assert.Equal(SpeechModels.BaseEnglish.Id, odd.VoiceModel);
        Assert.Equal(Settings.MaxVoicePreview, odd.VoicePreviewSeconds);
        Assert.Null(odd.MicDevice);
        Assert.Null(odd.VoiceCalibration);
        Assert.False(new Settings().VoiceInput); // opt in only
    }
}
