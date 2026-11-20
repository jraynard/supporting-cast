using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using HeroesNotIncluded.Audio;
using HeroesNotIncluded.Mods;
using HeroesNotIncluded.Ui;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Ecs;
using Narsil.Platform;
using Narsil.Ui;

namespace HeroesNotIncluded.Miner;

/// <summary>
/// The miner's game half (docs/MODDING.md): how hewing looks and sounds. The view reads only the session
/// (<see cref="HewSession"/>), which knows the face only as it looks, so it never tells ore from rock.
/// </summary>
public sealed class MinerGame : IGameMod
{
    public void Register(GameRegistry r)
    {
        var view = new HewView();
        r.WorkView<HewSession>(view.Draw);
        r.WorkSound<HewSession>(Sound);
        var sounding = new SoundView();
        r.WorkView<SoundSession>(sounding.Draw);
        r.WorkSound<SoundSession>(Tap);
        r.CellMarks(Chalk);
        var loading = new LoadView();
        r.WorkView<LoadSession>(loading.Draw);
        r.WorkSound<LoadSession>(Load);
        Pushing.LeanLeft = r.Control("lean_left", "Lean the barrow back to the left, as you push it", [Binding.Key(GlfwKey.Q)], [Binding.Pad(PadButton.LB)], "Barrow");
        Pushing.LeanRight = r.Control("lean_right", "Lean the barrow back to the right, as you push it", [Binding.Key(GlfwKey.E)], [Binding.Pad(PadButton.RB)], "Barrow");
        r.Overlay(Pushing.Draw);
        // A barrow's load heaps up in its tray (its model's ore boxes): one more than its pieces, so an empty one shows none.
        r.ModelCount((sim, e) => Barrows.IsBarrow(sim, e) ? 1 + Barrows.LoadOf(sim, e).Pieces.Count : null);
    }

    /// <summary>Each piece set on the bed knocks down onto the planks (lower for a heavier one); lifted off, a scrape.</summary>
    private static void Load(WorkSoundContext ctx, LoadSession s)
    {
        if (s.Sets > ctx.A && s.Placed.Count > 0)
            ctx.Audio.Play(Sounds.Knock, gain: 0.8f, pitch: 1.15f - 0.08f * Math.Min(4f, s.Placed[^1].Def.Mass));
        if (s.Lifts > ctx.B)
            ctx.Audio.Play(MinerSounds.Glance, gain: 0.5f, pitch: 1.2f);
        ctx.A = s.Sets;
        ctx.B = s.Lifts;
    }

    /// <summary>The chalk on the mine's faces, for anyone to see: an X for a seam, a ring for nothing.</summary>
    private static IEnumerable<CellMark> Chalk(Simulation sim)
    {
        // Where to take the barrow you're pushing (yours alone: it's set from your own screen each frame).
        if (Pushing.DeliverAt is { } at)
            yield return new CellMark(at, "items/iron_ore", new float4(1f, 1f, 1f, 1f), 0.85f, Floating: true);
        if (sim.SystemOrNull<ChalkMarks>() is not { } chalk)
            yield break;
        foreach (var (i, mark) in chalk.All)
            yield return new CellMark(sim.Map.CellOf(i), mark == ChalkMark.Seam ? "props/chalk_seam" : "props/chalk_bare", new float4(0.96f, 0.95f, 0.9f, 0.9f), 0.75f);
    }

    /// <summary>
    /// Each tap: the hammer's two notes mixed by how it was heard (<see cref="SoundSession.LastHeard"/>, noise and all):
    /// a clear ring off sound rock, going dead and low with something behind it.
    /// </summary>
    private static void Tap(WorkSoundContext ctx, SoundSession s)
    {
        if (s.TapCount > ctx.A)
        {
            var dead = s.LastHeard;
            ctx.Audio.Play(MinerSounds.TapRing, gain: 1f - dead, pitch: 1f - 0.18f * dead);
            ctx.Audio.Play(MinerSounds.TapDead, gain: 0.25f + 0.75f * dead, pitch: 1.05f - 0.15f * dead);
        }
        ctx.A = s.TapCount;
    }

    /// <summary>
    /// Each blow, in the mod's own sounds (<see cref="MinerSounds"/>): a slab breaking away cracks and its rubble
    /// falls, a chip ticks, the point ringing off the rock jangles, a soft miss skids.
    /// </summary>
    private static void Sound(WorkSoundContext ctx, HewSession s)
    {
        if (s.Blows > ctx.A)
            ctx.Audio.Play(s.LastBlow switch
            {
                HewBlow.Broke => MinerSounds.Break,
                HewBlow.Chipped => MinerSounds.Chip,
                HewBlow.Rang => MinerSounds.Ring,
                _ => MinerSounds.Glance,
            });
        ctx.A = s.Blows;
    }
}

/// <summary>The miner's own sound ids (its sounds/sounds.json, made by tools/sfx_synth.py's <c>mine_*</c>).</summary>
internal static class MinerSounds
{
    public const string Break = "miner.break", Chip = "miner.chip", Ring = "miner.ring", Glance = "miner.glance";

    /// <summary>The hammer's tap on sound rock and on rock with something behind it.</summary>
    public const string TapRing = "miner.tap.ring", TapDead = "miner.tap.dead";
}

