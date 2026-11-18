using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Afterlife;
using HeroesNotIncluded.Sim.Carpentry;
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

    private void DrawPit(PitSession s)
    {
        BeginWork(s.Title, ("F|Hold the button", "pull the saw down when the top sawyer starts a stroke; let go between"), ("Esc", "climb out"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(360, 140);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // The saw down the left: how far it's pulled this stroke.
        var g0 = new float2(min.X + Gui.S(18), min.Y + Gui.S(12));
        var g1 = new float2(min.X + Gui.S(30), max.Y - Gui.S(12));
        Gui.Trough(dl, g0, g1);
        var y = g0.Y + (g1.Y - g0.Y) * Math.Clamp(s.Down, 0f, 1f);
        dl.AddRectFilled(new float2(g0.X - Gui.S(4), y - Gui.S(3)), new float2(g1.X + Gui.S(4), y + Gui.S(3)), Theme.U8(s.Down >= 1f ? Theme.Good : Theme.Text), Gui.S(1), ImDrawFlags.None);

        var centre = new float2((g1.X + max.X) * 0.5f, min.Y + size.Y * 0.4f);
        if (s.CallOpen)
        {
            var left = 1f - s.CallAge / (float)Math.Max(1, s.Window);
            using (Gui.HeaderFont())
            {
                const string word = "Pull!";
                var ts = ImGui.CalcTextSize(word);
                dl.AddText(centre - ts * 0.5f, Theme.U8(left > 0.66f ? Theme.Good : Theme.Warn), word);
            }
            var b0 = new float2(centre.X - Gui.S(90), centre.Y + Gui.S(22));
            var b1 = new float2(centre.X + Gui.S(90), centre.Y + Gui.S(30));
            Gui.Trough(dl, b0, b1);
            dl.AddRectFilled(b0, new float2(b0.X + (b1.X - b0.X) * Math.Clamp(left, 0f, 1f), b1.Y), Theme.U8(Theme.Good, 0.8f), 0f, ImDrawFlags.None);
        }
        else if (s.LastStep >= 0 && s.StepsDone - s.LastStep < PitSession.WordSteps)
        {
            var (word, colour) = s.LastWas switch
            {
                PitResult.WithIt when s.LastGrade >= 0.7f => ("With the stroke!", Theme.Good),
                PitResult.WithIt => ("Short, or late", Theme.Warn),
                PitResult.Bound => ("Against the saw: it binds!", Theme.Bad),
                _ => ("Missed the stroke", Theme.Warn),
            };
            var ts = ImGui.CalcTextSize(word);
            dl.AddText(centre - ts * 0.5f, Theme.U8(colour), word);
        }
        else
        {
            var wait = $"Wait for {s.LeadName} to start the stroke";
            var ts = ImGui.CalcTextSize(wait);
            dl.AddText(centre - ts * 0.5f, Theme.U8(Theme.Dim), wait);
        }
        TextC(Theme.Dim, $"With the stroke {s.Pulls}, against it {s.Binds}, missed {s.Missed}.");
        EndWork();
    }

    private void DrawLift(LiftSession s)
    {
        BeginWork(s.Title, ("F|Hold the button", "keep the wagon up"), ("A/D", "lean against the sway to hold it level"), ("Esc", "set it down and step away"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(360, 140);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // A level: the bubble is the wagon's lean, the band in the middle is level enough.
        var t0 = new float2(min.X + Gui.S(20), min.Y + Gui.S(46));
        var t1 = new float2(max.X - Gui.S(20), min.Y + Gui.S(62));
        Gui.Trough(dl, t0, t1);
        var mid = (t0.X + t1.X) * 0.5f;
        var half = (t1.X - t0.X) * 0.5f;
        dl.AddRectFilled(new float2(mid - half * s.BandWidth, t0.Y), new float2(mid + half * s.BandWidth, t1.Y), Theme.U8(Theme.Good, 0.35f), 0f, ImDrawFlags.None);
        var bx = mid + half * s.Tilt;
        var bubble = new float2(bx, (t0.Y + t1.Y) * 0.5f);
        dl.AddCircleFilled(in bubble, Gui.S(7), Theme.U8(!s.Up ? Theme.Dim : s.Level ? Theme.Good : Theme.Warn), 20);

        var centre = new float2((min.X + max.X) * 0.5f, min.Y + Gui.S(24));
        var (word, colour) = s.LastStep >= 0 && s.StepsDone - s.LastStep < LiftSession.WordSteps
            ? s.LastLevel ? ("Level: it went home", Theme.Good) : ("It tipped: the hub jolted!", Theme.Bad)
            : s.Spent ? ("Your arms gave: rest them", Theme.Bad)
            : !s.Up ? ("Lift it (hold F)", Theme.Text)
            : s.Level ? ($"Hold it there for {s.LeadName}'s blow", Theme.Dim) : ("It's tipping: lean against it", Theme.Warn);
        var ts = ImGui.CalcTextSize(word);
        dl.AddText(centre - ts * 0.5f, Theme.U8(colour), word);

        // The arms: they tire holding it up and rest with it down.
        var g0 = new float2(min.X + Gui.S(20), max.Y - Gui.S(30));
        var g1 = new float2(max.X - Gui.S(20), max.Y - Gui.S(20));
        Gui.Trough(dl, g0, g1);
        var left = 1f - s.Strain;
        dl.AddRectFilled(g0, new float2(g0.X + (g1.X - g0.X) * left, g1.Y), Theme.U8(left < 0.2f ? Theme.Bad : left < 0.5f ? Theme.Warn : Theme.Good, 0.85f), 0f, ImDrawFlags.None);
        const string arms = "Arms";
        dl.AddText(new float2(g0.X, g0.Y - ImGui.CalcTextSize(arms).Y - Gui.S(2)), Theme.U8(Theme.Dim), arms);
        TextC(Theme.Dim, $"Level for {s.Holds} blows, tipped for {s.Jolts}. Set it down between blows to rest your arms.");
        EndWork();
    }
}
