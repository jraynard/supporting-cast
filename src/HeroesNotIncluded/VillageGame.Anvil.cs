using Brutal.ImGuiApi;
using Narsil.Sim.Core;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Work;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

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

    private const float GradeSeconds = 0.9f;

    // How far across the hammer's drawn at the piece's edge (Offset 1), unscaled pixels.
    private static float HammerShift => Gui.S(90);

    private void DrawHammer(HammerSession s)
    {
        BeginWork(s.Title, ("F|Hold the button", "raise it to the mark, let go to strike"), ("A/D|Move", "keep it over the marker"), ("Esc", "back to the fire"));
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
            _strikeCold = s.LastCold;
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
        TakesPointer(min, max, edges: true); // across: the hammer moved over the piece as the mouse moves (the panel's width is -1 to 1)

        // The forge's light behind it all.
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        var glowTop = Theme.U8(Theme.Accent, 0f);
        var glowBottom = Theme.U8(Theme.Accent, 0.22f);
        var gMin = new float2(min.X + Gui.S(4), min.Y + size.Y * 0.45f);
        var gMax = max - Gui.S(4, 4);
        dl.AddRectFilledMultiColor(in gMin, in gMax, glowTop, glowTop, glowBottom, glowBottom);

        var px = MathF.Max(1f, MathF.Round(3f * Gui.Scale));
        if (Gui.TrySprite("ui/work/anvil", out var anvil))
        {
            var a = new float2(anvil.PixelSize.X, anvil.PixelSize.Y) * px;
            var aMin = new float2(min.X + size.X * 0.5f - a.X * 0.55f, max.Y - a.Y - Gui.S(6)) + jolt;
            Gui.Image(dl, anvil, aMin, aMin + a, Gui.White);
            // The bar on the anvil's face, glowing as hot as it still is: as long as anywhere the marker
            // may go, and drawn out longer as it's shaped (the marker ranging further with it).
            var face = new float2(aMin.X + 15 * px, aMin.Y + 4 * px);
            if (Gui.TrySprite("ui/work/billet", out var billet))
            {
                var bh = billet.PixelSize.Y * px;
                var strikeX = face.X + 2 * px + billet.PixelSize.X * px * 0.6f;
                var halfLen = (s.SpotReach + s.CentreWidth + 0.1f) * HammerShift;
                var b = new float2(halfLen * 2f, bh);
                var bMin = new float2(strikeX - halfLen, face.Y - bh + px);
                var hot = GlowColour(s.Temp);
                var glowAt = bMin + b * 0.5f;
                dl.AddEllipseFilled(in glowAt, new float2(b.X * 0.62f, b.X * 0.3f), Gui.U8(hot, 0.25f * GlowStrength(s.Temp)), 0f, 32);
                DrawBar(dl, bMin, b, px, hot);
                if (since < 0.08f)
                    dl.AddRectFilled(in bMin, bMin + b, new ImColor8(255, 255, 240, (byte)(220 * (1f - since / 0.08f))), 0f, ImDrawFlags.None);
                var strikePoint = new float2(strikeX, bMin.Y);
                if (_pendingSparks)
                    ThrowSparks(strikePoint, _strikeGrade, s.Blows);
                // The spot to strike, a notch over the bar, green while the hammer's over it; it moves after every blow.
                var spotX = strikePoint.X + s.Spot * HammerShift;
                var mid0 = new float2(spotX, bMin.Y - Gui.S(3));
                var mid1 = new float2(spotX, bMin.Y + b.Y);
                var spotColour = Theme.U8(s.Centred ? Theme.Good : Theme.Accent, s.Centred ? 0.9f : 0.7f);
                dl.AddLine(in mid0, in mid1, spotColour, Gui.S(2f));
                dl.AddTriangleFilled(new float2(spotX - Gui.S(6), bMin.Y - Gui.S(12)), new float2(spotX + Gui.S(6), bMin.Y - Gui.S(12)), new float2(spotX, bMin.Y - Gui.S(4)), spotColour);
                // A ghost of the blow to aim for: as high, over the spot; green while the hammer's raised into the band.
                var inBand = s.Ready && !s.Swinging && s.Raise > 0f && s.InBand;
                DrawHammerSwing(dl, s.Mark, new float2(spotX, strikePoint.Y), px, inBand ? Theme.U8(Theme.Good, 0.45f) : Gui.U8(new float4(1f, 1f, 1f, 0.22f)));
                // Where it would fall: a faint line from the face down to the bar, green over the marker.
                var fallX = strikePoint.X + s.Offset * HammerShift;
                if (s.Ready)
                    dl.AddLine(new float2(fallX, bMin.Y - Gui.S(30)), new float2(fallX, bMin.Y + b.Y), Theme.U8(s.Centred ? Theme.Good : Theme.Text, s.Centred ? 0.7f : 0.35f), Gui.S(1.5f));
                DrawHammerSwing(dl, s.Raise, new float2(fallX, strikePoint.Y), px, Gui.White);
                // Great work: the striker's sledge across the anvil, as they're holding it (heaved, coming down, resting).
                if (s.Heavy && _sim.World.TryGet<WorkSession>(s.Partner, out var helper) && helper is SledgeSession sledge && helper.Partner == _avatar)
                    DrawSledgeSwing(dl, sledge.Raise, new float2(spotX, strikePoint.Y), px, sledge.Raise >= SledgeSession.FullHeave ? Gui.White : Gui.U8(new float4(1f, 1f, 1f, 0.75f)));
                DrawSparks(dl);
                if (since < GradeSeconds)
                    DrawGrade(dl, strikePoint, since);
            }
        }

        // The wind, up the right of the anvil: the mark's band, the hammer now, and for a moment the top of the last wind.
        var gx = max.X - Gui.S(16);
        var g0 = new float2(gx - Gui.S(5), min.Y + Gui.S(10));
        var g1 = new float2(gx + Gui.S(5), max.Y - Gui.S(10));
        Gui.Trough(dl, g0, g1);
        float Y(float f) => g1.Y - (g1.Y - g0.Y) * Math.Clamp(f, 0f, 1f);
        var windIn = s.Ready && !s.Swinging && s.Raise > 0f && s.InBand;
        dl.AddRectFilled(new float2(g0.X, Y(s.BandHigh)), new float2(g1.X, Y(s.BandLow)), windIn ? Theme.U8(Theme.Good, 0.7f) : Theme.U8(Theme.Accent, 0.55f), 0f, ImDrawFlags.None);
        if (since < GradeSeconds && s.LastWind >= 0f)
        {
            var top = Y(s.LastWind);
            dl.AddRectFilled(new float2(g0.X - Gui.S(2), top - Gui.S(1.5f)), new float2(g1.X + Gui.S(2), top + Gui.S(1.5f)),
                Theme.U8(s.LastWindWas == 0 ? Theme.Good : s.LastWindWas == 1 ? Theme.Warn : Theme.Bad, 0.9f * (1f - since / GradeSeconds)), 0f, ImDrawFlags.None);
        }
        var now = Y(s.Raise);
        dl.AddRectFilled(new float2(g0.X - Gui.S(4), now - Gui.S(2)), new float2(g1.X + Gui.S(4), now + Gui.S(2)), Theme.U8(s.Ready ? Theme.Text : Theme.Dim), Gui.S(1), ImDrawFlags.None);

        // How far it's been beaten into shape, over every heat; and a warning as the colour goes.
        ImGui.Spacing();
        var sMin = ImGui.GetCursorScreenPos();
        var sSize = Gui.S(420, 10);
        ImGui.Dummy(in sSize);
        var sMax = sMin + sSize;
        Gui.Trough(dl, sMin, sMax);
        var fill = new float2(sMin.X + Gui.S(2) + (sSize.X - Gui.S(4)) * Math.Clamp(s.Shape, 0f, 1f), sMax.Y - Gui.S(2));
        var fMin = sMin + Gui.S(2, 2);
        dl.AddRectFilled(in fMin, in fill, Theme.U8(Theme.Accent, 0.8f), 0f, ImDrawFlags.None);
        if (s.Workable < 0.95f)
            TextC(s.Workable < 0.5f ? Theme.Bad : Theme.Warn, s.Workable < 0.5f
                ? "It's going black: blows now do it no good, and may crack it. Back to the fire."
                : "The colour's fading: it's moving less under the hammer.");
        else if (since < GradeSeconds * 2 && s.LastWind >= 0f && (s.LastWindWas != 0 || s.LastOffCentre))
            TextC(s.LastWindWas > 1 ? Theme.Bad : Theme.Warn, s.LastWindWas > 1 ? "Raised too far: it came down too hard, and did little."
                : s.LastWindWas > 0 ? "A touch past the mark: a heavy blow, not quite true."
                : s.LastWindWas < 0 ? "Short of the mark: it moved the metal less." : "Off the marker: it glanced.");
        else
            TextC(Theme.Dim, s.Repairing ? "Hammering out the chips: over the marker, raise it to the mark, let go." : "Keep it over the marker, raise it to the mark (not past it), and let go.");
        DrawStrikerLine(s);
        EndWork();
    }

    private bool _pendingSparks, _strikeCold;

    /// <summary>
    /// The bar being worked, any length: the billet's art drawn out (its rows pale to dark top to
    /// bottom, its ends and underside darker), tinted by its heat.
    /// </summary>
    private static void DrawBar(ImDrawListPtr dl, float2 at, float2 size, float px, float4 hot)
    {
        float[] rows = [255f, 255f, 225f, 225f, 190f, 150f];
        var row = size.Y / rows.Length;
        for (var y = 0; y < rows.Length; y++)
        {
            var shade = new float4(hot.X * rows[y] / 255f, hot.Y * rows[y] / 255f, hot.Z * rows[y] / 255f, hot.W);
            dl.AddRectFilled(at + new float2(0f, y * row), at + new float2(size.X, (y + 1) * row), Gui.U8(shade), 0f, ImDrawFlags.None);
        }
        var end = new float4(hot.X * 150f / 255f, hot.Y * 150f / 255f, hot.Z * 150f / 255f, hot.W);
        dl.AddRectFilled(at, at + new float2(px, size.Y), Gui.U8(end), 0f, ImDrawFlags.None);
        dl.AddRectFilled(at + new float2(size.X - px, 0f), at + size, Gui.U8(end), 0f, ImDrawFlags.None);
    }

    /// <summary>
    /// The hammer, pivoting at the smith's hand off to the right, wound back by <paramref name="raise"/>
    /// (0 flat on the bar, 1 as far back as it goes): the smith's own swing, drawn as it's made.
    /// </summary>
    private void DrawHammerSwing(ImDrawListPtr dl, float raise, float2 strike, float px, ImColor8 tint) =>
        DrawToolSwing(dl, "ui/work/hammer", 26f / 32f, raise, strike, px, tint);

    /// <summary>
    /// A tool lying flat in its sprite (handle at the left, head at the right) swung about the hand:
    /// raised up off the work by <paramref name="raise"/> (0 resting on it), the middle of its striking
    /// face (<paramref name="faceAlong"/> of the way along) on <paramref name="strike"/> when flat.
    /// The smith's hammer at the anvil, the axe at the log.
    /// </summary>
    private void DrawToolSwing(ImDrawListPtr dl, string spriteName, float faceAlong, float raise, float2 strike, float px, ImColor8 tint, bool fromLeft = false)
    {
        if (!Gui.TrySprite(spriteName, out var hammer))
            return;
        var angle = -Math.Clamp(raise, 0f, 1f) * 1.15f; // radians up from resting flat on the bar
        var len = new float2(hammer.PixelSize.X, hammer.PixelSize.Y) * px;
        // Flat, the middle of the head's face sits on the strike point: the pivot's to the right of that.
        var pivot = new float2(strike.X + len.X * (faceAlong - 0.15f), strike.Y - len.Y * 0.5f);
        var dir = new float2(-MathF.Cos(angle), MathF.Sin(angle));   // from the pivot toward the head
        var up = new float2(-dir.Y, dir.X);
        var back = pivot - dir * (len.X * 0.15f);
        var head = back + dir * len.X;
        var h = up * (len.Y * 0.5f);
        // The sprite runs handle (u=0) to iron head (u=1): the handle sits at the hand and the head
        // at the far end, on the bar. `h` points up off the shaft, where the sprite's top row goes.
        // From the left (the striker across the anvil from the smith): the same swing, mirrored about the strike point.
        float2 M(float2 p) => fromLeft ? new float2(2f * strike.X - p.X, p.Y) : p;
        Gui.ImageQuad(dl, hammer, M(back + h), M(head + h), M(head - h), M(back - h), tint);
    }

    /// <summary>The striker's sledge, across the anvil from the smith: a bigger swing from the left.</summary>
    private void DrawSledgeSwing(ImDrawListPtr dl, float raise, float2 strike, float px, ImColor8 tint) =>
        DrawToolSwing(dl, "ui/work/hammer", 26f / 32f, raise, strike, px * 1.45f, tint, fromLeft: true);

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
        var (word, colour) = _strikeCold ? ("Cold iron!", Theme.Bad) : _strikeGrade switch
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
