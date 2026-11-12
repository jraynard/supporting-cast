using Brutal.ImGuiApi;
using Narsil.Sim.Core;
using Narsil.Ui;
using SupportingCast.Sim.Work;
using SupportingCast.Ui;

namespace SupportingCast;

// The hammering game drawn as a scene: the anvil, the glowing bar on it, and the hammer whose
// swing is the timing cue (it comes down as the marker crosses the mark). A blow throws sparks
// and a word on how it landed. All of it is read from the session; nothing here touches the sim.
public sealed partial class VillageGame
{
    private readonly record struct Spark(float2 Pos, float2 Vel, float Born, float Life);

    private readonly List<Spark> _sparks = [];
    private long _anvilSession = -1;
    private int _anvilBlows;
    private float _anvilQuality, _strikeAt = -10f, _strikeGrade;

    private const float StrikeSeconds = 0.18f, GradeSeconds = 0.9f;

    private void DrawHammer(HammerSession s)
    {
        BeginWork(s.Title, ("F", "strike as the hammer comes down on the mark"), ("Esc", "stop"));
        if (s.StartTick != _anvilSession)
        {
            _anvilSession = s.StartTick;
            _anvilBlows = 0;
            _anvilQuality = 0f;
            _strikeAt = -10f;
            _sparks.Clear();
        }
        // A new blow: its own grade is how far the running average moved.
        if (s.Blows > _anvilBlows)
        {
            _strikeGrade = Math.Clamp(s.Quality * s.Blows - _anvilQuality * _anvilBlows, 0f, 1f);
            _anvilBlows = s.Blows;
            _anvilQuality = s.Quality;
            _strikeAt = _time;
            _pendingSparks = true;
        }

        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 170);
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var since = _time - _strikeAt;
        // A small shake after a blow, bigger for a clean one.
        var shake = since < 0.2f ? (1f - since / 0.2f) * Gui.S(3f) * (0.4f + _strikeGrade) : 0f;
        var jolt = new float2(MathF.Sin(_time * 90f) * shake, MathF.Cos(_time * 70f) * shake);
        var min = origin;
        var max = origin + size;

        // The forge's light behind it all.
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        var glowTop = Theme.U8(Theme.Accent, 0f);
        var glowBottom = Theme.U8(Theme.Accent, 0.22f);
        var gMin = new float2(min.X + Gui.S(4), min.Y + size.Y * 0.45f);
        var gMax = max - Gui.S(4, 4);
        dl.AddRectFilledMultiColor(in gMin, in gMax, glowTop, glowTop, glowBottom, glowBottom);

        var px = MathF.Max(1f, MathF.Round(3f * Gui.Scale));
        var heat = 1f - 0.7f * s.Blows / (float)HammerSession.BlowsNeeded;
        if (Gui.TrySprite("ui/work/anvil", out var anvil))
        {
            var a = new float2(anvil.PixelSize.X, anvil.PixelSize.Y) * px;
            var aMin = new float2(min.X + size.X * 0.5f - a.X * 0.55f, max.Y - a.Y - Gui.S(6)) + jolt;
            Gui.Image(dl, anvil, aMin, aMin + a, Gui.White);
            // The bar on the anvil's face, glowing as hot as it still is.
            var face = new float2(aMin.X + 15 * px, aMin.Y + 4 * px);
            if (Gui.TrySprite("ui/work/billet", out var billet))
            {
                var b = new float2(billet.PixelSize.X, billet.PixelSize.Y) * px;
                var bMin = new float2(face.X + 2 * px, face.Y - b.Y + px);
                var hot = HeatColour(heat);
                var glowAt = bMin + b * 0.5f;
                dl.AddCircleFilled(in glowAt, b.X * 0.7f, Gui.U8(hot, 0.18f * heat), 24);
                Gui.Image(dl, billet, bMin, bMin + b, Gui.U8(hot));
                if (since < 0.08f)
                    dl.AddRectFilled(in bMin, bMin + b, new ImColor8(255, 255, 240, (byte)(220 * (1f - since / 0.08f))), 0f, ImDrawFlags.None);
                var strikePoint = new float2(bMin.X + b.X * 0.6f, bMin.Y);
                if (_pendingSparks)
                    ThrowSparks(strikePoint, _strikeGrade, s.Blows);
                DrawHammerSwing(dl, s, strikePoint, px, since);
                DrawSparks(dl);
                if (since < GradeSeconds)
                    DrawGrade(dl, strikePoint, since);
            }
        }

        // The timing strip: the mark as a glowing notch, the marker, and a pip per blow.
        ImGui.Spacing();
        var tMin = ImGui.GetCursorScreenPos();
        var tSize = Gui.S(420, 22);
        ImGui.Dummy(in tSize);
        var tMax = tMin + tSize;
        Gui.Trough(dl, tMin, tMax);
        var inner = Gui.S(3);
        float X(float f) => tMin.X + inner + (tSize.X - inner * 2) * f;
        var mMin = new float2(X(s.Mark - s.HalfWidth), tMin.Y + inner);
        var mMax = new float2(X(s.Mark + s.HalfWidth), tMax.Y - inner);
        dl.AddRectFilled(in mMin, in mMax, Theme.U8(Theme.Accent, 0.55f), 0f, ImDrawFlags.None);
        var notchMin = new float2(X(s.Mark) - Gui.S(1), tMin.Y + inner);
        var notchMax = new float2(X(s.Mark) + Gui.S(1), tMax.Y - inner);
        dl.AddRectFilled(in notchMin, in notchMax, Theme.U8(Theme.Text), 0f, ImDrawFlags.None);
        var x = X(s.Marker);
        var nMin = new float2(x - Gui.S(2), tMin.Y - Gui.S(3));
        var nMax = new float2(x + Gui.S(2), tMax.Y + Gui.S(3));
        dl.AddRectFilled(in nMin, in nMax, Theme.U8(s.Ready ? Theme.Text : Theme.Dim), Gui.S(1), ImDrawFlags.None);

