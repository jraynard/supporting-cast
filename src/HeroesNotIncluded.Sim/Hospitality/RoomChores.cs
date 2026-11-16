using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Hospitality;

/// <summary>A chamber pot that's been used: empty it into a bucket before the room's let again.</summary>
public struct Slops;

/// <summary>A washstand whose jug's been used: fill it from a bucket of water before the room's let again.</summary>
public struct JugEmpty;

/// <summary>Something a guest left in their room: whose it was, by name too, for once they've gone.</summary>
public struct LeftBy
{
    public Entity Owner;
    public string Name;
}

/// <summary>
/// Something a guest left tucked in the bed (under the pillow, in the sheets): nobody knows it's
/// there till the bed's stripped. The item is off the map and in no container meanwhile.
/// </summary>
public struct Tucked
{
    public Entity Item;
}

/// <summary>
/// Turning a room round after a guest's gone (docs/DESIGN.md, Innkeeper, "Rooms"): the bed
/// stripped and made up with clean linen (<see cref="MakeBedSession"/>), the chamber pot emptied
/// into a bucket (<see cref="SlopSession"/>) and tipped at the midden, the jug filled from a
/// bucket of water, and anything they left picked up. A room that isn't ready can still be let;
/// its guest thinks the less of the place.
/// </summary>
public static class RoomChores
{
    public const string LinenDef = "Linen", SoiledDef = "SoiledLinen", SlopDef = "SlopBucket", PotDef = "ChamberPot", StandDef = "Washstand",
        MiddenDef = "Midden", LostPropertyDef = "LostProperty";

    /// <summary>Of a guest checking out: the chance they leave something behind, and that it's in the bed rather than on the floor.</summary>
    public const float ForgetChance = 0.25f, TuckedShare = 0.5f;

    /// <summary>Satisfaction lost by a guest given a room with the last one's slops in it, or no water in the jug.</summary>
    public const float SlopsMood = 0.75f, DryJugMood = 0.25f;

    /// <summary>Satisfaction for having something you left handed back (it's carried into the town's renown as they leave).</summary>
    public const float ReturnedMood = 1.5f;

    /// <summary>The fixtures of a kind in a let bed's room: its chamber pot, its washstand.</summary>
    public static Entity FixtureOf(Simulation sim, Entity bed, string def)
    {
        var w = sim.World;
        if (!w.IsAlive(bed) || Buildings.Of(sim, bed) is not { IsNull: false } inn)
            return Entity.Null;
        var room = sim.Rooms.RoomAt(w.Get<Thing>(bed).Cell);
        if (room == null)
            return Entity.Null;
        foreach (var part in Buildings.Parts(sim, inn))
            if (w.Get<Thing>(part).Def.Name == def && sim.Rooms.RoomAt(w.Get<Thing>(part).Cell) == room)
                return part;
        return Entity.Null;
    }

    /// <summary>The let bed whose room a fixture (or anything) is in, or Entity.Null.</summary>
    public static Entity BedIn(Simulation sim, int3 cell)
    {
        var room = sim.Rooms.RoomAt(cell);
        if (room == null)
            return Entity.Null;
        foreach (var e in sim.World.Query<GuestRoom>().OrderBy(e => e.Index))
            if (sim.Rooms.RoomAt(sim.World.Get<Thing>(e).Cell) == room)
                return e;
        return Entity.Null;
    }

    /// <summary>A guest's gone: the room wants turning round. What they left is there to be found.</summary>
    public static void Vacated(Simulation sim, Entity guest, Entity bed)
    {
        var w = sim.World;
        if (!w.IsAlive(bed))
            return;
        if (!w.Has<Unmade>(bed))
            w.Add(bed, new Unmade());
        if (FixtureOf(sim, bed, PotDef) is { IsNull: false } pot && !w.Has<Slops>(pot))
            w.Add(pot, new Slops());
        if (FixtureOf(sim, bed, StandDef) is { IsNull: false } stand && !w.Has<JugEmpty>(stand))
            w.Add(stand, new JugEmpty());
        if (w.TryGet<Creature>(guest, out var c) && !c.Dead && sim.Rand.NextFloat() < ForgetChance)
            LeaveSomething(sim, guest, c.Name, bed);
    }

