using Brutal.ImGuiApi;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// Customers you owe a visit (PerceptionView.Awaiting): someone whose blade or box you took the
// job on and have yet to collect, or whose thing is ready to go back. A tag over them on screen,
// an arrow at the screen's edge when they're off it, and a button on the order that walks you there
// and does the handing (SeeToCustomerCommand). Where they are comes only from Perception.
public sealed partial class VillageGame
{
    /// <summary>The tags and edge arrows for the customers you owe a visit, for the renderer.</summary>
    private void UpdateErrandPointers()
    {
        var pointers = _renderer.Pointers;
        pointers.Clear();
        if (!HasAvatar || !_sim.Round.Live)
            return;
        foreach (var errand in View.Awaiting())
        {
            if (errand.Returning && !errand.Ready)
                continue; // still at the work: they'll keep
            if (!_sim.World.TryGet<Position>(errand.Who, out var pos))
                continue;
            var at = _renderer.DrawPos(_sim, errand.Who, pos.Pos) + WorldScale.Rise(2.2f);
            pointers.Add(new(at, errand.Who, errand.Returning ? $"{errand.Name}: take it back" : $"{errand.Name}: collect"));
        }
    }

    /// <summary>On an order you're seeing to: go and collect it, or take it back once it looks done.</summary>
    private void DrawErrandButton(KnownErrand errand)
    {
        if (errand.Returning && !errand.Ready)
        {
            TextC(Theme.Dim, $"With you. When it's done, take it back to {errand.Name}.");
            return;
        }
        var label = errand.Returning ? $"Take it back to {errand.Name}" : $"Go and collect it from {errand.Name}";
        if (ImGui.Button(label, in _autoSize))
            Issue(new SeeToCustomerCommand(_avatar, errand.Order));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip(errand.Returning
                ? "Walks you over and hands it back. They look it over: done, they pay; if not, they say what's wrong. Moving calls it off."
                : "Walks you over; they hand it to you when you're close. Moving calls it off.");
    }
}
