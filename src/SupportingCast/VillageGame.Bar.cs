using Brutal.ImGuiApi;
using Narsil.Ui;
using SupportingCast.Sim.Work;
using SupportingCast.Ui;

namespace SupportingCast;

// The bar's upkeep scenes: scrubbing crockery at the tub in time with the brush, and knocking a
// new keg's tap in as the mallet comes down. Read from the session only.
public sealed partial class VillageGame
{
    /// <summary>The tub and the brush going round: scrub as it comes to the top of its stroke.</summary>
    private void DrawWash(WashSession s)
    {
        BeginWork(s.Title, ("F", "scrub as the brush comes round"), ("Esc", "stop"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 150);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // The tub of grey water, and the vessel in it.
        var tubMin = new float2(min.X + size.X * 0.2f, min.Y + size.Y * 0.45f);
        var tubMax = new float2(max.X - size.X * 0.2f, max.Y - Gui.S(8));
        dl.AddRectFilled(in tubMin, in tubMax, new ImColor8(96, 66, 40, 255), Gui.S(4), ImDrawFlags.None);
        var waterMin = new float2(tubMin.X + Gui.S(6), tubMin.Y + Gui.S(8));
        var waterMax = new float2(tubMax.X - Gui.S(6), tubMax.Y - Gui.S(6));
        dl.AddRectFilled(in waterMin, in waterMax, new ImColor8(120, 130, 128, 255), Gui.S(2), ImDrawFlags.None);
        var cup = (waterMin + waterMax) * 0.5f - new float2(0, Gui.S(18));
        var grime = (byte)(200 * (1f - s.Scrub));
        var cupMin = cup - Gui.S(22, 20);
        var cupMax = cup + Gui.S(22, 20);
        dl.AddRectFilled(in cupMin, in cupMax, new ImColor8(150, 112, 70, 255), Gui.S(4), ImDrawFlags.None);
        dl.AddRectFilled(in cupMin, in cupMax, new ImColor8(70, 60, 40, grime), Gui.S(4), ImDrawFlags.None);

        // The brush sweeping across: the mark at the top of its stroke is the moment to scrub.
        var arcY = min.Y + Gui.S(22);
        float X(float brush) => min.X + size.X * (0.15f + 0.7f * brush);
        var markMin = new float2(X(0.75f), arcY - Gui.S(10));
        var markMax = new float2(X(1f) + Gui.S(14), arcY + Gui.S(10));
        dl.AddRectFilled(in markMin, in markMax, Theme.U8(s.OnBeat ? Theme.Good : Theme.Dim, 0.35f), Gui.S(3), ImDrawFlags.None);
        var bx = X(s.Brush);
        var brush = new float2(bx, arcY);
        var brushMin = brush - Gui.S(14, 6);
        var brushMax = brush + Gui.S(14, 6);
        dl.AddRectFilled(in brushMin, in brushMax, new ImColor8(160, 120, 70, 255), Gui.S(2), ImDrawFlags.None);

        ImGui.Spacing();
        ImGui.Text($"{s.Washed} of {s.Count} washed.");
        if (s.Washed > 0 || s.Scrub > 0)
        {
            ImGui.SameLine(0, 10);
            TextC(s.LastGood ? Theme.Good : Theme.Dim, s.LastGood ? "A good stroke." : "That barely touched it.");
        }
        Bar("Clean", s.Scrub, Theme.Accent);
        EndWork();
    }

    /// <summary>The cask's end, the tap and the mallet swinging: strike as it comes down onto the tap.</summary>
    private void DrawTap(TapSession s)
    {
        BeginWork(s.Title, ("F", "strike as the mallet comes down"), ("Esc", "leave it"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 150);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // The cask's head, hooped, with the tap going into it.
        var c = new float2(min.X + size.X * 0.4f, (min.Y + max.Y) * 0.5f);
        dl.AddCircleFilled(in c, Gui.S(54), new ImColor8(120, 82, 46, 255), 40);
        dl.AddCircle(in c, Gui.S(54), Theme.U8(Theme.Iron), 40, Gui.S(4));
        var seated = s.Blows / (float)TapSession.BlowsNeeded;
        var tapMin = new float2(c.X + Gui.S(40) - Gui.S(30) * seated, c.Y - Gui.S(6));
        var tapMax = new float2(c.X + Gui.S(90) - Gui.S(30) * seated, c.Y + Gui.S(6));
        dl.AddRectFilled(in tapMin, in tapMax, new ImColor8(170, 140, 90, 255), Gui.S(2), ImDrawFlags.None);

        // The mallet: raised, then down onto the tap's end.
        var head = new float2(tapMax.X + Gui.S(18), c.Y - Gui.S(60) * (1f - s.Mallet));
        var headMin = head - Gui.S(12, 16);
        var headMax = head + Gui.S(12, 16);
        dl.AddRectFilled(in headMin, in headMax, s.OnTheTap ? Theme.U8(Theme.Good) : new ImColor8(110, 80, 50, 255), Gui.S(2), ImDrawFlags.None);
        var handleEnd = head + new float2(Gui.S(60), Gui.S(10));
        dl.AddLine(in head, in handleEnd, new ImColor8(150, 110, 70, 255), Gui.S(4));

        // Foam where it's sprayed.
        for (var i = 0; i < s.Misses * 6; i++)
        {
            var p = new float2(c.X + Gui.S(40) + ((i * 37) % 60) * Gui.Scale, c.Y + Gui.S(10) + ((i * 53) % 40) * Gui.Scale);
            dl.AddCircleFilled(in p, Gui.S(3 + i % 3), new ImColor8(240, 232, 210, 200), 8);
        }

        ImGui.Spacing();
        ImGui.Text($"Driven home: {s.Blows} of {TapSession.BlowsNeeded}.");
        if (s.Misses > 0)
        {
            ImGui.SameLine(0, 10);
            TextC(Theme.Bad, $"{s.Wasted} servings sprayed out.");
        }
        EndWork();
    }
}
