using Brutal.ImGuiApi;
using Narsil.Sim.Core;
using Narsil.Ui;
using SupportingCast.Sim.Systems;
using SupportingCast.Sim.Work;
using SupportingCast.Ui;

namespace SupportingCast;

// The forge's other scenes: the piece in the fire with the bellows, the quench with its steam, and
// the piece in your tongs between steps. Heat is only ever shown as colour, never as a number: a
// smith reads iron by eye. Everything here is read from the session or Perception.
public sealed partial class VillageGame
{
    private readonly record struct Puff(float2 Pos, float2 Vel, float Born, float Life, float Size);

    private readonly List<Puff> _steam = [];
    private float _steamDue;

    // Black through dull red, cherry, orange and yellow to white: what hot iron looks like.
    private static readonly (float Temp, float4 Colour)[] GlowRamp =
    [
        (430f, new float4(0.27f, 0.26f, 0.28f, 1f)),
        (520f, new float4(0.36f, 0.09f, 0.07f, 1f)),
        (650f, new float4(0.58f, 0.11f, 0.06f, 1f)),
        (760f, new float4(0.82f, 0.17f, 0.08f, 1f)),
        (870f, new float4(0.96f, 0.38f, 0.10f, 1f)),
        (1000f, new float4(1f, 0.62f, 0.17f, 1f)),
        (1150f, new float4(1f, 0.86f, 0.42f, 1f)),
        (1300f, new float4(1f, 0.97f, 0.88f, 1f)),
    ];

    /// <summary>The colour iron glows at a heat.</summary>
    private static float4 GlowColour(float temp)
    {
        if (temp <= GlowRamp[0].Temp)
            return GlowRamp[0].Colour;
        for (var i = 1; i < GlowRamp.Length; i++)
            if (temp <= GlowRamp[i].Temp)
            {
                var (t0, c0) = GlowRamp[i - 1];
                var (t1, c1) = GlowRamp[i];
                return c0 + (c1 - c0) * ((temp - t0) / (t1 - t0));
            }
        return GlowRamp[^1].Colour;
    }

    /// <summary>How much light hot iron throws, 0..1.</summary>
    private static float GlowStrength(float temp) => Math.Clamp((temp - 480f) / 700f, 0f, 1f);

    /// <summary>A small square of a heat's colour, inline with text.</summary>
    private static void Swatch(float temp)
    {
        var dl = ImGui.GetWindowDrawList();
        var size = new float2(ImGui.GetFrameHeight() * 0.8f, ImGui.GetFrameHeight() * 0.8f);
        var min = ImGui.GetCursorScreenPos() + new float2(0, ImGui.GetFrameHeight() * 0.1f);
        ImGui.Dummy(in size);
        var max = min + size;
        dl.AddRectFilled(in min, in max, Gui.U8(GlowColour(temp)), Gui.S(2), ImDrawFlags.None);
        dl.AddRect(in min, in max, Theme.U8(Theme.Iron), Gui.S(2), ImDrawFlags.None, Gui.S(1));
    }

