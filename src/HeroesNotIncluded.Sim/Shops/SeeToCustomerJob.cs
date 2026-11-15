using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Shops;

/// <summary>
/// A player's walk to a customer whose thing changes hands (<see cref="SeeToCustomerCommand"/>),
/// following them if they move. In reach, the thing is handed back if the player has it (the same
/// as giving it from the inspector: they look it over and pay, or say what's wrong), or else handed
/// over to the player. It ends there, or when the order closes or the customer is gone.
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
        var returning = Mine(e);
        Report = returning ? $"Taking it back to {e.PosterName}" : $"Going to {e.PosterName}";
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
        if (Mine(e))
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
}
