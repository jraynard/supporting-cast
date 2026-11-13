using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The call to arms (Sim.Militia): while it's out, a line at the top of the screen says what the
// town wants for the muster rack and what it pays. Town business, called out in the square, so
// it's shown to everyone without going through Perception.
public sealed partial class VillageGame
{
    private void DrawCallToArms()
    {
        var m = _sim.Militia;
        if (!InRound || !m.Open)
            return;
        _hud.Next(Anchor.Top);
        Panel.Begin("##call-to-arms", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDecoration);
        _hud.Took();
        TextC(Theme.Warn, $"Call to arms: raiders expected {m.When}.");
        ImGui.SameLine(0, 10);
        if (m.Paying)
            TextC(Theme.Dim, $"{m.Paid} of {m.Wanted} weapons hung on the muster rack · {m.Price} silver each");
        else if (m.Wanted > 0 && m.Paid < m.Wanted)
            TextC(Theme.Dim, $"{m.Paid} of {m.Wanted} weapons · the town purse is spent");
        else
            TextC(Theme.Dim, "The rack by the well is open: take up arms.");
        Panel.End();
    }
}