/// <summary>
/// The wall side on: the rock to the right, its face a rough edge, the crack running into it at some height, and
/// the miner's pick swung at it from the left. At rest the pick lies along the wall with its point in the rock where
/// it's aimed, the hands low; held, it swings back up about the hands, and on the blow it comes in in a blink
/// (as the smith's hammer does at the anvil, turned to a wall). Each slab that breaks takes a layer off the wall,
/// so the face steps back into the rock, and its pieces tumble to the rubble at its foot. A chip throws flakes; a
/// blow that rings off the rock throws sparks and shakes the pick. Read only from the session, which knows the
/// face only as it looks.
/// </summary>
internal sealed class HewView
{
    /// <summary>Along the pick sprite (ui/work/pick, 34 pixels), where its point is: what lands on the rock.</summary>
    private const float PointAlong = 25.5f / 34f;

    /// <summary>How far the haft leans out from the wall at rest, from straight up and down (25°).</summary>
    private static readonly float LeanSin = MathF.Sin(25f * MathF.PI / 180f), LeanCos = MathF.Cos(25f * MathF.PI / 180f);

    /// <summary>Segments down the wall's rough edge.</summary>
    private const int EdgeSteps = 14;

    private int _blows;
    private float _blowAt = -10f;
    private float _raisedLast, _crackLast;
    private int3 _cell = new(-1, -1, -1);

    /// <summary>The pieces of the last slab coming down.</summary>
    private readonly List<Falling> _falling = [];

    private readonly record struct Falling(float2 From, float2 Velocity, float Spin, float Size, int Seed, float Born);

