using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tinkering;

/// <summary>What's wrong with a party's wagon after the road.</summary>
[Flags]
public enum WagonDamage : byte
{
    None = 0,

    /// <summary>A wheel knocked out of true: trued spoke by spoke (<see cref="TrueSession"/>).</summary>
    Wheel = 1,

    /// <summary>The axle cracked: a new one fitted (<see cref="FitSession"/>), from wood.</summary>
    Axle = 2,
}

/// <summary>A party's wagon, parked in town while they're here (<see cref="Wagons"/>).</summary>
public struct Wagon
{
    public int Party;

    /// <summary>Whose it is: the one who asks to have it mended.</summary>
    public Entity Owner;

    public WagonDamage Damage;

    /// <summary>How its wheel's buckled (<see cref="TrueSession"/>): the same each time it's looked at, until it's trued.</summary>
    public uint WheelSeed;

    /// <summary>Who last mended something on it, to be paid for the order.</summary>
    public Entity MendedBy;
}

/// <summary>
/// Party wagons (docs/DESIGN.md, "Tinker"). Some parties ride in with a wagon, and the road has
/// often had its way with it: a wheel out of true, an axle cracked. It's parked near their haunt,
/// and the owner asks for it mended. A wagon still broken when they set out on a quest carries
/// nothing home: no strongbox, no curios, and a party in a worse temper. Mended where it stands,
/// by whoever takes the order on; it's paid for once nothing's wrong with it.
/// </summary>
public static class Wagons
{
    public const string Def = "PartyWagon";

    /// <summary>The share of wagons that come in the worse for the road.</summary>
    public const float DamagedChance = 0.8f;

    /// <summary>Wood a new axle takes.</summary>
    public const int AxleWood = 2;

    /// <summary>Satisfaction lost by each of a party setting out with their wagon still broken.</summary>
    public const float BrokenMood = 1f;

    public static bool IsWagon(Simulation sim, Entity e) => sim.World.IsAlive(e) && sim.World.Has<Wagon>(e);

    public static bool Damaged(Simulation sim, Entity wagon) => IsWagon(sim, wagon) && sim.World.Get<Wagon>(wagon).Damage != WagonDamage.None;

    /// <summary>A party's wagon in town; Entity.Null if they have none.</summary>
    public static Entity Of(Simulation sim, int party)
    {
        foreach (var e in sim.World.Each<Wagon>())
            if (sim.World.Get<Wagon>(e).Party == party)
                return e;
        return Entity.Null;
    }

    public static string DamageLabel(WagonDamage d) => d switch
    {
        WagonDamage.Wheel => "a buckled wheel",
        WagonDamage.Axle => "a cracked axle",
        WagonDamage.Wheel | WagonDamage.Axle => "a buckled wheel and a cracked axle",
        _ => "nothing wrong with it",
    };

    /// <summary>
    /// A party arriving may bring a wagon, parked outside near where they're headed. Rolls from a
    /// stream of its own, seeded from <paramref name="seed"/> and the party, so a wagon or none
    /// doesn't reshuffle anything else about their arrival.
    /// </summary>
    public static Entity Bring(Simulation sim, ulong seed, PartyDef def, int party, Entity owner, int3 toward)
    {
        var rand = new SimRandom(seed ^ ((ulong)(uint)party * 0x9E37_79B9u) ^ 0xA6A6_0Eu);
        if (!rand.Chance(def.WagonChance) || ParkingNear(sim, toward) is not { } spot)
            return Entity.Null;
        var damage = WagonDamage.None;
        if (rand.Chance(DamagedChance))
        {
            var roll = rand.NextFloat();
            damage = roll < 0.55f ? WagonDamage.Wheel : roll < 0.85f ? WagonDamage.Axle : WagonDamage.Wheel | WagonDamage.Axle;
        }
        var wagon = sim.SpawnStructure(sim.Defs.Thing(Def), spot);
        sim.World.Add(wagon, new Wagon { Party = party, Owner = owner, Damage = damage, WheelSeed = rand.NextUInt() | 1u, MendedBy = Entity.Null });
        return wagon;
    }

