using Brutal.ImGuiApi;
using HeroesNotIncluded.Ui;
using HeroesNotIncluded.Sim.Work;
using Narsil.Ui;

namespace HeroesNotIncluded.Musician;

// The instrument in your hands, at the top of the playing panel: what you're doing as you play it. The pipe's holes
// covered by your fingers for the note, breath leaving its end; the lute's and the fiddle's strings, the fretting finger
// on the note's string and the string ringing (plucked dies away, bowed rings while the bow moves); the drum's head and
// the hand that strikes it, where it strikes. Read from the session; nothing touches the sim.
public sealed partial class MusicianGame
{
    /// <summary>Steps a plucked string or a drum's head is seen ringing after it's struck.</summary>
    private const int RingSteps = 40;

    private static readonly float4 Skin = new(0.86f, 0.68f, 0.54f, 1f);

    private static void DrawHands(PlaySession s)
    {
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = new float2(Gui.S(LaneWidth), Gui.S(92));
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        var last = s.Notes.Count > 0 ? s.Notes[^1] : (NoteEvent?)null;
        var age = last is { } n ? s.StepsDone - n.Step : int.MaxValue;
        var ringing = s.RingingNote;
        var t = (float)ImGui.GetTime();
        if (s.Def.IsDrum)
            DrawDrum(dl, min, max, s, last, age, t);
        else if (s.Def.Strings.Count > 0)
            DrawStrings(dl, min, max, s, last, ringing, age, t);
        else
            DrawPipe(dl, min, max, s, ringing ?? last, ringing != null, t);
    }

    /// <summary>The pipe: six holes, covered from the top down for the low note and opened one by one going up (a tin whistle's fingering).</summary>
    private static void DrawPipe(ImDrawListPtr dl, float2 min, float2 max, PlaySession s, NoteEvent? note, bool sounding, float t)
    {
        var y = (min.Y + max.Y) * 0.5f;
        var x0 = min.X + Gui.S(40);
        var x1 = max.X - Gui.S(70);
        var r = Gui.S(8);
        dl.AddRectFilled(new float2(x0, y - r), new float2(x1, y + r), Theme.U8(new float4(0.72f, 0.72f, 0.7f, 1f)), r, ImDrawFlags.None);
        dl.AddRectFilled(new float2(x0 - Gui.S(22), y - r - Gui.S(1)), new float2(x0 + Gui.S(18), y + r + Gui.S(1)), Theme.U8(Theme.Barrel), Gui.S(4), ImDrawFlags.None); // the mouthpiece
        dl.AddRectFilled(new float2(x0 + Gui.S(4), y - Gui.S(3)), new float2(x0 + Gui.S(12), y), Theme.U8(Theme.Cellar), 0f, ImDrawFlags.None);    // its window
        var degree = note?.Degree ?? 0;
        var covered = degree <= 6 ? 6 - degree : 6;
        var half = note is { Accidental: > 0 };
        var high = note is { } h && (h.Octave > 0 || h.Degree >= 7);
        for (var i = 0; i < 6; i++)
        {
            var hx = x0 + Gui.S(56) + i * (x1 - x0 - Gui.S(80)) / 5f;
            var hole = new float2(hx, y);
            dl.AddCircleFilled(in hole, Gui.S(4), Theme.U8(Theme.Cellar), 12);
            var on = note != null && i < covered;
            if (on || (half && i == covered))
            {
                var finger = new float2(hx, y - Gui.S(on ? 1 : 3));
                dl.AddCircleFilled(in finger, Gui.S(on ? 7 : 5), Theme.U8(Skin), 14);
                dl.AddCircle(in finger, Gui.S(on ? 7 : 5), Theme.U8(Theme.Cellar, 0.6f), 14, Gui.S(1));
            }
        }
        // Breath out of the far end while it sounds: fuller swelled, brighter overblown for the high notes.
        if (sounding)
        {
            var amp = s.Swelling ? Gui.S(7) : Gui.S(4);
            var colour = Theme.U8(high ? Theme.Accent : Theme.Parchment, 0.6f);
            for (var k = 0; k < 3; k++)
            {
                var off = (k - 1) * Gui.S(7);
                var p0 = new float2(x1 + Gui.S(4), y + off);
                var wob = MathF.Sin(t * 9f + k) * amp;
                dl.AddBezierCubic(p0, p0 + new float2(Gui.S(18), wob), p0 + new float2(Gui.S(36), -wob), p0 + new float2(Gui.S(56), wob * 0.5f), colour, Gui.S(1.5f), 16);
            }
        }
    }