    /// <summary>A loose thing from their pack (not their silver, keys or anything entrusted), left on the floor or tucked in the bed.</summary>
    private static void LeaveSomething(Simulation sim, Entity guest, string name, Entity bed)
    {
        var w = sim.World;
        var pack = sim.PackOf(guest);
        var things = pack.IsNull ? [] : sim.StoredIn(pack).Where(e => !Tinkering.Locksmithing.IsKey(sim, e) && !w.Has<Entrusted>(e)
            && w.Get<Thing>(e).Def.Name != Shops.Coin.Silver && !w.Has<Strongbox>(e)).ToList();
        if (things.Count == 0)
            return;
        var left = Inventory.TakeFromPerson(sim, guest, things[sim.Rand.NextInt(things.Count)], 1);
        if (left.IsNull)
            return;
        w.Add(left, new LeftBy { Owner = guest, Name = name });
        if (sim.Rand.NextFloat() < TuckedShare && !w.Has<Tucked>(bed))
            w.Add(bed, new Tucked { Item = left });
        else
            sim.PlaceItemNear(left, w.Get<Thing>(bed).Cell, byHand: true);
    }

    /// <summary>Stripping the bed: anything tucked in it comes out into the stripper's hands (or at their feet).</summary>
    public static Entity Untuck(Simulation sim, Entity bed, Entity by)
    {
        var w = sim.World;
        if (!w.TryGet<Tucked>(bed, out var t))
            return Entity.Null;
        w.Remove<Tucked>(bed);
        if (!w.IsAlive(t.Item))
            return Entity.Null;
        Inventory.Stow(sim, by, t.Item);
        sim.Messages.Tell(by, MessageKind.Info, $"Something in the sheets: {Label(sim, t.Item)}.", w.Get<Thing>(bed).Cell);
        return t.Item;
    }

    /// <summary>"Gorran's whetstone" for something a guest left behind; null otherwise.</summary>
    public static string? Label(Simulation sim, Entity item) =>
        sim.World.IsAlive(item) && sim.World.TryGet<LeftBy>(item, out var l) ? $"{l.Name}'s {sim.World.Get<Thing>(item).Label}" : null;

    /// <summary>Is the room this bed's in ready for a guest: made up, the pot empty, water in the jug?</summary>
    public static bool Ready(Simulation sim, Entity bed)
    {
        var w = sim.World;
        if (w.Has<Unmade>(bed))
            return false;
        var pot = FixtureOf(sim, bed, PotDef);
        var stand = FixtureOf(sim, bed, StandDef);
        return (pot.IsNull || !w.Has<Slops>(pot)) && (stand.IsNull || !w.Has<JugEmpty>(stand));
    }

    /// <summary>Shown to a room that isn't ready: what they say, and what it costs.</summary>
    public static void Grumble(Simulation sim, Entity guest, Entity bed)
    {
        var w = sim.World;
        var mood = 0f;
        string? said = null;
        if (FixtureOf(sim, bed, PotDef) is { IsNull: false } pot && w.Has<Slops>(pot))
        {
            mood += SlopsMood;
            said = "And somebody's left the pot full. Lovely.";
        }
        if (FixtureOf(sim, bed, StandDef) is { IsNull: false } stand && w.Has<JugEmpty>(stand))
        {
            mood += DryJugMood;
            said ??= "No water in the jug, either.";
        }
        if (mood <= 0)
            return;
        if (w.Has<Adventurer>(guest))
            w.Get<Adventurer>(guest).Satisfaction -= mood;
        if (said != null)
            sim.Speech.Say(sim, guest, said, Volume.Say);
    }

