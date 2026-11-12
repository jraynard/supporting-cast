using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tinkering;

/// <summary>
/// The cut of a lock: how many pins and at what heights (from <see cref="Seed"/>, as
/// <see cref="Lockwork.PinHeights"/>), and how tight its tolerances are, which is what makes it hard
/// to pick. On a lock not yet fitted, and moved onto the door it's fitted to.
/// </summary>
public struct LockCut
{
    public uint Seed;
    public int Pins;

    /// <summary>0..1: how crisply its pins were set. A tight lock gives a pick less room to set a pin.</summary>
    public float Tightness;

    public Entity Maker;
}

/// <summary>A key, cut to turn locks of its <see cref="Seed"/>. <see cref="Fit"/> 0..1 is how well it was cut: a poor one sticks.</summary>
public struct Key
{
    public uint Seed;

    /// <summary>The bits along it, one for each of its lock's pins: what a copy is filed to.</summary>
    public int Pins;

    public float Fit;
}

/// <summary>
/// Fitting locks and cutting keys (docs/DESIGN.md, "Tinker"). A lock is made at the tinker's bench
/// by filing its pins (<see cref="PinSession"/>), and comes with its first key; it's then fitted to
/// a door, which can be locked from then on by whoever holds a key cut for it (or picked by whoever
/// can't). Keys are copied at the bench from a key you have. AI villagers never lock a door, so
/// nobody's way about town changes unless a player decides it should.
/// </summary>
public static class Locksmithing
{
    public const string LockDef = "Lock", KeyDef = "Key";

    /// <summary>A key below this fit sometimes sticks in the lock, the more so the worse it's cut.</summary>
    public const float GoodFit = 0.85f;

    /// <summary>The share of the pick's room a perfectly tight lock takes away (<see cref="PickSession"/>).</summary>
    public const float TightnessSqueeze = 0.5f;

    public static bool IsDoor(Simulation sim, Entity e) =>
        sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Spawned && t.Def.Building is { IsDoor: true }
        && !sim.World.Has<Construction>(e);

    /// <summary>A door with a tinker's lock fitted to it.</summary>
    public static bool HasLock(Simulation sim, Entity door) => IsDoor(sim, door) && sim.World.Has<LockCut>(door);

