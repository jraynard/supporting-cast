using Brutal.ImGuiApi;
using Narsil.Platform;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// A shop's sign, read from the street: a painted board with its name and its prices. What's on it
// is painted up for anyone to see (Shops.Signs), so it's read straight from the sim.
public sealed partial class VillageGame
{
    /// <summary>The shop whose sign you're reading, or Entity.Null.</summary>
    private Entity _readingSign = Entity.Null;

    private static readonly float4 SignWood = new(0.36f, 0.22f, 0.12f, 1f);
    private static readonly float4 SignWoodDark = new(0.23f, 0.13f, 0.07f, 1f);
    private static readonly float4 SignPaint = new(0.98f, 0.86f, 0.55f, 1f);
    private static readonly float4 SignGilt = new(0.99f, 0.72f, 0.3f, 1f);

    /// <summary>
    /// The sign as a board: planks framed in dark wood with iron nails at the corners, hung on two
    /// chains; the name in big gilt letters, a flourish under it, then each price with a dotted
    /// leader to the silver. Closes if you walk off, on Esc, or on F again.
    /// </summary>
    private void DrawSignWindow()
    {
        if (_readingSign.IsNull)
            return;
        var w = _sim.World;
        if (!HasAvatar || !w.IsAlive(_readingSign) || Signs.CellOf(_sim, _readingSign) is not { } at
            || !AvatarSystem.InReach(w.Get<Position>(_avatar).Cell, at) || Host.Input.WasKeyPressed(GlfwKey.Escape))
        {
            _readingSign = Entity.Null;
            return;
        }
        var sign = Signs.Read(_sim, _readingSign);
        var width = ScreenFit.Width(460, 0.6f);
        var rowH = Gui.S(30);
        var rows = Math.Max(1, sign.Prices.Count);
        var height = Gui.S(176) + rows * rowH;
        var size = new float2(width, height);
        var pos = ImGui.GetIO().DisplaySize * 0.5f - new float2(width * 0.5f, height * 0.62f);
        pos.Y = MathF.Max(pos.Y, Gui.S(10)); // a long price list still starts on screen
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Always, default);
        ImGui.SetNextWindowSize(in size, ImGuiCond.Always);
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoNav;
        if (!ImGui.Begin("###shopsign"u8, flags))
        {
            ImGui.End();
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos();
        var max = min + size;

        // The chains it hangs from, then the board: planks inside a dark frame, nails at the corners.
        var boardMin = min + new float2(0, Gui.S(26));
        foreach (var x in new[] { min.X + width * 0.22f, min.X + width * 0.78f })
            for (var y = min.Y; y < boardMin.Y; y += Gui.S(7))
            {
                var c = new float2(x, y + Gui.S(3));
                dl.AddCircle(in c, Gui.S(3), Theme.U8(Theme.Iron), 8, Gui.S(1.5f));
            }
        dl.AddRectFilled(in boardMin, in max, Gui.U8(SignWoodDark), Gui.S(8), ImDrawFlags.None);
        var inner0 = boardMin + Gui.S(10, 10);
        var inner1 = max - Gui.S(10, 10);
        dl.AddRectFilled(in inner0, in inner1, Gui.U8(SignWood), Gui.S(4), ImDrawFlags.None);
        for (var y = inner0.Y + Gui.S(22); y < inner1.Y; y += Gui.S(22))
        {
            var a = new float2(inner0.X, y);
            var b = new float2(inner1.X, y);
            dl.AddLine(in a, in b, Gui.U8(SignWoodDark, 0.55f), Gui.S(1.2f));
        }
        foreach (var c in new[] { inner0 + Gui.S(8, 8), new float2(inner1.X - Gui.S(8), inner0.Y + Gui.S(8)),
                     new float2(inner0.X + Gui.S(8), inner1.Y - Gui.S(8)), inner1 - Gui.S(8, 8) })
            dl.AddCircleFilled(in c, Gui.S(3.5f), Theme.U8(Theme.Iron), 10);

        // The name, big and gilt, with a shadow so it reads as painted on.
        float y0 = inner0.Y + Gui.S(16);
        using (Gui.HeaderFont(Gui.S(36)))
        {
            var ts = ImGui.CalcTextSize(sign.Name);
            var shrink = Math.Min(1f, (inner1.X - inner0.X - Gui.S(30)) / Math.Max(1f, ts.X));
            using (Gui.HeaderFont(Gui.S(36) * shrink))
            {
                ts = ImGui.CalcTextSize(sign.Name);
                var p = new float2((min.X + max.X - ts.X) * 0.5f, y0);
                var shadow = p + Gui.S(2, 2);
                dl.AddText(in shadow, Gui.U8(SignWoodDark), sign.Name);
                dl.AddText(in p, Gui.U8(SignGilt), sign.Name);
                y0 += ts.Y;
            }
        }
        // What it is, small, and a flourish.
        var sub = Capitalized(sign.What);
        var subSize = ImGui.CalcTextSize(sub);
        var subAt = new float2((min.X + max.X - subSize.X) * 0.5f, y0 + Gui.S(2));
        dl.AddText(in subAt, Gui.U8(SignPaint, 0.75f), sub);
        y0 = subAt.Y + subSize.Y + Gui.S(10);
        var mid = (min.X + max.X) * 0.5f;
        var r0 = new float2(mid - width * 0.3f, y0);
        var r1 = new float2(mid + width * 0.3f, y0);
        dl.AddLine(in r0, in r1, Gui.U8(SignGilt, 0.7f), Gui.S(1.5f));
        var diamond = new float2(mid, y0);
        dl.AddCircleFilled(in diamond, Gui.S(4), Gui.U8(SignGilt), 4);
        y0 += Gui.S(14);

        // The prices, each with a dotted leader across to the silver.
        var left = inner0.X + Gui.S(26);
        var right = inner1.X - Gui.S(26);
        if (sign.Prices.Count == 0)
        {
            var closed = "Nothing on offer today.";
            var cs = ImGui.CalcTextSize(closed);
            var cp = new float2(mid - cs.X * 0.5f, y0 + Gui.S(4));
            dl.AddText(in cp, Gui.U8(SignPaint, 0.8f), closed);
        }
        foreach (var price in sign.Prices)
        {
            var label = Capitalized(price.Label);
            var cost = $"{price.Price} silver";
            var ls = ImGui.CalcTextSize(label);
            var cs = ImGui.CalcTextSize(cost);
            var coin = Gui.S(16);
            var lp = new float2(left, y0 + (rowH - ls.Y) * 0.5f);
            var cp = new float2(right - cs.X, y0 + (rowH - cs.Y) * 0.5f);
            dl.AddText(in lp, Gui.U8(SignPaint), label);
            dl.AddText(in cp, Gui.U8(SignGilt), cost);
            if (Gui.TrySprite("ui/icon_coin", out var coinSprite))
                Gui.Sprite(dl, coinSprite, new float2(cp.X - coin - Gui.S(6), y0 + (rowH - coin) * 0.5f), new float2(coin, coin));
            for (var x = lp.X + ls.X + Gui.S(8); x < cp.X - coin - Gui.S(12); x += Gui.S(7))
            {
                var dot = new float2(x, y0 + rowH * 0.62f);
                dl.AddCircleFilled(in dot, Gui.S(1.2f), Gui.U8(SignPaint, 0.45f), 4);
            }
            y0 += rowH;
        }
        var hint = "F or Esc to step back";
        var hs = ImGui.CalcTextSize(hint);
        var hp = new float2(mid - hs.X * 0.5f, inner1.Y - hs.Y - Gui.S(6));
        dl.AddText(in hp, Gui.U8(SignPaint, 0.4f), hint);
        ImGui.End();
    }
}
