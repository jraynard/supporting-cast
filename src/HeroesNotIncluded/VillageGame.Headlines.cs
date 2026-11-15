using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

/// <summary>
/// The season's big moments (a message with a <see cref="Message.Headline"/>: a raid, the dragon,
/// a fire, the festival, the end) told large, on the brand's "Not Included" ribbon across the top
/// of the screen: hearth red, swallowtail ends, the headline in the display face and the message
/// under it. One at a time, a few seconds each; a click looks at where it happened.
/// </summary>
public sealed partial class VillageGame
{
    private const float HeadlineSeconds = 7f;

    /// <summary>At most this many wait their turn; a flurry beyond it goes to the toasts instead.</summary>
    private const int HeadlinesQueued = 3;

    private readonly Queue<Message> _headlines = new();
    private Message? _headline;
    private float _headlineAt;

    // The brand's ribbon (art/brand/logo-horizontal.svg): the band and its darker tails.
    private static readonly float4 RibbonRed = new(0.71f, 0.26f, 0.17f, 1f);  // #B5432B
    private static readonly float4 RibbonTail = new(0.54f, 0.18f, 0.12f, 1f); // #8A2F1E

    /// <summary>A headline to announce; false if too many are already waiting (toast it instead).</summary>
    private bool QueueHeadline(Message m)
    {
        if (_headlines.Count >= HeadlinesQueued)
            return false;
        _headlines.Enqueue(m);
        return true;
    }

    private void DrawHeadline()
    {
        if (_headline == null || _time - _headlineAt > HeadlineSeconds)
        {
            _headline = _headlines.Count > 0 ? _headlines.Dequeue() : null;
            _headlineAt = _time;
        }
        if (_headline is not { } m)
            return;
        var age = _time - _headlineAt;
        var alpha = Math.Clamp(MathF.Min(age / 0.25f, (HeadlineSeconds - age) / 0.6f), 0f, 1f);

        // Measure first: the ribbon is sized to its words.
        var title = m.Headline ?? "";
        float2 titleSize;
        using (Gui.DisplayFont(34f))
            titleSize = ImGui.CalcTextSize(title);
        var wrap = MathF.Min(Gui.S(560), Screen.X * 0.6f);
        var textSize = ImGui.CalcTextSize(m.Text, false, wrap);
        var tail = Gui.S(40);        // each swallowtail's length
        var tuck = Gui.S(14);        // how far a tail runs in under the band
        var drop = Gui.S(10);        // the tails hang this much lower
        var notch = Gui.S(16);       // the V cut into a tail's end
        var bandW = MathF.Max(titleSize.X, textSize.X) + Gui.S(72);
        var bandH = Gui.S(12) + titleSize.Y + Gui.S(2) + textSize.Y + Gui.S(14);
        var total = new float2(bandW + 2 * (tail - tuck), bandH + drop);

        _hud.Next(Anchor.Top, total.X);
        var none = float2.Zero;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, in none);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.Begin("##headline"u8, Hud.Flags | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.AlwaysAutoResize);
        ImGui.PopStyleVar(2);
        _hud.Took();

        var dl = ImGui.GetWindowDrawList();
        var o = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##headline-look"u8, in total, ImGuiButtonFlags.None);
        if (clicked)
        {
            if (m.Location is { } at)
                LookAtCell(at);
            _headlineAt = _time - HeadlineSeconds + 0.6f; // and let it go
        }

        var ink = Theme.U8(Theme.Cellar, alpha);
        var line = Gui.S(3);
        // The tails first, behind the band: notched at their outer ends.
        var tailTop = o.Y + drop;
        var tailBottom = o.Y + total.Y;
        Tail(o.X, o.X + tail, notch);
        Tail(o.X + total.X, o.X + total.X - tail, -notch);
        // The band, with the logo's ember highlight along the top and a dark one along the bottom.
        var bandMin = new float2(o.X + tail - tuck, o.Y);
        var bandMax = new float2(bandMin.X + bandW, o.Y + bandH);
        dl.AddRectFilled(in bandMin, in bandMax, Theme.U8(RibbonRed, alpha), 0f, ImDrawFlags.None);
        var hiA = new float2(bandMin.X + line, bandMin.Y + Gui.S(5));
        var hiB = new float2(bandMax.X - line, hiA.Y);
        dl.AddLine(in hiA, in hiB, Theme.U8(Theme.Accent, 0.55f * alpha), Gui.S(2));
        var loA = new float2(bandMin.X + line, bandMax.Y - Gui.S(5));
        var loB = new float2(bandMax.X - line, loA.Y);
        dl.AddLine(in loA, in loB, Theme.U8(Theme.Cellar, 0.35f * alpha), Gui.S(2));
        dl.AddRect(in bandMin, in bandMax, ink, 0f, ImDrawFlags.None, line);

        // The headline in the display face, bone on red with an ink outline; the message under it.
        var mid = (bandMin.X + bandMax.X) * 0.5f;
        var tp = new float2(MathF.Round(mid - titleSize.X * 0.5f), bandMin.Y + Gui.S(10));
        using (Gui.DisplayFont(34f))
        {
            var d = MathF.Max(1f, MathF.Round(Gui.Scale * 1.5f));
            foreach (var off in (ReadOnlySpan<float2>)[new(-d, 0), new(d, 0), new(0, -d), new(0, d), new(d, d)])
            {
                var p = tp + off;
                dl.AddText(in p, ink, title);
            }
            dl.AddText(in tp, Theme.U8(Theme.Text, alpha), title);
        }
        var body = new float2(MathF.Round(mid - textSize.X * 0.5f), tp.Y + titleSize.Y + Gui.S(2));
        ImGui.SetCursorScreenPos(body);
        var bone = new float4(Theme.Text.X, Theme.Text.Y, Theme.Text.Z, alpha);
        ImGui.PushStyleColor(ImGuiCol.Text, in bone);
        ImGui.PushTextWrapPos(body.X - o.X + wrap);
        ImGui.TextWrapped(ImGuiText.Escape(m.Text));
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor(1);
        ImGui.End();

        // A swallowtail from x0 (its notched end) to x1 (under the band); cut's sign says which way the notch goes.
        void Tail(float x0, float x1, float cut)
        {
            var a = new float2(x0, tailTop);
            var b = new float2(x1, tailTop);
            var c = new float2(x1, tailBottom);
            var d = new float2(x0, tailBottom);
            var n = new float2(x0 + cut, (tailTop + tailBottom) * 0.5f);
            var fill = Theme.U8(RibbonTail, alpha);
            dl.AddTriangleFilled(in a, in b, in n, fill);
            dl.AddTriangleFilled(in n, in b, in c, fill);
            dl.AddTriangleFilled(in n, in c, in d, fill);
            dl.AddLine(in a, in b, ink, line);
            dl.AddLine(in b, in c, ink, line);
            dl.AddLine(in c, in d, ink, line);
            dl.AddLine(in d, in n, ink, line);
            dl.AddLine(in n, in a, ink, line);
        }
    }
}
