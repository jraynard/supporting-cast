using HeroesNotIncluded.Sim.Components;
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
