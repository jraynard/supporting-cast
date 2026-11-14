using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Work;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The festival's games (Sim/Work/FestivalGames): the striker's gauge and the butts' painted face.
public sealed partial class VillageGame
{
    /// <summary>The striker: a needle sweeping the gauge; F brings the mallet down. The puck flies as high as the needle stood.</summary>
    private void DrawStriker(StrikerSession s)
    {
        BeginWork(s.Title, ("F", "swing the mallet as the needle tops the gauge"), ("Esc", "leave it"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(360, 190);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // The post: a tall track with the bell at its top and marks up its side.
        var postX = min.X + size.X * 0.72f;
        var bottom = max.Y - Gui.S(16);
        var top = min.Y + Gui.S(30);
        var trackMin = new float2(postX - Gui.S(7), top);
        var trackMax = new float2(postX + Gui.S(7), bottom);
        dl.AddRectFilled(in trackMin, in trackMax, new ImColor8(84, 58, 36, 255), Gui.S(2), ImDrawFlags.None);
        for (var k = 1; k < 10; k++)
        {
            var y = bottom - (bottom - top) * k / 10f;
            var a = new float2(postX - Gui.S(12), y);
            var b = new float2(postX - Gui.S(7), y);
            dl.AddLine(in a, in b, Theme.U8(Theme.Dim), Gui.S(1));
        }
        var bellAt = new float2(postX, top - Gui.S(10));
        var rang = s.Flight > 0 && s.Last >= StrikerSession.Bell;
        dl.AddCircleFilled(in bellAt, Gui.S(11), rang ? Theme.U8(Theme.Accent) : Theme.U8(Theme.Iron), 20);

        // The puck: up the track with the last swing, falling back as its flight ends.
        var rise = s.Flight > 0 ? s.Last * Math.Min(1f, 2f * (1f - s.Flight / (float)StrikerSession.FlightSteps) + 0.0001f) : 0f;
        var puckY = bottom - (bottom - top) * rise;
        var pMin = new float2(postX - Gui.S(9), puckY - Gui.S(8));
        var pMax = new float2(postX + Gui.S(9), puckY);
        dl.AddRectFilled(in pMin, in pMax, new ImColor8(200, 70, 50, 255), Gui.S(2), ImDrawFlags.None);
        // The best so far, a mark on the post.
        if (s.Best > 0)
        {
            var y = bottom - (bottom - top) * s.Best;
            var a = new float2(postX + Gui.S(7), y);
            var b = new float2(postX + Gui.S(18), y);
            dl.AddLine(in a, in b, Theme.U8(Theme.Good), Gui.S(2));
        }

        // The gauge: a needle swinging across an arc, the top of it in the bell's colour.
        var c = new float2(min.X + size.X * 0.3f, max.Y - Gui.S(30));
        var r = Gui.S(90);
        for (var k = 0; k < 24; k++)
        {
            float a0 = MathF.PI * (1f - k / 24f), a1 = MathF.PI * (1f - (k + 1) / 24f);
            var p0 = c + new float2(MathF.Cos(a0), -MathF.Sin(a0)) * r;
            var p1 = c + new float2(MathF.Cos(a1), -MathF.Sin(a1)) * r;
            var hot = (k + 0.5f) / 24f >= StrikerSession.Bell;
            dl.AddLine(in p0, in p1, hot ? Theme.U8(Theme.Accent) : Theme.U8(Theme.Dim), Gui.S(hot ? 6 : 3));
        }
        var needle = s.Flight > 0 ? s.Last : s.Needle;
        var na = MathF.PI * (1f - needle);
        var tip = c + new float2(MathF.Cos(na), -MathF.Sin(na)) * (r - Gui.S(6));
        dl.AddLine(in c, in tip, Theme.U8(Theme.Text), Gui.S(3));
        dl.AddCircleFilled(in c, Gui.S(5), Theme.U8(Theme.Iron), 12);

        ImGui.Spacing();
        ImGui.Text($"Swing {Math.Min(s.Swings + 1, FestivalGames.Turns)} of {FestivalGames.Turns}.");
        if (s.Best > 0)
        {
            ImGui.SameLine(0, 10);
            TextC(s.Best >= StrikerSession.Bell ? Theme.Good : Theme.Dim, s.Best >= StrikerSession.Bell ? "You rang the bell!" : $"Best: {s.Best * 100:F0}");
        }
        EndWork();
    }

    /// <summary>The butts: the painted face, the aim swaying over it, the arrows where they landed.</summary>
    private void DrawButts(ButtsSession s)
    {
        BeginWork(s.Title, ("WASD", "hold the aim on the gold"), ("F", "loose"), ("Esc", "leave it"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(260, 260);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        var c = (min + max) * 0.5f;
        var r = Gui.S(110);
        ImColor8[] colours = [new(230, 180, 70, 255), new(181, 67, 43, 255), new(60, 72, 96, 255), new(30, 30, 34, 255), new(242, 230, 207, 255)];
        for (var k = ButtsSession.Rings.Length - 1; k >= 0; k--)
            dl.AddCircleFilled(in c, r * ButtsSession.Rings[k], colours[k], 40);
        for (var k = 0; k < s.Shots; k++)
        {
            var at = c + new float2(s.HitX(k), s.HitY(k)) * r;
            var a = at - Gui.S(4, 4);
            var b = at + Gui.S(4, 4);
            dl.AddLine(in a, in b, Theme.U8(Theme.Text), Gui.S(2));
            dl.AddCircleFilled(in at, Gui.S(3), new ImColor8(90, 60, 30, 255), 8);
        }
        if (s.Landing == 0 && s.Shots < FestivalGames.Turns)
        {
            var aim = c + new float2(s.AimX, s.AimY) * r;
            dl.AddCircle(in aim, Gui.S(9), Theme.U8(Theme.Text), 16, Gui.S(2));
            var h0 = aim - new float2(Gui.S(14), 0);
            var h1 = aim + new float2(Gui.S(14), 0);
            var v0 = aim - new float2(0, Gui.S(14));
            var v1 = aim + new float2(0, Gui.S(14));
            dl.AddLine(in h0, in h1, Theme.U8(Theme.Text), Gui.S(1));
            dl.AddLine(in v0, in v1, Theme.U8(Theme.Text), Gui.S(1));
        }
        ImGui.Spacing();
        ImGui.Text($"Arrow {Math.Min(s.Shots + 1, FestivalGames.Turns)} of {FestivalGames.Turns}.");
        ImGui.SameLine(0, 10);
        TextC(Theme.Dim, $"{s.Points} points");
        EndWork();
    }
}
