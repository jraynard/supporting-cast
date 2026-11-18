using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Carpentry;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The carpenter in the woods: felling a tree where it's aimed (FellSession), bucking the trunk into
// logs (BuckSession, and the other end of the saw, CrosscutSession) and splitting firewood at the
// chopping block (SplitSession). Everything here is read from the predicted session; nothing touches the sim.
public sealed partial class VillageGame
{
    // Blows seen so far, and when the last one landed: chips fly for a moment after each.
    private int _fellBlows, _splitBlows;
    private float _fellBlowAt = -10f, _splitBlowAt = -10f;

    /// <summary>
    /// While you're felling: where the tree's aimed to come down, as long as it is tall, on the ground
    /// (your own session's aim, as you set it). Ready (ember) once the back cut's begun.
    /// </summary>
    private (float3 From, float3 To, float Drawn, bool Ready, bool Winded)? FellLine()
    {
        if (_work.Session is not FellSession { Done: false } s || !_sim.World.IsAlive(s.Tree)
            || !_sim.World.TryGet<Sim.Components.Thing>(s.Tree, out var t) || t.Def.Felling is not { } props)
            return null;
        var from = Sim.Components.Position.CenterOf(t.Cell);
        var d = Dir(s.Aim);
        var to = from + new float3(d.X, d.Y, 0) * props.Height;
        return (from, to, 1f, s.Backing, false);
    }

    private static float2 Dir(float deg) => new(MathF.Cos(deg * MathF.PI / 180f), MathF.Sin(deg * MathF.PI / 180f));

    /// <summary>A way on the map (degrees: 0 east, 90 south) as it points on screen, the camera's way round (as the minimap).</summary>
    private float2 ScreenDir(float deg)
    {
        var rad = deg * MathF.PI / 180f;
        var (cos, sin) = (MathF.Cos(rad), MathF.Sin(rad));
        return Frame() is { } f ? f.East * cos - f.North * sin : new float2(cos, sin);
    }