    internal void Draw(WorkView v, HewSession s)
    {
        v.Begin(s.Title,
            ("W/S|Mouse", "put the point at the crack"),
            ("F|Click", "hold to swing back, let go to strike"),
            ("Esc", "stop"));
        ImGuiText.Colored(s.Jarred > 0 ? Theme.Warn : Theme.Dim,
            s.Jarred > 0 ? "It rang off the rock. Your arms are jarred: the next swing comes up slow."
            : s.LastBlow == HewBlow.Chipped ? "A chip. On the crack, but harder."
            : s.LastBlow == HewBlow.Glanced ? "It glanced off. Strike along the crack."
            : "Strike along the crack, hard. Off it, the point rings off the rock.");

        if (s.Cell != _cell)
        {
            _cell = s.Cell;
            _falling.Clear();
            _blows = s.Blows;
            _crackLast = s.Crack;
        }

        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(400, 350);
        ImGui.Dummy(in size);
        var px = MathF.Max(1f, MathF.Round(3f * Gui.Scale));
        // The wall from top to floor; the point reaches it between aimTop and aimBottom (the mouse's range too).
        var top = min.Y + Gui.S(50);
        var floorY = min.Y + Gui.S(320);
        var aimTop = min.Y + Gui.S(100);
        var aimBottom = min.Y + Gui.S(240);
        var right = min.X + Gui.S(395);
        float Y(float along) => aimTop + (along + 1f) / 2f * (aimBottom - aimTop);
        v.TakesPointer(new float2(min.X + Gui.S(30), aimTop), new float2(right, aimBottom));

        // The face's edge: where it starts, stepped back a layer for each slab taken off, rough down its height.
        var start = min.X + Gui.S(215);
        var layer = Gui.S(110) / Math.Max(1, s.Chunks);
        float EdgeAt(int layers, float y)
        {
            var t = Math.Clamp((y - top) / (floorY - top), 0f, 1f) * EdgeSteps;
            var k = (int)MathF.Floor(t);
            var a = Hash(layers * 97 + k, 1);
            var b = Hash(layers * 97 + k + 1, 1);
            var jag = a + (b - a) * (t - k);
            return start + layer * layers + (jag - 0.5f) * Gui.S(10);
        }
        var done = s.ChunksDone;

        // A blow since the last frame: a slab breaking away comes down off the old edge at the crack's height.
        if (s.Blows != _blows)
        {
            (_blows, _blowAt) = (s.Blows, v.Time);
            if (s.LastBlow == HewBlow.Broke)
            {
                var y = Y(_crackLast);
                for (var k = 0; k < 6; k++)
                {
                    var seed = s.ChunksDone * 31 + k;
                    var from = new float2(EdgeAt(done - 1, y) + layer * Hash(seed, 2), y + (Hash(seed, 1) - 0.5f) * Gui.S(70));
                    var vel = new float2(-Gui.S(30 + 110 * Hash(seed, 3)), -Gui.S(50) * Hash(seed, 4));
                    _falling.Add(new Falling(from, vel, (Hash(seed, 5) - 0.5f) * 9f, Gui.S(6 + 8 * Hash(seed, 6)), seed, v.Time));
                }
            }
        }
        _crackLast = s.Crack;

        // The rock: dark, speckled, from its edge to the right, a strip of paler fresh rock along a face that's been worked.
        var rock = new float4((Theme.Iron * 0.42f + Theme.Cellar * 0.58f).XYZ, 1f);
        var fresh = new float4((rock * 1.6f).XYZ, 1f);
        for (var k = 0; k < EdgeSteps; k++)
        {
            var y0 = top + (floorY - top) * k / EdgeSteps;
            var y1 = top + (floorY - top) * (k + 1) / EdgeSteps;
            var a = new float2(EdgeAt(done, y0), y0);
            var b = new float2(EdgeAt(done, y1), y1);
            dl.AddQuadFilled(in a, new float2(right, y0), new float2(right, y1), in b, Theme.U8(rock));
            if (done > 0)
                dl.AddQuadFilled(in a, a + new float2(Gui.S(9), 0), b + new float2(Gui.S(9), 0), in b, Theme.U8(fresh));
            dl.AddLine(in a, in b, Theme.U8(new float4(Theme.Cellar.XYZ, 0.9f)), Gui.S(2));
        }
        for (var k = 0; k < 70; k++)
        {
            var p = new float2(start - Gui.S(10) + (right - start) * Hash(k, 40), top + (floorY - top) * Hash(k, 41));
            if (p.X < EdgeAt(done, p.Y) + Gui.S(12))
                continue;
            var light = Hash(k, 42) > 0.5f;
            dl.AddCircleFilled(in p, Gui.S(1.5f + 2f * Hash(k, 43)), Theme.U8(light ? new float4((rock * 1.3f).XYZ, 0.7f) : new float4(Theme.Cellar.XYZ, 0.5f)), 5);
        }
        // Strata running back into the hill.
        for (var k = 1; k <= 3; k++)
        {
            var y = top + (floorY - top) * k / 4f;
            dl.AddLine(new float2(EdgeAt(done, y) + Gui.S(4), y), new float2(right, y - Gui.S(5) * (k % 2 == 0 ? 1 : -1)), Theme.U8(new float4(Theme.Cellar.XYZ, 0.55f)), Gui.S(1.5f));
        }
        // The tunnel's floor, from the far side of the window to the wall.
        dl.AddRectFilled(new float2(min.X + Gui.S(26), floorY), new float2(right, floorY + Gui.S(18)), Theme.U8(new float4((Theme.Oak * 0.55f + Theme.Iron * 0.25f).XYZ, 1f)), 0f, ImDrawFlags.None);

        // The crack: a jagged dark line running into the rock at its height, and the band a strike must land in.
        var cy = Y(s.Crack);
        var cEdge = EdgeAt(done, cy);
        var band = s.CrackWidthNow * (aimBottom - aimTop) / 2f;
        dl.AddRectFilled(new float2(cEdge - Gui.S(30), cy - band), new float2(cEdge + Gui.S(60), cy + band), Theme.U8(new float4(Theme.Parchment.XYZ, 0.12f)), 0f, ImDrawFlags.None);
        var prev = new float2(cEdge, cy);
        for (var k = 1; k <= 6; k++)
        {
            var next = new float2(cEdge + Gui.S(10) * k, cy + Gui.S(3.5f) * (k % 2 == 0 ? 1 : -1));
            dl.AddLine(in prev, in next, Theme.U8(Theme.Cellar), Gui.S(3f - 0.3f * k));
            prev = next;
        }

        // The rubble at the foot of the wall: a few stones for every slab that's come down, the last once it's landed.
        var settled = v.Time - _blowAt > 0.5f ? done : done - 1;
        for (var k = 0; k < settled; k++)
            for (var j = 0; j < 4; j++)
            {
                var seed = k * 53 + j * 7 + 11;
                var p = new float2(EdgeAt(done, floorY) - Gui.S(10 + 70 * Hash(seed, 1)), floorY - Gui.S(3) - Gui.S(4) * Hash(seed, 2) - Gui.S(2) * k);
                Lump(dl, p, new float2(Gui.S(6 + 5 * Hash(seed, 3)), Gui.S(4 + 3 * Hash(seed, 4))), seed, fresh, 0f);
            }

        // The pieces coming down: off the face, tumbling, landing on the floor.
        var gravity = Gui.S(700f);
        _falling.RemoveAll(f => v.Time - f.Born > 0.5f);
        foreach (var f in _falling)
        {
            var t = v.Time - f.Born;
            var p = f.From + f.Velocity * t + new float2(0f, gravity * t * t / 2f);
            p.Y = MathF.Min(p.Y, floorY - f.Size * 0.5f);
            Lump(dl, p, new float2(f.Size, f.Size * 0.75f), f.Seed, fresh, f.Spin * t);
        }

        // The pick: its point in the rock where it's aimed, raised as far as it's held; after a blow, swung in from
        // where it was in a blink. A blow that rang shakes it.
        var since = v.Time - _blowAt;
        if (s.Raised > 0f)
            _raisedLast = s.Raised;
        var raise = s.Raised > 0f ? s.Raised : since < 0.07f ? _raisedLast * (1f - since / 0.07f) : 0f;
        var ay = Y(s.Aim);
        var strike = new float2(EdgeAt(done, ay) + Gui.S(3), ay);
        var shake = s.LastBlow == HewBlow.Rang && since < 0.3f ? (1f - since / 0.3f) * Gui.S(4f) : 0f;
        var jolt = new float2(MathF.Sin(v.Time * 95f) * shake, MathF.Cos(v.Time * 70f) * shake);
        SwingAtWall(dl, raise, strike + jolt, px, s.Jarred > 0 ? Theme.U8(Theme.Warn) : Gui.White);

        // What flies off the point on the blow, back out of the wall: grit from a slab or a chip, sparks from a ring.
        if (since < 0.4f)
        {
            var t = since / 0.4f;
            var (count, colour, spread) = s.LastBlow switch
            {
                HewBlow.Broke => (8, new float4(Theme.Parchment.XYZ, 0.8f), 1f),
                HewBlow.Chipped => (4, new float4(Theme.Parchment.XYZ, 0.7f), 0.5f),
                HewBlow.Rang => (7, new float4(1f, 0.85f, 0.45f, 1f), 0.8f),
                _ => (0, Theme.Dim, 0f),
            };
            for (var k = 0; k < count; k++)
            {
                var a = MathF.PI * (0.6f + 0.8f * k / Math.Max(1, count - 1));
                var dir = new float2(MathF.Cos(a), MathF.Sin(a));
                var p = strike + dir * Gui.S(60) * spread * t + new float2(0, Gui.S(120) * t * t);
                if (s.LastBlow == HewBlow.Rang)
                    dl.AddLine(in p, p - dir * Gui.S(6), Theme.U8(new float4(colour.XYZ, 1f - t)), Gui.S(2));
                else
                    dl.AddCircleFilled(in p, Gui.S(2.5f), Theme.U8(new float4(colour.XYZ, colour.W * (1f - t))), 6);
            }
        }

        // How far back it's swung, against the mark of a hard enough blow.
        var m0 = new float2(min.X + Gui.S(4), aimTop);
        var m1 = new float2(min.X + Gui.S(18), floorY);
        Gui.Trough(dl, m0, m1);
        var raisedY = m1.Y - (m1.Y - m0.Y) * s.Raised;
        var hard = s.Raised >= HewSession.NeedRaise;
        dl.AddRectFilled(new float2(m0.X, raisedY), in m1, Theme.U8(s.Jarred > 0 ? Theme.Warn : hard ? Theme.Good : Theme.OakLight), 0f, ImDrawFlags.None);
        var needY = m1.Y - (m1.Y - m0.Y) * HewSession.NeedRaise;
        dl.AddLine(new float2(m0.X - Gui.S(4), needY), new float2(m1.X + Gui.S(4), needY), Theme.U8(Theme.Parchment), Gui.S(2));
        v.End();
    }

