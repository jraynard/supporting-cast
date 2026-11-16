using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Environment;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Bots;

/// <summary>
/// The innkeeper's chores beyond the bar: the keg changed before the cask runs dry (it knows how
/// low it is from the gauge at its last pour, as a player does), the book struck through for guests
/// who've gone, and a vacated room's chamber pot emptied at the midden and its jug filled at the
/// well. It goes by what it's seen: a pot or jug in sight, or in a room it knows was left.
/// </summary>
public sealed partial class BotPlayer
{
    /// <summary>Servings left in each cask as its last pour showed them.</summary>
    private readonly Dictionary<Entity, int> _caskSeen = [];

    // The cask it last went to knock a keg into, and how often it's tried (a cask fuller than it
    // thought won't take one).
    private Entity _tapping = Entity.Null;
    private int _tapTries;

    /// <summary>Beds it's seen left (unmade) whose rooms may still want their pot and jug seen to.</summary>
    private readonly HashSet<Entity> _turning = [];

    /// <summary>A bucket kept for the rooms' chores, not to be taken back to the well yet; and the chest it came from.</summary>
    private bool _choreBucket;
    private Entity _bucketFrom = Entity.Null;

    /// <summary>A bucket on it is for the rooms' chores: <see cref="PutBucketBack"/> leaves it be.</summary>
    private bool KeepsBucket => _choreBucket;

    /// <summary>The casks beside it, as the gauge showed them on the pour it's just finished.</summary>
    private void NoteCasks(Simulation sim, Entity me)
    {
        var w = sim.World;
        var here = w.Get<Position>(me).Cell;
        foreach (var e in sim.SpiralFrom(here, 1))
            if (sim.Map.InBounds(e) && sim.Map.Structure[sim.Map.Idx(e)] is { IsNull: false } cask
                && w.TryGet<Thing>(cask, out var t) && t.Def.Building?.Cask != null && AvatarSystem.InReach(sim, here, cask))
                _caskSeen[cask] = PourSession.ServingsLeft(sim, cask);
    }

    /// <summary>A cask it knows is low, whose keg it has in its arms: it finishes knocking that in first.</summary>
    private Entity KegInArms(Simulation sim, Entity me)
    {
        foreach (var cask in _caskSeen.Keys.OrderBy(e => e.Index))
            if (sim.World.IsAlive(cask) && !BarWork.KegFor(sim, me, cask).IsNull && _caskSeen[cask] <= BarWork.TapBelow)
                return cask;
        return Entity.Null;
    }

    /// <summary>A cask it saw run low: a keg from the storeroom and knocked in. True while at it.</summary>
    private bool ChangeKeg(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        // The tap's in (the keg's gone from its arms): the cask is full again, as it was told.
        if (!_tapping.IsNull && BarWork.KegFor(sim, me, _tapping).IsNull)
        {
            _caskSeen.Remove(_tapping);
            (_tapping, _tapTries) = (Entity.Null, 0);
        }
        var low = _caskSeen.Where(kv => kv.Value <= BarWork.TapBelow && w.IsAlive(kv.Key)).Select(kv => kv.Key).OrderBy(e => e.Index).FirstOrDefault(Entity.Null);
        if (low.IsNull)
            return false;
        if (_tapTries >= 3)
        {
            // It wouldn't take the keg: someone's changed it already, or there was more in it than it thought.
            _caskSeen.Remove(low);
            (_tapping, _tapTries) = (Entity.Null, 0);
            return false;
        }
        var keg = w.Get<Thing>(low).Def.Building!.Cask!.Keg;
        if (BarWork.KegFor(sim, me, low).IsNull && KegStore(sim, me, keg).IsNull)
            return false; // none in: the carter brings more in the morning
        if (_tapTries == 0 && Alarm($"keg:{low.Index}:{now / SimClock.TicksPerHour}"))
            Say(sim, me, "inn.keg", Volume.Say, now);
        return Restock(sim, me, view, now, low);
    }

    /// <summary>
    /// A free hand: what it holds into the pack, or, if it won't go (a keg, a bucket), set down
    /// beside it. A chore's bucket put down is done with.
    /// </summary>
    private void FreeHand(Simulation sim, Entity me)
    {
        var w = sim.World;
        var held = w.Get<Carrying>(me).Item;
        if (held.IsNull)
            return;
        var t = w.Get<Thing>(held);
        if (!t.Def.Bulky)
        {
            Send(new StowCommand(me));
            return;
        }
        var here = w.Get<Position>(me).Cell;
        foreach (var c in sim.SpiralFrom(here, 1))
            if (sim.Map.InBounds(c) && AvatarSystem.InReach(here, c) && sim.CanAcceptItem(c, t.Def, out _, t.Material))
            {
                Send(new AvatarDropCommand(me, c));
                break;
            }
        if (Buckets.IsBucket(sim, held))
            _choreBucket = false;
    }