    /// <summary>
    /// The trunk from above, turned as the camera looks (so "up the panel" is "up the screen"): the bark ring, the notch cut on the
    /// aimed side and the back cut opposite, the hinge between them, the tree's lean and where it's
    /// aimed to fall. Beside it, the axe raised as high as it's held.
    /// </summary>
    private void DrawFell(FellSession s)
    {
        _workAside = true; // the tree and where it'll land stay in sight
        BeginWork(s.Title,
            s.AimSet ? ("F|Click", "hold to raise the axe, let go to strike") : ("A/D", "turn where it should fall"),
            s.Backing ? ("F|Click", "thin the hinge: it goes when it's thin") : ("Space", "notch cut: go round for the back cut"),
            ("Esc", "stop"));
        TextC(Theme.Dim, !s.AimSet
            ? "Aim it clear of roofs and people. It leans a little: the notch has to pull it round."
            : !s.Backing ? "Notch on the side it's to fall, to the chalk mark. Too shallow, it won't steer."
            : "Back cut from the other side. Leave a hinge: cut through it and it goes its own way.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(380, 260);
        ImGui.Dummy(in size);
        var c = new float2(min.X + Gui.S(140), min.Y + Gui.S(130));
        var r = Gui.S(80);

        // The ground round it, and north.
        dl.AddCircleFilled(in c, r * 1.55f, Theme.U8(new float4(Theme.Well.XYZ, 0.6f)), 48);
        var n = c + ScreenDir(270f) * (r * 1.55f + Gui.S(8)) - new float2(Gui.S(4), Gui.S(7));
        dl.AddText(in n, Theme.U8(Theme.Dim), "N"u8);

        // The lean: a faint arrow out from the trunk.
        var lean = ScreenDir(s.Lean);
        var l0 = c + lean * r * 1.05f;
        var l1 = c + lean * r * (1.05f + 0.35f * s.LeanPull);
        dl.AddLine(in l0, in l1, Theme.U8(new float4(Theme.Warn.XYZ, 0.7f)), Gui.S(3));
        var lt = l1 + lean * Gui.S(10) - new float2(Gui.S(14), Gui.S(7));
        dl.AddText(in lt, Theme.U8(new float4(Theme.Warn.XYZ, 0.8f)), "lean"u8);

        var aim = ScreenDir(s.Aim);
        var side = new float2(-aim.Y, aim.X);

        // The trunk: bark, then wood, the rings.
        dl.AddCircleFilled(in c, r, Theme.U8(Theme.Oak), 48);
        dl.AddCircleFilled(in c, r - Gui.S(7), Theme.U8(Theme.Barrel), 48);
        for (var ring = 1; ring <= 4; ring++)
            dl.AddCircle(in c, r * ring / 5.5f, Theme.U8(new float4(Theme.OakLight.XYZ, 0.5f)), 40, Gui.S(1));

        // Along the aim the trunk's width is 1, from the aimed side (where the notch is) to the back.
        float2 Across(float t) => c + aim * (r - 2f * r * t);
        // The notch's chalk mark, and the notch cut so far: a wedge in from the aimed side.
        var mark = Across(FellSession.NotchMark);
        var m0 = mark + side * r * 0.75f;
        var m1 = mark - side * r * 0.75f;
        dl.AddLine(in m0, in m1, Theme.U8(new float4(Theme.Parchment.XYZ, s.Backing ? 0.35f : 0.9f)), Gui.S(2));
        if (s.Notch > 0f)
        {
            var tip = Across(s.Notch);
            var w0 = c + aim * r + side * r * Math.Min(0.9f, s.Notch * 1.6f);
            var w1 = c + aim * r - side * r * Math.Min(0.9f, s.Notch * 1.6f);
            dl.AddTriangleFilled(in w0, in w1, in tip, Theme.U8(Theme.Cellar));
        }
        // The back cut, straight in from the far side; the hinge left between.
        if (s.Back > 0f)
        {
            var from = Across(1f);
            var to = Across(1f - s.Back);
            var b0 = from + side * r * 0.85f;
            var b1 = to - side * r * 0.85f;
            var lo = new float2(MathF.Min(b0.X, b1.X), MathF.Min(b0.Y, b1.Y));
            var hi = new float2(MathF.Max(b0.X, b1.X), MathF.Max(b0.Y, b1.Y));
            dl.AddRectFilled(in lo, in hi, Theme.U8(Theme.Cellar), 0f, ImDrawFlags.None);
        }
        if (s.Backing)
        {
            var hinge = s.Hinge;
            var hingeAt = Across(Math.Clamp(s.Notch, 0f, 1f));
            var label = hinge < 0f ? "cut through!" : hinge <= FellSession.HingeFalls * 1.8f ? "hinge: thin" : "hinge";
            var colour = hinge < 0f ? Theme.Bad : hinge <= FellSession.HingeFalls * 1.8f ? Theme.Warn : Theme.Good;
            var ht = new float2(c.X + r + Gui.S(16), c.Y - Gui.S(30));
            dl.AddText(in ht, Theme.U8(colour), label);
            dl.AddCircle(in hingeAt, Gui.S(5), Theme.U8(colour), 12, Gui.S(2));
        }

        // Where it's aimed to fall: an arrow out from the trunk, over everything.
        var a0 = c + aim * r * 0.2f;
        var a1 = c + aim * r * 1.75f;
        dl.AddLine(in a0, in a1, Theme.U8(Theme.Accent), Gui.S(s.AimSet ? 2 : 4));
        var h0 = a1 - aim * Gui.S(12) + side * Gui.S(8);
        var h1 = a1 - aim * Gui.S(12) - side * Gui.S(8);
        dl.AddTriangleFilled(in h0, in h1, in a1, Theme.U8(Theme.Accent));
        if (!s.AimSet)
        {
            EndWork();
            return; // the axe comes out once it's aimed
        }

        // The axe over the side being cut, raised as high as it's held, off to the side of the arrow.
        var cutSide = s.Backing ? -aim : aim;
        var lift = s.Raised * Gui.S(60);
        var edge = c + cutSide * (r + Gui.S(10) + lift) + side * r * 0.45f;
        var e0 = edge + side * Gui.S(14);
        var e1 = edge - side * Gui.S(14);
        var e2 = e1 + cutSide * Gui.S(16);
        var e3 = e0 + cutSide * Gui.S(16);
        dl.AddQuadFilled(in e0, in e1, in e2, in e3, Theme.U8(Theme.Iron));
        dl.AddLine(in e0, in e1, Theme.U8(Theme.Parchment), Gui.S(2));
        var haft1 = edge + cutSide * Gui.S(70);
        var haft0 = edge + cutSide * Gui.S(14);
        dl.AddLine(in haft0, in haft1, Theme.U8(Theme.Oak), Gui.S(5));

        // Chips off the last blow.
        if (s.Blows != _fellBlows)
            (_fellBlows, _fellBlowAt) = (s.Blows, _time);
        var since = _time - _fellBlowAt;
        if (since < 0.5f)
            for (var k = 0; k < 8; k++)
            {
                var f = since / 0.5f;
                var p = c + cutSide * (r + f * Gui.S(46)) + side * MathF.Sin(k * 1.7f) * Gui.S(40) * f;
                dl.AddCircleFilled(in p, Gui.S(2.5f), Theme.U8(new float4(Theme.OakLight.XYZ, 1f - f)), 6);
            }

        // How deep: the notch against its mark, then the hinge.
        var g = new float2(min.X + Gui.S(300), min.Y + Gui.S(40));
        dl.AddText(in g, Theme.U8(Theme.Dim), s.Backing ? "back cut"u8 : "notch"u8);
        var t0 = new float2(g.X, g.Y + Gui.S(20));
        var t1 = new float2(g.X + Gui.S(14), g.Y + Gui.S(200));
        Gui.Trough(dl, t0, t1);
        var depth = s.Backing ? s.Back : s.Notch;
        var fill = new float2(t0.X, t0.Y + (t1.Y - t0.Y) * Math.Clamp(depth, 0f, 1f));
        dl.AddRectFilled(in t0, new float2(t1.X, fill.Y), Theme.U8(Theme.OakLight), 0f, ImDrawFlags.None);
        var target = t0.Y + (t1.Y - t0.Y) * (s.Backing ? 1f - s.Notch - FellSession.HingeFalls : FellSession.NotchMark);
        dl.AddLine(new float2(t0.X - Gui.S(6), target), new float2(t1.X + Gui.S(6), target), Theme.U8(Theme.Parchment), Gui.S(2));
        // Where this blow would go to, as raised.
        var would = depth + s.Raised * FellSession.DeepestBlow / Math.Max(0.3f, s.Girth);
        var wy = t0.Y + (t1.Y - t0.Y) * Math.Clamp(would, 0f, 1f);
        var past = s.Backing ? 1f - s.Notch - would < 0f : would > FellSession.NotchMark + 0.12f;
        dl.AddLine(new float2(t0.X - Gui.S(4), wy), new float2(t1.X + Gui.S(4), wy), Theme.U8(past ? Theme.Bad : Theme.Accent), Gui.S(2));
        EndWork();
    }

    /// <summary>
    /// The trunk side on, crown to the left, its lengths marked: the cut in hand through it, the saw
    /// across it worked to and fro, the kerf closing on the blade past halfway (and a wedge in it, if
    /// one's been driven), and the logs already off it.
    /// </summary>
    private void DrawBuck(BuckSession s)
    {
        _workAside = true;
        BeginWork(s.Title,
            ("W/S|Mouse", "work the saw: the pull cuts"),
            s.CanKeepGreat ? ("Space", "keep the butt whole: a great log") : ("Space", "drive a wedge in the cut"),
            ("Esc", "stop"));
        TextC(Theme.Dim, s.Bound ? "The saw's bound in the kerf! Drive a wedge in to free it."
            : s.Progress >= BuckSession.PinchFrom && !s.Wedged ? "Past halfway the trunk's weight closes the cut: wedge it before it binds."
            : "Steady pulls. A rushed saw binds sooner.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(440, 220);
        ImGui.Dummy(in size);
        TakesPointer(new float2(min.X, min.Y), new float2(min.X + size.X, min.Y + size.Y));

        // The trunk, its lengths: those cut away to the left, the rest still whole.
        var lengths = Math.Max(1, s.Lengths);
        var t0 = new float2(min.X + Gui.S(20), min.Y + Gui.S(110));
        var t1 = new float2(min.X + size.X - Gui.S(20), min.Y + Gui.S(170));
        var per = (t1.X - t0.X) / lengths;
        for (var k = 0; k < lengths; k++)
        {
            var gap = k < s.CutsDone ? Gui.S(10) * (s.CutsDone - k) : 0f;
            var k0 = new float2(t0.X + k * per - gap, t0.Y);
            var k1 = new float2(t0.X + (k + 1) * per - gap - Gui.S(1), t1.Y);
            var butt = s.GreatButt && k >= lengths - 2;
            dl.AddRectFilled(in k0, in k1, Theme.U8(k < s.CutsDone ? Theme.OakLight : butt ? Theme.Barrel : Theme.Oak), Gui.S(6), ImDrawFlags.None);
        }
        if (s.KeptGreat)
            TextC(Theme.Good, "The butt's kept whole: a great log for the pit saw.");

        if (!s.Done && s.CutsDone < s.CutsNeeded)
        {
            // The cut in hand: at the next length's end, down through the trunk as far as it's gone.
            var x = t0.X + (s.CutsDone + 1) * per;
            var depthY = t0.Y + (t1.Y - t0.Y) * s.Progress;
            var open = Gui.S(3) * (1f - s.Pinch);
            dl.AddRectFilled(new float2(x - open - Gui.S(1), t0.Y), new float2(x + open + Gui.S(1), depthY), Theme.U8(Theme.Cellar), 0f, ImDrawFlags.None);
            if (s.Wedged || s.Wedging > 0f)
            {
                var in_ = s.Wedging > 0f ? 1f - s.Wedging : 1f;
                var w0 = new float2(x - Gui.S(7), t0.Y - Gui.S(16) + in_ * Gui.S(14));
                var w1 = new float2(x + Gui.S(7), t0.Y - Gui.S(16) + in_ * Gui.S(14));
                var w2 = new float2(x, t0.Y + in_ * Gui.S(14));
                dl.AddTriangleFilled(in w0, in w1, in w2, Theme.U8(Theme.OakLight));
            }
            // The saw across the trunk at the cut, slid to and fro with the stroke.
            var shift = (s.Stroke - 0.5f) * Gui.S(160);
            var blade0 = new float2(x - Gui.S(110) + shift, depthY);
            var blade1 = new float2(x + Gui.S(110) + shift, depthY);
            dl.AddLine(in blade0, in blade1, Theme.U8(s.Bound ? Theme.Bad : s.Pinch > 0.6f ? Theme.Warn : Theme.Iron), Gui.S(5));
            var g0 = new float2(blade0.X - Gui.S(4), depthY - Gui.S(22));
            var g1 = new float2(blade0.X + Gui.S(4), depthY + Gui.S(4));
            dl.AddRectFilled(in g0, in g1, Theme.U8(Theme.Oak), Gui.S(2), ImDrawFlags.None);
            var g2 = new float2(blade1.X - Gui.S(4), depthY - Gui.S(22));
            var g3 = new float2(blade1.X + Gui.S(4), depthY + Gui.S(4));
            dl.AddRectFilled(in g2, in g3, Theme.U8(Theme.Oak), Gui.S(2), ImDrawFlags.None);
            // Sawdust on the pull.
            if (s.Cutting)
                for (var k = 0; k < 6; k++)
                {
                    var f = (_time * 2.5f + k / 6f) % 1f;
                    var d = new float2(x + MathF.Sin(k * 2.3f + _time * 3f) * Gui.S(10), t1.Y + f * Gui.S(30));
                    dl.AddCircleFilled(in d, Gui.S(1.5f), Theme.U8(new float4(Theme.Parchment.XYZ, 1f - f)), 6);
                }
            // How closed the kerf is.
            var p0 = new float2(min.X + Gui.S(20), min.Y + Gui.S(196));
            var p1 = new float2(min.X + Gui.S(200), min.Y + Gui.S(204));
            Gui.Trough(dl, p0, p1);
            dl.AddRectFilled(in p0, new float2(p0.X + (p1.X - p0.X) * s.Pinch, p1.Y), Theme.U8(s.Pinch > 0.7f ? Theme.Bad : Theme.Warn), 0f, ImDrawFlags.None);
            var pl = new float2(p1.X + Gui.S(8), p0.Y - Gui.S(5));
            dl.AddText(in pl, Theme.U8(Theme.Dim), s.Wedged ? "wedged"u8 : "kerf closing"u8);
        }
        DrawOtherEnd(s);
        EndWork();
    }

    /// <summary>On a great trunk: how the other end's pulling, or that nobody's there.</summary>
    private void DrawOtherEnd(BuckSession s)
    {
        if (!s.GreatButt || s.KeptGreat)
            return;
        if (!_sim.World.IsAlive(s.Partner) || !_sim.World.TryGet<Sim.Components.Creature>(s.Partner, out var other))
        {
            TextC(Theme.Warn, "A great trunk: alone the saw barely moves. Call Help! from the quick-say wheel for someone on the other end.");
            return;
        }
        var recent = s.LastPullStep >= 0 && s.StepsDone - s.LastPullStep < CrosscutSession.WordSteps;
        if (recent && s.LastPull < 0f)
            TextC(Theme.Bad, $"{other.Name} pulled against you: the blade jerks in the cut!");
        else if (recent)
            TextC(s.LastPull >= 0.6f ? Theme.Good : Theme.Text, $"{other.Name} pulled back with you{(s.LastPull < 0.4f ? ", short" : "")}.");
        else
            TextC(Theme.Dim, $"{other.Name} has the other end: each pull you start, they pull back.");
    }

    /// <summary>The other end of a crosscut: when to pull, as the pitman's panel.</summary>
    private void DrawCrosscut(CrosscutSession s)
    {
        BeginWork(s.Title, ("F|Hold the button", "pull back when they start a stroke; let go between"), ("Esc", "let go of the saw"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(360, 120);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        var g0 = new float2(min.X + Gui.S(18), max.Y - Gui.S(24));
        var g1 = new float2(max.X - Gui.S(18), max.Y - Gui.S(14));
        Gui.Trough(dl, g0, g1);
        var x = g1.X - (g1.X - g0.X) * Math.Clamp(s.Drawn, 0f, 1f);
        dl.AddRectFilled(new float2(x - Gui.S(3), g0.Y - Gui.S(4)), new float2(x + Gui.S(3), g1.Y + Gui.S(4)), Theme.U8(s.Drawn >= 1f ? Theme.Good : Theme.Text), Gui.S(1), ImDrawFlags.None);
        var centre = new float2((min.X + max.X) * 0.5f, min.Y + size.Y * 0.35f);
        var (word, colour) = s.CallOpen
            ? ("Pull!", 1f - s.CallAge / (float)Math.Max(1, s.Window) > 0.66f ? Theme.Good : Theme.Warn)
            : s.LastStep >= 0 && s.StepsDone - s.LastStep < CrosscutSession.WordSteps
                ? s.LastWas switch
                {
                    PitResult.WithIt when s.LastGrade >= 0.7f => ("With the stroke!", Theme.Good),
                    PitResult.WithIt => ("Short, or late", Theme.Warn),
                    PitResult.Bound => ("Against it: the blade jerks!", Theme.Bad),
                    _ => ("Missed the stroke", Theme.Warn),
                }
                : ($"Wait for {s.LeadName} to pull", Theme.Dim);
        var ts = ImGui.CalcTextSize(word);
        dl.AddText(centre - ts * 0.5f, Theme.U8(colour), word);
        TextC(Theme.Dim, $"With the stroke {s.Pulls}, against it {s.Binds}, missed {s.Missed}.");
        EndWork();
    }

    /// <summary>
    /// A log stood on the block, end grain up: the checks it'll split along, the axe's edge over it
    /// where it's aimed, raised as high as it's held, and the splits done.
    /// </summary>
    private void DrawSplit(SplitSession s)
    {
        BeginWork(s.Title,
            ("A/D|Mouse", "put the edge over a check"),
            ("F|Click", "hold to raise, let go to strike"),
            s.Stuck > 0 ? ("Space", "rock the stuck axe free") : ("Esc", "stop"));
        TextC(s.Stuck > 0 ? Theme.Warn : Theme.Dim, s.Stuck > 0 ? "The axe is stuck in the wood. Rock it free."
            : s.LastBlow == SplitBlow.Bounced ? "It bounced off: strike on the check, and harder."
            : "Strike along the crack in the end grain, hard. Off the crack, the axe sticks.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(360, 240);
        ImGui.Dummy(in size);
        var c = new float2(min.X + Gui.S(160), min.Y + Gui.S(140));
        var r = Gui.S(85);
        TakesPointer(c - new float2(r, r), c + new float2(r, r));
        dl.AddCircleFilled(in c, r, Theme.U8(Theme.Oak), 48);
        dl.AddCircleFilled(in c, r - Gui.S(7), Theme.U8(Theme.Barrel), 48);
        for (var ring = 1; ring <= 4; ring++)
            dl.AddCircle(in c, r * ring / 5.5f, Theme.U8(new float4(Theme.OakLight.XYZ, 0.5f)), 40, Gui.S(1));
        // The splits already made: pale lines across the end; the check showing now, dark.
        for (var k = 0; k < s.SplitsDone; k++)
        {
            var a = k * MathF.PI / Math.Max(1, s.Splits);
            var d = new float2(MathF.Cos(a), MathF.Sin(a)) * (r - Gui.S(4));
            dl.AddLine(c - d, c + d, Theme.U8(Theme.OakLight), Gui.S(2));
        }
        var cx = c.X + s.Check * r;
        var half = MathF.Sqrt(Math.Max(0f, 1f - s.Check * s.Check)) * (r - Gui.S(6));
        var band = s.CheckWidthNow * r;
        dl.AddRectFilled(new float2(cx - band, c.Y - half), new float2(cx + band, c.Y + half), Theme.U8(new float4(Theme.Parchment.XYZ, 0.15f)), 0f, ImDrawFlags.None);
        dl.AddLine(new float2(cx, c.Y - half), new float2(cx + Gui.S(3), c.Y + half), Theme.U8(Theme.Cellar), Gui.S(3));
        // The axe edge-on over the aim, up as high as it's raised; stuck, buried in the end.
        var ax = c.X + s.Aim * r;
        var lift = s.Stuck > 0 ? -Gui.S(8) : s.Raised * Gui.S(60);
        var top = c.Y - r - Gui.S(10) - lift;
        var hard = s.Raised >= s.Need;
        dl.AddRectFilled(new float2(ax - Gui.S(3), top - Gui.S(26)), new float2(ax + Gui.S(3), top), Theme.U8(s.Stuck > 0 ? Theme.Bad : hard ? Theme.Good : Theme.Iron), 0f, ImDrawFlags.None);
        dl.AddLine(new float2(ax, top - Gui.S(26)), new float2(ax, top - Gui.S(90)), Theme.U8(Theme.Oak), Gui.S(5));
        if (s.Blows != _splitBlows)
            (_splitBlows, _splitBlowAt) = (s.Blows, _time);
        var since = _time - _splitBlowAt;
        if (since < 0.4f && s.LastBlow == SplitBlow.Split)
            for (var k = 0; k < 6; k++)
            {
                var f = since / 0.4f;
                var p = new float2(c.X + (k - 2.5f) * Gui.S(14) * (1f + f), c.Y - r * 0.2f + f * f * Gui.S(40));
                dl.AddCircleFilled(in p, Gui.S(3), Theme.U8(new float4(Theme.OakLight.XYZ, 1f - f)), 6);
            }
        // How hard it needs, against how high it's raised.
        var m0 = new float2(min.X + Gui.S(300), min.Y + Gui.S(50));
        var m1 = new float2(min.X + Gui.S(314), min.Y + Gui.S(210));
        Gui.Trough(dl, m0, m1);
        var raisedY = m1.Y - (m1.Y - m0.Y) * s.Raised;
        dl.AddRectFilled(new float2(m0.X, raisedY), in m1, Theme.U8(hard ? Theme.Good : Theme.OakLight), 0f, ImDrawFlags.None);
        var needY = m1.Y - (m1.Y - m0.Y) * s.Need;
        dl.AddLine(new float2(m0.X - Gui.S(6), needY), new float2(m1.X + Gui.S(6), needY), Theme.U8(Theme.Parchment), Gui.S(2));
        EndWork();
    }
}