    /// <summary>
    /// The pick swung at a wall to its right: the smith's swing at the anvil (<c>WorkView.DrawToolSwing</c>) stood up
    /// against the wall. At rest the point is in the rock, the head high and the haft leaning down and out from the
    /// wall to the hands; raised, the head swings back up and away over the shoulder, about the hands.
    /// </summary>
    private static void SwingAtWall(ImDrawListPtr dl, float raise, float2 strike, float px, ImColor8 tint)
    {
        if (!Gui.TrySprite("ui/work/pick", out var pick))
            return;
        var angle = -Math.Clamp(raise, 0f, 1f) * 1.15f;
        var len = new float2(pick.PixelSize.X, pick.PixelSize.Y) * px;
        // As at the anvil (x along the surface, y down onto it), the point resting on the strike...
        var pivot = new float2(len.X * (PointAlong - 0.15f), -len.Y * 0.5f);
        var dir = new float2(-MathF.Cos(angle), MathF.Sin(angle));
        var up = new float2(-dir.Y, dir.X);
        var back = pivot - dir * (len.X * 0.15f);
        var head = back + dir * len.X;
        var h = up * (len.Y * 0.5f);
        // ...then stood up against the wall: along the anvil toward the hands becomes down and out from the wall (the
        // hands low, the head high), down onto the anvil becomes into the wall.
        var toHands = new float2(-LeanSin, LeanCos);
        var intoWall = new float2(LeanCos, LeanSin);
        float2 W(float2 p) => strike + toHands * p.X + intoWall * p.Y;
        Gui.ImageQuad(dl, pick, W(back + h), W(head + h), W(head - h), W(back - h), tint);
    }

    /// <summary>A rough lump of rock (a falling piece, a stone on the floor), its upper left edge in shadow.</summary>
    private static void Lump(ImDrawListPtr dl, float2 c, float2 r, int seed, float4 colour, float turn)
    {
        Fan(dl, c, r, seed, Theme.U8(new float4((colour * 0.5f).XYZ, 1f)), turn);
        Fan(dl, c + r * 0.12f, r * 0.85f, seed, Theme.U8(colour), turn);
    }

    private static void Fan(ImDrawListPtr dl, float2 c, float2 r, int seed, ImColor8 fill, float turn)
    {
        const int Corners = 9;
        Span<float2> pts = stackalloc float2[Corners];
        for (var k = 0; k < Corners; k++)
        {
            var a = turn + MathF.Tau * (k + 0.35f * Hash(seed, k)) / Corners;
            pts[k] = c + new float2(MathF.Cos(a) * r.X, MathF.Sin(a) * r.Y) * (0.6f + 0.4f * Hash(seed, k + 20));
        }
        for (var k = 0; k < Corners; k++)
            dl.AddTriangleFilled(in c, in pts[k], in pts[(k + 1) % Corners], fill);
    }

    /// <summary>A steady 0..1 from a seed and a salt, so a lump keeps its shape from frame to frame.</summary>
    private static float Hash(int seed, int salt)
    {
        var h = (uint)(seed * 374761393 + salt * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
    }
}

/// <summary>
/// The wall front on, as the miner faces it: five spots across it (open ground, where the wall isn't rock, shown dark),
/// the face in the middle. The hammer hangs over the spot it's on and taps down; each tap sends ripples off the rock,
/// wide and slow to fade for a clear ring, small and quick for a dead one (the same note the ear gets, noise and all,
/// so it can be read without sound). Under each spot, how it's rung so far over its taps; on it, the chalk. Read only
/// from the session's taps, never what's behind the wall.
/// </summary>
internal sealed class SoundView
{
    private int _taps;
    private float _tapAt = -10f;

    internal void Draw(WorkView v, SoundSession s)
    {
        v.Begin(s.Title,
            ("A/D|Mouse", "choose a spot"),
            ("F|Click", "tap it"),
            ("Space", "chalk an X where it sounds dead (again: a ring, nothing here)"),
            ("Esc", "done (the chalk stays)"));
        // Wrapped to the picture's width, so the advice doesn't stretch the window.
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Gui.S(420));
        ImGuiText.Colored(Theme.Dim, s.LastSpot < 0 ? "Tap along the wall and listen. Sound rock rings clear; with ore behind it, the note goes dead. Chalk an X where it's dead."
            : s.LastHeard < 0.3f ? "It rings back clear: sound rock, by that tap."
            : s.LastHeard < 0.6f ? "A duller note. Tap it again, and the spots beside it."
            : "Dead. Something's behind it, if the other taps agree: chalk an X there.");
        ImGui.PopTextWrapPos();