    /// <summary>
    /// Where a wagon (3 cells by 2) can stand outdoors, a few steps from <paramref name="toward"/>:
    /// every cell of it free, and a walkable ring all round it, so it never closes a lane.
    /// </summary>
    private static int3? ParkingNear(Simulation sim, int3 toward)
    {
        var map = sim.Map;
        var def = sim.Defs.Thing(Def);
        bool Open(int3 c) => map.InBounds(c) && map.Structure[map.Idx(c)].IsNull && map.Item[map.Idx(c)].IsNull && map.Passable(c)
            && Buildings.At(sim, c).IsNull && Buildings.SiteAt(sim, c) is not { IsNull: false } && !map.Roofed[map.Idx(c)];
        foreach (var c in sim.SpiralFrom(toward, 16))
        {
            if (Map.Distance(c, toward) < 5 || !sim.CanPlaceStructure(c, def, 0))
                continue;
            var cells = sim.FootprintCells(def, c, 0).ToList();
            if (!cells.All(Open) || !sim.Regions.CanReach(c, toward, PathEnd.Touch))
                continue;
            var ring = true;
            foreach (var f in cells)
                foreach (var d in Map.Neighbors8)
                {
                    var n = new int3(f.X + d.X, f.Y + d.Y, f.Z);
                    if (!cells.Contains(n) && !Open(n))
                        ring = false;
                }
            if (ring)
                return c;
        }
        return null;
    }

    /// <summary>The party's gone for good: they hitch up and take the wagon with them.</summary>
    public static void Leave(Simulation sim, int party)
    {
        var wagon = Of(sim, party);
        if (!wagon.IsNull)
            sim.DestroyThing(wagon);
    }

    // ---------------- Mending it ----------------

    /// <summary>Why this person can't true this wagon's wheel now; null if they can.</summary>
    public static string? WheelProblem(Simulation sim, Entity worker, Entity wagon)
    {
        if (!IsWagon(sim, wagon) || (sim.World.Get<Wagon>(wagon).Damage & WagonDamage.Wheel) == 0)
            return "its wheels run true";
        return InReach(sim, worker, wagon) ? null : "you need to be at the wagon";
    }

    /// <summary>Why this person can't fit a new axle to this wagon now; null if they can.</summary>
    public static string? AxleProblem(Simulation sim, Entity worker, Entity wagon)
    {
        if (!IsWagon(sim, wagon) || (sim.World.Get<Wagon>(wagon).Damage & WagonDamage.Axle) == 0)
            return "its axle is sound";
        if (!InReach(sim, worker, wagon))
            return "you need to be at the wagon";
        return WoodOn(sim, worker) < AxleWood ? $"a new axle takes {AxleWood} wood" : null;
    }

    private static bool InReach(Simulation sim, Entity worker, Entity wagon) =>
        AvatarSystem.InReach(sim, sim.World.Get<Position>(worker).Cell, wagon);

    private static bool IsWood(Simulation sim, Entity e) => sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Name == "Wood";

    /// <summary>Wood someone has on them (hands and pack).</summary>
    public static int WoodOn(Simulation sim, Entity who)
    {
        var w = sim.World;
        var n = 0;
        var held = w.Get<Carrying>(who).Item;
        if (IsWood(sim, held))
            n += w.Get<Stack>(held).Count;
        if (sim.PackOf(who) is { IsNull: false } pack)
            foreach (var item in sim.StoredIn(pack))
                if (IsWood(sim, item))
                    n += w.Get<Stack>(item).Count;
        return n;
    }

    internal static void UseWood(Simulation sim, Entity who, int count)
    {
        var w = sim.World;
        var sources = new List<Entity>();
        var held = w.Get<Carrying>(who).Item;
        if (IsWood(sim, held))
            sources.Add(held);
        if (sim.PackOf(who) is { IsNull: false } pack)
            sources.AddRange(sim.StoredIn(pack).Where(e => IsWood(sim, e)));
        foreach (var s in sources)
        {
            if (count <= 0)
                return;
            var take = Math.Min(count, w.Get<Stack>(s).Count);
            sim.DestroyThing(Inventory.TakeFromPerson(sim, who, s, take));
            count -= take;
        }
    }

