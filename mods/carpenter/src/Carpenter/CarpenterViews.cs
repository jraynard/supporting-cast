using Brutal.ImGuiApi;
using HeroesNotIncluded.Mods;
using HeroesNotIncluded.Sim.Carpentry;
using HeroesNotIncluded.Ui;
using Narsil.Ui;

namespace HeroesNotIncluded.Carpentry;

/// <summary>
/// The carpenter's mini-games as they're drawn: the woodshop's (saw, bow, fletching, joinery), the
/// woods' (felling, bucking, splitting) and the work for two (the pit, the lift). Each is drawn into
/// the game's work window through <see cref="WorkView"/>, and reads only what its session shows.
/// </summary>
public sealed class CarpenterViews
{
    /// <summary>The game as this frame's view sees it (set before each draw).</summary>
    internal WorkView V = null!;

    internal CarpenterViews With(WorkView view)
    {
        V = view;
        return this;
    }

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
    internal void DrawJoin(JoinSession s)
    {
        if (s.Cutting)
            V.Begin(s.Title, ("F|Click", "hold to raise the mallet, let go to strike"), ("Space", "deep enough: drive it home"), ("Esc", "stop"));
        else
            V.Begin(s.Title, ("F|Click", "hold to raise the mallet, let go to strike"), ("Esc", "stop"));
        var (tone, say) = s.Settling > 0 && s.JustSplit ? (Theme.Bad, "It splits! Forced home, cracked.")
            : s.Settling > 0 ? (Theme.Good, "Home, and tight.")
            : s.Cutting && s.Depth > 1f ? (Theme.Bad, "Past the mark: this joint will be loose.")
            : s.Cutting && s.Depth >= 1f - s.SnugWidth ? (Theme.Good, Controls.Keys("At the mark. Deep enough (~Space~)."))
            : s.Cutting && s.Work == JoinWork.Haft ? (Theme.Text, "Cut the split back to sound wood, down to the chalk mark. Past it, the peg sits loose.")
            : s.Cutting ? (Theme.Text, "Chop the mortise down to the chalk mark. Past it, the joint's loose.")
            : s.Raised > JoinSession.SafeAt(s.Seat) ? (Theme.Warn, "Too hard for how tight it is: it'll split.")
            : (Theme.Text, "Drive it home. Lighter blows as it tightens.");
        ImGuiText.Colored(tone, say);
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
            (_joinBlows, _joinBlowAt) = (s.Blows, V.Time);
        var since = V.Time - _joinBlowAt;
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
        V.End();
    }

    /// <summary>Under a new axle: who's taking the wagon's weight, and how level, or that nobody is.</summary>
    internal void DrawLifterLine(JoinSession s)
    {
        if (!s.TakesHelper)
            return;
        if (!V.Sim.World.IsAlive(s.Partner) || !V.Sim.World.TryGet<Sim.Components.Creature>(s.Partner, out var lifter))
        {
            ImGuiText.Colored(Theme.Warn, "With its weight on the ground, the hub barely goes on. Call Help! from the quick-say wheel for someone to lift the wagon.");
            return;
        }
        var recent = s.LastLiftStep >= 0 && s.StepsDone - s.LastLiftStep < LiftSession.WordSteps;
        var lift = V.Sim.World.TryGet<Sim.Work.WorkSession>(s.Partner, out var helper) ? helper as LiftSession : null;
        if (recent && s.LastLift < 0f)
            ImGuiText.Colored(Theme.Bad, $"{lifter.Name} let it tip: the hub's jolted back!");
        else if (recent)
            ImGuiText.Colored(Theme.Good, $"{lifter.Name} held it level: the blow went home.");
        else if (lift is { Up: false })
            ImGuiText.Colored(Theme.Warn, $"{lifter.Name} has set it down{(lift.Spent ? " to rest their arms" : "")}: wait for them to lift it.");
        else if (lift is { Level: true })
            ImGuiText.Colored(Theme.Good, $"{lifter.Name} has it up and level: strike now.");
        else if (lift != null)
            ImGuiText.Colored(Theme.Warn, $"{lifter.Name} has it up, but it's tipping: wait for it to level.");
        else
            ImGuiText.Colored(Theme.Dim, $"{lifter.Name} has the wagon's weight: strike when it's level.");
    }

    /// <summary>Planks from a log: hewn square first, then pit-sawn.</summary>
    internal void DrawSaw(SawSession s)
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
    internal void DrawHew(SawSession s)
    {
        V.Begin(s.Title, ("F|Click", "hold to raise the axe, let go to strike"), ("Space", "flat enough: turn the log"), ("Esc", "stop"));
        ImGuiText.Colored(Theme.Dim, s.Turning > 0f ? "Turning the log to its next side..." : "Hew the right side flat, as near the chalk line as you dare, then turn the log. Past the line is waste.");
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
            (_hewBlows, _hewBlowAt) = (s.Blows, V.Time);
        if (s.Side < SawSession.Sides || turning > 0f)
        {
            var since = V.Time - _hewBlowAt;
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
            ImGuiText.Colored(!done ? (k == s.Side ? Theme.Accent : Theme.Dim) : s.Proud[k] < 0f ? Theme.Bad : score > 0.9f ? Theme.Good : Theme.Warn,
                !done ? $"side {k + 1}" : s.Proud[k] < 0f ? $"side {k + 1}: bitten" : $"side {k + 1}: flat");
        }
        V.End();
    }

