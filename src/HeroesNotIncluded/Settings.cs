using System.Text.Json;
using System.Text.Json.Serialization;
using Narsil;
using Narsil.Platform;

namespace HeroesNotIncluded;

/// <summary>
/// The player's options, saved as JSON in the user config directory. One shared instance
/// (<see cref="Current"/>) because the title screen and village both read it and replace each
/// other freely. Unknown or missing fields fall back to defaults, so old files keep loading.
/// </summary>
public sealed class Settings
{
    public WindowMode WindowMode { get; set; } = WindowMode.Windowed;
    public int Width { get; set; } = 1600;
    public int Height { get; set; } = 900;

    /// <summary>Monitor name; null for the primary monitor.</summary>
    public string? Monitor { get; set; }
    public bool VSync { get; set; } = true;

    /// <summary>Frame rate cap; 0 for none.</summary>
    public int MaxFps { get; set; }
    public float UiScale { get; set; } = 1f;

    /// <summary>Drop to pause when a danger message arrives (raids, fires, a downed villager).</summary>
    public bool PauseOnDanger { get; set; } = true;

    /// <summary>A glow round the button for what needs doing now (a patient waiting: the gift, lay on hands).</summary>
    public bool GuideHighlights { get; set; } = true;

    /// <summary>Alone, time holds while the tutorial's card is open to read: a tester watched the clock run while reading.</summary>
    public bool TutorialHold { get; set; } = true;

    /// <summary>
    /// The round's HUD shows only what needs you: needs when they're low, the weather when it's cold,
    /// no compass beside the minimap, the shop panel shut till an order comes. Off, everything shows
    /// all the time. On by default: a Steam Deck playtest found the full HUD crowded the screen.
    /// </summary>
    public bool SimpleHud { get; set; } = true;

    /// <summary>The camera shakes a little with heavy blows you deal or take, and rumbles with dragon fire nearby.</summary>
    public bool ScreenShake { get; set; } = true;

    /// <summary>Multiplier on keyboard camera panning.</summary>
    public float CameraPanSpeed { get; set; } = 1f;

    /// <summary>
    /// Anti-aliasing (MSAA) samples for the world: 1 (off), 2, 4 or 8. Smooths the edges of roofs,
    /// beams and walls as the camera turns, for GPU memory and fill rate (older PCs and handhelds: 2
    /// or off). Applies at the next launch.
    /// </summary>
    public int Msaa { get; set; } = DefaultMsaa;

    public const int DefaultMsaa = 4;

    /// <summary>The MSAA choices offered.</summary>
    public static readonly int[] MsaaChoices = [1, 2, 4, 8];

    /// <summary>The HD-2D look: tilt-shift blur, bloom on fire, vignette and a warmer grade (F10 toggles it).</summary>
    public bool Hd2d { get; set; } = true;

    /// <summary>HD-2D strengths, 0..1 each.</summary>
    public float TiltShift { get; set; } = 0.6f;
    public float Bloom { get; set; } = 0.7f;
    public float Vignette { get; set; } = 0.35f;
    public float Warmth { get; set; } = 0.5f;

    /// <summary>Sound volumes, 0..1: everything, then music, effects (work, fights, the interface), the world around, and voices.</summary>
    public float MasterVolume { get; set; } = 0.8f;
    public float MusicVolume { get; set; } = 0.55f;
    public float EffectsVolume { get; set; } = 1f;
    public float AmbienceVolume { get; set; } = 0.8f;
    public float VoiceVolume { get; set; } = 0.8f;

    /// <summary>Silence while another window has focus.</summary>
    public bool MuteInBackground { get; set; } = true;

    /// <summary>Speech heard as a babble of syllables, each voice its own.</summary>
    public bool Babble { get; set; } = true;

    /// <summary>
    /// Speak aloud: hold the talk key and your words become your character's line (Voice/). Off
    /// until the player opts in; the microphone isn't opened before then.
    /// </summary>
    public bool VoiceInput { get; set; }

