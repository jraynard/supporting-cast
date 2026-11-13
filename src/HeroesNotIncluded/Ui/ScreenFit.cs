using Brutal.ImGuiApi;
using Narsil.Ui;

namespace HeroesNotIncluded.Ui;

/// <summary>
/// Keeps free-floating windows (the ones <see cref="Hud"/> doesn't place) on a small screen. A
/// window that sizes to its contents would otherwise run off the bottom of an 800-high Steam Deck
/// screen; capped, it scrolls.
/// </summary>
public static class ScreenFit
{
    /// <summary>Cap the next window at the screen, less a margin. Call before <c>Panel.Begin</c>.</summary>
    public static void Next()
    {
        var screen = ImGui.GetIO().DisplaySize;
        var margin = Gui.S(10) * 2;
        var min = new float2(0, 0);
        var max = new float2(MathF.Max(screen.X - margin, Gui.S(200)), MathF.Max(screen.Y - margin, Gui.S(120)));
        ImGui.SetNextWindowSizeConstraints(in min, in max, (ImGuiSizeCallback?)null, default);
    }

    /// <summary>A panel width: <paramref name="px"/> at the UI scale, but no more than a share of the screen.</summary>
    public static float Width(float px, float maxFraction) =>
        MathF.Min(Gui.S(px), ImGui.GetIO().DisplaySize.X * maxFraction);
}
