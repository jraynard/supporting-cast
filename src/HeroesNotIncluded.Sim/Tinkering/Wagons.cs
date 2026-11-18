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

    /// <summary>The axle cracked: a new one hewn and its hubs driven on (the carpenter's, <see cref="Carpentry.JoinSession"/>), from a log.</summary>
    Axle = 2,

    /// <summary>A side board sprung or the shaft split: a plank pegged across it (the carpenter's, <see cref="Carpentry.JoinSession"/>).</summary>
    Board = 4,
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

    /// <summary>Who trued its wheel, to be paid for the tinker's order (<see cref="ServiceKind.WagonRepair"/>).</summary>
    public Entity MendedBy;

    /// <summary>Who did its woodwork (the axle, a board), to be paid for the carpenter's order (<see cref="ServiceKind.WagonJoinery"/>).</summary>
    public Entity JoinedBy;

    /// <summary>Who took its weight while the carpenter drove a new axle's hubs on (<see cref="Carpentry.LiftSession"/>).</summary>
    public Entity LiftedBy;
}

/// <summary>
/// Party wagons (docs/DESIGN.md, "Tinker" and "Carpenter"). Some parties ride in with a wagon, and
/// the road has often had its way with it: a wheel out of true, an axle cracked, a board sprung.
/// It's parked near their haunt, and the owner asks for it mended: the wheel of the tinker
/// (<see cref="ServiceKind.WagonRepair"/>), the woodwork of the carpenter (<see cref="ServiceKind.WagonJoinery"/>),
/// each its own order, paid once that part's sound. A wagon still broken in any part when they set
/// out on a quest carries nothing home: no strongbox, no curios, and a party in a worse temper.
/// </summary>
public static class Wagons
{
    public const string Def = "PartyWagon";

    /// <summary>The share of wagons that come in the worse for the road.</summary>
    public const float DamagedChance = 0.8f;

    /// <summary>Logs a new axle is hewn from.</summary>
    public const int AxleWood = 1;

    /// <summary>Planks pegged across a split board.</summary>
    public const int BoardPlanks = 1;

    /// <summary>Pegs a board takes, and the joints of an axle (an arm and a hub at each end).</summary>
    public const int BoardPegs = 3, AxleEnds = 2;

    /// <summary>The share of damaged wagons with a sprung board as well.</summary>
    public const float BoardChance = 0.45f;

    /// <summary>Satisfaction lost by each of a party setting out with their wagon still broken.</summary>
    public const float BrokenMood = 1f;

    public static bool IsWagon(Simulation sim, Entity e) => sim.World.IsAlive(e) && sim.World.Has<Wagon>(e);

    public static bool Damaged(Simulation sim, Entity wagon) => IsWagon(sim, wagon) && sim.World.Get<Wagon>(wagon).Damage != WagonDamage.None;

    /// <summary>The parts each shop's order is for: the wheel the tinker's, the woodwork the carpenter's.</summary>
    public static WagonDamage PartsFor(ServiceKind kind) => kind switch
    {
        ServiceKind.WagonRepair => WagonDamage.Wheel,
        ServiceKind.WagonJoinery => WagonDamage.Axle | WagonDamage.Board,
        _ => WagonDamage.None,
    };

    /// <summary>Is the part of it this order's for still broken?</summary>
    public static bool DamagedFor(Simulation sim, Entity wagon, ServiceKind kind) =>
        IsWagon(sim, wagon) && (sim.World.Get<Wagon>(wagon).Damage & PartsFor(kind)) != 0;

    /// <summary>A party's wagon in town; Entity.Null if they have none.</summary>
    public static Entity Of(Simulation sim, int party)
    {
        foreach (var e in sim.World.Each<Wagon>())
            if (sim.World.Get<Wagon>(e).Party == party)
                return e;
        return Entity.Null;
    }

