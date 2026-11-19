using Brutal.ImGuiApi;
using HeroesNotIncluded.Mods;
using HeroesNotIncluded.Ui;
using Narsil.Ui;

namespace Candlemaker;

/// <summary>
/// The candlemaker's game half (docs/MODDING.md): the dip drawn and heard. The candle hangs over the wax
/// vat; its outer layer glows warm when fresh and goes pale as it sets, the only sign of when to dip again.
/// </summary>
public sealed class CandlemakerGame : IGameMod
{
    public void Register(GameRegistry r)
    {
        r.WorkView<DipSession>(Draw);
        r.WorkSound<DipSession>(Sound);
    }

    private static float4 Mix(float4 a, float4 b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

    private static void Draw(WorkView view, DipSession s)
    {
        view.Begin(s.Title, ("F", "dip the wick"), ("Esc", "stop"));
        ImGuiText.Colored(Theme.Dim, $"{s.Dipped} of {DipSession.Layers} layers. Let each one set before the next.");

        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = new float2(Gui.S(220), Gui.S(220));
        var max = min + size;
        dl.AddRectFilled(in min, in max, Theme.U8(Theme.Inset), Gui.S(4), ImDrawFlags.None);

        // The vat of melted wax along the bottom.
        var vat0 = new float2(min.X + Gui.S(30), max.Y - Gui.S(46));
        var vat1 = new float2(max.X - Gui.S(30), max.Y - Gui.S(10));
        dl.AddRectFilled(in vat0, in vat1, Theme.U8(Theme.Oak), Gui.S(3), ImDrawFlags.None);
        var wax0 = new float2(vat0.X + Gui.S(5), vat0.Y + Gui.S(5));
        var wax1 = new float2(vat1.X - Gui.S(5), vat0.Y + Gui.S(12));
        dl.AddRectFilled(in wax0, in wax1, Theme.U8(Mix(Theme.Parchment, Theme.Accent, 0.55f)), Gui.S(2), ImDrawFlags.None);

        // The candle hanging from its wick over the vat: wider with each layer, its colour its warmth.
        var cx = (min.X + max.X) / 2f;
        var top = min.Y + Gui.S(36);
        var bottom = vat0.Y - Gui.S(14);
        var wickTop = new float2(cx, min.Y + Gui.S(12));
        var wickEnd = new float2(cx, top);
        dl.AddLine(in wickTop, in wickEnd, Theme.U8(Theme.Dim), Gui.S(2));
        if (s.Dipped > 0)
        {
            var half = Gui.S(3) + Gui.S(2.5f) * s.Dipped;
            var cool = Theme.Parchment;
            var warm = Mix(Theme.Parchment, Theme.Accent, 0.8f);
            var colour = Mix(cool, warm, s.Warmth);
            var c0 = new float2(cx - half, top);
            var c1 = new float2(cx + half, bottom);
            dl.AddRectFilled(in c0, in c1, Theme.U8(colour), Gui.S(3), ImDrawFlags.None);
            // Lumps where the soft wax dragged: drawn where they happened, the same every frame.
            for (var i = 0; i < s.Lumps; i++)
            {
                var y = top + (bottom - top) * (0.2f + 0.6f * ((i * 37 % 10) / 10f));
                var side = i % 2 == 0 ? -1f : 1f;
                var at = new float2(cx + side * half, y);
                dl.AddCircleFilled(in at, Gui.S(4) + Gui.S(0.4f) * s.Dipped, Theme.U8(colour), 12);
            }
        }
        else
        {
            var tip = new float2(cx, bottom);
            dl.AddLine(in wickEnd, in tip, Theme.U8(Theme.Dim), Gui.S(2));
        }

        view.TakesPointer(min, max); // a click on the picture is a dip
        ImGui.Dummy(in size);
        if (s.Lumpy)
            ImGuiText.Colored(Theme.Warn, "It's gone lumpy.");
        view.End();
    }

    /// <summary>A splash each dip (the game's own water sound for now: give the mod a sounds/sounds.json of its own).</summary>
    private static void Sound(WorkSoundContext ctx, DipSession s)
    {
        if (s.Dipped > ctx.A)
            ctx.Audio.Play("water.splash", gain: 0.35f);
        ctx.A = s.Dipped;
    }
}
