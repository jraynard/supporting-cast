using Narsil.Platform;

namespace HeroesNotIncluded;

/// <summary>
/// Spots a Steam Deck and gives first-run settings that suit it: a 7" 1280x800 screen read at arm's
/// length needs bigger text, and battery life wants a cap below 60. Only used when there's no
/// settings file yet, so nothing the player chose is ever overridden. Detection runs before the
/// window exists, so it reads the environment and the board's DMI name rather than the monitor.
/// </summary>
public static class HandheldDefaults
{
    /// <summary>Deck models by DMI product name: the LCD and OLED boards.</summary>
    private static readonly string[] DeckProducts = ["Jupiter", "Galileo"];

    /// <summary>Body text 20px tall: comfortable at arm's length, and still room for the side panels and the hotbar between them.</summary>
    public const float DeckUiScale = 1.2f;
    public const int DeckMaxFps = 45;

    /// <summary>
    /// True on a Steam Deck. Steam sets <c>SteamDeck=1</c> in Game Mode; in Desktop Mode the board
    /// name still gives it away.
    /// </summary>
    public static bool IsSteamDeck(string? steamDeckEnv, string? dmiVendor, string? dmiProduct) =>
        steamDeckEnv?.Trim() == "1"
        || (dmiVendor?.Trim() == "Valve" && DeckProducts.Contains(dmiProduct?.Trim()));

    /// <summary>Whether this machine is a Steam Deck, read from the real environment.</summary>
    public static bool OnSteamDeck { get; } = IsSteamDeck(
        Environment.GetEnvironmentVariable("SteamDeck"),
        ReadDmi("sys_vendor"),
        ReadDmi("product_name"));

    /// <summary>First-run settings for this machine: the plain defaults, or the Deck's.</summary>
    public static Settings FirstRun(bool steamDeck)
    {
        var s = new Settings();
        if (!steamDeck)
            return s;
        s.UiScale = DeckUiScale;
        s.WindowMode = WindowMode.Borderless; // Game Mode runs everything full screen anyway
        s.Width = 1280;
        s.Height = 800;
        s.MaxFps = DeckMaxFps;
        return s;
    }

    private static string? ReadDmi(string name)
    {
        if (!OperatingSystem.IsLinux())
            return null;
        try
        {
            var path = Path.Combine("/sys/devices/virtual/dmi/id", name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