    public static string DamageLabel(WagonDamage d)
    {
        var parts = new List<string>();
        if ((d & WagonDamage.Wheel) != 0)
            parts.Add("a buckled wheel");
        if ((d & WagonDamage.Axle) != 0)
            parts.Add("a cracked axle");
        if ((d & WagonDamage.Board) != 0)
            parts.Add("a sprung board");
        return parts.Count switch
        {
            0 => "nothing wrong with it",
            1 => parts[0],
            _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
        };
    }

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
        // A sprung board, now and then, besides (its own stream, so the rest roll as before).
        var boards = new SimRandom(seed ^ ((ulong)(uint)party * 0x85EB_CA6Bu) ^ 0xB0A2_D5u);
        if (damage != WagonDamage.None && boards.Chance(BoardChance))
            damage |= WagonDamage.Board;
        var wagon = sim.SpawnStructure(sim.Defs.Thing(Def), spot);
        sim.World.Add(wagon, new Wagon { Party = party, Owner = owner, Damage = damage, WheelSeed = rand.NextUInt() | 1u, MendedBy = Entity.Null, JoinedBy = Entity.Null });
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
        return WoodOn(sim, worker) < AxleWood ? $"a new axle is hewn from {AxleWood} log" : null;
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
            sim.Messages.Tell(worker, MessageKind.Info, $"Can't true it: {why}.", sim.LocationOf(worker));
            return;
        }
        WorkSystem.Start(sim, worker, new TrueSession { Wagon = wagon, Seed = sim.World.Get<Wagon>(wagon).WheelSeed });
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
    public static void AxleFitted(Simulation sim, Entity worker, Entity wagon, float quality, Entity? lifter = null)
    {
        if (!IsWagon(sim, wagon) || WoodOn(sim, worker) < AxleWood)
            return;
        UseWood(sim, worker, AxleWood);
        ref var wg = ref sim.World.Get<Wagon>(wagon);
        wg.Damage &= ~WagonDamage.Axle;
        wg.JoinedBy = worker;
        var lifted = lifter is { } l && sim.World.IsAlive(l) && sim.World.TryGet<Creature>(l, out var lc) ? lc.Name : null;
        if (lifted != null)
            wg.LiftedBy = lifter!.Value;
        var how = quality >= 0.75f ? "It runs sweetly." : quality >= 0.4f ? "It'll hold." : "It knocks a bit, but it'll roll.";
        sim.Messages.Add(MessageKind.Good, $"{sim.World.Get<Creature>(worker).Name} fits the wagon a new axle{(lifted != null ? $", {lifted} taking its weight" : "")}. {how}", sim.LocationOf(worker));
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

/// <summary>
/// An AI tinker trues a party wagon's wheel, or an AI carpenter does its woodwork (the order's
/// part, <see cref="Wagons.PartsFor"/>): takes the order on, walks out to it, and works it over,
/// rolled rather than played. Players do it by hand (<see cref="TrueSession"/>, <see cref="Carpentry.JoinSession"/>).
/// </summary>
public sealed class WagonJob(int order) : Job
{
    /// <summary>Ticks of work for each thing wrong with it.</summary>
    public const int TicksEach = 600;

    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || !trade.Def.HasWorkplace)
            return null;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var e in sim.Shops.FrontFirst(sim))
        {
            if (!e.IsOpen || e.Kind.Kind is not (ServiceKind.WagonRepair or ServiceKind.WagonJoinery) || e.Kind.AtRole != trade.Def.Role || !trade.Def.Serving(e.Kind.Kind)
                || !e.KnownBy.Contains(pawn) || (!e.Assignee.IsNull && e.Assignee != pawn) || !Wagons.DamagedFor(sim, e.Subject, e.Kind.Kind)
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
        var kind = e.Kind.Kind;
        Report = $"Going to {e.PosterName}'s wagon";
        foreach (var s in GoToEntity(wagon, PathEnd.Touch, 1500)) yield return s;
        if (HasFailed) yield break;
        while (Alive(wagon) && Wagons.DamagedFor(Sim, wagon, kind))
        {
            var damage = Get<Wagon>(wagon).Damage & Wagons.PartsFor(kind);
            var part = (damage & WagonDamage.Wheel) != 0 ? WagonDamage.Wheel : (damage & WagonDamage.Axle) != 0 ? WagonDamage.Axle : WagonDamage.Board;
            Report = part switch
            {
                WagonDamage.Wheel => "Truing a wagon wheel",
                WagonDamage.Axle => "Hewing a wagon a new axle",
                _ => "Pegging a wagon's sprung board",
            };
            for (var i = 0; i < TicksEach; i++)
            {
                if (!Alive(wagon) || !Wagons.DamagedFor(Sim, wagon, kind) || !e.IsOpen)
                {
                    Fail("the wagon's gone");
                    yield break;
                }
                SkillRules.Learn(Sim, Pawn, Skill.Crafting, SkillRules.XpPerWorkTick);
                yield return JobStatus.Running;
            }
            ref var wg = ref Get<Wagon>(wagon);
            wg.Damage &= ~part;
            if (part == WagonDamage.Wheel)
                wg.MendedBy = Pawn;
            else
                wg.JoinedBy = Pawn;
        }
        // The order is paid for by the shop system once nothing's wrong with the wagon.
    }
}
