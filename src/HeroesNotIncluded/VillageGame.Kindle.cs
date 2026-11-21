using Brutal.ImGuiApi;
using HeroesNotIncluded.Sim.Work;
using HeroesNotIncluded.Ui;
using Narsil.Ui;

namespace HeroesNotIncluded;

// Lighting a cold forge (KindleSession), drawn plainly for now: the flint and the swinging steel on the
// left, the tinder on the right, the ember in it glowing up as it's blown, the breath as wisps toward it,
// and your lungs as a bar. Everything here is read from the session; nothing touches the sim.
public sealed partial class VillageGame
{
    private long _kindleSession = -1;
    private int _kindleStrikes;
    private float _kindleStruckAt = -10f;

    private void DrawKindle(KindleSession s)
    {
        BeginWork(s.Title, s.Ember
            ? [("F", "short breaths: hold to blow, let go before it gusts"), ("Esc", "leave it")]
            : [("F", "strike as the steel comes down on the flint"), ("Esc", "leave it")]);
        if (s.StartTick != _kindleSession)
            (_kindleSession, _kindleStrikes) = (s.StartTick, s.Strikes);
        if (s.Strikes > _kindleStrikes)
            (_kindleStrikes, _kindleStruckAt) = (s.Strikes, _time);

        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 170);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // Your breath: what's left in the lungs, at the left edge.
        var lungMin = new float2(min.X + Gui.S(14), min.Y + Gui.S(20));
        var lungMax = new float2(lungMin.X + Gui.S(10), max.Y - Gui.S(20));
        dl.AddRect(in lungMin, in lungMax, Theme.U8(Theme.Dim), Gui.S(2), ImDrawFlags.None, Gui.S(1.5f));
        var full = new float2(lungMin.X + Gui.S(2), lungMax.Y - (lungMax.Y - lungMin.Y - Gui.S(4)) * s.Breath - Gui.S(2));
        var fullMax = new float2(lungMax.X - Gui.S(2), lungMax.Y - Gui.S(2));
        dl.AddRectFilled(in full, in fullMax, Theme.U8(s.Breath < 0.2f ? Theme.Bad : Theme.Parchment, 0.8f), Gui.S(1), ImDrawFlags.None);

        // The flint, and the steel swinging down onto it.
        var flint = new float2(min.X + size.X * 0.3f, min.Y + size.Y * 0.62f);
        var flintMin = flint - Gui.S(26, 14);
        var flintMax = flint + Gui.S(26, 14);
        dl.AddRectFilled(in flintMin, in flintMax, new ImColor8(118, 122, 130, 255), Gui.S(6), ImDrawFlags.None);
        var pivot = new float2(flint.X - Gui.S(70), flint.Y - Gui.S(70));
        if (!s.Ember)
        {
            // The strike: where the steel's edge meets the flint, a faint arc it must come through.
            static float Angle(float t) => -0.2f + 1.55f * t; // radians round the pivot: raised to down past the flint
            var reach = Gui.S(92);
            for (var k = 0; k <= 8; k++)
            {
                var a0 = Angle(KindleSession.StrikeAt - KindleSession.StrikeHalfWidth + 2f * KindleSession.StrikeHalfWidth * k / 9f);
                var a1 = Angle(KindleSession.StrikeAt - KindleSession.StrikeHalfWidth + 2f * KindleSession.StrikeHalfWidth * (k + 1) / 9f);
                var p0 = pivot + new float2(MathF.Cos(a0), MathF.Sin(a0)) * reach;
                var p1 = pivot + new float2(MathF.Cos(a1), MathF.Sin(a1)) * reach;
                dl.AddLine(in p0, in p1, Theme.U8(Theme.Accent, 0.35f), Gui.S(5));
            }
            var a = Angle(s.Steel);
            var tip = pivot + new float2(MathF.Cos(a), MathF.Sin(a)) * reach;
            var grip = pivot + new float2(MathF.Cos(a), MathF.Sin(a)) * Gui.S(40);
            dl.AddLine(in grip, in tip, Theme.U8(s.OnTheStrike ? Theme.Accent : Theme.Iron), Gui.S(7));
        }

        // Sparks off the last strike, a moment long.
        var since = _time - _kindleStruckAt;
        if (since < 0.35f && s.LastStrike is 1 or 2)
        {
            for (var i = 0; i < 9; i++)
            {
                var ang = -0.4f + i * 0.22f;
                var dist = Gui.S(20 + 110 * since / 0.35f) * (0.6f + 0.4f * ((i * 7) % 5) / 4f);
                var p = flint + new float2(MathF.Cos(ang) * dist, -MathF.Sin(ang) * dist * 0.5f);
                dl.AddCircleFilled(in p, Gui.S(2.5f), Theme.U8(Theme.Accent, 1f - since / 0.35f), 6);
            }
        }

        // The tinder, and the ember in it.
        var nest = new float2(min.X + size.X * 0.74f, min.Y + size.Y * 0.62f);
        dl.AddCircleFilled(in nest, Gui.S(34), new ImColor8(122, 92, 58, 255), 24);
        dl.AddCircle(in nest, Gui.S(34), new ImColor8(90, 66, 40, 255), 24, Gui.S(3));
        if (s.Ember)
        {
            // Dull red to yellow-white as it comes up; a soft halo once it's near catching.
            var g = s.Glow;
            var col = new float4(0.55f + 0.45f * g, 0.12f + 0.75f * g * g, 0.05f + 0.45f * g * g * g, 1f);
            if (g > 0.5f)
                dl.AddCircleFilled(in nest, Gui.S(14 + 26 * g), Gui.U8(new float4(col.X, col.Y, col.Z, 0.18f * g)), 24);
            dl.AddCircleFilled(in nest, Gui.S(6 + 14 * g), Gui.U8(col), 20);

            // The breath, as wisps blowing at it: more of them the harder it comes, red once it's a gust.
            var gust = s.Draft > KindleSession.GustAt;
            var wisps = (int)MathF.Round(s.Draft * 6f);
            for (var i = 0; i < wisps; i++)
            {
                var y = nest.Y - Gui.S(24) + i * Gui.S(9);
                var from = new float2(nest.X - Gui.S(120) + (float)((_time * 160f + i * 37f) % 40f) * Gui.Scale, y);
                var to = from + new float2(Gui.S(46), 0);
                dl.AddLine(in from, in to, Theme.U8(gust ? Theme.Bad : Theme.Parchment, 0.65f), Gui.S(2));
            }
        }

        ImGui.Spacing();
        if (s.Ember)
            TextC(s.Draft > KindleSession.GustAt ? Theme.Bad : Theme.Dim,
                s.Draft > KindleSession.GustAt ? "Too hard: it's scattering." : s.Gentle ? "It takes the breath." : "Blow on it, gently.");
        else
            TextC(Theme.Dim, s.LastStrike switch
            {
                1 => "A spark took.",
                2 => "Sparks, but the tinder didn't take one.",
                3 => "The steel missed the flint.",
                _ => "Strike a spark into the tinder.",
            });
        if (s.Lost > 0)
            TextC(Theme.Dim, s.Lost == 1 ? "One ember lost." : $"{s.Lost} embers lost.");
        EndWork();
    }
}
