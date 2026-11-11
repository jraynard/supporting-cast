using Brutal.ImGuiApi;
using Narsil;
using Narsil.Rendering;
using DragonWorld.Rendering;

namespace DragonWorld.Ui;

/// <summary>
/// Pieces the character and dragon creators share: a colour row (round preset swatches plus a picker circle) and
/// a framed, animated preview of a layered sheet drawn from the same tinted layers the world uses.
/// </summary>
public static class LookWidgets
{
    private const float Slot = 30f;
    private const float Radius = 11f;

    /// <summary>The last colour each row's picker made, so it stays on the picker circle after a preset is chosen.</summary>
    private static readonly Dictionary<int, uint> LastCustom = [];

    /// <summary>
    /// A label and a row of round swatches: the presets, then a circle holding a custom colour with an
    /// eyedropper on it that opens a colour picker. Whatever is picked wears a white ring (the picker
    /// circle when the colour isn't a preset). Colours are 0xRRGGBB.
    /// </summary>
    public static void ColorRow(string label, int id, ref uint rgb, uint[] presets)
    {
        ImGui.PushID(id);
        ImGui.TextColored(Theme.Dim, label);
        rgb &= 0xFFFFFF;
        var custom = Array.IndexOf(presets, rgb) < 0;
        for (var i = 0; i < presets.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, 2);
            ImGui.PushID(i);
            if (Swatch(presets[i], rgb == presets[i], false))
                rgb = presets[i];
            ImGui.PopID();
        }

