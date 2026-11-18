using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Carpentry;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The carpenter's: sawing planks at the sawhorse and fletching a quiver at the bench. The fletching
// shows a shaft's warp as the carpenter's eye reads it (FletchSession.SeenWarp), never the truth.
public sealed partial class VillageGame
{
    // The joinery's blows seen so far, and when the last landed: shavings fly for a moment after each.
    private int _joinBlows;
    private float _joinBlowAt = -10f;

    /// <summary>
    /// Joinery (<see cref="JoinSession"/>): the piece in section, the mortise cut down into it toward
    /// the chalk mark (the snug band just short of it), a dashed line where the raised chisel's blow
    /// would take it; then the tenon (or the peg, the hub) sinking in as it's driven. Beside it the
    /// mallet's gauge: how hard the blow raised now would be, against the mark of what the joint will
    /// take as tight as it is now.
    /// </summary>
    private void DrawJoin(JoinSession s)
    {
        if (s.Cutting)
            BeginWork(s.Title, ("F|Click", "hold to raise the mallet, let go to strike"), ("Space", "deep enough: drive it home"), ("Esc", "stop"));
        else
            BeginWork(s.Title, ("F|Click", "hold to raise the mallet, let go to strike"), ("Esc", "stop"));
        var (tone, say) = s.Settling > 0 && s.JustSplit ? (Theme.Bad, "It splits! Forced home, cracked.")
            : s.Settling > 0 ? (Theme.Good, "Home, and tight.")
            : s.Cutting && s.Depth > 1f ? (Theme.Bad, "Past the mark: this joint will be loose.")
            : s.Cutting && s.Depth >= 1f - s.SnugWidth ? (Theme.Good, Controls.Keys("At the mark. Deep enough (~Space~)."))
            : s.Cutting && s.Work == JoinWork.Haft ? (Theme.Text, "Cut the split back to sound wood, down to the chalk mark. Past it, the peg sits loose.")
            : s.Cutting ? (Theme.Text, "Chop the mortise down to the chalk mark. Past it, the joint's loose.")
            : s.Raised > JoinSession.SafeAt(s.Seat) ? (Theme.Warn, "Too hard for how tight it is: it'll split.")
            : (Theme.Text, "Drive it home. Lighter blows as it tightens.");
        TextC(tone, say);
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(360, 300);
        ImGui.Dummy(in size);
        // The piece in section: a block, the mortise down into it from the top.
        var x0 = min.X + Gui.S(40);
        var x1 = min.X + Gui.S(240);
        var top = min.Y + Gui.S(150);
        var bottom = min.Y + Gui.S(290);
        var unit = Gui.S(100); // a depth of 1 (the mark)
        var b0 = new float2(x0, top);
        var b1 = new float2(x1, bottom);
        dl.AddRectFilled(in b0, in b1, Theme.U8(Theme.Barrel), Gui.S(3));
        dl.AddRect(in b0, in b1, Theme.U8(Theme.Oak), Gui.S(3), ImDrawFlags.None, Gui.S(2));
        var mx = (x0 + x1) * 0.5f;
        var half = Gui.S(22);
        // The snug band, and the chalk mark.
        var snug0 = new float2(mx - half - Gui.S(14), top + (1f - s.SnugWidth) * unit);
        var snug1 = new float2(mx + half + Gui.S(14), top + unit);
        if (s.Cuts)
            dl.AddRectFilled(in snug0, in snug1, Theme.U8(new float4(Theme.Good.XYZ, 0.25f)));
        var m0 = new float2(mx - half - Gui.S(18), top + unit);
        var m1 = new float2(mx + half + Gui.S(18), top + unit);
        if (s.Cuts)
            dl.AddLine(in m0, in m1, Theme.U8(Theme.Parchment), Gui.S(2));
        // The mortise as deep as it's cut (a peg's hole is bored already, to the mark).
        var depth = s.Cuts ? Math.Min(s.Depth, 1.35f) : 1f;
        var h0 = new float2(mx - half, top);
        var h1 = new float2(mx + half, top + depth * unit);
        dl.AddRectFilled(in h0, in h1, Theme.U8(Theme.Cellar));
        if (s.Cutting)
        {
            // Where the blow raised now would cut to.
            var to = top + Math.Min(s.Depth + JoinSession.CutDepth(s.Raised), 1.35f) * unit;
            var past = s.Depth + JoinSession.CutDepth(s.Raised) > 1f;
            for (var x = mx - half; x < mx + half; x += Gui.S(8))
            {
                var d0 = new float2(x, to);
                var d1 = new float2(x + Gui.S(4), to);
                dl.AddLine(in d0, in d1, Theme.U8(past ? Theme.Bad : Theme.Accent), Gui.S(2));
            }
            // The chisel upright in the mortise, the mallet over it raised as it's held.
            // (to the hole as it's drawn: a mortise cut far past the mark still shows the chisel at its foot)
            var c0 = new float2(mx - Gui.S(5), top + depth * unit - Gui.S(60));
            var c1 = new float2(mx + Gui.S(5), top + depth * unit);
            dl.AddRectFilled(in c0, in c1, Theme.U8(Theme.Iron));
            var lift = s.Raised * Gui.S(55);
            var k0 = new float2(mx - Gui.S(24), c0.Y - Gui.S(18) - lift);
            var k1 = new float2(mx + Gui.S(24), c0.Y - lift);
            dl.AddRectFilled(in k0, in k1, Theme.U8(Theme.OakLight), Gui.S(3));
        }
        else
        {
            // The tenon above the mortise, driven down into it as far as it's gone home. It's cut to the
            // mark's length, so home its shoulder sits on the face: a mortise short of the mark leaves it
            // standing proud, one cut past the mark leaves a gap under its end (loose).
            var tenon = unit;
            var into = s.Seat * Math.Min(depth, 1f) * unit;
            var t0 = new float2(mx - half + Gui.S(3), top - tenon + into);
            var t1 = new float2(mx + half - Gui.S(3), top - tenon + into + tenon);
            dl.AddRectFilled(in t0, in t1, Theme.U8(s.Settling > 0 && s.JustSplit ? Theme.Bad : Theme.OakLight));
            var shoulder0 = new float2(mx - half - Gui.S(30), t0.Y - Gui.S(14));
            var shoulder1 = new float2(mx + half + Gui.S(30), t0.Y);
            dl.AddRectFilled(in shoulder0, in shoulder1, Theme.U8(Theme.Oak), Gui.S(2));
            var lift = s.Raised * Gui.S(40);
            var k0 = new float2(mx - Gui.S(26), shoulder0.Y - Gui.S(20) - lift);
            var k1 = new float2(mx + Gui.S(26), shoulder0.Y - Gui.S(2) - lift);
            dl.AddRectFilled(in k0, in k1, Theme.U8(Theme.OakLight), Gui.S(3));
            // The mallet's gauge: the blow as raised, against what the joint will take now.
            var g0 = new float2(min.X + Gui.S(290), min.Y + Gui.S(60));
            var g1 = new float2(min.X + Gui.S(310), min.Y + Gui.S(290));
            var gh = g1.Y - g0.Y;
            dl.AddRectFilled(in g0, in g1, Theme.U8(Theme.Inset), Gui.S(2));
            var fill = new float2(g0.X, g1.Y - s.Raised * gh);
            dl.AddRectFilled(in fill, in g1, Theme.U8(s.Raised > JoinSession.SafeAt(s.Seat) ? Theme.Bad : Theme.Good), Gui.S(2));
            var safe0 = new float2(g0.X - Gui.S(6), g1.Y - JoinSession.SafeAt(s.Seat) * gh);
            var safe1 = new float2(g1.X + Gui.S(6), safe0.Y);
            dl.AddLine(in safe0, in safe1, Theme.U8(Theme.Warn), Gui.S(2));
        }
        // Shavings off the last blow.
        if (s.Blows != _joinBlows)
            (_joinBlows, _joinBlowAt) = (s.Blows, _time);
        var since = _time - _joinBlowAt;
        if (since < 0.4f)
            for (var k = 0; k < 6; k++)
            {
                var f = since / 0.4f;
                var p = new float2(mx + MathF.Cos(k * 1.9f) * f * Gui.S(60), top - f * Gui.S(25) + f * f * Gui.S(50));
                dl.AddCircleFilled(in p, Gui.S(2.5f), Theme.U8(new float4(Theme.OakLight.XYZ, 1f - f)), 6);
            }
        var label = new float2(min.X + Gui.S(4), min.Y + Gui.S(4));
        dl.AddText(in label, Theme.U8(Theme.Dim), $"{(s.Work is JoinWork.Board or JoinWork.Haft ? "Peg" : s.Work == JoinWork.Axle ? "End" : "Joint")} {Math.Min(s.Joint + 1, s.Joints)} of {s.Joints}"
            + (s.Splits > 0 ? $", {s.Splits} split" : ""));
        DrawLifterLine(s);
        EndWork();
    }