        if (s.TapCount != _taps)
            (_taps, _tapAt) = (s.TapCount, v.Time);
        else if (s.TapCount == 0)
            _tapAt = -10f;
        var since = v.Time - _tapAt;

        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(420, 300);
        ImGui.Dummy(in size);
        var px = MathF.Max(1f, MathF.Round(3f * Gui.Scale));
        var w0 = new float2(min.X + Gui.S(10), min.Y + Gui.S(70));
        var w1 = new float2(min.X + Gui.S(410), min.Y + Gui.S(230));
        var spotW = (w1.X - w0.X) / SoundSession.Spots;
        v.TakesPointer(w0, w1);
        var rock = new float4((Theme.Iron * 0.42f + Theme.Cellar * 0.58f).XYZ, 1f);
        var chalk = new float4(0.96f, 0.95f, 0.9f, 0.9f);

        for (var k = 0; k < SoundSession.Spots; k++)
        {
            var a = new float2(w0.X + spotW * k, w0.Y);
            var b = new float2(a.X + spotW, w1.Y);
            var mid = (a + b) / 2f;
            if (!s.Rock[k])
            {
                // Open ground: the tunnel going on, dark.
                dl.AddRectFilled(in a, in b, Theme.U8(new float4((Theme.Cellar * 0.7f).XYZ, 1f)), 0f, ImDrawFlags.None);
                continue;
            }
            dl.AddRectFilled(in a, in b, Theme.U8(rock), 0f, ImDrawFlags.None);
            for (var j = 0; j < 14; j++)
            {
                var p = new float2(a.X + spotW * Hash(k * 31 + j, 1), a.Y + (b.Y - a.Y) * Hash(k * 31 + j, 2));
                dl.AddCircleFilled(in p, Gui.S(1.5f + 2f * Hash(k * 31 + j, 3)), Theme.U8(Hash(k * 31 + j, 4) > 0.5f ? new float4((rock * 1.3f).XYZ, 0.7f) : new float4(Theme.Cellar.XYZ, 0.5f)), 5);
            }
            // The chalk on it: an X for a seam, a ring for nothing.
            var r = Gui.S(18);
            if (s.Marks[k] == ChalkMark.Seam)
            {
                dl.AddLine(mid + new float2(-r, -r), mid + new float2(r, r), Theme.U8(chalk), Gui.S(4));
                dl.AddLine(mid + new float2(r, -r), mid + new float2(-r, r), Theme.U8(chalk), Gui.S(4));
            }
            else if (s.Marks[k] == ChalkMark.Bare)
                dl.AddCircle(in mid, r, Theme.U8(chalk), 20, Gui.S(4));
            // Its last few taps (each tick a tap), each where it was heard, dead at the left to clear at the right; the newest brightest.
            var g0 = new float2(a.X + Gui.S(10), w1.Y + Gui.S(12));
            var g1 = new float2(b.X - Gui.S(10), w1.Y + Gui.S(30));
            Gui.Trough(dl, g0, g1);
            var recent = s.RecentAt(k).ToList();
            for (var j = 0; j < recent.Count; j++)
            {
                var x = g0.X + Gui.S(3) + (g1.X - g0.X - Gui.S(6)) * (1f - recent[j]);
                var bright = 0.35f + 0.65f * (j + 1) / recent.Count;
                dl.AddLine(new float2(x, g0.Y + Gui.S(2)), new float2(x, g1.Y - Gui.S(2)), Theme.U8(new float4(Theme.Parchment.XYZ, bright)), Gui.S(2.5f));
            }
        }
        // The seams between the spots, and the face (the middle one) marked.
        for (var k = 1; k < SoundSession.Spots; k++)
            dl.AddLine(new float2(w0.X + spotW * k, w0.Y), new float2(w0.X + spotW * k, w1.Y), Theme.U8(new float4(Theme.Cellar.XYZ, 0.6f)), Gui.S(1));
        var faceLabel = "the face";
        var fl = new float2(w0.X + spotW * (SoundSession.Spots / 2 + 0.5f) - ImGui.CalcTextSize(faceLabel).X / 2f, w0.Y - Gui.S(20));
        dl.AddText(in fl, Theme.U8(Theme.Dim), faceLabel);
        // The key to the ticks: dead at the left, clear at the right.
        var keyY = w1.Y + Gui.S(38);
        dl.AddText(new float2(w0.X, keyY), Theme.U8(Theme.Dim), "< dead: ore behind, chalk an X"u8);
        var clear = "clear: sound rock >";
        dl.AddText(new float2(w1.X - ImGui.CalcTextSize(clear).X, keyY), Theme.U8(Theme.Dim), clear);

        // The tap: ripples off the rock where it landed, wide and lingering for a clear ring, small and quick for a dead one.
        if (s.LastSpot >= 0 && since < 1.2f)
        {
            var at = new float2(w0.X + spotW * (s.LastSpot + 0.5f), w0.Y + (w1.Y - w0.Y) * 0.55f);
            var ring = 1f - s.LastHeard;
            var life = 0.25f + 0.9f * ring;
            for (var j = 0; j < 3; j++)
            {
                var t = since - j * 0.12f;
                if (t < 0f || t > life)
                    continue;
                var f = t / life;
                dl.AddCircle(in at, Gui.S(8) + Gui.S(10 + 40 * ring) * f, Theme.U8(new float4(Theme.Parchment.XYZ, (1f - f) * (0.35f + 0.6f * ring))), 24, Gui.S(2));
            }
        }

