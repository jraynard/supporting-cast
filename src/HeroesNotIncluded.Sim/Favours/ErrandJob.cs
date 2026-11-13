using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Favours;

/// <summary>
/// The town's labourer (the trade anyone may hold, <see cref="Defs.TradeDef.Unlimited"/>) checks the
/// notice board, takes on an errand there's enough lying about to do, gathers it and brings it to
/// whoever asked, and is paid on the spot (<see cref="FavourBoard.Deliver"/>). It's hauling, so it
/// comes under that work.
/// </summary>
public sealed class ErrandJob(int favour) : Job
{
    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || !trade.Def.Unlimited || sim.PackOf(pawn).IsNull)
            return null;
        foreach (var f in sim.Favours.All)
        {
            if (!f.IsOpen || f.Wants is not { } wants || f.Poster == pawn || (!f.Assignee.IsNull && f.Assignee != pawn)
                || !w.IsAlive(f.Poster) || w.Get<Creature>(f.Poster).Dead)
                continue;
            if (Lying(sim, pawn, wants) >= f.Count)
                return new ErrandJob(f.Id);
        }
        return null;
    }

    /// <summary>How many of a thing are about (lying, or kept where anyone may take it) where this one can get at them.</summary>
    private static int Lying(Simulation sim, Entity pawn, Defs.ThingDef def)
    {
        var w = sim.World;
        var from = w.Get<Position>(pawn).Cell;
        var n = 0;
        foreach (var e in sim.AvailableItems())
            if (w.Get<Thing>(e).Def == def && !sim.Reservations.IsReservedByOther(e, pawn) && Reachable(sim, pawn, from, e))
                n += w.Get<Stack>(e).Count;
        return n;
    }

    /// <summary>A stack lying on the ground is walked onto; one in a chest is reached from beside it.</summary>
    private static bool Reachable(Simulation sim, Entity pawn, int3 from, Entity stack) =>
        Navigation.CanReach(sim, pawn, from, sim.LocationOf(stack), sim.World.Get<Thing>(stack).Spawned ? PathEnd.OnTile : PathEnd.Touch);

    protected override IEnumerable<JobStatus> Run()
    {
        var board = Sim.Favours;
        if (board.Find(favour) is not { IsOpen: true, Wants: { } wants } f)
        {
            Fail("it's gone from the board");
            yield break;
        }

        // To the board, to read it and take the errand on.
        Report = "Checking the notice board";
        if (NearestBoard() is not { } at)
        {
            Fail("no notice board");
            yield break;
        }
        foreach (var s in GoTo(at, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        board.Read(Sim, Pawn);
        if (f.Assignee != Pawn && !board.Accept(Sim, Pawn, favour))
        {
            Fail("someone else took it");
            yield break;
        }

        // Gather it, a stack at a time, from wherever it's lying or kept.
        Report = $"Fetching {f.Count} {wants.Label} for {f.PosterName}";
        var pack = Sim.PackOf(Pawn);
        var have = Inventory.CarriedOf(Sim, Pawn, wants).Sum(e => Get<Stack>(e).Count);
        for (var round = 0; round < 8 && have < f.Count; round++)
        {
            if (NearestOf(wants) is not { } next || !Sim.Reservations.TryReserve(next, Pawn))
                break;
            var loose = Get<Thing>(next).Spawned;
            foreach (var s in GoTo(Sim.LocationOf(next), loose ? PathEnd.OnTile : PathEnd.Touch)) yield return s;
            if (HasFailed) yield break;
            if (!Alive(next) || !Sim.IsAvailable(next))
                continue;
            var take = Math.Min(Math.Min(f.Count - have, Get<Stack>(next).Count), Sim.ContainerRoom(pack, wants, Get<Thing>(next).Material));
            if (take <= 0)
                break;
            Sim.Store(pack, Sim.TakeFromStack(next, take));
            have += take;
        }
        if (have < f.Count || !f.IsOpen)
        {
            Fail("couldn't find enough");
            yield break;
        }

        Report = $"Bringing {f.PosterName} the {wants.Label}";
        foreach (var s in GoToEntity(f.Poster, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        if (!board.Deliver(Sim, Pawn, favour))
            Fail("couldn't hand it over");
    }

    private int3? NearestBoard()
    {
        var best = (int3?)null;
        var dist = int.MaxValue;
        foreach (var e in Sim.World.Query<Thing>())
            if (FavourBoard.IsBoard(Sim, e) && Map.Distance(Get<Thing>(e).Cell, PawnCell) is var d && d < dist)
                (best, dist) = (Get<Thing>(e).Cell, d);
        return best;
    }

    private Entity? NearestOf(Defs.ThingDef def)
    {
        Entity? best = null;
        var dist = int.MaxValue;
        foreach (var e in Sim.AvailableItems())
            if (Get<Thing>(e).Def == def && !Sim.Reservations.IsReservedByOther(e, Pawn)
                && Map.Distance(Sim.LocationOf(e), PawnCell) is var d && d < dist && Reachable(Sim, Pawn, PawnCell, e))
                (best, dist) = (e, d);
        return best;
    }
}
