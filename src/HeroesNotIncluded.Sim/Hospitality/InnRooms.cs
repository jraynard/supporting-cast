using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;

namespace HeroesNotIncluded.Sim.Hospitality;

/// <summary>
/// A let bed's room: its number (painted on the door, and what the keys are known by) and its
/// door, which has a lock fitted. Two beds in one room share its number and its key.
/// </summary>
public struct GuestRoom
{
    public int Number;
    public Entity Door;
}

/// <summary>A guest who's checked out and means to leave their key on the bar counter on the way.</summary>
public struct ReturningKey
{
    public Entity Key;
    public Entity Inn;
}

/// <summary>
/// A guest handed the key to a room someone else has tonight: they go up to it, walk in on
/// whoever's there, and come back down for another (<see cref="OwedRoom"/>).
/// </summary>
public struct WalkIn
{
    public Entity Bed;
}

/// <summary>
/// Paid for a room and given someone else's: the next key they're handed is on the house, and they
/// give back the wrong one with it.
/// </summary>
public struct OwedRoom
{
    public Entity Inn;
    public Entity WrongBed;
}

/// <summary>
/// The inn's rooms upstairs, their locks and their keys (docs/DESIGN.md, Innkeeper, "Rooms"). Each
/// room's door is locked, and its key hangs on the key board behind the bar; the keeper holds a
/// second set of their own (<see cref="Defs.TradeDef.HoldsKeys"/>). A room is let by handing a guest
/// its key: whatever key they're given is the room they go to. Leaving, a guest hands their key in
/// at the bar, or forgets it in the room, or now and then walks off with it.
/// </summary>
public static class InnRooms
{
    public const string BoardDef = "KeyBoard";

    /// <summary>Pins in a room door's lock.</summary>
    public const int RoomPins = 3;

    /// <summary>Of a guest checking out: the chance they leave the key in the room, and that they take it with them.</summary>
    public const float LeftInRoomChance = 0.2f, WalksOffChance = 0.08f;

    /// <summary>How long a guest gives to finding the bar to hand in their key before they give up and keep it.</summary>
    public const int HandInTicks = 2400;

    /// <summary>
    /// Number the let beds of an inn by their rooms, fit a lock to each room's door (locked), and
    /// hang a key to each on the key board. Done once, when the town is raised.
    /// </summary>
    public static void FitLocks(Simulation sim, Entity inn)
    {
        var w = sim.World;
        if (!w.IsAlive(inn) || !w.TryGet<PresetBuilding>(inn, out var b) || !b.Def.LetsBeds)
            return;
        sim.RebuildDerivedIfNeeded();
        var parts = Buildings.Parts(sim, inn);
        var board = parts.FirstOrDefault(e => w.Get<Thing>(e).Def.Name == BoardDef, Entity.Null);
        var doors = parts.Where(e => w.Get<Thing>(e).Def.Building is { IsDoor: true }).ToList();
        var numbered = new Dictionary<Entity, int>();
        foreach (var bed in parts.Where(e => w.Get<Thing>(e).Def.Building is { IsBed: true } && !w.Has<GuestRoom>(e)))
        {
            var door = DoorInto(sim, w.Get<Thing>(bed).Cell, doors);
            if (door.IsNull)
                continue;
            if (!numbered.TryGetValue(door, out var number))
            {
                number = numbered.Count + 1;
                numbered[door] = number;
                // Cut from the door's place and the round's seed, not sim.Rand: fitting them leaves
                // the rest of the town's dice where they were.
                var at = w.Get<Thing>(door).Cell;
                if (!w.Has<LockCut>(door))
                    w.Add(door, new LockCut { Seed = SimRandom.Hash(at.X, at.Y * 64 + at.Z, (uint)sim.Seed ^ 0x600D_D00Bu) | 1u, Pins = RoomPins, Tightness = 0.4f, Maker = Entity.Null });
                sim.SetLocked(door, true);
                var cut = w.Get<LockCut>(door);
                if (!board.IsNull)
                    sim.Store(board, Locksmithing.MakeKey(sim, cut.Seed, cut.Pins, 1f));
            }
            w.Add(bed, new GuestRoom { Number = number, Door = door });
        }
    }

    /// <summary>The door into the room a cell is in: a door with a room cell beside it.</summary>
    private static Entity DoorInto(Simulation sim, int3 cell, List<Entity> doors)
    {
        var room = sim.Rooms.RoomAt(cell);
        if (room == null)
            return Entity.Null;
        foreach (var door in doors)
        {
            var at = sim.World.Get<Thing>(door).Cell;
            if (at.Z != cell.Z)
                continue;
            foreach (var d in Map.Neighbors4)
            {
                var n = new int3(at.X + d.X, at.Y + d.Y, at.Z);
                if (sim.Rooms.RoomAt(n) == room)
                    return door;
            }
        }
        return Entity.Null;
    }

