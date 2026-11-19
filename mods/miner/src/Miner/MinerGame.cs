using Brutal.ImGuiApi;
using HeroesNotIncluded.Audio;
using HeroesNotIncluded.Mods;
using HeroesNotIncluded.Ui;
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
    }

    /// <summary>
    /// Each blow: a slab breaking away cracks, a chip knocks, the point ringing off the rock rings like iron.
    /// The game's own sounds for now; the mod can have its own (sounds/sounds.json) once it's settled.
    /// </summary>
    private static void Sound(WorkSoundContext ctx, HewSession s)
    {
        if (s.Blows > ctx.A)
            switch (s.LastBlow)
            {
                case HewBlow.Broke:
                    ctx.Audio.Play(Sounds.Snap, pitch: 0.55f);
                    ctx.Audio.Play(Sounds.Knock, gain: 0.8f, pitch: 0.6f);
                    break;
                case HewBlow.Chipped:
                    ctx.Audio.Play(Sounds.Knock, gain: 0.7f, pitch: 0.9f);
                    break;
                case HewBlow.Rang:
                    ctx.Audio.Play(Sounds.AnvilPoor, gain: 0.6f, pitch: 1.3f);
                    break;
                default:
                    ctx.Audio.Play(Sounds.Knock, gain: 0.4f, pitch: 1.2f);
                    break;
            }
        ctx.A = s.Blows;
    }
}

/// <summary>
/// The rock face front on: the crack running down it, the hollow growing as slabs come away, the pick
/// over where it's aimed and swung back as far as it's held, and beside it the swing's gauge against the
/// mark of a hard enough blow. Dust and grit fly from a slab breaking.
/// </summary>
internal sealed class HewView
{
    private int _blows;
    private float _blowAt = -10f;

    internal void Draw(WorkView v, HewSession s)
    {
        v.Begin(s.Title,
            ("A/D|Mouse", "put the point over the crack"),
            ("F|Click", "hold to swing back, let go to strike"),
            ("Esc", "stop"));
        ImGuiText.Colored(s.Jarred > 0 ? Theme.Warn : Theme.Dim,
            s.Jarred > 0 ? "It rang off the rock. Your arms are jarred: the next swing comes up slow."
            : s.LastBlow == HewBlow.Chipped ? "A chip. On the crack, but harder."
            : s.LastBlow == HewBlow.Glanced ? "It glanced off. Strike along the crack."
            : "Strike along the crack, hard. Off it, the point rings off the rock.");

        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(360, 300);
        ImGui.Dummy(in size);
        // Room above the face for the pick swung back.
        var f0 = new float2(min.X + Gui.S(20), min.Y + Gui.S(105));
        var f1 = new float2(min.X + Gui.S(280), min.Y + Gui.S(290));
        var w = f1.X - f0.X;
        v.TakesPointer(f0, f1);

        // The face: dark rock, a few strata across it, the hollow where slabs have come away.
        var rock = Theme.Iron * 0.55f + Theme.Cellar * 0.45f;
        dl.AddRectFilled(in f0, in f1, Theme.U8(rock), Gui.S(3), ImDrawFlags.None);
        for (var k = 1; k <= 3; k++)
        {
            var y = f0.Y + (f1.Y - f0.Y) * k / 4f;
            dl.AddLine(new float2(f0.X, y), new float2(f1.X, y + Gui.S(6) * (k % 2 == 0 ? 1 : -1)), Theme.U8(new float4(Theme.Cellar.XYZ, 0.6f)), Gui.S(1.5f));
        }
        var dug = (float)s.ChunksDone / Math.Max(1, s.Chunks);
        if (dug > 0f)
        {
            var c = (f0 + f1) / 2f;
            var r = new float2(w, f1.Y - f0.Y) * 0.42f * dug;
            dl.AddRectFilled(c - r, c + r, Theme.U8(new float4(Theme.Cellar.XYZ, 0.9f)), Gui.S(12) * dug, ImDrawFlags.None);
        }

        // The crack: a jagged dark line down the face where it'll break, and the band a strike must land in.
        var cx = f0.X + (s.Crack + 1f) / 2f * w;
        var band = s.CrackWidthNow * w / 2f;
        dl.AddRectFilled(new float2(cx - band, f0.Y), new float2(cx + band, f1.Y), Theme.U8(new float4(Theme.Parchment.XYZ, 0.12f)), 0f, ImDrawFlags.None);
        var prev = new float2(cx, f0.Y);
        for (var k = 1; k <= 8; k++)
        {
            var next = new float2(cx + Gui.S(4) * (k % 2 == 0 ? 1 : -1), f0.Y + (f1.Y - f0.Y) * k / 8f);
            dl.AddLine(in prev, in next, Theme.U8(Theme.Cellar), Gui.S(3));
            prev = next;
        }

        // The pick over the aim, its point at the face, swung back (up) as far as it's held.
        var ax = f0.X + (s.Aim + 1f) / 2f * w;
        var hard = s.Raised >= HewSession.NeedRaise;
        var lift = s.Raised * Gui.S(70);
        var tip = new float2(ax, f0.Y - Gui.S(4) - lift);
        var head = s.Jarred > 0 ? Theme.Warn : hard ? Theme.Good : Theme.Iron;
        dl.AddTriangleFilled(tip, tip + new float2(-Gui.S(18), -Gui.S(12)), tip + new float2(Gui.S(18), -Gui.S(12)), Theme.U8(head));
        dl.AddLine(tip + new float2(0, -Gui.S(10)), tip + new float2(0, -Gui.S(60)), Theme.U8(Theme.Oak), Gui.S(5));

        // Grit from a slab breaking away.
        if (s.Blows != _blows)
            (_blows, _blowAt) = (s.Blows, v.Time);
        var since = v.Time - _blowAt;
        if (since < 0.45f && s.LastBlow == HewBlow.Broke)
            for (var k = 0; k < 8; k++)
            {
                var t = since / 0.45f;
                var p = new float2(cx + (k - 3.5f) * Gui.S(10) * (1f + t), f0.Y + Gui.S(30) + t * t * Gui.S(120));
                dl.AddCircleFilled(in p, Gui.S(3), Theme.U8(new float4(Theme.Parchment.XYZ, 0.8f * (1f - t))), 6);
            }

        // How far back it's swung, against the mark of a hard enough blow.
        var m0 = new float2(min.X + Gui.S(310), min.Y + Gui.S(110));
        var m1 = new float2(min.X + Gui.S(324), min.Y + Gui.S(285));
        Gui.Trough(dl, m0, m1);
        var raisedY = m1.Y - (m1.Y - m0.Y) * s.Raised;
        dl.AddRectFilled(new float2(m0.X, raisedY), in m1, Theme.U8(s.Jarred > 0 ? Theme.Warn : hard ? Theme.Good : Theme.OakLight), 0f, ImDrawFlags.None);
        var needY = m1.Y - (m1.Y - m0.Y) * HewSession.NeedRaise;
        dl.AddLine(new float2(m0.X - Gui.S(6), needY), new float2(m1.X + Gui.S(6), needY), Theme.U8(Theme.Parchment), Gui.S(2));
        v.End();
    }
}
