using System.Text.Json;
using System.Text.Json.Serialization;
using Narsil;
using Narsil.Platform;

namespace SupportingCast;

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

    /// <summary>Multiplier on keyboard camera panning.</summary>
    public float CameraPanSpeed { get; set; } = 1f;

    /// <summary>The HD-2D look: tilt-shift blur, bloom on fire, vignette and a warmer grade (F10 toggles it).</summary>
    public bool Hd2d { get; set; } = true;

    /// <summary>HD-2D strengths, 0..1 each.</summary>
    public float TiltShift { get; set; } = 0.6f;
    public float Bloom { get; set; } = 0.7f;
    public float Vignette { get; set; } = 0.35f;
    public float Warmth { get; set; } = 0.5f;

    /// <summary>Relay server for online co-op ("host" or "host:port"); empty turns join codes off.</summary>
    public string RelayServer { get; set; } = "";

    /// <summary>Hosting also opens the village through the relay, with a join code to share.</summary>
    public bool HostOnline { get; set; } = true;

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

    public Settings Clone() => (Settings)MemberwiseClone();

    /// <summary>Keep values in range, whatever a hand-edited file says.</summary>
    public Settings Sanitized()
    {
        var s = Clone();
        if (!Enum.IsDefined(s.WindowMode))
            s.WindowMode = WindowMode.Windowed;
        s.Width = Math.Clamp(s.Width, 320, 16384);
        s.Height = Math.Clamp(s.Height, 240, 16384);
        s.MaxFps = Math.Clamp(s.MaxFps, 0, 1000);
        s.UiScale = float.IsFinite(s.UiScale) ? Math.Clamp(s.UiScale, MinUiScale, MaxUiScale) : 1f;
        s.CameraPanSpeed = float.IsFinite(s.CameraPanSpeed) ? Math.Clamp(s.CameraPanSpeed, MinPanSpeed, MaxPanSpeed) : 1f;
        static float Unit(float v, float fallback) => float.IsFinite(v) ? Math.Clamp(v, 0f, 1f) : fallback;
        s.TiltShift = Unit(s.TiltShift, 0.6f);
        s.Bloom = Unit(s.Bloom, 0.7f);
        s.Vignette = Unit(s.Vignette, 0.35f);
        s.Warmth = Unit(s.Warmth, 0.5f);
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

    public static Settings Load(string path)
    {
        FilePath = path;
        if (!File.Exists(path))
            return Current = new Settings();
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
