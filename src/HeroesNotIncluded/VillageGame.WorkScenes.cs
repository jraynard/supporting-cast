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

    /// <summary>The hand over the hurt, the hurt drawn up into it as light; the hand trembles as the strain builds.</summary>
    private void DrawScene(bool held, bool calm, float drawn, float strain)
    {
        var (dl, min, max, px) = WorkScene(84, Theme.Arcane);
        var mid = (min + max) * 0.5f;
        if (Prop(dl, "forearm", new float2(mid.X, max.Y - Gui.S(6)), px) is not { } arm)
            return;
        var hurt = new float2(mid.X, arm.Min.Y + 6 * px);
        var shake = strain * 2f * px;
        var palm = new float2(mid.X + MathF.Sin(_time * 40f) * shake, arm.Min.Y - 5 * px);
        // What you feel of it: a glow, its brightness only how much is left to draw.
        dl.AddCircleFilled(in hurt, (5f + 6f * (1f - drawn)) * px, Gui.U8(Theme.Arcane, 0.18f * (1f - drawn) + 0.05f), 20);
        if (held)
            for (var k = 0; k < 5; k++)
            {
                var t = (_time * 1.2f + k / 5f) % 1f;
                var at = hurt + (palm - hurt) * t + new float2(MathF.Sin(t * 9f + k) * 2f * px, 0);
                dl.AddCircleFilled(in at, px * (1.4f - t * 0.6f), Gui.U8(calm ? Theme.Arcane : Theme.Bad, 0.8f * (1f - t * 0.5f)), 8);
            }
        Prop(dl, "hand", palm + new float2(-4 * px, 4 * px), px);
    }

    // ---------------- The smith: the edge ----------------

    /// <summary>The blade on the stone at the angle you hold it, warming as you grind; sparks as the edge comes back.</summary>
    private void SharpenScene(long session, bool held, float angle, float heat, float progress)
    {
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
    /// The wheel on its stand, side on: it turns with the session, its rim swinging out and in at
    /// the gauge (the right) by what the gauge reads, the spoke there picked out.
    /// </summary>
    private void TrueScene(float turn, float gauge, float band)
    {
        var (dl, min, max, px) = WorkScene(130);
        var centre = new float2((min.X + max.X) * 0.5f, min.Y + Gui.S(62));
        var r = Gui.S(52);
        var oak = Theme.U8(Theme.OakLight);
        var dark = Theme.U8(Theme.Oak);
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
            var picked = MathF.Abs(MathF.IEEERemainder(a, MathF.Tau)) < MathF.PI / TrueSession.Spokes;
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
        dl.AddCircleFilled(in centre, Gui.S(9), dark, 16);
        dl.AddCircleFilled(in centre, Gui.S(5), Theme.U8(Theme.Iron), 12);
        // The gauge's finger at the rim, and where true lies.
        var gx = centre.X + r + Gui.S(14);
        var trueA = new float2(gx, centre.Y - Gui.S(16));
        var trueB = new float2(gx, centre.Y + Gui.S(16));
        dl.AddLine(in trueA, in trueB, Theme.U8(MathF.Abs(gauge) <= band ? Theme.Good : Theme.Dim), Gui.S(2));
        var finger = new float2(centre.X + r + lean + Gui.S(4), centre.Y);
        var post = new float2(gx + Gui.S(18), centre.Y);
        dl.AddLine(in finger, in post, Theme.U8(Theme.Iron), Gui.S(3));
    }

    /// <summary>
    /// The patient lying head to feet across it, the parts named over them; what the hands have made
    /// out glows where it lies (the worse the redder, festering found sickly), and the hands over them,
    /// pressing in as you press. Only what's been felt is drawn.
    /// </summary>
    private void FeelScene(IReadOnlyList<string> parts, IEnumerable<(float At, float Gravity, float Found, bool Festering)> found, float hands, bool held)
    {
        var (dl, min, max, px) = WorkScene(104, Theme.Arcane);
        if (Prop(dl, "patient", new float2((min.X + max.X) * 0.5f, max.Y - Gui.S(6)), px) is not { } body)
            return;
        float AlongBody(float f) => body.Min.X + 4 * px + f * (body.Max.X - body.Min.X - 8 * px);
        var bodyY = body.Min.Y + 12 * px;
        for (var i = 0; i < parts.Count; i++)
        {
            var label = parts[i];
            var at = new float2(AlongBody(FeelSession.PartAt(i, parts.Count)) - ImGui.CalcTextSize(label).X / 2, min.Y + Gui.S(4));
            dl.AddText(in at, Theme.U8(Theme.Dim), label);
        }
        foreach (var (at, gravity, amount, festering) in found)
        {
            var centre = new float2(AlongBody(at), bodyY);
            dl.AddCircleFilled(in centre, Gui.S(4f + 6f * gravity), Theme.U8(festering ? Theme.Warn : Theme.Bad, 0.25f + 0.65f * amount), 20);
        }
        var press = held ? 3f * px : 0f;
        Prop(dl, "hand", new float2(MathF.Min(AlongBody(hands) + 6 * px, max.X - 13 * px), bodyY - 2 * px + press), px, held ? Gui.White : Gui.U8(new float4(0.88f, 0.84f, 0.8f, 1f)));
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
            Cell("Draw", () => DrawScene(true, Wave(0.7f) > 0.3f, (_time * 0.1f) % 1f, Wave(0.9f)));
            Cell("Sharpen", () => SharpenScene(1, true, Wave(0.8f), Wave(0.3f), _time * 0.05f));
            Cell("Probe", () => ProbeScene(Wave(0.4f), Wave(3f) > 0.85f ? 0.8f : 0.1f, Wave(1.1f)));
            Cell("Loupe", () => LoupeScene(Wave(0.3f), Wave(1.3f), Wave(0.6f) > 0.5f));
            Cell("Feel", () => FeelScene(["head", "chest", "belly", "arms", "legs"], [(0.3f, 0.6f, 0.9f, false), (0.75f, 0.3f, Wave(0.5f), true)], Wave(0.35f), Wave(1f) > 0.5f));
            Cell("True", () => TrueScene(_time * 0.1f, (Wave(0.63f) - 0.5f) * 0.6f, 0.06f));
            ImGui.EndTable();
        }
        Panel.End();
    }
}