        // The hammer over the spot it's on, tapping down on a tap and lifting again.
        var hit = new float2(w0.X + spotW * (s.Spot + 0.5f), w0.Y + (w1.Y - w0.Y) * 0.55f);
        var lift = since < 0.06f ? 0f : since < 0.3f ? 0.35f * (since - 0.06f) / 0.24f : 0.35f;
        v.DrawToolSwing(dl, "ui/work/hammer", 26f / 32f, s.Rock[s.Spot] ? lift : 0.5f, hit, px, s.Rock[s.Spot] ? Gui.White : Theme.U8(new float4(1f, 1f, 1f, 0.4f)));
        v.End();
    }

    private static float Hash(int seed, int salt)
    {
        var h = (uint)(seed * 374761393 + salt * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
    }
}

/// <summary>
/// The barrow's bed from above, the wheel at the top and the handles at the bottom: its slots, the pieces set in them (each
/// its own item's picture, the second of a slot laid over the first), the slot the next piece hangs over, and a dot where
/// the load's weight centres against the steady zone (square across, a little behind the wheel). Beside it, the next piece,
/// what's still to load, and the weight against what the barrow takes.
/// </summary>
internal sealed class LoadView
{
    internal void Draw(WorkView v, LoadSession s)
    {
        v.Begin(s.Title,
            ("WASD|Mouse", "choose a place on the bed"),
            ("F|Click", "set the next piece there"),
            ("Space", "lift the top piece off again"),
            ("Esc", "done (what's on it stays)"));
        var (across, back) = Barrows.Centre(s.Placed);
        var steady = s.Risk < Barrows.Steady;
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Gui.S(430));
        ImGuiText.Colored(steady ? Theme.Dim : Theme.Warn, s.Placed.Count == 0
            ? "Load it square across, the weight a little behind the wheel. The dot shows where the weight is: keep it in the green and it'll push steady."
            : steady ? "Steady: it'll go anywhere."
            : MathF.Abs(across) * Barrows.SideRisk >= MathF.Abs(back - Barrows.IdealRow) * Barrows.ForeRisk
                ? $"It leans {(across < 0 ? "left" : "right")}: it may go over on a turn or on rough ground. Load the other side."
                : back < Barrows.IdealRow
                    ? "Too far forward: it may nose over. Load nearer the handles."
                    : "Too far back: it drags at your arms and may tip back. Load nearer the wheel.");
        ImGui.PopTextWrapPos();

        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(430, 370);
        ImGui.Dummy(in size);
        var cell = Gui.S(60);
        var b0 = new float2(min.X + Gui.S(40), min.Y + Gui.S(60));
        var b1 = b0 + new float2(cell * Barrows.Cols, cell * Barrows.Rows);
        v.TakesPointer(b0, b1);
        float2 SlotCentre(int slot) => b0 + new float2((Barrows.ColOf(slot) + 0.5f) * cell, (Barrows.RowOf(slot) + 0.5f) * cell);

        // The wheel at the front (up), the handles out behind (down), the tray's planked sides.
        var wheelC = new float2((b0.X + b1.X) / 2f, b0.Y - Gui.S(22));
        dl.AddRectFilled(wheelC - new float2(Gui.S(9), Gui.S(24)), wheelC + new float2(Gui.S(9), Gui.S(24)), Theme.U8(Theme.Iron), Gui.S(6), ImDrawFlags.None);
        dl.AddRectFilled(wheelC - new float2(Gui.S(3), Gui.S(10)), wheelC + new float2(Gui.S(3), Gui.S(10)), Theme.U8(Theme.Oak), 0f, ImDrawFlags.None);
        foreach (var x in new[] { b0.X + Gui.S(14), b1.X - Gui.S(14) })
            dl.AddLine(new float2(x, b1.Y), new float2(x + (x < wheelC.X ? -Gui.S(6) : Gui.S(6)), b1.Y + Gui.S(36)), Theme.U8(Theme.Oak), Gui.S(7));
        dl.AddRectFilled(b0 - new float2(Gui.S(8), Gui.S(8)), b1 + new float2(Gui.S(8), Gui.S(8)), Theme.U8(Theme.Oak), Gui.S(6), ImDrawFlags.None);
        dl.AddRectFilled(in b0, in b1, Theme.U8(Theme.OakLight * 0.75f + Theme.Cellar * 0.25f), Gui.S(3), ImDrawFlags.None);
        for (var k = 1; k < Barrows.Rows; k++)
            dl.AddLine(new float2(b0.X, b0.Y + cell * k), new float2(b1.X, b0.Y + cell * k), Theme.U8(new float4(Theme.Cellar.XYZ, 0.45f)), Gui.S(1));
        for (var k = 1; k < Barrows.Cols; k++)
            dl.AddLine(new float2(b0.X + cell * k, b0.Y), new float2(b0.X + cell * k, b1.Y), Theme.U8(new float4(Theme.Cellar.XYZ, 0.25f)), Gui.S(1));