    /// <summary>Under a new axle: who's taking the wagon's weight, and how level, or that nobody is.</summary>
    private void DrawLifterLine(JoinSession s)
    {
        if (!s.TakesHelper)
            return;
        if (!_sim.World.IsAlive(s.Partner) || !_sim.World.TryGet<Sim.Components.Creature>(s.Partner, out var lifter))
        {
            TextC(Theme.Warn, "With its weight on the ground, the hub barely goes on. Call Help! from the quick-say wheel for someone to lift the wagon.");
            return;
        }
        var recent = s.LastLiftStep >= 0 && s.StepsDone - s.LastLiftStep < Sim.Carpentry.LiftSession.WordSteps;
        if (recent && s.LastLift < 0f)
            TextC(Theme.Bad, $"{lifter.Name} let it tip: the hub's jolted back!");
        else if (recent)
            TextC(Theme.Good, $"{lifter.Name} held it level: the blow went home.");
        else
            TextC(Theme.Dim, $"{lifter.Name} has the wagon's weight: strike when it's level.");
    }

    /// <summary>Planks from a log: hewn square first, then pit-sawn.</summary>
    private void DrawSaw(SawSession s)
    {
        if (s.Hewing)
            DrawHew(s);
        else
            DrawPitSaw(s);
    }

    // The blows seen so far, and when the last one landed: chips fly for a moment after each.
    private int _hewBlows;
    private float _hewBlowAt = -10f;