    /// <summary>Which microphone, by OpenAL's name for it; null for the system's default.</summary>
    public string? MicDevice { get; set; }

    /// <summary>The speech model (<see cref="Narsil.Speech.SpeechModels"/>): "base.en" (quick) or "small.en" (careful).</summary>
    public string VoiceModel { get; set; } = Narsil.Speech.SpeechModels.BaseEnglish.Id;

    /// <summary>Seconds a spoken line waits, shown, before it's said: 0 says it at once, below 0 waits for you.</summary>
    public float VoicePreviewSeconds { get; set; } = DefaultVoicePreview;

    /// <summary>The player's quiet, normal and loud voice on this microphone; null until calibrated.</summary>
    public Narsil.Speech.VoiceCalibration? VoiceCalibration { get; set; }

    /// <summary>Turn the game down while the talk key is held, so the microphone hears you and not the speakers.</summary>
    public bool DuckWhileTalking { get; set; } = true;

    public const float DefaultVoicePreview = 2f, MaxVoicePreview = 10f;

    /// <summary>Relay server for online co-op ("host" or "host:port"); empty uses the game's own (<see cref="Net.CoopRelay.Default"/>).</summary>
    public string RelayServer { get; set; } = "";

    /// <summary>Hosting also opens the village through the relay, with a join code to share.</summary>
    public bool HostOnline { get; set; } = true;

    /// <summary>Keep each round's messages, crises, link health and summary under logs/rounds, for the tuning playtests.</summary>
    public bool KeepPlaytestLogs { get; set; } = true;

    /// <summary>
    /// Rebound controls, by action id (<see cref="Controls"/>): each action's whole list, as text
    /// ("Key:Q", "Pad:LB"). Only what differs from the defaults; null for none.
    /// </summary>
    public Dictionary<string, string[]>? Bindings { get; set; }

    /// <summary>The mods turned on, by id (docs/MODDING.md); their load order is decided from their dependencies.</summary>
    public List<string> Mods { get; set; } = [];

    /// <summary>Mods that are on by default (the game's own trades) that the player turned off.</summary>
    public List<string> ModsOff { get; set; } = [];

    public const float MinUiScale = 0.75f, MaxUiScale = 2f;
    public const float MinPanSpeed = 0.25f, MaxPanSpeed = 3f;

    [JsonIgnore]
    public DisplaySettings Display
    {
        get => new() { Mode = WindowMode, Size = new int2(Width, Height), Monitor = Monitor };
        set
        {
            WindowMode = value.Mode;
            // Borderless always runs at the desktop mode; keep the windowed size for coming back.
            if (value.Mode != WindowMode.Borderless)
                (Width, Height) = (value.Size.X, value.Size.Y);
            Monitor = value.Monitor;
        }
    }

    public static Settings Current { get; set; } = new();

    /// <summary>Where <see cref="Save"/> writes; null (tests, tools) disables saving.</summary>
    public static string? FilePath { get; set; }