        // The steady zone: where the weight may centre and the barrow never tips (a diamond: across counts for more than fore and aft).
        float2 At(float a, float r) => b0 + new float2((a + (Barrows.Cols - 1) / 2f + 0.5f) * cell, (r + 0.5f) * cell);
        var da = Barrows.Steady / Barrows.SideRisk;
        var dr = Barrows.Steady / Barrows.ForeRisk;
        var zone = Theme.U8(new float4(Theme.Good.XYZ, 0.55f));
        var top = At(0, Barrows.IdealRow - dr);
        var right = At(da, Barrows.IdealRow);
        var bottom = At(0, Barrows.IdealRow + dr);
        var left = At(-da, Barrows.IdealRow);
        dl.AddQuadFilled(in top, in right, in bottom, in left, Theme.U8(new float4(Theme.Good.XYZ, 0.12f)));
        dl.AddQuad(in top, in right, in bottom, in left, zone, Gui.S(2));
        // Said in words, beside the bed: the green's the steady zone, the dot's the weight.
        var keyAt = new float2(b0.X - Gui.S(30), b1.Y + Gui.S(44));
        dl.AddQuadFilled(keyAt + new float2(Gui.S(6), 0), keyAt + new float2(Gui.S(12), Gui.S(7)), keyAt + new float2(Gui.S(6), Gui.S(14)), keyAt + new float2(0, Gui.S(7)), zone);
        dl.AddText(keyAt + new float2(Gui.S(18), -Gui.S(1)), Theme.U8(Theme.Dim), "steady zone"u8);
        dl.AddCircleFilled(keyAt + new float2(Gui.S(126), Gui.S(7)), Gui.S(6), Theme.U8(Theme.Good), 12);
        dl.AddText(keyAt + new float2(Gui.S(138), -Gui.S(1)), Theme.U8(Theme.Dim), "where the weight is: keep it in the green"u8);

        // The pieces, each its own picture; the second in a slot laid over the first, a little off.
        var count = new int[Barrows.Slots];
        foreach (var p in s.Placed)
        {
            var layer = count[p.Slot]++;
            var c = SlotCentre(p.Slot) + new float2(Gui.S(7), -Gui.S(7)) * layer - new float2(Gui.S(4), -Gui.S(4));
            var half = new float2(Gui.S(22), Gui.S(22));
            if (Gui.TrySprite(p.Def.Sprite, out var sprite))
                Gui.Image(dl, sprite, c - half, c + half, Gui.White);
            else
                dl.AddCircleFilled(in c, half.X * 0.6f, Theme.U8(Theme.Iron), 8);
        }

        // The slot the next piece hangs over: its outline, the piece itself faint in it if it fits.
        var hover = SlotCentre(s.Slot);
        var fits = s.Fits(s.Slot);
        dl.AddRect(hover - new float2(cell / 2f - Gui.S(2)), hover + new float2(cell / 2f - Gui.S(2)), Theme.U8(fits ? Theme.Parchment : Theme.Warn), Gui.S(4), ImDrawFlags.None, Gui.S(2));
        if (fits && s.ToLoad.Count > 0 && Gui.TrySprite(s.ToLoad[0].Sprite, out var next))
            Gui.Image(dl, next, hover - new float2(Gui.S(20)), hover + new float2(Gui.S(20)), Theme.U8(new float4(1f, 1f, 1f, 0.45f)));

        // Where the weight centres.
        if (s.Placed.Count > 0)
        {
            var dot = At(across, back);
            dl.AddCircleFilled(in dot, Gui.S(7), Theme.U8(steady ? Theme.Good : Theme.Warn), 16);
            dl.AddCircle(in dot, Gui.S(7), Theme.U8(Theme.Cellar), 16, Gui.S(2));
        }

        // Beside it: the next piece, what's still to load, and the weight in it.
        var side = new float2(b1.X + Gui.S(36), b0.Y - Gui.S(30));
        dl.AddText(in side, Theme.U8(Theme.Dim), "Next"u8);
        if (s.ToLoad.Count > 0)
        {
            var nextDef = s.ToLoad[0];
            var at = side + new float2(0, Gui.S(20));
            if (Gui.TrySprite(nextDef.Sprite, out var sp))
                Gui.Image(dl, sp, at, at + new float2(Gui.S(44)), Gui.White);
            dl.AddText(at + new float2(Gui.S(52), Gui.S(12)), Theme.U8(Theme.Text), $"{nextDef.Label} ({nextDef.Mass:0.#} kg)");
            dl.AddText(at + new float2(0, Gui.S(56)), Theme.U8(Theme.Dim), s.ToLoad.Count == 1 ? "the last to load" : $"{s.ToLoad.Count} to load");
        }
        else
            dl.AddText(side + new float2(0, Gui.S(20)), Theme.U8(Theme.Dim), "Nothing left to load."u8);
        // The weight against what it takes.
        var g0 = new float2(side.X, b1.Y - Gui.S(40));
        var g1 = new float2(side.X + Gui.S(150), b1.Y - Gui.S(28));
        Gui.Trough(dl, g0, g1);
        dl.AddRectFilled(in g0, new float2(g0.X + (g1.X - g0.X) * Math.Clamp(s.Mass / Barrows.MaxMass, 0f, 1f), g1.Y), Theme.U8(Theme.Barrel), 0f, ImDrawFlags.None);
        dl.AddText(new float2(g0.X, g1.Y + Gui.S(4)), Theme.U8(Theme.Dim), $"{s.Mass:0.#} of {Barrows.MaxMass:0} kg");
        v.End();
    }
}

/// <summary>
/// Pushing a loaded barrow (the miner's walk to the smithy): a panel at the bottom of the screen with how far it leans
/// (<see cref="BarrowLoad.Lean"/>, as anyone could see it), the keys that lean it back (Q/E, sent as
/// <see cref="BarrowLeanCommand"/>), and which way the smithy is; and an icon bobbing over the smithy's chest
/// (<see cref="DeliverAt"/>, drawn by <see cref="MinerGame"/>'s marks), on this screen alone.
/// </summary>
internal static class Pushing
{
    public static string LeanLeft = "", LeanRight = "";

    /// <summary>The smithy's chest while this player pushes a loaded barrow, else null.</summary>
    public static int3? DeliverAt;

    private static int _sent;
    private static float _sentAt = -10f;

