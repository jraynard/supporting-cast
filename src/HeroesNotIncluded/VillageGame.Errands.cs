using Brutal.ImGuiApi;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// Customers you owe a visit (PerceptionView.Awaiting): someone whose blade or box you took the
// job on and have yet to collect, or whose thing is ready to go back. A tag over them on screen,
// an arrow at the screen's edge when they're off it, and a button on the order that walks you there
// and does the handing (SeeToCustomerCommand). Where they are comes only from Perception.
public sealed partial class VillageGame
{
    /// <summary>
    /// The tags and edge arrows for everyone you owe something, for the renderer: each order you've
    /// taken that's still to be done (labelled with what's owed), a guest back down from the wrong
    /// room wanting another key, and whoever you last picked out in the room book.
    /// </summary>
    private void UpdateErrandPointers()
    {
        var pointers = _renderer.Pointers;
        pointers.Clear();
        if (!HasAvatar || !_sim.Round.Live)
            return;
        var shown = new HashSet<Entity>();
        foreach (var errand in View.Awaiting())
        {
            shown.Add(errand.Who);
            if (errand.Returning && !errand.Ready)
                continue; // still at the work: they'll keep
            Point(errand.Who, errand.Returning ? $"{errand.Name}: take it back" : $"{errand.Name}: collect");
        }
        var trade = _sim.World.TryGet<Trade>(_avatar, out var t) ? t.Def : null;
        foreach (var o in View.Orders())
        {
            if (o.Status != OrderStatus.Open || o.Mine || o.Poster.IsNull || shown.Contains(o.Poster))
                continue;
            // A guest who walked in on someone: their new ask isn't taken yet, but it's the keeper's to put right.
            var owed = o.Kind == Sim.Defs.ServiceKind.Lodging && o.AssigneeName.Length == 0 && Serves(trade, o.Kind) && View.OwedARoom(o.Poster);
            if (!o.MineToDo && !owed)
                continue;
            shown.Add(o.Poster);
            Point(o.Poster, $"{o.PosterName}: {Owed(o)}");
        }
        if (_bookPointer is { } picked && _time < picked.Until && !shown.Contains(picked.Who))
            Point(picked.Who, picked.Name);

        void Point(Entity who, string label)
        {
            if (!_sim.World.TryGet<Position>(who, out var pos))
                return;
            pointers.Add(new(_renderer.DrawPos(_sim, who, pos.Pos) + WorldScale.Rise(2.2f), who, label));
        }
    }

    /// <summary>What's owed on an order, for its pointer: "their key, room 3", "their ale".</summary>
    private string Owed(KnownOrder o)
    {
        if (o.Kind == Sim.Defs.ServiceKind.Lodging)
            return View.OwedARoom(o.Poster) ? "the right key" : View.WrittenInto(o.Poster) is { } room ? $"their key, room {room}" : "a room";
        if (o.Kind == Sim.Defs.ServiceKind.MixedDrink && View.WantedDrink(o.Id) is { } named)
            return $"their {named.Label}";
        if (o.Kind is Sim.Defs.ServiceKind.Drink or Sim.Defs.ServiceKind.MixedDrink or Sim.Defs.ServiceKind.Meal && o.KindLabel.StartsWith("a "))
            return "their " + o.KindLabel[2..];
        return o.KindLabel;
    }

    /// <summary>Seconds a guest picked out in the room book stays pointed to.</summary>
    private const float BookPointerSeconds = 20f;

    /// <summary>A guest picked out in the room book: pointed to for a while.</summary>
    private (Entity Who, string Name, float Until)? _bookPointer;

    /// <summary>
    /// On an order you're seeing to whose thing changes hands, after its time bar: go and collect it,
    /// or take it back once it looks done (greyed out while it's still at the work).
    /// </summary>
    private void DrawErrandButton(KnownErrand errand)
    {
        if (errand.Returning && !errand.Ready)
        {
            CardIcon($"##errand{errand.Order}", "ui/work/hand", false, $"With you. When it's done, take it back to {errand.Name}.", () => { });
            return;
        }
        CardIcon($"##errand{errand.Order}", "ui/work/hand", true, errand.Returning
                ? $"Take it back to {errand.Name}: walks you over and hands it back. They look it over: done, they pay; if not, they say what's wrong. Moving calls it off."
                : $"Go and collect it from {errand.Name}: walks you over; they hand it to you when you're close. Moving calls it off.",
            () => Issue(new SeeToCustomerCommand(_avatar, errand.Order)));
        GuideGlow(TutorialStep == (errand.Returning ? "giveback" : "handover"));
    }
}