    // How high the axe was raised the frame before a blow: it's swung down from there.
    private float _hewRaisedLast;

    /// <summary>
    /// The same blows from the hewer's side, as the smith's hammer is drawn at the anvil: the log lying
    /// along on its trestle, the broad axe pivoting at the hand, up as high as it's held; let go, it
    /// comes down onto the log in a blink, the log jolts and the chips fly.
    /// </summary>
    internal void DrawHewFromTheSide(ImDrawListPtr dl, SawSession s, float2 min, float2 max)
    {
        var px = MathF.Max(1f, MathF.Round(3f * Gui.Scale));
        var since = V.Time - _hewBlowAt;
        var shake = since < 0.2f ? (1f - since / 0.2f) * Gui.S(3f) : 0f;
        var jolt = new float2(MathF.Sin(V.Time * 90f) * shake, MathF.Cos(V.Time * 70f) * shake);
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
        V.DrawToolSwing(dl, "ui/work/axe", 27f / 34f, raise, strike + jolt, px, Gui.White);
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
    internal void DrawPitSaw(SawSession s)
    {
        V.Begin(s.Title, ("W/S|Mouse", "work the saw down and up: down cuts"), ("A/D", "steer it back to the line"), ("Esc", "stop"));
        ImGuiText.Colored(Theme.Dim, "Long, steady strokes: a rushed one wanders. Keep the blade on the chalk line.");
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
        V.TakesPointer(new float2(line - travel * 4f, min.Y + Gui.S(10)), new float2(line + travel * 4f, min.Y + size.Y - Gui.S(10)));
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
            // A great log's pitman, under the beam on the box handle: pulling (lit) or waiting.
            if (s.Great && V.Sim.World.TryGet<Sim.Work.WorkSession>(s.Partner, out var helper) && helper is PitSession pit && helper.Partner == V.Me)
            {
                var feet = new float2(bx - Gui.S(18), bottom.Y + Gui.S(46));
                var head = new float2(feet.X, bottom.Y + Gui.S(10));
                var who = pit.Held || pit.Down > 0f ? Theme.Accent : Theme.Text;
                dl.AddCircleFilled(in head, Gui.S(6), Theme.U8(who), 12);
                dl.AddLine(head + Gui.S(0, 7), feet, Theme.U8(who), Gui.S(4));
                dl.AddLine(head + Gui.S(0, 7), bottom, Theme.U8(who), Gui.S(3));
            }
            // Sawdust falling from under the beam on the downstroke.
            if (s.Cutting)
                for (var k = 0; k < 6; k++)
                {
                    var f = (V.Time * 2.5f + k / 6f) % 1f;
                    var d = new float2(bx + MathF.Sin(k * 2.3f + V.Time * 3f) * Gui.S(8), b1.Y + f * Gui.S(40));
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
        V.End();
    }

    /// <summary>Under a great log's saw: how the pitman's pulling, or that there's none.</summary>
    internal void DrawPitmanLine(SawSession s)
    {
        if (!s.Great)
            return;
        if (!V.Sim.World.IsAlive(s.Partner) || !V.Sim.World.TryGet<Sim.Components.Creature>(s.Partner, out var pitman))
        {
            ImGuiText.Colored(Theme.Warn, "A great log: your stroke barely moves the saw alone. Call Help! from the quick-say wheel for a pitman below.");
            return;
        }
        var recent = s.LastPullStep >= 0 && s.StepsDone - s.LastPullStep < PitSession.WordSteps;
        if (recent && s.LastPull < 0f)
            ImGuiText.Colored(Theme.Bad, $"{pitman.Name} pulled against you: the saw binds!");
        else if (recent)
            ImGuiText.Colored(s.LastPull >= 0.6f ? Theme.Good : Theme.Text, $"{pitman.Name} pulled with your stroke{(s.LastPull < 0.4f ? ", short" : "")}.");
        else
            ImGuiText.Colored(Theme.Dim, $"{pitman.Name} is in the pit: each downstroke you start, they pull.");
    }

    /// <summary>The arrow at the stage it's at: sighting, shaving, the head, fletching.</summary>
    internal void DrawFletch(FletchSession s)
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
                ImGuiText.Colored(Theme.Dim, "·");
                ImGui.SameLine(0, 6);
            }
            ImGuiText.Colored(i < (int)s.Stage ? Theme.Good : i == (int)s.Stage ? Theme.Accent : Theme.Dim, names[i]);
        }
    }

    /// <summary>The shaft as it bows, sighted down its length, and a needle for the warp the eye reads.</summary>
    internal void DrawSight(FletchSession s)
    {
        V.Begin(s.Title, ("F", "hold to bend it straight"), ("Space", "it's true"), ("Esc", "stop"));
        ArrowSteps(s);
        ImGuiText.Colored(Theme.Dim, "Bend it back against the warp. Go too far and it warps the other way.");
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
        V.End();
    }

    /// <summary>
    /// The shaft side on: the wood still standing proud of its line, the gouges cut into it, and the
    /// knife where the hand has it, edge down. A bar says how much is off so far.
    /// </summary>
    internal void DrawShave(FletchSession s)
    {
        V.Begin(s.Title, ("Mouse|WASD", "move the knife"), ("Click|F", "hold to shave"), ("Space", "smooth enough: done"), ("Esc", "stop"));
        ArrowSteps(s);
        ImGuiText.Colored(Theme.Dim, "Take the lumps down to the line. Below it, the knife bites into the shaft.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(400, 170);
        ImGui.Dummy(in size);
        var area0 = new float2(min.X + Gui.S(10), min.Y + Gui.S(4));
        var area1 = new float2(min.X + size.X - Gui.S(10), min.Y + size.Y - Gui.S(24));
        V.TakesPointer(area0, area1);
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
        V.End();
    }

    /// <summary>The shaft's end with the iron head on it, tipped as crooked as it is; a guide line it should follow; how far it's on.</summary>
    internal void DrawHead(FletchSession s)
    {
        V.Begin(s.Title, ("F", "hold to push the head on"), ("A/D|Move", "keep it in line"), ("Esc", "stop"));
        ArrowSteps(s);
        ImGuiText.Colored(Theme.Dim, "Push the head onto the shaft. It tips as it goes: keep it on the line.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(380, 150);
        ImGui.Dummy(in size);
        V.TakesPointer(min, min + size);
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
        V.End();
    }

    /// <summary>The shaft end on, still; the feathers set on it; and a pointer round it where the next will go.</summary>
    internal void DrawFeathers(FletchSession s)
    {
        V.Begin(s.Title, ("Mouse|A/D", "point round the shaft"), ("Click|F", "set a feather"), ("Esc", "stop"));
        ArrowSteps(s);
        ImGuiText.Colored(Theme.Dim, "Three feathers, evenly round the shaft.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(220, 220);
        ImGui.Dummy(in size);
        V.TakesPointer(min, min + size);
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
        V.End();
    }


    /// <summary>The bow: on the tillering stick while it's being scraped, then being strung.</summary>
    internal void DrawBow(BowSession s)
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
    internal void DrawTiller(BowSession s)
    {
        V.Begin(s.Title, ("A/D|Mouse", "pick a section"), ("F", "hold to scrape it"), ("Space", "every bar's at its mark: string it"), ("Esc", "stop"));
        ImGuiText.Colored(Theme.Dim, "Each bar is wood still to come off. Scrape every one down to its mark, both limbs alike. Past the mark is a hinge.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(440, 250);
        ImGui.Dummy(in size);
        var left = min.X + Gui.S(20);
        var width = size.X - Gui.S(40);
        var gripY = min.Y + Gui.S(36);
        V.TakesPointer(new float2(left, min.Y), new float2(left + width, min.Y + size.Y));
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
        var work = s.Held ? MathF.Sin(V.Time * 18f) * Gui.S(6) : 0f;
        var sc0 = new float2(selectedMid.X - Gui.S(12) + work, selectedMid.Y - Gui.S(14));
        var sc1 = new float2(selectedMid.X + Gui.S(12) + work, selectedMid.Y - Gui.S(8));
        dl.AddRectFilled(in sc0, in sc1, Theme.U8(Theme.Iron), Gui.S(1), ImDrawFlags.None);
        if (s.Scraping)
            for (var k = 0; k < 6; k++)
            {
                var f = (V.Time * 2.2f + k / 6f) % 1f;
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
        V.End();
    }

    /// <summary>The bow braced against the floor, bending as it's held down; the string's loop at the nock; and a gauge of how far it's bent, the band where the loop catches and the strain past it.</summary>
    internal void DrawStringing(BowSession s)
    {
        V.Begin(s.Title, ("F", "hold to bend the bow"), ("Space", "slip the loop into the nock"), ("Esc", "stop"));
        ImGuiText.Colored(Theme.Dim, "Bend it into the green, then slip the string on. Bent into the red, the limbs strain.");
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
        V.End();
    }


    // Blows seen so far, and when the last one landed: chips fly for a moment after each.
    private int _fellBlows, _splitBlows;
    private float _fellBlowAt = -10f, _splitBlowAt = -10f;

    /// <summary>
    /// While you're felling: where the tree's aimed to come down, as long as it is tall, on the ground
    /// (your own session's aim, as you set it). Ready (ember) once the back cut's begun.
    /// </summary>
    internal AimLine? FellLine()
    {
        if (V.Session is not FellSession { Done: false } s || !V.Sim.World.IsAlive(s.Tree)
            || !V.Sim.World.TryGet<Sim.Components.Thing>(s.Tree, out var t) || t.Def.Felling is not { } props)
            return null;
        var from = Sim.Components.Position.CenterOf(t.Cell);
        var d = Dir(s.Aim);
        var to = from + new float3(d.X, d.Y, 0) * props.Height;
        return new AimLine(from, to, 1f, s.Backing, false);
    }

    // How high the felling axe was raised the frame before a blow: it's swung in from there.
    private float _fellRaisedLast;

    /// <summary>
    /// The axe seen from above, swung about the hands as a woodsman's is: at rest its bit lies in the
    /// cut at <paramref name="cutPoint"/>, the haft running back along <paramref name="side"/> to the
    /// hands; raised by <paramref name="swing"/> (0..1) it's swung back out away from the trunk
    /// (<paramref name="outward"/>), round its arc.
    /// </summary>
    internal void DrawAxeSwung(ImDrawListPtr dl, float2 cutPoint, float2 outward, float2 side, float swing)
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
    private float2 ScreenDir(float deg) => V.ScreenDir(deg);

    /// <summary>
    /// The trunk from above, turned as the camera looks (so "up the panel" is "up the screen"): the bark ring, the notch cut on the
    /// aimed side and the back cut opposite, the hinge between them, the tree's lean and where it's
    /// aimed to fall. Beside it, the axe raised as high as it's held.
    /// </summary>
    internal void DrawFell(FellSession s)
    {
        V.Aside(); // the tree and where it'll land stay in sight
        if (!s.AimSet)
            V.Begin(s.Title, ("A/D", "turn where it should fall"), ("F|Click", "hold, let go: the first blow sets the aim and starts the notch"), ("Esc", "stop"));
        else if (s.Backing)
            V.Begin(s.Title, ("F|Click", "hold, let go: thin the hinge till it goes"), ("Esc", "stop"));
        else
            V.Begin(s.Title, ("F|Click", "hold to raise the axe, let go to strike"), ("Space", "notch cut: go round for the back cut"), ("Esc", "stop"));
        ImGuiText.Colored(Theme.Dim, !s.AimSet
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
            V.End();
            return; // the axe comes out once it's aimed
        }

        // The axe, swung from the feller's hands beside the trunk: raised, it's swung back out round
        // its arc; let go, it whips round into the cut (a blink), then rests in it.
        var cutSide = s.Backing ? -aim : aim;
        if (s.Blows != _fellBlows)
            (_fellBlows, _fellBlowAt) = (s.Blows, V.Time);
        var since = V.Time - _fellBlowAt;
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
        V.End();
    }

    /// <summary>
    /// The trunk side on, crown to the left, its lengths marked: the cut in hand through it, the saw
    /// across it worked to and fro, the kerf closing on the blade past halfway (and a wedge in it, if
    /// one's been driven), and the logs already off it.
    /// </summary>
    internal void DrawBuck(BuckSession s)
    {
        V.Aside();
        V.Begin(s.Title,
            ("W/S|Mouse", "work the saw: the pull cuts"),
            s.CanKeepGreat ? ("Space", "keep the butt whole: a great log") : ("Space", "drive a wedge in the cut"),
            ("Esc", "stop"));
        ImGuiText.Colored(Theme.Dim, s.Bound ? "The saw's bound in the kerf! Drive a wedge in to free it."
            : s.Progress >= BuckSession.PinchFrom && !s.Wedged ? "Past halfway the trunk's weight closes the cut: wedge it before it binds."
            : "Steady pulls. A rushed saw binds sooner.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(620, 220);
        ImGui.Dummy(in size);
        V.TakesPointer(new float2(min.X, min.Y), new float2(min.X + size.X, min.Y + size.Y));

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
            ImGuiText.Colored(Theme.Good, "The butt's kept whole: a great log for the pit saw.");

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
                    var f = (V.Time * 2.5f + k / 6f) % 1f;
                    var d = new float2(x + MathF.Sin(k * 2.3f + V.Time * 3f) * Gui.S(10), t1.Y + f * Gui.S(30));
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
        V.End();
    }

    /// <summary>
    /// The same cut looking along the trunk, as the hew's side view: the round end, the saw across it
    /// going back and forth with the strokes at the foot of the cut, the cut's face dark above it, the
    /// wedge at the top.
    /// </summary>
    internal void DrawBuckAcross(ImDrawListPtr dl, BuckSession s, float2 min, float2 max)
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
                    var f = (V.Time * 3f + k / 10f) % 1f;
                    var side = k % 2 == 0 ? -1f : 1f;
                    var from = new float2(side < 0 ? inLeft : inRight, depthY);
                    var d = from + new float2(side * (Gui.S(6) + f * Gui.S(22)) * (0.6f + 0.4f * MathF.Sin(k * 1.9f)), -Gui.S(4) * f + Gui.S(36) * f * f);
                    dl.AddCircleFilled(in d, Gui.S(1.6f), Theme.U8(new float4(Theme.Parchment.XYZ, 1f - f)), 6);
                }
        }
        dl.AddText(new float2(min.X + Gui.S(4), min.Y + Gui.S(4)), Theme.U8(Theme.Dim), "along the trunk"u8);
    }

    /// <summary>On a great trunk: how the other end's pulling, or that nobody's there.</summary>
    internal void DrawOtherEnd(BuckSession s)
    {
        if (!s.GreatButt || s.KeptGreat)
            return;
        if (!V.Sim.World.IsAlive(s.Partner) || !V.Sim.World.TryGet<Sim.Components.Creature>(s.Partner, out var other))
        {
            ImGuiText.Colored(Theme.Warn, "A great trunk: alone the saw barely moves. Call Help! from the quick-say wheel for someone on the other end.");
            return;
        }
        var recent = s.LastPullStep >= 0 && s.StepsDone - s.LastPullStep < CrosscutSession.WordSteps;
        if (recent && s.LastPull < 0f)
            ImGuiText.Colored(Theme.Bad, $"{other.Name} pulled against you: the blade jerks in the cut!");
        else if (recent)
            ImGuiText.Colored(s.LastPull >= 0.6f ? Theme.Good : Theme.Text, $"{other.Name} pulled back with you{(s.LastPull < 0.4f ? ", short" : "")}.");
        else
            ImGuiText.Colored(Theme.Dim, $"{other.Name} has the other end: each pull you start, they pull back.");
    }

    /// <summary>The other end of a crosscut: when to pull, as the pitman's panel.</summary>
    internal void DrawCrosscut(CrosscutSession s)
    {
        V.Begin(s.Title, ("F|Hold the button", "pull back when they start a stroke; let go between"), ("Esc", "let go of the saw"));
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
        ImGuiText.Colored(Theme.Dim, $"With the stroke {s.Pulls}, against it {s.Binds}, missed {s.Missed}.");
        V.End();
    }

    /// <summary>
    /// A log stood on the block, end grain up: the checks it'll split along, the axe's edge over it
    /// where it's aimed, raised as high as it's held, and the splits done.
    /// </summary>
    internal void DrawSplit(SplitSession s)
    {
        V.Begin(s.Title,
            ("A/D|Mouse", "put the edge over a check"),
            ("F|Click", "hold to raise, let go to strike"),
            s.Stuck > 0 ? ("Space", "rock the stuck axe free") : ("Esc", "stop"));
        ImGuiText.Colored(s.Stuck > 0 ? Theme.Warn : Theme.Dim, s.Stuck > 0 ? "The axe is stuck in the wood. Rock it free."
            : s.LastBlow == SplitBlow.Bounced ? "It bounced off: strike on the check, and harder."
            : "Strike along the crack in the end grain, hard. Off the crack, the axe sticks.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(360, 240);
        ImGui.Dummy(in size);
        var c = new float2(min.X + Gui.S(160), min.Y + Gui.S(140));
        var r = Gui.S(85);
        V.TakesPointer(c - new float2(r, r), c + new float2(r, r));
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
            (_splitBlows, _splitBlowAt) = (s.Blows, V.Time);
        var since = V.Time - _splitBlowAt;
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
        V.End();
    }

    // The claw hammer at a wagon's sprung board (NailSession).

    private int _nailBlows;
    private float _nailBlowAt = -10f;

    /// <summary>
    /// The plank across the board, side on: the nails driven so far flush in it, the one in hand
    /// standing proud as far as it's still out (bent over, if it bent), and the hammer above it, its
    /// head where it'll land (A/D keep it over the nail) and raised as high as it's held.
    /// </summary>
    internal void DrawNail(NailSession s)
    {
        V.Begin(s.Title,
            ("F|Click", s.Set ? "hold to raise, let go to drive it" : "a light tap to set the nail"),
            ("A/D", "keep the hammer over the nail"),
            s.Bent ? ("Space", "draw the bent nail with the claw") : ("Esc", "stop"));
        var (tone, say) = s.Bent ? (Theme.Bad, Controls.Keys($"Bent over! Draw it with the claw (~Space~, {s.ClawLeft} more)."))
            : s.Settling > 0 ? (s.LastBlow == NailBlow.Bruised ? Theme.Warn : Theme.Good, s.LastBlow == NailBlow.Bruised ? "Flush, but you've bruised the board." : "Flush.")
            : !s.Set ? (Theme.Text, "Tap it first to set it: hit it hard now and it bends.")
            : s.LastBlow == NailBlow.Missed ? (Theme.Warn, "Missed the head: you've dented the plank.")
            : (Theme.Text, "Square on the head, and drive it in. Ease off as it nears flush.");
        ImGuiText.Colored(tone, say);
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
            (_nailBlows, _nailBlowAt) = (s.Blows, V.Time);
        var label = new float2(min.X + Gui.S(4), min.Y + Gui.S(4));
        dl.AddText(in label, Theme.U8(Theme.Dim), $"Nail {Math.Min(s.Nail + 1, s.Nails)} of {s.Nails}" + (s.BentNails > 0 ? $", {s.BentNails} bent" : ""));
        V.End();
    }

    /// <summary>
    /// The pitman's view, from above: the great log across the pit, the cut run along it, the long saw through the kerf
    /// moving with the top sawyer's stroke (read from their session), the two of them on its handles, sawdust as it
    /// cuts. Above it, what to do now: wait as they lift it, pull as the stroke starts.
    /// </summary>
    internal void DrawPit(PitSession s)
    {
        V.Begin(s.Title, ("F|Hold the button", "pull the saw down as the top sawyer starts a stroke; let go between"), ("Esc", "climb out"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 260);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        var top = V.Sim.World.TryGet<Sim.Work.WorkSession>(s.Partner, out var lead) ? lead as SawSession : null;
        var sawyer = s.LeadName;

        // From above: the great log across the pit's mouth, its cut run along it so far, the long saw upright through
        // the kerf, the top sawyer on the far side of it (on the log), you below in the pit, a hand each on its handles.
        var cx = (min.X + max.X) * 0.5f;
        var logY = min.Y + Gui.S(150);
        var logHalf = Gui.S(26);
        var log0 = new float2(min.X + Gui.S(40), logY - logHalf);
        var log1 = new float2(max.X - Gui.S(44), logY + logHalf);
        // The pit's mouth under the log, dark, the pitman's side.
        dl.AddRectFilled(new float2(log0.X + Gui.S(30), logY), new float2(log1.X - Gui.S(30), max.Y - Gui.S(8)), Theme.U8(Theme.Cellar), Gui.S(3), ImDrawFlags.None);
        dl.AddRectFilled(log0 - Gui.S(0, 4), log1 + Gui.S(0, 4), Theme.U8(new float4(0.3f, 0.2f, 0.13f, 1f)), Gui.S(12), ImDrawFlags.None); // the bark
        dl.AddRectFilled(log0, log1, Theme.U8(Theme.Barrel), Gui.S(10), ImDrawFlags.None);
        for (var g = 1; g <= 3; g++) // the grain along it
        {
            var gy = log0.Y + (log1.Y - log0.Y) * g / 4f + MathF.Sin(g * 1.3f) * Gui.S(2);
            dl.AddLine(new float2(log0.X + Gui.S(8), gy), new float2(log1.X - Gui.S(8), gy), Theme.U8(new float4(Theme.OakLight.XYZ, 0.35f)), Gui.S(1));
        }
        // How far it's cut: the kerf from the near end to where the saw is now, the chalk line on ahead.
        var progress = Math.Clamp(top?.Progress ?? 0.3f, 0f, 1f);
        var kerfX = log0.X + Gui.S(10) + (log1.X - log0.X - Gui.S(20)) * progress;
        dl.AddLine(new float2(log0.X + Gui.S(6), logY), new float2(kerfX, logY), Theme.U8(Theme.Inset), Gui.S(3));
        for (var x = kerfX + Gui.S(6); x < log1.X - Gui.S(8); x += Gui.S(10))
            dl.AddLine(new float2(x, logY), new float2(Math.Min(x + Gui.S(5), log1.X - Gui.S(8)), logY), Theme.U8(Theme.Parchment, 0.5f), Gui.S(1));

        // The saw, upright through the kerf: seen from above, its blade a short bar along the cut, its frame's two
        // handles out either side; the whole of it rides toward the top sawyer as they lift and toward you on the stroke.
        var stroke = top?.Stroke ?? 0.5f;
        var shift = (stroke - 0.5f) * Gui.S(36);
        var tiller = new float2(kerfX, log0.Y - Gui.S(30) + shift);
        var box = new float2(kerfX, log1.Y + Gui.S(30) + shift);
        dl.AddLine(tiller, box, Theme.U8(Theme.Iron), Gui.S(4));
        dl.AddRectFilled(new float2(kerfX - Gui.S(2), logY - Gui.S(18)), new float2(kerfX + Gui.S(2), logY + Gui.S(18)), Theme.U8(Theme.Iron), 0f, ImDrawFlags.None);
        dl.AddRectFilled(tiller - Gui.S(18, 3), tiller + Gui.S(18, 3), Theme.U8(Theme.Oak), Gui.S(3), ImDrawFlags.None);
        dl.AddRectFilled(box - Gui.S(14, 3), box + Gui.S(14, 3), Theme.U8(Theme.Oak), Gui.S(3), ImDrawFlags.None);

        // The two of them from above: shoulders and a head, both arms to their handle; each leans the way they work.
        void Figure(float2 hands, float2 at, float4 colour, bool lit)
        {
            var c = Theme.U8(colour);
            dl.AddLine(new float2(at.X - Gui.S(14), at.Y), hands - Gui.S(8, 0), c, Gui.S(4));
            dl.AddLine(new float2(at.X + Gui.S(14), at.Y), hands + Gui.S(8, 0), c, Gui.S(4));
            dl.AddEllipseFilled(at, Gui.S(17, 8), c, 0f, 20);
            dl.AddCircleFilled(at, Gui.S(8), Theme.U8(new float4(colour.XYZ * 0.75f, 1f)), 14);
            if (lit)
                dl.AddEllipse(at, Gui.S(22, 13), Theme.U8(Theme.Good, 0.5f), 0f, 20, Gui.S(1.5f));
        }
        var lifting = top != null && !top.Cutting && top.Pace > 0f;
        Figure(tiller, new float2(kerfX, tiller.Y - Gui.S(26)), Theme.Parchment, top is { Cutting: true });
        var pulling = s.Held || s.Down > 0f;
        Figure(box, new float2(kerfX, box.Y + Gui.S(24)), pulling ? Theme.Accent : Theme.Text, pulling);
        if (lifting) // which way the saw's going: an arrow beside it
            dl.AddTriangleFilled(new float2(kerfX + Gui.S(28), tiller.Y + Gui.S(4)), new float2(kerfX + Gui.S(22), tiller.Y + Gui.S(14)), new float2(kerfX + Gui.S(34), tiller.Y + Gui.S(14)), Theme.U8(Theme.Dim));
        else if (top is { Cutting: true })
            dl.AddTriangleFilled(new float2(kerfX + Gui.S(28), box.Y - Gui.S(4)), new float2(kerfX + Gui.S(22), box.Y - Gui.S(14)), new float2(kerfX + Gui.S(34), box.Y - Gui.S(14)), Theme.U8(Theme.Good));
        // Sawdust spilling from the kerf as it cuts.
        if (top is { Cutting: true })
            for (var k = 0; k < 8; k++)
            {
                var f = (V.Time * 2.5f + k / 8f) % 1f;
                var d = new float2(kerfX + MathF.Sin(k * 2.3f + V.Time * 3f) * Gui.S(14), logY + f * Gui.S(40));
                dl.AddCircleFilled(in d, Gui.S(1.6f), Theme.U8(new float4(Theme.Parchment.XYZ, 1f - f)), 6);
            }
        var groundY = min.Y + Gui.S(60);

        // What to do now, across the top.
        var centre = new float2((min.X + max.X) * 0.5f, min.Y + Gui.S(10));
        string word;
        float4 colour;
        var big = false;
        if (s.CallOpen)
        {
            (word, colour, big) = ("Pull!", Theme.Good, true);
            var left = 1f - s.CallAge / (float)Math.Max(1, s.Window);
            var b0 = new float2(min.X + Gui.S(16), min.Y + Gui.S(44));
            var b1 = new float2(min.X + Gui.S(120), min.Y + Gui.S(51));
            Gui.Trough(dl, b0, b1);
            dl.AddRectFilled(b0, new float2(b0.X + (b1.X - b0.X) * Math.Clamp(left, 0f, 1f), b1.Y), Theme.U8(left > 0.33f ? Theme.Good : Theme.Warn, 0.85f), 0f, ImDrawFlags.None);
        }
        else if (s.Held && s.Down >= 1f && s.LastWas == PitResult.WithIt)
            (word, colour) = (s.LastGrade >= 0.7f ? "With the stroke! Now let go: it goes back up" : "Short, or late. Let go: it goes back up", s.LastGrade >= 0.7f ? Theme.Good : Theme.Warn);
        else if (s.LastStep >= 0 && s.StepsDone - s.LastStep < PitSession.WordSteps)
            (word, colour) = s.LastWas switch
            {
                PitResult.WithIt when s.LastGrade >= 0.7f => ("With the stroke!", Theme.Good),
                PitResult.WithIt => ("Short, or late", Theme.Warn),
                PitResult.Bound => ("Against the saw: it binds!", Theme.Bad),
                _ => ("Missed the stroke", Theme.Warn),
            };
        else if (top != null && top.Stroke < 0.25f && !top.Cutting)
            (word, colour) = ($"{sawyer}'s at the top: the stroke's coming", Theme.Accent);
        else if (top != null && !top.Cutting && top.Pace > 0f)
            (word, colour) = ($"{sawyer}'s lifting it: let it go up", Theme.Dim);
        else
            (word, colour) = ($"Wait for {sawyer} to start the stroke", Theme.Dim);
        if (big)
            using (Gui.HeaderFont())
            {
                var ts = ImGui.CalcTextSize(word);
                dl.AddText(new float2(min.X + Gui.S(14), centre.Y), Theme.U8(colour), word);
            }
        else
        {
            var ts = ImGui.CalcTextSize(word);
            dl.AddText(new float2(min.X + Gui.S(14), centre.Y), Theme.U8(colour), word);
        }

        // Your pull, up the right edge.
        var g0 = new float2(max.X - Gui.S(24), groundY);
        var g1 = new float2(max.X - Gui.S(14), max.Y - Gui.S(12));
        Gui.Trough(dl, g0, g1);
        var y = g0.Y + (g1.Y - g0.Y) * Math.Clamp(s.Down, 0f, 1f);
        dl.AddRectFilled(new float2(g0.X - Gui.S(4), y - Gui.S(3)), new float2(g1.X + Gui.S(4), y + Gui.S(3)), Theme.U8(s.Down >= 1f ? Theme.Good : Theme.Text), Gui.S(1), ImDrawFlags.None);
        ImGuiText.Colored(Theme.Dim, $"With the stroke {s.Pulls}, against it {s.Binds}, missed {s.Missed}.");
        V.End();
    }

    /// <summary>
    /// The lifter's view: the wagon side on, its near end held up (or down), tipping with its sway; the carpenter's
    /// mallet over the hub as they hold it (read from their session); a level on top with the band to keep it in.
    /// </summary>
    internal void DrawLift(LiftSession s)
    {
        V.Begin(s.Title, ("F|Hold the button", "keep the wagon up"), ("A/D", "lean against the tip to hold it level"), ("Esc", "set it down and step away"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 200);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        var join = V.Sim.World.TryGet<Sim.Work.WorkSession>(s.Partner, out var lead) ? lead as JoinSession : null;
        var carpenter = s.LeadName;

        // The ground, and the wagon on it: pivoting on its far wheel, the near end up while you hold it, rocked by the tilt.
        var groundY = max.Y - Gui.S(22);
        dl.AddRectFilled(new float2(min.X + Gui.S(4), groundY), new float2(max.X - Gui.S(4), max.Y - Gui.S(4)), Theme.U8(Theme.Cellar), Gui.S(3), ImDrawFlags.None);
        var pivot = new float2(max.X - Gui.S(110), groundY - Gui.S(22));
        var length = Gui.S(200);
        var lift = s.Up ? 0.16f : 0f;
        var angle = lift + s.Tilt * 0.12f;  // up at the near end; the tip rocks it
        var dir = new float2(-MathF.Cos(angle), -MathF.Sin(angle)); // from the far wheel toward the near end
        var up = new float2(dir.Y, -dir.X);
        if (up.Y > 0)
            up = -up;
        var near = pivot + dir * length;
        float2 P(float along, float above) => pivot + dir * along + up * above;
        var body = Theme.U8(Theme.Oak);
        dl.AddQuadFilled(P(-Gui.S(14), Gui.S(10)), P(length + Gui.S(10), Gui.S(10)), P(length + Gui.S(10), Gui.S(46)), P(-Gui.S(14), Gui.S(46)), body);
        dl.AddCircleFilled(in pivot, Gui.S(20), Theme.U8(Theme.Barrel), 20);                // the far wheel, on the ground
        dl.AddCircle(in pivot, Gui.S(20), Theme.U8(Theme.OakLight), 20, Gui.S(2));
        // The near axle's end: the hub going on (seated so far by the carpenter's blows).
        var hub = P(length - Gui.S(16), 0f);
        dl.AddCircleFilled(in hub, Gui.S(9), Theme.U8(Theme.Iron), 16);
        if (join != null)
        {
            var seat = Math.Clamp(join.Seat, 0f, 1f);
            dl.AddCircle(in hub, Gui.S(12), Theme.U8(Theme.Good, 0.4f + 0.6f * seat), 16, Gui.S(2f + 3f * seat));
            // The carpenter's mallet over the hub, raised as they raise it.
            V.DrawToolSwing(dl, "ui/work/hammer", 26f / 32f, join.Raised, hub - Gui.S(0, 10), MathF.Max(1f, MathF.Round(2.5f * Gui.Scale)), Gui.White);
        }
        // You at the near end, under its weight: lit while you're holding it.
        var you = Theme.U8(s.Spent ? Theme.Bad : s.Up ? Theme.Accent : Theme.Text);
        var hands = P(length + Gui.S(6), Gui.S(14));
        var feet = new float2(near.X - Gui.S(26), groundY);
        var head = new float2(feet.X, Math.Min(hands.Y, groundY - Gui.S(60)) - Gui.S(8));
        dl.AddCircleFilled(in head, Gui.S(7), you, 12);
        dl.AddLine(head + Gui.S(0, 8), feet, you, Gui.S(5));
        dl.AddLine(head + Gui.S(0, 10), hands, you, Gui.S(3));

        // The level, across the top: the bubble's the wagon's lean, the band is level enough.
        var t0 = new float2(min.X + Gui.S(20), min.Y + Gui.S(34));
        var t1 = new float2(max.X - Gui.S(20), min.Y + Gui.S(48));
        Gui.Trough(dl, t0, t1);
        var mid = (t0.X + t1.X) * 0.5f;
        var half = (t1.X - t0.X) * 0.5f;
        dl.AddRectFilled(new float2(mid - half * s.BandWidth, t0.Y), new float2(mid + half * s.BandWidth, t1.Y), Theme.U8(Theme.Good, 0.35f), 0f, ImDrawFlags.None);
        var bubble = new float2(mid + half * s.Tilt, (t0.Y + t1.Y) * 0.5f);
        dl.AddCircleFilled(in bubble, Gui.S(7), Theme.U8(!s.Up ? Theme.Dim : s.Level ? Theme.Good : Theme.Warn), 20);

        // What to do now.
        var (word, colour) = s.LastStep >= 0 && s.StepsDone - s.LastStep < LiftSession.WordSteps
            ? s.LastLevel ? ("Level: the blow went home", Theme.Good) : ("It tipped: the hub jolted!", Theme.Bad)
            : s.Spent ? ("Your arms gave: set it down and rest them", Theme.Bad)
            : !s.Up ? ("Lift it (hold F)", Theme.Text)
            : !s.Level ? (s.Tilt > 0f ? "It's tipping: lean back with A" : "It's tipping: lean back with D", Theme.Warn)
            : join is { Raised: > 0.1f } ? ($"Steady: {carpenter}'s mallet is up", Theme.Accent)
            : ($"Level. Hold it for {carpenter}'s blow", Theme.Dim);
        var ts = ImGui.CalcTextSize(word);
        dl.AddText(new float2((min.X + max.X) * 0.5f - ts.X * 0.5f, min.Y + Gui.S(10)), Theme.U8(colour), word);

        // The arms: they tire holding it up and rest with it down.
        var a0 = new float2(min.X + Gui.S(14), min.Y + Gui.S(60));
        var a1 = new float2(min.X + Gui.S(24), groundY - Gui.S(8));
        Gui.Trough(dl, a0, a1);
        var left = 1f - s.Strain;
        dl.AddRectFilled(new float2(a0.X, a1.Y - (a1.Y - a0.Y) * left), a1, Theme.U8(left < 0.2f ? Theme.Bad : left < 0.5f ? Theme.Warn : Theme.Good, 0.85f), 0f, ImDrawFlags.None);
        ImGuiText.Colored(Theme.Dim, $"Level for {s.Holds} blows, tipped for {s.Jolts}. Arms: the bar on the left. Set it down between blows to rest them.");
        V.End();
    }
}