    public static void StartWheel(Simulation sim, Entity worker, Entity wagon)
    {
        if (WheelProblem(sim, worker, wagon) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, $"Can't true it: {why}.", sim.LocationOf(worker));
            return;
        }
        WorkSystem.Start(sim, worker, new TrueSession { Wagon = wagon, Seed = sim.World.Get<Wagon>(wagon).WheelSeed });
    }

    public static void StartAxle(Simulation sim, Entity worker, Entity wagon)
    {
        if (AxleProblem(sim, worker, wagon) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, $"Can't fit an axle: {why}.", sim.LocationOf(worker));
            return;
        }
        FitSession.StartAxle(sim, worker, wagon);
    }

    /// <summary>The wheel's trued: it runs straight again.</summary>
    public static void WheelTrued(Simulation sim, Entity worker, Entity wagon)
    {
        ref var wg = ref sim.World.Get<Wagon>(wagon);
        wg.Damage &= ~WagonDamage.Wheel;
        wg.MendedBy = worker;
        sim.Messages.Add(MessageKind.Good, $"{sim.World.Get<Creature>(worker).Name} trues the wagon's wheel.", sim.LocationOf(worker));
    }

    /// <summary>A new axle's in, as well as it was fitted: a poor fit still rolls, but the work shows.</summary>
    public static void AxleFitted(Simulation sim, Entity worker, Entity wagon, float quality)
    {
        if (!IsWagon(sim, wagon) || WoodOn(sim, worker) < AxleWood)
            return;
        UseWood(sim, worker, AxleWood);
        ref var wg = ref sim.World.Get<Wagon>(wagon);
        wg.Damage &= ~WagonDamage.Axle;
        wg.MendedBy = worker;
        var how = quality >= 0.75f ? "It runs sweetly." : quality >= 0.4f ? "It'll hold." : "It knocks a bit, but it'll roll.";
        sim.Messages.Add(MessageKind.Good, $"{sim.World.Get<Creature>(worker).Name} fits the wagon a new axle. {how}", sim.LocationOf(worker));
    }
}

/// <summary>True a party wagon's wheel (<see cref="TrueSession"/>).</summary>
public sealed record TrueWheelCommand(Entity Avatar, Entity Wagon) : ISimCommand, Core.IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (Core.InventoryCommand.Ready(sim, Avatar) && Wagons.IsWagon(sim, Wagon) && !sim.World.Has<Work.WorkSession>(Avatar))
            Wagons.StartWheel(sim, Avatar, Wagon);
    }
}

/// <summary>Fit a new axle to a party wagon, from wood you carry (<see cref="FitSession"/>).</summary>
public sealed record FitAxleCommand(Entity Avatar, Entity Wagon) : ISimCommand, Core.IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (Core.InventoryCommand.Ready(sim, Avatar) && Wagons.IsWagon(sim, Wagon) && !sim.World.Has<Work.WorkSession>(Avatar))
            Wagons.StartAxle(sim, Avatar, Wagon);
    }
}

/// <summary>
/// An AI tinker mends a party's wagon: takes the order on, walks out to it, and works it over,
/// rolled rather than played. Players do it by hand (<see cref="TrueSession"/>, <see cref="FitSession"/>).
/// </summary>
public sealed class WagonJob(int order) : Job
{
    /// <summary>Ticks of work for each thing wrong with it.</summary>
    public const int TicksEach = 600;

    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return null;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var e in sim.Shops.FrontFirst(sim))
        {
            if (!e.IsOpen || e.Kind.Kind != ServiceKind.WagonRepair || e.Kind.AtDef != workplace || !trade.Def.Serving(e.Kind.Kind)
                || !e.KnownBy.Contains(pawn) || (!e.Assignee.IsNull && e.Assignee != pawn) || !Wagons.Damaged(sim, e.Subject)
                || sim.Reservations.IsReservedByOther(e.Subject, pawn))
                continue;
            if (Navigation.CanReach(sim, pawn, from, w.Get<Thing>(e.Subject).Cell, PathEnd.Touch))
                return new WagonJob(e.Id);
        }
        return null;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        var orders = Sim.Shops;
        if (orders.Find(order) is not { IsOpen: true } e || (e.Assignee != Pawn && !orders.Accept(Sim, Pawn, order)))
        {
            Fail("someone else has it");
            yield break;
        }
        var wagon = e.Subject;
        Report = $"Going to {e.PosterName}'s wagon";
        foreach (var s in GoToEntity(wagon, PathEnd.Touch, 1500)) yield return s;
        if (HasFailed) yield break;
        while (Alive(wagon) && Wagons.Damaged(Sim, wagon))
        {
            var damage = Get<Wagon>(wagon).Damage;
            var part = (damage & WagonDamage.Wheel) != 0 ? WagonDamage.Wheel : WagonDamage.Axle;
            Report = part == WagonDamage.Wheel ? "Truing a wagon wheel" : "Fitting a wagon a new axle";
            for (var i = 0; i < TicksEach; i++)
            {
                if (!Alive(wagon) || !Wagons.Damaged(Sim, wagon) || !e.IsOpen)
                {
                    Fail("the wagon's gone");
                    yield break;
                }
                SkillRules.Learn(Sim, Pawn, Skill.Crafting, SkillRules.XpPerWorkTick);
                yield return JobStatus.Running;
            }
            ref var wg = ref Get<Wagon>(wagon);
            wg.Damage &= ~part;
            wg.MendedBy = Pawn;
        }
        // The order is paid for by the shop system once nothing's wrong with the wagon.
    }
}