    /// <summary>
    /// Into a guest room behind a locked door: its key off the board (or the spare from the
    /// storeroom), then the door. Null once it's open (or not locked); else whether it's at it
    /// (false: there's no key to be had).
    /// </summary>
    private bool? IntoRoom(Simulation sim, Entity me, PerceptionView view, Entity inn, Entity bed, long now)
    {
        var w = sim.World;
        if (!w.TryGet<GuestRoom>(bed, out var room) || !sim.IsLocked(room.Door))
            return null;
        if (InnRooms.KeyOn(sim, me, bed).IsNull)
        {
            var seed = InnRooms.SeedOf(sim, bed);
            var store = InnRooms.OnBoard(sim, inn, seed).IsNull ? InnRooms.SpareBox(sim, inn) : InnRooms.Board(sim, inn);
            if (InnRooms.KeyIn(sim, store, seed).IsNull)
                return false; // no key to be had: leave it
            Doing = "fetching a room key";
            return AtStation(sim, me, store, now, s =>
            {
                if (view.InContainer(s).FirstOrDefault(k => InnRooms.RoomOfKey(sim, k) == room.Number, Entity.Null) is { IsNull: false } key)
                    Send(new TakeFromStorageCommand(me, key));
            });
        }
        Doing = "unlocking a room";
        return AtStation(sim, me, room.Door, now, d => Send(new Tinkering.TurnKeyCommand(me, d, false)));
    }

    /// <summary>
    /// The book, as last read at the board, still has a guest in a room they've left (the bed
    /// stripped, and their key back on its hook or the night gone): strike them through, so the
    /// room reads free. Never the guest it's letting a room to.
    /// </summary>
    private bool StrikeOut(Simulation sim, Entity me, PerceptionView view, Entity inn, long now)
    {
        var board = InnRooms.Board(sim, inn);
        if (board.IsNull)
            return false;
        if (view.RoomBoard(board) is { } read)
            _roomBoard = read;
        if (_roomBoard is not { } book || book.Board != board)
            return false;
        var waiting = view.Orders().Where(o => o.Kind == Defs.ServiceKind.Lodging && o.Status == Shops.OrderStatus.Open).Select(o => o.Poster).ToHashSet();
        for (var r = 1; r <= book.Rooms; r++)
        {
            if (book.Lines[r] is not { Out: false } line || waiting.Contains(line.Guest) || (line.Night >= book.Tonight && !book.Hung[r]))
                continue;
            var bed = sim.World.Query<GuestRoom>().FirstOrDefault(b => sim.World.Get<GuestRoom>(b).Number == r, Entity.Null);
            if (bed.IsNull || !Lodging.IsUnmade(sim, bed))
                continue;
            Doing = $"striking {line.Name} out of the book";
            var room = r;
            return AtStation(sim, me, board, now, b => Send(new MarkOutCommand(me, b, room)));
        }
        return false;
    }

