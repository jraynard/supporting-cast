using Brutal.ImGuiApi;
using Narsil.Ui;

namespace SupportingCast.Ui;

/// <summary>
/// The game's ImGui look: "parchment and ember". Warm dark-brown panels, bone text, amber
/// accents echoing the dragon's glow, rounded corners and roomier padding than stock ImGui.
/// Status colours live here too, so every panel agrees on what green and amber mean.
/// </summary>
public static class Theme
{
    public static readonly float4 Good = new(0.55f, 0.92f, 0.45f, 1f);
    public static readonly float4 Warn = new(1f, 0.78f, 0.3f, 1f);
    public static readonly float4 Bad = new(1f, 0.42f, 0.36f, 1f);
    public static readonly float4 Dim = new(0.7f, 0.64f, 0.56f, 1f);
    public static readonly float4 Text = new(0.95f, 0.9f, 0.82f, 1f);
    public static readonly float4 Accent = new(0.95f, 0.58f, 0.2f, 1f);
    public static readonly float4 AccentSoft = new(0.62f, 0.34f, 0.14f, 1f);
    public static readonly float4 Panel = new(0.11f, 0.085f, 0.075f, 0.95f);

    public static ImColor8 U8(float4 c, float alpha = 1f) => Gui.U8(c, alpha);

    /// <summary>Global UI scale (fonts and sizes): <see cref="Gui.Scale"/>.</summary>
    public static float Scale => Gui.Scale;

    /// <summary>The whole look, for the shared kit (<see cref="UiTheme"/>).</summary>
    public static readonly UiTheme Look = new()
    {
        Palette = new UiPalette { Text = Text, Dim = Dim, Accent = Accent, AccentSoft = AccentSoft, Good = Good, Warn = Warn, Bad = Bad, Panel = Panel },
        Colors = new Dictionary<ImGuiCol, float4>
        {
            [ImGuiCol.Text] = new float4(Text.X, Text.Y, Text.Z, 1f),
            [ImGuiCol.TextDisabled] = new float4(0.58f, 0.52f, 0.45f, 1f),
            [ImGuiCol.WindowBg] = new float4(Panel.X, Panel.Y, Panel.Z, Panel.W),
            [ImGuiCol.ChildBg] = new float4(0.15f, 0.115f, 0.095f, 0.6f),
            [ImGuiCol.PopupBg] = new float4(0.13f, 0.1f, 0.085f, 0.97f),
            [ImGuiCol.Border] = new float4(0.42f, 0.3f, 0.19f, 0.55f),
            [ImGuiCol.BorderShadow] = new float4(0, 0, 0, 0),
            [ImGuiCol.FrameBg] = new float4(0.21f, 0.165f, 0.13f, 1f),
            [ImGuiCol.FrameBgHovered] = new float4(0.3f, 0.22f, 0.16f, 1f),
            [ImGuiCol.FrameBgActive] = new float4(0.38f, 0.26f, 0.16f, 1f),
            [ImGuiCol.TitleBg] = new float4(0.16f, 0.11f, 0.085f, 1f),
            [ImGuiCol.TitleBgActive] = new float4(0.34f, 0.18f, 0.1f, 1f),
            [ImGuiCol.TitleBgCollapsed] = new float4(0.16f, 0.11f, 0.085f, 0.8f),
            [ImGuiCol.MenuBarBg] = new float4(0.1f, 0.075f, 0.065f, 0.96f),
            [ImGuiCol.ScrollbarBg] = new float4(0.08f, 0.06f, 0.05f, 0.5f),
            [ImGuiCol.ScrollbarGrab] = new float4(0.36f, 0.26f, 0.18f, 1f),
            [ImGuiCol.ScrollbarGrabHovered] = new float4(0.48f, 0.33f, 0.2f, 1f),
            [ImGuiCol.ScrollbarGrabActive] = new float4(0.6f, 0.38f, 0.18f, 1f),
            [ImGuiCol.CheckMark] = new float4(1f, 0.72f, 0.32f, 1f),
            [ImGuiCol.SliderGrab] = new float4(0.85f, 0.52f, 0.22f, 1f),
            [ImGuiCol.SliderGrabActive] = new float4(1f, 0.64f, 0.26f, 1f),
            [ImGuiCol.Button] = new float4(0.3f, 0.21f, 0.14f, 1f),
            [ImGuiCol.ButtonHovered] = new float4(0.5f, 0.3f, 0.15f, 1f),
            [ImGuiCol.ButtonActive] = new float4(0.68f, 0.38f, 0.14f, 1f),
            [ImGuiCol.Header] = new float4(0.36f, 0.23f, 0.13f, 0.8f),
            [ImGuiCol.HeaderHovered] = new float4(0.5f, 0.31f, 0.15f, 0.85f),
            [ImGuiCol.HeaderActive] = new float4(0.62f, 0.36f, 0.15f, 1f),
            [ImGuiCol.Separator] = new float4(0.42f, 0.3f, 0.19f, 0.6f),
            [ImGuiCol.SeparatorHovered] = new float4(0.7f, 0.42f, 0.18f, 1f),
            [ImGuiCol.SeparatorActive] = new float4(0.9f, 0.55f, 0.2f, 1f),
            [ImGuiCol.ResizeGrip] = new float4(0.5f, 0.32f, 0.16f, 0.3f),
            [ImGuiCol.ResizeGripHovered] = new float4(0.7f, 0.42f, 0.18f, 0.7f),
            [ImGuiCol.ResizeGripActive] = new float4(0.9f, 0.55f, 0.2f, 0.9f),
            [ImGuiCol.Tab] = new float4(0.24f, 0.17f, 0.12f, 1f),
            [ImGuiCol.TabHovered] = new float4(0.5f, 0.31f, 0.15f, 1f),
            [ImGuiCol.TabSelected] = new float4(0.4f, 0.24f, 0.13f, 1f),
            [ImGuiCol.TabDimmed] = new float4(0.18f, 0.13f, 0.1f, 1f),
            [ImGuiCol.TabDimmedSelected] = new float4(0.3f, 0.2f, 0.13f, 1f),
            [ImGuiCol.PlotHistogram] = new float4(0.86f, 0.56f, 0.22f, 1f),
            [ImGuiCol.PlotHistogramHovered] = new float4(1f, 0.66f, 0.26f, 1f),
            [ImGuiCol.TableHeaderBg] = new float4(0.24f, 0.17f, 0.12f, 1f),
            [ImGuiCol.TableBorderStrong] = new float4(0.42f, 0.3f, 0.19f, 0.7f),
            [ImGuiCol.TableBorderLight] = new float4(0.32f, 0.23f, 0.16f, 0.5f),
            [ImGuiCol.TableRowBgAlt] = new float4(1f, 0.9f, 0.75f, 0.04f),
            [ImGuiCol.TextSelectedBg] = new float4(0.8f, 0.45f, 0.15f, 0.35f),
            [ImGuiCol.NavCursor] = new float4(1f, 0.66f, 0.26f, 1f),
            [ImGuiCol.ModalWindowDimBg] = new float4(0.05f, 0.03f, 0.02f, 0.5f),
        },
    };

    public static void Apply(float scale = 1f) => Look.Apply(scale);

    /// <summary>A section header in the heavier font, with a thin rule under it.</summary>
    public static void Header(Narsil.Ui.ImGuiLayer layer, string text) => Gui.Header(text);
}