    /// <summary>Something they left, handed back to them: thanks, and they think the better of an honest house.</summary>
    public static bool HandedBack(Simulation sim, Entity giver, Entity to, Entity item)
    {
        var w = sim.World;
        if (!w.TryGet<LeftBy>(item, out var l) || l.Owner != to)
            return false;
        w.Remove<LeftBy>(item);
        Inventory.Stow(sim, to, item);
        if (w.Has<Adventurer>(to))
            w.Get<Adventurer>(to).Satisfaction += ReturnedMood;
        sim.Speech.Say(sim, to, $"My {w.Get<Thing>(item).Label}! I'd given it up. That's honest of you.", Volume.Say);
        return true;
    }

    // ---------------- At the fixtures (the work key: BarWork) ----------------

    /// <summary>An empty bucket on someone, hand first.</summary>
    public static Entity EmptyBucketOn(Simulation sim, Entity who) => AvatarSystem.Supply(sim, who, d => d.Name == Environment.Buckets.Empty);

    /// <summary>A bucket of water on someone, hand first.</summary>
    public static Entity WaterOn(Simulation sim, Entity who) => AvatarSystem.Supply(sim, who, d => d.Name == Environment.Buckets.Full);

    /// <summary>A bucket of slops on someone, hand first.</summary>
    public static Entity SlopsOn(Simulation sim, Entity who) => AvatarSystem.Supply(sim, who, d => d.Name == SlopDef);

    /// <summary>Soiled linen on someone, hand first.</summary>
    public static Entity SoiledOn(Simulation sim, Entity who) => AvatarSystem.Supply(sim, who, d => d.Name == SoiledDef);

    /// <summary>Clean linen on someone, hand first.</summary>
    public static Entity LinenOn(Simulation sim, Entity who) => AvatarSystem.Supply(sim, who, d => d.Name == LinenDef);

    /// <summary>What the work key does at a pot, washstand or midden for this person; null if nothing.</summary>
    public static string? StepAt(Simulation sim, Entity who, Entity station)
    {
        var w = sim.World;
        var def = w.Get<Thing>(station).Def.Name;
        if (def == PotDef && w.Has<Slops>(station))
            return EmptyBucketOn(sim, who).IsNull ? "The pot wants emptying: bring an empty bucket" : "Empty the pot into the bucket";
        if (def == StandDef && w.Has<JugEmpty>(station))
            return WaterOn(sim, who).IsNull ? "The jug's empty: bring a bucket of water" : "Fill the jug";
        if (def == MiddenDef && !SlopsOn(sim, who).IsNull)
            return "Tip the slops";
        return null;
    }

    /// <summary>
    /// Does the work key at this thing want the bucket of water in hand (an empty jug's washstand),
    /// rather than throwing it? Throwing is checked first, so it'd always win at a washstand.
    /// </summary>
    public static bool TakesWater(Simulation sim, Entity station) =>
        !station.IsNull && sim.World.IsAlive(station) && sim.World.TryGet<Thing>(station, out var t) && t.Def.Name == StandDef && sim.World.Has<JugEmpty>(station);