    internal static void Draw(WorkView v)
    {
        DeliverAt = null;
        var sim = v.Sim;
        var me = v.Me;
        if (me.IsNull || Barrows.Held(sim, me) is not { IsNull: false } barrow || !sim.World.TryGet<BarrowLoad>(barrow, out var load) || load.Pieces.Count == 0)
            return;
        // Q/E: sent when they change, or again if the barrow hasn't taken it (a new barrow, or one that's just gone over).
        var nudge = v.Playing && !v.Working ? (v.Down(LeanRight) ? 1 : 0) - (v.Down(LeanLeft) ? 1 : 0) : 0;
        if (nudge != load.Nudge && (nudge != _sent || v.Time - _sentAt > 0.3f))
        {
            v.Issue(new BarrowLeanCommand(me, nudge));
            (_sent, _sentAt) = (nudge, v.Time);
        }

        var smithy = HeroesNotIncluded.Sim.Shops.ShopSystem.BuildingOf(sim, sim.Defs.Building(HeroesNotIncluded.Sim.Shops.OreCarterSystem.SmithyDef));
        var chest = smithy.IsNull ? Entity.Null : HeroesNotIncluded.Sim.Systems.Stock.ChestOf(sim, smithy);
        var here = sim.World.Get<HeroesNotIncluded.Sim.Components.Position>(me).Cell;
        if (!chest.IsNull)
            DeliverAt = sim.World.Get<HeroesNotIncluded.Sim.Components.Thing>(chest).Cell;

        v.BeginHudPanel("##barrow-push");
        ImGuiText.Colored(Theme.Parchment, $"Pushing the barrow: {Barrows.Describe(load.Pieces)}");
        // The lean: the middle steady, the ends where it goes over.
        var dl = ImGui.GetWindowDrawList();
        var at = ImGui.GetCursorScreenPos();
        var size = Gui.S(320, 22);
        ImGui.Dummy(in size);
        var g0 = at + new float2(0, Gui.S(4));
        var g1 = at + new float2(size.X, Gui.S(18));
        Gui.Trough(dl, g0, g1);
        var w = g1.X - g0.X;
        var mid = (g0.X + g1.X) / 2f;
        dl.AddRectFilled(new float2(g0.X, g0.Y), new float2(g0.X + w * 0.12f, g1.Y), Theme.U8(new float4(Theme.Bad.XYZ, 0.5f)), 0f, ImDrawFlags.None);
        dl.AddRectFilled(new float2(g1.X - w * 0.12f, g0.Y), new float2(g1.X, g1.Y), Theme.U8(new float4(Theme.Bad.XYZ, 0.5f)), 0f, ImDrawFlags.None);
        dl.AddLine(new float2(mid, g0.Y - Gui.S(2)), new float2(mid, g1.Y + Gui.S(2)), Theme.U8(Theme.Dim), Gui.S(1));
        var x = mid + load.Lean * (w / 2f - Gui.S(4));
        var danger = MathF.Abs(load.Lean);
        var colour = danger > 0.7f ? Theme.Bad : danger > 0.4f ? Theme.Warn : Theme.Good;
        dl.AddRectFilled(new float2(x - Gui.S(4), g0.Y - Gui.S(3)), new float2(x + Gui.S(4), g1.Y + Gui.S(3)), Theme.U8(colour), Gui.S(2), ImDrawFlags.None);
        ImGuiText.Colored(danger > 0.4f ? Theme.Warn : Theme.Dim,
            $"{WorkView.Glyph(LeanLeft)} / {WorkView.Glyph(LeanRight)}: lean it back. "
            + (danger > 0.7f ? $"It's going over to the {(load.Lean < 0 ? "left" : "right")}!" : danger > 0.4f ? "It's leaning: ease it back." : "Steady."));
        // Where it goes: the smithy, which way and how far, an arrow pointing as the map's turned.
        if (!chest.IsNull)
        {
            var to = sim.World.Get<HeroesNotIncluded.Sim.Components.Thing>(chest).Cell;
            var steps = (int)MathF.Round(HeroesNotIncluded.Sim.Maps.Map.Distance(here, to));
            var arrowAt = ImGui.GetCursorScreenPos() + new float2(Gui.S(10), Gui.S(10));
            ImGui.Dummy(Gui.S(22, 20));
            ImGui.SameLine();
            if (steps <= 1)
                ImGuiText.Colored(Theme.Good, "The smithy's chest: press F at it to tip the load in.");
            else
            {
                var degrees = MathF.Atan2(to.Y - here.Y, to.X - here.X) * 180f / MathF.PI;
                var dir = v.ScreenDir(degrees);
                var len = MathF.Max(1e-3f, MathF.Sqrt(dir.X * dir.X + dir.Y * dir.Y));
                dir /= len;
                var side = new float2(-dir.Y, dir.X);
                dl.AddTriangleFilled(arrowAt + dir * Gui.S(9), arrowAt - dir * Gui.S(6) + side * Gui.S(6), arrowAt - dir * Gui.S(6) - side * Gui.S(6), Theme.U8(Theme.Parchment));
                ImGuiText.Colored(Theme.Dim, $"To the smithy, {steps} steps {Way(degrees)}: tip it into the chest (marked) with F.");
            }
        }
        v.EndHudPanel();
    }

    /// <summary>A way on the map in words (degrees: 0 east, 90 south).</summary>
    private static string Way(float degrees)
    {
        string[] ways = ["east", "south-east", "south", "south-west", "west", "north-west", "north", "north-east"];
        var k = (int)MathF.Round(((degrees % 360f) + 360f) % 360f / 45f) % 8;
        return ways[k];
    }
}