    /// <summary>A lock item, not yet fitted (a fitted one is part of its door).</summary>
    public static bool IsLock(Simulation sim, Entity e) =>
        sim.World.IsAlive(e) && sim.World.Has<LockCut>(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Name == LockDef;

    public static bool IsKey(Simulation sim, Entity e) => sim.World.IsAlive(e) && sim.World.Has<Key>(e);

    /// <summary>Pins a maker cuts into a lock: more as they get better at it.</summary>
    public static int PinsFor(int level) => Math.Clamp(Lockwork.MinPins + level / 3, Lockwork.MinPins, Lockwork.MaxPins);

    /// <summary>A new key for a lock of <paramref name="seed"/>, unspawned.</summary>
    public static Entity MakeKey(Simulation sim, uint seed, int pins, float fit)
    {
        var key = sim.CreateItem(sim.Defs.Thing(KeyDef), 1);
        sim.World.Add(key, new Key { Seed = seed, Pins = Math.Clamp(pins, Lockwork.MinPins, Lockwork.MaxPins), Fit = Math.Clamp(fit, 0f, 1f) });
        return key;
    }

    /// <summary>Cut a lock into a lock item just made (by hand or by an AI's bill), and its first key.</summary>
    public static Entity CutLock(Simulation sim, Entity item, uint seed, int pins, float tightness, float fit, Entity maker)
    {
        var w = sim.World;
        var cut = new LockCut { Seed = seed | 1u, Pins = Math.Clamp(pins, Lockwork.MinPins, Lockwork.MaxPins), Tightness = Math.Clamp(tightness, 0f, 1f), Maker = maker };
        if (w.Has<LockCut>(item))
            w.Get<LockCut>(item) = cut;
        else
            w.Add(item, cut);
        return MakeKey(sim, cut.Seed, cut.Pins, fit);
    }

    /// <summary>Every key on someone (in hand, on the belt, in the pack), in a fixed order.</summary>
    public static List<Entity> Keys(Simulation sim, Entity who)
    {
        var w = sim.World;
        var list = new List<Entity>();
        if (w.TryGet<Carrying>(who, out var c) && IsKey(sim, c.Item))
            list.Add(c.Item);
        if (sim.PackOf(who) is { IsNull: false } pack)
            foreach (var item in sim.StoredIn(pack))
                if (IsKey(sim, item))
                    list.Add(item);
        return list;
    }

    /// <summary>The best-cut key on someone for this lock; Entity.Null if they've none that fits.</summary>
    public static Entity KeyFor(Simulation sim, Entity who, uint seed)
    {
        var best = Entity.Null;
        var bestFit = -1f;
        foreach (var k in Keys(sim, who))
        {
            var key = sim.World.Get<Key>(k);
            if (key.Seed == seed && key.Fit > bestFit)
                (best, bestFit) = (k, key.Fit);
        }
        return best;
    }

    // ---------------- Keys in AI hands ----------------

    /// <summary>Ticks an AI villager takes to unlock a door with their key.</summary>
    public const int UnlockTicks = 40;

    /// <summary>A key to every lock fitted at a trade's workplace (<see cref="TradeDef.HoldsKeys"/>), for whoever takes it up.</summary>
    public static void GiveWorkplaceKeys(Simulation sim, Entity who, TradeDef trade)
    {
        var w = sim.World;
        if (trade.WorkplaceDef is not { } workplace)
            return;
        foreach (var door in w.Query<LockCut>().OrderBy(e => e.Index))
        {
            if (!IsDoor(sim, door) || Buildings.Of(sim, door) is not { IsNull: false } b || w.Get<PresetBuilding>(b).Def != workplace)
                continue;
            var cut = w.Get<LockCut>(door);
            if (KeyFor(sim, who, cut.Seed).IsNull)
                Inventory.Stow(sim, who, MakeKey(sim, cut.Seed, cut.Pins, 1f));
        }
    }

    /// <summary>
    /// For an AI villager who can't otherwise get from <paramref name="from"/> to <paramref name="to"/>:
    /// a locked door they hold the key to, that they can reach and that opens the way. Entity.Null if
    /// there's none (or it's a player's character: players turn their own keys).
    /// </summary>
    public static Entity KeyedDoor(Simulation sim, Entity pawn, int3 from, int3 to, PathEnd end)
    {
        var w = sim.World;
        if (w.Count<LockCut>() == 0 || w.Has<Avatar>(pawn) || w.Has<PlayerCharacter>(pawn))
            return Entity.Null;
        var keys = Keys(sim, pawn);
        if (keys.Count == 0)
            return Entity.Null;
        var map = sim.Map;
        foreach (var door in w.Query<LockCut>().OrderBy(e => e.Index))
        {
            if (!IsDoor(sim, door) || !sim.IsLocked(door) || !keys.Exists(k => w.Get<Key>(k).Seed == w.Get<LockCut>(door).Seed))
                continue;
            var cell = w.Get<Thing>(door).Cell;
            bool near = false, far = false;
            foreach (var d in Map.Neighbors8)
            {
                var n = new int3(cell.X + d.X, cell.Y + d.Y, cell.Z);
                if (!map.InBounds(n) || !map.Passable(n))
                    continue;
                near |= sim.Regions.CanReach(from, n, PathEnd.OnTile);
                far |= sim.Regions.CanReach(n, to, end);
            }
            if (near && far)
                return door;
        }
        return Entity.Null;
    }

    /// <summary>An AI villager unlocks a door with their key to get through; it's locked again behind them (<see cref="Relock"/>).</summary>
    public static bool LetThrough(Simulation sim, Entity pawn, Entity door)
    {
        var w = sim.World;
        if (!HasLock(sim, door) || KeyFor(sim, pawn, w.Get<LockCut>(door).Seed).IsNull)
            return false;
        if (!sim.IsLocked(door))
            return true;
        sim.SetLocked(door, false);
        if (w.Has<Relock>(door))
            w.Get<Relock>(door).By = pawn;
        else
            w.Add(door, new Relock { By = pawn });
        sim.RebuildDerivedIfNeeded();
        return true;
    }

    /// <summary>Why this person can't fit this lock to this door now, or null if they can.</summary>
    public static string? FitProblem(Simulation sim, Entity who, Entity door, Entity lockItem)
    {
        var w = sim.World;
        if (!IsDoor(sim, door))
            return "that isn't a door";
        if (!AvatarSystem.InReach(sim, w.Get<Position>(who).Cell, door))
            return "you need to be at the door";
        if (w.Has<LockCut>(door))
            return "it already has a lock";
        if (!w.IsAlive(lockItem) || !w.Has<LockCut>(lockItem) || !Inventory.OnPerson(sim, who, lockItem))
            return "you need the lock in hand or in your pack";
        if (sim.IsLocked(door))
            return "it's locked";
        return null;
    }

    /// <summary>Fit a lock to a door: it's part of the door from now on, and locks with the keys cut for it.</summary>
    public static bool Fit(Simulation sim, Entity who, Entity door, Entity lockItem)
    {
        if (FitProblem(sim, who, door, lockItem) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, $"Can't fit that: {why}.", sim.LocationOf(who));
            return false;
        }
        var w = sim.World;
        var cut = w.Get<LockCut>(lockItem);
        var taken = Inventory.TakeFromPerson(sim, who, lockItem, 1);
        sim.DestroyThing(taken);
        w.Add(door, cut);
        SkillRules.Learn(sim, who, Skill.Crafting, 20f);
        sim.Messages.Add(MessageKind.Info, $"{w.Get<Creature>(who).Name} fits a lock to the {w.Get<Thing>(door).Label}.", sim.LocationOf(who));
        return true;
    }

