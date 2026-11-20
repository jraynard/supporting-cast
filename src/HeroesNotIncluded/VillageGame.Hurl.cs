using Brutal.ImGuiApi;
using Brutal.Numerics;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Ui;
using Narsil.Ui;

namespace HeroesNotIncluded;

/// <summary>
/// A stool picked up to throw (<see cref="Hurling"/>, <see cref="HurlCommand"/>), worked like a
/// bucket: hold the right button to draw it back, filling the gauge, and click the left to hurl it
/// the way you aim. Nothing shows where it'll land: it's a stool, not a bow.
/// </summary>
public sealed partial class VillageGame
{
    /// <summary>The stool drawn back, as last told to the sim.</summary>
    private bool _stoolSent;

    /// <summary>A stool in your hands.</summary>
    private bool HoldingStool => HasAvatar && Hurling.Holding(_sim, _avatar);

    /// <summary>The stool's buttons: Heavy (right) held draws it back, Strike (left) hurls it. True: it takes the mouse.</summary>
    private bool HandleStool()
    {
        var holding = HoldingStool;
        var tick = _sim.Clock.Tick;
        var back = holding && Controls.Map.Down(Act.Heavy);
        if (back != _stoolSent)
        {
            Issue(new HurlCommand(_avatar, back, false, _aim, tick));
            _stoolSent = back;
        }
        if (back && Controls.Map.Pressed(Act.Strike) && Hurling.Charge(_sim, _avatar) > 0f)
        {
            var (ax, ay, az) = Here();
            Issue(new HurlCommand(_avatar, true, true, _aim, tick, ax, ay, az));
            _stoolSent = false;
        }
        return holding;
    }

    /// <summary>A stool in hand: what the buttons do, and while it's drawn back, how hard it'll go.</summary>
    private void DrawStoolThrow()
    {
        if (!HoldingStool)
            return;
        var (strike, heavy, use) = (Press(Act.Strike), Press(Act.Heavy), Controls.Glyph(Act.Use));
        _hud.Next(Anchor.Bottom);
        Panel.Begin("##stool-throw", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs);
        _hud.Took();
        var charge = Hurling.Charge(_sim, _avatar);
        if (charge <= 0f)
        {
            TextC(Theme.Warn, $"A stool in hand. Hold {heavy} to draw it back, {strike} to hurl it the way you aim.");
            ImGui.SameLine(0, Gui.S(10));
            TextC(Theme.Dim, $"{use} sets it down.");
            Panel.End();
            return;
        }
        TextC(Theme.Dim, charge < 1f ? $"Further back, harder and further. {Cap(strike)} to hurl it." : $"All the way back. {Cap(strike)} to hurl it.");
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(360, 18);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        var pad = Gui.S(3);
        var fillMin = min + new float2(pad, pad);
        var fillMax = new float2(min.X + pad + (size.X - 2 * pad) * charge, max.Y - pad);
        dl.AddRectFilled(in fillMin, in fillMax, Theme.U8(charge < 1f ? Theme.Accent : Theme.Warn), 0f, ImDrawFlags.None);
        Panel.End();
    }
}
