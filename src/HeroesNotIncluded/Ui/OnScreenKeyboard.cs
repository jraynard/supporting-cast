using System.Diagnostics;
using Brutal.ImGuiApi;
using Narsil;

namespace HeroesNotIncluded.Ui;

/// <summary>
/// Brings up Steam's on-screen keyboard when a text field is clicked into on a Steam Deck, where
/// there's no keyboard to type on. Asks Steam through its URL handler rather than the Steamworks
/// API, so it works for the plain Linux build added as a non-Steam game. Best effort: if Steam
/// isn't there to answer, nothing happens and Steam+X still opens it by hand.
/// </summary>
public static class OnScreenKeyboard
{
    /// <summary>Call straight after an <c>ImGui.InputText*</c>: opens the keyboard as that field takes focus.</summary>
    public static void AfterTextField()
    {
        if (HandheldDefaults.OnSteamDeck && ImGui.IsItemActivated())
            Show();
    }

    private static void Show()
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo("xdg-open", "steam://open/keyboard")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true, // keep xdg-open's chatter out of the game's console
                RedirectStandardError = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Engine.Warning($"Couldn't ask Steam for its on-screen keyboard: {ex.Message}");
        }
    }
}
