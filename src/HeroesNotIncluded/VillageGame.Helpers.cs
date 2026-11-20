using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Afterlife;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The helpers' panels for two pairs of hands (Sim.Work.Pairs) beyond the sledge: the keeper at a
// Calling back (the grave's pull to brace through), the pitman under a great log (the stroke to pull
// with) and the lifter at a wagon (the weight to hold level). Everything here is read; nothing touches the sim.
public sealed partial class VillageGame
{
    private void DrawHold(HoldSession s)
    {
        BeginWork(s.Title, ("F|Hold the button", "press them down as the pull peaks; ease off between"), ("Esc", "let go of them"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 210);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        DrawHoldScene(dl, min, max, s, s.Held);

        // The cue, big across the top: the pull rising, its peak (hold them down), then easing off.
        var centre = new float2((min.X + max.X) * 0.5f, min.Y + Gui.S(18));
        string word;
        float4 colour;
        var big = false;
        if (s.PullAge >= 0)
            (word, colour, big) = s.AtPeak
                ? (s.Held ? ("Hold them!", Theme.Good, true) : ("Hold them down!", Theme.Warn, true))
                : s.PullAge < HoldSession.PeakAt ? ("The pull's rising...", Theme.Arcane, false) : ("Ease off", Theme.Dim, false);
        else if (s.LastStep >= 0 && s.StepsDone - s.LastStep < HoldSession.WordSteps)
            (word, colour) = s.LastWas == HoldResult.Held ? ("Held them through it", Theme.Good) : ("They slipped from you!", Theme.Bad);
        else
            (word, colour) = ($"Rest your hands. Each rune {s.LeadName} keys, the grave pulls", Theme.Dim);
        if (big)
            using (Gui.HeaderFont())
            {
                var ts = ImGui.CalcTextSize(word);
                dl.AddText(centre - ts * 0.5f, Theme.U8(colour), word);
            }
        else
        {
            var ts = ImGui.CalcTextSize(word);
            dl.AddText(centre - ts * 0.5f, Theme.U8(colour), word);
        }

        // The pull's course under the scene: it swells to the peak's band, then ebbs.
        var t0 = new float2(min.X + Gui.S(40), max.Y - Gui.S(16));
        var t1 = new float2(max.X - Gui.S(16), max.Y - Gui.S(9));
        Gui.Trough(dl, t0, t1);
        var span = HoldSession.PeakAt * 2f;
        float X(float age) => t0.X + (t1.X - t0.X) * Math.Clamp(age / span, 0f, 1f);
        dl.AddRectFilled(new float2(X(HoldSession.PeakAt - s.Half), t0.Y), new float2(X(HoldSession.PeakAt + s.Half), t1.Y),
            Theme.U8(Theme.Arcane, 0.55f), 0f, ImDrawFlags.None);
        if (s.PullAge >= 0)
        {
            var x = X(s.PullAge);
            var mark = s.AtPeak ? (s.Held ? Theme.Good : Theme.Warn) : Theme.Text;
            dl.AddRectFilled(new float2(x - Gui.S(3), t0.Y - Gui.S(5)), new float2(x + Gui.S(3), t1.Y + Gui.S(5)), Theme.U8(mark), Gui.S(1), ImDrawFlags.None);
        }

        // The grip, up the left edge: it tires while you press and comes back while you ease.
        var g0 = new float2(min.X + Gui.S(14), min.Y + Gui.S(40));
        var g1 = new float2(min.X + Gui.S(24), max.Y - Gui.S(12));
        Gui.Trough(dl, g0, g1);
        var gripColour = s.Grip <= HoldSession.WeakGrip ? Theme.Bad : s.Grip < 0.5f ? Theme.Warn : Theme.Good;
        dl.AddRectFilled(new float2(g0.X, g1.Y - (g1.Y - g0.Y) * s.Grip), g1, Theme.U8(gripColour, 0.85f), 0f, ImDrawFlags.None);

        if (s.Grip <= HoldSession.WeakGrip)
            TextC(Theme.Warn, "Your grip's spent: ease off between pulls to get it back.");
        else
            TextC(Theme.Dim, $"Held {s.Holds}, slipped {s.Slips}.");
        EndWork();
    }

    /// <summary>
    /// The body on the pallet as the keeper and the empath both see it: a pale glow over it as the grave pulls,
    /// the body lifting toward it, and the keeper's hands on its shoulders, pressing it down while they hold.
    /// </summary>
    private void DrawHoldScene(ImDrawListPtr dl, float2 min, float2 max, HoldSession s, bool pressing)
    {
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        var w = max.X - min.X;
        var pull = s.Pull;
        var pinned = pressing && s.Grip > HoldSession.WeakGrip;

        // The pallet: a low frame with a blanket.
        var palletY = max.Y - Gui.S(44);
        var p0 = new float2(min.X + w * 0.16f, palletY);
        var p1 = new float2(max.X - w * 0.1f, palletY + Gui.S(12));
        dl.AddRectFilled(p0, p1, Theme.U8(new float4(0.42f, 0.3f, 0.2f, 1f)), Gui.S(2), ImDrawFlags.None);
        dl.AddRectFilled(new float2(p0.X + Gui.S(6), p1.Y), new float2(p0.X + Gui.S(12), p1.Y + Gui.S(10)), Theme.U8(new float4(0.32f, 0.22f, 0.15f, 1f)), 0f, ImDrawFlags.None);
        dl.AddRectFilled(new float2(p1.X - Gui.S(12), p1.Y), new float2(p1.X - Gui.S(6), p1.Y + Gui.S(10)), Theme.U8(new float4(0.32f, 0.22f, 0.15f, 1f)), 0f, ImDrawFlags.None);

        // The grave's pull: a cold glow over the chest, wisps drawing up from the body toward it.
        var feet = new float2(p0.X + Gui.S(20), palletY - Gui.S(6));
        var restChest = new float2(p0.X + (p1.X - p0.X) * 0.62f, palletY - Gui.S(9));
        var lift = Gui.S(30) * pull * (pinned ? 0.18f : 1f);
        var chest = restChest - new float2(0f, lift);
        var head = new float2(p1.X - Gui.S(22), palletY - Gui.S(12) - lift * 0.7f);
        var glow = new float2(restChest.X, min.Y + Gui.S(52));
        if (pull > 0f)
        {
            dl.AddCircleFilled(glow, Gui.S(14 + 26 * pull), Theme.U8(Theme.Arcane, 0.12f + 0.25f * pull), 32);
            dl.AddCircleFilled(glow, Gui.S(6 + 8 * pull), Theme.U8(Theme.Arcane, 0.35f + 0.4f * pull), 24);
            for (var i = 0; i < 4; i++)
            {
                var from = chest + new float2(Gui.S(-18 + 12 * i), 0f);
                var sway = MathF.Sin(_time * 4f + i * 1.7f) * Gui.S(8);
                dl.AddBezierCubic(from, from + new float2(sway, -Gui.S(20)), glow + new float2(-sway, Gui.S(24)), glow,
                    Theme.U8(Theme.Arcane, 0.25f + 0.5f * pull), Gui.S(1.5f), 16);
            }
        }

        // The body, lying feet to the left: it arches toward the glow unless it's held.
        var bodyCol = Theme.U8(new float4(0.7f, 0.68f, 0.64f, 1f));
        var hip = new float2(feet.X + (chest.X - feet.X) * 0.45f, palletY - Gui.S(7) - lift * 0.35f);
        dl.AddLine(feet, hip, bodyCol, Gui.S(7));
        dl.AddLine(hip, chest, bodyCol, Gui.S(10));
        dl.AddLine(chest, head, bodyCol, Gui.S(6));
        dl.AddCircleFilled(head, Gui.S(9), bodyCol, 16);
        if (pull > 0.2f && !pinned)
            dl.AddCircle(head, Gui.S(12), Theme.U8(Theme.Arcane, 0.6f * pull), 16, Gui.S(1.5f));

        // The keeper's hands on the shoulders, arms from the near side: pressed and lit while held, hovering when not.
        var handCol = pinned ? Theme.Good : pressing ? Theme.Warn : Theme.Text;
        var hover = pressing ? 0f : Gui.S(18);
        var shoulders = new[] { chest + new float2(Gui.S(-10), -Gui.S(7) - hover), new float2(head.X - Gui.S(18), head.Y - Gui.S(4) - hover) };
        foreach (var hand in shoulders)
        {
            dl.AddLine(new float2(hand.X - Gui.S(30), max.Y - Gui.S(4)), hand, Theme.U8(handCol, 0.8f), Gui.S(5));
            dl.AddCircleFilled(hand, Gui.S(6), Theme.U8(handCol), 12);
            if (pinned)
                dl.AddCircle(hand, Gui.S(9 + 3 * pull), Theme.U8(Theme.Good, 0.4f), 12, Gui.S(1.5f));
        }
        if (s.LastStep >= 0 && s.StepsDone - s.LastStep < 10 && s.LastWas == HoldResult.Slipped)
            dl.AddCircleFilled(chest, Gui.S(16) * (1f - (s.StepsDone - s.LastStep) / 10f), Theme.U8(Theme.Bad, 0.5f), 16);
    }
}