    /// <summary>
    /// A room someone's left: the chamber pot emptied into a bucket and tipped at the midden, then
    /// the bucket filled at the well for the jug; the bucket back in its chest after. True while at it.
    /// </summary>
    private bool TurnRooms(Simulation sim, Entity me, PerceptionView view, Entity inn, long now)
    {
        var w = sim.World;
        NoteLeftRooms(sim, inn);
        // What it knows wants doing: in a room it saw left, or in plain sight.
        Entity Wanting<T>(string def) where T : struct
        {
            foreach (var bed in _turning.OrderBy(e => e.Index))
                if (RoomChores.FixtureOf(sim, bed, def) is { IsNull: false } f && w.Has<T>(f))
                    return f;
            foreach (var part in Buildings.Parts(sim, inn))
                if (w.Get<Thing>(part).Def.Name == def && w.Has<T>(part) && view.CanSeeCell(w.Get<Thing>(part).Cell))
                    return part;
            return Entity.Null;
        }
        var pot = Wanting<Slops>(RoomChores.PotDef);
        var jug = Wanting<JugEmpty>(RoomChores.StandDef);
        _turning.RemoveWhere(b => !w.IsAlive(b) || RoomChores.Ready(sim, b)
            || ((RoomChores.FixtureOf(sim, b, RoomChores.PotDef) is var p && (p.IsNull || !w.Has<Slops>(p)))
                && (RoomChores.FixtureOf(sim, b, RoomChores.StandDef) is var s && (s.IsNull || !w.Has<JugEmpty>(s)))));

        var held = w.Get<Carrying>(me).Item;
        // Slops: out to the midden, and tipped.
        if (RoomChores.SlopsOn(sim, me) is { IsNull: false } slops)
        {
            Doing = "tipping the slops";
            if (held != slops)
                return InHand(sim, me, slops);
            return AtStation(sim, me, Nearest(sim, me, RoomChores.MiddenDef), now, m => Send(new BarWorkCommand(me, m)));
        }
        var empty = RoomChores.EmptyBucketOn(sim, me);
        var water = RoomChores.WaterOn(sim, me);
        if (!pot.IsNull && !empty.IsNull)
        {
            Doing = "emptying a chamber pot";
            _choreBucket = true;
            if (held != empty)
                return InHand(sim, me, empty);
            return IntoRoom(sim, me, view, inn, RoomChores.BedIn(sim, w.Get<Thing>(pot).Cell), now)
                ?? AtStation(sim, me, pot, now, x => Send(new BarWorkCommand(me, x)));
        }
        if (!jug.IsNull && !water.IsNull)
        {
            Doing = "filling a jug";
            _choreBucket = true;
            if (held != water)
                return InHand(sim, me, water);
            return IntoRoom(sim, me, view, inn, RoomChores.BedIn(sim, w.Get<Thing>(jug).Cell), now)
                ?? AtStation(sim, me, jug, now, x => Send(new BarWorkCommand(me, x)));
        }
        if (!jug.IsNull && pot.IsNull && !empty.IsNull)
        {
            Doing = "drawing water for a jug";
            _choreBucket = true;
            if (held != empty)
                return InHand(sim, me, empty);
            var wells = Buckets.Wells(sim);
            if (wells.Count == 0)
                return false;
            var here = w.Get<Position>(me).Cell;
            return AtStation(sim, me, wells.MinBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, here))!, now, x => Use(sim, me, x));
        }
        if (!pot.IsNull || !jug.IsNull)
            return FetchBucket(sim, me, view, inn, now);
        // All done: the bucket back where it came from.
        if (_choreBucket && Buckets.IsBucket(sim, held) && !Buckets.IsFull(sim, held) && w.IsAlive(_bucketFrom) && sim.IsContainer(_bucketFrom))
        {
            Doing = "putting the bucket away";
            return AtStation(sim, me, _bucketFrom, now, c =>
            {
                Use(sim, me, c);
                _choreBucket = false;
            });
        }
        _choreBucket = false; // one from the well goes back there (PutBucketBack)
        return false;
    }

    /// <summary>Rooms it sees left (their beds stripped bare): their pot and jug will want seeing to too.</summary>
    private void NoteLeftRooms(Simulation sim, Entity inn)
    {
        foreach (var part in Buildings.Parts(sim, inn))
            if (Lodging.IsUnmade(sim, part))
                _turning.Add(part);
    }

    /// <summary>Something on it into its hands: what's there already into the pack (or set down, if it won't go).</summary>
    private bool InHand(Simulation sim, Entity me, Entity item)
    {
        var held = sim.World.Get<Carrying>(me).Item;
        if (!held.IsNull && sim.World.Get<Thing>(held).Def.Bulky)
            FreeHand(sim, me);
        else
            Send(new HoldCommand(me, item));
        return true;
    }

    /// <summary>An empty bucket for the chores: the inn's own from its chest, or one from the well.</summary>
    private bool FetchBucket(Simulation sim, Entity me, PerceptionView view, Entity inn, long now)
    {
        var w = sim.World;
        var here = w.Get<Position>(me).Cell;
        if (!w.Get<Carrying>(me).Item.IsNull)
        {
            FreeHand(sim, me);
            return true;
        }
        Doing = "fetching a bucket";
        // The inn keeps one in the storeroom (it's where it's put back).
        var chest = Buildings.Parts(sim, inn).Where(p => sim.IsContainer(p) && sim.StoredIn(p).Any(i => w.Get<Thing>(i).Def.Name == Buckets.Empty))
            .OrderBy(p => Maps.Map.Distance(w.Get<Thing>(p).Cell, here)).FirstOrDefault(Entity.Null);
        if (!chest.IsNull)
            return AtStation(sim, me, chest, now, c =>
            {
                if (view.InContainer(c).FirstOrDefault(i => w.Get<Thing>(i).Def.Name == Buckets.Empty, Entity.Null) is { IsNull: false } b)
                {
                    _bucketFrom = c;
                    _choreBucket = true;
                    Send(new TakeFromStorageCommand(me, b));
                }
            });
        // Else one lying about in sight, or by the well.
        var bucket = sim.AvailableItems().Where(e => w.Get<Thing>(e) is { Spawned: true } t && t.Def.Name == Buckets.Empty && view.CanSee(e)
                && !sim.Reservations.IsReservedByOther(e, me))
            .OrderBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, here)).FirstOrDefault(Entity.Null);
        if (!bucket.IsNull)
            return AtStation(sim, me, bucket, now, b =>
            {
                _bucketFrom = Entity.Null;
                _choreBucket = true;
                Use(sim, me, b);
            });
        var wells = Buckets.Wells(sim);
        if (wells.Count == 0)
            return false;
        Walk(sim, me, w.Get<Thing>(wells.MinBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, here))!).Cell, now);
        return true;
    }
}
