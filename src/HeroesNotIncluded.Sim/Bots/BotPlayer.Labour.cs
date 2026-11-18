using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Environment;
using HeroesNotIncluded.Sim.Favours;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Bots;

/// <summary>
/// The labourer's work: the festival put up on the last afternoon, errands off the notice board
/// (read at the board, as a player must, taken on, then fetched from what's lying about or in the
/// town's stores, or gathered: a tree felled, a bush picked), and between them, buckets left lying
/// carried back to the well. Everything through the work key, as a player's labourer does it.
/// </summary>
public sealed partial class BotPlayer
{
    /// <summary>How often it looks in at the notice board between errands.</summary>
    public const int BoardEveryTicks = SimClock.TicksPerHour * 3;

    /// <summary>How far off it'll go to gather (a known wood, a boulder), in cells.</summary>
    public const int GatherRange = 80;

    private long _boardRead = -BoardEveryTicks;
    private readonly HashSet<Entity> _raised = [];
    private int _errand;

    // A slip: it counts one short of what was asked, and finds out when it's handed over.
    private int _shortBy;
    private bool _handedOver;

    /// <summary>The labourer between errands. True while at it.</summary>
    private bool Labour(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(me, out var trade) || !trade.Def.Gathers || !sim.Round.Live)
            return false;
        // Mid-swing at a tree or a site: the key's held, let it work.
        if (w.Get<Mind>(me).Job is Ai.HarvestJob or Ai.BuildJob)
            return true;
        return PutUp(sim, me, now) || Errand(sim, me, view, now) || ReadBoard(sim, me, view, now) || StrayBucket(sim, me, view, now);
    }

    /// <summary>The afternoon before the festival: the sites in the square, built (their stuff's already there).</summary>
    private bool PutUp(Simulation sim, Entity me, long now)
    {
        var w = sim.World;
        if (sim.Round.Phase != RoundPhase.Playing || !sim.Festival.SettingUp)
            return false;
        var here = w.Get<Position>(me).Cell;
        var site = sim.Festival.Sites.Where(e => !e.IsNull && w.IsAlive(e) && w.Has<Construction>(e) && sim.CanBuildNow(e))
            .OrderBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, here)).FirstOrDefault(Entity.Null);
        if (site.IsNull)
            return false;
        Doing = $"putting up the {w.Get<Construction>(site).Target.Label}";
        if (_raised.Add(site))
            BotStats.Count(Stats.Taken, "FestivalSite");
        WorkAt(sim, me, w.Get<Thing>(site).Cell, now);
        return true;
    }

    /// <summary>Walk up to a cell and work what's there with the key held (a tree, a site, a stack to pick up).</summary>
    private void WorkAt(Simulation sim, Entity me, int3 cell, long now)
    {
        if (sim.World.Get<Mind>(me).Job is AvatarWalkJob { Interact: true } walk && walk.Target == cell)
            return; // on its way
        if (now - _lastWalk < ThinkTicks * 2)
            return;
        _lastWalk = now;
        Send(new Core.AvatarGoToCommand(me, cell, Interact: true));
    }

    /// <summary>The errand it's taken on, if it's still open and its own.</summary>
    private KnownFavour? MyErrand(PerceptionView view)
    {
        foreach (var f in view.Favours())
            if (f.Id == _errand && f.Status == FavourStatus.Open && f.MineToDo && f.Wants != null)
                return f;
        return null;
    }

    /// <summary>The errand in hand: what's asked fetched or gathered, then brought to whoever asked.</summary>
    private bool Errand(Simulation sim, Entity me, PerceptionView view, long now)
    {
        if (_errand == 0)
            return false;
        if (MyErrand(view) is not { } f)
        {
            (_errand, _shortBy, _handedOver) = (0, 0, false);
            return false;
        }
        var wants = f.Wants!;
        var have = OnMe(sim, me, wants.Name);
        if (have >= f.Count - _shortBy)
        {
            Doing = $"bringing {f.PosterName} the {wants.Label}";
            if (!Reach(sim, me, f.Poster, now, returning: true))
                return true;
            if (_handedOver && have < f.Count)
            {
                // Handed over and they're still waiting: it's short. Count again, and fetch the rest.
                (_shortBy, _handedOver) = (0, false);
                Stats.Mistakes++;
                Say(sim, me, "errand.short", Volume.Say, now, who: f.PosterName);
                return true;
            }
            _handedOver = true;
            Send(new DeliverFavourCommand(me, f.Id));
            return true;
        }
        var need = f.Count - _shortBy - have;
        if (Known(sim, me, view, wants) is { IsNull: false } stack)
        {
            Doing = $"fetching {wants.Label} for {f.PosterName}";
            return Fetch(sim, me, view, stack, need, now);
        }
        if (Source(sim, me, view, wants) is { IsNull: false } source)
        {
            Doing = $"gathering {wants.Label} for {f.PosterName}";
            WorkAt(sim, me, sim.World.Get<Thing>(source).Cell, now);
            return true;
        }
        // Nowhere to be had that it knows of: it's let go (the asker will find someone else, or not).
        (_errand, _shortBy, _handedOver) = (0, 0, false);
        return false;
    }

    /// <summary>Some of a stack picked up from the ground, or taken out of the store it's kept in.</summary>
    public bool Fetch(Simulation sim, Entity me, PerceptionView view, Entity stack, int need, long now)
    {
        var w = sim.World;
        if (w.TryGet<Stored>(stack, out var stored))
            return AtStation(sim, me, stored.In, now, c =>
            {
                if (view.InContainer(c).Contains(stack))
                    Send(new Core.TakeFromStorageCommand(me, stack, need));
            });
        // A full pair of hands takes nothing more that won't go in the pack: put it down first.
        var t = w.Get<Thing>(stack);
        if (!w.Get<Carrying>(me).Item.IsNull && sim.PackOf(me) is var pack && (pack.IsNull || sim.ContainerRoom(pack, t.Def, t.Material) <= 0))
        {
            FreeHand(sim, me);
            return true;
        }
        WorkAt(sim, me, t.Cell, now);
        return true;
    }

    /// <summary>
    /// The nearest stack of a thing it knows of and may take: lying in sight, or kept in the town's
    /// stores (where anyone may help themselves). Entity.Null if none.
    /// </summary>
    public static Entity Known(Simulation sim, Entity me, PerceptionView view, ThingDef def)
    {
        var w = sim.World;
        var here = w.Get<Position>(me).Cell;
        var best = Entity.Null;
        var bestD = int.MaxValue;
        foreach (var e in sim.AvailableItems())
        {
            var t = w.Get<Thing>(e);
            if (t.Def != def || !sim.IsAvailable(e) || sim.Reservations.IsReservedByOther(e, me) || !Ai.Claims.Free(sim, e, me)
                || (t.Spawned && !view.CanSee(e)))
                continue;
            var at = sim.LocationOf(e);
            var d = Maps.Map.Distance(here, at);
            if (d < bestD && sim.Regions.CanReach(here, at, t.Spawned ? PathEnd.OnTile : PathEnd.Touch))
                (best, bestD) = (e, d);
        }
        return best;
    }

    /// <summary>
    /// Something to gather a thing from: a tree or a boulder (the town's woods and rocks are known),
    /// or a bush in sight that's ready to pick. Entity.Null if none in reach.
    /// </summary>
    private static Entity Source(Simulation sim, Entity me, PerceptionView view, ThingDef def)
    {
        var w = sim.World;
        if (!RoundSystem.MayGather(sim, me))
            return Entity.Null;
        var here = w.Get<Position>(me).Cell;
        var best = Entity.Null;
        var bestD = GatherRange + 1;
        foreach (var e in w.Each<Thing>())
        {
            var t = w.Get<Thing>(e);
            if (!t.Spawned || t.Def.Harvest is not { } harvest || !harvest.Yield.Exists(y => y.ThingDef == def) || w.Has<Construction>(e))
                continue;
            var d = Maps.Map.Distance(here, t.Cell);
            if (d >= bestD)
                continue;
            if (w.TryGet<PlantState>(e, out var plant) && (!plant.Ready || !view.CanSeeCell(t.Cell)))
                continue;
            if (sim.Reservations.IsReservedByOther(e, me) || !sim.Regions.CanReach(here, t.Cell, PathEnd.Touch))
                continue;
            (best, bestD) = (e, d);
        }
        return best;
    }

    /// <summary>
    /// Every few hours, to the notice board to read it; an errand on it that it can fill, and nobody's
    /// taken, it takes on. True while at it.
    /// </summary>
    private bool ReadBoard(Simulation sim, Entity me, PerceptionView view, long now)
    {
        if (_errand != 0)
            return false;
        // What it already knows of first: an errand someone told it of, or one it read and couldn't do then.
        foreach (var f in view.Favours().OrderBy(f => f.PostedTick))
        {
            if (f.Status != FavourStatus.Open || f.Wants is not { } wants || f.Mine || (f.AssigneeName.Length > 0 && !f.MineToDo)
                || (Known(sim, me, view, wants).IsNull && Source(sim, me, view, wants).IsNull))
                continue;
            _errand = f.Id;
            // A slip: it'll count one short.
            _shortBy = f.Count > 1 && _slipRand.Chance(SlipChance) ? 1 : 0;
            _handedOver = false;
            if (!f.MineToDo)
            {
                BotStats.Count(Stats.Taken, "Errand");
                Send(new AcceptFavourCommand(me, f.Id));
                Say(sim, me, "errand.take", Volume.Say, now, who: f.PosterName, thing: wants.Label);
            }
            Doing = $"taking on {f.PosterName}'s errand";
            return true;
        }
        if (now - _boardRead < BoardEveryTicks)
            return false;
        var board = Nearest(sim, me, "NoticeBoard");
        if (board.IsNull)
            return false;
        Doing = "reading the notice board";
        return AtStation(sim, me, board, now, _ =>
        {
            _boardRead = now;
            Send(new ReadBoardCommand(me));
        });
    }

    /// <summary>A bucket left lying away from the wells, in sight: picked up, and it goes back (<see cref="PutBucketBack"/>).</summary>
    private bool StrayBucket(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (sim.Fire.BurningTiles.Count > 0 || !w.Get<Carrying>(me).Item.IsNull)
            return false;
        var wells = Buckets.Wells(sim);
        if (wells.Count == 0)
            return false;
        var here = w.Get<Position>(me).Cell;
        var stray = Entity.Null;
        var bestD = int.MaxValue;
        foreach (var e in sim.AvailableItems())
        {
            var t = w.Get<Thing>(e);
            if (!t.Spawned || !Buckets.IsBucket(sim, e) || !view.CanSee(e) || sim.Reservations.IsReservedByOther(e, me)
                || wells.Any(x => Maps.Map.Distance(w.Get<Thing>(x).Cell, t.Cell) < Buckets.StrayFrom))
                continue;
            var d = Maps.Map.Distance(here, t.Cell);
            if (d < bestD && sim.Regions.CanReach(here, t.Cell, PathEnd.Touch))
                (stray, bestD) = (e, d);
        }
        if (stray.IsNull)
            return false;
        Doing = "carrying a bucket back to the well";
        return AtStation(sim, me, stray, now, b =>
        {
            BotStats.Count(Stats.Taken, "StrayBucket");
            Use(sim, me, b);
        });
    }
}