    /// <summary>The work key at a pot, washstand or midden. True if it was one of them.</summary>
    public static bool Start(Simulation sim, Entity who, Entity station)
    {
        var w = sim.World;
        var def = w.Get<Thing>(station).Def.Name;
        var at = w.Get<Thing>(station).Cell;
        if (def == PotDef && w.Has<Slops>(station))
        {
            if (EmptyBucketOn(sim, who).IsNull)
                sim.Messages.Tell(who, MessageKind.Info, "You'll want an empty bucket for that. There's one in the storeroom, and more at the well.", at);
            else
                WorkSystem.Start(sim, who, new SlopSession { Pot = station });
            return true;
        }
        if (def == StandDef && w.Has<JugEmpty>(station))
        {
            var water = WaterOn(sim, who);
            if (water.IsNull)
            {
                sim.Messages.Tell(who, MessageKind.Info, "Fill a bucket at the well, then fill the jug from it.", at);
                return true;
            }
            SwapBucket(sim, who, water, Environment.Buckets.Empty);
            w.Remove<JugEmpty>(station);
            sim.Messages.Tell(who, MessageKind.Info, "The jug's full again.", at);
            return true;
        }
        if (def == MiddenDef && SlopsOn(sim, who) is { IsNull: false } slops)
        {
            SwapBucket(sim, who, slops, Environment.Buckets.Empty);
            sim.Messages.Tell(who, MessageKind.Info, "Tipped. The bucket's empty again: keep it for the next pot, or fill it at the well for a jug.", at);
            return true;
        }
        return false;
    }

    /// <summary>One kind of bucket becomes another in the same place (hand or pack).</summary>
    public static void SwapBucket(Simulation sim, Entity who, Entity bucket, string into)
    {
        var w = sim.World;
        var inHand = w.Get<Carrying>(who).Item == bucket;
        Inventory.TakeFromPerson(sim, who, bucket, 1);
        sim.World.Destroy(bucket);
        var made = sim.CreateItem(sim.Defs.Thing(into), 1);
        if (inHand && w.Get<Carrying>(who).Item.IsNull)
            w.Get<Carrying>(who).Item = made;
        else
            Inventory.Stow(sim, who, made);
    }
}

/// <summary>
/// Emptying a chamber pot into a bucket: tip it as the pour comes round, steady. Tipped off the
/// beat, some goes on the floor (a mess to mop), and you try again.
/// </summary>
public sealed class SlopSession : WorkSession
{
    public const int Pours = 2;

    /// <summary>Seconds a sway takes.</summary>
    public const float Beat = 0.8f;

    public Entity Pot { get; set; } = Entity.Null;

    public int Poured { get; private set; }
    public int Spills { get; private set; }

    public float Sweep => Triangle(StepsDone, Beat);

    public bool OnCue => Sweep > 0.72f;

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Pot) && sim.World.TryGet<Thing>(Pot, out var t) ? t.Cell : null;

    public override Skill Skill => Skill.Cooking;
    public override float Xp => 1f;
    public override string Title => "Emptying the pot";

    public override bool StillValid(Simulation sim) =>
        sim.World.IsAlive(Pot) && sim.World.Has<Slops>(Pot) && !RoomChores.EmptyBucketOn(sim, Worker).IsNull
        && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Pot);

    protected override void Press()
    {
        if (!OnCue)
        {
            Spills++;
            return;
        }
        Poured++;
        if (Poured >= Pours)
            Done = true;
    }

    protected override void Step() { }

    /// <summary>Spills land on the floor as the session's settled, so a miss shows at once.</summary>
    private int _spilled;

    public override void Settle(Simulation sim)
    {
        for (; _spilled < Spills; _spilled++)
            sim.Mess.Spill(sim, MessKind.Mud, sim.World.Get<Thing>(Pot).Cell, 0.4f);
    }

    public override void Finish(Simulation sim)
    {
        var w = sim.World;
        Settle(sim);
        w.Remove<Slops>(Pot);
        if (RoomChores.EmptyBucketOn(sim, Worker) is { IsNull: false } bucket)
            RoomChores.SwapBucket(sim, Worker, bucket, RoomChores.SlopDef);
        sim.Messages.Tell(Worker, MessageKind.Info, "Emptied into the bucket. Carry it out to the midden behind the inn and tip it there (F).", w.Get<Thing>(Pot).Cell);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Pot);
        w.Int(Poured);
        w.Int(Spills);
        w.Int(_spilled);
    }

    protected override void ReadState(SaveReader r)
    {
        Pot = r.Entity();
        Poured = r.Int();
        Spills = r.Int();
        _spilled = r.Int();
    }
}
