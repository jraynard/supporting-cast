using System.Diagnostics;
using Narsil.Speech;

namespace HeroesNotIncluded.Voice;

/// <summary>
/// <c>--dictate-test FILE</c>: headless, a recorded clip (.wav or .ogg) through the same loudness
/// reading and recognizer the game uses, with how long each took. For choosing the model a machine
/// can afford (run it on the Deck) and for checking accuracy. <c>--model ID</c> picks the model,
/// <c>--prompt TEXT</c> the names to steer it, <c>--threads N</c> the threads. <c>mic</c> for the
/// file records four seconds from the microphone in the settings instead (kept nowhere).
/// </summary>
public static class DictateTest
{
    private static float[] Record(string? device, float seconds)
    {
        Console.WriteLine($"Microphones: {string.Join(", ", Narsil.Audio.MicCapture.Devices().DefaultIfEmpty("none"))}");
        using var mic = Narsil.Audio.MicCapture.Open(device);
        if (mic == null)
        {
            Console.WriteLine("error: no microphone opened");
            return [];
        }
        Console.WriteLine($"Recording {seconds:F0} s from {mic.Name}: speak now.");
        var samples = new List<float>();
        mic.Start();
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            Thread.Sleep(50);
            mic.ReadAll(samples);
        }
        mic.Stop();
        Console.WriteLine($"Got {samples.Count / (float)SpeechAudio.Rate:F2} s of audio.");
        return [.. samples];
    }

    public static int Run(string path, string? modelId, string? prompt, int threads)
    {
        var model = SpeechModels.Find(modelId) ?? SpeechModels.BaseEnglish;
        if (!VoiceService.ModelPresent(model))
        {
            Console.WriteLine($"Fetching {model.File} ({model.Megabytes:F0} MB) into {VoiceService.ModelDirectory}...");
            SpeechModels.Download(model, VoiceService.ModelDirectory).GetAwaiter().GetResult();
        }
        var pcm = path == "mic" ? Record(Settings.Current.MicDevice, 4f) : SpeechAudio.Load(path);
        if (pcm.Length == 0)
            return 1;
        var loudness = VoiceLoudness.Measure(pcm);
        var reading = (Settings.Current.VoiceCalibration ?? VoiceCalibration.Default).Classify(loudness);
        Console.WriteLine($"{Path.GetFileName(path)}: {pcm.Length / (float)SpeechAudio.Rate:F1} s, speech {loudness.SpeechSeconds:F1} s at {loudness.SpeechDb:F1} dB over {loudness.NoiseDb:F1} dB, {loudness.VoicedShare:P0} voiced: {reading.Level} ({reading.Confidence:P0} sure)");

        var clock = Stopwatch.StartNew();
        using var recognizer = WhisperRecognizer.Load(SpeechModels.PathIn(VoiceService.ModelDirectory, model), threads);
        Console.WriteLine($"{model.Id} loaded in {clock.Elapsed.TotalSeconds:F2} s, {(threads > 0 ? threads : WhisperRecognizer.DefaultThreads)} thread(s)");
        var clip = loudness.Silent ? pcm : pcm[loudness.Start..loudness.End];
        Transcription last = default;
        for (var run = 0; run < 2; run++) // the first run warms up; the second is what a player waits
        {
            last = recognizer.TranscribeAsync(clip, prompt).GetAwaiter().GetResult();
            Console.WriteLine($"run {run + 1}: {last.Took.TotalSeconds:F2} s ({clip.Length / (float)SpeechAudio.Rate / Math.Max(0.001, last.Took.TotalSeconds):F1}x real time), {last.Confidence:P0} confident");
        }
        Console.WriteLine($"\"{last.Text}\"");
        foreach (var line in Transcript.Lines(last.Text, Sim.Speech.SpeechSystem.MaxLength))
            Console.WriteLine($"  line: {line}");
        return last.Text.Length > 0 ? 0 : 1;
    }
}
