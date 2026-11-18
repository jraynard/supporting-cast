using Narsil;
using Narsil.Audio;

namespace HeroesNotIncluded.Audio;

/// <summary>
/// The game's one <see cref="AudioEngine"/>, shared like <see cref="Settings.Current"/> because the
/// title screen and the village replace each other and the music carries across. Silent until
/// <see cref="Open"/> (tests, tools, headless runs), so callers never check.
/// </summary>
public static class GameAudio
{
    public static AudioEngine Engine { get; private set; } = AudioEngine.Silent();

    private static SoundReloader? _reloader;

    /// <summary><c>SC_SOUND_LOG=1</c>: log every sound as it starts, for tuning and checking hooks.</summary>
    private static readonly bool LogPlays = Environment.GetEnvironmentVariable("SC_SOUND_LOG") == "1";
    private static long _logged;

    /// <summary>The manifest: the checkout's when running from one (edits heard at once), else the build's.</summary>
    public static string ManifestPath(string dataDirectory) =>
        Path.Combine(DevPaths.Sounds ?? Path.Combine(dataDirectory, "sounds"), "sounds.json");

    /// <summary>Open the output device (or stay silent without one) and load the sounds: the game's and each mod's.</summary>
    public static void Open(GameHost host)
    {
        Engine.Dispose();
        var paths = Mods.GameContent.Current.SoundManifests(host.DataDirectory);
        Engine = AudioEngine.Open(paths[0]);
        if (paths.Count > 1)
            Engine.SetManifests(paths);
        Engine.Preload();
        Watch(paths);
        Apply(Settings.Current);
    }

    /// <summary>Load from these manifests (the content changed: mods turned on or off) and watch their folders.</summary>
    public static void UseManifests(IReadOnlyList<string> paths)
    {
        if (paths.SequenceEqual(Engine.ManifestPaths))
            return;
        Engine.SetManifests(paths);
        Engine.Preload();
        Watch(paths);
    }

    private static void Watch(IReadOnlyList<string> paths)
    {
        _reloader?.Dispose();
        _reloader = new SoundReloader(Engine, paths.Select(p => Path.GetDirectoryName(p)!), DevPaths.Watch());
    }

    /// <summary>The game's share of the master volume while the talk key is held: low enough not to be heard back.</summary>
    public const float DuckedTo = 0.3f;

    private static float _master = 1f, _duck = 1f;

    public static void Apply(Settings s)
    {
        _master = s.MasterVolume;
        Engine.MasterVolume = _master * _duck;
        Engine.SetVolume(AudioBus.Music, s.MusicVolume);
        Engine.SetVolume(AudioBus.Sfx, s.EffectsVolume);
        Engine.SetVolume(AudioBus.Ui, s.EffectsVolume);
        Engine.SetVolume(AudioBus.Ambience, s.AmbienceVolume);
        Engine.SetVolume(AudioBus.Voice, s.VoiceVolume);
    }

    /// <summary>Once a frame, from whichever game is running: F5 (with art) and file edits reload sounds.</summary>
    public static void Update(GameHost host, float dt)
    {
        Engine.Muted = Settings.Current.MuteInBackground && !host.Window.IsFocused;
        // The microphone and the speech model follow the settings here too, for every game alike.
        Voice.VoiceService.Update(Settings.Current);
        // Down quickly as the talk key's pressed, back up gently after.
        var duck = Voice.VoiceService.Ducking ? DuckedTo : 1f;
        _duck += (duck - _duck) * Math.Min(1f, dt * (duck < _duck ? 20f : 4f));
        Engine.MasterVolume = _master * _duck;
        _reloader?.Update(dt, force: host.Input.WasKeyPressed(Brutal.GlfwApi.GlfwKey.F5));
        Engine.Update(dt);
        if (LogPlays)
        {
            foreach (var p in Engine.Recent.Skip(Math.Max(0, Engine.Recent.Count - (int)Math.Min(Engine.Started - _logged, int.MaxValue))))
            {
                Log.Engine.Info($"sound {p.Id}{(p.At is { } at ? $" at {at.X:F0},{at.Y:F0}" : "")} gain {p.Gain:F2} pitch {p.Pitch:F2}{(p.Muffle > 0 ? $" muffled {p.Muffle:F1}" : "")}");
            }
            _logged = Engine.Started;
        }
    }

    public static void Close()
    {
        _reloader?.Dispose();
        _reloader = null;
        Engine.Dispose();
        Engine = AudioEngine.Silent();
    }
}
