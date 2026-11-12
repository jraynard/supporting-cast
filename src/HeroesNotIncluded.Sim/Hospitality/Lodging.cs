using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;
using SupportingCast.Sim.Work;

namespace SupportingCast.Sim.Hospitality;

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

    /// <summary>Out: the bed's free again, and needs making up.</summary>
    public static void CheckOut(Simulation sim, Entity guest)
    {
        var w = sim.World;
        if (!w.TryGet<LodgeStay>(guest, out var stay))
            return;
        w.Remove<LodgeStay>(guest);
        if (w.TryGet<BedOwner>(guest, out var own) && own.Bed == stay.Bed)
            w.Remove<BedOwner>(guest);
        if (w.IsAlive(stay.Bed) && !w.Has<Unmade>(stay.Bed))
            w.Add(stay.Bed, new Unmade());
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

/// <summary>
/// Making up a slept-in bed: strip it, smooth the sheet, tuck it in. Three movements, each pressed
/// as the hands come round to it; a fumble only costs time.
/// </summary>
public sealed class MakeBedSession : WorkSession
{
    public const int Moves = 3;

    /// <summary>Seconds a movement takes.</summary>
    public const float Beat = 0.7f;

    public Entity Bed { get; set; } = Entity.Null;

    public int Movements { get; private set; }

    public float Sweep => Triangle(StepsDone, Beat);

    public bool OnCue => Sweep > 0.78f;

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Bed) && sim.World.TryGet<Thing>(Bed, out var t) ? t.Cell : null;

    public override Skill Skill => Skill.Cooking;
    public override float Xp => 2f;
    public override string Title => "Making the bed";

    public override bool StillValid(Simulation sim) =>
        Lodging.IsUnmade(sim, Bed) && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Bed);

    protected override void Press()
    {
        if (!OnCue)
            return;
        Movements++;
        if (Movements >= Moves)
            Done = true;
    }

    protected override void Step() { }

    public override void Finish(Simulation sim)
    {
        sim.World.Remove<Unmade>(Bed);
        sim.Messages.Add(MessageKind.Info, "The bed's made up fresh.", sim.World.Get<Thing>(Bed).Cell);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Bed);
        w.Int(Movements);
    }

    protected override void ReadState(SaveReader r)
    {
        Bed = r.Entity();
        Movements = r.Int();
    }
}
