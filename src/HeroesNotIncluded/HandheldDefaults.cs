using Narsil.Platform;

namespace HeroesNotIncluded;

/// <summary>
/// Spots a Steam Deck and gives first-run settings that suit it: a 7" 1280x800 screen read at arm's
/// length needs bigger text, and battery life wants a cap below 60. Only used when there's no
/// settings file yet, so nothing the player chose is ever overridden. Narsil's <see cref="Handheld"/>
/// does the spotting.
/// </summary>
public static class HandheldDefaults
{
    /// <summary>Body text 20px tall: comfortable at arm's length, and still room for the side panels and the hotbar between them.</summary>
    public const float DeckUiScale = 1.2f;
    public const int DeckMaxFps = 45;

    /// <summary>Whether this machine is a Steam Deck (<see cref="Handheld"/>).</summary>
    public static bool OnSteamDeck => Handheld.OnSteamDeck;

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
}
