using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Work;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The bar's upkeep scenes: scrubbing crockery at the tub in time with the brush, and knocking a
// new keg's tap in as the mallet comes down. Read from the session only.
public sealed partial class VillageGame
{
    private readonly List<Puff> _washSuds = [];
    private float _washSudsDue;

    /// <summary>
    /// Washing up: the piece out of the water, big, with its grime on it in patches; the brush you
    /// move over it (WASD), scrubbing with F held, suds where it scrubs. A clean piece shines a
    /// moment before the next comes up.
    /// </summary>
    private void DrawWash(WashSession s)
    {
        if (UsingPointer)
            BeginWork(s.Title, ("Mouse", "drag to scrub"), ("Esc", "stop"));
        else
            BeginWork(s.Title, ("WASD", "move the brush"), ("F", "hold to scrub"), ("Esc", "stop"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 210);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        // Grey dishwater all round.
        dl.AddRectFilled(in min, in max, new ImColor8(96, 108, 110, 255), Gui.S(6), ImDrawFlags.None);
        for (var i = 0; i < 6; i++)
        {
            var c = new float2(min.X + (0.1f + 0.16f * i) * size.X, min.Y + (0.2f + 0.12f * (i % 3)) * size.Y + MathF.Sin(_time * 1.5f + i) * Gui.S(3));
            dl.AddCircle(in c, Gui.S(10 + 4 * (i % 2)), new ImColor8(130, 145, 148, 120), 16, Gui.S(1));
        }

        // The piece fills a square in the middle; the grid's cells map onto it.
        var side = size.Y - Gui.S(16);
        var p0 = new float2((min.X + max.X - side) * 0.5f, min.Y + Gui.S(8));
        float2 At(float u, float v) => p0 + new float2(u * side, v * side);
        TakesPointer(p0, p0 + new float2(side, side));
        var cw = side / WashSession.GridW;
        var ch = side / WashSession.GridH;
        var wood = new ImColor8(178, 132, 82, 255);
        var dark = new ImColor8(110, 74, 42, 255);
        if (s.Bowl)
        {
            var c = At(0.5f, 0.55f);
            dl.AddCircleFilled(in c, side * 0.46f, dark, 48);
            dl.AddCircleFilled(in c, side * 0.43f, wood, 48);
            dl.AddCircleFilled(in c, side * 0.3f, new ImColor8(160, 116, 70, 255), 48);
        }
        else
        {
            var a = At(0.2f, 0.06f);
            var b = At(0.8f, 0.97f);
            dl.AddRectFilled(in a, in b, dark, Gui.S(10), ImDrawFlags.None);
            var a2 = a + Gui.S(5, 5);
            var b2 = b - Gui.S(5, 5);
            dl.AddRectFilled(in a2, in b2, wood, Gui.S(8), ImDrawFlags.None);
            foreach (var hoop in new[] { 0.25f, 0.75f })
            {
                var h0 = At(0.2f, hoop);
                var h1 = At(0.8f, hoop);
                dl.AddLine(in h0, in h1, Theme.U8(Theme.Iron), Gui.S(4));
            }
            var hand0 = At(0.8f, 0.3f);
            var hand1 = At(0.92f, 0.5f);
            var hand2 = At(0.8f, 0.7f);
            dl.AddLine(in hand0, in hand1, dark, Gui.S(7));
            dl.AddLine(in hand1, in hand2, dark, Gui.S(7));
        }

        // The grime: a blot per cell, as thick as what's left there.
        for (var y = 0; y < WashSession.GridH; y++)
            for (var x = 0; x < WashSession.GridW; x++)
            {
                var g = s.GrimeAt(x, y);
                if (g <= 0.02f)
                    continue;
                var c = p0 + new float2((x + 0.5f) * cw, (y + 0.5f) * ch);
                dl.AddCircleFilled(in c, MathF.Max(cw, ch) * (0.45f + 0.35f * g), new ImColor8(78, 60, 34, (byte)(230 * Math.Min(1f, g))), 10);
            }

        // Clean: a shine across it.
        if (s.Shine > 0)
        {
            var t = 1f - s.Shine / (float)WashSession.ShineSteps;
            for (var k = 0; k < 5; k++)
            {
                var c = At(0.3f + 0.1f * k, 0.25f + 0.12f * (k % 3));
                var r = Gui.S(6 + 8 * MathF.Sin(t * MathF.PI));
                var a = c - new float2(r, 0);
                var b = c + new float2(r, 0);
                var a2 = c - new float2(0, r);
                var b2 = c + new float2(0, r);
                dl.AddLine(in a, in b, new ImColor8(255, 250, 220, 230), Gui.S(2));
                dl.AddLine(in a2, in b2, new ImColor8(255, 250, 220, 230), Gui.S(2));
            }
            var label = "Clean!";
            var ls = ImGui.CalcTextSize(label);
            var lp = new float2((min.X + max.X - ls.X) * 0.5f, max.Y - ls.Y - Gui.S(10));
            dl.AddText(in lp, Theme.U8(Theme.Good), label);
        }

        // The brush: bristles down on the piece, a handle up and away; it jiggles as it scrubs.
        var jig = s.Scrubbing ? new float2(MathF.Sin(_time * 40f) * Gui.S(4), MathF.Cos(_time * 33f) * Gui.S(2)) : default;
        var bc = p0 + new float2(s.BrushX * side, s.BrushY * side) + jig;
        // As wide as what it scrubs.
        var bw = WashSession.BrushRadius * 2f * side;
        var bh = bw * 0.4f;
        var bristle0 = bc - new float2(bw * 0.5f, 0);
        var bristle1 = bc + new float2(bw * 0.5f, bh * 0.5f);
        dl.AddRectFilled(in bristle0, in bristle1, new ImColor8(220, 200, 150, 255), Gui.S(2), ImDrawFlags.None);
        var back0 = bc - new float2(bw * 0.55f, bh * 0.5f);
        var back1 = bc + new float2(bw * 0.55f, 0);
        dl.AddRectFilled(in back0, in back1, new ImColor8(120, 80, 46, 255), Gui.S(4), ImDrawFlags.None);
        var handleEnd = bc + Gui.S(50, -46);
        var handleStart = bc + new float2(bw * 0.3f, -bh * 0.4f);
        dl.AddLine(in handleStart, in handleEnd, new ImColor8(120, 80, 46, 255), Gui.S(6));

        // Suds where it scrubs.
        var dt = ImGui.GetIO().DeltaTime;
        if (s.Scrubbing)
            _washSudsDue += dt * 40f;
        var seed = (int)(_time * 1000f);
        while (_washSudsDue >= 1f)
        {
            _washSudsDue -= 1f;
            var r = new Narsil.Sim.Core.SimRandom((ulong)(seed++ * 2654435761u + 3));
            _washSuds.Add(new Puff(bc + new float2(r.Range(-20f, 20f), r.Range(0f, 10f)) * Gui.Scale, new float2(r.Range(-30f, 30f), r.Range(-50f, -15f)) * Gui.Scale,
                _time, r.Range(0.4f, 0.9f), Gui.S(r.Range(2f, 5f))));
        }
        _washSuds.RemoveAll(p => _time - p.Born > p.Life);
        foreach (var p in _washSuds)
        {
            var t = _time - p.Born;
            var at = p.Pos + p.Vel * t;
            dl.AddCircle(in at, p.Size, new ImColor8(245, 248, 250, (byte)(220 * (1f - t / p.Life))), 10, Gui.S(1.2f));
        }

        ImGui.Spacing();
        ImGui.Text($"{s.Washed} of {s.Count} washed.");
        ImGui.SameLine(0, 10);
        Bar("Clean", s.Shine > 0 ? 1f : s.Clean, Theme.Accent);
        EndWork();
    }

    /// <summary>The cask's end, the tap and the mallet swinging: strike as it comes down onto the tap.</summary>
    private void DrawTap(TapSession s)
    {
        BeginWork(s.Title, ("F", "strike as the mallet comes down"), ("Esc", "leave it"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 150);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // The cask's head, hooped, with the tap going into it.
        var c = new float2(min.X + size.X * 0.4f, (min.Y + max.Y) * 0.5f);
        var px = MathF.Max(1f, MathF.Round(3f * Gui.Scale));
        if (Gui.TrySprite("ui/work/cask_end", out var caskArt))
        {
            var half = new float2(20, 20) * px;
            Gui.Image(dl, caskArt, c - half, c + half, Gui.White);
        }
        else
        {
            dl.AddCircleFilled(in c, Gui.S(54), new ImColor8(120, 82, 46, 255), 40);
            dl.AddCircle(in c, Gui.S(54), Theme.U8(Theme.Iron), 40, Gui.S(4));
        }
        // The tap, driven home into the cask's side a little further with every good blow.
        var seated = s.Blows / (float)TapSession.BlowsNeeded;
        var tapPx = MathF.Max(1f, MathF.Round(2f * Gui.Scale));
        var tapMin = new float2(c.X + Gui.S(40) - Gui.S(30) * seated, c.Y - 6 * tapPx);
        var tapMax = tapMin + new float2(26, 12) * tapPx;
        if (Gui.TrySprite("ui/work/spigot", out var spigotArt))
            Gui.Image(dl, spigotArt, new float2(tapMax.X, tapMin.Y), new float2(tapMin.X, tapMax.Y), Gui.White); // mirrored: the spout outward
        else
            dl.AddRectFilled(in tapMin, in tapMax, new ImColor8(170, 140, 90, 255), Gui.S(2), ImDrawFlags.None);

        // The mallet: raised, then down onto the tap's end.
        var head = new float2(tapMax.X + Gui.S(18), c.Y - Gui.S(60) * (1f - s.Mallet));
        if (Gui.TrySprite("ui/work/mallet", out var malletArt))
        {
            var mMin = head - new float2(6, 8) * px;
            Gui.Image(dl, malletArt, mMin, mMin + new float2(40, 16) * px, s.OnTheTap ? Gui.U8(Theme.Good) : Gui.White);
        }
        else
        {
            var headMin = head - Gui.S(12, 16);
            var headMax = head + Gui.S(12, 16);
            dl.AddRectFilled(in headMin, in headMax, s.OnTheTap ? Theme.U8(Theme.Good) : new ImColor8(110, 80, 50, 255), Gui.S(2), ImDrawFlags.None);
            var handleEnd = head + new float2(Gui.S(60), Gui.S(10));
            dl.AddLine(in head, in handleEnd, new ImColor8(150, 110, 70, 255), Gui.S(4));
        }

        // Foam where it's sprayed.
        for (var i = 0; i < s.Misses * 6; i++)
        {
            var p = new float2(c.X + Gui.S(40) + ((i * 37) % 60) * Gui.Scale, c.Y + Gui.S(10) + ((i * 53) % 40) * Gui.Scale);
            dl.AddCircleFilled(in p, Gui.S(3 + i % 3), new ImColor8(240, 232, 210, 200), 8);
        }

        ImGui.Spacing();
        ImGui.Text($"Driven home: {s.Blows} of {TapSession.BlowsNeeded}.");
        if (s.Misses > 0)
        {
            ImGui.SameLine(0, 10);
            TextC(Theme.Bad, $"{s.Wasted} servings sprayed out.");
        }
        EndWork();
    }

    // The mop in the scrub scene: where its head is (eased toward the way you last stroked), the
    // strokes seen so far (a new one throws suds), and the suds.
    private float _mopX;
    private int _mopStrokesSeen = -1;
    private float _mopY;

    // The mess a scrub began with, by session: the bar under it shows what's left of that.
    private (long Session, float Amount) _mopStart = (-1, 0f);

    private float MessAtStart(Sim.Hospitality.ScrubSession s)
    {
        if (_mopStart.Session != s.StartTick)
            _mopStart = (s.StartTick, s.Amount);
        return _mopStart.Amount;
    }
    private long _mopSession = -1;
    private readonly List<Puff> _suds = [];

    /// <summary>
    /// Scrubbing up a mess, seen from above: the stain on the boards fading as it's scrubbed, the
    /// mop swinging across it with each stroke (A, D), a wet sheen where it's been and suds
    /// thrown up by every stroke. How much is left is shown by the stain itself, and a bar.
    /// </summary>
    private void DrawScrub(Sim.Hospitality.ScrubSession s)
    {
        if (s.StartTick != _mopSession)
            (_mopSession, _mopStrokesSeen) = (s.StartTick, -1); // a new stain: the mop starts in the middle
        if (UsingPointer)
            BeginWork(s.Title, ("Mouse", "drag over it to rub it out"), ("Esc", "stop"));
        else
            BeginWork(s.Title, ("A/D", "sweep back and forth"), ("W/S", "the next row"), ("Esc", "stop"));
        TextC(Theme.Dim, s.Mop ? "The mop makes short work of it. Keep it moving over the mess." : "By hand, this takes a while: a mop would be quicker. Keep the rag moving.");
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 170);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;

        // The floorboards.
        dl.AddRectFilled(in min, in max, new ImColor8(96, 66, 42, 255), Gui.S(4), ImDrawFlags.None);
        for (var y = min.Y + Gui.S(24); y < max.Y; y += Gui.S(24))
        {
            var a = new float2(min.X, y);
            var b = new float2(max.X, y);
            dl.AddLine(in a, in b, new ImColor8(60, 40, 26, 255), Gui.S(2));
        }
        for (var row = 0; row * Gui.S(24) < size.Y; row++)
        {
            var x = min.X + ((row * 137) % 300) * Gui.Scale + Gui.S(40);
            var a = new float2(x, min.Y + row * Gui.S(24));
            var b = new float2(x, Math.Min(max.Y, a.Y + Gui.S(24)));
            dl.AddLine(in a, in b, new ImColor8(60, 40, 26, 255), Gui.S(2));
        }

        // The mess's grid lies over the floor inside a margin; the mouse maps onto the same.
        var pad = Gui.S(14);
        var f0 = min + new float2(pad, pad);
        var fs = size - new float2(pad * 2, pad * 2);
        TakesPointer(f0, f0 + fs);
        float2 At(float u, float v) => f0 + new float2(u * fs.X, v * fs.Y);
        float cw = fs.X / Sim.Hospitality.ScrubSession.GridW, ch = fs.Y / Sim.Hospitality.ScrubSession.GridH;

        // The stain in blots, each as dark as what's left of it (blood looks like any dark stain).
        var tint = s.Kind switch
        {
            Sim.Hospitality.MessKind.SpilledAle => new float4(0.78f, 0.58f, 0.22f, 1f),
            Sim.Hospitality.MessKind.Mud => new float4(0.36f, 0.25f, 0.14f, 1f),
            Sim.Hospitality.MessKind.Crumbs => new float4(0.85f, 0.72f, 0.48f, 1f),
            _ => new float4(0.2f, 0.12f, 0.1f, 1f),
        };
        for (var y = 0; y < Sim.Hospitality.ScrubSession.GridH; y++)
            for (var x = 0; x < Sim.Hospitality.ScrubSession.GridW; x++)
            {
                var g = s.BlotAt(x, y);
                if (g <= 0.02f)
                    continue;
                var c = At((x + 0.5f) / Sim.Hospitality.ScrubSession.GridW, (y + 0.5f) / Sim.Hospitality.ScrubSession.GridH);
                dl.AddCircleFilled(in c, MathF.Max(cw, ch) * (0.4f + 0.3f * g), Gui.U8(tint, 0.85f * Math.Min(1f, g)), 12);
            }
        var left = Math.Clamp(s.Amount / Math.Max(0.001f, MessAtStart(s)), 0f, 1f);

        // The mop: its head eases after where it is on the floor; the handle runs up and off to the right.
        var want = At(s.MopX, s.MopY);
        if (_mopStrokesSeen < 0)
            (_mopX, _mopY) = (want.X, want.Y);
        var dt = ImGui.GetIO().DeltaTime;
        _mopX += (want.X - _mopX) * Math.Min(1f, dt * 16f);
        _mopY += (want.Y - _mopY) * Math.Min(1f, dt * 16f);
        var headC = new float2(_mopX, _mopY - Gui.S(10));
        // The wet sheen round where it's working, as wide as it reaches.
        var rx = Sim.Hospitality.ScrubSession.Reach * fs.X;
        dl.AddCircleFilled(in want, rx, new ImColor8(200, 220, 230, 40), 24);
        var handleTop = new float2(headC.X + Gui.S(90), min.Y - Gui.S(6));
        dl.AddLine(in headC, in handleTop, new ImColor8(196, 160, 110, 255), Gui.S(5));
        for (var i = -6; i <= 6; i++)
        {
            var a = headC + new float2(i * Gui.S(3), 0);
            var swing = MathF.Sin(_time * 18f + i) * Gui.S(2) - (want.X - _mopX) * 0.05f;
            var b = a + new float2(swing + i * Gui.S(1.5f), Gui.S(22));
            dl.AddLine(in a, in b, new ImColor8(210, 210, 200, 255), Gui.S(3));
        }

        // Suds thrown up with each new stroke.
        if (_mopStrokesSeen >= 0 && s.Strokes > _mopStrokesSeen)
            for (var k = 0; k < 8; k++)
            {
                var r = new Narsil.Sim.Core.SimRandom((ulong)(s.Strokes * 31 + k + 1));
                _suds.Add(new Puff(headC + new float2(r.Range(-20f, 20f), Gui.S(18)) * 1f, new float2(r.Range(-40f, 40f), r.Range(-60f, -20f)) * Gui.Scale,
                    _time, r.Range(0.3f, 0.7f), Gui.S(r.Range(2f, 5f))));
            }
        _mopStrokesSeen = s.Strokes;
        _suds.RemoveAll(p => _time - p.Born > p.Life);
        foreach (var p in _suds)
        {
            var t = _time - p.Born;
            var at = p.Pos + p.Vel * t;
            dl.AddCircle(in at, p.Size, new ImColor8(240, 245, 250, (byte)(220 * (1f - t / p.Life))), 10, Gui.S(1.2f));
        }

        ImGui.Spacing();
        Bar("Left", left, Theme.Warn);
        ImGui.Text($"{s.Strokes} stroke{(s.Strokes == 1 ? "" : "s")}.");
        EndWork();
    }

    /// <summary>Making up a bed: strip it (hold F), shake the clean sheet out (F at the top), tuck it in (A and D in turn).</summary>
    private void DrawMakeBed(Sim.Hospitality.MakeBedSession s)
    {
        var hints = s.At switch
        {
            Sim.Hospitality.BedStep.Strip => new[] { ("F", "hold to pull the old sheet off"), ("Esc", "leave it") },
            Sim.Hospitality.BedStep.Spread => [("F", "as the sheet billows to the top"), ("Esc", "leave it")],
            _ => [("A", "tuck the head end"), ("D", "tuck the foot"), ("Esc", "leave it")],
        };
        BeginWork(s.Title, hints);
        MakeBedScene(s.At, s.Stripped, s.Billow, s.Flopped, s.OnCue, s.Tucked, s.NextSide);
        string[] steps = ["Strip it", "Shake out a clean sheet", "Tuck it in"];
        for (var i = 0; i < steps.Length; i++)
        {
            var at = (int)s.At;
            var line = i == 2 && at == 2 ? $"{steps[i]}: {s.Tucked} of {Sim.Hospitality.MakeBedSession.Corners} corners" : steps[i];
            TextC(i < at ? Theme.Good : i == at ? Theme.Accent : Theme.Dim, (i < at ? "Done: " : "") + line);
        }
        if (s.At == Sim.Hospitality.BedStep.Strip)
            Bar("", s.Stripped, Theme.Accent);
        else if (s.At == Sim.Hospitality.BedStep.Spread)
            Bar("", s.Flopped ? 0f : s.Billow, s.OnCue ? Theme.Good : Theme.Dim);
        TextC(Theme.Dim, s.Flopped ? "It flopped. Shake it out again." : "Clean linen on, last night's into your pack for the wash tub.");
        EndWork();
    }

    // The house mix being put together in the bar panel: which bar ingredients are ticked.
    private readonly HashSet<string> _houseAdds = [];

    /// <summary>
    /// Mixing a drink: the cues coming along the bar toward the mark, each pressed (F, A or D) as it
    /// reaches it. Done cues show how they landed.
    /// </summary>
    private void DrawMix(MixSession s)
    {
        BeginWork(s.Title, ("F A D", "the cue as it reaches the mark"), ("Esc", "stop"));
        MixScene(s);
        MixCues(s.Cues, s.Resolved, s.StepsDone, s.Last);
        ImGui.Spacing();
        ImGui.Text($"{s.Hits} of {s.Resolved} on the mark, {s.Cues.Length - s.Resolved} to go.");
        if (s.Last is { } last)
        {
            ImGui.SameLine(0, 10);
            TextC(last ? Theme.Good : Theme.Bad, last ? "Neat." : "Sloppy.");
        }
        EndWork();
    }

    /// <summary>
    /// The mixing cues sliding along the bar toward the mark (<see cref="MixSession"/>'s, or the
    /// work-scene gallery's): <paramref name="steps"/> is the session's steps done, <paramref name="last"/>
    /// how the last one taken landed.
    /// </summary>
    private void MixCues(string cues, int resolved, float steps, bool? last)
    {
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 110);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        var markX = min.X + size.X * 0.2f;
        var midY = (min.Y + max.Y) * 0.5f;
        var a = new float2(markX, min.Y + Gui.S(8));
        var b = new float2(markX, max.Y - Gui.S(8));
        dl.AddLine(in a, in b, Theme.U8(Theme.Good), Gui.S(2));
        // Steps to pixels: a cue slides in from the right and reaches the mark when it's due.
        var perStep = size.X * 0.7f / (MixSession.Gap * 3f);
        var radius = Gui.S(19);
        // Dark tokens with a barrel-hoop ring and a bright letter: ember's kept for rarer things.
        // The one due next rings green as it nears the mark; the last one taken slides on past it,
        // flashing how it landed.
        using (Gui.BodyFont(ImGui.GetFontSize() * 1.4f))
        {
            if (resolved > 0 && last is { } landed)
            {
                var x = markX + (MixSession.DueOf(resolved - 1) - steps) * perStep;
                if (x > min.X + radius)
                    Cue(resolved - 1, x, landed ? Theme.Good : Theme.Bad, Math.Clamp((x - min.X) / (markX - min.X), 0.25f, 1f));
            }
            for (var i = resolved; i < cues.Length; i++)
            {
                var x = markX + (MixSession.DueOf(i) - steps) * perStep;
                if (x > max.X - radius)
                    break;
                var near = i == resolved && x - markX < MixSession.Gap * 0.5f * perStep;
                Cue(i, x, near ? Theme.Good : Theme.Barrel, 1f);
            }
        }

        void Cue(int i, float x, float4 ring, float alpha)
        {
            var c = new float2(x, midY);
            dl.AddCircleFilled(in c, radius, Theme.U8(Theme.Oak, alpha), 24);
            dl.AddCircle(in c, radius, Theme.U8(ring, alpha), 24, Gui.S(ring == Theme.Barrel ? 2 : 3));
            var label = Controls.Cue(cues[i]);
            var ts = ImGui.CalcTextSize(label);
            var tp = c - ts * 0.5f;
            dl.AddText(in tp, Theme.U8(Theme.Parchment, alpha), label);
        }
    }

