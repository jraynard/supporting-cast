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
            var lying = Lying(sim, pawn, wants);
            if (lying >= f.Count)
                return new ErrandJob(f.Id);
            if (trade.Def.Gathers)
                MarkToGather(sim, pawn, wants, f.Count - lying);
        }
        return null;
    }

    /// <summary>The most cells (trees, bushes, rocks) an errand marks to gather at a time.</summary>
    public const int MostMarked = 6;

    /// <summary>How far from the notice board an errand's gathering is marked, cells.</summary>
    public const int GatherRange = 40;

    /// <summary>
    /// Not enough of it lying about: mark enough of what yields it (trees for logs, bushes for
    /// berries, rocks for stone) near the square for the gatherer's own work to bring in, and the
    /// errand's done with what's then lying about. Nothing more's marked while enough is already.
    /// </summary>
    private static void MarkToGather(Simulation sim, Entity pawn, Defs.ThingDef def, int shortBy)
    {
        var w = sim.World;
        var map = sim.Map;
        static int Yields(Defs.ThingDef? source, Defs.ThingDef def) =>
            source?.Harvest?.Yield.Where(y => y.ThingDef == def).Sum(y => y.Count) ?? 0;
        if (!sim.Defs.AllThings.Any(t => Yields(t, def) > 0))
            return; // nothing in the world gives it
        var marked = 0;
        foreach (var flag in (Designation[])[Designation.Chop, Designation.Mine, Designation.Harvest])
            foreach (var i in map.Designated(flag))
                if (!map.Structure[i].IsNull && w.TryGet<Thing>(map.Structure[i], out var t))
                    marked += Yields(t.Def, def);
        if (marked >= shortBy)
            return;
        var board = FavourBoard.AnyBoard(sim);
        if (board.IsNull)
            return;
        var centre = w.Get<Thing>(board).Cell;
        var from = w.Get<Position>(pawn).Cell;
        var sources = new List<(Entity Source, int Dist)>();
        foreach (var e in w.Query<Thing>())
        {
            var t = w.Get<Thing>(e);
            if (!t.Spawned || Yields(t.Def, def) == 0 || Map.Distance(t.Cell, centre) is var d && d > GatherRange
                || (w.TryGet<PlantState>(e, out var plant) && !plant.Ready) || (map.Designations[map.Idx(t.Cell)] & FlagOf(t.Def.Harvest!.WorkType)) != 0
                || !sim.Regions.CanReach(from, t.Cell, PathEnd.Touch))
                continue;
            sources.Add((e, d));
        }
        // Nearest the square first; ties to the lower entity, so the set's order decides nothing.
        foreach (var (source, _) in sources.OrderBy(s => s.Dist).ThenBy(s => s.Source.Index).Take(MostMarked))
        {
            if (marked >= shortBy)
                break;
            var t = w.Get<Thing>(source);
            map.Designate(map.Idx(t.Cell), FlagOf(t.Def.Harvest!.WorkType));
            marked += Yields(t.Def, def);
        }
    }

    private static Designation FlagOf(Defs.WorkType type) => type switch
    {
        Defs.WorkType.Chop => Designation.Chop,
        Defs.WorkType.Mine => Designation.Mine,
        _ => Designation.Harvest,
    };

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