    /// <summary>The piece glowing on the coals; the bellows pumping as you hold F; draw it out when it looks right.</summary>
    private void DrawHeat(HeatSession s)
    {
        BeginWork(s.Title, ("F", "hold to work the bellows"), ("D", "draw it out"), ("Esc", "leave it"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 170);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // The hearth: a bed of coals as hot as the fire, flickering.
        var fire = GlowColour(s.Fire);
        var bedMin = new float2(min.X + size.X * 0.28f, max.Y - Gui.S(54));
        var bedMax = new float2(max.X - Gui.S(18), max.Y - Gui.S(10));
        var glowAt = (bedMin + bedMax) * 0.5f;
        dl.AddCircleFilled(in glowAt, size.X * 0.32f, Gui.U8(fire, 0.12f + 0.2f * GlowStrength(s.Fire)), 40);
        dl.AddRectFilled(in bedMin, in bedMax, new ImColor8(26, 20, 18, 255), Gui.S(6), ImDrawFlags.None);
        for (var i = 0; i < 46; i++)
        {
            var hx = (i * 0.618034f) % 1f;
            var hy = (i * 0.381966f + 0.17f) % 1f;
            var c = new float2(bedMin.X + Gui.S(8) + hx * (bedMax.X - bedMin.X - Gui.S(16)), bedMin.Y + Gui.S(6) + hy * (bedMax.Y - bedMin.Y - Gui.S(12)));
            var flicker = 0.55f + 0.45f * MathF.Sin(_time * (3f + i % 5) + i * 1.7f);
            var lit = GlowStrength(s.Fire) * flicker;
            var coal = new float4(0.12f, 0.1f, 0.1f, 1f) + (fire - new float4(0.12f, 0.1f, 0.1f, 1f)) * lit;
            dl.AddCircleFilled(in c, Gui.S(4.5f + i % 3), Gui.U8(coal), 8);
        }

        // The piece, lying in the coals.
        var px = MathF.Max(1f, MathF.Round(3f * Gui.Scale));
        if (Gui.TrySprite("ui/work/billet", out var billet))
        {
            var b = new float2(billet.PixelSize.X, billet.PixelSize.Y) * px;
            var bMin = new float2(glowAt.X - b.X * 0.5f, bedMin.Y + Gui.S(10) - b.Y * 0.5f);
            var hot = GlowColour(s.Temp);
            var c = bMin + b * 0.5f;
            dl.AddCircleFilled(in c, b.X * 0.6f, Gui.U8(hot, 0.25f * GlowStrength(s.Temp)), 24);
            Gui.Image(dl, billet, bMin, bMin + b, Gui.U8(hot));
            if (s.Burning && (_time * 30f) % 1f < 0.5f)
                ThrowSparks(c, 0.6f, (int)(_time * 60f));
        }

        // The bellows on the left, squeezed while you pump, puffing air at the fire.
        if (Gui.TrySprite("buildings/bellows", out var bellows))
        {
            var squeeze = s.Pumping ? 0.5f + 0.5f * MathF.Sin(_time * 9f) : 1f;
            var bw = Gui.S(78) * (0.75f + 0.25f * squeeze);
            var bh = Gui.S(78);
            var bMin = new float2(min.X + Gui.S(14), max.Y - bh - Gui.S(18));
            Gui.Image(dl, bellows, bMin, bMin + new float2(bw, bh), Gui.White);
            if (s.Pumping)
                for (var i = 0; i < 3; i++)
                {
                    var t = (_time * 1.8f + i / 3f) % 1f;
                    var a = new float2(bMin.X + bw + t * Gui.S(60), bMin.Y + bh * 0.55f + (i - 1) * Gui.S(6));
                    var z = a + new float2(Gui.S(14), 0);
                    dl.AddLine(in a, in z, new ImColor8(235, 225, 210, (byte)(140 * (1f - t))), Gui.S(1.5f));
                }
        }
        DrawSparks(dl);

        // What it should look like to work: the colours, not the numbers.
        ImGui.Spacing();
        ImGui.AlignTextToFramePadding();
        TextC(Theme.Dim, "Work it from");
        ImGui.SameLine(0, 6);
        Swatch((s.Lo + Math.Min(s.Hi, s.Lo + 60f)) * 0.5f);
        ImGui.SameLine(0, 6);
        TextC(Theme.Dim, "to");
        ImGui.SameLine(0, 6);
        Swatch(s.Hi);
        if (s.Burning)
        {
            ImGui.SameLine(0, 14);
            TextC(Theme.Bad, "It's throwing sparks: it's burning!");
        }
        EndWork();
    }

    /// <summary>The tub, the piece going in and the steam: lift it out as the roar dies to a hiss.</summary>
    private void DrawQuench(QuenchSession s)
    {
        BeginWork(s.Title, ("F", "hold to keep it under; let go to lift it out"), ("Esc", "leave it"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 170);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // The tub: staves and dark water.
        var tubMin = new float2(min.X + size.X * 0.22f, min.Y + size.Y * 0.5f);
        var tubMax = new float2(max.X - size.X * 0.22f, max.Y - Gui.S(8));
        var surface = tubMin.Y + Gui.S(10);
        dl.AddRectFilled(in tubMin, in tubMax, new ImColor8(96, 66, 40, 255), Gui.S(4), ImDrawFlags.None);
        var waterMin = new float2(tubMin.X + Gui.S(6), surface);
        var waterMax = new float2(tubMax.X - Gui.S(6), tubMax.Y - Gui.S(6));
        dl.AddRectFilled(in waterMin, in waterMax, new ImColor8(40, 66, 80, 255), Gui.S(2), ImDrawFlags.None);
        for (var x = tubMin.X + Gui.S(18); x < tubMax.X; x += Gui.S(22))
        {
            var a = new float2(x, tubMin.Y);
            var z = new float2(x, tubMax.Y);
            dl.AddLine(in a, in z, new ImColor8(70, 46, 28, 255), Gui.S(1));
        }

        // The piece, held over the water or plunged into it.
        var px = MathF.Max(1f, MathF.Round(3f * Gui.Scale));
        var under = s.Held;
        if (Gui.TrySprite("ui/work/billet", out var billet))
        {
            var b = new float2(billet.PixelSize.X, billet.PixelSize.Y) * px;
            var centre = new float2((waterMin.X + waterMax.X) * 0.5f, under ? surface + Gui.S(24) : surface - Gui.S(30));
            var bMin = centre - b * 0.5f;
            var hot = GlowColour(s.Temp);
            if (!under)
                dl.AddCircleFilled(in centre, b.X * 0.6f, Gui.U8(hot, 0.25f * GlowStrength(s.Temp)), 24);
            Gui.Image(dl, billet, bMin, bMin + b, Gui.U8(hot, under ? 0.75f : 1f));
            // The tongs, from the smith's hand off to the right.
            var hand = new float2(max.X - Gui.S(16), centre.Y - Gui.S(30));
            var jaw = new float2(bMin.X + b.X, centre.Y);
            dl.AddLine(in hand, in jaw, Theme.U8(Theme.Iron), Gui.S(3));
        }

        // Steam: a roar of it, thinning to a wisp as the piece cools. Bubbles while it's fierce.
        var dt = ImGui.GetIO().DeltaTime;
        _steamDue += dt * 90f * s.Steam;
        var seed = (int)(_time * 1000f);
        while (_steamDue >= 1f)
        {
            _steamDue -= 1f;
            var r = new SimRandom((ulong)(seed++ * 2654435761u + 7));
            var at = new float2(waterMin.X + (0.3f + 0.4f * r.NextFloat()) * (waterMax.X - waterMin.X), surface);
            _steam.Add(new Puff(at, new float2(r.Range(-20f, 20f), r.Range(-90f, -50f)) * Gui.Scale, _time, r.Range(0.6f, 1.4f), Gui.S(r.Range(6f, 14f))));
        }
        _steam.RemoveAll(p => _time - p.Born > p.Life);
        foreach (var p in _steam)
        {
            var t = _time - p.Born;
            var at = p.Pos + p.Vel * t;
            var fade = 1f - t / p.Life;
            dl.AddCircleFilled(in at, p.Size * (1f + t), new ImColor8(230, 232, 236, (byte)(110 * fade)), 12);
        }
        if (s.Steam > 0.35f)
            for (var i = 0; i < 5; i++)
            {
                var c = new float2(waterMin.X + (0.35f + 0.06f * i) * (waterMax.X - waterMin.X) + MathF.Sin(_time * 13f + i) * Gui.S(4), surface + Gui.S(2));
                dl.AddCircle(in c, Gui.S(2 + i % 2), new ImColor8(190, 210, 220, 200), 8, Gui.S(1));
            }
        EndWork();
    }

    /// <summary>Between steps: what's in your tongs, how it glows, how far it's shaped, and where it goes next.</summary>
    private void DrawTongs()
    {
        if (!HasAvatar || View.Piece(_avatar) is not { } p)
            return;
        _hud.Next(Anchor.Bottom);
        Panel.Begin("In your tongs###tongs", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav);
        _hud.Took();
        ImGui.AlignTextToFramePadding();
        Swatch(p.Temp);
        ImGui.SameLine(0, 8);
        ImGui.Text(Cap(p.Label));
        ImGui.SameLine(0, 10);
        TextC(p.Hot ? Theme.Good : Theme.Dim, p.Next is { } next ? Cap(next) + "." : "");
        if (!p.Shaped)
            Bar(p.Mend ? "Mended" : "Shaped", p.Shape, Theme.Accent);
        if (ImGui.SmallButton("Set it aside"u8))
            Issue(new SetAsidePieceCommand(_avatar));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip(p.Mend ? "Stop the mend; the piece stays as it is." : "Stop the work; its materials are set down by the forge.");
        Panel.End();
    }
}