        // The picker circle shows the custom colour in use, else the last one made, else a colour wheel.
        if (custom)
            LastCustom[id] = rgb;
        var shown = LastCustom.TryGetValue(id, out var last) ? (uint?)last : null;
        ImGui.SameLine(0, 2);
        if (Swatch(shown, custom, true))
        {
            if (!custom && shown is { } again)
                rgb = again;
            ImGui.OpenPopup("##picker"u8);
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Narsil.Ui.ImGuiText.Tooltip("Pick any colour");
        if (ImGui.BeginPopup("##picker"u8))
        {
            var c = HumanLook.ToFloat3(rgb);
            if (ImGui.ColorPicker3("##wheel"u8, ref c, ImGuiColorEditFlags.NoSidePreview))
            {
                rgb = HumanLook.FromFloat3(c);
                LastCustom[id] = rgb;
            }
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    /// <summary>One round button: filled with <paramref name="rgb"/> (a colour wheel when null), ringed in white when picked, with the eyedropper when it's the picker.</summary>
    public static bool Swatch(uint? rgb, bool picked, bool dropper)
    {
        var size = new float2(Slot, Slot);
        var min = ImGui.GetCursorScreenPos();
        var pressed = ImGui.InvisibleButton("##swatch"u8, in size, ImGuiButtonFlags.None);
        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.None);
        var dl = ImGui.GetWindowDrawList();
        var center = min + new float2(Slot / 2, Slot / 2);
        if (rgb is { } fill)
            dl.AddCircleFilled(in center, Radius, new ImColor8((byte)(fill >> 16), (byte)(fill >> 8), (byte)fill, 255), 32);
        else
            ColorWheel(dl, center, Radius);
        if (picked)
            dl.AddCircle(in center, Radius + 2.5f, new ImColor8(255, 255, 255, 255), 32, 2.5f);
        else
            dl.AddCircle(in center, Radius, hovered ? new ImColor8(200, 200, 200, 255) : new ImColor8(0, 0, 0, 150), 32, hovered ? 2f : 1f);
        if (dropper)
            Eyedropper(dl, center, rgb is { } c && Luma(c) > 0.55f);
        return pressed;
    }

    private static float Luma(uint rgb) => (0.299f * (rgb >> 16 & 0xFF) + 0.587f * (rgb >> 8 & 0xFF) + 0.114f * (rgb & 0xFF)) / 255f;

    /// <summary>Hue wedges around a disc: what the picker circle shows before any custom colour exists.</summary>
    private static void ColorWheel(ImDrawListPtr dl, float2 center, float radius)
    {
        const int wedges = 18;
        for (var k = 0; k < wedges; k++)
        {
            var a0 = k * MathF.Tau / wedges;
            var a1 = (k + 1) * MathF.Tau / wedges;
            var p0 = center + new float2(MathF.Cos(a0), MathF.Sin(a0)) * radius;
            var p1 = center + new float2(MathF.Cos(a1), MathF.Sin(a1)) * radius;
            var rgb = HumanLook.FromFloat3(Hue(k / (float)wedges));
            dl.AddTriangleFilled(in center, in p0, in p1, new ImColor8((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255));
        }
    }

    private static float3 Hue(float h)
    {
        float Ch(float n) => Math.Clamp(MathF.Abs((h * 6 + n) % 6 - 3) - 1, 0, 1);
        return new float3(Ch(0), Ch(4), Ch(2));
    }

    /// <summary>A pipette drawn from strokes, tip at the lower left; dark on light colours, white on dark ones.</summary>
    private static void Eyedropper(ImDrawListPtr dl, float2 c, bool dark)
    {
        var ink = dark ? new ImColor8(20, 20, 24, 255) : new ImColor8(255, 255, 255, 255);
        var halo = dark ? new ImColor8(255, 255, 255, 110) : new ImColor8(0, 0, 0, 140);
        var tip = c + new float2(-5.5f, 5.5f);
        var neck = c + new float2(-2.5f, 2.5f);
        var top = c + new float2(2.5f, -2.5f);
        var bulb = c + new float2(4.5f, -4.5f);
        var capA = c + new float2(2.5f, -6.5f);
        var capB = c + new float2(6.5f, -2.5f);
        foreach (var (color, extra) in new[] { (halo, 1.6f), (ink, 0f) })
        {
            dl.AddLine(in tip, in neck, color, 1.2f + extra);
            dl.AddLine(in neck, in top, color, 2.4f + extra);
            dl.AddCircleFilled(in bulb, 1.8f + extra / 2, color, 12);
            dl.AddLine(in capA, in capB, color, 1.6f + extra);
        }
    }

    /// <summary>
    /// A sheet's <paramref name="tag"/> animation at <paramref name="scale"/>× native size in a
    /// framed box, base frame first, then each layer (by frame index, placed at its offset).
    /// </summary>
    public static void Preview(GameHost host, SpriteSheet? sheet, IEnumerable<(SpriteSheet Sheet, uint Color)> layers, string tag, float time, float scale)
    {
        scale /= sheet?.Density ?? 1f;                  // a finer sheet shows at the same size
        var frameSize = sheet?.FrameSize ?? new int2(32, 32);
        var box = new float2(frameSize.X * scale + 16, frameSize.Y * scale + 16);
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var max = min + box;
        dl.AddRectFilled(in min, in max, new ImColor8(40, 30, 24, 220), 8f, ImDrawFlags.None);
        dl.AddRect(in min, in max, Theme.U8(Theme.AccentSoft), 8f, ImDrawFlags.None, 1.5f);
        if (sheet?.Find(tag) is { } anim)
        {
            var frame = anim.Frames[anim.FrameAt(time)];
            var at = min + new float2(8, 8);
            Image(host, dl, frame, at, scale, Rgba.White);
            foreach (var (layer, color) in layers)
                if (HumanLook.LayerFrame(sheet, layer, frame) is { } lf)
                    Image(host, dl, lf, at, scale, color);
        }
        ImGui.Dummy(in box);
    }

    private static void Image(GameHost host, ImDrawListPtr dl, in SheetFrame f, float2 at, float scale, uint color)
    {
        var a = at + new float2(f.Offset.X, f.Offset.Y) * scale;
        var b = a + new float2(f.Sprite.PixelSize.X, f.Sprite.PixelSize.Y) * scale;
        var (texture, page0, page1) = host.AtlasImage(f.Sprite);
        float2? uv0 = page0;
        float2? uv1 = page1;
        var tint = new ImColor8((byte)(color & 0xFF), (byte)(color >> 8 & 0xFF), (byte)(color >> 16 & 0xFF), (byte)(color >> 24));
        dl.AddImage(texture, in a, in b, in uv0, in uv1, tint);
    }
}
