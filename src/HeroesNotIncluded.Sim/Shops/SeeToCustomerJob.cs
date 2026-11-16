using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Shops;

/// <summary>
/// A player's walk to a customer whose thing changes hands (<see cref="SeeToCustomerCommand"/>),
/// following them if they move. In reach, the thing is handed back if the player has it (the same
/// as giving it from the inspector: they look it over and pay, or say what's wrong), or else handed
/// over to the player. A drink order is carried to them instead: the poured drink on the player (in
/// hand or pack) is served as across the bar (<see cref="BarService.Serve"/>), and the job fails if
/// it's gone by then (drunk, set down). It ends there, or when the order closes or the customer is gone.
/// </summary>
public sealed class SeeToCustomerJob(int order) : Job
{
    /// <summary>How long it keeps after someone across town before giving up: a couple of game hours.</summary>
    public const int MaxTicks = SimClock.TicksPerHour * 2;

    public int Order { get; } = order;

    protected override IEnumerable<JobStatus> Run()
    {
        if (Sim.Shops.Find(Order) is not { } e || e.Poster.IsNull)
        {
            Fail("no such order");
            yield break;
        }
        var drink = e.Kind.Kind is Defs.ServiceKind.Drink or Defs.ServiceKind.MixedDrink;
        if (drink && DrinkFor(Sim, Pawn, e.Kind.Kind).IsNull)
        {
            Fail("nothing poured");
            yield break;
        }
        var returning = drink || Mine(e);
        Report = drink ? $"Taking {e.PosterName} their drink" : returning ? $"Taking it back to {e.PosterName}" : $"Going to {e.PosterName}";
        foreach (var step in GoToEntity(e.Poster, PathEnd.Touch, MaxTicks))
        {
            if (!e.IsOpen || e.Assignee != Pawn)
            {
                Fail("the order's closed");
                yield break;
            }
            if (Handing.InReach(Sim, Pawn, e.Poster))
                break;
            yield return step;
        }
        if (FailReason != null || !e.IsOpen || e.Assignee != Pawn || !Handing.InReach(Sim, Pawn, e.Poster))
            yield break;
        Get<Mover>(Pawn).Stop();
        if (drink)
        {
            var poured = DrinkFor(Sim, Pawn, e.Kind.Kind);
            if (poured.IsNull)
            {
                Fail("the drink's gone");
                yield break;
            }
            BarService.Serve(Sim, Pawn, new BarService.Serving(poured, e));
        }
        else if (Mine(e))
        {
            // Out of the pack and into their hands: they look it over as they would from the inspector.
            var taken = Inventory.TakeFromPerson(Sim, Pawn, e.Subject, 1);
            Sim.Shops.Received(Sim, Pawn, e.Poster, taken);
        }
        else
            Sim.Shops.TryHandOver(Sim, e);
    }

    /// <summary>The player has the customer's thing loose in hand or pack (not wielded or worn).</summary>
    private bool Mine(Order e)
    {
        var w = Sim.World;
        return w.IsAlive(e.Subject) && w.TryGet<Entrusted>(e.Subject, out var t) && t.Owner == e.Poster
            && (w.Get<Carrying>(Pawn).Item == e.Subject || Inventory.InOwnPack(Sim, Pawn, e.Subject));
    }

    /// <summary>
    /// The poured drink on this person that would fill the order (in hand, then the pack): one of
    /// the kind asked for (mixed for a mixed drink, plain for an ale) if there is one, else any.
    /// </summary>
    public static Entity DrinkFor(Simulation sim, Entity who, Defs.ServiceKind kind)
    {
        var mixed = kind == Defs.ServiceKind.MixedDrink;
        var match = AvatarSystem.SupplyItem(sim, who, d => sim.World.Get<Thing>(d).Def.Drink && sim.World.Has<Work.DrinkMix>(d) == mixed);
        return match.IsNull ? BarService.DrinkOn(sim, who) : match;
    }
}
