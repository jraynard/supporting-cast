using Brutal.ImGuiApi;
using Narsil.Ui;

namespace SupportingCast.Ui;

/// <summary>
/// The game's ImGui look: "carved wood and iron". Oak-plank panels in an iron rim with riveted
/// corners, carved title plates, dark wells for lists, bone text and an ember accent (the forge
/// and the hearth). The chrome is nine-sliced from sprites drawn by tools/norse_ui.py; the
/// mechanism is Narsil's (<see cref="UiTheme"/>). Status colours live here too, so every panel
/// agrees on what green and amber mean.
/// </summary>
public static class Theme
{
    public static readonly float4 Good = new(0.58f, 0.86f, 0.42f, 1f);
    public static readonly float4 Warn = new(0.99f, 0.77f, 0.33f, 1f);
    public static readonly float4 Bad = new(0.97f, 0.43f, 0.35f, 1f);
    public static readonly float4 Dim = new(0.72f, 0.65f, 0.55f, 1f);
    public static readonly float4 Text = new(0.95f, 0.9f, 0.81f, 1f);
    public static readonly float4 Accent = new(0.99f, 0.64f, 0.26f, 1f);
    public static readonly float4 AccentSoft = new(0.56f, 0.34f, 0.17f, 1f);
    public static readonly float4 Panel = new(0.15f, 0.105f, 0.075f, 0.97f);
    public static readonly float4 Inset = new(0.09f, 0.065f, 0.05f, 1f);
    public static readonly float4 Iron = new(0.5f, 0.52f, 0.56f, 0.9f);

    /// <summary>The background of a mini-game's scene and its tracks.</summary>
    public static readonly float4 Well = new(0.07f, 0.055f, 0.045f, 1f);

    /// <summary>The empath's games: a dusk violet, the gift's colour.</summary>
    public static readonly float4 Spirit = new(0.12f, 0.1f, 0.16f, 1f);

    /// <summary>The gift's own glow: an empath's weave, its rings and strands.</summary>
    public static readonly float4 Arcane = new(0.66f, 0.58f, 1f, 1f);

    // Names under people's feet in the world, by who they are.
    public static readonly float4 NameHostile = new(1f, 0.43f, 0.39f, 1f);
    public static readonly float4 NamePrisoner = new(1f, 0.8f, 0.47f, 1f);
    public static readonly float4 NameDragon = new(1f, 0.67f, 0.47f, 1f);
    public static readonly float4 NameAnimal = new(0.75f, 0.84f, 0.65f, 1f);
    public static readonly float4 NamePerson = new(0.93f, 0.91f, 0.86f, 1f);

    public static ImColor8 U8(float4 c, float alpha = 1f) => Gui.U8(c, alpha);

    /// <summary>Global UI scale (fonts and sizes): <see cref="Gui.Scale"/>.</summary>
    public static float Scale => Gui.Scale;

    /// <summary>The whole look, for the shared kit (<see cref="UiTheme"/>).</summary>
    public static readonly UiTheme Look = new()
    {
        Palette = new UiPalette
        {
            Text = Text, Dim = Dim, Accent = Accent, AccentSoft = AccentSoft, Good = Good, Warn = Warn, Bad = Bad,
            Panel = Panel, Inset = Inset, Rim = Iron,
        },
        Frames = new UiFrames
        {
            Panel = "ui/frame_panel", Title = "ui/frame_title", Inset = "ui/frame_inset", Bar = "ui/bar_trough", KeyCap = "ui/keycap",
            Border = 6, PixelScale = 2f,
        },
        Rounding = 0.4f,
        FrameBorder = 1f,
        Colors = new Dictionary<ImGuiCol, float4>
        {
            [ImGuiCol.Border] = new float4(0.06f, 0.045f, 0.035f, 0.9f),
            [ImGuiCol.ChildBg] = new float4(0.075f, 0.055f, 0.042f, 0.88f),
            [ImGuiCol.FrameBg] = new float4(0.08f, 0.06f, 0.045f, 1f),
            [ImGuiCol.Button] = new float4(0.27f, 0.19f, 0.13f, 1f),
            [ImGuiCol.ButtonHovered] = new float4(0.42f, 0.27f, 0.15f, 1f),
            [ImGuiCol.ButtonActive] = new float4(0.62f, 0.36f, 0.15f, 1f),
            [ImGuiCol.MenuBarBg] = new float4(0.1f, 0.072f, 0.052f, 0.97f),
            [ImGuiCol.PopupBg] = new float4(0.12f, 0.085f, 0.062f, 0.98f),
        },
    };

    public static void Apply(float scale = 1f) => Look.Apply(scale);

    /// <summary>A section header in the heavier font, with a thin rule under it.</summary>
    public static void Header(Narsil.Ui.ImGuiLayer layer, string text) => Gui.Header(text);
}
