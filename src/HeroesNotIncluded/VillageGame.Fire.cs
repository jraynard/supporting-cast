using Brutal.ImGuiApi;
using Brutal.Numerics;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Environment;
using HeroesNotIncluded.Ui;
using Narsil.Ui;

namespace HeroesNotIncluded;

/// <summary>Throwing a bucket of water (<see cref="ThrowSession"/>): the swing, and where it'll land.</summary>
public sealed partial class VillageGame
{
    private void DrawThrow(ThrowSession s)
    {
        BeginWork(s.Title, ("F", "hold to swing back, let go to throw"), ("A/D", "turn"), ("Esc", "lower it"));
        TextC(Theme.Dim, "Point with the mouse, or turn with A and D. The further back it swings, the further it goes: let go as the mark lines up.");
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(360, 26);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        // How far back it's swung, and a mark at the distance aimed at.
        var reach = (float)(Buckets.MaxRange - 1);
        var share = Math.Clamp((s.AimShare * Buckets.MaxRange - 1f) / reach, 0f, 1f);
        var pad = Gui.S(3);
        var fillMax = new float2(min.X + pad + (size.X - 2 * pad) * s.Swing, max.Y - pad);
        var fillMin = min + new float2(pad, pad);
        dl.AddRectFilled(in fillMin, in fillMax, Theme.U8(Theme.Water), 0f, ImDrawFlags.None);
        var markX = min.X + pad + (size.X - 2 * pad) * share;
        var top = new float2(markX, min.Y);
        var bottom = new float2(markX, max.Y);
        dl.AddLine(in top, in bottom, Theme.U8(Theme.Text), Gui.S(2));
        EndWork();
        DrawLanding(s.LandsAt);
    }

    /// <summary>The patch the water would land on, outlined on the ground (the cell and the ring round it).</summary>
    private void DrawLanding(int3 at)
    {
        if (!_sim.Map.InBounds(at))
            return;
        var dl = ImGui.GetBackgroundDrawList(ImGui.GetMainViewport());
        float2 P(float x, float y) => _camera.WorldToScreen(new float3(x, y, at.Z));
        var corners = new[] { P(at.X - 1, at.Y - 1), P(at.X + 2, at.Y - 1), P(at.X + 2, at.Y + 2), P(at.X - 1, at.Y + 2) };
        var colour = Theme.U8(Theme.Water, 0.9f);
        for (var k = 0; k < 4; k++)
        {
            var a = corners[k];
            var b = corners[(k + 1) % 4];
            dl.AddLine(in a, in b, colour, Gui.S(2));
        }
        var c = P(at.X + 0.5f, at.Y + 0.5f);
        dl.AddCircleFilled(in c, Gui.S(4), colour, 12);
    }
}
