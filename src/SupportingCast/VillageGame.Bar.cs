using Brutal.ImGuiApi;
using Narsil.Ui;
using SupportingCast.Sim.Ecs;
using SupportingCast.Sim.Work;
using SupportingCast.Ui;

namespace SupportingCast;

// The bar's upkeep scenes: scrubbing crockery at the tub in time with the brush, and knocking a
// new keg's tap in as the mallet comes down. Read from the session only.
public sealed partial class VillageGame
{
    /// <summary>The tub and the brush going round: scrub as it comes to the top of its stroke.</summary>
    private void DrawWash(WashSession s)
    {
        BeginWork(s.Title, ("F", "scrub as the brush comes round"), ("Esc", "stop"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 150);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // The tub of grey water, and the vessel in it.
        var tubMin = new float2(min.X + size.X * 0.2f, min.Y + size.Y * 0.45f);
        var tubMax = new float2(max.X - size.X * 0.2f, max.Y - Gui.S(8));
        dl.AddRectFilled(in tubMin, in tubMax, new ImColor8(96, 66, 40, 255), Gui.S(4), ImDrawFlags.None);
        var waterMin = new float2(tubMin.X + Gui.S(6), tubMin.Y + Gui.S(8));
        var waterMax = new float2(tubMax.X - Gui.S(6), tubMax.Y - Gui.S(6));
        dl.AddRectFilled(in waterMin, in waterMax, new ImColor8(120, 130, 128, 255), Gui.S(2), ImDrawFlags.None);
        var cup = (waterMin + waterMax) * 0.5f - new float2(0, Gui.S(18));
        var grime = (byte)(200 * (1f - s.Scrub));
        var cupMin = cup - Gui.S(22, 20);
        var cupMax = cup + Gui.S(22, 20);
        dl.AddRectFilled(in cupMin, in cupMax, new ImColor8(150, 112, 70, 255), Gui.S(4), ImDrawFlags.None);
        dl.AddRectFilled(in cupMin, in cupMax, new ImColor8(70, 60, 40, grime), Gui.S(4), ImDrawFlags.None);

        // The brush sweeping across: the mark at the top of its stroke is the moment to scrub.
        var arcY = min.Y + Gui.S(22);
        float X(float brush) => min.X + size.X * (0.15f + 0.7f * brush);
        var markMin = new float2(X(0.75f), arcY - Gui.S(10));
        var markMax = new float2(X(1f) + Gui.S(14), arcY + Gui.S(10));
        dl.AddRectFilled(in markMin, in markMax, Theme.U8(s.OnBeat ? Theme.Good : Theme.Dim, 0.35f), Gui.S(3), ImDrawFlags.None);
        var bx = X(s.Brush);
        var brush = new float2(bx, arcY);
        var brushMin = brush - Gui.S(14, 6);
        var brushMax = brush + Gui.S(14, 6);
        dl.AddRectFilled(in brushMin, in brushMax, new ImColor8(160, 120, 70, 255), Gui.S(2), ImDrawFlags.None);

        ImGui.Spacing();
        ImGui.Text($"{s.Washed} of {s.Count} washed.");
        if (s.Washed > 0 || s.Scrub > 0)
        {
            ImGui.SameLine(0, 10);
            TextC(s.LastGood ? Theme.Good : Theme.Dim, s.LastGood ? "A good stroke." : "That barely touched it.");
        }
        Bar("Clean", s.Scrub, Theme.Accent);
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
        dl.AddCircleFilled(in c, Gui.S(54), new ImColor8(120, 82, 46, 255), 40);
        dl.AddCircle(in c, Gui.S(54), Theme.U8(Theme.Iron), 40, Gui.S(4));
        var seated = s.Blows / (float)TapSession.BlowsNeeded;
        var tapMin = new float2(c.X + Gui.S(40) - Gui.S(30) * seated, c.Y - Gui.S(6));
        var tapMax = new float2(c.X + Gui.S(90) - Gui.S(30) * seated, c.Y + Gui.S(6));
        dl.AddRectFilled(in tapMin, in tapMax, new ImColor8(170, 140, 90, 255), Gui.S(2), ImDrawFlags.None);

        // The mallet: raised, then down onto the tap's end.
        var head = new float2(tapMax.X + Gui.S(18), c.Y - Gui.S(60) * (1f - s.Mallet));
        var headMin = head - Gui.S(12, 16);
        var headMax = head + Gui.S(12, 16);
        dl.AddRectFilled(in headMin, in headMax, s.OnTheTap ? Theme.U8(Theme.Good) : new ImColor8(110, 80, 50, 255), Gui.S(2), ImDrawFlags.None);
        var handleEnd = head + new float2(Gui.S(60), Gui.S(10));
        dl.AddLine(in head, in handleEnd, new ImColor8(150, 110, 70, 255), Gui.S(4));

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
            var label = s.Cues[i].ToString();
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