    public static string DefaultPath => Path.Combine(GameHost.DefaultConfigDirectory(TitleGame.DataFolder), "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public Settings Clone()
    {
        var copy = (Settings)MemberwiseClone();
        copy.Bindings = Bindings?.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        copy.Mods = [.. Mods ?? []]; // a hand-edited file's null
        copy.ModsOff = [.. ModsOff ?? []];
        return copy;
    }

    /// <summary>Keep values in range, whatever a hand-edited file says.</summary>
    public Settings Sanitized()
    {
        var s = Clone();
        if (!Enum.IsDefined(s.WindowMode))
            s.WindowMode = WindowMode.Windowed;
        s.Width = Math.Clamp(s.Width, 320, 16384);
        s.Height = Math.Clamp(s.Height, 240, 16384);
        s.MaxFps = Math.Clamp(s.MaxFps, 0, 1000);
        if (Array.IndexOf(MsaaChoices, s.Msaa) < 0)
            s.Msaa = DefaultMsaa;
        s.UiScale = float.IsFinite(s.UiScale) ? Math.Clamp(s.UiScale, MinUiScale, MaxUiScale) : 1f;
        s.CameraPanSpeed = float.IsFinite(s.CameraPanSpeed) ? Math.Clamp(s.CameraPanSpeed, MinPanSpeed, MaxPanSpeed) : 1f;
        static float Unit(float v, float fallback) => float.IsFinite(v) ? Math.Clamp(v, 0f, 1f) : fallback;
        s.TiltShift = Unit(s.TiltShift, 0.6f);
        s.Bloom = Unit(s.Bloom, 0.7f);
        s.Vignette = Unit(s.Vignette, 0.35f);
        s.Warmth = Unit(s.Warmth, 0.5f);
        s.MasterVolume = Unit(s.MasterVolume, 0.8f);
        s.MusicVolume = Unit(s.MusicVolume, 0.55f);
        s.EffectsVolume = Unit(s.EffectsVolume, 1f);
        s.AmbienceVolume = Unit(s.AmbienceVolume, 0.8f);
        s.VoiceVolume = Unit(s.VoiceVolume, 0.8f);
        s.Bindings = Controls.Sanitize(s.Bindings);
        if (Narsil.Speech.SpeechModels.Find(s.VoiceModel) == null)
            s.VoiceModel = Narsil.Speech.SpeechModels.BaseEnglish.Id;
        s.VoicePreviewSeconds = float.IsFinite(s.VoicePreviewSeconds) ? Math.Clamp(s.VoicePreviewSeconds, -1f, MaxVoicePreview) : DefaultVoicePreview;
        if (s.VoicePreviewSeconds < 0f)
            s.VoicePreviewSeconds = -1f;
        if (s.VoiceCalibration is { } c && !(Level(c.QuietDb) && Level(c.NormalDb) && Level(c.LoudDb) && Share(c.QuietVoiced) && Share(c.NormalVoiced)))
            s.VoiceCalibration = null;
        static bool Level(float db) => float.IsFinite(db) && db is >= -120f and <= 10f;
        static bool Share(float v) => float.IsFinite(v) && v is >= 0f and <= 1f;
        if (s.MicDevice is { Length: 0 or > 500 })
            s.MicDevice = null;
        s.RelayServer = (s.RelayServer ?? "").Trim();
        if (s.RelayServer.Length > 200)
            s.RelayServer = "";
        return s;
    }

    public static string Serialize(Settings settings) => JsonSerializer.Serialize(settings, Json);

    /// <summary>Parse a settings file; a missing or unreadable one gives defaults rather than stopping the game from starting.</summary>
    public static Settings Deserialize(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<Settings>(json, Json) ?? new Settings()).Sanitized();
        }
        catch (JsonException ex)
        {
            Log.Engine.Warning($"Settings file is invalid, using defaults: {ex.Message}");
            return new Settings();
        }
    }

    /// <summary>Load the settings file; with none yet, start from defaults suited to this machine (a Steam Deck gets its own).</summary>
    public static Settings Load(string path) => Load(path, HandheldDefaults.OnSteamDeck);

    public static Settings Load(string path, bool steamDeck)
    {
        FilePath = path;
        if (!File.Exists(path))
            return Current = HandheldDefaults.FirstRun(steamDeck);
        try
        {
            return Current = Deserialize(File.ReadAllText(path));
        }
        catch (IOException ex)
        {
            Log.Engine.Warning($"Couldn't read {path}, using default settings: {ex.Message}");
            return Current = new Settings();
        }
    }

    public static void Save()
    {
        if (FilePath == null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, Serialize(Current));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Engine.Warning($"Couldn't save settings to {FilePath}: {ex.Message}");
        }
    }
}