    /// <summary>
    /// The log end on, turned so the side being hewn faces right, upright, as a hewer standing beside
    /// it sees it: bark round it, the square chalked on its end, each side flat where it's been hewn.
    /// Over that side the broad axe, raised as high as it's held, its edge on a dashed line down the
    /// face where the blow will cut to, against the chalk; let go, it comes straight down along it.
    /// </summary>
    private void DrawHew(SawSession s)
    {
        BeginWork(s.Title, ("F|Click", "hold to raise the axe, let go to strike"), ("Space", "flat enough: turn the log"), ("Esc", "stop"));
        TextC(Theme.Dim, s.Turning > 0f ? "Turning the log to its next side..." : "Hew the right side flat, as near the chalk line as you dare, then turn the log. Past the line is waste.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(560, 260);
        ImGui.Dummy(in size);
        var c = new float2(min.X + Gui.S(130), min.Y + Gui.S(160));
        var r = Gui.S(95);
        var half = r * 0.66f;                                  // the chalked square's half side
        // How far from the middle a side's face stands: at the bark untouched, at the chalk flat, nearer past it.
        float Face(int side) => half + s.Proud[side] * (r - half);
        // The side facing right is the one being hewn; the rest follow round, a quarter turn each. Just
        // after a side's done the log is still coming round: eased from where it was to where it'll be.
        var turning = s.Turning;
        var ease = turning * turning * (3f - 2f * turning);
        float Facing(int side) => (side - Math.Min(s.Side, SawSession.Sides - 1) + ease) * MathF.PI / 2f;
        float Radius(float a)
        {
            var radius = r;
            for (var k = 0; k < SawSession.Sides; k++)
            {
                var cos = MathF.Cos(a - Facing(k));
                if (cos > 0.001f)
                    radius = MathF.Min(radius, Face(k) / cos);
            }
            return radius;
        }
        // The log's end as it stands: bark, then the wood inside it.
        const int n = 72;
        for (var i = 0; i < n; i++)
        {
            float a0 = i * MathF.Tau / n, a1 = (i + 1) * MathF.Tau / n;
            var p0 = c + new float2(MathF.Cos(a0), MathF.Sin(a0)) * Radius(a0);
            var p1 = c + new float2(MathF.Cos(a1), MathF.Sin(a1)) * Radius(a1);
            dl.AddTriangleFilled(in c, in p0, in p1, Theme.U8(Theme.Oak));
            var q0 = c + new float2(MathF.Cos(a0), MathF.Sin(a0)) * MathF.Min(Radius(a0), r - Gui.S(7));
            var q1 = c + new float2(MathF.Cos(a1), MathF.Sin(a1)) * MathF.Min(Radius(a1), r - Gui.S(7));
            dl.AddTriangleFilled(in c, in q0, in q1, Theme.U8(Theme.Barrel));
        }
        // Growth rings and the heart.
        for (var ring = 1; ring <= 4; ring++)
            dl.AddCircle(in c, r * ring / 5.5f, Theme.U8(new float4(Theme.OakLight.XYZ, 0.5f)), 40, Gui.S(1));
        // The chalked square.
        for (var k = 0; k < SawSession.Sides; k++)
        {
            var f = Facing(k);
            var mid = c + new float2(MathF.Cos(f), MathF.Sin(f)) * half;
            var along = new float2(-MathF.Sin(f), MathF.Cos(f)) * half;
            var e0 = mid - along;
            var e1 = mid + along;
            dl.AddLine(in e0, in e1, Theme.U8(k == s.Side ? Theme.Parchment : new float4(Theme.Parchment.XYZ, 0.55f)), Gui.S(k == s.Side ? 2 : 1));
        }
        if (s.Side < SawSession.Sides && turning <= 0f)
        {
            // Where the blow would cut to, as raised: dashed down the right face.
            var cutTo = Face(s.Side) - SawSession.BlowDepth(s.Raised) * (r - half);
            var x = c.X + cutTo;
            var past = cutTo < half - 0.5f;
            for (var y = c.Y - r; y < c.Y + r; y += Gui.S(10))
            {
                var d0 = new float2(x, y);
                var d1 = new float2(x, y + Gui.S(5));
                dl.AddLine(in d0, in d1, Theme.U8(past ? Theme.Bad : Theme.Accent), Gui.S(2));
            }
            // The broad axe over the right side, edge down on that line, raised with the hold: its
            // head a wedge to the edge, the haft rising away from the log.
            var lift = s.Raised * Gui.S(24); // a little: the side view shows the swing
            var edgeY = c.Y - r - Gui.S(6) - lift;
            var e0 = new float2(x - Gui.S(2), edgeY);
            var e1 = new float2(x + Gui.S(14), edgeY - Gui.S(4));
            var e2 = new float2(x + Gui.S(14), edgeY - Gui.S(28));
            var e3 = new float2(x - Gui.S(2), edgeY - Gui.S(32));
            dl.AddQuadFilled(in e3, in e2, in e1, in e0, Theme.U8(Theme.Iron));
            var bit0 = new float2(x - Gui.S(2), edgeY - Gui.S(32));
            var bit1 = new float2(x - Gui.S(2), edgeY);
            dl.AddLine(in bit0, in bit1, Theme.U8(Theme.Parchment), Gui.S(2));
            var haft0 = new float2(x + Gui.S(12), edgeY - Gui.S(18));
            var haft1 = new float2(x + Gui.S(92), edgeY - Gui.S(70));
            dl.AddLine(in haft0, in haft1, Theme.U8(Theme.Oak), Gui.S(5));
        }
        // Chips off the last blow, flying off the face (the face just finished, if the log's turning).
        if (s.Blows != _hewBlows)
            (_hewBlows, _hewBlowAt) = (s.Blows, _time);
        if (s.Side < SawSession.Sides || turning > 0f)
        {
            var since = _time - _hewBlowAt;
            var chipFace = Face(Math.Max(0, turning > 0f ? s.Side - 1 : s.Side));
            if (since < 0.5f)
                for (var k = 0; k < 8; k++)
                {
                    var f = since / 0.5f;
                    var p = new float2(c.X + chipFace + f * Gui.S(50) * (0.6f + 0.4f * MathF.Cos(k * 1.3f)),
                        c.Y + MathF.Sin(k * 1.7f) * Gui.S(50) - f * Gui.S(30) + f * f * Gui.S(60));
                    dl.AddCircleFilled(in p, Gui.S(2.5f), Theme.U8(new float4(Theme.OakLight.XYZ, 1f - f)), 6);
                }
        }
        DrawHewFromTheSide(dl, s, new float2(min.X + Gui.S(270), min.Y), new float2(min.X + size.X, min.Y + size.Y));
        // The sides done.
        for (var k = 0; k < SawSession.Sides; k++)
        {
            var done = k < s.Side;
            var score = done ? s.SideScore(s.Proud[k]) : 0f;
            if (k > 0)
                ImGui.SameLine(0, Gui.S(14));
            TextC(!done ? (k == s.Side ? Theme.Accent : Theme.Dim) : s.Proud[k] < 0f ? Theme.Bad : score > 0.9f ? Theme.Good : Theme.Warn,
                !done ? $"side {k + 1}" : s.Proud[k] < 0f ? $"side {k + 1}: bitten" : $"side {k + 1}: flat");
        }
        EndWork();
    }

    // How high the axe was raised the frame before a blow: it's swung down from there.
    private float _hewRaisedLast;

    /// <summary>
    /// The same blows from the hewer's side, as the smith's hammer is drawn at the anvil: the log lying
    /// along on its trestle, the broad axe pivoting at the hand, up as high as it's held; let go, it
    /// comes down onto the log in a blink, the log jolts and the chips fly.
    /// </summary>
    private void DrawHewFromTheSide(ImDrawListPtr dl, SawSession s, float2 min, float2 max)
    {
        var px = MathF.Max(1f, MathF.Round(3f * Gui.Scale));
        var since = _time - _hewBlowAt;
        var shake = since < 0.2f ? (1f - since / 0.2f) * Gui.S(3f) : 0f;
        var jolt = new float2(MathF.Sin(_time * 90f) * shake, MathF.Cos(_time * 70f) * shake);
        if (!Gui.TrySprite("ui/work/log_side", out var log))
            return;
        var l = new float2(log.PixelSize.X, log.PixelSize.Y) * px;
        var lMin = new float2((min.X + max.X) * 0.5f - l.X * 0.5f, max.Y - l.Y - Gui.S(30)) + jolt;
        // The trestle under it.
        var legs = Theme.U8(Theme.Oak);
        dl.AddLine(new float2(lMin.X + l.X * 0.2f, lMin.Y + l.Y), new float2(lMin.X + l.X * 0.12f, max.Y - Gui.S(6)), legs, Gui.S(4));
        dl.AddLine(new float2(lMin.X + l.X * 0.8f, lMin.Y + l.Y), new float2(lMin.X + l.X * 0.88f, max.Y - Gui.S(6)), legs, Gui.S(4));
        Gui.Image(dl, log, lMin, lMin + l, Gui.White);
        // Where the blade bites: along the top, the hewn face showing as flatter wood where it's been worked.
        var strike = new float2(lMin.X + l.X * 0.42f, lMin.Y + px * 2f);
        var hewn = 1f - Math.Clamp(s.Side < SawSession.Sides ? s.Proud[s.Side] : 0f, 0f, 1f);
        if (hewn > 0f)
            dl.AddRectFilled(new float2(lMin.X + px * 3, lMin.Y + px * 2), new float2(lMin.X + l.X - px * 10, lMin.Y + px * (2 + 3 * hewn)),
                Theme.U8(Theme.OakLight, 0.85f), 0f, ImDrawFlags.None);
        // The axe: as raised; after a blow, swung down from where it was (a blink), then resting on the log.
        if (s.Raised > 0f)
            _hewRaisedLast = s.Raised;
        var raise = s.Turning > 0f ? 0.15f
            : s.Raised > 0f ? s.Raised
            : since < 0.07f ? _hewRaisedLast * (1f - since / 0.07f)
            : 0f;
        DrawToolSwing(dl, "ui/work/axe", 27f / 34f, raise, strike + jolt, px, Gui.White);
        // Chips off the face on the blow.
        if (since < 0.45f && s.Blows > 0)
            for (var k = 0; k < 7; k++)
            {
                var f = since / 0.45f;
                var p = strike + new float2(MathF.Cos(k * 1.3f + 0.4f) * Gui.S(70) * f, -Gui.S(40) * f + Gui.S(80) * f * f);
                dl.AddCircleFilled(in p, Gui.S(2.5f), Theme.U8(new float4(Theme.OakLight.XYZ, 1f - f)), 6);
            }
        var label = new float2(min.X + Gui.S(4), min.Y + Gui.S(4));
        dl.AddText(in label, Theme.U8(Theme.Dim), "from the side"u8);
    }

    /// <summary>
    /// The beam end on, as the top sawyer sees it: the planks' lines chalked down its end, the cuts
    /// done, and the long saw upright on the line, worked up and down. Beside it the beam from above,
    /// the cut creeping along it, wandering where it wandered. An arrow points back to the line when it's off.
    /// </summary>
    private void DrawPitSaw(SawSession s)
    {
        BeginWork(s.Title, ("W/S|Mouse", "work the saw down and up: down cuts"), ("A/D", "steer it back to the line"), ("Esc", "stop"));
        TextC(Theme.Dim, "Long, steady strokes: a rushed one wanders. Keep the blade on the chalk line.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(440, 260);
        ImGui.Dummy(in size);
        // The beam's end, a square, its planks' lines down it.
        var b0 = new float2(min.X + Gui.S(40), min.Y + Gui.S(60));
        var side = Gui.S(150);
        var b1 = b0 + new float2(side, side);
        var travel = Gui.S(30);                                   // a blade off the line by 1, in pixels
        float LineX(int cut) => b0.X + side * (cut + 1f) / (SawSession.Cuts + 1f);
        var current = Math.Min(s.Cut, SawSession.Cuts - 1);
        var line = LineX(current);
        // The mouse: across the beam steers, down the panel works the saw.
        // Across a good width, so working the saw up and down doesn't steer it by accident.
        TakesPointer(new float2(line - travel * 4f, min.Y + Gui.S(10)), new float2(line + travel * 4f, min.Y + size.Y - Gui.S(10)));
        dl.AddRectFilled(in b0, in b1, Theme.U8(Theme.Barrel), Gui.S(3), ImDrawFlags.None);
        var bc = (b0 + b1) * 0.5f;
        for (var ring = 1; ring <= 3; ring++)
            dl.AddCircle(in bc, side * ring / 7f, Theme.U8(new float4(Theme.OakLight.XYZ, 0.5f)), 32, Gui.S(1));
        for (var k = 0; k < SawSession.Cuts; k++)
        {
            var x = LineX(k);
            var l0 = new float2(x, b0.Y);
            var l1 = new float2(x, b1.Y);
            dl.AddLine(in l0, in l1, Theme.U8(k == current ? Theme.Parchment : new float4(Theme.Parchment.XYZ, 0.45f)), Gui.S(k == current ? 2 : 1));
            if (k < s.Cut)
            {
                // A cut done: a kerf down the end where it ended up.
                var kx = x + s.CutOffsets[k] * travel;
                var k0 = new float2(kx, b0.Y);
                var k1 = new float2(kx, b1.Y);
                dl.AddLine(in k0, in k1, Theme.U8(Theme.Cellar), Gui.S(4));
            }
        }
        var band = s.OnLineWidth * travel;
        var band0 = new float2(line - band, b0.Y);
        var band1 = new float2(line + band, b1.Y);
        dl.AddRectFilled(in band0, in band1, Theme.U8(new float4(Theme.Parchment.XYZ, 0.2f)), 0f, ImDrawFlags.None);

        if (s.Cut < SawSession.Cuts)
        {
            // The long saw, upright on its line: a blade taller than the beam, worked down and up, the
            // tiller handle across its top and the box handle under it.
            var bx = line + s.Offset * travel;
            var reach = Gui.S(70);
            var shift = (s.Stroke - 0.5f) * reach;
            var top = new float2(bx, min.Y + Gui.S(16) + shift);
            var bottom = new float2(bx, top.Y + side + Gui.S(80));
            var accuracy = s.Accuracy(s.Offset);
            dl.AddLine(in top, in bottom, Theme.U8(accuracy > 0.7f ? Theme.Iron : accuracy > 0.3f ? Theme.Warn : Theme.Bad), Gui.S(4));
            var t0 = new float2(bx - Gui.S(22), top.Y - Gui.S(3));
            var t1 = new float2(bx + Gui.S(22), top.Y + Gui.S(5));
            dl.AddRectFilled(in t0, in t1, Theme.U8(Theme.Oak), Gui.S(3), ImDrawFlags.None);
            var x0 = new float2(bx - Gui.S(10), bottom.Y - Gui.S(4));
            var x1 = new float2(bx + Gui.S(10), bottom.Y + Gui.S(6));
            dl.AddRectFilled(in x0, in x1, Theme.U8(Theme.Oak), Gui.S(3), ImDrawFlags.None);
            // Back to the line: an arrow beside the blade, and the key.
            if (MathF.Abs(s.Offset) > s.OnLineWidth)
            {
                var way = -MathF.Sign(s.Offset);
                var ay = b0.Y - Gui.S(14);
                var a0 = new float2(bx, ay);
                var a1 = new float2(bx + way * Gui.S(26), ay);
                var colour = Theme.U8(Theme.Accent);
                dl.AddLine(in a0, in a1, colour, Gui.S(3));
                var h0 = new float2(a1.X - way * Gui.S(6), ay - Gui.S(6));
                var h1 = new float2(a1.X - way * Gui.S(6), ay + Gui.S(6));
                dl.AddTriangleFilled(in h0, in h1, in a1, colour);
                var label = new float2(a1.X + (way > 0 ? Gui.S(4) : -Gui.S(14)), ay - Gui.S(8));
                dl.AddText(in label, colour, way < 0 ? "A" : "D");
            }
            // Sawdust falling from under the beam on the downstroke.
            if (s.Cutting)
                for (var k = 0; k < 6; k++)
                {
                    var f = (_time * 2.5f + k / 6f) % 1f;
                    var d = new float2(bx + MathF.Sin(k * 2.3f + _time * 3f) * Gui.S(8), b1.Y + f * Gui.S(40));
                    dl.AddCircleFilled(in d, Gui.S(1.5f), Theme.U8(new float4(Theme.Parchment.XYZ, 1f - f)), 6);
                }
        }

        // The beam from above, along its length: its line, and the cut so far.
        var v0 = new float2(min.X + Gui.S(240), min.Y + Gui.S(60));
        var v1 = new float2(min.X + size.X - Gui.S(16), min.Y + Gui.S(60) + side);
        dl.AddRectFilled(in v0, in v1, Theme.U8(Theme.OakLight), Gui.S(3), ImDrawFlags.None);
        var vx = (v0.X + v1.X) * 0.5f;
        var vl0 = new float2(vx, v0.Y);
        var vl1 = new float2(vx, v1.Y);
        dl.AddLine(in vl0, in vl1, Theme.U8(Theme.Parchment), Gui.S(1));
        var travelAbove = (v1.X - v0.X) * 0.3f;
        var marks = Math.Min(SawSession.Marks, (int)(s.Progress * SawSession.Marks));
        // A little smoothed, as a kerf is: each point the mean of its neighbours along the cut.
        float Smooth(int i)
        {
            float sum = 0f, n = 0f;
            for (var j = Math.Max(0, i - 3); j <= Math.Min(Math.Max(0, marks - 1), i + 3); j++)
            {
                sum += s.Path[j];
                n++;
            }
            return n > 0 ? sum / n : 0f;
        }
        var prev = new float2(vx + Smooth(0) * travelAbove, v0.Y);
        for (var i = 0; i < marks; i++)
        {
            var p = new float2(vx + Smooth(i) * travelAbove, v0.Y + (i + 1f) / SawSession.Marks * (v1.Y - v0.Y));
            dl.AddLine(in prev, in p, Theme.U8(Theme.Cellar), Gui.S(3));
            prev = p;
        }
        var label2 = new float2(v0.X, v1.Y + Gui.S(6));
        dl.AddText(in label2, Theme.U8(Theme.Dim), "from above"u8);
        DrawPitmanLine(s);
        EndWork();
    }

    /// <summary>Under a great log's saw: how the pitman's pulling, or that there's none.</summary>
    private void DrawPitmanLine(SawSession s)
    {
        if (!s.Great)
            return;
        if (!_sim.World.IsAlive(s.Partner) || !_sim.World.TryGet<Sim.Components.Creature>(s.Partner, out var pitman))
        {
            TextC(Theme.Warn, "A great log: your stroke barely moves the saw alone. Call Help! from the quick-say wheel for a pitman below.");
            return;
        }
        var recent = s.LastPullStep >= 0 && s.StepsDone - s.LastPullStep < Sim.Carpentry.PitSession.WordSteps;
        if (recent && s.LastPull < 0f)
            TextC(Theme.Bad, $"{pitman.Name} pulled against you: the saw binds!");
        else if (recent)
            TextC(s.LastPull >= 0.6f ? Theme.Good : Theme.Text, $"{pitman.Name} pulled with your stroke{(s.LastPull < 0.4f ? ", short" : "")}.");
        else
            TextC(Theme.Dim, $"{pitman.Name} is in the pit: each downstroke you start, they pull.");
    }

    /// <summary>The arrow at the stage it's at: sighting, shaving, the head, fletching.</summary>
    private void DrawFletch(FletchSession s)
    {
        switch (s.Stage)
        {
            case ArrowStage.Sight:
                DrawSight(s);
                break;
            case ArrowStage.Shave:
                DrawShave(s);
                break;
            case ArrowStage.Head:
                DrawHead(s);
                break;
            default:
                DrawFeathers(s);
                break;
        }
    }

    /// <summary>Which of the four the arrow's at, as a row of steps under the title.</summary>
    private static void ArrowSteps(FletchSession s)
    {
        string[] names = ["Sight", "Shave", "Head", "Fletch"];
        for (var i = 0; i < names.Length; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine(0, 6);
                TextC(Theme.Dim, "·");
                ImGui.SameLine(0, 6);
            }
            TextC(i < (int)s.Stage ? Theme.Good : i == (int)s.Stage ? Theme.Accent : Theme.Dim, names[i]);
        }
    }

    /// <summary>The shaft as it bows, sighted down its length, and a needle for the warp the eye reads.</summary>
    private void DrawSight(FletchSession s)
    {
        BeginWork(s.Title, ("F", "hold to bend it straight"), ("Space", "it's true"), ("Esc", "stop"));
        ArrowSteps(s);
        TextC(Theme.Dim, "Bend it back against the warp. Go too far and it warps the other way.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(360, 150);
        ImGui.Dummy(in size);
        var mid = min.X + size.X * 0.5f;
        var y0 = min.Y + Gui.S(50);
        var len = Gui.S(150);
        var bow = s.SeenWarp * Gui.S(40);
        var prev = new float2(mid - len, y0);
        for (var i = 1; i <= 16; i++)
        {
            var t = i / 16f;
            var p = new float2(mid - len + 2f * len * t, y0 + bow * 4f * t * (1f - t));
            dl.AddLine(in prev, in p, Theme.U8(Theme.Barrel), Gui.S(4));
            prev = p;
        }
        var gy = min.Y + Gui.S(115);
        var span = Gui.S(150);
        var g1 = new float2(mid - span, gy);
        var g2 = new float2(mid + span, gy + Gui.S(10));
        dl.AddRectFilled(in g1, in g2, Theme.U8(Theme.Well), Gui.S(3), ImDrawFlags.None);
        var tw = s.TrueWidth * span;
        var t1 = new float2(mid - tw, gy);
        var t2 = new float2(mid + tw, gy + Gui.S(10));
        dl.AddRectFilled(in t1, in t2, Theme.U8(Theme.Good), Gui.S(2), ImDrawFlags.None);
        var nx = mid + Math.Clamp(s.SeenWarp, -1f, 1f) * span;
        var n1 = new float2(nx, gy - Gui.S(8));
        var n2 = new float2(nx, gy + Gui.S(18));
        dl.AddLine(in n1, in n2, Theme.U8(Theme.Accent), Gui.S(3));
        EndWork();
    }

    /// <summary>
    /// The shaft side on: the wood still standing proud of its line, the gouges cut into it, and the
    /// knife where the hand has it, edge down. A bar says how much is off so far.
    /// </summary>
    private void DrawShave(FletchSession s)
    {
        BeginWork(s.Title, ("Mouse|WASD", "move the knife"), ("Click|F", "hold to shave"), ("Space", "smooth enough: done"), ("Esc", "stop"));
        ArrowSteps(s);
        TextC(Theme.Dim, "Take the lumps down to the line. Below it, the knife bites into the shaft.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(400, 170);
        ImGui.Dummy(in size);
        var area0 = new float2(min.X + Gui.S(10), min.Y + Gui.S(4));
        var area1 = new float2(min.X + size.X - Gui.S(10), min.Y + size.Y - Gui.S(24));
        TakesPointer(area0, area1);
        float X(float u) => area0.X + u * (area1.X - area0.X);
        float Y(float v) => area0.Y + v * (area1.Y - area0.Y);
        var line = Y(FletchSession.LineV);
        var unit = FletchSession.HeightV * (area1.Y - area0.Y); // a height of 1, in pixels

        // The shaft under its line, gouged where the knife went in.
        var shaft0 = new float2(area0.X, line);
        var shaft1 = new float2(area1.X, line + Gui.S(16));
        dl.AddRectFilled(in shaft0, in shaft1, Theme.U8(Theme.Barrel), Gui.S(3), ImDrawFlags.None);
        var colW = (area1.X - area0.X) / FletchSession.Columns;
        for (var i = 0; i < FletchSession.Columns; i++)
        {
            var x0 = area0.X + i * colW;
            if (s.Wood[i] > 0.002f)
            {
                var a = new float2(x0, line - s.Wood[i] * unit);
                var b = new float2(x0 + colW + 0.5f, line);
                dl.AddRectFilled(in a, in b, Theme.U8(Theme.OakLight), 0f, ImDrawFlags.None);
            }
            if (s.Gouges[i] > 0.001f)
            {
                var a = new float2(x0, line);
                var b = new float2(x0 + colW + 0.5f, line + Math.Min(Gui.S(14), s.Gouges[i] * unit));
                dl.AddRectFilled(in a, in b, Theme.U8(Theme.Bad), 0f, ImDrawFlags.None);
            }
        }
        var l1 = new float2(area0.X, line);
        var l2 = new float2(area1.X, line);
        dl.AddLine(in l1, in l2, Theme.U8(new float4(Theme.Parchment.XYZ, 0.7f)), Gui.S(1));

        // The knife: a short blade, edge down at where it is, its handle up and back.
        var kx = X(s.KnifeX);
        var ky = Y(s.KnifeY);
        var edgeW = FletchSession.Edge * (area1.X - area0.X);
        var down = s.Held;
        var e1 = new float2(kx - edgeW, ky);
        var e2 = new float2(kx + edgeW, ky);
        var bladeTop1 = new float2(kx - edgeW, ky - Gui.S(10));
        dl.AddRectFilled(in bladeTop1, in e2, Theme.U8(Theme.Iron), Gui.S(1), ImDrawFlags.None);
        dl.AddLine(in e1, in e2, Theme.U8(s.KnifeHeight < 0f ? Theme.Bad : down ? Theme.Accent : Theme.Parchment), Gui.S(2));
        var hd1 = new float2(kx + edgeW - Gui.S(4), ky - Gui.S(26));
        var hd2 = new float2(kx + edgeW + Gui.S(4), ky - Gui.S(10));
        dl.AddRectFilled(in hd1, in hd2, Theme.U8(Theme.Oak), Gui.S(2), ImDrawFlags.None);

        // How much is off so far.
        var off = s.ShaveScore();
        var bar0 = new float2(area0.X, min.Y + size.Y - Gui.S(14));
        var bar1 = new float2(area1.X, min.Y + size.Y - Gui.S(6));
        dl.AddRectFilled(in bar0, in bar1, Theme.U8(Theme.Well), Gui.S(3), ImDrawFlags.None);
        var fill = new float2(bar0.X + off * (bar1.X - bar0.X), bar1.Y);
        dl.AddRectFilled(in bar0, in fill, Theme.U8(off > 0.8f ? Theme.Good : Theme.Warn), Gui.S(3), ImDrawFlags.None);
        EndWork();
    }

    /// <summary>The shaft's end with the iron head on it, tipped as crooked as it is; a guide line it should follow; how far it's on.</summary>
    private void DrawHead(FletchSession s)
    {
        BeginWork(s.Title, ("F", "hold to push the head on"), ("A/D|Move", "keep it in line"), ("Esc", "stop"));
        ArrowSteps(s);
        TextC(Theme.Dim, "Push the head onto the shaft. It tips as it goes: keep it on the line.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(380, 150);
        ImGui.Dummy(in size);
        TakesPointer(min, min + size);
        var y = min.Y + size.Y * 0.5f;
        var tip = min.X + Gui.S(250);          // the shaft's end
        // The shaft, and the guide on along its line.
        var s0 = new float2(min.X + Gui.S(20), y - Gui.S(5));
        var s1 = new float2(tip, y + Gui.S(5));
        dl.AddRectFilled(in s0, in s1, Theme.U8(Theme.Barrel), Gui.S(2), ImDrawFlags.None);
        var g1 = new float2(tip, y);
        var g2 = new float2(min.X + size.X - Gui.S(10), y);
        dl.AddLine(in g1, in g2, Theme.U8(new float4(Theme.Parchment.XYZ, 0.4f)), Gui.S(1));
        // The head: a socket and a leaf-shaped point, slid on by how far it's seated, tipped by the tilt.
        var socketLen = Gui.S(30);
        var gap = (1f - s.Seated) * Gui.S(40);
        var pivot = new float2(tip + gap - socketLen * 0.5f, y);
        var angle = s.Tilt * 0.6f;
        var dir = new float2(MathF.Cos(angle), MathF.Sin(angle));
        var nrm = new float2(-dir.Y, dir.X);
        float2 P(float along, float across) => pivot + dir * along + nrm * across;
        var a1 = P(0, -Gui.S(6)); var a2 = P(socketLen, -Gui.S(6)); var a3 = P(socketLen, Gui.S(6)); var a4 = P(0, Gui.S(6));
        var good = s.LineScore(s.Tilt) > 0.7f;
        var iron = Theme.U8(good ? Theme.Iron : Theme.Warn);
        dl.AddQuadFilled(in a1, in a2, in a3, in a4, iron);
        var p1 = P(socketLen, -Gui.S(14)); var p2 = P(socketLen + Gui.S(40), 0); var p3 = P(socketLen, Gui.S(14));
        dl.AddTriangleFilled(in p1, in p2, in p3, iron);
        // Seated: bound on with sinew, wraps over the socket.
        if (s.Seated >= 1f)
            for (var k = 0; k < 5; k++)
            {
                var w1 = P(Gui.S(4) + k * Gui.S(5), -Gui.S(7));
                var w2 = P(Gui.S(6) + k * Gui.S(5), Gui.S(7));
                dl.AddLine(in w1, in w2, Theme.U8(Theme.Parchment), Gui.S(2));
            }
        // How far on.
        var bar0 = new float2(min.X + Gui.S(20), min.Y + size.Y - Gui.S(14));
        var bar1 = new float2(min.X + size.X - Gui.S(20), min.Y + size.Y - Gui.S(6));
        dl.AddRectFilled(in bar0, in bar1, Theme.U8(Theme.Well), Gui.S(3), ImDrawFlags.None);
        var fill = new float2(bar0.X + s.Seated * (bar1.X - bar0.X), bar1.Y);
        dl.AddRectFilled(in bar0, in fill, Theme.U8(Theme.Accent), Gui.S(3), ImDrawFlags.None);
        EndWork();
    }

    /// <summary>The shaft end on, still; the feathers set on it; and a pointer round it where the next will go.</summary>
    private void DrawFeathers(FletchSession s)
    {
        BeginWork(s.Title, ("Mouse|A/D", "point round the shaft"), ("Click|F", "set a feather"), ("Esc", "stop"));
        ArrowSteps(s);
        TextC(Theme.Dim, "Three feathers, evenly round the shaft.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(220, 220);
        ImGui.Dummy(in size);
        TakesPointer(min, min + size);
        var centre = new float2(min.X + size.X * 0.5f, min.Y + size.Y * 0.5f);
        var r = Gui.S(24);
        float2 At(float turn, float radius) =>
            centre + new float2(MathF.Sin(turn * MathF.Tau), -MathF.Cos(turn * MathF.Tau)) * radius;
        dl.AddCircle(in centre, Gui.S(78), Theme.U8(Theme.Inset), 48, Gui.S(2));
        dl.AddCircleFilled(in centre, r, Theme.U8(Theme.Barrel), 24);
        var nock = Gui.S(5);
        dl.AddCircleFilled(in centre, nock, Theme.U8(Theme.Oak), 12);
        for (var i = 0; i < s.Set; i++)
        {
            var a = At(s.FeatherAt[i], r);
            var b = At(s.FeatherAt[i], Gui.S(70));
            dl.AddLine(in a, in b, Theme.U8(Theme.Parchment), Gui.S(7));
        }
        if (s.Set < FletchSession.Feathers)
        {
            var a = At(s.Aim, r + Gui.S(2));
            var b = At(s.Aim, Gui.S(70));
            dl.AddLine(in a, in b, Theme.U8(new float4(Theme.Accent.XYZ, 0.7f)), Gui.S(3));
        }
        EndWork();
    }


    /// <summary>The bow: on the tillering stick while it's being scraped, then being strung.</summary>
    private void DrawBow(BowSession s)
    {
        if (s.Stringing)
            DrawStringing(s);
        else
            DrawTiller(s);
    }

    /// <summary>
    /// The bow on the tillering stick, drawn part way: each section of each limb bent as it bends now,
    /// an even arc over it to match, and the scraper working the section it's on (shavings flying
    /// while it bites). Under it a bar per section: the wood still to come off, down to its mark.
    /// </summary>
    private void DrawTiller(BowSession s)
    {
        BeginWork(s.Title, ("A/D|Mouse", "pick a section"), ("F", "hold to scrape it"), ("Space", "every bar's at its mark: string it"), ("Esc", "stop"));
        TextC(Theme.Dim, "Each bar is wood still to come off. Scrape every one down to its mark, both limbs alike. Past the mark is a hinge.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(440, 250);
        ImGui.Dummy(in size);
        var left = min.X + Gui.S(20);
        var width = size.X - Gui.S(40);
        var gripY = min.Y + Gui.S(36);
        TakesPointer(new float2(left, min.Y), new float2(left + width, min.Y + size.Y));
        var segW = width / BowSession.Sections;
        var mid = left + width * 0.5f;

        static int Col(int i) => BowSession.ColumnOf(i);

        // A limb's points, grip outward, bending by each section's give (an even bow follows the arc).
        float2[] Limb(int first, int dir, Func<int, float> give)
        {
            var pts = new float2[BowSession.SectionsPerLimb + 1];
            pts[0] = new float2(mid, gripY);
            var angle = 0f;
            for (var k = 0; k < BowSession.SectionsPerLimb; k++)
            {
                angle += give(first + k) * 0.22f;
                pts[k + 1] = pts[k] + new float2(MathF.Cos(angle) * dir, MathF.Sin(angle)) * segW;
            }
            return pts;
        }
        var selectedMid = new float2(mid, gripY);
        for (var limb = 0; limb < 2; limb++)
        {
            var first = limb == 0 ? 0 : BowSession.SectionsPerLimb;
            var dir = limb == 0 ? -1 : 1;
            var even = Limb(first, dir, _ => 1f);
            var now = Limb(first, dir, i => s.Bend(i));
            for (var k = 0; k < BowSession.SectionsPerLimb; k++)
            {
                dl.AddLine(in even[k], in even[k + 1], Theme.U8(new float4(Theme.Parchment.XYZ, 0.35f)), Gui.S(2));
                var i = first + k;
                var colour = s.Stiffness[i] < BowSession.Even - s.RightWidth ? Theme.Bad
                    : s.SectionScore(s.Stiffness[i]) > 0.8f ? Theme.Good : Theme.Barrel;
                dl.AddLine(in now[k], in now[k + 1], Theme.U8(colour), Gui.S(i == s.Selected ? 8 : 5));
                if (i == s.Selected)
                    selectedMid = (now[k] + now[k + 1]) * 0.5f;
            }
        }
        var g1 = new float2(mid - Gui.S(6), gripY - Gui.S(8));
        var g2 = new float2(mid + Gui.S(6), gripY + Gui.S(8));
        dl.AddRectFilled(in g1, in g2, Theme.U8(Theme.Oak), Gui.S(2), ImDrawFlags.None);

        // The scraper on the section it's at: a blade across the limb, worked back and forth while held,
        // shavings curling off while it bites.
        var work = s.Held ? MathF.Sin(_time * 18f) * Gui.S(6) : 0f;
        var sc0 = new float2(selectedMid.X - Gui.S(12) + work, selectedMid.Y - Gui.S(14));
        var sc1 = new float2(selectedMid.X + Gui.S(12) + work, selectedMid.Y - Gui.S(8));
        dl.AddRectFilled(in sc0, in sc1, Theme.U8(Theme.Iron), Gui.S(1), ImDrawFlags.None);
        if (s.Scraping)
            for (var k = 0; k < 6; k++)
            {
                var f = (_time * 2.2f + k / 6f) % 1f;
                var p = new float2(selectedMid.X + MathF.Sin(k * 2.1f) * Gui.S(14) * f, selectedMid.Y - Gui.S(14) - f * Gui.S(22));
                dl.AddCircleFilled(in p, Gui.S(2f) * (1f - f * 0.5f), Theme.U8(new float4(Theme.Parchment.XYZ, 1f - f)), 6);
            }

        // A bar per section: wood still to come off, standing over its mark; red below the mark, a hinge.
        var mark = min.Y + size.Y - Gui.S(26);   // the mark: even
        var tall = Gui.S(70);                     // a stiffness of 0.6 over even, in pixels
        var m0 = new float2(left, mark);
        var m1 = new float2(left + width, mark);
        for (var i = 0; i < BowSession.Sections; i++)
        {
            var col = Col(i);
            var x = left + col * segW + Gui.S(5);
            var w = segW - Gui.S(10);
            var over = s.Stiffness[i] - BowSession.Even;
            var colour = over < -s.RightWidth ? Theme.Bad : over <= s.RightWidth ? Theme.Good : Theme.OakLight;
            var top = mark - Math.Max(0f, over) / 0.6f * tall;
            var bottom = over < 0 ? mark - over / 0.6f * tall : mark;
            var r0 = new float2(x, Math.Min(top, mark - Gui.S(2)));
            var r1 = new float2(x + w, Math.Max(bottom, mark));
            dl.AddRectFilled(in r0, in r1, Theme.U8(colour), Gui.S(2), ImDrawFlags.None);
            if (i == s.Selected)
            {
                var a0 = new float2(x - Gui.S(3), mark - tall - Gui.S(4));
                var a1 = new float2(x + w + Gui.S(3), mark + Gui.S(18));
                dl.AddRect(in a0, in a1, Theme.U8(Theme.Accent), Gui.S(3), ImDrawFlags.None, Gui.S(2));
            }
        }
        dl.AddLine(in m0, in m1, Theme.U8(Theme.Parchment), Gui.S(2));
        var limbs = new float2(left, mark + Gui.S(6));
        dl.AddText(in limbs, Theme.U8(Theme.Dim), "upper limb"u8);
        var lower = new float2(mid + Gui.S(8), mark + Gui.S(6));
        dl.AddText(in lower, Theme.U8(Theme.Dim), "lower limb"u8);
        EndWork();
    }

    /// <summary>The bow braced against the floor, bending as it's held down; the string's loop at the nock; and a gauge of how far it's bent, the band where the loop catches and the strain past it.</summary>
    private void DrawStringing(BowSession s)
    {
        BeginWork(s.Title, ("F", "hold to bend the bow"), ("Space", "slip the loop into the nock"), ("Esc", "stop"));
        TextC(Theme.Dim, "Bend it into the green, then slip the string on. Bent into the red, the limbs strain.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(360, 220);
        ImGui.Dummy(in size);
        var cx = min.X + Gui.S(130);
        var foot = new float2(cx, min.Y + size.Y - Gui.S(20));   // the lower tip, against the floor
        var height = Gui.S(170);
        // The bow standing on its lower tip, bowed by how far it's bent.
        var bow = Gui.S(12) + s.Flex * Gui.S(46);
        var prev = foot;
        float2 At(float t) => new(cx - bow * 4f * t * (1f - t), foot.Y - t * height * (1f - 0.18f * s.Flex));
        for (var i = 1; i <= 20; i++)
        {
            var p = At(i / 20f);
            dl.AddLine(in prev, in p, Theme.U8(s.MostBend > BowSession.Strained ? Theme.Warn : Theme.Barrel), Gui.S(6));
            prev = p;
        }
        var top = At(1f);
        // The string: tied at the foot, its loop hanging short of the top nock till it's slipped on.
        var loop = new float2(cx + Gui.S(4), foot.Y - height * 0.86f);
        var strung = Theme.U8(Theme.Parchment);
        dl.AddLine(in foot, in loop, strung, Gui.S(1.5f));
        dl.AddCircle(in loop, Gui.S(4), strung, 10, Gui.S(1.5f));
        var reach = MathF.Abs(loop.Y - top.Y) < Gui.S(8);
        dl.AddCircleFilled(in top, Gui.S(4), Theme.U8(reach ? Theme.Good : Theme.Iron), 10); // the nock
        // A hand pressing down on the grip while it's held.
        if (s.Held)
        {
            var hand = At(0.5f);
            var h0 = new float2(hand.X - Gui.S(10), hand.Y - Gui.S(6));
            var h1 = new float2(hand.X + Gui.S(4), hand.Y + Gui.S(6));
            dl.AddRectFilled(in h0, in h1, Theme.U8(Theme.Accent), Gui.S(4), ImDrawFlags.None);
        }
        // The gauge: how far it's bent, the catch from its mark up to the strain, red past it.
        var gx = min.X + Gui.S(230);
        var g0 = new float2(gx, min.Y + Gui.S(20));
        var g1 = new float2(gx + Gui.S(18), min.Y + size.Y - Gui.S(20));
        var gh = g1.Y - g0.Y;
        dl.AddRectFilled(in g0, in g1, Theme.U8(Theme.Well), Gui.S(3), ImDrawFlags.None);
        var catch0 = new float2(g0.X, g1.Y - BowSession.Strained * gh);
        var catch1 = new float2(g1.X, g1.Y - s.CatchLow * gh);
        dl.AddRectFilled(in catch0, in catch1, Theme.U8(Theme.Good), Gui.S(2), ImDrawFlags.None);
        var strain1 = new float2(g1.X, g1.Y - BowSession.Strained * gh);
        dl.AddRectFilled(in g0, in strain1, Theme.U8(Theme.Bad), Gui.S(2), ImDrawFlags.None);
        var ny = g1.Y - s.Flex * gh;
        var n0 = new float2(g0.X - Gui.S(6), ny);
        var n1 = new float2(g1.X + Gui.S(6), ny);
        dl.AddLine(in n0, in n1, Theme.U8(Theme.Accent), Gui.S(3));
        if (s.Missed > 0)
        {
            var t = new float2(gx + Gui.S(28), g1.Y - Gui.S(14));
            dl.AddText(in t, Theme.U8(Theme.Warn), s.Missed == 1 ? "The loop slipped past once." : $"The loop slipped past {s.Missed} times.");
        }
        EndWork();
    }
}
