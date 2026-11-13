using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

/// <summary>
/// A small compass in the bottom right. The camera turns, and the town's talk is all of roads by
/// their side ("raiders on the north road"), so the needle shows where north (-y on the map, as its rows run) is on
/// screen right now.
/// </summary>
public sealed partial class VillageGame
{
    private const float CompassSize = 76f;

    private void DrawCompass()
    {
        // North and east on screen: where a step of -y (+x) from the camera's target lands.
        var from = _camera.WorldToScreen(_camera.Target);
        if (ScreenDir(_camera.WorldToScreen(_camera.Target + new float3(0, -1, 0)) - from) is not { } north
            || ScreenDir(_camera.WorldToScreen(_camera.Target + new float3(1, 0, 0)) - from) is not { } east)
            return;

        _hud.Next(Anchor.BottomRight);
        Panel.Begin("##compass", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoScrollbar);
        _hud.Took();
        var size = Gui.S(CompassSize, CompassSize);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var dl = ImGui.GetWindowDrawList();
        var centre = min + size * 0.5f;
        var r = size.X * 0.5f;
        dl.AddCircleFilled(in centre, r, Theme.U8(Theme.Inset), 40);
        dl.AddCircle(in centre, r, Theme.U8(Theme.OakLight), 40, Gui.S(1.5f));

        // The needle: north half in the accent, south half dim.
        var tip = centre + north * (r * 0.5f);
        var tail = centre - north * (r * 0.5f);
        var wing = east * (r * 0.13f);
        var a = centre + wing;
        var b = centre - wing;
        dl.AddTriangleFilled(in tip, in a, in b, Theme.U8(Theme.Accent));
        dl.AddTriangleFilled(in tail, in b, in a, Theme.U8(Theme.Dim, 0.7f));

        // The four letters round the rim.
        Letter(dl, centre + north * (r * 0.76f), "N", Theme.Accent);
        Letter(dl, centre - north * (r * 0.76f), "S", Theme.Dim);
        Letter(dl, centre + east * (r * 0.76f), "E", Theme.Dim);
        Letter(dl, centre - east * (r * 0.76f), "W", Theme.Dim);
        Panel.End();

        static float2? ScreenDir(float2 d)
        {
            var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
            return len < 1e-4f ? null : d * (1f / len);
        }

        static void Letter(ImDrawListPtr dl, float2 at, string text, float4 color)
        {
            var half = ImGui.CalcTextSize(text) * 0.5f;
            var p = at - half;
            dl.AddText(in p, Theme.U8(color), text);
        }
    }
}
