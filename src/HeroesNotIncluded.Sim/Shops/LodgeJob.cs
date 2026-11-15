using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;

namespace HeroesNotIncluded.Sim.Shops;

/// <summary>
/// An AI innkeeper lets a room: takes on someone's <see cref="ServiceKind.Lodging"/> while a bed
/// upstairs is free, takes its key off the board behind the bar (<see cref="Hospitality.InnRooms"/>)
/// and hands it to them, as a player innkeeper does. It always takes the right key. An inn
/// without keys shows them up in person (<see cref="ShopSystem"/> gives them the bed in reach).
/// </summary>
public sealed class LodgeJob(int order) : Job
{
    /// <summary>A room this villager could let now: their trade's, known to them, theirs or nobody's, with a bed free.</summary>
    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return null;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var e in sim.Shops.FrontFirst(sim))
        {
            if (!e.IsOpen || e.Kind.Kind != ServiceKind.Lodging || e.Kind.AtDef != workplace || !trade.Def.Serving(ServiceKind.Lodging) || !e.KnownBy.Contains(pawn)
                || (!e.Assignee.IsNull && e.Assignee != pawn) || !w.IsAlive(e.Poster))
                continue;
            var again = !Hospitality.Lodging.Kept(sim, e.Poster).IsNull; // another night in the room they have
            if ((again || !ShopSystem.FreeLetBed(sim, e.Where).IsNull) && Navigation.CanReach(sim, pawn, from, w.Get<Position>(e.Poster).Cell, PathEnd.Touch))
                return new LodgeJob(e.Id);
        }
        return null;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        var orders = Sim.Shops;
        if (orders.Find(order) is not { IsOpen: true } e || (e.Assignee != Pawn && !orders.Accept(Sim, Pawn, order)))
        {
            Fail("someone else has it");
            yield break;
        }
        Report = $"Letting a room to {e.PosterName}";
        var board = Hospitality.InnRooms.Board(Sim, e.Where);
        if (!board.IsNull && Hospitality.Lodging.Kept(Sim, e.Poster).IsNull)
        {
            // A room with a key: take its key off the board, and hand it over.
            var bed = ShopSystem.FreeLetBed(Sim, e.Where);
            var key = Hospitality.InnRooms.OnBoard(Sim, e.Where, Hospitality.InnRooms.SeedOf(Sim, bed));
            if (key.IsNull)
            {
                Fail("no room free");
                yield break;
            }
            foreach (var s in GoToEntity(board, PathEnd.Touch, 1500)) yield return s;
            if (HasFailed) yield break;
            if (!Alive(key) || !Sim.World.TryGet<Stored>(key, out var st) || st.In != board || !e.IsOpen)
            {
                Fail("the key's gone");
                yield break;
            }
            var held = Sim.TakeFromStack(key, 1);
            Systems.Inventory.Stow(Sim, Pawn, held);
            foreach (var s in GoToEntity(e.Poster, PathEnd.Touch, 1500)) yield return s;
            if (HasFailed) yield break;
            for (var t = 0; t < 4 && e.IsOpen && !Handing.InReach(Sim, Pawn, e.Poster); t++)
                foreach (var s in Wait(SimClock.TicksPerHour / 24)) yield return s;
            if (!e.IsOpen || !Alive(held) || !Systems.Inventory.OnPerson(Sim, Pawn, held) || !Handing.InReach(Sim, Pawn, e.Poster))
            {
                Fail("couldn't let the room");
                yield break;
            }
            Sim.Shops.Received(Sim, Pawn, e.Poster, Systems.Inventory.TakeFromPerson(Sim, Pawn, held, 1));
            yield break;
        }
        foreach (var s in GoToEntity(e.Poster, PathEnd.Touch, 1500)) yield return s;
        if (HasFailed) yield break;
        // In reach: the order system shows them up on its next pass. Wait for it a moment.
        for (var t = 0; t < 4 && e.IsOpen; t++)
            foreach (var s in Wait(SimClock.TicksPerHour / 12)) yield return s;
        if (e.IsOpen)
            Fail("couldn't let the room");
    }
}
