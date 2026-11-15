using Brutal.ImGuiApi;
using Narsil.Rendering;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Sim.Work;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The plainer mini-games drawn as small scenes over their gauges, from the ui/work/* props
// (tools/work_ui_art.py): the pot and its salt, the bed being made, the arm being bound, the hand
// drawing a hurt in, the blade on the stone, the probe along a strongbox's seam. Everything moves
// from the session in hand and what you've done; nothing here reads the sim, and nothing shows
// what the character couldn't see (a wound is a glow you feel, a trap is never drawn).
public sealed partial class VillageGame
{
    private readonly record struct Mote(float2 From, float2 To, float Born, float Life);

    /// <summary>What a scene remembers between frames: whose session it is, counts seen, and its motes in flight.</summary>
    private sealed class SceneState
    {
        public long Session = long.MinValue;
        public int Count;
        public float Progress;
        public readonly List<Mote> Motes = [];
        /// <summary>When each of a scene's marks first showed (a warmth sensed), for a bloom that dies away.</summary>
        public readonly List<float> Since = [];
    }

    private readonly Dictionary<string, SceneState> _scenes = [];

    /// <summary>A scene's box under the hints, inset and lit like the anvil's; its pixel scale with it.</summary>
    private (ImDrawListPtr Dl, float2 Min, float2 Max, float Px) WorkScene(float height, float4? light = null)
    {
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, height);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        if (light is { } l)
        {
            var top = Theme.U8(l, 0f);
            var bottom = Theme.U8(l, 0.2f);
            var gMin = new float2(min.X + Gui.S(4), min.Y + size.Y * 0.4f);
            var gMax = max - Gui.S(4, 4);
            dl.AddRectFilledMultiColor(in gMin, in gMax, top, top, bottom, bottom);
        }
        return (dl, min, max, MathF.Max(1f, MathF.Round(3f * Gui.Scale)));
    }

    /// <summary>A prop at <paramref name="at"/> (its bottom middle), at the scene's pixel scale; its box, or null if the art's missing.</summary>
    private static (float2 Min, float2 Max)? Prop(ImDrawListPtr dl, string name, float2 at, float px, ImColor8? tint = null)
    {
        if (!Gui.TrySprite("ui/work/" + name, out var sprite))
            return null;
        var size = new float2(sprite.PixelSize.X, sprite.PixelSize.Y) * px;
        var min = new float2(MathF.Round(at.X - size.X * 0.5f), MathF.Round(at.Y - size.Y));
        Gui.Image(dl, sprite, min, min + size, tint ?? Gui.White);
        return (min, min + size);
    }

    /// <summary>A prop turned by <paramref name="degrees"/> about the point <paramref name="pivot"/>, which sits at <paramref name="anchor"/> (0..1 of the sprite).</summary>
    private static void PropTurned(ImDrawListPtr dl, string name, float2 pivot, float2 anchor, float degrees, float px, ImColor8? tint = null)
    {
        if (!Gui.TrySprite("ui/work/" + name, out var sprite))
            return;
        var size = new float2(sprite.PixelSize.X, sprite.PixelSize.Y) * px;
        var a = degrees * MathF.PI / 180f;
        var right = new float2(MathF.Cos(a), MathF.Sin(a)) * size.X;
        var down = new float2(-MathF.Sin(a), MathF.Cos(a)) * size.Y;
        var tl = pivot - right * anchor.X - down * anchor.Y;
        Gui.ImageQuad(dl, sprite, tl, tl + right, tl + right + down, tl + down, tint ?? Gui.White);
    }

    /// <summary>A scene's state for this session: a new session forgets the last one's counts and motes.</summary>
    private SceneState SceneFor(string scene, long session, int count = 0, float progress = 0f)
    {
        if (!_scenes.TryGetValue(scene, out var st))
            _scenes[scene] = st = new SceneState();
        if (st.Session != session)
        {
            st.Session = session;
            st.Count = count;
            st.Progress = progress;
            st.Motes.Clear();
            st.Since.Clear();
        }
        return st;
    }

    private void DrawMotes(ImDrawListPtr dl, SceneState st, float4 colour, float size)
    {
        st.Motes.RemoveAll(m => _time - m.Born > m.Life);
        foreach (var m in st.Motes)
        {
            var t = (_time - m.Born) / m.Life;
            var at = m.From + (m.To - m.From) * t + new float2(0, -MathF.Sin(t * MathF.PI) * Gui.S(6));
            dl.AddRectFilled(at - new float2(size, size) * 0.5f, at + new float2(size, size) * 0.5f, Theme.U8(colour, 1f - t * 0.6f), 0f, ImDrawFlags.None);
        }
    }

    // ---------------- The kitchen: seasoning ----------------

    /// <summary>The pot steaming, the salt crock by it: each pinch falls from one into the other.</summary>
    private void SeasonScene(long session, int pinches)
    {
        var st = SceneFor("season", session, pinches);
        var (dl, min, max, px) = WorkScene(96, Theme.Accent);
        var floor = max.Y - Gui.S(8);
        var pot = Prop(dl, "stew_pot", new float2(min.X + (max.X - min.X) * 0.45f, floor), px);
        var crock = Prop(dl, "salt_crock", new float2(max.X - Gui.S(70), floor), px);
        if (pot is not { } p || crock is not { } c)
            return;
        var rim = new float2((p.Min.X + p.Max.X) * 0.5f, p.Min.Y + 6 * px);
        for (var k = 0; k < 3; k++) // steam off the stew
        {
            var t = (_time * 0.5f + k / 3f) % 1f;
            var at = rim + new float2((k - 1) * Gui.S(18) + MathF.Sin(_time * 1.6f + k) * Gui.S(4), -t * Gui.S(40));
            dl.AddCircleFilled(in at, Gui.S(4f + 5f * t), Gui.U8(new float4(0.92f, 0.9f, 0.86f, 1f), 0.22f * (1f - t)), 12);
        }
        if (pinches > st.Count)
        {
            var from = new float2((c.Min.X + c.Max.X) * 0.5f, c.Min.Y);
            for (var g = 0; g < 7; g++)
                st.Motes.Add(new Mote(from + new float2(g % 3 * px, 0), rim + new float2((g - 3) * px * 1.5f, 0), _time + g * 0.03f, 0.55f));
        }
        st.Count = pinches;
        DrawMotes(dl, st, new float4(0.95f, 0.95f, 0.92f, 1f), px);
    }

    // ---------------- The inn: making a bed ----------------

    /// <summary>The bed, the sheet as far as it's got (rumpled, held up, laid, tucked) and the hands coming round.</summary>
    private void MakeBedScene(int movements, float sweep, bool onCue)
    {
        var (dl, min, max, px) = WorkScene(100);
        var floor = max.Y - Gui.S(8);
        var midX = (min.X + max.X) * 0.5f;
        if (Prop(dl, "bed", new float2(midX, floor), px) is not { } bed)
            return;
        var mattress = new float2(bed.Min.X + 32 * px, bed.Min.Y + 12 * px); // the mattress's top middle
        switch (movements)
        {
            case 0: // yesterday's sheet, rumpled
                PropTurned(dl, "sheet", mattress + new float2(-4 * px, 0), new float2(0.5f, 1f), -6f, px, Gui.U8(Theme.Dim));
                PropTurned(dl, "sheet", mattress + new float2(10 * px, -2 * px), new float2(0.5f, 1f), 9f, px * 0.6f, Gui.U8(Theme.Dim));
                break;
            case 1: // stripped: the clean sheet shaken out over it
                PropTurned(dl, "sheet", mattress + new float2(0, -8 * px - MathF.Sin(_time * 5f) * px), new float2(0.5f, 1f), MathF.Sin(_time * 3f) * 4f, px);
                break;
            case 2: // laid on, hanging over the foot
                Prop(dl, "sheet", mattress + new float2(6 * px, 1 * px), px);
                break;
            default: // tucked in square
                Prop(dl, "sheet", mattress + new float2(2 * px, 1 * px), px);
                break;
        }
        var handX = bed.Min.X + (bed.Max.X - bed.Min.X) * (0.15f + 0.7f * sweep);
        Prop(dl, "hand", new float2(handX, mattress.Y - 6 * px), px, onCue ? Gui.White : Gui.U8(new float4(0.85f, 0.8f, 0.75f, 1f)));
    }

    // ---------------- The healer: binding and drawing ----------------

    /// <summary>The forearm with the turns of bandage on it so far, and the one being pulled (tinted by how tight).</summary>
    private void BandageScene(int turns, int turnsDone, float tension, float low, float high)
    {
        var (dl, min, max, px) = WorkScene(70);
        var midY = (min.Y + max.Y) * 0.5f;
        if (Prop(dl, "forearm", new float2((min.X + max.X) * 0.5f, midY + 8 * px), px) is not { } arm)
            return;
        var first = arm.Min.X + 10 * px;
        var step = (36 * px) / Math.Max(1, turns);
        for (var i = 0; i <= Math.Min(turnsDone, turns - 1); i++)
        {
            var at = new float2(first + i * step, arm.Max.Y + px);
            if (i < turnsDone)
                Prop(dl, "wrap", at, px);
            else
            {
                var tint = tension >= BandageSession.Crush ? Theme.Bad : tension >= low && tension <= high ? Theme.Good : Theme.Text;
                var pull = (1f - tension) * 3f * px; // slack, it sits loose and lower
                Prop(dl, "wrap", at + new float2(0, pull), px, Gui.U8(tint));
            }
        }
    }

    // ---------------- The smith: the edge ----------------

    /// <summary>The blade on the stone at the angle you hold it, warming as you grind; sparks as the edge comes back.</summary>
    private void SharpenScene(long session, bool held, float angle, float heat, float progress, bool onWheel = false)
    {
        if (onWheel)
        {
            GrindScene(session, held, angle, heat, progress);
            return;
        }
        var state = SceneFor("sharpen", session, 0, progress);
        var (dl, min, max, px) = WorkScene(84);
        var floor = max.Y - Gui.S(8);
        var midX = (min.X + max.X) * 0.5f;
        var stone = Prop(dl, "whetstone", new float2(midX, floor), px);
        if (stone is not { } st)
            return;
        var stroke = held ? MathF.Sin(_time * 9f) * 8f * px : 0f;
        var contact = new float2(midX + stroke, st.Min.Y + px);
        var tilt = (angle - 0.5f) * 50f; // the band's middle lies flat on the stone
        var warm = Math.Clamp(heat, 0f, 1f);
        var tint = new float4(1f, 1f - 0.45f * warm, 1f - 0.75f * warm, 1f);
        PropTurned(dl, "blade", contact, new float2(0.55f, 0.85f), -14f + tilt, px, Gui.U8(tint));
        if (progress > state.Progress + 0.002f)
        {
            for (var k = 0; k < 3; k++)
                state.Motes.Add(new Mote(contact, contact + new float2((k - 1) * Gui.S(14) - stroke * 0.5f, -Gui.S(10 + 6 * k)), _time, 0.35f));
            state.Progress = progress;
        }
        DrawMotes(dl, state, Theme.Accent, px);
    }

    /// <summary>How far the grindstone in the scene has turned (degrees), and when it was last drawn.</summary>
    private float _wheelTurn, _wheelDrawn;

    /// <summary>Degrees a second the wheel turns cranked (while you bear down) and idling between.</summary>
    private const float WheelCranked = 420f, WheelIdle = 60f;

    /// <summary>
    /// The smithy's grindstone side on, turning in its trough: the blade laid across the top of the
    /// wheel at the angle you hold it, sparks thrown off the rim the way it turns.
    /// </summary>
    private void GrindScene(long session, bool held, float angle, float heat, float progress)
    {
        var state = SceneFor("grind", session, 0, progress);
        var (dl, min, max, px) = WorkScene(136);
        var floor = max.Y - Gui.S(8);
        var midX = (min.X + max.X) * 0.5f;
        var dt = Math.Clamp(_time - _wheelDrawn, 0f, 0.1f);
        _wheelDrawn = _time;
        _wheelTurn = (_wheelTurn + dt * (held ? WheelCranked : WheelIdle)) % 360f;

        Prop(dl, "grind_posts", new float2(midX, floor), px);
        if (!Gui.TrySprite("ui/work/grindwheel", out var wheel))
            return;
        var radius = wheel.PixelSize.Y * 0.5f * px;
        var hub = new float2(midX, floor - 4 * px - radius); // its foot down in the trough
        PropTurned(dl, "grindwheel", hub, new float2(0.5f, 0.5f), _wheelTurn, px);
        Prop(dl, "grind_trough", new float2(midX, floor), px);

        // The blade rides the top of the rim, a little toward you, at the angle you hold it.
        var contact = hub + new float2(-2 * px, -radius + px);
        var tilt = (angle - 0.5f) * 50f;
        var warm = Math.Clamp(heat, 0f, 1f);
        var tint = new float4(1f, 1f - 0.45f * warm, 1f - 0.75f * warm, 1f);
        var stroke = held ? MathF.Sin(_time * 7f) * 3f * px : 0f;
        PropTurned(dl, "blade", contact + new float2(stroke, 0), new float2(0.55f, 0.85f), -8f + tilt, px, Gui.U8(tint));
        // Sparks leave the rim along its turn (clockwise: over the top and away to the right), whenever you bear down.
        if (held && (progress > state.Progress + 0.001f || ((int)(_time * 20f) & 1) == 0))
        {
            for (var k = 0; k < 2; k++)
            {
                var spread = (k + (_time * 13f % 1f)) * 0.5f;
                state.Motes.Add(new Mote(contact, contact + new float2(Gui.S(50 + 40 * spread), Gui.S(10 + 30 * spread)), _time, 0.3f));
            }
            state.Progress = progress;
        }
        DrawMotes(dl, state, WheelSpark, px);
    }

    /// <summary>Sparks off the wheel: hot and pale, so they show against a blade warming orange.</summary>
    private static readonly float4 WheelSpark = new(1f, 0.92f, 0.6f, 1f);

    // ---------------- The tinker: the strongbox's seam ----------------

    /// <summary>The strongbox's front, the probe along its seam from hasp to hinge, pressed in as hard as you press it.</summary>
    private void ProbeScene(float probe, float feel, float pressure)
    {
        var (dl, min, max, px) = WorkScene(150);
        var floor = max.Y - Gui.S(6);
        if (Prop(dl, "strongbox", new float2((min.X + max.X) * 0.5f, floor), px) is not { } box)
            return;
        var seamY = box.Min.Y + 10 * px;
        var x = box.Min.X + 6 * px + probe * (box.Max.X - box.Min.X - 12 * px);
        var shake = feel * 1.5f * px * MathF.Sin(_time * 50f);
        var depth = pressure * 3f * px;
        Prop(dl, "probe", new float2(x + shake, seamY + depth + px), px);
    }

    /// <summary>The curio on its cloth and the loupe moved along it; a glint where the eye catches.</summary>
    private void LoupeScene(float at, float caught, bool held)
    {
        var (dl, min, max, px) = WorkScene(120);
        if (Prop(dl, "curio", new float2((min.X + max.X) * 0.5f, max.Y - Gui.S(40)), px) is not { } piece)
            return;
        var x = piece.Min.X + 4 * px + at * (piece.Max.X - piece.Min.X - 8 * px);
        var lens = new float2(x, piece.Min.Y + 9 * px);
        var still = held ? 0f : MathF.Sin(_time * 3f) * px;
        Prop(dl, "loupe", new float2(lens.X, lens.Y + 20 * px + still), px);
        if (caught > 0.3f) // what catches the eye: a glint in the glass
        {
            var g = Gui.U8(new float4(1f, 0.97f, 0.85f, 1f), Math.Clamp((caught - 0.3f) * 1.6f, 0f, 1f));
            var r = px * (2f + 2f * caught) * (0.8f + 0.2f * MathF.Sin(_time * 8f));
            dl.AddLine(lens - new float2(r, 0), lens + new float2(r, 0), g, px);
            dl.AddLine(lens - new float2(0, r), lens + new float2(0, r), g, px);
        }
    }

    /// <summary>
    /// The wheel on its stand, side on: the spoke at the key (the right) picked out, the rim
    /// swinging out and in at the gauge by what it reads while it spins, and the last spin's chalk
    /// on the rim at each spoke, outside it for out and inside for in.
    /// </summary>
    private void TrueScene(TrueSession s) => TrueScene(s.Turn, s.Spinning, s.Gauge, s.BandWidth, s.Selected, s.Chalk, s.Chalked, s.Worked);

    private void TrueScene(float turn, bool spinning, float reading, float band, int selected, float[] chalk, bool[] chalked, int[] worked)
    {
        var (dl, min, max, px) = WorkScene(130);
        var centre = new float2((min.X + max.X) * 0.5f, min.Y + Gui.S(62));
        var r = Gui.S(52);
        var oak = Theme.U8(Theme.OakLight);
        var dark = Theme.U8(Theme.Oak);
        var gauge = spinning ? reading : 0f;
        foreach (var dx in new[] { -1f, 1f }) // the stand's legs
        {
            var top = centre + new float2(dx * Gui.S(6), 0);
            var foot = new float2(centre.X + dx * Gui.S(40), max.Y - Gui.S(6));
            dl.AddLine(in top, in foot, dark, Gui.S(6));
        }
        var lean = gauge * Gui.S(30); // the rim's wobble, drawn as the wheel's right side swinging
        for (var i = 0; i < TrueSession.Spokes; i++)
        {
            var a = (i / (float)TrueSession.Spokes - turn) * MathF.Tau;
            var dir = new float2(MathF.Cos(a), MathF.Sin(a));
            var tip = centre + dir * r + new float2(MathF.Max(0f, dir.X) * lean, 0);
            var picked = !spinning && i == selected;
            dl.AddLine(in centre, in tip, picked ? Theme.U8(Theme.Accent) : oak, Gui.S(picked ? 4 : 3));
        }
        for (var k = 0; k < 32; k++) // the rim, its right side out of true by the reading
        {
            float2 At(int n)
            {
                var a = n / 32f * MathF.Tau;
                var dir = new float2(MathF.Cos(a), MathF.Sin(a));
                return centre + dir * r + new float2(MathF.Max(0f, dir.X) * lean, 0);
            }
            var p0 = At(k);
            var p1 = At(k + 1);
            dl.AddLine(in p0, in p1, dark, Gui.S(7));
            dl.AddLine(in p0, in p1, oak, Gui.S(4));
        }
        for (var i = 0; i < TrueSession.Spokes; i++) // the chalk where each spoke meets the rim
        {
            if (i >= chalked.Length || !chalked[i])
                continue;
            var a = (i / (float)TrueSession.Spokes - turn) * MathF.Tau;
            var dir = new float2(MathF.Cos(a), MathF.Sin(a));
            var rim = centre + dir * r + new float2(MathF.Max(0f, dir.X) * lean, 0);
            var off = MathF.Abs(chalk[i]) <= band ? 0f : MathF.Sign(chalk[i]);
            var c = Gui.U8(new float4(0.95f, 0.93f, 0.86f, worked[i] != 0 ? 0.45f : 1f));
            if (off == 0f)
                dl.AddCircleFilled(in rim, Gui.S(2), c, 8);
            else
            {
                var a0 = rim + dir * off * Gui.S(5);
                var a1 = rim + dir * off * Gui.S(12);
                dl.AddLine(in a0, in a1, c, Gui.S(3));
            }
        }
        dl.AddCircleFilled(in centre, Gui.S(9), dark, 16);
        dl.AddCircleFilled(in centre, Gui.S(5), Theme.U8(Theme.Iron), 12);
        // The gauge's finger at the rim, and where true lies; swung clear while the key's at work.
        var gx = centre.X + r + Gui.S(14);
        var trueA = new float2(gx, centre.Y - Gui.S(16));
        var trueB = new float2(gx, centre.Y + Gui.S(16));
        dl.AddLine(in trueA, in trueB, Theme.U8(spinning && MathF.Abs(gauge) <= band ? Theme.Good : Theme.Dim), Gui.S(2));
        var finger = new float2(centre.X + r + lean + Gui.S(spinning ? 4 : 12), centre.Y);
        var post = new float2(gx + Gui.S(18), centre.Y);
        dl.AddLine(in finger, in post, Theme.U8(Theme.Iron), Gui.S(3));
    }

    /// <summary>A wound as the hands know it, for the scene: its kind's look only once it's read, festering only once found.</summary>
    private readonly record struct HandsSpot(float At, float Gravity, Sim.Defs.DrawPattern Pattern, float Sensed, float Found, float Drawn, bool Festering);

    /// <summary>What the laying-on-of-hands scene draws: the session as the empath has it.</summary>
    private readonly record struct HandsView(long Session, IReadOnlyList<string> Parts, IReadOnlyList<HandsSpot> Spots, float Hands, bool Held,
        bool Linked, bool Calm, float Strain, float Pulse, int Under, int Step, int LastDrawn, int LastDrawnAt, int LastFlinch, float Feel);

    private static HandsView HandsViewOf(HandsOnSession s) => new(s.StartTick, s.Parts,
        [.. s.Spots.Select(p => new HandsSpot(p.At, p.Gravity, p.Pattern, p.Sensed, p.Found, p.Drawn, s.FoundFestering(p)))],
        s.Hands01, s.Held, s.Linked, s.Link.Calm, s.Link.Strain, s.Pulse, s.Under, s.StepsDone, s.LastDrawn, s.LastDrawnAt, s.LastFlinch, s.Feel);

    /// <summary>Frost's pale blue, venom's sick green, a burn's scorch: the marks' own colours.</summary>
    private static readonly float4 FrostBlue = new(0.72f, 0.88f, 1f, 1f), VenomGreen = new(0.55f, 0.8f, 0.35f, 1f), Scorch = new(0.95f, 0.5f, 0.2f, 1f), Bruise = new(0.5f, 0.3f, 0.6f, 1f);

    /// <summary>
    /// The patient lying head to feet, the parts named over them. The palms glow warmer as they
    /// near a hurt (all a skim tells you), and a warmth blooms where they've sensed one; once read, the wound's own mark, beating in its pulse (a cut's bright slash,
    /// a bite's two, a bruise swelling, a burn's scorch, frost's rime, venom spreading), fading as
    /// it's drawn. The hands over them, pressing as you press; while the link's open a thread of
    /// light from the wound into the palms, fraying red off balance and shaking with strain. A
    /// flash as a wound comes across; the body jerks when they flinch.
    /// </summary>
    private void HandsOnScene(HandsView v)
    {
        var st = SceneFor("handson", v.Session);
        var (dl, min, max, px) = WorkScene(120, Theme.Arcane);
        var flinch = v.Step - v.LastFlinch is >= 0 and < 20 ? (1f - (v.Step - v.LastFlinch) / 20f) : 0f;
        var jerk = MathF.Sin(_time * 60f) * flinch * 3f * px;
        if (Prop(dl, "patient", new float2((min.X + max.X) * 0.5f + jerk, max.Y - Gui.S(6)), px) is not { } body)
            return;
        float AlongBody(float f) => body.Min.X + 4 * px + f * (body.Max.X - body.Min.X - 8 * px);
        var bodyY = body.Min.Y + 12 * px;
        for (var i = 0; i < v.Parts.Count; i++)
        {
            var label = v.Parts[i];
            var at = new float2(AlongBody(HandsOnSession.PartAt(i, v.Parts.Count)) - ImGui.CalcTextSize(label).X / 2, min.Y + Gui.S(4));
            dl.AddText(in at, Theme.U8(Theme.Dim), label);
        }

        // When each warmth was first sensed: already known ones (read before) don't bloom.
        while (st.Since.Count < v.Spots.Count)
            st.Since.Add(v.Spots[st.Since.Count].Sensed > 0f ? float.MinValue : float.MaxValue);
        for (var i = 0; i < v.Spots.Count; i++)
        {
            var sp = v.Spots[i];
            if (sp.Sensed > 0f && st.Since[i] == float.MaxValue)
                st.Since[i] = _time;
            if (sp.Sensed <= 0f || sp.Drawn >= 1f && v.Step - v.LastDrawnAt > 40)
                continue;
            var centre = new float2(AlongBody(sp.At), bodyY);
            var left = 1f - sp.Drawn;
            // The warmth: all a skim tells you. It blooms as it's found, a ring spreading and fading.
            dl.AddCircleFilled(in centre, Gui.S(7f + 7f * sp.Gravity), Theme.U8(Theme.Warn, 0.12f + 0.18f * sp.Sensed * left), 24);
            var bloom = (_time - st.Since[i]) / 0.7f;
            if (bloom is >= 0f and < 1f)
            {
                dl.AddCircleFilled(in centre, Gui.S(7f + 7f * sp.Gravity) * (1f + 0.4f * (1f - bloom)), Theme.U8(Theme.Warn, 0.35f * (1f - bloom)), 24);
                dl.AddCircle(in centre, Gui.S(8f + 26f * bloom), Theme.U8(Theme.Warn, 0.8f * (1f - bloom)), 32, Gui.S(2f));
            }
            if (sp.Found >= 1f)
                WoundMark(dl, centre, sp, i == v.Under ? v.Pulse : 0f, left, px);
            else if (i == v.Under && v.Pulse > 0f)
                dl.AddCircle(in centre, Gui.S(5f + 9f * v.Pulse), Theme.U8(Theme.Text, 0.25f + 0.5f * v.Pulse * sp.Found), 24, Gui.S(1.5f));
            if (sp.Festering)
                dl.AddCircle(in centre, Gui.S(9f + 6f * sp.Gravity), Theme.U8(VenomGreen, 0.6f * left), 20, Gui.S(2f));
        }

        // The hands: both, side by side, pressing in as you press, trembling with the strain.
        var shake = v.Strain * 2f * px * MathF.Sin(_time * 40f);
        var handX = MathF.Min(AlongBody(v.Hands), max.X - 13 * px);
        var press = v.Held ? 3f * px : 0f;
        var palm = new float2(handX + shake, bodyY - 2 * px + press);
        // Warmth in the palms as they near a hurt, beating quicker the nearer: hot and cold, by eye.
        var warm = Math.Clamp(v.Feel, 0f, 1f);
        if (warm > 0.05f && !v.Linked)
        {
            var beat = 0.75f + 0.25f * MathF.Sin(_time * (3f + 9f * warm));
            dl.AddCircleFilled(in palm, Gui.S(10f + 10f * warm) * beat, Theme.U8(Theme.Warn, 0.1f + 0.3f * warm), 24);
        }
        var cool = new float4(0.88f, 0.84f, 0.8f, 1f);
        var tint = v.Held ? Gui.White : Gui.U8(cool + (new float4(1f, 0.78f, 0.55f, 1f) - cool) * warm);
        Prop(dl, "hand", palm + new float2(-5 * px, 0), px, tint);
        Prop(dl, "hand", palm + new float2(5 * px, -px), px, tint);

        // The link: light drawn up out of the wound into the palms.
        if (v.Linked && v.Under >= 0)
        {
            var from = new float2(AlongBody(v.Spots[v.Under].At), bodyY);
            var to = palm - new float2(0, 9 * px);
            var colour = v.Calm ? Theme.Arcane : Theme.Bad;
            for (var k = 0; k < 6; k++)
            {
                var t = (_time * 1.3f + k / 6f) % 1f;
                var fray = v.Calm ? 1f : 3f;
                var at = from + (to - from) * t + new float2(MathF.Sin(t * 9f + k) * fray * px, 0);
                dl.AddCircleFilled(in at, px * (1.4f - t * 0.6f), Gui.U8(colour, 0.85f * (1f - t * 0.4f)), 8);
            }
            dl.AddLine(in from, in to, Gui.U8(colour, 0.25f), Gui.S(1.5f));
        }

        // A wound come across: a flash where it was, rising into the hands.
        if (v.LastDrawn >= 0 && v.LastDrawn < v.Spots.Count && v.Step - v.LastDrawnAt is >= 0 and < 40)
        {
            var t = (v.Step - v.LastDrawnAt) / 40f;
            var at = new float2(AlongBody(v.Spots[v.LastDrawn].At), bodyY - t * 10 * px);
            dl.AddCircleFilled(in at, Gui.S(6f + 18f * t), Gui.U8(Theme.Arcane, 0.5f * (1f - t)), 24);
            if (st.Count != v.LastDrawnAt)
            {
                st.Count = v.LastDrawnAt;
                for (var k = 0; k < 10; k++)
                    st.Motes.Add(new Mote(at, palm + new float2((k - 5) * px, -8 * px), _time + k * 0.03f, 0.5f));
            }
        }
        DrawMotes(dl, st, Theme.Arcane, px);
    }

    /// <summary>A read wound's mark by how it feels, beating with <paramref name="pulse"/>, faded to <paramref name="left"/> of it.</summary>
    private void WoundMark(ImDrawListPtr dl, float2 c, HandsSpot sp, float pulse, float left, float px)
    {
        var a = (0.35f + 0.55f * sp.Gravity) * left + 0.3f * pulse;
        var r = Gui.S(3f + 5f * sp.Gravity) * (1f + 0.35f * pulse);
        switch (sp.Pattern)
        {
            case Sim.Defs.DrawPattern.Double: // a bite: two tears
                for (var k = -1; k <= 1; k += 2)
                {
                    var o = new float2(k * 2.5f * px, 0);
                    dl.AddLine(c + o + new float2(-r, -r * 0.4f), c + o + new float2(r, r * 0.4f), Gui.U8(Theme.Bad, a), Gui.S(2f));
                }
                break;
            case Sim.Defs.DrawPattern.Wave: // a bruise: a dark swelling that breathes
                dl.AddCircleFilled(in c, r * (1f + 0.2f * MathF.Sin(_time * 2.4f)), Gui.U8(Bruise, a), 24);
                break;
            case Sim.Defs.DrawPattern.Drift: // a burn: a scorch, its heat streaming off one way
                dl.AddCircleFilled(in c, r, Gui.U8(Scorch, a * 0.8f), 20);
                for (var k = 0; k < 3; k++)
                {
                    var t = (_time * 0.8f + k / 3f) % 1f;
                    var at = c + new float2(t * r * 2f, -t * r);
                    dl.AddCircleFilled(in at, px * (1.2f - t), Gui.U8(Scorch, a * (1f - t)), 8);
                }
                break;
            case Sim.Defs.DrawPattern.Numb: // frost: a pale rime ringed in spikes
                dl.AddCircle(in c, r, Gui.U8(FrostBlue, a), 20, Gui.S(1.5f));
                for (var k = 0; k < 6; k++)
                {
                    var ang = k * MathF.Tau / 6f;
                    var d = new float2(MathF.Cos(ang), MathF.Sin(ang));
                    dl.AddLine(c + d * r, c + d * r * 1.5f, Gui.U8(FrostBlue, a * 0.8f), Gui.S(1f));
                }
                break;
            case Sim.Defs.DrawPattern.Grow: // venom: a sick stain spreading
                dl.AddCircleFilled(in c, r * (0.8f + 0.4f * pulse), Gui.U8(VenomGreen, a * 0.7f), 20);
                dl.AddCircleFilled(in c, r * 0.4f, Gui.U8(Theme.Bad, a), 12);
                break;
            default: // a cut, a stab, a shock: one bright slash
                dl.AddLine(c + new float2(-r, -r * 0.5f), c + new float2(r, r * 0.5f), Gui.U8(Theme.Bad, a), Gui.S(2.5f));
                dl.AddLine(c + new float2(-r, -r * 0.5f), c + new float2(r, r * 0.5f), Gui.U8(Theme.Text, a * 0.5f * pulse), Gui.S(1f));
                break;
        }
    }

    /// <summary>The cask's head end at <paramref name="centre"/> and the tap in its side; where the spout is.</summary>
    private static float2 CaskAndTap(ImDrawListPtr dl, float2 centre, float px, float fallbackRadius)
    {
        if (!Gui.TrySprite("ui/work/cask_end", out var cask) || !Gui.TrySprite("ui/work/spigot", out var tap))
        {
            dl.AddCircleFilled(in centre, fallbackRadius, new ImColor8(120, 82, 46, 255), 32);
            dl.AddCircle(in centre, fallbackRadius, Theme.U8(Theme.Iron), 32, Gui.S(3));
            var t0 = centre + new float2(-fallbackRadius * 0.86f, fallbackRadius * 0.6f);
            var t1 = t0 + Gui.S(-26, 6);
            dl.AddLine(in t0, in t1, new ImColor8(170, 140, 90, 255), Gui.S(5));
            return t1 + Gui.S(0, 6);
        }
        var half = new float2(20, 20) * px;
        var tapMin = new float2(MathF.Round(centre.X - 14 * px - 26 * px), MathF.Round(centre.Y + 6 * px));
        Gui.Image(dl, tap, tapMin, tapMin + new float2(26, 12) * px, Gui.White);
        Gui.Image(dl, cask, centre - half, centre + half, Gui.White);
        return tapMin + new float2(2, 12) * px;
    }

    // ---------------- The gallery (--work-scenes) ----------------

    /// <summary>Every scene at once, driven by a clock instead of a session: to look over the art without playing each game.</summary>
    private void DrawWorkSceneGallery()
    {
        ImGui.SetNextWindowPos(Gui.S(20, 20), ImGuiCond.FirstUseEver);
        Panel.Begin("Work scenes###workscenes", ImGuiWindowFlags.AlwaysAutoResize);
        float Wave(float speed, float phase = 0f) => 0.5f + 0.5f * MathF.Sin(_time * speed + phase);
        var beat = (int)(_time / 1.5f);
        if (ImGui.BeginTable("##scenes"u8, 2, ImGuiTableFlags.None, default, 0f))
        {
            void Cell(string label, Action draw)
            {
                ImGui.TableNextColumn();
                TextC(Theme.Dim, label);
                draw();
            }
            Cell("Season", () => SeasonScene(1, beat));
            Cell("Make bed", () => MakeBedScene(beat % 4, Wave(2f), Wave(2f) > 0.8f));
            Cell("Bandage", () => BandageScene(5, beat % 5, Wave(1.5f), 0.55f, 0.75f));
            Cell("Sharpen", () => SharpenScene(1, true, Wave(0.8f), Wave(0.3f), _time * 0.05f));
            Cell("Grind", () => SharpenScene(1, true, Wave(0.8f), Wave(0.3f), _time * 0.05f, onWheel: true));
            Cell("Probe", () => ProbeScene(Wave(0.4f), Wave(3f) > 0.85f ? 0.8f : 0.1f, Wave(1.1f)));
            Cell("Loupe", () => LoupeScene(Wave(0.3f), Wave(1.3f), Wave(0.6f) > 0.5f));
            Cell("Cask, tap and mallet", () =>
            {
                var (dl, min, max, px) = WorkScene(130);
                var spout = CaskAndTap(dl, new float2(min.X + Gui.S(150), min.Y + Gui.S(62)), px, Gui.S(54));
                dl.AddLine(spout, spout + new float2(0, Gui.S(30)), new ImColor8(200, 140, 50, 230), Gui.S(4));
                if (Gui.TrySprite("ui/work/mallet", out var mallet))
                {
                    var head = new float2(min.X + Gui.S(300), min.Y + Gui.S(20) + Wave(3f) * Gui.S(60));
                    var mMin = head - new float2(6, 8) * px;
                    Gui.Image(dl, mallet, mMin, mMin + new float2(40, 16) * px, Gui.White);
                }
            });
            Cell("Hands on", () =>
            {
                // Every kind of wound read, one being drawn; the hands sweeping back and forth.
                var step = (int)(_time * 60f);
                var kinds = Enum.GetValues<Sim.Defs.DrawPattern>();
                var spots = kinds.Select((k, i) => new HandsSpot(0.08f + 0.84f * i / Math.Max(1, kinds.Length - 1), 0.3f + 0.1f * i, k, 1f, 1f, i == 1 ? (_time * 0.15f) % 1f : 0f, i == kinds.Length - 1)).ToList();
                HandsOnScene(new HandsView(1, ["head", "neck", "chest", "belly", "arms", "legs", "feet"], spots, spots[1].At, true, true, Wave(0.7f) > 0.3f,
                    Wave(0.9f), HandsOnSession.PulseShape(kinds[1], step, spots[1].Drawn), 1, step, -1, -1000, -1000, Wave(0.4f)));
            });
            Cell("True", () => TrueScene(_time * 0.1f % 1f, true, (Wave(0.63f) - 0.5f) * 0.6f, 0.06f, 0, [0.2f, -0.03f, 0f, -0.15f, 0.1f, 0f, 0.02f, -0.25f], [true, true, true, true, true, true, true, true], [0, 0, 0, 0, 0, 0, 0, 0]));
            ImGui.EndTable();
        }
        Panel.End();
    }
}