    /// <summary>The room number of a let bed, or 0.</summary>
    public static int Number(Simulation sim, Entity bed) =>
        sim.World.IsAlive(bed) && sim.World.TryGet<GuestRoom>(bed, out var r) ? r.Number : 0;

    /// <summary>The lock seed of a let bed's room door, or 0.</summary>
    public static uint SeedOf(Simulation sim, Entity bed) =>
        sim.World.IsAlive(bed) && sim.World.TryGet<GuestRoom>(bed, out var r) && sim.World.IsAlive(r.Door) && sim.World.TryGet<LockCut>(r.Door, out var cut)
            ? cut.Seed : 0u;

    /// <summary>The let beds of a room, in a fixed order.</summary>
    public static List<Entity> BedsOf(Simulation sim, uint seed)
    {
        var beds = new List<Entity>();
        if (seed == 0)
            return beds;
        foreach (var e in sim.World.Query<GuestRoom>().OrderBy(e => e.Index))
            if (SeedOf(sim, e) == seed)
                beds.Add(e);
        return beds;
    }

    /// <summary>Is this a key to one of the inn's rooms?</summary>
    public static bool IsRoomKey(Simulation sim, Entity item) =>
        sim.World.IsAlive(item) && sim.World.TryGet<Key>(item, out var key) && RoomNumber(sim, key.Seed) > 0;

    /// <summary>The room a lock seed opens, or 0.</summary>
    public static int RoomNumber(Simulation sim, uint seed)
    {
        if (seed == 0 || sim.World.Count<GuestRoom>() == 0)
            return 0;
        foreach (var e in sim.World.Each<GuestRoom>())
            if (SeedOf(sim, e) == seed)
                return sim.World.Get<GuestRoom>(e).Number;
        return 0;
    }

    /// <summary>The room a key opens, or 0.</summary>
    public static int RoomOfKey(Simulation sim, Entity key) =>
        sim.World.IsAlive(key) && sim.World.TryGet<Key>(key, out var k) ? RoomNumber(sim, k.Seed) : 0;

    /// <summary>"key to room 3" for a room key; null for anything else.</summary>
    public static string? KeyLabel(Simulation sim, Entity item) =>
        RoomOfKey(sim, item) is > 0 and var n ? $"key to room {n}" : null;

    /// <summary>The inn's key board, or Entity.Null.</summary>
    public static Entity Board(Simulation sim, Entity inn) =>
        sim.World.IsAlive(inn) ? Buildings.Parts(sim, inn).FirstOrDefault(e => sim.World.Get<Thing>(e).Def.Name == BoardDef, Entity.Null) : Entity.Null;

    /// <summary>The key to a room hanging on the inn's board, or Entity.Null.</summary>
    public static Entity OnBoard(Simulation sim, Entity inn, uint seed)
    {
        var board = Board(sim, inn);
        if (board.IsNull || seed == 0)
            return Entity.Null;
        foreach (var k in sim.StoredIn(board))
            if (sim.World.TryGet<Key>(k, out var key) && key.Seed == seed)
                return k;
        return Entity.Null;
    }

    /// <summary>The key to this bed's room on someone (in hand or pack), or Entity.Null.</summary>
    public static Entity KeyOn(Simulation sim, Entity who, Entity bed)
    {
        var seed = SeedOf(sim, bed);
        if (seed == 0)
            return Entity.Null;
        foreach (var k in Locksmithing.Keys(sim, who))
            if (sim.World.Get<Key>(k).Seed == seed)
                return k;
        return Entity.Null;
    }

    /// <summary>
    /// Whoever's been given a bed of this room for tonight (someone who's not <paramref name="but"/>),
    /// or Entity.Null: the room's taken.
    /// </summary>
    public static Entity LodgedIn(Simulation sim, uint seed, Entity but)
    {
        var w = sim.World;
        if (seed == 0 || w.Count<LodgeStay>() == 0)
            return Entity.Null;
        foreach (var e in w.Query<LodgeStay>().OrderBy(e => e.Index))
            if (e != but && Lodging.Lodged(sim, e) && SeedOf(sim, w.Get<LodgeStay>(e).Bed) == seed)
                return e;
        return Entity.Null;
    }