        ImGui.Spacing();
        var pipR = Gui.S(5);
        var pips = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new float2(tSize.X, pipR * 2 + Gui.S(2)));
        var pipsW = HammerSession.BlowsNeeded * pipR * 3;
        for (var i = 0; i < HammerSession.BlowsNeeded; i++)
        {
            var c = new float2(pips.X + (tSize.X - pipsW) * 0.5f + pipR * 1.5f + i * pipR * 3, pips.Y + pipR + Gui.S(1));
            if (i < s.Blows)
                dl.AddCircleFilled(in c, pipR, Theme.U8(Theme.Accent), 12);
            dl.AddCircle(in c, pipR, Theme.U8(Theme.Iron), 12, Gui.S(1.5f));
        }
        EndWork();
    }

    private bool _pendingSparks;

    /// <summary>White-hot to cherry to dull red as the bar cools under the hammer.</summary>
    private static float4 HeatColour(float heat)
    {
        var cherry = new float4(0.95f, 0.36f, 0.12f, 1f);
        var white = new float4(1f, 0.9f, 0.6f, 1f);
        var dull = new float4(0.45f, 0.16f, 0.1f, 1f);
        return heat > 0.6f ? cherry + (white - cherry) * ((heat - 0.6f) / 0.4f) : dull + (cherry - dull) * (heat / 0.6f);
    }

    /// <summary>
    /// The hammer, pivoting at the smith's hand off to the right. Between blows it hangs high when
    /// the marker is far from the mark and drops toward the bar as it nears it; just after a blow
    /// it rests on the bar, then lifts.
    /// </summary>
    private void DrawHammerSwing(ImDrawListPtr dl, HammerSession s, float2 strike, float px, float since)
    {
        if (!Gui.TrySprite("ui/work/hammer", out var hammer))
            return;
        var near = Math.Clamp(1f - MathF.Abs(s.Marker - s.Mark) / 0.45f, 0f, 1f);
        var raise = since < StrikeSeconds ? 0f : s.Ready ? 1f - near * near : 0.5f;
        var angle = -raise * 1.15f; // radians up from resting flat on the bar
        var len = new float2(hammer.PixelSize.X, hammer.PixelSize.Y) * px;
        // The head's face meets the bar when flat: the pivot is a hammer's length to the right.
        var pivot = new float2(strike.X + len.X * 0.85f, strike.Y - len.Y * 0.5f);
        var dir = new float2(-MathF.Cos(angle), MathF.Sin(angle));   // from the pivot toward the head
        var up = new float2(-dir.Y, dir.X);
        var back = pivot - dir * (len.X * 0.15f);
        var head = back + dir * len.X;
        var h = up * (len.Y * 0.5f);
        // The sprite runs handle (left) to head (right), so its left edge sits at the far end, the head.
        Gui.ImageQuad(dl, hammer, head - h, back - h, back + h, head + h, Gui.White);
    }

    private void ThrowSparks(float2 at, float grade, int seed)
    {
        _pendingSparks = false;
        var rand = new SimRandom((ulong)(seed * 7919 + 17));
        var count = (int)(6 + 22 * grade);
        for (var i = 0; i < count; i++)
        {
            var angle = rand.Range(-2.9f, -0.25f);
            var speed = Gui.S(rand.Range(60f, 220f)) * (0.5f + grade);
            _sparks.Add(new Spark(at, new float2(MathF.Cos(angle), MathF.Sin(angle)) * speed, _time, rand.Range(0.25f, 0.7f)));
        }
    }

    private void DrawSparks(ImDrawListPtr dl)
    {
        _sparks.RemoveAll(sp => _time - sp.Born > sp.Life);
        var gravity = Gui.S(420f);
        foreach (var sp in _sparks)
        {
            var t = _time - sp.Born;
            var p = sp.Pos + sp.Vel * t + new float2(0, 0.5f * gravity * t * t);
            var tail = p - (sp.Vel + new float2(0, gravity * t)) * 0.018f;
            var fade = 1f - t / sp.Life;
            dl.AddLine(in tail, in p, new ImColor8(255, (byte)(180 + 60 * fade), (byte)(80 * fade), (byte)(255 * fade)), Gui.S(1.6f));
        }
    }

    private void DrawGrade(ImDrawListPtr dl, float2 at, float since)
    {
        var (word, colour) = _strikeGrade switch
        {
            >= 0.95f => ("Clean!", Theme.Good),
            >= 0.7f => ("Good", Theme.Text),
            >= 0.4f => ("Glancing", Theme.Warn),
            _ => ("Off the mark", Theme.Bad),
        };
        var fade = 1f - since / GradeSeconds;
        using (Gui.HeaderFont())
        {
            var ts = ImGui.CalcTextSize(word);
            var p = new float2(at.X - ts.X * 0.5f, at.Y - Gui.S(40) - since * Gui.S(30));
            var shadow = p + new float2(1, 1);
            dl.AddText(in shadow, new ImColor8(0, 0, 0, (byte)(180 * fade)), word);
            dl.AddText(in p, Theme.U8(colour, fade), word);
        }
    }
}
