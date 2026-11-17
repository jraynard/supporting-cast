using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Work;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The striker at the anvil (Sim.Work.Pairs): the sledge's heave up the side, the smith's call as a
// draining "Now!", and the piece the two of them are beating out, read from the smith's session.
// Everything here is read; nothing touches the sim.
public sealed partial class VillageGame
{
    /// <summary>Steps a word on the last blow stays up.</summary>
    private const int SledgeWordSteps = 60;

    private void DrawSledge(SledgeSession s)
    {
        BeginWork(s.Title, ("F|Hold the button", "heave the sledge up; let go when the smith taps"), ("Esc", "step away"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(360, 150);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // The heave, up the left: the full mark, the sledge now, sagging (red) once the arms tire.
        var g0 = new float2(min.X + Gui.S(18), min.Y + Gui.S(12));
        var g1 = new float2(min.X + Gui.S(30), max.Y - Gui.S(12));
        Gui.Trough(dl, g0, g1);
        float Y(float f) => g1.Y - (g1.Y - g0.Y) * Math.Clamp(f, 0f, 1f);
        dl.AddRectFilled(new float2(g0.X, Y(1f)), new float2(g1.X, Y(SledgeSession.FullHeave)), Theme.U8(Theme.Good, 0.45f), 0f, ImDrawFlags.None);
        var now = Y(s.Raise);
        var heaveColour = s.Tiring ? Theme.Bad : s.Raise >= SledgeSession.FullHeave ? Theme.Good : Theme.Text;
        dl.AddRectFilled(new float2(g0.X - Gui.S(4), now - Gui.S(3)), new float2(g1.X + Gui.S(4), now + Gui.S(3)), Theme.U8(heaveColour), Gui.S(1), ImDrawFlags.None);

        // The call: "Now!" while it stands, its bar draining as the moment goes.
        var centre = new float2((g1.X + max.X) * 0.5f, min.Y + size.Y * 0.38f);
        if (s.CallOpen)
        {
            var left = 1f - s.CallAge / (float)Math.Max(1, s.Window);
            using (Gui.HeaderFont())
            {
                const string word = "Now!";
                var ts = ImGui.CalcTextSize(word);
                dl.AddText(centre - ts * 0.5f, Theme.U8(left > 0.33f ? Theme.Good : Theme.Warn), word);
            }
            var b0 = new float2(centre.X - Gui.S(90), centre.Y + Gui.S(22));
            var b1 = new float2(centre.X + Gui.S(90), centre.Y + Gui.S(30));
            Gui.Trough(dl, b0, b1);
            dl.AddRectFilled(b0, new float2(b0.X + (b1.X - b0.X) * Math.Clamp(left, 0f, 1f), b1.Y), Theme.U8(Theme.Good, 0.8f), 0f, ImDrawFlags.None);
        }
        else if (s.LastStep >= 0 && s.StepsDone - s.LastStep < SledgeWordSteps)
        {
            var (word, colour) = s.LastWas switch
            {
                SledgeResult.OnCall when s.LastGrade >= 0.8f => ("Square on it!", Theme.Good),
                SledgeResult.OnCall when s.LastGrade >= 0.5f => ("On the call", Theme.Text),
                SledgeResult.OnCall => ("Light, or late", Theme.Warn),
                SledgeResult.Offbeat => ("Off the beat!", Theme.Bad),
                _ => ("Missed the call", Theme.Warn),
            };
            var ts = ImGui.CalcTextSize(word);
            dl.AddText(centre - ts * 0.5f, Theme.U8(colour), word);
        }
        else
        {
            const string wait = "Watch the smith's hammer";
            var ts = ImGui.CalcTextSize(wait);
            dl.AddText(centre - ts * 0.5f, Theme.U8(Theme.Dim), wait);
        }

        // The piece itself, as the smith's session has it: its glow, and how far it's drawn out.
        if (_sim.World.TryGet<WorkSession>(s.Partner, out var lead) && lead is HammerSession h)
        {
            var p0 = new float2(g1.X + Gui.S(30), max.Y - Gui.S(34));
            var p1 = new float2(max.X - Gui.S(16), max.Y - Gui.S(22));
            dl.AddRectFilled(in p0, in p1, Gui.U8(GlowColour(h.Temp)), Gui.S(2), ImDrawFlags.None);
            var s0 = new float2(p0.X, max.Y - Gui.S(14));
            var s1 = new float2(p1.X, max.Y - Gui.S(8));
            Gui.Trough(dl, s0, s1);
            dl.AddRectFilled(s0, new float2(s0.X + (s1.X - s0.X) * Math.Clamp(h.Shape, 0f, 1f), s1.Y), Theme.U8(Theme.Accent, 0.8f), 0f, ImDrawFlags.None);
        }

        if (s.Tiring)
            TextC(Theme.Warn, "Your arms are tiring: it's sagging. Let it fall on the next call.");
        else
            TextC(Theme.Dim, $"On the call {s.Strikes}, off the beat {s.Offbeats}, missed {s.Missed}.");
        EndWork();
    }

    /// <summary>
    /// Under the smith's own anvil: on heavy work, how the striker's doing, or that there's none.
    /// The smith's taps call the sledge; alone, the piece barely moves.
    /// </summary>
    private void DrawStrikerLine(HammerSession s)
    {
        if (!s.Heavy)
            return;
        var w = _sim.World;
        if (!w.IsAlive(s.Partner) || !w.TryGet<Creature>(s.Partner, out var striker))
        {
            TextC(Theme.Warn, "Striker's work: your taps barely move it alone. Call Help! from the quick-say wheel for a hand on the sledge.");
            return;
        }
        var recent = s.LastStrikeStep >= 0 && s.StepsDone - s.LastStrikeStep < SledgeWordSteps;
        if (recent && s.LastStrike < 0f)
            TextC(Theme.Bad, $"{striker.Name} brought the sledge down off the beat!");
        else if (recent)
            TextC(s.LastStrike >= 0.6f ? Theme.Good : Theme.Text, $"{striker.Name} struck on your call{(s.LastStrike >= 0.8f ? ": square on it" : s.LastStrike < 0.4f ? ", light" : "")}.");
        else
            TextC(Theme.Dim, $"{striker.Name} is at the sledge: your tap is the call.");
    }
}
