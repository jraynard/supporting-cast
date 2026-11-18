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

    // How high the felling axe was raised the frame before a blow: it's swung in from there.
    private float _fellRaisedLast;

    /// <summary>
    /// The axe seen from above, swung about the hands as a woodsman's is: at rest its bit lies in the
    /// cut at <paramref name="cutPoint"/>, the haft running back along <paramref name="side"/> to the
    /// hands; raised by <paramref name="swing"/> (0..1) it's swung back out away from the trunk
    /// (<paramref name="outward"/>), round its arc.
    /// </summary>
    private void DrawAxeSwung(ImDrawListPtr dl, float2 cutPoint, float2 outward, float2 side, float swing)
    {
        if (!Gui.TrySprite("ui/work/axe", out var axe))
            return;
        var px = MathF.Max(1f, MathF.Round(3f * Gui.Scale));
        var len = new float2(axe.PixelSize.X, axe.PixelSize.Y) * px;
        // The bit's middle sits 27 of the sprite's 34 pixels along: the hands are that far back up the haft.
        const float bitAlong = 27f / 34f;
        var rest = -side;                                        // from the hands toward the head, at rest
        var back = cutPoint - rest * (len.X * bitAlong);         // the haft's end, in the hands: it stays put
        var angle = Math.Clamp(swing, 0f, 1f) * 1.25f;            // swung back out, radians
        var dir = rest * MathF.Cos(angle) + outward * MathF.Sin(angle);
        // The sprite's top (the axe's back) faces away from the trunk at rest, its bit toward it; the
        // same side all the way round the swing.
        var flip = -rest.Y * outward.X + rest.X * outward.Y >= 0f ? 1f : -1f;
        var away = new float2(-dir.Y, dir.X) * flip;
        var head = back + dir * len.X;
        var h = away * (len.Y * 0.5f);
        Gui.ImageQuad(dl, axe, back + h, head + h, head - h, back - h, Gui.White);
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
        if (!s.AimSet)
            BeginWork(s.Title, ("A/D", "turn where it should fall"), ("F|Click", "hold, let go: the first blow sets the aim and starts the notch"), ("Esc", "stop"));
        else if (s.Backing)
            BeginWork(s.Title, ("F|Click", "hold, let go: thin the hinge till it goes"), ("Esc", "stop"));
        else
            BeginWork(s.Title, ("F|Click", "hold to raise the axe, let go to strike"), ("Space", "notch cut: go round for the back cut"), ("Esc", "stop"));
        TextC(Theme.Dim, !s.AimSet
            ? "It's aimed the way it leans (the yellow arrow), the easy way. Turn it if that's onto a roof or someone; against the lean, the notch has to pull it round."
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
        var l1 = c + lean * r * (1.25f + 0.3f * s.LeanPull);
        // Broad, under the aim's thin arrow, so both show when they're the same way.
        var leanColour = Theme.U8(new float4(Theme.Warn.XYZ, 0.65f));
        dl.AddLine(in l0, in l1, leanColour, Gui.S(9));
        var leanSide = new float2(-lean.Y, lean.X);
        dl.AddTriangleFilled(l1 + lean * Gui.S(14), l1 + leanSide * Gui.S(11), l1 - leanSide * Gui.S(11), leanColour);
        var lt = l1 + lean * Gui.S(22) - new float2(Gui.S(18), Gui.S(7));
        dl.AddText(in lt, leanColour, "leans"u8);

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
        // The notch: a V into the aimed face, its mouth on the bark either side, its point as deep as it's cut.
        if (s.Notch > 0f)
        {
            var tip = Across(Math.Min(s.Notch, 1f));
            var open = Math.Clamp(s.Notch * 1.5f, 0.12f, 1.1f); // radians either side of the aim, round the bark
            float2 Round(float a) => c + new float2(aim.X * MathF.Cos(a) - aim.Y * MathF.Sin(a), aim.X * MathF.Sin(a) + aim.Y * MathF.Cos(a)) * (r - Gui.S(1));
            const int bits = 8;
            for (var k = 0; k < bits; k++)
            {
                var p0 = Round(-open + 2f * open * k / bits);
                var p1 = Round(-open + 2f * open * (k + 1) / bits);
                dl.AddTriangleFilled(in tip, in p0, in p1, Theme.U8(Theme.Cellar));
            }
        }
        // The back cut: a slim cut in from the far side toward the notch, never past the notch's point
        // (the hinge is the wood left between them).
        if (s.Back > 0f)
        {
            var deepest = Math.Max(1f - s.Back, Math.Min(s.Notch, 1f) + 0.015f);
            var point = Across(deepest);
            var mouth = Across(1f) + aim * Gui.S(1);
            var wide = Gui.S(7);
            dl.AddTriangleFilled(mouth + side * wide, mouth - side * wide, in point, Theme.U8(Theme.Cellar));
        }
        if (s.Backing)
        {
            var hinge = s.Hinge;
            var hingeAt = Across(Math.Clamp(s.Notch, 0f, 1f));
            var label = hinge < 0f ? "cut through!" : hinge <= FellSession.HingeFalls * 1.8f ? "hinge: thin" : "hinge";
            var colour = hinge < 0f ? Theme.Bad : hinge <= FellSession.HingeFalls * 1.8f ? Theme.Warn : Theme.Good;
            var ht = new float2(c.X + r + Gui.S(16), c.Y - r);
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

        // The axe, swung from the feller's hands beside the trunk: raised, it's swung back out round
        // its arc; let go, it whips round into the cut (a blink), then rests in it.
        var cutSide = s.Backing ? -aim : aim;
        if (s.Blows != _fellBlows)
            (_fellBlows, _fellBlowAt) = (s.Blows, _time);
        var since = _time - _fellBlowAt;
        if (s.Raised > 0f)
            _fellRaisedLast = s.Raised;
        var swing = s.Raised > 0f ? s.Raised : since < 0.07f ? _fellRaisedLast * (1f - since / 0.07f) : 0f;
        var bite = Math.Min(s.Backing ? s.Back : s.Notch, 0.35f);
        var cutPoint = c + cutSide * r * (1f - bite);
        DrawAxeSwung(dl, cutPoint, cutSide, side, swing);
        if (since < 0.5f)
            for (var k = 0; k < 8; k++)
            {
                var f = since / 0.5f;
                var p = cutPoint + cutSide * f * Gui.S(46) + side * MathF.Sin(k * 1.7f) * Gui.S(40) * f;
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
        var size = Gui.S(620, 220);
        ImGui.Dummy(in size);
        TakesPointer(new float2(min.X, min.Y), new float2(min.X + size.X, min.Y + size.Y));

        // The trunk, its lengths: those cut away to the left, the rest still whole.
        var lengths = Math.Max(1, s.Lengths);
        var t0 = new float2(min.X + Gui.S(20), min.Y + Gui.S(110));
        var t1 = new float2(min.X + Gui.S(400), min.Y + Gui.S(170));
        var per = (t1.X - t0.X) / lengths;
        for (var k = 0; k < lengths; k++)
        {
            var gap = k < s.CutsDone ? Gui.S(10) * (s.CutsDone - k) : 0f;
            var k0 = new float2(t0.X + k * per - gap, t0.Y);
            var k1 = new float2(t0.X + (k + 1) * per - gap - Gui.S(1), t1.Y);
            var butt = s.GreatButt && k >= lengths - 2;
            // Each length its own shade of bark, so the logs it'll make read apart; one cut off is paler, sawn wood at its end.
            var bark = butt ? Theme.Barrel : k % 2 == 0 ? Theme.Oak : new float4(Theme.Oak.XYZ * 1.35f, 1f);
            dl.AddRectFilled(in k0, in k1, Theme.U8(k < s.CutsDone ? Theme.OakLight : bark), Gui.S(6), ImDrawFlags.None);
            if (k < s.CutsDone)
                dl.AddRectFilled(new float2(k1.X - Gui.S(5), k0.Y + Gui.S(2)), new float2(k1.X, k1.Y - Gui.S(2)), Theme.U8(Theme.Parchment, 0.8f), Gui.S(2), ImDrawFlags.None);
        }
        if (s.KeptGreat)
            TextC(Theme.Good, "The butt's kept whole: a great log for the pit saw.");

        if (!s.Done && s.CutsDone < s.CutsNeeded)
        {
            // The cut in hand: at the next length's end, down through the trunk as far as it's gone.
            var x = t0.X + (s.CutsDone + 1) * per;
            var depthY = t0.Y + (t1.Y - t0.Y) * s.Progress;
            if (s.Wedged || s.Wedging > 0f)
            {
                var in_ = s.Wedging > 0f ? 1f - s.Wedging : 1f;
                var w0 = new float2(x - Gui.S(7), t0.Y - Gui.S(16) + in_ * Gui.S(14));
                var w1 = new float2(x + Gui.S(7), t0.Y - Gui.S(16) + in_ * Gui.S(14));
                var w2 = new float2(x, t0.Y + in_ * Gui.S(14));
                dl.AddTriangleFilled(in w0, in w1, in w2, Theme.U8(Theme.OakLight));
            }
            // Side on, the blade's hidden in the trunk: only the saw's handle shows at the cut, standing
            // up off the blade, and it sinks as the cut goes down.
            var tone = Theme.U8(s.Bound ? Theme.Bad : s.Pinch > 0.6f ? Theme.Warn : Theme.OakLight);
            dl.AddRectFilled(new float2(x - Gui.S(6), depthY - Gui.S(34)), new float2(x + Gui.S(6), depthY - Gui.S(4)), tone, Gui.S(2), ImDrawFlags.None);
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
        DrawBuckAcross(dl, s, new float2(min.X + Gui.S(420), min.Y), new float2(min.X + size.X, min.Y + size.Y));
        DrawOtherEnd(s);
        EndWork();
    }

    /// <summary>
    /// The same cut looking along the trunk, as the hew's side view: the round end, the saw across it
    /// going back and forth with the strokes at the foot of the cut, the cut's face dark above it, the
    /// wedge at the top.
    /// </summary>
    private void DrawBuckAcross(ImDrawListPtr dl, BuckSession s, float2 min, float2 max)
    {
        var c = new float2((min.X + max.X) * 0.5f, min.Y + Gui.S(120));
        var r = Gui.S(60);
        dl.AddCircleFilled(in c, r, Theme.U8(Theme.Oak), 40);
        dl.AddCircleFilled(in c, r - Gui.S(6), Theme.U8(Theme.Barrel), 40);
        for (var ring = 1; ring <= 3; ring++)
            dl.AddCircle(in c, r * ring / 4.5f, Theme.U8(new float4(Theme.OakLight.XYZ, 0.5f)), 32, Gui.S(1));
        if (!s.Done && s.CutsDone < s.CutsNeeded && !s.KeptGreat)
        {
            var depthY = c.Y - r + 2f * r * Math.Clamp(s.Progress, 0f, 1f);
            // Where the blade's inside the round, it's hidden: only what's out either side shows.
            var dyc = (depthY - c.Y) / r;
            var chord = MathF.Sqrt(Math.Max(0f, 1f - dyc * dyc)) * r;
            if (s.Wedged || s.Wedging > 0f)
            {
                var sunk = s.Wedging > 0f ? 1f - s.Wedging : 1f;
                var wy = c.Y - r - Gui.S(16) + sunk * Gui.S(14);
                dl.AddTriangleFilled(new float2(c.X - Gui.S(8), wy), new float2(c.X + Gui.S(8), wy), new float2(c.X, wy + Gui.S(17)), Theme.U8(Theme.OakLight));
            }
            // The saw, slid across with the stroke: pulled toward you (right), pushed back (left).
            var shift = (s.Stroke - 0.5f) * Gui.S(60);
            var reach = r + Gui.S(16);
            var b0 = new float2(c.X - reach + shift, depthY);
            var b1 = new float2(c.X + reach + shift, depthY);
            var colour = Theme.U8(s.Bound ? Theme.Bad : s.Pinch > 0.6f ? Theme.Warn : Theme.Iron);
            var inLeft = c.X - chord;
            var inRight = c.X + chord;
            void Blade(float from, float to)
            {
                if (to - from < 1f)
                    return;
                dl.AddLine(new float2(from, depthY), new float2(to, depthY), colour, Gui.S(4));
                for (var bx = from + Gui.S(3); bx < to - Gui.S(3); bx += Gui.S(7))
                    dl.AddTriangleFilled(new float2(bx - Gui.S(3), depthY + Gui.S(1)), new float2(bx + Gui.S(3), depthY + Gui.S(1)), new float2(bx, depthY + Gui.S(5)), colour);
            }
            Blade(b0.X, MathF.Min(b1.X, inLeft));
            Blade(MathF.Max(b0.X, inRight), b1.X);
            foreach (var end in new[] { b0, b1 })
                dl.AddRectFilled(end - new float2(Gui.S(4), Gui.S(16)), end + new float2(Gui.S(4), Gui.S(4)), Theme.U8(Theme.OakLight), Gui.S(2), ImDrawFlags.None);
            // Sawdust spat out of the cut either side on the pull, falling away.
            if (s.Cutting)
                for (var k = 0; k < 10; k++)
                {
                    var f = (_time * 3f + k / 10f) % 1f;
                    var side = k % 2 == 0 ? -1f : 1f;
                    var from = new float2(side < 0 ? inLeft : inRight, depthY);
                    var d = from + new float2(side * (Gui.S(6) + f * Gui.S(22)) * (0.6f + 0.4f * MathF.Sin(k * 1.9f)), -Gui.S(4) * f + Gui.S(36) * f * f);
                    dl.AddCircleFilled(in d, Gui.S(1.6f), Theme.U8(new float4(Theme.Parchment.XYZ, 1f - f)), 6);
                }
        }
        dl.AddText(new float2(min.X + Gui.S(4), min.Y + Gui.S(4)), Theme.U8(Theme.Dim), "along the trunk"u8);
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

// The claw hammer at a wagon's sprung board (NailSession).
public sealed partial class VillageGame
{
    private int _nailBlows;
    private float _nailBlowAt = -10f;

    /// <summary>
    /// The plank across the board, side on: the nails driven so far flush in it, the one in hand
    /// standing proud as far as it's still out (bent over, if it bent), and the hammer above it, its
    /// head where it'll land (A/D keep it over the nail) and raised as high as it's held.
    /// </summary>
    private void DrawNail(NailSession s)
    {
        BeginWork(s.Title,
            ("F|Click", s.Set ? "hold to raise, let go to drive it" : "a light tap to set the nail"),
            ("A/D", "keep the hammer over the nail"),
            s.Bent ? ("Space", "draw the bent nail with the claw") : ("Esc", "stop"));
        var (tone, say) = s.Bent ? (Theme.Bad, Controls.Keys($"Bent over! Draw it with the claw (~Space~, {s.ClawLeft} more)."))
            : s.Settling > 0 ? (s.LastBlow == NailBlow.Bruised ? Theme.Warn : Theme.Good, s.LastBlow == NailBlow.Bruised ? "Flush, but you've bruised the board." : "Flush.")
            : !s.Set ? (Theme.Text, "Tap it first to set it: hit it hard now and it bends.")
            : s.LastBlow == NailBlow.Missed ? (Theme.Warn, "Missed the head: you've dented the plank.")
            : (Theme.Text, "Square on the head, and drive it in. Ease off as it nears flush.");
        TextC(tone, say);
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(380, 230);
        ImGui.Dummy(in size);
        // The board, and the plank across it.
        var top = min.Y + Gui.S(170);
        var board0 = new float2(min.X + Gui.S(10), top + Gui.S(18));
        var board1 = new float2(min.X + size.X - Gui.S(10), top + Gui.S(50));
        dl.AddRectFilled(in board0, in board1, Theme.U8(Theme.Barrel), Gui.S(2), ImDrawFlags.None);
        var plank0 = new float2(min.X + Gui.S(30), top);
        var plank1 = new float2(min.X + size.X - Gui.S(30), top + Gui.S(18));
        dl.AddRectFilled(in plank0, in plank1, Theme.U8(Theme.OakLight), Gui.S(2), ImDrawFlags.None);
        float NailX(int k) => plank0.X + (plank1.X - plank0.X) * (k + 1f) / (s.Nails + 1f);
        var length = Gui.S(46);
        for (var k = 0; k < s.Nails; k++)
        {
            var x = NailX(k);
            if (k < s.Nail || (k == s.Nail && s.Settling > 0))
            {
                // Driven: just its head on the plank.
                dl.AddRectFilled(new float2(x - Gui.S(5), top - Gui.S(2)), new float2(x + Gui.S(5), top + Gui.S(1)), Theme.U8(Theme.Iron), 0f, ImDrawFlags.None);
                continue;
            }
            if (k > s.Nail)
            {
                // To come: a pencil mark where it goes.
                dl.AddLine(new float2(x, top + Gui.S(2)), new float2(x, top + Gui.S(10)), Theme.U8(new float4(Theme.Parchment.XYZ, 0.5f)), Gui.S(1));
                continue;
            }
            // The one in hand: as much of it as is still out, upright, or bent over.
            var proud = length * (1f - s.Depth);
            var foot = new float2(x, top);
            var head = s.Bent ? new float2(x + proud * 0.75f, top - proud * 0.4f) : new float2(x, top - proud);
            if (s.Bent)
                dl.AddLine(new float2(x, top - proud * 0.35f), in foot, Theme.U8(Theme.Iron), Gui.S(3));
            dl.AddLine(s.Bent ? new float2(x, top - proud * 0.35f) : foot, in head, Theme.U8(Theme.Iron), Gui.S(3));
            dl.AddRectFilled(head - new float2(Gui.S(6), Gui.S(2)), head + new float2(Gui.S(6), Gui.S(1)), Theme.U8(Theme.Iron), 0f, ImDrawFlags.None);
            if (!s.Bent && s.Settling == 0)
            {
                // Where the hammer will land: its face over the head, shifted by how far off it's wandered.
                var travel = Gui.S(70);
                var hx = x + s.Offset * travel;
                var square = MathF.Abs(s.Offset) <= s.OnHeadWidth;
                var band = s.OnHeadWidth * travel;
                dl.AddRectFilled(new float2(x - band, head.Y - Gui.S(4)), new float2(x + band, head.Y - Gui.S(1)), Theme.U8(new float4(Theme.Good.XYZ, 0.35f)), 0f, ImDrawFlags.None);
                var lift = s.Raised * Gui.S(90);
                var face = head.Y - Gui.S(6) - lift;
                dl.AddRectFilled(new float2(hx - Gui.S(9), face - Gui.S(22)), new float2(hx + Gui.S(9), face), Theme.U8(square ? Theme.Iron : Theme.Warn), Gui.S(2), ImDrawFlags.None);
                // The claw behind, the haft off to the right.
                dl.AddTriangleFilled(new float2(hx - Gui.S(9), face - Gui.S(20)), new float2(hx - Gui.S(22), face - Gui.S(30)), new float2(hx - Gui.S(9), face - Gui.S(12)), Theme.U8(Theme.Iron));
                dl.AddLine(new float2(hx + Gui.S(9), face - Gui.S(11)), new float2(hx + Gui.S(80), face - Gui.S(40)), Theme.U8(Theme.Oak), Gui.S(5));
                if (!square)
                {
                    var way = -MathF.Sign(s.Offset);
                    var ay = face - Gui.S(40);
                    var a0 = new float2(hx, ay);
                    var a1 = new float2(hx + way * Gui.S(24), ay);
                    dl.AddLine(in a0, in a1, Theme.U8(Theme.Accent), Gui.S(3));
                    dl.AddText(new float2(a1.X + (way > 0 ? Gui.S(4) : -Gui.S(14)), ay - Gui.S(8)), Theme.U8(Theme.Accent), way < 0 ? "A" : "D");
                }
                if (!s.Set)
                {
                    // The tap's limit on the raise: past this line, it bends.
                    var setY = head.Y - Gui.S(6) - NailSession.SetMax * Gui.S(90);
                    dl.AddLine(new float2(hx + Gui.S(14), setY), new float2(hx + Gui.S(34), setY), Theme.U8(Theme.Warn), Gui.S(2));
                }
            }
        }
        if (s.Blows != _nailBlows)
            (_nailBlows, _nailBlowAt) = (s.Blows, _time);
        var label = new float2(min.X + Gui.S(4), min.Y + Gui.S(4));
        dl.AddText(in label, Theme.U8(Theme.Dim), $"Nail {Math.Min(s.Nail + 1, s.Nails)} of {s.Nails}" + (s.BentNails > 0 ? $", {s.BentNails} bent" : ""));
        EndWork();
    }
}