    /// <summary>The neck from above, low string at the bottom: the fretting finger on the note's string, that string ringing; a lute's body, or a fiddle's and its bow.</summary>
    private static void DrawStrings(ImDrawListPtr dl, float2 min, float2 max, PlaySession s, NoteEvent? last, NoteEvent? ringing, int age, float t)
    {
        var bowed = s.Def.Sustains;
        var strings = s.Def.Strings;
        var nut = min.X + Gui.S(24);
        var bodyX = max.X - Gui.S(bowed ? 110 : 120);
        var top = min.Y + Gui.S(18);
        var bottom = max.Y - Gui.S(18);
        // The neck and the body.
        var bodyC = new float2(bodyX + Gui.S(bowed ? 40 : 50), (top + bottom) * 0.5f);
        var bodyR = new float2(Gui.S(bowed ? 48 : 60), (bottom - top) * 0.5f + Gui.S(14));
        dl.AddEllipseFilled(bodyC, bodyR, Theme.U8(Theme.Barrel), 0f, 32);
        dl.AddEllipse(bodyC, bodyR, Theme.U8(Theme.OakLight), 0f, 32, Gui.S(2));
        dl.AddRectFilled(new float2(nut - Gui.S(8), top - Gui.S(8)), new float2(bodyX, bottom + Gui.S(8)), Theme.U8(Theme.Oak), Gui.S(3), ImDrawFlags.None);
        if (!bowed)
            dl.AddCircleFilled(bodyC - new float2(Gui.S(14), 0f), Gui.S(12), Theme.U8(Theme.Cellar), 20); // the lute's rose
        for (var f = 1; f <= 7; f++) // frets (a fiddle's are only where a finger goes)
        {
            var fx = nut + (bodyX - nut) * f / 8f;
            dl.AddLine(new float2(fx, top - Gui.S(6)), new float2(fx, bottom + Gui.S(6)), Theme.U8(Theme.OakLight, bowed ? 0.15f : 0.6f), Gui.S(1));
        }
        var on = last is { } n ? s.Def.StringOf(n) : -1;
        var chord = s.Chord >= 0 && age < RingSteps;
        var end = bodyC.X + Gui.S(bowed ? 30 : 34);
        for (var i = 0; i < strings.Count; i++)
        {
            var y = bottom - (bottom - top) * i / Math.Max(1, strings.Count - 1);
            var broken = (s.Broken & (1 << i)) != 0;
            var rings = bowed ? ringing != null && i == on : (i == on || chord) && age < RingSteps;
            var amp = rings ? Gui.S(bowed ? 1.8f : 3f) * (bowed ? 1f : 1f - age / (float)RingSteps) : 0f;
            var colour = Theme.U8(broken ? Theme.Bad : rings ? Theme.Accent : Theme.Parchment, broken ? 0.5f : 0.9f);
            if (amp <= 0f || broken)
                dl.AddLine(new float2(nut, y), new float2(broken ? (nut + end) * 0.5f : end, y), colour, Gui.S(1.5f));
            else
            {
                var prev = new float2(nut, y);
                for (var k = 1; k <= 24; k++)
                {
                    var x = nut + (end - nut) * k / 24f;
                    var p = new float2(x, y + MathF.Sin(k / 24f * MathF.PI) * MathF.Sin(t * 60f + k) * amp);
                    dl.AddLine(prev, p, colour, Gui.S(1.5f));
                    prev = p;
                }
            }
        }
        // The fretting finger: on the note's string, as far up the neck as its pitch over the string's.
        if (last is { } note && on >= 0 && (ringing != null || age < RingSteps))
        {
            var fret = Math.Clamp(s.Def.SemisOf(note) - strings[on].Semis, 0, 7);
            if (fret > 0)
            {
                var y = bottom - (bottom - top) * on / Math.Max(1, strings.Count - 1);
                var fx = nut + (bodyX - nut) * (fret - 0.5f) / 8f;
                dl.AddCircleFilled(new float2(fx, y), Gui.S(6), Theme.U8(Skin), 14);
                dl.AddCircle(new float2(fx, y), Gui.S(6), Theme.U8(Theme.Cellar, 0.6f), 14, Gui.S(1));
            }
        }
        if (bowed)
        {
            // The bow across the strings by the bridge, drawn back and forth while a note sounds.
            var stroke = ringing != null ? MathF.Sin(t * 3.2f) * Gui.S(40) : 0f;
            var bx = bodyC.X - Gui.S(4);
            var b0 = new float2(bx + Gui.S(30), top - Gui.S(14) + stroke);
            var b1 = new float2(bx - Gui.S(30), bottom + Gui.S(14) + stroke);
            dl.AddLine(b0, b1, Theme.U8(Theme.Parchment, 0.8f), Gui.S(2));
            dl.AddLine(b0 + new float2(Gui.S(4), 0f), b1 + new float2(Gui.S(4), 0f), Theme.U8(Theme.Barrel), Gui.S(3));
            dl.AddCircleFilled(b1 + new float2(Gui.S(2), Gui.S(4)), Gui.S(6), Theme.U8(Skin), 12); // the bow hand
        }
        else if (last != null && age < 12)
        {
            // The plucking hand over the rose, at the string (or sweeping all of them for a strum).
            var y = chord ? top + (bottom - top) * (s.StrumUp ? 1f - age / 12f : age / 12f) : bottom - (bottom - top) * Math.Max(0, on) / Math.Max(1, strings.Count - 1);
            dl.AddCircleFilled(new float2(bodyC.X - Gui.S(14), y), Gui.S(7), Theme.U8(Skin), 14);
        }
    }

