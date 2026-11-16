using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Hospitality;

/// <summary>A guest's room at the inn: the bed they've been given, and the last night they've paid for.</summary>
public struct LodgeStay
{
    public Entity Bed;

    /// <summary>The night paid for, by the day it begins on (<see cref="Lodging.Tonight"/>).</summary>
    public int Night;
}

/// <summary>A let bed that's been slept in and not made up again since.</summary>
public struct Unmade;

/// <summary>
/// Rooms let by the night. A guest pays each evening for that night (<see cref="Shops.ShopSystem"/>
/// asks for a room again once the last night they paid for is over), and keeps the same room while
/// they're in town. They check out when they move on, or when a night goes by unpaid. The bed they
/// leave is unmade, and can't be let again till the keeper's made it up (<see cref="MakeBedSession"/>);
/// a guest shown to an unmade bed when there's no other takes it, and says what they think of it.
/// </summary>
public static class Lodging
{
    /// <summary>The hour past which an unpaid night counts as gone (when a guest stops asking for a room).</summary>
    public const float LapsedHour = 2;

    /// <summary>The night it is (or was, before noon): named by the day it began on.</summary>
    public static int Tonight(Simulation sim) => sim.Clock.HourOfDay >= 12 ? sim.Clock.Day : sim.Clock.Day - 1;

    /// <summary>Has this person a room paid for tonight?</summary>
    public static bool Lodged(Simulation sim, Entity who) =>
        sim.World.TryGet<LodgeStay>(who, out var stay) && stay.Night >= Tonight(sim) && sim.World.IsAlive(stay.Bed)
        && sim.World.TryGet<BedOwner>(who, out var own) && own.Bed == stay.Bed;

    /// <summary>A guest is given a bed for tonight (theirs from last night if they still have it).</summary>
    public static void Let(Simulation sim, Entity guest, Entity bed)
    {
        var w = sim.World;
        w.Remove<BedOwner>(guest);
        w.Add(guest, new BedOwner { Bed = bed });
        w.Remove<LodgeStay>(guest);
        w.Add(guest, new LodgeStay { Bed = bed, Night = Tonight(sim) });
    }

    /// <summary>The bed they had last night, if it's still theirs to have again.</summary>
    public static Entity Kept(Simulation sim, Entity guest) =>
        sim.World.TryGet<LodgeStay>(guest, out var stay) && sim.World.IsAlive(stay.Bed)
        && sim.World.TryGet<BedOwner>(guest, out var own) && own.Bed == stay.Bed ? stay.Bed : Entity.Null;

    /// <summary>
    /// Out: the bed's free again, and needs making up. The key goes back to the bar, or is left in
    /// the room, or leaves with them (<see cref="InnRooms.OnCheckOut"/>).
    /// </summary>
    public static void CheckOut(Simulation sim, Entity guest)
    {
        var w = sim.World;
        if (!w.TryGet<LodgeStay>(guest, out var stay))
            return;
        w.Remove<LodgeStay>(guest);
        if (w.TryGet<BedOwner>(guest, out var own) && own.Bed == stay.Bed)
            w.Remove<BedOwner>(guest);
        RoomChores.Vacated(sim, guest, stay.Bed);
        if (w.TryGet<Creature>(guest, out var c) && !c.Dead)
            InnRooms.OnCheckOut(sim, guest, stay.Bed);
    }

    /// <summary>
    /// A guest let in by someone else's key has walked in on them: both put out, the one with the
    /// wrong key owed another room (<see cref="OwedRoom"/>).
    /// </summary>
    public static void WalkedIn(Simulation sim, Entity guest, Entity bed)
    {
        var w = sim.World;
        if (!w.Has<WalkIn>(guest))
            return;
        w.Remove<WalkIn>(guest);
        var there = InnRooms.LodgedIn(sim, InnRooms.SeedOf(sim, bed), guest);
        var inn = Buildings.Of(sim, bed);
        if (there.IsNull)
        {
            // Whoever had it has gone: it's theirs after all.
            Let(sim, guest, bed);
            return;
        }
        if (w.Has<OwedRoom>(guest))
            w.Remove<OwedRoom>(guest);
        w.Add(guest, new OwedRoom { Inn = inn, WrongBed = bed });
        foreach (var who in new[] { guest, there })
            if (w.TryGet<Adventurer>(who, out _))
                w.Get<Adventurer>(who).Satisfaction -= WalkInJob.Mood;
        sim.Speech.Say(sim, there, "Oi! This room's taken. Get out!", Volume.Shout);
        sim.Speech.Say(sim, guest, "Sorry, sorry. That keeper gave me the wrong key.", Volume.Say);
        sim.Messages.Add(MessageKind.Info, $"{w.Get<Creature>(guest).Name} walks in on {w.Get<Creature>(there).Name}: the wrong key.", w.Get<Thing>(bed).Cell);
    }

    public static bool IsUnmade(Simulation sim, Entity bed) => sim.World.IsAlive(bed) && sim.World.Has<Unmade>(bed);
}

/// <summary>Guests check out when they move on, or when a night goes by without their paying for it.</summary>
public sealed class LodgingSystem : ISimSystem
{
    public string Name => "Lodging";
    public int Interval => SimClock.TicksPerHour / 4;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        if (w.Count<LodgeStay>() == 0)
            return;
        var tonight = Lodging.Tonight(sim);
        var hour = sim.Clock.HourOfDay;
        // Small hours to noon: last night's done, and anyone who didn't pay for it has lapsed.
        var lapsing = hour is >= Lodging.LapsedHour and < 12;
        foreach (var e in w.Query<LodgeStay>())
        {
            var stay = w.Get<LodgeStay>(e);
            var gone = !w.TryGet<Creature>(e, out var c) || c.Dead || w.Has<Leaving>(e);
            if (gone || (lapsing && stay.Night < tonight))
                Lodging.CheckOut(sim, e);
        }
    }
}