    /// <summary>
    /// Turn a key in a door's lock, to lock it or unlock it. A badly cut key may stick: nothing
    /// happens, try again. False if it didn't turn (or there's no key for it on them).
    /// </summary>
    public static bool Turn(Simulation sim, Entity who, Entity door, bool lockIt)
    {
        var w = sim.World;
        var at = sim.LocationOf(who);
        if (!HasLock(sim, door) || !AvatarSystem.InReach(sim, w.Get<Position>(who).Cell, door) || sim.IsLocked(door) == lockIt)
            return false;
        var key = KeyFor(sim, who, w.Get<LockCut>(door).Seed);
        if (key.IsNull)
        {
            sim.Messages.Add(MessageKind.Info, "None of your keys fits this lock.", at);
            return false;
        }
        var fit = w.Get<Key>(key).Fit;
        if (fit < GoodFit && sim.Rand.Chance((GoodFit - fit) * 1.2f))
        {
            sim.Messages.Add(MessageKind.Info, "The key sticks in the lock. Try it again.", at);
            return false;
        }
        // Someone standing in the doorway can't be locked in or out through it.
        if (lockIt && sim.Creatures().Any(c => w.Get<Position>(c).Cell == w.Get<Thing>(door).Cell))
        {
            sim.Messages.Add(MessageKind.Info, "Someone's in the doorway.", at);
            return false;
        }
        sim.SetLocked(door, lockIt);
        sim.Messages.Add(MessageKind.Info, $"{w.Get<Creature>(who).Name} {(lockIt ? "locks" : "unlocks")} the {w.Get<Thing>(door).Label}.", at);
        return true;
    }
}

/// <summary>A door an AI villager unlocked with their key to get through: locked again once they're clear of it (<see cref="RelockSystem"/>).</summary>
public struct Relock
{
    public Entity By;
}

/// <summary>
/// Doors an AI villager let themselves through: locked again behind them once they're two steps
/// clear and nobody's in the doorway (or they're gone). A door a player unlocks stays as they left it.
/// </summary>
public sealed class RelockSystem : ISimSystem
{
    public string Name => "Relock";
    public int Interval => 20;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        if (w.Count<Relock>() == 0)
            return;
        foreach (var door in w.Query<Relock>())
        {
            var by = w.Get<Relock>(door).By;
            if (!Locksmithing.HasLock(sim, door) || sim.IsLocked(door))
            {
                w.Remove<Relock>(door);
                continue;
            }
            var cell = w.Get<Thing>(door).Cell;
            var clear = !w.IsAlive(by) || !w.TryGet<Position>(by, out var p) || w.Get<Creature>(by).Dead || Map.Distance(p.Cell, cell) >= 2;
            if (!clear || sim.Creatures().Any(c => w.Get<Position>(c).Cell == cell))
                continue;
            sim.SetLocked(door, true);
            w.Remove<Relock>(door);
        }
    }
}

/// <summary>Fit a lock you carry to a door in reach (<see cref="Locksmithing.Fit"/>).</summary>
public sealed record FitLockCommand(Entity Avatar, Entity Door, Entity Lock) : ISimCommand, Core.IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (Core.InventoryCommand.Ready(sim, Avatar) && Core.InventoryCommand.IsItem(sim, Lock) && Locksmithing.IsDoor(sim, Door))
            Locksmithing.Fit(sim, Avatar, Door, Lock);
    }
}

/// <summary>Lock or unlock a door in reach with a key you carry (<see cref="Locksmithing.Turn"/>).</summary>
public sealed record TurnKeyCommand(Entity Avatar, Entity Door, bool Lock) : ISimCommand, Core.IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (Core.InventoryCommand.Ready(sim, Avatar) && Locksmithing.HasLock(sim, Door))
            Locksmithing.Turn(sim, Avatar, Door, Lock);
    }
}
