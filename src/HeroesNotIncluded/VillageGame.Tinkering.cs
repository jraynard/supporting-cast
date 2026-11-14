using Brutal.ImGuiApi;
using Brutal.GlfwApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The tinker's: pinning locks and filing keys, fitting contraptions together, truing wagon wheels,
// curios under the loupe; the window at a door's lock or a party's wagon; and what can be done
// with locks, keys and curios from the pack. Curios and locks come through View (Perception), which
// never says what a curio's really worth or how tight someone else's lock is.
public sealed partial class VillageGame
{
    /// <summary>The door or wagon whose window is open (F at it), or null.</summary>
    private Entity _tinkerAt = Entity.Null;

    /// <summary>The curio whose worth you're talking over (quote, offer), or null; and the sliders' silver.</summary>
    private Entity _curioTalk = Entity.Null;
    private int _quote = 20, _offer = 15;

    // ---------------- The games ----------------

    /// <summary>The pins as columns, each filed down toward its mark; the one in the file, and how each set one sits.</summary>
    private void DrawPin(PinSession s)
    {
        BeginWork(s.Title, ("F", "hold to file"), ("Space", "set the pin, take up the next"), ("Esc", "stop"));
        TextC(Theme.Dim, "File to the mark: the longer a stroke, the harder the file bites. Past the mark, it's sloppy; far past, ruined.");
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var w = Gui.S(40);
        var h = Gui.S(130);
        ImGui.Dummy(new float2(Gui.S(20) + s.Pins * (w + Gui.S(12)), h + Gui.S(6)));
        for (var i = 0; i < s.Pins; i++)
        {
            var colMin = new float2(origin.X + Gui.S(10) + i * (w + Gui.S(12)), origin.Y);
            var colMax = new float2(colMin.X + w, origin.Y + h);
            dl.AddRectFilled(in colMin, in colMax, Theme.U8(Theme.Well), Gui.S(3), ImDrawFlags.None);
            if (i > s.Pin)
                continue;
            var length = i < s.Pin ? Math.Clamp(s.Mark - s.Errors[i], 0f, 1f) : s.Length;
            var top = colMax.Y - length * (h - Gui.S(8));
            var pinMin = new float2(colMin.X + Gui.S(10), top);
            var pinMax = new float2(colMax.X - Gui.S(10), colMax.Y - Gui.S(2));
            var colour = i < s.Pin ? (s.Accuracy(s.Errors[i]) > 0.7f ? Theme.Good : s.Errors[i] >= PinSession.Ruined ? Theme.Bad : Theme.Warn) : Theme.Iron;
            dl.AddRectFilled(in pinMin, in pinMax, Theme.U8(colour), Gui.S(2), ImDrawFlags.None);
            if (i == s.Pin)
            {
                // The mark scribed on the pin's side: the maker sees where it goes.
                var y = colMax.Y - s.Mark * (h - Gui.S(8));
                var a = new float2(colMin.X + Gui.S(2), y);
                var b = new float2(colMax.X - Gui.S(2), y);
                dl.AddLine(in a, in b, Theme.U8(Theme.Accent), Gui.S(2));
                // The file across the pin's end, stroking while you file.
                if (Gui.TrySprite("ui/work/rasp", out var rasp))
                {
                    var rpx = MathF.Max(1f, MathF.Round(2f * Gui.Scale));
                    var stroke = s.Held ? MathF.Sin(_time * 14f) * 6f * rpx : 0f;
                    var rMin = new float2((colMin.X + colMax.X) * 0.5f - 20 * rpx + stroke, top - 7 * rpx);
                    Gui.Image(dl, rasp, rMin, rMin + new float2(34 * rpx, 7 * rpx), Gui.White);
                }
            }
        }
        EndWork();
    }

