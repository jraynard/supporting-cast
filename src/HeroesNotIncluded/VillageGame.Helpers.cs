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
        BeginWork(s.Title, ("F|Hold the button", "brace as each pull peaks; ease off between"), ("Esc", "let go of them"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(360, 140);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // The pull, across the middle: a track from the rune keyed to the pull ebbing, the peak's
        // band to brace through, and the pull now as a marker swelling toward it.
        var t0 = new float2(min.X + Gui.S(20), min.Y + Gui.S(46));
        var t1 = new float2(max.X - Gui.S(20), min.Y + Gui.S(58));
        Gui.Trough(dl, t0, t1);
        var span = HoldSession.PeakAt * 2f;
        float X(float age) => t0.X + (t1.X - t0.X) * Math.Clamp(age / span, 0f, 1f);
        dl.AddRectFilled(new float2(X(HoldSession.PeakAt - s.Half), t0.Y), new float2(X(HoldSession.PeakAt + s.Half), t1.Y),
            Theme.U8(Theme.Arcane, 0.55f), 0f, ImDrawFlags.None);
        var centre = new float2((min.X + max.X) * 0.5f, min.Y + Gui.S(24));
        if (s.PullAge >= 0)
        {
            var x = X(s.PullAge);
            var colour = s.AtPeak ? (s.Held ? Theme.Good : Theme.Warn) : Theme.Text;
            dl.AddRectFilled(new float2(x - Gui.S(3), t0.Y - Gui.S(6)), new float2(x + Gui.S(3), t1.Y + Gui.S(6)), Theme.U8(colour), Gui.S(1), ImDrawFlags.None);
            var word = s.AtPeak ? (s.Held ? "Hold!" : "Brace now!") : s.PullAge < HoldSession.PeakAt ? "The grave pulls..." : "It ebbs";
            using (Gui.HeaderFont())
            {
                var ts = ImGui.CalcTextSize(word);
                dl.AddText(centre - ts * 0.5f, Theme.U8(s.AtPeak ? Theme.Good : Theme.Dim), word);
            }
        }
        else if (s.LastStep >= 0 && s.StepsDone - s.LastStep < HoldSession.WordSteps)
        {
            var (word, colour) = s.LastWas == HoldResult.Held ? ("Held them", Theme.Good) : ("Your hold slipped!", Theme.Bad);
            var ts = ImGui.CalcTextSize(word);
            dl.AddText(centre - ts * 0.5f, Theme.U8(colour), word);
        }
        else
        {
            var wait = $"Watch {s.LeadName}'s runes: each one, the grave pulls";
            var ts = ImGui.CalcTextSize(wait);
            dl.AddText(centre - ts * 0.5f, Theme.U8(Theme.Dim), wait);
        }

        // The grip: it tires while you brace and comes back while you ease.
        var g0 = new float2(min.X + Gui.S(20), max.Y - Gui.S(30));
        var g1 = new float2(max.X - Gui.S(20), max.Y - Gui.S(20));
        Gui.Trough(dl, g0, g1);
        var gripColour = s.Grip <= HoldSession.WeakGrip ? Theme.Bad : s.Grip < 0.5f ? Theme.Warn : Theme.Good;
        dl.AddRectFilled(g0, new float2(g0.X + (g1.X - g0.X) * s.Grip, g1.Y), Theme.U8(gripColour, 0.85f), 0f, ImDrawFlags.None);
        var label = "Grip";
        dl.AddText(new float2(g0.X, g0.Y - ImGui.CalcTextSize(label).Y - Gui.S(2)), Theme.U8(Theme.Dim), label);

        if (s.Grip <= HoldSession.WeakGrip)
            TextC(Theme.Warn, "Your grip's spent: ease off between pulls to get it back.");
        else
            TextC(Theme.Dim, $"Held {s.Holds}, slipped {s.Slips}.");
        EndWork();
    }


}
