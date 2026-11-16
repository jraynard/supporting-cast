using Brutal.ImGuiApi;
using Narsil.Sim.Core;
using Narsil.Ui;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The inn's kitchen: the stew pot from the side as it's tended (its heat shown only by the
// surface and the steam), seasoning by taste, and the pot beside you between times. Everything
// here is read from the session or Perception: never the pot's rot or the seasoning it wants.
public sealed partial class VillageGame
{
    private static readonly float4 StewColour = new(0.47f, 0.27f, 0.13f, 1f);

    /// <summary>The pot over the fire: the flames as the fire's set, the surface as hot as it is, the spoon when you stir.</summary>
    private void DrawStew(StewSession s)
    {
        if (s.Flaring > 0)
            BeginWork(s.Title, ("Space", "clap the lid on!"), ("F|Press and circle", "stir"), ("A/D|Wheel", "bank or feed the fire"), ("Esc", "leave it"));
        else
            BeginWork(s.Title, ("F|Press and circle", "stir"), ("A/D|Wheel", "bank or feed the fire"), ("Esc", "leave it"));
        TextC(Theme.Dim, s.Fatty ? "Keep it at a simmer, and stir before it catches. It's fatty: a hard boil spits." : "Keep it at a simmer, and stir before it catches.");
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 240);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // The fire under it: taller and brighter as it's fed.
        var potMin = new float2(min.X + size.X * 0.17f, min.Y + size.Y * 0.3f);
        var potMax = new float2(max.X - size.X * 0.17f, max.Y - Gui.S(48));
        TakesPointer(potMin, potMax, wheel: true); // the pot: press in it and go round its middle; the wheel works the fire
        var fireY = max.Y - Gui.S(10);
        for (var i = 0; i < 9; i++)
        {
            var x = potMin.X + Gui.S(14) + i * (potMax.X - potMin.X - Gui.S(28)) / 8f;
            var flicker = 0.7f + 0.3f * MathF.Sin(_time * (6f + i % 3) + i * 2.1f);
            var h = Gui.S(6 + 30 * s.Flame) * flicker;
            var a = new float2(x - Gui.S(7), fireY);
            var b = new float2(x + Gui.S(7), fireY);
            var c = new float2(x, fireY - h);
            dl.AddTriangleFilled(in a, in b, in c, Gui.U8(new float4(0.95f, 0.45f + 0.3f * s.Flame, 0.1f, 0.85f)));
        }

        // The pot (the iron pot's art at a whole-pixel scale, sat on the fire), and the stew's surface in it.
        float2 surfMin, surfMax;
        if (Gui.TrySprite("ui/work/stew_pot", out var potArt))
        {
            var px = MathF.Max(1f, MathF.Floor((potMax.X - potMin.X + Gui.S(12)) / 44f));
            var pMin = new float2(MathF.Round((potMin.X + potMax.X) * 0.5f - 22 * px), MathF.Round(potMax.Y - 30 * px));
            Gui.Image(dl, potArt, pMin, pMin + new float2(44, 30) * px, Gui.White);
            surfMin = pMin + new float2(6, 5) * px;
            surfMax = pMin + new float2(38, 9) * px;
        }
        else
        {
            dl.AddRectFilled(in potMin, in potMax, new ImColor8(38, 36, 38, 255), Gui.S(14), ImDrawFlags.None);
            var rimMin = new float2(potMin.X - Gui.S(6), potMin.Y - Gui.S(6));
            var rimMax = new float2(potMax.X + Gui.S(6), potMin.Y + Gui.S(4));
            dl.AddRectFilled(in rimMin, in rimMax, new ImColor8(58, 56, 60, 255), Gui.S(3), ImDrawFlags.None);
            surfMin = new float2(potMin.X + Gui.S(8), potMin.Y + Gui.S(4));
            surfMax = new float2(potMax.X - Gui.S(8), potMin.Y + Gui.S(16));
        }
        var stew = StewColour * (0.85f + 0.15f * s.Heat);
        stew.W = 1f;
        dl.AddRectFilled(in surfMin, in surfMax, Gui.U8(stew), Gui.S(4), ImDrawFlags.None);

        // Bubbles: none when still, a lazy few at a simmer, a churn at the boil.
        var bubbles = s.Surface switch { Surface.Still => 0, Surface.Steaming => 1, Surface.Simmer => 4, _ => 10 };
        for (var i = 0; i < bubbles; i++)
        {
            var t = (_time * (0.6f + 0.25f * i) + i * 0.37f) % 1f;
            var c = new float2(surfMin.X + ((i * 0.618034f) % 1f) * (surfMax.X - surfMin.X), surfMin.Y + Gui.S(6));
            dl.AddCircle(in c, Gui.S(2 + 4 * t), new ImColor8(210, 170, 120, (byte)(200 * (1f - t))), 10, Gui.S(1.2f));
        }

        // Steam off the top, thicker as it heats.
        var dt = ImGui.GetIO().DeltaTime;
        _steamDue += dt * 50f * Math.Max(0f, s.Heat - 0.2f);
        var seed = (int)(_time * 1000f);
        while (_steamDue >= 1f)
        {
            _steamDue -= 1f;
            var r = new SimRandom((ulong)(seed++ * 2654435761u + 11));
            var at = new float2(surfMin.X + r.NextFloat() * (surfMax.X - surfMin.X), surfMin.Y);
            _steam.Add(new Puff(at, new float2(r.Range(-12f, 12f), r.Range(-60f, -30f)) * Gui.Scale, _time, r.Range(0.6f, 1.2f), Gui.S(r.Range(5f, 10f))));
        }
        _steam.RemoveAll(p => _time - p.Born > p.Life);
        foreach (var p in _steam)
        {
            var t = _time - p.Born;
            var at = p.Pos + p.Vel * t;
            dl.AddCircleFilled(in at, p.Size * (1f + t), new ImColor8(230, 232, 236, (byte)(90 * (1f - t / p.Life))), 12);
        }

        // The spoon: going round while you stir (with the mouse, where it is over the pot), resting against the rim when you don't.
        var mid = (surfMin + surfMax) * 0.5f;
        var mouse = ImGui.GetMousePos();
        var tip = s.Held && UsingPointer
            ? new float2(Math.Clamp(mouse.X, surfMin.X + Gui.S(6), surfMax.X - Gui.S(6)), Math.Clamp(mouse.Y, surfMin.Y, surfMax.Y + Gui.S(6)))
            : s.Stirring
            ? mid + new float2(MathF.Cos(_time * 7f) * (surfMax.X - surfMin.X) * 0.3f, Gui.S(2))
            : new float2(surfMax.X - Gui.S(14), surfMin.Y + Gui.S(4));
        var handle = tip + new float2(Gui.S(46), -Gui.S(70));
        dl.AddLine(in tip, in handle, new ImColor8(150, 110, 70, 255), Gui.S(4));

        // The fat's caught: flames leaping off the surface, higher as the time runs out.
        if (s.Flaring > 0)
        {
            var urgency = 1f - s.Flaring / (float)Cooking.FlareSteps;
            for (var i = 0; i < 7; i++)
            {
                var x = surfMin.X + Gui.S(10) + i * (surfMax.X - surfMin.X - Gui.S(20)) / 6f;
                var flicker = 0.6f + 0.4f * MathF.Sin(_time * (9f + i % 4) + i * 1.7f);
                var h = Gui.S(30 + 50 * urgency) * flicker;
                var a = new float2(x - Gui.S(9), surfMin.Y);
                var b = new float2(x + Gui.S(9), surfMin.Y);
                var t2 = new float2(x, surfMin.Y - h);
                dl.AddTriangleFilled(in a, in b, in t2, Gui.U8(new float4(0.98f, 0.55f + 0.3f * flicker, 0.1f, 0.9f)));
            }
        }

        ImGui.Spacing();
        if (s.Flaring > 0)
            TextC(Theme.Bad, UsingPointer ? "The fat's caught! Clap the lid on (right-click)!" : Controls.Keys("The fat's caught! Clap the lid on (~Space~)!"));
        TextC(s.Surface is Surface.Simmer ? Theme.Good : s.Surface == Surface.Boil ? Theme.Warn : Theme.Dim,
            $"It's {Cooking.SurfaceLabel(s.Surface)}.");
        if (s.Catching)
        {
            ImGui.SameLine(0, 12);
            TextC(Theme.Bad, "Something's catching on the bottom!");
        }
        Bar("Cooked", Math.Clamp(s.Doneness, 0f, 1f), Theme.Accent);
        if (!s.Lit)
            TextC(Theme.Bad, "The fire's out: it'll only cool.");
        EndWork();
    }

    /// <summary>Seasoning by taste: pinches in, a taste, and what it says.</summary>
    private void DrawSeason(SeasonSession s)
    {
        BeginWork(s.Title, ("F", "a pinch"), ("Space", "taste it"), ("Esc", "that'll do"));
        TextC(Theme.Dim, "Every pot wants its own. Taste as you go: it can't come back out.");
        SeasonScene(s.StartTick, s.Pinches);
        ImGui.Spacing();
        ImGui.Text($"{s.Pinches} pinch{(s.Pinches == 1 ? "" : "es")} in.");
        if (s.TastedAt < 0)
            TextC(Theme.Dim, "You haven't tasted it yet.");
        else
        {
            var fresh = s.TastedAt == s.Pinches;
            var colour = s.Taste == "about right" ? Theme.Good : s.Taste.Contains("salty") ? Theme.Bad : Theme.Warn;
            TextC(fresh ? colour : Theme.Dim, fresh ? $"\"{Cap(s.Taste)}.\"" : $"Last taste: \"{s.Taste}\" (before {s.Pinches - s.TastedAt} more).");
        }
        EndWork();
    }

    /// <summary>Smelling food (or the pot): a sniff at a time, and what the last one said. Never the rot itself.</summary>
    private void DrawSniff(SniffSession s)
    {
        BeginWork(s.Title, ("Space", "sniff"), ("Esc", "that'll do"));
        SniffScene(s, SmellColour(s.Smell));
        TextC(Theme.Dim, s.IsPot ? "A pot hides what's in it. At best you'll know something's off." : "Fresh, fine, on the turn, or off. A practised nose is surer.");
        ImGui.Spacing();
        if (s.Sniffs == 0)
            TextC(Theme.Dim, Controls.Keys("You haven't smelt it yet: ~Space~."));
        else
        {
            var bad = s.Smell is "off" or "not quite right";
            var colour = bad ? Theme.Bad : s.Smell == "on the turn" ? Theme.Warn : Theme.Good;
            TextC(colour, $"It smells {s.Smell}.");
            if (s.Sniffs > 1)
                TextC(Theme.Dim, $"({s.Sniffs} sniffs.)");
        }
        EndWork();
    }

    private static float4 CrustColour(Crust c) => Rendering.KitchenLook.Crust(c);

    /// <summary>The roast from the end, its four sides round it, the fire under the one to it, flaring when the fat drips.</summary>
    private void DrawRoast(RoastSession s)
    {
        BeginWork(s.Title, ("F", "turn it"), ("Space", "lift it off"), ("Esc", "leave it on the spit"));
        RoastScene(s);
        TextC(Theme.Dim, "Golden all round, and through. When the fat flares, turn it away.");
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 170);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        var c = new float2((min.X + max.X) * 0.5f, min.Y + size.Y * 0.42f);
        var r = Gui.S(34);
        // Each side as a quarter round the rod; the one to the fire at the bottom.
        for (var i = 0; i < SpitRoast.Sides; i++)
        {
            var slot = (i - s.Facing + SpitRoast.Sides) % SpitRoast.Sides; // 0: down, to the fire
            float2 off = slot switch { 0 => new(0, 1), 1 => new(1, 0), 2 => new(0, -1), _ => new(-1, 0) };
            var a = c + off * r * 0.55f - new float2(r * 0.5f, r * 0.5f);
            var b = a + new float2(r, r);
            dl.AddRectFilled(in a, in b, Gui.U8(CrustColour(Roasting.CrustOf(s.Brown[i]))), Gui.S(8), ImDrawFlags.None);
        }
        var rodA = new float2(min.X + Gui.S(30), c.Y);
        var rodB = new float2(max.X - Gui.S(30), c.Y);
        dl.AddLine(in rodA, in rodB, new ImColor8(90, 90, 96, 255), Gui.S(3));
        // The fire under it, leaping when it flares.
        var fireY = max.Y - Gui.S(10);
        for (var i = 0; i < 7; i++)
        {
            var x = c.X - Gui.S(54) + i * Gui.S(18);
            var flicker = 0.7f + 0.3f * MathF.Sin(_time * (6f + i % 3) + i * 2.1f);
            var h = Gui.S(s.Flaring > 0 ? 64 : 26) * flicker;
            var a = new float2(x - Gui.S(7), fireY);
            var b = new float2(x + Gui.S(7), fireY);
            var tip = new float2(x, fireY - h);
            dl.AddTriangleFilled(in a, in b, in tip, Gui.U8(new float4(0.95f, s.Flaring > 0 ? 0.8f : 0.5f, 0.1f, 0.85f)));
        }
        ImGui.Spacing();
        var faces = string.Join(", ", s.Brown.Select((b, i) => (i == s.Facing ? "*" : "") + Roasting.CrustLabel(Roasting.CrustOf(b))));
        Wrapped($"Its sides: {faces} (* to the fire).");
        if (s.Flaring > 0)
            TextC(Theme.Bad, "The fat's caught: it's flaring!");
        if (!s.Lit)
            TextC(Theme.Bad, "The fire's out.");
        EndWork();
    }

    /// <summary>
    /// Kneading on the beat: the dough, a hand drawing back and pushing in from the side the next
    /// push comes from, a timing strip whose tick crosses the mark on each beat, and each push's
    /// mark once it's made.
    /// </summary>
    private void DrawKnead(KneadSession s)
    {
        BeginWork(s.Title, ("A/D|Click a side", "push from that side, on the beat"), ("Esc", "leave it"));
        KneadScene(s);
        TakesPointer(_lastScene.Min, _lastScene.Max); // a click on a half pushes from that side
        if (s.StartTick != _kneadSession)
        {
            _kneadSession = s.StartTick;
            _kneadPushed = 0;
            _kneadAt = -10f;
            _kneadMarks.Clear();
        }
        if (s.Pushed > _kneadPushed)
        {
            for (var i = _kneadPushed; i < s.Pushed; i++)
                _kneadMarks.Add(s.LastMark);
            _kneadPushed = s.Pushed;
            _kneadAt = _time;
        }
        var since = _time - _kneadAt;
        var half = KneadSession.Beat / 2f;
        var window = KneadSession.Window * s.Hands.Tolerance;
        var onBeat = s.Off <= window;
        // Steps to the nearest beat, negative before it: the strip's tick crosses the mark at 0.
        var t = s.Phase < half ? s.Phase : s.Phase - KneadSession.Beat;

        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 150);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // The board, and the dough on it: squashed by a push, smoother the cleaner they've been.
        var c = new float2((min.X + max.X) * 0.5f, min.Y + Gui.S(62));
        var boardA = new float2(min.X + Gui.S(70), c.Y + Gui.S(20));
        var boardB = new float2(max.X - Gui.S(70), c.Y + Gui.S(34));
        dl.AddRectFilled(in boardA, in boardB, new ImColor8(150, 110, 70, 255), Gui.S(3), ImDrawFlags.None);
        var squash = since < 0.3f ? 1f - since / 0.3f : 0f;
        var rx = Gui.S(50 + 22 * squash);
        var ry = Gui.S(28 - 10 * squash);
        var centre = new float2(c.X, c.Y + Gui.S(20) - ry);
        if (onBeat)
            dl.AddEllipseFilled(in centre, new float2(rx + Gui.S(7), ry + Gui.S(7)), Theme.U8(Theme.Good, 0.35f), 0f, 32);
        var clean = _kneadMarks.Count == 0 ? 1f : _kneadMarks.Count(m => m == 1) / (float)_kneadMarks.Count;
        var dough = new float4(0.86f + 0.05f * clean, 0.78f + 0.06f * clean, 0.60f + 0.08f * clean, 1f);
        dl.AddEllipseFilled(in centre, new float2(rx, ry), Gui.U8(dough), 0f, 32);
        // Lumps for the pushes that went in badly.
        var lumps = _kneadMarks.Count(m => m != 1);
        for (var i = 0; i < lumps; i++)
        {
            var at = centre + new float2((i % 2 == 0 ? -1 : 1) * rx * (0.25f + 0.12f * (i / 2)), ry * (i % 3 - 1) * 0.35f);
            dl.AddCircleFilled(in at, Gui.S(4), Gui.U8(dough * new float4(0.88f, 0.86f, 0.82f, 1f)), 12);
        }

        // The hand, on the side the next push comes from: drawn back between beats, pressing in on it.
        if (!s.Done)
        {
            var side = s.NextSide;
            var reach = 1f - Math.Min(1f, s.Off / half);
            var handX = centre.X + side * (rx + Gui.S(16) + (1f - reach) * Gui.S(56));
            var hA = new float2(handX - Gui.S(14), centre.Y - Gui.S(11));
            var hB = new float2(handX + Gui.S(14), centre.Y + Gui.S(11));
            dl.AddRectFilled(in hA, in hB, onBeat ? new ImColor8(240, 196, 150, 255) : new ImColor8(200, 160, 125, 255), Gui.S(8), ImDrawFlags.None);
            var key = Controls.Glyph(side < 0 ? Act.WalkLeft : Act.WalkRight);
            var ks = ImGui.CalcTextSize(key);
            var kp = new float2(handX - ks.X * 0.5f, hA.Y - ks.Y - Gui.S(4));
            dl.AddText(in kp, Theme.U8(onBeat ? Theme.Good : Theme.Dim), key);
        }

        // The timing strip: the tick runs left to right and crosses the mark on the beat.
        var stripC = new float2(c.X, max.Y - Gui.S(16));
        var stripHalf = Gui.S(150);
        var sA = new float2(stripC.X - stripHalf, stripC.Y - Gui.S(3));
        var sB = new float2(stripC.X + stripHalf, stripC.Y + Gui.S(3));
        dl.AddRectFilled(in sA, in sB, Theme.U8(Theme.Inset), Gui.S(3), ImDrawFlags.None);
        var zw = window / half * stripHalf;
        var zA = new float2(stripC.X - zw, stripC.Y - Gui.S(6));
        var zB = new float2(stripC.X + zw, stripC.Y + Gui.S(6));
        dl.AddRectFilled(in zA, in zB, Theme.U8(Theme.Good, onBeat ? 0.8f : 0.4f), Gui.S(3), ImDrawFlags.None);
        var tickX = stripC.X + t / half * stripHalf;
        var tA = new float2(tickX, stripC.Y - Gui.S(9));
        var tB = new float2(tickX, stripC.Y + Gui.S(9));
        dl.AddLine(in tA, in tB, Theme.U8(onBeat ? Theme.Good : Theme.Text), Gui.S(3));

        // Each push so far: green clean, amber lumpy, hollow still to come.
        for (var i = 0; i < KneadSession.Pushes; i++)
        {
            var at = new float2(min.X + Gui.S(16) + i * Gui.S(14), min.Y + Gui.S(14));
            if (i < _kneadMarks.Count)
                dl.AddCircleFilled(in at, Gui.S(5), Theme.U8(_kneadMarks[i] == 1 ? Theme.Good : Theme.Warn), 12);
            else
                dl.AddCircle(in at, Gui.S(5), Theme.U8(Theme.Dim), 12, Gui.S(1.5f));
        }

        ImGui.Spacing();
        if (since < 0.8f && _kneadMarks.Count > 0)
            TextC(_kneadMarks[^1] == 1 ? Theme.Good : Theme.Warn, _kneadMarks[^1] == 1 ? "Smooth." : "Lumpy: on the beat, from the hand's side.");
        else
            TextC(onBeat ? Theme.Good : Theme.Dim, onBeat ? Controls.Keys($"Now: {(s.NextSide < 0 ? "~A~" : "~D~")}!") : "Wait for the tick to cross the mark.");
        EndWork();
    }

    private long _kneadSession = -1;
    private int _kneadPushed;
    private float _kneadAt = -10f;
    private readonly List<int> _kneadMarks = [];

    /// <summary>What baking smells of, in the order it goes: the golden crust is when it comes out.</summary>
    private static readonly string[] BakeSmells = ["raw dough", "warm and yeasty", "bread baking", "a golden crust", "toasty, getting dark", "burning!"];

    private static readonly float4[] BakeColours =
    [
        new(0.91f, 0.84f, 0.69f, 1f), new(0.89f, 0.77f, 0.55f, 1f), new(0.84f, 0.64f, 0.36f, 1f),
        new(0.81f, 0.55f, 0.16f, 1f), new(0.51f, 0.31f, 0.14f, 1f), new(0.19f, 0.14f, 0.12f, 1f),
    ];

    /// <summary>
    /// The smells in order, those it's been through lit and the one it's at now marked, the golden
    /// crust picked out as the one to take it out on. Only the smell: how far it's baked stays hidden.
    /// </summary>
    private void SmellLadder(string smell)
    {
        var now = Array.IndexOf(BakeSmells, smell);
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 40);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var w = size.X / BakeSmells.Length;
        for (var i = 0; i < BakeSmells.Length; i++)
        {
            var a = new float2(min.X + i * w + Gui.S(2), min.Y + Gui.S(4));
            var b = new float2(min.X + (i + 1) * w - Gui.S(2), min.Y + Gui.S(16));
            var col = BakeColours[i];
            dl.AddRectFilled(in a, in b, Gui.U8(col, i <= now ? 1f : 0.25f), Gui.S(3), ImDrawFlags.None);
            if (i == 3)
                dl.AddRect(in a, in b, Theme.U8(Theme.Good), Gui.S(3), ImDrawFlags.None, Gui.S(i == now ? 3 : 1.5f));
            if (i == now)
            {
                var tip = new float2((a.X + b.X) * 0.5f, b.Y + Gui.S(2));
                var l = new float2(tip.X - Gui.S(6), tip.Y + Gui.S(8));
                var r = new float2(tip.X + Gui.S(6), tip.Y + Gui.S(8));
                dl.AddTriangleFilled(in tip, in r, in l, Theme.U8(Theme.Text));
            }
        }
        var label = "out here";
        var ls = ImGui.CalcTextSize(label);
        var lp = new float2(min.X + 3.5f * w - ls.X * 0.5f, min.Y + Gui.S(26) - Gui.S(2));
        if (now != 3)
            dl.AddText(in lp, Theme.U8(Theme.Good, 0.8f), label);
    }

    /// <summary>
    /// Baking: the oven door's shut, so all there is is the smell. The oven glows; what it gives off
    /// from round the door is the smell made visible (its colour the stage, never how far through).
    /// </summary>
    private void DrawBake(BakeSession s)
    {
        BeginWork(s.Title, ("Space", "take it out"), ("Esc", "leave it in"));
        BakeScene(s, SmellColour(s.Smell));
        var now = Array.IndexOf(BakeSmells, s.Smell);
        if (now != _bakeStage)
        {
            _bakeStage = now;
            _bakeChangedAt = _time;
        }
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 150);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        // The oven: a domed front, the shut door, the fire glowing under it.
        var c = new float2((min.X + max.X) * 0.5f, max.Y - Gui.S(14));
        var domeR = Gui.S(62);
        var domeC = new float2(c.X, c.Y - Gui.S(38));
        dl.AddCircleFilled(in domeC, domeR, new ImColor8(118, 112, 120, 255), 40);
        var baseA = new float2(c.X - domeR - Gui.S(10), c.Y - Gui.S(40));
        var baseB = new float2(c.X + domeR + Gui.S(10), c.Y);
        dl.AddRectFilled(in baseA, in baseB, new ImColor8(100, 96, 104, 255), Gui.S(3), ImDrawFlags.None);
        var glow = s.Lit ? 0.55f + 0.2f * MathF.Sin(_time * 5f) : 0f;
        var fA = new float2(c.X - Gui.S(30), c.Y - Gui.S(14));
        var fB = new float2(c.X + Gui.S(30), c.Y - Gui.S(4));
        dl.AddRectFilled(in fA, in fB, Gui.U8(new float4(0.95f, 0.5f, 0.15f, glow)), Gui.S(3), ImDrawFlags.None);
        var dA = new float2(c.X - Gui.S(26), c.Y - Gui.S(66));
        var dB = new float2(c.X + Gui.S(26), c.Y - Gui.S(22));
        dl.AddRectFilled(in dA, in dB, new ImColor8(70, 52, 40, 255), Gui.S(10), ImDrawFlags.None);
        // What comes from round the door: wisps the colour of the smell, rising.
        if (now >= 1)
        {
            var colour = BakeColours[Math.Max(0, now)];
            var thick = now >= 4 ? 1f : 0.6f;
            for (var k = 0; k < 6; k++)
            {
                var rise = (_time * 0.35f + k / 6f) % 1f;
                var x = c.X + (k % 2 == 0 ? -1 : 1) * Gui.S(18) + MathF.Sin(_time * 1.3f + k * 1.7f) * Gui.S(8);
                var y = dA.Y - rise * Gui.S(60);
                var at = new float2(x, y);
                dl.AddCircleFilled(in at, Gui.S(5 + 7 * rise), Gui.U8(colour, 0.8f * thick * (1f - rise)), 16);
            }
        }
        // A pulse when the smell turns: something to notice.
        var turned = _time - _bakeChangedAt;
        if (turned < 1.2f && now > 0)
        {
            var pa = min + Gui.S(3, 3);
            var pb = max - Gui.S(3, 3);
            dl.AddRect(in pa, in pb, Theme.U8(now == 3 ? Theme.Good : now >= 4 ? Theme.Bad : Theme.Accent, 1f - turned / 1.2f), Gui.S(6), ImDrawFlags.None, Gui.S(3));
        }
        ImGui.Spacing();
        SmellLadder(s.Smell);
        var (say, col) = now switch
        {
            3 => (Controls.Keys("A golden crust: take it out now (~Space~)."), Theme.Good),
            4 => (Controls.Keys("Toasty, getting dark: out with it (~Space~)!"), Theme.Warn),
            5 => ("Burning!", Theme.Bad),
            _ => ($"It smells of {s.Smell}. Out on a golden crust.", Theme.Text),
        };
        TextC(col, say);
        if (!s.Lit)
            TextC(Theme.Bad, "The oven's gone cold.");
        EndWork();
    }

    private int _bakeStage = -1;
    private float _bakeChangedAt = -10f;

    /// <summary>A station in reach of your character (the 3x3 round them), or Entity.Null.</summary>
    private Entity StationBeside(Func<Entity, bool> isOne)
    {
        if (!HasAvatar)
            return Entity.Null;
        var at = _sim.World.Get<Position>(_avatar).Cell;
        var map = _sim.Map;
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var cell = new int3(at.X + dx, at.Y + dy, at.Z);
            if (map.InBounds(cell) && map.Structure[map.Idx(cell)] is { IsNull: false } st && isOne(st))
                return st;
        }
        return Entity.Null;
    }

    /// <summary>The oven beside you, between times: whether there's bread in and what it smells of, or how to start.</summary>
    private void OfferOven(List<StationCard> cards)
    {
        if (StationBeside(e => Baking.IsOven(_sim, e)) is not { IsNull: false } oven || View.Oven(oven) is not { } o)
            return;
        cards.Add(new("The bread oven", oven, () =>
        {
            if (o.Loaves > 0)
            {
                TextC(o.Smell == "burning!" ? Theme.Bad : o.Smell == "a golden crust" ? Theme.Good : Theme.Text, $"{(o.Pie ? "A pie's" : "Bread's")} in. It smells of {o.Smell}.");
                SmellLadder(o.Smell);
                TextC(Theme.Dim, Controls.Keys("~F~ to stand by it and take it out."));
            }
            else if (!o.Lit)
                TextC(Theme.Warn, Controls.Keys("Cold. Feed it wood (~F~ with wood on you) before anything goes in."));
            else if (!Baking.DoughIn(_sim, _avatar).IsNull)
                TextC(Theme.Good, Controls.Keys("~F~ puts your dough in. Then it's all in the smell: out on a golden crust."));
            else if (!Pies.RawIn(_sim, _avatar).IsNull)
                TextC(Theme.Good, Controls.Keys("~F~ puts your pie in. Out on a golden crust, as bread is."));
            else
                Wrapped($"Empty. Bread starts at the prep table: knead {Baking.FlourPerDough} flour into dough, let it rise an hour or two, then bring it here.");
        }));
    }

    /// <summary>The spit beside you, between times: what's on it and how it looks, or how to start.</summary>
    private void OfferSpit(List<StationCard> cards)
    {
        if (StationBeside(e => Roasting.IsSpit(_sim, e)) is not { IsNull: false } spit || View.Spit(spit) is not { } p)
            return;
        cards.Add(new("The spit", spit, () =>
        {
            if (p.Pieces > 0)
            {
                Wrapped($"{p.Pieces} meat on the spit: {string.Join(", ", p.Sides.Select(Roasting.CrustLabel))}.");
                if (p.Sides[p.Facing] >= Crust.Dark)
                    TextC(Theme.Bad, "The side to the fire is going dark.");
                TextC(Theme.Dim, Controls.Keys("~F~ to turn it, or lift it off."));
            }
            else if (!p.Lit)
                TextC(Theme.Warn, "Nothing on it, and the fire beside it is out: light the hearth first.");
            else if (!Roasting.Joint(_sim, _avatar).IsNull)
                TextC(Theme.Good, Controls.Keys("~F~ puts your meat on (up to four). Turn it so every side goes golden."));
            else
                Wrapped("Nothing on it. Bring raw meat from the larder and put it on to roast.");
        }));
    }

    /// <summary>The prep table beside you: what you could do there with what's on you, each by name.</summary>
    private void OfferPrep(List<StationCard> cards)
    {
        if (StationBeside(e => Rations.IsPrepTable(_sim, e)) is not { IsNull: false } table)
            return;
        cards.Add(new("The prep table", table, () =>
        {
            var chop = Rations.Choppable(_sim, _avatar);
            var flour = !Baking.FlourIn(_sim, _avatar).IsNull;
            var portions = Rations.PortionsOn(_sim, _avatar);
            // What each step makes, as a tile: lit when you've what it takes, its name and needs in the tooltip.
            var things = _sim.Defs;
            PrepButton(table, PrepStep.Chop, chop.IsNull ? things.Thing("ChoppedMeat") : _sim.World.Get<Thing>(chop).Def,
                chop.IsNull ? "Chop" : $"Chop the {_sim.World.Get<Thing>(chop).Def.Label}", !chop.IsNull,
                "Raw meat, fish or cabbage, cut on the knife's beat. Chopped food cooks quicker.");
            ImGui.SameLine(0, Gui.S(TileGap));
            PrepButton(table, PrepStep.Knead, things.Thing("Dough"), "Knead dough", flour, $"{Baking.FlourPerDough} flour, pushed from either side on the beat. Then let it rise and bake it.");
            ImGui.SameLine(0, Gui.S(TileGap));
            PrepButton(table, PrepStep.Pack, things.Thing("Rations"), "Pack rations", portions >= Rations.Portions, $"{Rations.Portions} portions of any food, wrapped and tied: sold to parties for the road.");
            ImGui.SameLine(0, Gui.S(TileGap));
            var pie = Pies.CanMake(_sim, _avatar);
            PrepButton(table, PrepStep.Pie, things.Thing("RawPie"), "Make a pie", pie, $"A ball of dough and {Pies.Filling} portions of raw food, the rim crimped round. Then the oven.");
            ImGui.SameLine(0, Gui.S(TileGap));
            PrepButton(table, PrepStep.Salt, things.Thing("SaltMeat"), "Salt it", Larder.CanSalt(_sim, _avatar), $"Raw meat or fish rubbed with salt, a scoop to {Larder.PerScoop}: keeps for weeks, and seasons a pot by itself.");
            // A cookhouse apart from the inn: the inn's hamper, once the innkeeper's ordered one.
            if (HeroesNotIncluded.Sim.Hospitality.Provisions.Cooks(_sim, _avatar))
            {
                ImGui.SameLine(0, Gui.S(TileGap));
                PrepButton(table, PrepStep.Hamper, things.Thing(HeroesNotIncluded.Sim.Hospitality.Provisions.HamperDef), "Pack a hamper for the inn",
                    Rations.HamperWanted(_sim, _avatar) && HeroesNotIncluded.Sim.Hospitality.Provisions.DishesOn(_sim, _avatar).Count > 0,
                    $"Up to {HeroesNotIncluded.Sim.Hospitality.Provisions.Holds} servings of the dishes on you, once the innkeeper's ordered one. Put it up on the hatch for them.");
            }
            if (chop.IsNull && !flour && portions < Rations.Portions && !pie)
                TextC(Theme.Dim, "Bring raw food, flour or a few portions of food from the larder.");
        }));
    }

    /// <summary>The butcher block beside you: what it's for, and whether you've anything to dress.</summary>
    private void OfferBlock(List<StationCard> cards)
    {
        if (StationBeside(e => Butchery.IsBlock(_sim, e)) is not { IsNull: false } block)
            return;
        cards.Add(new("The butcher block", block, () =>
        {
            if (Butchery.Side(_sim, _avatar) is { IsNull: false } side)
                TextC(Theme.Good, Controls.Keys($"~F~ to joint the {_sim.World.Get<Thing>(side).Def.Label}: bring the cleaver down on the joints."));
            else if (Butchery.Bird(_sim, _avatar) is { IsNull: false } bird)
                TextC(Theme.Good, Controls.Keys($"~F~ to pluck the {_sim.World.Get<Thing>(bird).Def.Label}: short firm pulls, or the skin tears."));
            else
                Wrapped("Bring a side of pork or a bird in its feathers from the larder: they're dressed here.");
        }));
    }

    /// <summary>Jointing: the side along the block, its joints marked, the cleaver sweeping over it.</summary>
    private void DrawButcher(ButcherSession s)
    {
        BeginWork(s.Title, ("A/D|Point", "carry the cleaver over a mark"), ("F", "bring it down"), ("Esc", "leave it"));
        if (s.StartTick != _blockSession)
        {
            _blockSession = s.StartTick;
            _blockBlows = 0;
            _blockAt = -10f;
        }
        if (s.Blows != _blockBlows)
        {
            _blockBlows = s.Blows;
            _blockAt = _time;
        }
        var since = _time - _blockAt;
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 150);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        var blockA = new float2(min.X + Gui.S(24), max.Y - Gui.S(30));
        var blockB = new float2(max.X - Gui.S(24), max.Y - Gui.S(12));
        dl.AddRectFilled(in blockA, in blockB, new ImColor8(120, 86, 56, 255), Gui.S(3), ImDrawFlags.None);
        var sideA = new float2(blockA.X + Gui.S(26), blockA.Y - Gui.S(34));
        var sideB = new float2(blockB.X - Gui.S(26), blockA.Y);
        var len = sideB.X - sideA.X;
        TakesPointer(new float2(sideA.X, min.Y), new float2(sideB.X, max.Y)); // across: one end of the side to the other
        // The side, its pieces parted at the joints cut so far.
        var cuts = new List<float> { 0f };
        for (var i = 0; i < ButcherSession.JointsAt.Length; i++)
            if (s.Parted[i])
                cuts.Add(ButcherSession.JointsAt[i]);
        cuts.Add(1f);
        cuts.Sort();
        for (var i = 0; i + 1 < cuts.Count; i++)
        {
            var a = new float2(sideA.X + len * cuts[i] + (i > 0 ? Gui.S(3) : 0), sideA.Y);
            var b = new float2(sideA.X + len * cuts[i + 1] - (i + 2 < cuts.Count ? Gui.S(3) : 0), sideB.Y);
            dl.AddRectFilled(in a, in b, new ImColor8(196, 110, 100, 255), Gui.S(4), ImDrawFlags.None);
            var fatA = new float2(a.X, a.Y);
            var fatB = new float2(b.X, a.Y + Gui.S(7));
            dl.AddRectFilled(in fatA, in fatB, new ImColor8(236, 222, 200, 255), Gui.S(3), ImDrawFlags.None);
        }
        // The joints still to part, marked where the knife should go.
        for (var i = 0; i < ButcherSession.JointsAt.Length; i++)
        {
            if (s.Parted[i])
                continue;
            var x = sideA.X + len * ButcherSession.JointsAt[i];
            for (var y = sideA.Y + Gui.S(2); y < sideB.Y - Gui.S(2); y += Gui.S(6))
            {
                var a = new float2(x, y);
                var b = new float2(x, y + Gui.S(3));
                dl.AddLine(in a, in b, new ImColor8(250, 240, 220, 230), Gui.S(2));
            }
        }
        // Where the last blow landed: a gash in the meat, green on a joint, red through bone.
        if (since < 0.9f && s.LastAt >= 0f)
        {
            var gx = sideA.X + len * s.LastAt;
            dl.AddLine(new float2(gx, sideA.Y - Gui.S(2)), new float2(gx, sideB.Y), Theme.U8(s.LastMark == 1 ? Theme.Good : Theme.Bad, 1f - since / 0.9f), Gui.S(3));
        }
        // The cleaver, edge on over where it's held: a faint line down to the meat where it would fall,
        // swaying a little till the hand's settled. A blow drops it there; then it's lifted back.
        var sway = s.Settled ? 0f : MathF.Sin(_time * 21f) * Gui.S(1.5f);
        var cx = sideA.X + len * s.Cleaver + sway;
        if (s.Ready)
            dl.AddLine(new float2(cx, sideA.Y - Gui.S(12)), new float2(cx, sideB.Y), Theme.U8(s.Settled ? Theme.Text : Theme.Dim, 0.35f), Gui.S(1));
        var lift = since < 0.05f ? since / 0.05f : since < 0.18f ? 1f - (since - 0.05f) / 0.13f : 0f; // down and back up
        var drop = lift * Gui.S(30) - (s.Ready ? 0f : Gui.S(6) * (1f - lift));
        var bladeA = new float2(cx - Gui.S(3), sideA.Y - Gui.S(44) + drop);
        var bladeB = new float2(cx + Gui.S(3), sideA.Y - Gui.S(12) + drop);
        var steel = since < 0.3f && s.LastAt >= 0f ? (s.LastMark == 1 ? Theme.U8(Theme.Good) : Theme.U8(Theme.Bad)) : new ImColor8(200, 204, 212, 255);
        // The blade's flat a little to the side, its edge the line it falls on.
        var flatA = new float2(cx, bladeA.Y);
        var flatB = new float2(cx + Gui.S(16), bladeB.Y - Gui.S(4));
        dl.AddRectFilled(in flatA, in flatB, new ImColor8(150, 154, 162, 255), Gui.S(2), ImDrawFlags.None);
        dl.AddRectFilled(in bladeA, in bladeB, steel, Gui.S(1), ImDrawFlags.None);
        var handleA = new float2(cx + Gui.S(8), bladeA.Y - Gui.S(22));
        var handleB = new float2(cx + Gui.S(14), bladeA.Y + Gui.S(2));
        dl.AddRectFilled(in handleA, in handleB, new ImColor8(90, 60, 40, 255), Gui.S(2), ImDrawFlags.None);
        ImGui.Spacing();
        TextC(Theme.Dim, $"Joints parted: {s.Parted.Count(p => p)} of {ButcherSession.JointsAt.Length}. Through bone: {s.Hacks}. Blows left: {ButcherSession.MostBlows - s.Blows}.");
        if (since < 0.8f && s.LastMark >= 0)
            TextC(s.LastMark == 1 ? Theme.Good : Theme.Bad, s.LastMark == 1 ? "Clean through the joint." : "Through the bone: that's waste.");
        else
            TextC(Theme.Dim, s.Settled ? "Steady over it: bring it down." : "Hold it over a mark and let your hand settle: a hurried blow goes wide.");
        EndWork();
    }

    private long _blockSession = -1;
    private int _blockBlows;
    private float _blockAt = -10f;

    /// <summary>Plucking: the bird, its feathers thinning, and how hard the pull is.</summary>
    // The feathers flying off the bird (Puff's Size used as each one's turn), and what's been seen of the plucking to shed them.
    private readonly List<Puff> _feathers = [];
    private long _featherSession = -1;
    private float _featherPlucked, _feathersShed;
    private int _featherMark;

    private void DrawPluck(PluckSession s)
    {
        BeginWork(s.Title, ("F|Press and drag away", "pull, let go to take the handful"), ("Esc", "leave it"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 190);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        // The bird: pale skin under a coat of feathers that thins as it's plucked.
        var c = new float2((min.X + max.X) * 0.5f, min.Y + Gui.S(78));
        // The mouse over a square round the bird, so a drag pulls as hard whichever way it goes.
        var half = new float2(size.X, size.X) * 0.5f;
        TakesPointer(c - half, c + half);
        var gripAt = c - half + new float2(s.GripU, s.GripV) * size.X;
        var hand = ImGui.GetMousePos();
        var gripping = s.Gripping && s.Held && UsingPointer;
        // Gripped, the skin's drawn out toward the hand as hard as it's pulled.
        var toward = gripping && hand != gripAt ? (hand - gripAt) / MathF.Max(1f, MathF.Sqrt((hand - gripAt).X * (hand - gripAt).X + (hand - gripAt).Y * (hand - gripAt).Y)) : new float2(0f, -1f);
        c += toward * Gui.S(6) * Math.Min(1f, s.Pull);
        var stretch = 1f + 0.12f * s.Pull;
        dl.AddEllipseFilled(in c, new float2(Gui.S(70), Gui.S(40) * stretch), new ImColor8(236, 206, 176, 255), 0f, 32);
        var feathers = (int)MathF.Round(48 * (1f - s.Plucked));
        for (var i = 0; i < feathers; i++)
        {
            var a = i * 2.39996f; // golden angle: an even scatter, the same every frame
            var r = MathF.Sqrt((i + 0.5f) / 48f);
            var at = c + new float2(MathF.Cos(a) * r * Gui.S(64), MathF.Sin(a) * r * Gui.S(34) * stretch);
            var tip = at + new float2(Gui.S(7), -Gui.S(3));
            dl.AddLine(in at, in tip, new ImColor8(150, 120, 90, 255), Gui.S(3));
        }
        // A grip by the mouse: a tuft of feathers drawn out from where it took hold to the hand, as tight as the pull.
        if (gripping)
        {
            var tight = Theme.U8(s.Taut ? Theme.Warn : s.Pull >= 0.5f ? Theme.Good : Theme.Text, 0.5f + 0.4f * Math.Min(1f, s.Pull));
            for (var k = -2; k <= 2; k++)
            {
                var side = new float2(-toward.Y, toward.X) * Gui.S(2.5f) * k;
                dl.AddLine(gripAt + side * 0.4f, hand + side, tight, Gui.S(1.5f + 1.5f * Math.Min(1f, s.Pull)));
            }
            dl.AddCircleFilled(in hand, Gui.S(7), new ImColor8(222, 184, 150, 255), 16);
            dl.AddCircleFilled(in gripAt, Gui.S(3), Theme.U8(Theme.Text, 0.8f), 12);
        }
        // How hard it's pulled: taut past the mark, and it'll tear somewhere beyond.
        var barA = new float2(min.X + Gui.S(60), max.Y - Gui.S(22));
        var barB = new float2(max.X - Gui.S(60), max.Y - Gui.S(12));
        dl.AddRectFilled(in barA, in barB, Theme.U8(Theme.Inset), Gui.S(3), ImDrawFlags.None);
        var fillB = new float2(barA.X + (barB.X - barA.X) * Math.Min(1f, s.Pull), barB.Y);
        dl.AddRectFilled(in barA, in fillB, Theme.U8(s.Taut ? Theme.Warn : s.Pull >= 0.5f ? Theme.Good : Theme.Accent), Gui.S(3), ImDrawFlags.None);
        var firmX = barA.X + (barB.X - barA.X) * 0.5f;
        var tautX = barA.X + (barB.X - barA.X) * PluckSession.TautAt;
        var fa = new float2(firmX, barA.Y - Gui.S(4));
        var fb = new float2(firmX, barB.Y + Gui.S(4));
        dl.AddLine(in fa, in fb, Theme.U8(Theme.Good), Gui.S(2));
        var ta = new float2(tautX, barA.Y - Gui.S(4));
        var tb = new float2(tautX, barB.Y + Gui.S(4));
        dl.AddLine(in ta, in tb, Theme.U8(Theme.Warn), Gui.S(2));
        // Feathers: one coming loose for every so much plucked, drifting off the grip; a handful let
        // go throws a little burst from the hand.
        if (s.StartTick != _featherSession)
            (_featherSession, _featherPlucked, _featherMark, _feathersShed) = (s.StartTick, s.Plucked, s.LastMark, 0f);
        var from = gripping ? gripAt : c;
        _feathersShed += Math.Max(0f, s.Plucked - _featherPlucked) * 120f;
        _featherPlucked = s.Plucked;
        var seed = (int)(_time * 997f);
        while (_feathersShed >= 1f)
        {
            _feathersShed -= 1f;
            var r = new SimRandom((ulong)(seed++ * 2654435761u + 7));
            _feathers.Add(new Puff(from, new float2(r.Range(-40f, 40f), r.Range(-70f, -20f)) * Gui.Scale, _time, r.Range(0.7f, 1.3f), r.Range(0f, 6.28f)));
        }
        if (s.LastMark != _featherMark && s.LastMark == 1 && !s.Held)
            for (var k = 0; k < 8; k++)
            {
                var r = new SimRandom((ulong)(seed++ * 2654435761u + 11));
                _feathers.Add(new Puff(gripping ? hand : c, new float2(r.Range(-90f, 90f), r.Range(-110f, -30f)) * Gui.Scale, _time, r.Range(0.6f, 1.1f), r.Range(0f, 6.28f)));
            }
        _featherMark = s.LastMark;
        _feathers.RemoveAll(f => _time - f.Born > f.Life);
        foreach (var f in _feathers)
        {
            var t = _time - f.Born;
            var at = f.Pos + f.Vel * t + new float2(MathF.Sin(t * 5f + f.Size) * Gui.S(6), Gui.S(30) * t * t); // drifting, falling slowly
            var tilt = f.Size + t * 3f;
            var d = new float2(MathF.Cos(tilt), MathF.Sin(tilt)) * Gui.S(4);
            dl.AddLine(at - d, at + d, new ImColor8(150, 120, 90, (byte)(230 * (1f - t / f.Life))), Gui.S(2.5f));
        }
        ImGui.Spacing();
        Bar("Plucked", s.Plucked, Theme.Accent);
        var (col, say) = s.Taut ? (Theme.Warn, "The skin's pulling taut: let go!")
            : s.Held && s.Pull >= 0.5f ? (Theme.Good, "That's firm: let go to take the handful.")
            : gripping && s.Pull < 0.15f ? (Theme.Text, "Drag away from the bird to pull.")
            : s.Held ? (Theme.Text, "Pulling... past the green mark, then let go.")
            : s.LastMark switch
            {
                -1 => (Theme.Bad, "It tore! Shorter pulls."),
                1 => (Theme.Good, "A good handful."),
                0 => (Theme.Dim, "A few feathers. Pull firmer."),
                _ => (Theme.Dim, UsingPointer ? "Press on the bird and drag away to pull; let go past the green mark, before it's taut."
                    : "Hold to pull; let go past the green mark, before it's taut."),
            };
        TextC(col, say);
        if (s.Tears > 0)
            TextC(Theme.Bad, $"Torn {s.Tears} time{(s.Tears == 1 ? "" : "s")}.");
        EndWork();
    }

    /// <summary>Crimping a pie: the rim from above, its marks, the pinch going round, and each crimp made.</summary>
    private void DrawCrimp(CrimpSession s)
    {
        BeginWork(s.Title, ("F", "crimp as the pinch reaches a mark"), ("Esc", "leave it"));
        CrimpScene(s);
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 170);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        var c = (min + max) * 0.5f;
        var r = Gui.S(62);
        dl.AddCircleFilled(in c, r + Gui.S(8), new ImColor8(222, 196, 150, 255), 48);
        dl.AddCircleFilled(in c, r - Gui.S(6), new ImColor8(170, 90, 70, 255), 48);
        var window = CrimpSession.Window * s.Hands.Tolerance;
        float2 Rim(float marks, float radius)
        {
            var a = marks / CrimpSession.Marks * MathF.Tau - MathF.PI / 2f;
            return c + new float2(MathF.Cos(a), MathF.Sin(a)) * radius;
        }
        // The marks: crimped ones pinched (green tight, amber loose), the next one ringed.
        for (var i = 0; i < CrimpSession.Marks; i++)
        {
            var at = Rim(i, r + Gui.S(1));
            if (i < s.Crimped)
                dl.AddCircleFilled(in at, Gui.S(6), Theme.U8(s.Crimps[i] == 1 ? Theme.Good : Theme.Warn), 12);
            else
                dl.AddCircle(in at, Gui.S(6), Theme.U8(Theme.Dim), 12, Gui.S(1.5f));
        }
        // The pinch, going round: bright as it's on a mark.
        var onMark = s.Off <= window;
        var pinch = Rim(s.At, r + Gui.S(1));
        dl.AddCircle(in pinch, Gui.S(10), Theme.U8(onMark ? Theme.Good : Theme.Text), 16, Gui.S(3));
        ImGui.Spacing();
        TextC(onMark ? Theme.Good : Theme.Dim, onMark ? Controls.Keys("Now: ~F~!") : $"Crimped {s.Crimped} of {CrimpSession.Marks}. On the marks, or it leaks.");
        EndWork();
    }

    private void PrepButton(Entity table, PrepStep step, ThingDef makes, string label, bool ready, string tip)
    {
        if (DefTile($"prep-{step}", makes, $"{label}{(ready ? "" : " (not with what you have)")}\n{tip}", dim: !ready, enabled: ready, iconSize: 32))
            Issue(new PrepCommand(_avatar, table, step));
    }

    /// <summary>Chopping on the knife's beat: the knife rising and falling over the food, cutting its way along it, and what the last cut did.</summary>
    private void DrawChop(ChopSession s)
    {
        BeginWork(s.Title, ("A|Move left", "draw your fingers back a slice"), ("F", "chop"), ("Esc", "put the knife down"));
        if (s.StartTick != _chopSession)
        {
            _chopSession = s.StartTick;
            _chopCuts = 0;
            _chopAt = -10f;
        }
        if (s.Cuts + s.Slips != _chopCuts)
        {
            _chopCuts = s.Cuts + s.Slips;
            _chopAt = _time;
        }
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 150);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        TakesPointer(min, max); // the mouse moved left draws the fingers back as far as it moves
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        // The board; the knife held still over it; the food running off to its left under the guiding
        // fingers, and the slices off it piled to its right.
        var boardA = new float2(min.X + Gui.S(16), max.Y - Gui.S(28));
        var boardB = new float2(max.X - Gui.S(16), max.Y - Gui.S(12));
        dl.AddRectFilled(in boardA, in boardB, new ImColor8(150, 110, 70, 255), Gui.S(3), ImDrawFlags.None);
        var food = s.Label switch
        {
            "cabbage" => new ImColor8(120, 170, 80, 255),
            "raw fish" => new ImColor8(150, 160, 170, 255),
            _ => new ImColor8(170, 70, 60, 255),
        };
        var top = boardA.Y - Gui.S(26);
        var knifeX = min.X + size.X * 0.64f;
        var slice = size.X / ChopSession.MouseDraw;               // a slice's width: as far as the mouse moves to bare one
        var left = Math.Max(0, s.Needed - s.Cuts);
        var foodStart = Math.Max(boardA.X + Gui.S(4), knifeX - slice * (left + 0.3f));
        dl.AddRectFilled(new float2(foodStart, top), new float2(knifeX, boardA.Y), food, Gui.S(4), ImDrawFlags.None);
        // Where the fingers should be drawn back to for an even slice: a band, and the bared food lighter.
        var fingers = knifeX - s.Bared * slice;
        var even0 = knifeX - (1f + s.Even) * slice;
        var even1 = knifeX - (1f - s.Even) * slice;
        dl.AddRectFilled(new float2(even0, top - Gui.S(8)), new float2(even1, top - Gui.S(3)), Theme.U8(Theme.Good, 0.6f), Gui.S(2), ImDrawFlags.None);
        if (s.Bared > 0.02f)
            dl.AddRectFilled(new float2(fingers, top), new float2(knifeX, boardA.Y), new ImColor8(255, 255, 255, 40), 0f, ImDrawFlags.None);
        // The slices off it, piled to the right of the blade.
        for (var i = 0; i < Math.Min(s.Cuts, 8); i++)
        {
            var x0 = knifeX + Gui.S(10) + i * Gui.S(12);
            if (x0 + Gui.S(10) > boardB.X)
                break;
            dl.AddRectFilled(new float2(x0, top + Gui.S(4) + (i % 2) * Gui.S(2)), new float2(x0 + Gui.S(10), boardA.Y), food, Gui.S(3), ImDrawFlags.None);
        }
        // The guiding fingers, curled on the food: red while they're still under the blade.
        var skin = s.Unsafe ? new ImColor8(236, 150, 140, 255) : new ImColor8(232, 196, 160, 255);
        for (var k = 0; k < 3; k++)
            dl.AddCircleFilled(new float2(fingers - Gui.S(5), top + Gui.S(4) + k * Gui.S(7)), Gui.S(4.5f), skin, 12);
        dl.AddRectFilled(new float2(fingers - Gui.S(24), top - Gui.S(6)), new float2(fingers - Gui.S(6), top + Gui.S(14)), skin, Gui.S(5), ImDrawFlags.None);
        // The knife: held up, dropped to the board the moment you chop; red on a slip, green on a clean slice.
        var cut = _time - _workPressedAt;
        var lift = cut < 0.06f ? 0f : cut < 0.2f ? (cut - 0.06f) / 0.14f : 1f;
        var edge = boardA.Y - lift * Gui.S(38);
        var bladeA = new float2(knifeX, edge - Gui.S(34));
        var bladeB = new float2(knifeX + Gui.S(5), edge);
        var flash = _time - _chopAt < 0.3f;
        var steel = flash && s.LastMark == -1 ? new ImColor8(230, 90, 80, 255) : flash && s.LastMark == 1 ? Theme.U8(Theme.Good) : new ImColor8(210, 214, 220, 255);
        dl.AddRectFilled(in bladeA, in bladeB, steel, Gui.S(1), ImDrawFlags.None);
        var handleA = new float2(knifeX, bladeA.Y - Gui.S(18));
        var handleB = new float2(knifeX + Gui.S(7), bladeA.Y);
        dl.AddRectFilled(in handleA, in handleB, new ImColor8(90, 60, 40, 255), Gui.S(2), ImDrawFlags.None);
        ImGui.Spacing();
        Bar("Cuts", s.Needed == 0 ? 0f : s.Cuts / (float)s.Needed, Theme.Accent);
        var (colour, said) = s.LastMark switch
        {
            _ when s.Unsafe && s.Cuts + s.Slips > 0 && !flash => (Theme.Dim, "Draw your fingers back a slice (to the green), then chop."),
            1 => (Theme.Good, "Clean."),
            -1 => (Theme.Bad, "Your fingers! Draw them back before you chop."),
            0 when s.Cuts > 0 => (Theme.Warn, s.LastSlice < 1f ? "A sliver: draw back further." : "A slab: not so far back."),
            _ => (Theme.Dim, "Draw your fingers back a slice (to the green), then chop."),
        };
        TextC(colour, said);
        EndWork();
    }

    private long _chopSession = -1;
    private int _chopCuts; // cuts and slips seen, to flash each as it lands
    private float _chopAt = -10f;

    /// <summary>
    /// Packing rations: the cloth's four corners folded on cue, each from its side, then the cord
    /// drawn and tied. The cord reads only as slack, snug or biting: how snug is the packer's own
    /// judgement, and a slack-ish knot never shows.
    /// </summary>
    private void DrawPack(PackSession s)
    {
        if (s.Wrapped)
            BeginWork(s.Title, ("F", "hold to draw the cord, let go to tie"), ("Esc", "leave it"));
        else
            BeginWork(s.Title, ("A/D|Click a side", "fold the corner from its side, as the hand comes round"), ("Esc", "leave it"));
        PackScene(s);
        TakesPointer(_lastScene.Min, _lastScene.Max); // folding: a click on a half folds from that side
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 150);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // The cloth from above, its corners folding in, and the food in the middle.
        var c = (min + max) * 0.5f;
        var half = Gui.S(46);
        var cloth = new float4(0.86f, 0.82f, 0.72f, 1f);
        var a = c + new float2(-half, -half);
        var b = c + new float2(half, half);
        dl.AddRectFilled(in a, in b, Gui.U8(cloth), Gui.S(3), ImDrawFlags.None);
        var food = Gui.S(16);
        var fa = c - new float2(food, food);
        var fb = c + new float2(food, food);
        dl.AddRectFilled(in fa, in fb, Gui.U8(new float4(0.55f, 0.42f, 0.25f, 1f)), Gui.S(6), ImDrawFlags.None);
        // Folded corners: a triangle over the middle, crisp or crumpled as it landed.
        float2[] corners = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)];
        for (var i = 0; i < PackSession.Folds; i++)
        {
            if (i >= s.Folded)
                continue;
            var k = corners[i];
            var side1 = c + new float2(k.X * half, -k.Y * half);
            var side2 = c + new float2(-k.X * half, k.Y * half);
            var shade = 0.7f + 0.25f * s.FoldMarks[i];
            var crumple = (1f - s.FoldMarks[i]) * Gui.S(10);
            var inward = c + k * crumple;
            dl.AddTriangleFilled(in side1, in side2, in inward, Gui.U8(new float4(cloth.X * shade, cloth.Y * shade, cloth.Z * shade, 1f)));
        }
        // The cord, once it's wrapped: drawn tighter as it's pulled.
        if (s.Wrapped)
        {
            var sag = Gui.S(18) * Math.Max(0f, 1f - s.Tension);
            var l = c + new float2(-half - Gui.S(30), sag);
            var r = c + new float2(half + Gui.S(30), sag);
            var cord = new ImColor8(120, 84, 50, 255);
            dl.AddLine(in l, in c, cord, Gui.S(3));
            dl.AddLine(in c, in r, cord, Gui.S(3));
        }
        else
        {
            // The hand coming round: a mark sweeping toward the cue at the top.
            var dir = s.NextSide < 0 ? "A" : "D";
            var cueA = new float2(min.X + Gui.S(12), min.Y + Gui.S(12));
            ImGui.SetCursorScreenPos(cueA);
            TextC(s.OnCue ? Theme.Good : Theme.Dim, Controls.Keys($"Corner {s.Folded + 1} of {PackSession.Folds}: ~{dir}~"));
            ImGui.SetCursorScreenPos(new float2(min.X, max.Y));
        }
        ImGui.Spacing();
        if (!s.Wrapped)
            Bar("", s.Sweep, s.OnCue ? Theme.Good : Theme.Dim);
        else
            TextC(s.Feel == "snug" ? Theme.Good : s.Feel == "biting" ? Theme.Bad : Theme.Dim, $"The cord feels {s.Feel}.");
        EndWork();
    }

    /// <summary>The stew pot beside you, between times: what's in it, how it looks, and its steps.</summary>
    private void OfferPot(List<StationCard> cards)
    {
        if (!HasAvatar || PotBeside() is not { } p)
            return;
        cards.Add(new("The stew pot", p.Pot, () =>
        {
            if (p.Flaring)
            {
                TextC(Theme.Bad, Controls.Keys("The fat's caught: flames over the pot! ~F~ to clap the lid on."));
                if (ImGui.SmallButton("Clap the lid on"u8))
                    Issue(new PotCommand(_avatar, p.Pot, PotStep.Lid));
            }
            if (p.Empty)
                TextC(Theme.Dim, Controls.Keys("Empty. Bring it food from the larder: ~F~ at the pot puts in what you carry."));
            else
            {
                Wrapped($"In it: {p.Contents}. It tastes {p.Tastes}.");
                if (p.House is { } house)
                    TextC(Theme.Good, $"It tastes like your {house}: it's the house dish.");
                if (p.Mends)
                    TextC(Theme.Good, "Fresh enough to be a broth: good for the hurt, and for a healer's gift.");
                TextC(p.Surface == Surface.Simmer ? Theme.Good : Theme.Dim, $"It's {Cooking.SurfaceLabel(p.Surface)}{(p.Lit ? "" : ", and the fire beside it is out")}.");
                if (p.Caught is { } caught)
                    TextC(Theme.Bad, Cap(caught) + ".");
                if (!p.Cooked)
                    Bar("Cooked", p.Doneness, Theme.Accent);
                else
                    TextC(Theme.Good, $"Cooked through: {p.Bowls} bowl{(p.Bowls == 1 ? "" : "s")} in it.");
                if (ImGui.SmallButton("Tend it"u8))
                    Issue(new PotCommand(_avatar, p.Pot, PotStep.Tend));
                ImGui.SameLine(0, 6);
                if (ImGui.SmallButton("Season it"u8))
                    Issue(new PotCommand(_avatar, p.Pot, PotStep.Season));
                ImGui.SameLine(0, 6);
                if (ImGui.SmallButton("Smell it"u8))
                    Issue(new PotCommand(_avatar, p.Pot, PotStep.Sniff));
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip("Has anything in it turned? You can't pick out what. Better to smell it before it goes in.");
                if (p.Doneness >= Cooking.LadleFrom && p.Bowls > 0)
                {
                    ImGui.SameLine(0, 6);
                    if (ImGui.SmallButton("Ladle a bowl"u8))
                        Issue(new PotCommand(_avatar, p.Pot, PotStep.Ladle));
                }
                if (p.Cooked && _sim.World.TryGet<Trade>(_avatar, out var trade) && trade.Def.Serving(Sim.Defs.ServiceKind.Meal))
                    DrawNameDish(p);
            }
        }));
    }

    private ImInputString? _dishNameInput;

    /// <summary>Name a cooked pot the house dish: a button, then the name. The sim says if it isn't good enough.</summary>
    private void DrawNameDish(KnownPot p)
    {
        if (_dishNameInput == null)
        {
            if (ImGui.SmallButton(p.House != null ? "Rename the house dish"u8 : "Make it the house dish"u8))
                _dishNameInput = new ImInputString(Sim.Shops.ShopName.MaxLength + 8, ""u8);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip("Put a name to a fine pot. It goes on the sign, and any pot that tastes like it is the same dish. Some will ask for it.");
            return;
        }
        ImGui.SetNextItemWidth(Gui.S(200));
        var done = ImGui.InputTextWithHint("##dishname"u8, "What's it called?"u8, _dishNameInput, ImGuiInputTextFlags.EnterReturnsTrue, null, IntPtr.Zero);
        OnScreenKeyboard.AfterTextField();
        ImGui.SameLine(0, 6);
        done |= ImGui.SmallButton("Name it"u8);
        ImGui.SameLine(0, 6);
        if (ImGui.SmallButton("Cancel"u8))
            _dishNameInput = null;
        else if (done)
        {
            Issue(new NameDishCommand(_avatar, p.Pot, Narsil.Ui.ImGuiText.Value(_dishNameInput)));
            _dishNameInput = null;
        }
    }

    /// <summary>A stew pot in arm's reach of you, as you see it.</summary>
    private KnownPot? PotBeside()
    {
        var at = _sim.World.Get<Position>(_avatar).Cell;
        var map = _sim.Map;
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var c = new int3(at.X + dx, at.Y + dy, at.Z);
            if (map.InBounds(c) && map.Structure[map.Idx(c)] is { IsNull: false } s && Cooking.IsPot(_sim, s) && View.Pot(s) is { } p)
                return p;
        }
        return null;
    }
}