    /// <summary>
    /// The bed a key gives its holder: in that room, one nobody else has been given (the first, if
    /// they're all taken: someone else's). Entity.Null if the key opens no room.
    /// </summary>
    public static Entity BedFor(Simulation sim, uint seed, Entity guest)
    {
        var beds = BedsOf(sim, seed);
        if (beds.Count == 0)
            return Entity.Null;
        var w = sim.World;
        foreach (var bed in beds)
        {
            var taken = false;
            foreach (var o in w.Each<BedOwner>())
                if (o != guest && w.Get<BedOwner>(o).Bed == bed && w.IsAlive(o))
                    taken = true;
            if (!taken)
                return bed;
        }
        return beds[0];
    }

    /// <summary>A room key lying loose in the inn (handed in on the counter, or left in a room): for the keeper to hang up again.</summary>
    public static bool LooseKey(Simulation sim, Entity item) =>
        sim.World.IsAlive(item) && sim.World.Get<Thing>(item).Spawned && IsRoomKey(sim, item);

    /// <summary>
    /// What a guest does with their key as they check out: hand it in at the bar (most of the time),
    /// leave it in the room, or walk off with it.
    /// </summary>
    public static void OnCheckOut(Simulation sim, Entity guest, Entity bed)
    {
        var w = sim.World;
        var key = KeyOn(sim, guest, bed);
        if (key.IsNull)
            return;
        var roll = sim.Rand.NextFloat();
        if (roll < WalksOffChance)
            return; // it goes where they go
        if (roll < WalksOffChance + LeftInRoomChance && w.IsAlive(bed))
        {
            var left = Inventory.TakeFromPerson(sim, guest, key, 1);
            sim.PlaceItemNear(left, w.Get<Thing>(bed).Cell, byHand: true);
            return;
        }
        if (w.Has<ReturningKey>(guest))
            w.Remove<ReturningKey>(guest);
        w.Add(guest, new ReturningKey { Key = key, Inn = Buildings.Of(sim, bed) });
    }

    /// <summary>Where a guest leaves their key at the inn: on the bar counter, else by the front of the bar's line.</summary>
    public static int3? HandInAt(Simulation sim, Entity inn)
    {
        var w = sim.World;
        if (!w.IsAlive(inn) || !w.TryGet<PresetBuilding>(inn, out var b))
            return null;
        var counter = Buildings.Parts(sim, inn).FirstOrDefault(e => w.Get<Thing>(e).Def.Name == "BarCounter", Entity.Null);
        if (!counter.IsNull)
            return w.Get<Thing>(counter).Cell;
        return b.Anchor;
    }
}

/// <summary>
/// A guest who's checked out leaves their room key at the bar on the way: to the counter, the key
/// set down on it, a word to whoever's there. Given up on if the way's too long.
/// </summary>
public sealed class HandInKeyJob : Ai.Job
{
    protected override IEnumerable<Ai.JobStatus> Run()
    {
        var w = Sim.World;
        if (!w.TryGet<ReturningKey>(Pawn, out var r))
            yield break;
        // Once tried is enough: whatever happens, they don't come back to it.
        w.Remove<ReturningKey>(Pawn);
        if (!Systems.Inventory.OnPerson(Sim, Pawn, r.Key) || InnRooms.HandInAt(Sim, r.Inn) is not { } at)
            yield break;
        Report = "Handing in the key";
        foreach (var s in GoTo(at, PathEnd.Touch)) yield return s;
        if (HasFailed || !Alive(r.Key) || !Systems.Inventory.OnPerson(Sim, Pawn, r.Key))
            yield break;
        var key = Systems.Inventory.TakeFromPerson(Sim, Pawn, r.Key, 1);
        Sim.PlaceItemNear(key, at, byHand: true);
        Sim.Speech.Say(Sim, Pawn, "Key's on the counter. Thanks for the bed.", Speech.Volume.Say);
    }
}

/// <summary>
/// Given the key to a room someone else has tonight, a guest goes up to put their things down and
/// walks in on them. Both are put out; the one who walked in comes back down for another room.
/// </summary>
public sealed class WalkInJob(Entity bed) : Ai.Job
{
    /// <summary>Satisfaction each of them loses.</summary>
    public const float Mood = 1.5f;

    public override Entity Focus => bed;

    protected override IEnumerable<Ai.JobStatus> Run()
    {
        if (!Alive(bed))
        {
            Sim.World.Remove<WalkIn>(Pawn);
            yield break;
        }
        Report = "Going up to the room";
        foreach (var s in GoTo(Get<Thing>(bed).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed)
            yield break;
        Lodging.WalkedIn(Sim, Pawn, bed);
    }
}
