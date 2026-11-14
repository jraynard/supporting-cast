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
        BeginWork(s.Title, ("A/D", "back and forth"), ("Esc", "stop"));
        TextC(Theme.Dim, s.Mop ? "The mop makes short work of it." : "By hand, this takes a while: a mop would be quicker.");
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

        // The stain, fading as it comes up (blood looks like any dark stain).
        var mid = (min + max) * 0.5f;
        var sprite = s.Kind switch
        {
            Sim.Hospitality.MessKind.SpilledAle => "ui/mess_puddle",
            Sim.Hospitality.MessKind.Mud => "ui/mess_mud",
            Sim.Hospitality.MessKind.Crumbs => "ui/mess_crumbs",
            _ => "ui/mess_stain",
        };
        var left = Math.Clamp(s.Amount, 0f, 1f);
        if (Gui.TrySprite(sprite, out var stain))
        {
            var box = Gui.S(150, 150);
            Gui.Sprite(dl, stain, mid - box * 0.5f, box, Gui.U8(new float4(1f, 1f, 1f, 0.15f + 0.85f * left)));
        }

        // The mop: its head eases toward the side you last stroked to; the handle runs up and off to the right.
        var reach = Gui.S(110);
        var target = mid.X + s.LastDir * reach;
        if (_mopStrokesSeen < 0)
            _mopX = mid.X;
        var dt = ImGui.GetIO().DeltaTime;
        _mopX += (target - _mopX) * Math.Min(1f, dt * 12f);
        var headC = new float2(_mopX, mid.Y + Gui.S(10));
        // The wet sheen it leaves along its way.
        var sheen0 = new float2(Math.Min(_mopX, mid.X) - Gui.S(20), mid.Y - Gui.S(10));
        var sheen1 = new float2(Math.Max(_mopX, mid.X) + Gui.S(20), mid.Y + Gui.S(30));
        dl.AddRectFilled(in sheen0, in sheen1, new ImColor8(200, 220, 230, 40), Gui.S(10), ImDrawFlags.None);
        var handleTop = new float2(headC.X + Gui.S(90), min.Y - Gui.S(6));
        dl.AddLine(in headC, in handleTop, new ImColor8(196, 160, 110, 255), Gui.S(5));
        for (var i = -6; i <= 6; i++)
        {
            var a = headC + new float2(i * Gui.S(3), 0);
            var swing = MathF.Sin(_time * 18f + i) * Gui.S(2) - (target - _mopX) * 0.05f;
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

    /// <summary>Making up a bed: three movements, each as the hands come round.</summary>
    private void DrawMakeBed(Sim.Hospitality.MakeBedSession s)
    {
        BeginWork(s.Title, ("F", "as the hands come round"), ("Esc", "leave it"));
        MakeBedScene(s.Movements, s.Sweep, s.OnCue);
        string[] moves = ["Strip it", "Smooth the sheet", "Tuck it in"];
        for (var i = 0; i < moves.Length; i++)
            TextC(i < s.Movements ? Theme.Good : i == s.Movements ? Theme.Accent : Theme.Dim, (i < s.Movements ? "Done: " : "") + moves[i]);
        Bar("", s.Sweep, s.OnCue ? Theme.Good : Theme.Dim);
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
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 90);
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
        for (var i = s.Resolved; i < s.Cues.Length; i++)
        {
            var x = markX + (s.DueOf(i) - s.StepsDone) * perStep;
            if (x > max.X - Gui.S(10))
                break;
            var c = new float2(x, midY);
            dl.AddCircleFilled(in c, Gui.S(14), Theme.U8(Theme.Accent, 0.85f), 20);
            var label = Controls.Cue(s.Cues[i]);
            var ts = ImGui.CalcTextSize(label);
            var tp = c - ts * 0.5f;
            dl.AddText(in tp, Theme.U8(Theme.Text), label);
        }
        ImGui.Spacing();
        ImGui.Text($"{s.Hits} of {s.Resolved} on the mark, {s.Cues.Length - s.Resolved} to go.");
        if (s.Last is { } last)
        {
            ImGui.SameLine(0, 10);
            TextC(last ? Theme.Good : Theme.Bad, last ? "Neat." : "Sloppy.");
        }
        EndWork();
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
