using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;

namespace HeroesNotIncluded.Sim.Ai;

/// <summary>
/// The season's last evening (<see cref="RoundPhase.Festival"/>): the town and whoever's visiting
/// leave off work for the square. While it gathers they mill about the well; in the revels each
/// picks something to do (<see cref="FestivalDef.Activities"/>: the feast tables, the bonfire, the
/// dancing round it, the games, the show table); at the ceremony they gather round the well to hear it.
/// </summary>
public static class Festival
{
    /// <summary>How far from the well the crowd spreads.</summary>
    public const int Spread = 6;

    /// <summary>How far round the bonfire (cells, square ring) the dance goes.</summary>
    public const int DanceRadius = 2;

    /// <summary>Off to the square and about it, if the festival's on; null otherwise.</summary>
    public static Job? Gathering(Simulation sim, Entity pawn)
    {
        if (sim.Round.Phase != RoundPhase.Festival || sim.Round.Square is not { } square)
            return null;
        var f = sim.Festival;
        // Orders waiting at their counter keep a trade at work through the revels: the inn serves on.
        if (f.Part != FestivalPart.Ceremony && StillServing(sim, pawn))
            return null;
        if (f.Part == FestivalPart.Ceremony)
        {
            // The crier stands by the well; everyone else round it, close enough to hear.
            var spot = AiQueries.RandomReachableNear(sim, pawn, square, pawn == f.Crier ? 1 : 4);
            return spot is { } at ? new FestivalJob(FestivalActivity.Gather, Entity.Null, [at], sim.Rand.Range(600, 1200)) : null;
        }
        if (f.Part == FestivalPart.Revels)
        {
            var weights = sim.World.Has<Adventurer>(pawn) ? sim.Defs.Festival.VisitorWeights : sim.Defs.Festival.TownWeights;
            if (Pick(sim, weights) is { } activity && Activity(sim, pawn, activity) is { } job)
                return job;
        }
        return Mill(sim, pawn, square);
    }

