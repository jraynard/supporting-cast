using Narsil;
using Narsil.Audio;
using Narsil.Speech;

namespace HeroesNotIncluded.Voice;

/// <summary>Where speaking aloud stands, for the settings and the HUD to explain.</summary>
public enum VoiceStatus
{
    Off,
    NeedsModel,
    Downloading,
    Loading,
    NoMicrophone,
    Failed,
    Ready,
}

/// <summary>
/// The microphone and the speech model, shared by the title screen and the village (as
/// <see cref="Audio.GameAudio"/> is) so a model loaded once stays loaded. Nothing is opened until
/// the player turns speaking aloud on (or calibrates), and both are let go when it's turned off.
/// The recognizer loads off the main thread; the microphone is only polled, never recorded to disk.
/// </summary>
public static class VoiceService
{
    /// <summary>Models live with the settings, outside the install, so an update doesn't fetch them again.</summary>
    public static string ModelDirectory => Path.Combine(GameHost.DefaultConfigDirectory(TitleGame.DataFolder), "models");

    public static SpeechModel Model(Settings s) => SpeechModels.Find(s.VoiceModel) ?? SpeechModels.BaseEnglish;

    public static bool ModelPresent(SpeechModel model) => SpeechModels.IsPresent(ModelDirectory, model);

    private static MicCapture? _mic;
    private static string? _micFor;
    private static bool _micTried;

    private static ISpeechRecognizer? _recognizer;
    private static string? _recognizerFor;
    private static Task<ISpeechRecognizer>? _loading;
    private static string? _loadingFor;
    private static string? _loadFailed;

    private static CancellationTokenSource? _download;
    private static Task? _downloading;

    /// <summary>The open microphone, or null.</summary>
    public static MicCapture? Mic => _mic;

    /// <summary>The loaded recognizer, or null while loading, off or without a model.</summary>
    public static ISpeechRecognizer? Recognizer => _recognizer;

    private static SpeechModel? _fetching;
    private static float _fetched; // written from the download's thread

    /// <summary>The model being fetched, and how far along (0..1); null when nothing is.</summary>
    public static (SpeechModel Model, float Progress)? Download => _fetching is { } m ? (m, Volatile.Read(ref _fetched)) : null;

    /// <summary>Why the last download or load failed, in words for the player; null if it didn't.</summary>
    public static string? Problem { get; private set; }

    /// <summary>Someone's holding the talk key: the game turns itself down (<see cref="Audio.GameAudio"/>).</summary>
    public static bool Ducking { get; set; }

    /// <summary>Something besides the setting wants the microphone open: the settings window calibrating.</summary>
    public static bool Calibrating { get; set; }

    /// <summary>For tests: a recognizer that stands in for the real one.</summary>
    internal static void UseForTests(ISpeechRecognizer? recognizer)
    {
        _recognizer = recognizer;
        _recognizerFor = recognizer == null ? null : "test";
    }

    public static VoiceStatus Status(Settings s)
    {
        if (!s.VoiceInput)
            return VoiceStatus.Off;
        var model = Model(s);
        if (Download is { } d && d.Model == model)
            return VoiceStatus.Downloading;
        if (_recognizerFor == "test" && _recognizer != null)
            return VoiceStatus.Ready;
        if (!ModelPresent(model))
            return VoiceStatus.NeedsModel;
        if (_loadFailed == model.Id)
            return VoiceStatus.Failed;
        if (_mic == null && _micTried)
            return VoiceStatus.NoMicrophone;
        return _recognizer != null && _recognizerFor == model.Id ? VoiceStatus.Ready : VoiceStatus.Loading;
    }

    /// <summary>Once a frame: open or let go of the microphone and the model as the settings ask.</summary>
    public static void Update(Settings s)
    {
        if (_loading is { IsCompleted: true } loaded)
        {
            if (loaded.IsCompletedSuccessfully && loaded.Result is { } r)
            {
                _recognizer?.Dispose();
                (_recognizer, _recognizerFor) = (r, _loadingFor);
                Log.Engine.Info($"Speech model {_loadingFor} loaded.");
            }
            else
            {
                _loadFailed = _loadingFor;
                Problem = $"The speech model wouldn't load: {loaded.Exception?.InnerException?.Message ?? "unknown error"}";
                Log.Engine.Warning(Problem);
            }
            (_loading, _loadingFor) = (null, null);
        }
        if (_downloading is { IsCompleted: true } fetched)
        {
            if (fetched.IsFaulted)
            {
                Problem = $"The download didn't finish: {fetched.Exception?.InnerException?.Message ?? "unknown error"}";
                Log.Engine.Warning(Problem);
            }
            _downloading = null;
            _fetching = null;
            _download?.Dispose();
            _download = null;
        }

        var wanted = s.VoiceInput || Calibrating;
        if (!wanted)
        {
            ReleaseMic();
            if (_recognizerFor != "test")
                ReleaseRecognizer();
            return;
        }
        if (_mic == null ? !_micTried || _micFor != s.MicDevice : _micFor != s.MicDevice)
        {
            ReleaseMic();
            _mic = MicCapture.Open(s.MicDevice);
            (_micFor, _micTried) = (s.MicDevice, true);
            if (_mic != null)
                Log.Engine.Info($"Microphone: {_mic.Name}");
        }
        if (!s.VoiceInput || _recognizerFor == "test")
            return;
        var model = Model(s);
        if (_recognizer != null && _recognizerFor != model.Id)
            ReleaseRecognizer();
        if (_recognizer == null && _loading == null && _loadFailed != model.Id && ModelPresent(model))
        {
            var path = SpeechModels.PathIn(ModelDirectory, model);
            _loadingFor = model.Id;
            _loading = Task.Run(() => (ISpeechRecognizer)WhisperRecognizer.Load(path));
        }
    }

    /// <summary>Try the microphone again (one plugged in since), and a model that failed to load.</summary>
    public static void Retry()
    {
        ReleaseMic();
        _micTried = false;
        _loadFailed = null;
        Problem = null;
    }

    /// <summary>Fetch a model in the background; <see cref="Download"/> follows it.</summary>
    public static void StartDownload(SpeechModel model)
    {
        if (_downloading != null)
            return;
        Problem = null;
        _loadFailed = null;
        _download = new CancellationTokenSource();
        (_fetching, _fetched) = (model, 0f);
        var progress = new Progress<float>(p => Volatile.Write(ref _fetched, p));
        var cancel = _download.Token;
        _downloading = Task.Run(() => SpeechModels.Download(model, ModelDirectory, progress, cancel), cancel);
    }

    public static void CancelDownload() => _download?.Cancel();

    private static void ReleaseMic()
    {
        _mic?.Dispose();
        _mic = null;
        _micFor = null;
    }

    private static void ReleaseRecognizer()
    {
        var r = _recognizer;
        (_recognizer, _recognizerFor) = (null, null);
        if (r != null)
            Task.Run(r.Dispose); // it waits out a clip mid-transcription: not on the frame
    }

    /// <summary>At exit.</summary>
    public static void Close()
    {
        _download?.Cancel();
        ReleaseMic();
        _recognizer?.Dispose();
        (_recognizer, _recognizerFor) = (null, null);
    }
}