    /// <summary>The bar beside you: the named drinks (and what they take), and a house mix of whatever's ticked.</summary>
    private void DrawBarPanel()
    {
        if (!HasAvatar || BarBeside() is not { IsNull: false } bar)
            return;
        var w = _sim.World;
        _hud.Next(Anchor.Bottom);
        Panel.Begin("The bar###bar", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav);
        _hud.Took();
        TextC(Theme.Dim, "From the cask, with a clean tankard and what goes in to hand (yours, or the shelf beside you):");
        foreach (var drink in _sim.Defs.AllThings.Where(d => d.Mix is { House: false }))
        {
            ImGui.PushID(drink.Name);
            var adds = MixSession.AddsOf(_sim, drink, null);
            var why = MixSession.WhyNot(_sim, _avatar, bar, drink, null);
            if (why != null)
                ImGui.BeginDisabled(true);
            if (ImGui.SmallButton($"Mix a {drink.Label}"))
                Issue(new MixCommand(_avatar, bar, drink, null));
            if (why != null)
                ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(why ?? drink.Description);
            ImGui.SameLine(0, 8);
            TextC(why == null ? Theme.Dim : Theme.Warn, why == null ? $"ale, {string.Join(", ", adds.Select(d => d.Label))}" : why);
            ImGui.PopID();
        }
        Gui.Rule();
        TextC(Theme.Dim, "A house mix of:");
        foreach (var add in _sim.Defs.AllThings.Where(d => d.MixCue is { Length: > 0 }))
        {
            ImGui.SameLine(0, 8);
            var on = _houseAdds.Contains(add.Name);
            if (ImGui.Checkbox(add.Label, ref on))
            {
                if (on)
                    _houseAdds.Add(add.Name);
                else
                    _houseAdds.Remove(add.Name);
            }
        }
        if (_houseAdds.Count > 0)
        {
            var house = _sim.Defs.Thing("HouseMix");
            var adds = string.Join(",", _houseAdds.Order());
            var why = MixSession.WhyNot(_sim, _avatar, bar, house, adds);
            if (why != null)
                ImGui.BeginDisabled(true);
            if (ImGui.SmallButton("Mix it"u8))
                Issue(new MixCommand(_avatar, bar, house, adds));
            if (why != null)
            {
                ImGui.EndDisabled();
                ImGui.SameLine(0, 8);
                TextC(Theme.Warn, why);
            }
        }
        Panel.End();
    }

    /// <summary>A bar counter in arm's reach of you.</summary>
    private Entity BarBeside()
    {
        var at = _sim.World.Get<Sim.Components.Position>(_avatar).Cell;
        var map = _sim.Map;
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var c = new int3(at.X + dx, at.Y + dy, at.Z);
            if (map.InBounds(c) && map.Structure[map.Idx(c)] is { IsNull: false } s && _sim.World.TryGet<Sim.Components.Thing>(s, out var t)
                && t.Def.Building is { Bar: true })
                return s;
        }
        return Entity.Null;
    }
}
