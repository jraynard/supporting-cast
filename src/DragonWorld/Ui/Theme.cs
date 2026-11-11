using Brutal.ImGuiApi;

namespace DragonWorld.Ui;

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

    public static ImColor8 U8(float4 c, float alpha = 1f) =>
        new((byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255), (byte)(Math.Clamp(c.W * alpha, 0, 1) * 255));

    /// <summary>Global UI scale (fonts and sizes), adjustable in the Debug window.</summary>
    public static float Scale { get; private set; } = 1f;

    public static void Apply(float scale = 1f)
    {
        var style = ImGui.GetStyle();
        ImGui.StyleColorsDark(style);
        style.WindowRounding = 7f;
        style.ChildRounding = 5f;
        style.FrameRounding = 4f;
        style.PopupRounding = 5f;
        style.GrabRounding = 4f;
        style.TabRounding = 5f;
        style.ScrollbarRounding = 6f;
        style.WindowBorderSize = 1f;
        style.FrameBorderSize = 0f;
        style.WindowPadding = new float2(11, 10);
        style.FramePadding = new float2(8, 4);
        style.ItemSpacing = new float2(8, 6);
        style.ItemInnerSpacing = new float2(6, 4);
        style.WindowTitleAlign = new float2(0.03f, 0.5f);
        style.ScrollbarSize = 12f;

        void Set(ImGuiCol col, float r, float g, float b, float a = 1f) => style.Colors[(int)col] = new float4(r, g, b, a);
        Set(ImGuiCol.Text, Text.X, Text.Y, Text.Z);
        Set(ImGuiCol.TextDisabled, 0.58f, 0.52f, 0.45f);
        Set(ImGuiCol.WindowBg, Panel.X, Panel.Y, Panel.Z, Panel.W);
        Set(ImGuiCol.ChildBg, 0.15f, 0.115f, 0.095f, 0.6f);
        Set(ImGuiCol.PopupBg, 0.13f, 0.1f, 0.085f, 0.97f);
        Set(ImGuiCol.Border, 0.42f, 0.3f, 0.19f, 0.55f);
        Set(ImGuiCol.BorderShadow, 0, 0, 0, 0);
        Set(ImGuiCol.FrameBg, 0.21f, 0.165f, 0.13f);
        Set(ImGuiCol.FrameBgHovered, 0.3f, 0.22f, 0.16f);
        Set(ImGuiCol.FrameBgActive, 0.38f, 0.26f, 0.16f);
        Set(ImGuiCol.TitleBg, 0.16f, 0.11f, 0.085f);
        Set(ImGuiCol.TitleBgActive, 0.34f, 0.18f, 0.1f);
        Set(ImGuiCol.TitleBgCollapsed, 0.16f, 0.11f, 0.085f, 0.8f);
        Set(ImGuiCol.MenuBarBg, 0.1f, 0.075f, 0.065f, 0.96f);
        Set(ImGuiCol.ScrollbarBg, 0.08f, 0.06f, 0.05f, 0.5f);
        Set(ImGuiCol.ScrollbarGrab, 0.36f, 0.26f, 0.18f);
        Set(ImGuiCol.ScrollbarGrabHovered, 0.48f, 0.33f, 0.2f);
        Set(ImGuiCol.ScrollbarGrabActive, 0.6f, 0.38f, 0.18f);
        Set(ImGuiCol.CheckMark, 1f, 0.72f, 0.32f);
        Set(ImGuiCol.SliderGrab, 0.85f, 0.52f, 0.22f);
        Set(ImGuiCol.SliderGrabActive, 1f, 0.64f, 0.26f);
        Set(ImGuiCol.Button, 0.3f, 0.21f, 0.14f);
        Set(ImGuiCol.ButtonHovered, 0.5f, 0.3f, 0.15f);
        Set(ImGuiCol.ButtonActive, 0.68f, 0.38f, 0.14f);
        Set(ImGuiCol.Header, 0.36f, 0.23f, 0.13f, 0.8f);
        Set(ImGuiCol.HeaderHovered, 0.5f, 0.31f, 0.15f, 0.85f);
        Set(ImGuiCol.HeaderActive, 0.62f, 0.36f, 0.15f);
        Set(ImGuiCol.Separator, 0.42f, 0.3f, 0.19f, 0.6f);
        Set(ImGuiCol.SeparatorHovered, 0.7f, 0.42f, 0.18f);
        Set(ImGuiCol.SeparatorActive, 0.9f, 0.55f, 0.2f);
        Set(ImGuiCol.ResizeGrip, 0.5f, 0.32f, 0.16f, 0.3f);
        Set(ImGuiCol.ResizeGripHovered, 0.7f, 0.42f, 0.18f, 0.7f);
        Set(ImGuiCol.ResizeGripActive, 0.9f, 0.55f, 0.2f, 0.9f);
        Set(ImGuiCol.Tab, 0.24f, 0.17f, 0.12f);
        Set(ImGuiCol.TabHovered, 0.5f, 0.31f, 0.15f);
        Set(ImGuiCol.TabSelected, 0.4f, 0.24f, 0.13f);
        Set(ImGuiCol.TabDimmed, 0.18f, 0.13f, 0.1f);
        Set(ImGuiCol.TabDimmedSelected, 0.3f, 0.2f, 0.13f);
        Set(ImGuiCol.PlotHistogram, 0.86f, 0.56f, 0.22f);
        Set(ImGuiCol.PlotHistogramHovered, 1f, 0.66f, 0.26f);
        Set(ImGuiCol.TableHeaderBg, 0.24f, 0.17f, 0.12f);
        Set(ImGuiCol.TableBorderStrong, 0.42f, 0.3f, 0.19f, 0.7f);
        Set(ImGuiCol.TableBorderLight, 0.32f, 0.23f, 0.16f, 0.5f);
        Set(ImGuiCol.TableRowBgAlt, 1f, 0.9f, 0.75f, 0.04f);
        Set(ImGuiCol.TextSelectedBg, 0.8f, 0.45f, 0.15f, 0.35f);
        Set(ImGuiCol.NavCursor, 1f, 0.66f, 0.26f);
        Set(ImGuiCol.ModalWindowDimBg, 0.05f, 0.03f, 0.02f, 0.5f);

        Scale = scale;
        if (Math.Abs(scale - 1f) > 0.001f)
            style.ScaleAllSizes(scale);
        style.FontScaleMain = scale;
    }

    /// <summary>A section header in the heavier font, with a thin amber rule under it.</summary>
    public static void Header(Narsil.Ui.ImGuiLayer layer, string text)
    {
        if (layer.HeaderFont is { } font)
            ImGui.PushFont(font, 0f);
        var color = Accent;
        ImGui.TextColored(in color, text.Replace("%", "%%")); // a printf format
        if (layer.HeaderFont != null)
            ImGui.PopFont();
    }
}