    /// <summary>Fitting: the part over its seat in section. Winding: the ratchet, its pawl and the spring.</summary>
    private void DrawFit(FitSession s)
    {
        if (s.Winding)
        {
            DrawWinding(s);
            return;
        }
        BeginWork(s.Title, ("A/D", "keep it square over the seat"), ("F", "hold to press it in"), ("Esc", "stop"));
        var name = PartName(s, s.Part);
        TextC(Theme.Text, s.Settling > 0 ? $"{Cap(name)}: seated." : $"Fitting the {name}.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(420, 170);
        ImGui.Dummy(in size);
        var mid = min.X + size.X * 0.5f;
        var travel = Gui.S(110);                   // how far a part off true by 1 sits from the middle
        var partW = Gui.S(46);
        var partH = Gui.S(78);
        var lip = min.Y + Gui.S(78);               // the top of the seat
        var sink = Gui.S(66);                      // how far a part goes down into it
        var bore = partW * 0.5f + Gui.S(3);

        // The housing either side of the bore, with the scribed mark over its middle.
        var leftMin = new float2(min.X + Gui.S(40), lip);
        var leftMax = new float2(mid - bore, min.Y + size.Y);
        var rightMin = new float2(mid + bore, lip);
        var rightMax = new float2(min.X + size.X - Gui.S(40), min.Y + size.Y);
        dl.AddRectFilled(in leftMin, in leftMax, Theme.U8(Theme.Iron), Gui.S(2), ImDrawFlags.None);
        dl.AddRectFilled(in rightMin, in rightMax, Theme.U8(Theme.Iron), Gui.S(2), ImDrawFlags.None);
        var boreMin = new float2(mid - bore, lip);
        var boreMax = new float2(mid + bore, min.Y + size.Y);
        dl.AddRectFilled(in boreMin, in boreMax, Theme.U8(Theme.Well), 0f, ImDrawFlags.None);
        var n1 = new float2(mid - Gui.S(7), lip - Gui.S(1));
        var n2 = new float2(mid + Gui.S(7), lip - Gui.S(1));
        var n3 = new float2(mid, lip + Gui.S(9));
        dl.AddTriangleFilled(in n1, in n2, in n3, Theme.U8(s.OnTrue ? Theme.Good : Theme.Accent));

        // The part: over the seat, as far off true as it is, down as far as it's gone; tilted as it binds.
        var cx = mid + s.Offset * travel;
        var bottom = lip + s.Depth * sink;
        var tilt = s.Bind * 0.18f * MathF.Sign(s.Offset);
        // Home, it flushes green a moment before the next part comes up.
        var brass = s.Settling > 0 && !s.Jammed ? Lerp(new float4(0.80f, 0.64f, 0.30f, 1f), Theme.Good, s.Settling / (float)FitSession.SettleSteps)
            : new float4(0.80f, 0.64f, 0.30f, 1f);
        float2 P(float x, float y)
        {
            // Turned about the corner that's riding on the seat's edge.
            var px = cx - MathF.Sign(s.Offset) * partW * 0.5f;
            float dx = x - px, dy = y - bottom;
            return new float2(px + dx * MathF.Cos(tilt) - dy * MathF.Sin(tilt), bottom + dx * MathF.Sin(tilt) + dy * MathF.Cos(tilt));
        }
        var a = P(cx - partW * 0.5f, bottom - partH);
        var b = P(cx + partW * 0.5f, bottom - partH);
        var c = P(cx + partW * 0.5f, bottom);
        var d = P(cx - partW * 0.5f, bottom);
        dl.AddTriangleFilled(in a, in b, in c, Gui.U8(brass));
        dl.AddTriangleFilled(in a, in c, in d, Gui.U8(brass));
        var m1 = P(cx, bottom - partH + Gui.S(4));
        var m2 = P(cx, bottom - Gui.S(4));
        dl.AddLine(in m1, in m2, Theme.U8(s.OnTrue ? Theme.Good : Theme.Text), Gui.S(2));   // the part's own mark, to line up

        // Where it's riding on the edge, glowing as it binds.
        if (s.Bind > 0.03f)
        {
            var edge = new float2(s.Offset > 0 ? mid + bore : mid - bore, lip);
            dl.AddCircleFilled(in edge, Gui.S(5) + s.Bind * Gui.S(9), Theme.U8(Theme.Bad, 0.25f + 0.6f * s.Bind), 16);
        }

        // The pressure on it, as an arrow pushing down.
        if (s.Pressure > 0.02f && s.Settling == 0)
        {
            var top = P(cx, bottom - partH);
            var len = Gui.S(10) + s.Pressure * Gui.S(30);
            var t0 = new float2(top.X, top.Y - Gui.S(6) - len);
            var t1 = new float2(top.X, top.Y - Gui.S(10));
            var colour = Theme.U8(s.Pressure > FitSession.LightTouch ? Theme.Accent : Theme.Dim);
            dl.AddLine(in t0, in t1, colour, Gui.S(2) + s.Pressure * Gui.S(4));
            var h1 = new float2(top.X - Gui.S(7), top.Y - Gui.S(12));
            var h2 = new float2(top.X + Gui.S(7), top.Y - Gui.S(12));
            var h3 = new float2(top.X, top.Y - Gui.S(3));
            dl.AddTriangleFilled(in h1, in h2, in h3, colour);
        }

        // The parts, done and to come, by name.
        for (var i = 0; i < s.Parts; i++)
        {
            var done = i < s.Part || (i == s.Part && s.Settling > 0);
            var dot = new float2(min.X + Gui.S(10), min.Y + Gui.S(12) + i * Gui.S(18));
            dl.AddCircleFilled(in dot, Gui.S(5), Theme.U8(done ? Theme.Good : i == s.Part ? Theme.Accent : Theme.Well), 12);
            var at = dot + Gui.S(10, -7);
            dl.AddText(in at, Theme.U8(done ? Theme.Good : i == s.Part ? Theme.Text : Theme.Dim), PartName(s, i));
        }

        var next = s.Part + 1 < s.Parts ? $"Next: the {PartName(s, s.Part + 1)}." : s.Winds ? $"Next: {(FitOf(s)?.Wound is { Length: > 0 } wound ? $"draw the {wound} back" : "wind the spring")}." : "That's the last.";
        var (tone, say) = s.Settling > 0 ? (s.Jammed ? Theme.Warn : Theme.Good, s.Jammed ? $"It's in, but crooked: it'll bind. {next}" : $"Home, and square. {next}")
            : s.Bind > 0.5f ? (Theme.Bad, "It's binding on the edge! Ease off and square it up.")
            : s.Bind > 0.05f ? (Theme.Warn, "It's riding on the edge: off true.")
            : s.OnTrue ? (Theme.Good, s.Pressure > FitSession.LightTouch ? "Going in square." : "Square over the seat: press it home.")
            : (Theme.Dim, "Off true: bring its mark over the seat's.");
        TextC(tone, say);
        EndWork();
    }

    /// <summary>
    /// The ratchet turning a click a tap, with the teeth the pawl catches in marked in brass and the
    /// ones past them, where the spring snaps, in red: wind till the brass is under the pawl, then latch.
    /// Beside it the spring tightens, reddening and shaking as it strains.
    /// </summary>
    private void DrawWinding(FitSession s)
    {
        var wound = FitOf(s)?.Wound is { Length: > 0 } w0 ? w0 : null;
        BeginWork(wound != null ? $"Spanning the {s.What}: drawing the {wound} back" : s.Title,
            ("F", "tap to wind a click"), ("Space", wound != null ? "latch it on the nut" : "latch it"), ("Esc", "stop"));
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(420, 180);
        ImGui.Dummy(in size);
        var shake = s.Strain > 0f ? MathF.Sin((float)ImGui.GetTime() * 60f) * s.Strain * Gui.S(2) : 0f;

        // The wheel turns clockwise as it's wound: the marked teeth come round to the pawl at the top.
        const int Teeth = 16;
        const float PerTension = MathF.Tau * 0.8f;          // a full wind is most of a turn
        var wheel = new float2(min.X + Gui.S(120) + shake, min.Y + Gui.S(100));
        var r = Gui.S(56);
        var up = -MathF.PI / 2f;                            // where the pawl is
        // A point on the wheel at tension t comes under the pawl when the wheel's wound to t.
        float At(float t) => up + (s.Tension - t) * PerTension;
        // The rim, in arcs: plain, then the catch (brass), then the snap (red).
        var lo = s.CatchAt - s.CatchHalfWidth;
        var hi = s.CatchAt + s.CatchHalfWidth;
        dl.AddCircleFilled(in wheel, r, Theme.U8(Theme.Iron), 40);
        void Arc(float t0, float t1, float4 colour)
        {
            const int N = 16;
            for (var i = 0; i < N; i++)
            {
                float a0 = At(t0 + (t1 - t0) * i / N), a1 = At(t0 + (t1 - t0) * (i + 1) / N);
                var p0 = wheel + new float2(MathF.Cos(a0), MathF.Sin(a0)) * (r - Gui.S(4));
                var p1 = wheel + new float2(MathF.Cos(a1), MathF.Sin(a1)) * (r - Gui.S(4));
                dl.AddLine(in p0, in p1, Theme.U8(colour), Gui.S(8));
            }
        }
        Arc(lo, hi, new float4(0.86f, 0.68f, 0.30f, 1f));
        Arc(hi, hi + FitSession.SnapPast, Theme.Bad);
        for (var i = 0; i < Teeth; i++)
        {
            var a0 = At(i / (float)Teeth / 0.8f);
            var a1 = a0 - MathF.Tau / Teeth * 0.6f;
            var p0 = wheel + new float2(MathF.Cos(a0), MathF.Sin(a0)) * r;
            var p1 = wheel + new float2(MathF.Cos(a1), MathF.Sin(a1)) * r;
            var tip = wheel + new float2(MathF.Cos(a0), MathF.Sin(a0)) * (r + Gui.S(10));
            dl.AddTriangleFilled(in p0, in tip, in p1, Theme.U8(Theme.Iron));
        }
        dl.AddCircleFilled(in wheel, Gui.S(10), Theme.U8(Theme.Well), 16);

        // The pawl at the top: down in a tooth (green) while it's catching, riding the tips otherwise.
        var pivot = new float2(wheel.X + Gui.S(46), wheel.Y - r - Gui.S(26));
        var reach = s.Catching ? r + Gui.S(2) : r + Gui.S(14);
        var pawlTip = new float2(wheel.X, wheel.Y - reach);
        dl.AddLine(in pivot, in pawlTip, Theme.U8(s.Catching ? Theme.Good : s.Slipped > 0 ? Theme.Warn : Theme.Text), Gui.S(5));
        dl.AddCircleFilled(in pivot, Gui.S(5), Theme.U8(Theme.Text), 12);

        if (wound != null)
            DrawSpanned(dl, s, new float2(min.X + Gui.S(310) + shake, min.Y + Gui.S(100)));
        else
        {
        // The spring: more turns and tighter the more it's wound, warming to red past the catch.
        var centre = new float2(min.X + Gui.S(310) + shake, min.Y + Gui.S(100));
        var turns = 2.5f + s.Tension * 4f;
        var outer = Gui.S(62) - s.Tension * Gui.S(12);
        var tone = s.Strain > 0f ? Lerp(Theme.Warn, Theme.Bad, s.Strain) : Lerp(Theme.Iron, Theme.Text, Math.Clamp(s.Tension, 0f, 1f));
        var last = centre;
        const int Steps = 120;
        for (var i = 1; i <= Steps; i++)
        {
            var f = i / (float)Steps;
            var ang = f * turns * MathF.Tau;
            var rad = Gui.S(6) + f * (outer - Gui.S(6));
            var p = centre + new float2(MathF.Cos(ang), MathF.Sin(ang)) * rad;
            dl.AddLine(in last, in p, Theme.U8(tone), Gui.S(2) + s.Tension * Gui.S(2));
            last = p;
        }
        }

        var (colour, say) = s.Strain > 0f ? (Theme.Bad, "Past the catch: it's straining! Let it creep back onto the brass, or latch it strained.")
            : s.Catching ? (Theme.Good, Controls.Keys("The pawl's in the brass teeth. Latch it now (~Space~)."))
            : s.Slipped > 0 ? (Theme.Warn, "It won't hold yet: the pawl slips. Wind on till the brass teeth reach it.")
            : s.Clicks == 0 ? (Theme.Text, Controls.Keys($"Tap ~F~ to wind the {wound ?? "spring"} a click at a time."))
            : (Theme.Text, $"Wind on: the brass teeth are coming round to the pawl. {s.Clicks} {(s.Clicks == 1 ? "click" : "clicks")}.");
        TextC(colour, say);
        EndWork();
    }

    /// <summary>What each part of a contraption (or an axle, or the crossbow) is called, as it's fitted.</summary>
    private string PartName(FitSession s, int i)
    {
        IReadOnlyList<string> names = !s.Wagon.IsNull ? ["axle", "linchpins"]
            : FitOf(s) is { PartNames.Count: > 0 } fit ? fit.PartNames
            : ["frame", "gear train", "pawl", "spring barrel", "casing"];
        return names[Math.Clamp(i, 0, names.Count - 1)];
    }

    /// <summary>The fitting the bench is set to (<see cref="Sim.Defs.RecipeDef.Fit"/>), if its recipe has one of its own.</summary>
    private Sim.Defs.FitProps? FitOf(FitSession s) =>
        _sim.World.IsAlive(s.Station) && _sim.World.TryGet<Workbench>(s.Station, out var bench) ? bench.Active?.Fit : null;

    /// <summary>
    /// A crossbow being spanned, seen from above: the steel bow bending back as the string's drawn
    /// toward the nut, the nut's catch marked in brass and overdrawing in red.
    /// </summary>
    private void DrawSpanned(ImDrawListPtr dl, FitSession s, float2 centre)
    {
        var half = Gui.S(70);                                     // half the bow's span
        var front = centre.Y - Gui.S(50);                         // where the bow sits on the stock
        var travel = Gui.S(95);                                   // how far back the string can come
        float Back(float t) => front + Math.Clamp(t, 0f, 1.2f) * travel;
        // The stock down the middle, the nut's slot cut in it.
        dl.AddRectFilled(new float2(centre.X - Gui.S(7), front - Gui.S(8)), new float2(centre.X + Gui.S(7), front + travel * 1.2f + Gui.S(10)),
            Theme.U8(Theme.Oak), Gui.S(3), ImDrawFlags.None);
        var lo = s.CatchAt - s.CatchHalfWidth;
        var hi = s.CatchAt + s.CatchHalfWidth;
        dl.AddRectFilled(new float2(centre.X - Gui.S(12), Back(lo)), new float2(centre.X + Gui.S(12), Back(hi)),
            Theme.U8(new float4(0.86f, 0.68f, 0.30f, 1f)), Gui.S(2), ImDrawFlags.None);          // the nut: latch the string here
        dl.AddRectFilled(new float2(centre.X - Gui.S(12), Back(hi)), new float2(centre.X + Gui.S(12), Back(hi + FitSession.SnapPast)),
            Theme.U8(Theme.Bad, 0.8f), Gui.S(2), ImDrawFlags.None);                                  // overdrawn: the bow cracks
        // The steel bow: its tips bend back with the draw.
        var bend = Math.Clamp(s.Tension, 0f, 1.2f) * Gui.S(26);
        var tone = s.Strain > 0f ? Lerp(Theme.Warn, Theme.Bad, s.Strain) : Theme.Iron;
        var left = new float2(centre.X - half + bend * 0.4f, front + bend);
        var right = new float2(centre.X + half - bend * 0.4f, front + bend);
        var mid = new float2(centre.X, front - Gui.S(4));
        dl.AddBezierQuadratic(in left, in mid, in right, Theme.U8(tone), Gui.S(5), 16);
        // The string from tip to tip through where it's drawn to.
        var drawn = new float2(centre.X, Back(s.Tension));
        dl.AddLine(in left, in drawn, Theme.U8(Theme.Parchment), Gui.S(2));
        dl.AddLine(in right, in drawn, Theme.U8(Theme.Parchment), Gui.S(2));
        dl.AddCircleFilled(in drawn, Gui.S(3), Theme.U8(s.Catching ? Theme.Good : Theme.Text), 10);
    }

    private static float4 Lerp(float4 a, float4 b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

    /// <summary>The wheel turning past the gauge: the needle out for a bulge, in for a dip; the band it must run within.</summary>
    private void DrawTrue(TrueSession s)
    {
        BeginWork(s.Title, ("F", "tighten the spoke at the gauge"), ("A", "slacken it"), ("Esc", "stop"));
        TextC(Theme.Dim, "Tighten where the needle swings out, slacken where it swings in. Get it running within the band.");
        TrueScene(s.Turn, s.Gauge, s.BandWidth);
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(420, 60);
        var max = min + size;
        ImGui.Dummy(in size);
        dl.AddRectFilled(in min, in max, Theme.U8(Theme.Well), Gui.S(3), ImDrawFlags.None);
        var mid = (min.X + max.X) * 0.5f;
        var scale = size.X * 0.5f / 0.4f;
        var bandMin = new float2(mid - s.BandWidth * scale, min.Y);
        var bandMax = new float2(mid + s.BandWidth * scale, max.Y);
        dl.AddRectFilled(in bandMin, in bandMax, Theme.U8(Theme.Good, 0.45f), Gui.S(2), ImDrawFlags.None);
        var x = Math.Clamp(mid + s.Gauge * scale, min.X + 2, max.X - 2);
        var a = new float2(x, min.Y + Gui.S(4));
        var b = new float2(x, max.Y - Gui.S(4));
        dl.AddLine(in a, in b, Theme.U8(MathF.Abs(s.Gauge) <= s.BandWidth ? Theme.Text : Theme.Accent), Gui.S(3));
        TextC(Theme.Dim, "in");
        ImGui.SameLine(size.X - Gui.S(20), 0);
        TextC(Theme.Dim, "out");
        Bar("Wheel", s.Turn, Theme.Dim, $"spoke {s.AtGauge + 1} of {TrueSession.Spokes}");
        EndWork();
    }

    /// <summary>
    /// The span from the side: its stones on the centring, the level sliding along them, and its
    /// bubble off the middle where a stone sags or rides high. The band is where level lies.
    /// </summary>
    private void DrawSpan(SpanSession s)
    {
        BeginWork(s.Title, ("F", "drive a wedge under the stone at the level"), ("A", "ease one out"), ("Esc", "stop"));
        TextC(Theme.Dim, "Lift the stones that sag, ease down the ones riding high. Get the whole span level.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(420, 90);
        var max = min + size;
        ImGui.Dummy(in size);
        dl.AddRectFilled(in min, in max, new ImColor8(40, 66, 80, 255), Gui.S(3), ImDrawFlags.None); // the river under it
        var w = size.X / SpanSession.Stones;
        float StoneX(float at) => min.X + w * 0.5f + at * (size.X - w);
        var mid = (min.Y + max.Y) * 0.5f;
        var scale = size.Y * 0.5f / 0.4f;
        // The band where level lies.
        var bandMin = new float2(min.X, mid - s.BandWidth * scale);
        var bandMax = new float2(max.X, mid + s.BandWidth * scale);
        dl.AddRectFilled(in bandMin, in bandMax, Theme.U8(Theme.Good, 0.3f), 0f, ImDrawFlags.None);
        for (var i = 0; i < SpanSession.Stones; i++)
        {
            var top = mid - s.Offsets[i] * scale - Gui.S(9);
            var cx = StoneX(i / (float)(SpanSession.Stones - 1));
            var a = new float2(cx - w * 0.5f + Gui.S(2), top);
            var b = new float2(cx + w * 0.5f - Gui.S(2), top + Gui.S(18));
            var level = MathF.Abs(s.Offsets[i]) <= s.BandWidth;
            if (Gui.TrySprite("ui/work/stone", out var stone))
                Gui.Image(dl, stone, a, b, level ? Gui.White : Gui.U8(new float4(0.78f, 0.74f, 0.7f, 1f)));
            else
                dl.AddRectFilled(in a, in b, level ? new ImColor8(170, 160, 140, 255) : new ImColor8(130, 118, 100, 255), Gui.S(2), ImDrawFlags.None);
            if (i == s.AtStone)
                dl.AddRect(in a, in b, Theme.U8(Theme.Accent), Gui.S(2), ImDrawFlags.None, Gui.S(2));
        }
        // The level: a bar over the span, its bubble off the middle by what it reads.
        var lx = StoneX(s.At);
        var px = MathF.Max(1f, MathF.Round(2f * Gui.Scale));
        var barA = new float2(lx - 22 * px, min.Y + Gui.S(3));
        var barB = barA + new float2(44 * px, 9 * px);
        if (Gui.TrySprite("ui/work/spirit_level", out var levelArt))
            Gui.Image(dl, levelArt, barA, barB, Gui.White);
        else
            dl.AddRectFilled(in barA, in barB, new ImColor8(200, 180, 120, 255), Gui.S(3), ImDrawFlags.None);
        // The bubble in the vial (its middle at 21.5 of 44), off true by what the level reads.
        var vial = barA.X + 21.5f * px;
        var bubble = new float2(Math.Clamp(vial - s.Reading * 40f * px, barA.X + 14 * px, barA.X + 29 * px), barA.Y + 4.5f * px);
        dl.AddCircleFilled(in bubble, 1.6f * px, Theme.U8(MathF.Abs(s.Reading) <= s.BandWidth ? Theme.Good : Theme.Accent), 12);
        Bar("Span", 1f - Math.Clamp(s.Worst / 0.32f, 0f, 1f), Theme.Good, $"stone {s.AtStone + 1} of {SpanSession.Stones}");
        EndWork();
    }

    /// <summary>The piece under the glass, the loupe on it, what catches the eye, and the reading.</summary>
    private void DrawLoupe(LoupeSession s)
    {
        BeginWork(s.Title, ("A/D", "move the loupe along it"), ("F", "hold still to study"), ("Esc", "stop"));
        TextC(Theme.Dim, "Where the eye catches, stop and study it. The far end finishes.");
        LoupeScene(s.At, s.Catch, s.Held);
        Bar("Eye", s.Catch, s.Catch > 0.6f ? Theme.Warn : Theme.Dim);
        if (s.Reading > 0f)
            Bar("Reading", s.Reading, Theme.Good);
        TextC(Theme.Dim, $"Tells read: {s.ReadCount}");
        EndWork();
    }

    /// <summary>
    /// The lock in section: the plug and housing, a chamber per pin with its key pin, driver and
    /// spring, the shear line between them; the pick under the pin being lifted, the tension wrench in
    /// the keyway with the finger's pressure on it; and an end view of the plug turning under it.
    /// No noise: the pin stiffening near its set point shows as the pick slowing and shuddering and the
    /// shear line glowing; a pin setting clicks, the plug jolting.
    /// </summary>
    private void DrawPick(PickSession s)
    {
        BeginWork(s.Title, ("W/S", "ease the tension on / off"), ("A/D", "lift the pin"), ("F", "set it"), ("Esc", "stop"));
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(440, 190);
        ImGui.Dummy(in size);
        var steps = s.StepsDone;
        bool Recent(int at, int within) => at >= 0 && steps - at < within;
        var clicked = Recent(s.ClickedAt, 30);
        var jolt = clicked ? (1f - (steps - s.ClickedAt) / 30f) * Gui.S(3) : 0f;
        var shudder = s.Feel > 0.3f && !s.Turning ? MathF.Sin((float)ImGui.GetTime() * 90f) * (s.Feel - 0.3f) * Gui.S(1.5f) : 0f;

        // The section: housing above, the plug below the shear line, the keyway through it.
        var left = min.X + Gui.S(56);
        var right = min.X + Gui.S(316);
        var shear = min.Y + Gui.S(86);
        var housingTop = min.Y + Gui.S(14);
        var plugBottom = min.Y + Gui.S(150);
        var steel = new float4(0.55f, 0.57f, 0.62f, 1f);
        var brass = new float4(0.82f, 0.66f, 0.30f, 1f);
        var housing0 = new float2(left, housingTop);
        var housing1 = new float2(right, shear);
        dl.AddRectFilled(in housing0, in housing1, Theme.U8(Theme.Iron), Gui.S(3), ImDrawFlags.None);
        var plug0 = new float2(left + jolt, shear);
        var plug1 = new float2(right + jolt, plugBottom);
        dl.AddRectFilled(in plug0, in plug1, Gui.U8(brass * 0.55f + new float4(0, 0, 0, 0.45f)), Gui.S(3), ImDrawFlags.None);
        var way0 = new float2(left + jolt, plugBottom - Gui.S(22));
        var way1 = new float2(right + jolt, plugBottom - Gui.S(8));
        dl.AddRectFilled(in way0, in way1, Theme.U8(Theme.Well), Gui.S(2), ImDrawFlags.None);

        // The chambers and their pins.
        var pitch = (right - left - Gui.S(30)) / Math.Max(1, s.Pins);
        var pinW = Math.Min(Gui.S(18), pitch * 0.55f);
        var travel = Gui.S(30);
        float2 pickTip = default;
        for (var i = 0; i < s.Pins; i++)
        {
            var cx = left + Gui.S(30) + pitch * (i + 0.5f) + jolt;
            var c0 = new float2(cx - pinW * 0.5f - Gui.S(2), housingTop + Gui.S(4));
            var c1 = new float2(cx + pinW * 0.5f + Gui.S(2), plugBottom - Gui.S(22));
            dl.AddRectFilled(in c0, in c1, Theme.U8(Theme.Well, 0.8f), Gui.S(2), ImDrawFlags.None);
            var set = i < s.Pin || s.Turning;
            var current = i == s.Pin && !s.Turning;
            // Set: the key pin's top at the shear line, the driver caught above it. Otherwise lifted as far as it's been.
            var lift = set ? 0f : current ? s.Lift * travel : 0f;
            var keyTop = set ? shear + Gui.S(1) : shear + Gui.S(12) - lift;
            var keyBottom = keyTop + Gui.S(32);
            var driverBottom = set ? shear - Gui.S(3) : keyTop;
            var driverTop = driverBottom - Gui.S(24);
            var k0 = new float2(cx - pinW * 0.5f, keyTop);
            var k1 = new float2(cx + pinW * 0.5f, keyBottom);
            dl.AddRectFilled(in k0, in k1, Gui.U8(brass), Gui.S(2), ImDrawFlags.None);
            var d0 = new float2(cx - pinW * 0.5f, driverTop);
            var d1 = new float2(cx + pinW * 0.5f, driverBottom);
            dl.AddRectFilled(in d0, in d1, Gui.U8(steel), Gui.S(2), ImDrawFlags.None);
            // The spring above, squeezed the higher the driver goes.
            var top = housingTop + Gui.S(6);
            var coils = 5;
            var last = new float2(cx - pinW * 0.4f, top);
            for (var k = 1; k <= coils * 2; k++)
            {
                var y = top + (driverTop - top) * k / (coils * 2f);
                var p = new float2(cx + (k % 2 == 0 ? -1 : 1) * pinW * 0.4f, y);
                dl.AddLine(in last, in p, Theme.U8(Theme.Dim), Gui.S(1.5f));
                last = p;
            }
            // Where this pin meets the shear line: gold once set, glowing as the current one stiffens.
            var s0 = new float2(cx - pinW * 0.5f - Gui.S(2), shear - Gui.S(1));
            var s1 = new float2(cx + pinW * 0.5f + Gui.S(2), shear + Gui.S(1));
            if (set)
                dl.AddRectFilled(in s0, in s1, Gui.U8(new float4(1f, 0.85f, 0.35f, 1f)), 0f, ImDrawFlags.None);
            else if (current && s.Feel > 0f)
                dl.AddRectFilled(in s0, in s1, Theme.U8(Theme.Good, s.Feel), 0f, ImDrawFlags.None);
            if (current)
                pickTip = new float2(cx + shudder, keyBottom);
            if (set && i == s.Pin - 1 && clicked)
            {
                var at = new float2(cx - Gui.S(14), housingTop - Gui.S(12));
                dl.AddText(in at, Theme.U8(Theme.Good, 1f - (steps - s.ClickedAt) / 30f), "click"u8);
            }
        }
        // The shear line itself, across the whole lock.
        var l0 = new float2(left, shear);
        var l1 = new float2(right, shear);
        dl.AddLine(in l0, in l1, Theme.U8(Theme.Dim, 0.6f), Gui.S(1));

        // The pick: in along the keyway, its hook up under the pin being lifted.
        if (!s.Turning && pickTip != default)
        {
            var handle = new float2(right + Gui.S(40), plugBottom - Gui.S(14));
            var under = new float2(pickTip.X + Gui.S(6), plugBottom - Gui.S(14));
            dl.AddLine(in handle, in under, Gui.U8(steel), Gui.S(3));
            dl.AddLine(in under, in pickTip, Gui.U8(steel), Gui.S(3));
        }

        // The tension wrench in the keyway's mouth, and the finger's pressure on it.
        var wrench0 = new float2(left - Gui.S(30), plugBottom - Gui.S(18));
        var wrench1 = new float2(left + Gui.S(14), plugBottom - Gui.S(12));
        dl.AddRectFilled(in wrench0, in wrench1, Gui.U8(steel), Gui.S(1), ImDrawFlags.None);
        var arm0 = new float2(left - Gui.S(30), plugBottom - Gui.S(18));
        var arm1 = new float2(left - Gui.S(24), plugBottom + Gui.S(18));
        dl.AddRectFilled(in arm0, in arm1, Gui.U8(steel), Gui.S(1), ImDrawFlags.None);
        var tensionTone = s.Binding ? Theme.Bad : s.InBand ? Theme.Good : Theme.Dim;
        if (s.Tension > 0.01f)
        {
            var len = Gui.S(8) + s.Tension * Gui.S(26);
            var t0 = new float2(left - Gui.S(27), plugBottom + Gui.S(22) + len);
            var t1 = new float2(left - Gui.S(27), plugBottom + Gui.S(24));
            dl.AddLine(in t0, in t1, Theme.U8(tensionTone), Gui.S(2) + s.Tension * Gui.S(3));
            var h1 = new float2(left - Gui.S(33), plugBottom + Gui.S(30));
            var h2 = new float2(left - Gui.S(21), plugBottom + Gui.S(30));
            var h3 = new float2(left - Gui.S(27), plugBottom + Gui.S(20));
            dl.AddTriangleFilled(in h1, in h2, in h3, Theme.U8(tensionTone));
        }

        // The end view: the plug in its housing, turned a hair by the tension, and right over at the end.
        var face = new float2(min.X + Gui.S(390), min.Y + Gui.S(72));
        dl.AddCircleFilled(in face, Gui.S(36), Theme.U8(Theme.Iron), 32);
        dl.AddCircleFilled(in face, Gui.S(26), Gui.U8(brass), 32);
        var angle = -MathF.PI / 2f + s.Tension * 0.14f + s.Turned * MathF.PI / 2f;
        var dir = new float2(MathF.Cos(angle), MathF.Sin(angle));
        var across = new float2(-dir.Y, dir.X);
        var slot0 = face - dir * Gui.S(18);
        var slot1 = face + dir * Gui.S(18);
        dl.AddLine(in slot0, in slot1, Theme.U8(Theme.Well), Gui.S(5));
        var ward = face + dir * Gui.S(6) + across * Gui.S(4);
        dl.AddLine(in face, in ward, Theme.U8(Theme.Well), Gui.S(3));
        var gauge0 = new float2(face.X - Gui.S(40), face.Y + Gui.S(48));
        var gauge1 = new float2(face.X + Gui.S(40), face.Y + Gui.S(56));
        dl.AddRectFilled(in gauge0, in gauge1, Theme.U8(Theme.Well), Gui.S(2), ImDrawFlags.None);
        var band0 = new float2(gauge0.X + s.Low * Gui.S(80), gauge0.Y);
        var band1 = new float2(gauge0.X + s.High * Gui.S(80), gauge1.Y);
        dl.AddRectFilled(in band0, in band1, Theme.U8(Theme.Good, 0.45f), Gui.S(2), ImDrawFlags.None);
        var needle0 = new float2(gauge0.X + s.Tension * Gui.S(80), gauge0.Y - Gui.S(3));
        var needle1 = new float2(gauge0.X + s.Tension * Gui.S(80), gauge1.Y + Gui.S(3));
        dl.AddLine(in needle0, in needle1, Theme.U8(tensionTone), Gui.S(2));
        if (s.Pushing != 0)
        {
            // The plug's shove, as a little arrow off the needle the way it's pushing.
            var x = needle0.X + s.Pushing * Gui.S(10);
            var a1 = new float2(x, gauge0.Y - Gui.S(8));
            var a2 = new float2(x, gauge0.Y - Gui.S(2));
            var big = s.Hitching ? 1.8f : 1f;
            var a1b = new float2(a1.X, gauge0.Y - Gui.S(5) - Gui.S(3) * big);
            var a2b = new float2(a2.X, gauge0.Y - Gui.S(5) + Gui.S(3) * big);
            var a3 = new float2(x + s.Pushing * Gui.S(6) * big, gauge0.Y - Gui.S(5));
            dl.AddTriangleFilled(in a1b, in a2b, in a3, Theme.U8(s.Hitching ? Theme.Bad : Theme.Warn));
        }
        var label = new float2(gauge0.X, gauge1.Y + Gui.S(4));
        dl.AddText(in label, Theme.U8(Theme.Dim), "tension"u8);

        var (tone, say) = s.Turning ? (Theme.Good, "The last pin sets... and the plug turns over.")
            : Recent(s.SlippedAt, 60) ? (Theme.Bad, "Overset! The pin jumped the line, and one you'd set fell back.")
            : Recent(s.MissedAt, 60) ? (Theme.Warn, "Not there: the pin falls back to the bottom. Lift it again.")
            : Recent(s.DroppedAt, 90) ? (Theme.Bad, "Too slack: every pin you'd set has dropped back.")
            : s.Binding ? (Theme.Warn, "Too much tension: the pins are frozen. Ease off, and catch it as it springs back.")
            : s.Hitching ? (Theme.Warn, "The plug's springing back toward slack: hold it (W) before the pins drop.")
            : s.Pin > 0 && s.Tension < s.Low + 0.04f ? (Theme.Warn, "Near the light edge: a touch more tension (W), or the pins drop.")
            : s.Pin > 0 && s.Tension > s.High - 0.04f ? (Theme.Warn, "Near the heavy edge: ease off a touch (S), or the pins drop.")
            : s.Tension < s.Low ? (Theme.Dim, "Ease the tension on with W till it's in the band, then lift the pin.")
            : s.Pushing < 0 && s.InBand ? (Theme.Warn, "The plug's pushing back against your finger: hold the tension (W).")
            : s.Pushing > 0 && s.InBand ? (Theme.Warn, "The plug gives under your finger: ease off a touch (S).")
            : clicked ? (Theme.Good, "Click. That one's set.")
            : s.Feel > 0.6f ? (Theme.Good, s.InBand ? Controls.Keys("It's stiffening under the pick: set it (~F~).") : "It's stiffening under the pick, but the tension's off.")
            : (Theme.Text, "Lift the pin slowly, feeling for where it stiffens.");
        TextC(tone, say);
        TextC(s.Slips > 0 ? Theme.Warn : Theme.Dim, $"Slips {s.Slips} of {PickSession.MaxSlips}");
        EndWork();
    }

    // ---------------- At a door or a wagon ----------------

    /// <summary>F at a door's lock or a wagon: its window.</summary>
    private void OpenTinkerWindow(Entity target, bool toggle) =>
        _tinkerAt = toggle && _tinkerAt == target ? Entity.Null : target;

    /// <summary>
    /// A door's lock (lock or unlock it with your key, pick it, or fit one you carry) or a party's
    /// wagon (what's wrong with it, and mending it). Closes when you walk off or press Esc.
    /// </summary>
    private void DrawTinkerWindow()
    {
        if (_tinkerAt.IsNull)
            return;
        var w = _sim.World;
        if (!HasAvatar || !w.IsAlive(_tinkerAt) || !AvatarSystem.InReach(_sim, w.Get<Position>(_avatar).Cell, _tinkerAt)
            || Controls.Map.Pressed(Act.Back))
        {
            _tinkerAt = Entity.Null;
            return;
        }
        var pos = ImGui.GetIO().DisplaySize * 0.5f + new float2(60, 0);
        float2? pivot = new float2(0f, 0.5f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Appearing, in pivot);
        var open = true;
        var label = w.Get<Thing>(_tinkerAt).Label;
        ScreenFit.Next();
        Panel.Begin($"{Cap(label)}###tinker", ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);
        PadWindow("###tinker", () => _tinkerAt = Entity.Null);
        if (View.Wagon(_tinkerAt) is { } wagon)
            DrawWagonInside(wagon);
        else
            DrawDoorInside(label);
        Panel.End();
        if (!open)
            _tinkerAt = Entity.Null;
    }

    private void DrawDoorInside(string label)
    {
        var door = _tinkerAt;
        if (View.Lock(door) is not { } known)
        {
            var lk = Tinkers.CarriedLock(_sim, _avatar);
            Wrapped($"No lock on the {label}.");
            ImGui.BeginDisabled(lk.IsNull || Working);
            if (ImGui.Button("Fit your lock to it"u8, in _autoSize))
                Issue(new FitLockCommand(_avatar, door, lk));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(lk.IsNull ? "You've no lock on you. Pin one at a tinker's bench." : "It's part of the door from then on: only its keys turn it, or a good pick.");
            return;
        }
        TextC(known.Locked ? Theme.Warn : Theme.Dim, $"{(known.Locked ? "Locked" : "Unlocked")}: a {known.Pins}-pin lock.");
        if (known.Tightness is { } tight)
            TextC(Theme.Dim, tight >= 0.8f ? "Your work, and tight: a hard one to pick." : tight >= 0.5f ? "Your work: fair to middling." : "Your work, and loose: an easy pick.");
        ImGui.BeginDisabled(!known.HaveKey || Working);
        if (ImGui.Button(known.Locked ? "Unlock it"u8 : "Lock it"u8, in _autoSize))
            Issue(new TurnKeyCommand(_avatar, door, !known.Locked));
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip(known.HaveKey ? "With the key you carry. A poorly cut one sometimes sticks." : "None of your keys fits it.");
        if (known.Locked)
        {
            var why = PickSession.DoorProblem(_sim, _avatar, door);
            ImGui.BeginDisabled(why != null || Working);
            if (ImGui.Button("Pick the lock"u8, in _autoSize))
                Issue(new PickDoorCommand(_avatar, door));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(why != null ? Cap(why) + "." : Controls.Keys("~AD~ lift the pin; ~F~ sets it where it gives most. A tight lock gives little room."));
        }
    }

    private void DrawWagonInside(KnownWagon wagon)
    {
        Wrapped($"{wagon.Owner}'s wagon: {Wagons.DamageLabel(wagon.Damage)}.");
        if ((wagon.Damage & WagonDamage.Wheel) != 0)
        {
            var why = Wagons.WheelProblem(_sim, _avatar, _tinkerAt);
            ImGui.BeginDisabled(why != null || Working);
            if (ImGui.Button("True the wheel"u8, in _autoSize))
                Issue(new TrueWheelCommand(_avatar, _tinkerAt));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(why != null ? Cap(why) + "." : Controls.Keys("As each spoke comes past the gauge: ~F~ tightens it (where the rim bulges out), ~A~ slackens it (where it dips in)."));
        }
        if ((wagon.Damage & WagonDamage.Axle) != 0)
        {
            var why = Wagons.AxleProblem(_sim, _avatar, _tinkerAt);
            ImGui.BeginDisabled(why != null || Working);
            if (ImGui.Button($"Fit a new axle ({Wagons.AxleWood} wood)"))
                Issue(new FitAxleCommand(_avatar, _tinkerAt));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(why != null ? Cap(why) + "." : Controls.Keys("Hold ~F~ to file each part while it binds; ~D~ seats it at the fit."));
        }
        if (wagon.Damage == WagonDamage.None)
            TextC(Theme.Good, "It's sound.");
    }

    // ---------------- From the pack ----------------

    /// <summary>What can be done with a key, a curio or a contraption you carry. True if anything was offered.</summary>
    private bool TinkerMenuEntries(Entity item, Action<ISimCommand> Later)
    {
        var w = _sim.World;
        var any = false;
        if (Locksmithing.IsKey(_sim, item))
        {
            any = true;
            var bench = BenchInReach();
            var why = bench.IsNull ? "you need to be at a tinker's bench" : PinSession.CopyProblem(_sim, _avatar, bench, item);
            ImGui.BeginDisabled(why != null || Working);
            if (ImGui.MenuItem("Copy the key"u8))
                Later(new CopyKeyCommand(_avatar, bench, item));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(why != null ? Cap(why) + "." : Controls.Keys("File a blank from an iron bar to this key's bits: hold ~F~ to file, ~Space~ to set each."));
        }
        if (View.Curio(item) is { } curio)
        {
            any = true;
            var why = Working ? "you're busy" : null;
            ImGui.BeginDisabled(why != null);
            if (ImGui.MenuItem(curio.Low == null ? "Look it over under the loupe"u8 : "Look it over again"u8))
                Later(new AppraiseCommand(_avatar, item));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(Controls.Keys("~AD~ move the loupe along it; where the eye catches, hold ~F~ still to study it. The far end finishes."));
            if (curio.Mine)
            {
                if (ImGui.MenuItem("Talk over its worth..."u8))
                {
                    _curioTalk = item;
                    _quote = curio.Quoted ?? ((curio.Low ?? 0) + (curio.High ?? 0)) / 2;
                    _offer = Math.Max(0, curio.Low ?? 0);
                }
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip("Say what you'd put it at, aloud; or offer its owner silver for it.");
            }
        }
        return any;
    }

    /// <summary>A tinker's bench beside the avatar, if any.</summary>
    private Entity BenchInReach()
    {
        var at = _sim.World.Get<Position>(_avatar).Cell;
        foreach (var e in _sim.World.Each<Thing>())
            if (Tinkers.IsBench(_sim, e) && AvatarSystem.InReach(_sim, at, e))
                return e;
        return Entity.Null;
    }

    /// <summary>
    /// Talking over a curio's worth: what you reckon (from your own look), what to say it's worth
    /// (aloud: whoever's by hears), and an offer to its owner if it's been left with you.
    /// </summary>
    private void DrawCurioTalk()
    {
        if (_curioTalk.IsNull)
            return;
        var w = _sim.World;
        if (!HasAvatar || View.Curio(_curioTalk) is not { Mine: true } curio || !Inventory.OnPerson(_sim, _avatar, _curioTalk)
            || Controls.Map.Pressed(Act.Back))
        {
            _curioTalk = Entity.Null;
            return;
        }
        var pos = ImGui.GetIO().DisplaySize * 0.5f + new float2(60, 0);
        float2? pivot = new float2(0f, 0.5f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Appearing, in pivot);
        var open = true;
        ScreenFit.Next();
        Panel.Begin($"{Cap(curio.Label)}###curio", ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);
        PadWindow("###curio", () => _curioTalk = Entity.Null);
        TextC(curio.Doubts ? Theme.Warn : Theme.Text, curio.Doubts
            ? $"A fake, you'd say: worth {curio.Low} to {curio.High} silver as a curiosity."
            : $"You'd put it at {curio.Low} to {curio.High} silver.");
        if (curio.Quoted is { } said)
            TextC(Theme.Dim, $"You've said {said}.");
        ImGui.SetNextItemWidth(Gui.S(200));
        ImGui.SliderInt("##quote"u8, ref _quote, 0, Math.Max(120, curio.High ?? 0), "%d silver"u8, ImGuiSliderFlags.AlwaysClamp);
        ImGui.SameLine(0, 6);
        if (ImGui.Button("Say it's worth that"u8, in _autoSize))
            Issue(new QuoteCurioCommand(_avatar, _curioTalk, _quote));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip("Aloud: whoever's by hears it. Its owner takes your word for it when you hand it back.");
        if (w.TryGet<Entrusted>(_curioTalk, out var left))
        {
            ImGui.Separator();
            ImGui.SetNextItemWidth(Gui.S(200));
            ImGui.SliderInt("##offer"u8, ref _offer, 0, Math.Max(120, curio.High ?? 0), "%d silver"u8, ImGuiSliderFlags.AlwaysClamp);
            ImGui.SameLine(0, 6);
            var near = Handing.InReach(_sim, _avatar, left.Owner);
            ImGui.BeginDisabled(!near || Working);
            if (ImGui.Button("Offer to buy it"u8, in _autoSize))
                Issue(new OfferForCurioCommand(_avatar, _curioTalk, _offer));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(near ? "They'll take it if it's near what they believe it's worth. Sell it on through the collector's crate."
                    : "Its owner needs to be beside you.");
        }
        Panel.End();
        if (!open)
            _curioTalk = Entity.Null;
    }

    /// <summary>Lines about a lock, key, curio or contraption, for its tooltip.</summary>
    private void TinkerDetails(Entity item, List<string> lines)
    {
        var w = _sim.World;
        if (View.Lock(item) is { } lk)
            lines.Add($"A {lk.Pins}-pin lock, not yet fitted.{(lk.Tightness is { } t ? t >= 0.8f ? " Tight work." : t < 0.5f ? " Loose work." : "" : "")}");
        if (w.TryGet<Key>(item, out var key) && Inventory.OnPerson(_sim, _avatar, item))
            lines.Add($"A key with {key.Pins} bits. {(key.Fit >= Locksmithing.GoodFit ? "It turns sweetly." : "It sticks now and then.")}");
        if (View.Curio(item) is { } curio)
        {
            if (curio.Low is { } low)
                lines.Add(curio.Doubts ? $"You'd say it's a fake: {low} to {curio.High} silver." : $"You'd put it at {low} to {curio.High} silver.");
            else if (curio.Told is { } told)
                lines.Add($"You've been told it's worth {told} silver.");
            else
                lines.Add("Nobody's looked it over yet.");
        }
        if (w.Get<Thing>(item).Def.Contraption is { } c)
            lines.Add(c.Use switch
            {
                Sim.Defs.ContraptionUse.Traps => "On a quest: finds traps and pits before they find you.",
                Sim.Defs.ContraptionUse.Falls => "On a quest: catches a fall that would kill, once.",
                Sim.Defs.ContraptionUse.Dark => "On a quest: lights dark places (barrows, mines, caves).",
                Sim.Defs.ContraptionUse.Beasts => "On a quest: takes the fight out of beasts.",
                _ => "On a quest: brings help when it's gone wrong, once.",
            });
    }
}