    /// <summary>Open orders wait at this villager's workplace for their trade to do.</summary>
    public static bool StillServing(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (w.Has<Adventurer>(pawn) || !w.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return false;
        foreach (var order in sim.Shops.All)
            if (order.IsOpen && w.IsAlive(order.Where) && w.TryGet<PresetBuilding>(order.Where, out var b) && b.Def == workplace
                && trade.Def.Serving(order.Kind.Kind))
                return true;
        return false;
    }

    private static FestivalActivity? Pick(Simulation sim, IReadOnlyList<(FestivalActivity Activity, float Weight)> weights)
    {
        var total = 0f;
        foreach (var (_, w) in weights)
            total += w;
        if (total <= 0)
            return null;
        var roll = sim.Rand.NextFloat() * total;
        foreach (var (a, w) in weights)
        {
            if (roll < w)
                return a;
            roll -= w;
        }
        return weights[^1].Activity;
    }

    private static Job? Mill(Simulation sim, Entity pawn, int3 square) =>
        AiQueries.RandomReachableNear(sim, pawn, square, Spread) is { } spot
            ? new FestivalJob(FestivalActivity.Mill, Entity.Null, [spot], sim.Rand.Range(240, 720))
            : null;

    private static Job? Activity(Simulation sim, Entity pawn, FestivalActivity activity)
    {
        var f = sim.Festival;
        var w = sim.World;
        switch (activity)
        {
            case FestivalActivity.Feast:
            {
                var seat = FreeSeat(sim, pawn);
                return seat.IsNull ? null : new FestivalJob(activity, seat, [w.Get<Thing>(seat).Cell], sim.Rand.Range(900, 1800));
            }
            case FestivalActivity.Bonfire:
            {
                var fire = f.Prop(sim, FestivalSystem.BonfireDef);
                return !fire.IsNull && AiQueries.RandomReachableNear(sim, pawn, w.Get<Thing>(fire).Cell, 3) is { } spot
                    ? new FestivalJob(activity, fire, [spot], sim.Rand.Range(600, 1200))
                    : null;
            }
            case FestivalActivity.Dance:
            {
                var fire = f.Prop(sim, FestivalSystem.BonfireDef);
                if (fire.IsNull)
                    return null;
                var ring = Ring(w.Get<Thing>(fire).Cell, DanceRadius);
                // Join the ring where it's nearest, and go round it a way.
                var here = w.Get<Position>(pawn).Cell;
                var start = 0;
                for (var k = 1; k < ring.Count; k++)
                    if (Maps.Map.Distance(ring[k], here) < Maps.Map.Distance(ring[start], here))
                        start = k;
                var steps = sim.Rand.Range(8, 2 * ring.Count);
                var route = new List<int3>();
                for (var k = 0; k < steps; k++)
                    route.Add(ring[(start + k) % ring.Count]);
                return new FestivalJob(activity, fire, route, 0);
            }
            case FestivalActivity.Games:
            {
                var games = f.Games.Select(g => f.Prop(sim, g.Game.Def)).Where(e => !e.IsNull).ToList();
                if (games.Count == 0)
                    return null;
                var thing = games[sim.Rand.NextInt(games.Count)];
                return AiQueries.RandomReachableNear(sim, pawn, w.Get<Thing>(thing).Cell, 3) is { } spot
                    ? new FestivalJob(activity, thing, [spot], sim.Rand.Range(900, 1500))
                    : null;
            }
            case FestivalActivity.Show:
            {
                var table = f.Prop(sim, FestivalSystem.ShowTableDef);
                return !table.IsNull && AiQueries.RandomReachableNear(sim, pawn, w.Get<Thing>(table).Cell, 2) is { } spot
                    ? new FestivalJob(activity, table, [spot], sim.Rand.Range(300, 600))
                    : null;
            }
            default:
                return null;
        }
    }

    /// <summary>Is this the festival's bonfire, with the revels on for dancing round it?</summary>
    public static bool CanDance(Simulation sim, Entity who, Entity fire) =>
        !fire.IsNull && sim.Festival.Open && fire == sim.Festival.Prop(sim, FestivalSystem.BonfireDef) && !sim.World.Has<Work.WorkSession>(who);

    /// <summary>A turn of the dance round the bonfire, for a player who joins it: twice round.</summary>
    public static Job? Dance(Simulation sim, Entity who, Entity fire)
    {
        if (!CanDance(sim, who, fire))
            return null;
        var ring = Ring(sim.World.Get<Thing>(fire).Cell, DanceRadius);
        var here = sim.World.Get<Position>(who).Cell;
        var start = 0;
        for (var k = 1; k < ring.Count; k++)
            if (Maps.Map.Distance(ring[k], here) < Maps.Map.Distance(ring[start], here))
                start = k;
        var route = new List<int3>();
        for (var k = 0; k <= 2 * ring.Count; k++)
            route.Add(ring[(start + k) % ring.Count]);
        return new FestivalJob(FestivalActivity.Dance, fire, route, 0);
    }

    /// <summary>The square ring of cells <paramref name="radius"/> round a cell, in order round it.</summary>
    public static List<int3> Ring(int3 center, int radius)
    {
        var ring = new List<int3>();
        for (var x = -radius; x < radius; x++) ring.Add(center + new int3(x, -radius, 0));
        for (var y = -radius; y < radius; y++) ring.Add(center + new int3(radius, y, 0));
        for (var x = radius; x > -radius; x--) ring.Add(center + new int3(x, radius, 0));
        for (var y = radius; y > -radius; y--) ring.Add(center + new int3(-radius, y, 0));
        return ring;
    }

    /// <summary>The nearest free seat put up for the festival's feast that they can reach, or Entity.Null.</summary>
    public static Entity FreeSeat(Simulation sim, Entity who)
    {
        var w = sim.World;
        var at = w.Get<Position>(who).Cell;
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        foreach (var seat in sim.Festival.PropsOf(sim, "Stool"))
        {
            if (sim.Reservations.IsReservedByOther(seat, who))
                continue;
            var cell = w.Get<Thing>(seat).Cell;
            var d = Maps.Map.Distance(at, cell);
            if (d < bestDist && Navigation.CanReach(sim, who, at, cell, PathEnd.OnTile))
                (best, bestDist) = (seat, d);
        }
        return best;
    }
}

/// <summary>
/// An evening at the festival (<see cref="Festival"/>): to a spot (or along the dance's ring),
/// then there a while. Ends when the festival moves on to its next part, so the ceremony's bell
/// brings everyone in.
/// </summary>
public sealed class FestivalJob(FestivalActivity activity, Entity focus, List<int3> route, int waitTicks) : Job
{
    public override bool Interruptible => true;

    public FestivalActivity Activity => activity;

    /// <summary>The thing it's about: the seat, the bonfire, the game, the show table.</summary>
    public override Entity Focus => focus;

    /// <summary>Sat at the feast (for the pose).</summary>
    public bool Seated { get; private set; }

    /// <summary>Going round the bonfire in the dance (for the renderer's step).</summary>
    public bool Dancing { get; private set; }

    protected override IEnumerable<JobStatus> Run()
    {
        var part = Sim.Festival.Part;
        bool Over() => Sim.Festival.Part != part || Sim.Round.Phase != RoundPhase.Festival;
        if (activity == FestivalActivity.Dance)
        {
            Report = "Dancing";
            Dancing = true;
            foreach (var cell in route)
            {
                foreach (var s in GoTo(cell, PathEnd.OnTile)) yield return s;
                if (HasFailed || Over() || !Alive(focus)) yield break;
            }
            yield break;
        }
        if (activity == FestivalActivity.Feast)
        {
            Report = "At the feast";
            if (!Alive(focus) || !Reserve(focus)) yield break;
        }
        else
            Report = activity switch
            {
                FestivalActivity.Bonfire => "By the bonfire",
                FestivalActivity.Games => "Watching the games",
                FestivalActivity.Show => "Looking over the show table",
                FestivalActivity.Gather => "Gathered round the well",
                _ => "At the festival",
            };
        foreach (var s in GoTo(route[0], PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        Seated = activity == FestivalActivity.Feast;
        for (var t = 0; t < waitTicks && !Over() && (focus.IsNull || Alive(focus)); t++)
            yield return JobStatus.Running;
    }
}