    /// <summary>The frame drum from above: the hand coming down where the stroke is played (the deep middle, the bright edge, the rim), and the head ringing.</summary>
    private static void DrawDrum(ImDrawListPtr dl, float2 min, float2 max, PlaySession s, NoteEvent? last, int age, float t)
    {
        var c = (min + max) * 0.5f;
        var r = (max.Y - min.Y) * 0.5f - Gui.S(8);
        dl.AddCircleFilled(c, r + Gui.S(5), Theme.U8(Theme.Oak), 40);
        dl.AddCircleFilled(c, r, Theme.U8(new float4(0.86f, 0.8f, 0.64f, 1f)), 40);
        var stroke = last?.Degree ?? 0;
        var at = stroke switch { 0 => c, 1 => c + new float2(r * 0.7f, 0f), _ => c - new float2(r * 0.92f, 0f) };
        if (last != null && age < RingSteps)
        {
            var k = age / (float)RingSteps;
            dl.AddCircle(at, Gui.S(6) + r * k, Theme.U8(Theme.Cellar, 0.5f * (1f - k)), 32, Gui.S(1.5f));
            if (stroke == 0)
                dl.AddCircle(at, Gui.S(3) + r * k * 0.6f, Theme.U8(Theme.Cellar, 0.4f * (1f - k)), 32, Gui.S(1.5f));
        }
        // The hand: pressed to the head for a few steps after the stroke, lifted between.
        var lifted = last == null || age > 8;
        var hand = at + new float2(0f, lifted ? -Gui.S(14) : 0f);
        dl.AddCircleFilled(hand, Gui.S(lifted ? 9 : 8), Theme.U8(Skin, lifted ? 0.7f : 1f), 16);
        dl.AddCircle(hand, Gui.S(lifted ? 9 : 8), Theme.U8(Theme.Cellar, 0.6f), 16, Gui.S(1));
        if (s.Def.Bend > 0f && s.Bend > 0.05f) // the other hand pressing the head, for a higher note
            dl.AddCircleFilled(c - new float2(r * 0.4f, -r * 0.3f), Gui.S(7), Theme.U8(Skin), 14);
    }
}