/// <summary>The steps of making up a bed, in order.</summary>
public enum BedStep : byte
{
    /// <summary>Last night's sheet dragged off: hold F.</summary>
    Strip,

    /// <summary>The clean sheet shaken out: press F as it billows to the top, and it settles flat.</summary>
    Spread,

    /// <summary>Its corners tucked under, left and right in turn: A, D, A, D.</summary>
    Tuck,
}

/// <summary>
/// Making up a slept-in bed, a step each way of working it. Strip: hold F and last night's sheet
/// comes off (let go and it slides back a little). Spread: the clean sheet billows; press F at the
/// top of the billow and it floats down flat, anywhere else and it flops and is shaken out again.
/// Tuck: the corners go under in turn, A for the left and D for the right. Nothing's spoiled by a
/// fumble; it only takes longer.
/// </summary>
public sealed class MakeBedSession : WorkSession
{
    /// <summary>Seconds of holding F to strip it.</summary>
    public const float StripSeconds = 0.9f;

    /// <summary>Seconds a billow takes, up and down.</summary>
    public const float Beat = 0.9f;

    /// <summary>Corners to tuck.</summary>
    public const int Corners = 4;

    /// <summary>Steps a flopped sheet lies before it can be shaken out again.</summary>
    public const int FlopSteps = 20;

    public Entity Bed { get; set; } = Entity.Null;

    public BedStep At { get; private set; }

    /// <summary>How far the old sheet's off, 0..1.</summary>
    public float Stripped { get; private set; }

    /// <summary>Corners tucked so far.</summary>
    public int Tucked { get; private set; }

    /// <summary>Steps since the spread began (the billow's clock), and the step a sheet last flopped (-1: never).</summary>
    public int SpreadSteps { get; private set; }
    public int FloppedAt { get; private set; } = -1;

    /// <summary>The clean sheet's lift as it's shaken, 0 (down) to 1 (billowed full).</summary>
    public float Billow => Triangle(SpreadSteps, Beat);

    /// <summary>A flopped sheet's still lying in a heap.</summary>
    public bool Flopped => FloppedAt >= 0 && StepsDone - FloppedAt < FlopSteps;

    public bool OnCue => At == BedStep.Spread && !Flopped && Billow > 0.8f;

    /// <summary>The side the next corner's on: -1 the left (A), 1 the right (D).</summary>
    public int NextSide => Tucked % 2 == 0 ? -1 : 1;

    /// <summary>Steps through the whole of it, for its sounds: one per step finished and per corner.</summary>
    public int Movements => (int)At + Tucked;

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Bed) && sim.World.TryGet<Thing>(Bed, out var t) ? t.Cell : null;

    public override Skill Skill => Skill.Cooking;
    public override float Xp => 2f;
    public override string Title => "Making the bed";

    public override bool StillValid(Simulation sim) =>
        Lodging.IsUnmade(sim, Bed) && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Bed) && !RoomChores.LinenOn(sim, Worker).IsNull;

    protected override void Press()
    {
        if (At != BedStep.Spread || Flopped)
            return;
        if (Billow > 0.8f)
            At = BedStep.Tuck;
        else
            FloppedAt = StepsDone;
    }

    protected override void Axis(int value)
    {
        if (At != BedStep.Tuck || value == 0 || value != NextSide)
            return;
        if (++Tucked >= Corners)
            Done = true;
    }

    protected override void Step()
    {
        switch (At)
        {
            case BedStep.Strip:
                Stripped = Math.Clamp(Stripped + (Held ? 1f : -0.4f) / (StripSeconds * SimClock.TicksPerSecond), 0f, 1f);
                if (Stripped >= 1f)
                    At = BedStep.Spread;
                break;
            case BedStep.Spread:
                if (!Flopped)
                    SpreadSteps++;
                break;
        }
    }

    /// <summary>
    /// Made up: a clean sheet on, last night's off (into the worker's pack, for the wash), and
    /// whatever was in the sheets in their hands.
    /// </summary>
    public override void Finish(Simulation sim)
    {
        MakeUp(sim, Bed, Worker);
        sim.Messages.Add(MessageKind.Info, "The bed's made up fresh.", sim.World.Get<Thing>(Bed).Cell);
    }

    /// <summary>The bed made up by someone with clean linen on them (an AI keeper's too, without the game).</summary>
    public static void MakeUp(Simulation sim, Entity bed, Entity by)
    {
        var w = sim.World;
        w.Remove<Unmade>(bed);
        RoomChores.Untuck(sim, bed, by);
        if (RoomChores.LinenOn(sim, by) is { IsNull: false } linen)
            sim.World.Destroy(Inventory.TakeFromPerson(sim, by, linen, 1));
        var soiled = sim.CreateItem(sim.Defs.Thing(RoomChores.SoiledDef), 1);
        if (sim.PackOf(by) is { IsNull: false } pack && sim.ContainerRoom(pack, sim.Defs.Thing(RoomChores.SoiledDef), null) > 0)
            sim.Store(pack, soiled);
        else
            sim.PlaceItemNear(soiled, w.Get<Thing>(bed).Cell, byHand: true);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Bed);
        w.Byte((byte)At);
        w.Float(Stripped);
        w.Int(Tucked);
        w.Int(SpreadSteps);
        w.Int(FloppedAt);
    }

    protected override void ReadState(SaveReader r)
    {
        Bed = r.Entity();
        At = (BedStep)r.Byte();
        Stripped = r.Float();
        Tucked = r.Int();
        SpreadSteps = r.Int();
        FloppedAt = r.Int();
    }
}
