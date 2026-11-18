using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tinkering;

/// <summary>
/// A trip-bell set on the ground (<see cref="TripBells"/>). <see cref="Sprung"/> and when it rang
/// are plain to anyone who looks; whether it jammed is known only at the reveal (<see cref="Jammed"/>),
/// and its binding (<see cref="Contraption.Binding"/>) never.
/// </summary>
public struct TripSet
{
    /// <summary>Who set it.</summary>
    public Entity By;

    /// <summary>Its cord's been tripped: it does nothing more until it's wound again.</summary>
    public bool Sprung;

    /// <summary>Tripped, but a binding part held the spring and the bell never rang. Hidden.</summary>
    public bool Jammed;

    /// <summary>The tick it last rang out; -1 if never.</summary>
    public long RangAt;
}

/// <summary>
/// The tinker's trip-bells (docs/DESIGN.md, "Tinker"): the first of the town's own contraptions.
/// Fitted at the bench like a contraption for the road, then carried out and set on the ground
/// (F with it in hand). A raider who crosses one trips it: it rings the town's alarm at once, far
/// out, and the clangor shakes them (<see cref="Afterlife.Unnerved"/>). Without one the town only
/// knows a raid's on it when somebody sees the raiders (<see cref="RaidCrisis.Watch"/>). A binding
/// bell may jam when tripped and stay silent; nobody can tell until it's needed.
/// </summary>
public static class TripBells
{
    public const string Def = "TripBell";

    /// <summary>How far either side of a bell its cord runs, cells: a raider this near trips it.</summary>
    public const int Reach = 5;

    /// <summary>No two bells within this of each other: one cord to a way in.</summary>
    public const int Apart = 2 * Reach;

    /// <summary>How far in from the map's edge an AI tinker sets one on the warned road, cells: out past where anyone in town would see them.</summary>
    public const int SetIn = 8;

    public static bool IsBell(Simulation sim, Entity item) =>
        sim.World.IsAlive(item) && sim.World.TryGet<Thing>(item, out var t) && t.Def.Contraption?.Use == ContraptionUse.Alarm;

    public static bool IsSet(Simulation sim, Entity item) => IsBell(sim, item) && sim.World.Has<TripSet>(item);

    /// <summary>A trip-bell in hand, not yet set; Entity.Null if none.</summary>
    public static Entity Held(Simulation sim, Entity who)
    {
        var held = sim.World.Get<Carrying>(who).Item;
        return IsBell(sim, held) && !sim.World.Has<TripSet>(held) ? held : Entity.Null;
    }

    /// <summary>A trip-bell in hand or in the pack, not yet set; Entity.Null if none.</summary>
    public static Entity Carried(Simulation sim, Entity who)
    {
        var held = Held(sim, who);
        if (!held.IsNull)
            return held;
        return sim.PackOf(who) is { IsNull: false } pack
            ? sim.StoredIn(pack).FirstOrDefault(e => IsBell(sim, e) && !sim.World.Has<TripSet>(e), Entity.Null)
            : Entity.Null;
    }

    /// <summary>Every bell set on the map, in entity order.</summary>
    public static List<Entity> All(Simulation sim)
    {
        var w = sim.World;
        return w.Query<TripSet>().Where(e => IsBell(sim, e) && w.Get<Thing>(e).Spawned).OrderBy(e => e.Index).ToList();
    }

    /// <summary>The bell set on this cell; Entity.Null if none.</summary>
    public static Entity At(Simulation sim, int3 cell)
    {
        foreach (var e in All(sim))
            if (sim.World.Get<Thing>(e).Cell == cell)
                return e;
        return Entity.Null;
    }

    /// <summary>Why a bell can't be set on this cell (out of doors, on open ground, not beside another); null if it can.</summary>
    public static string? SetProblem(Simulation sim, int3 cell)
    {
        var map = sim.Map;
        if (!map.InBounds(cell) || !map.Passable(cell) || !map.Standable(map.Idx(cell)))
            return "not on open ground";
        if (map.Roofed[map.Idx(cell)])
            return "only out of doors, across a way in";
        if (!map.Structure[map.Idx(cell)].IsNull)
            return "something's built there";
        foreach (var e in All(sim))
            if (Near(sim.World.Get<Thing>(e).Cell, cell, Apart))
                return "there's a trip-bell set close by";
        return null;
    }

    /// <summary>Set a carried bell on a cell. False if it can't go there.</summary>
    public static bool Set(Simulation sim, Entity who, Entity bell, int3 cell)
    {
        var w = sim.World;
        if (!IsBell(sim, bell) || w.Has<TripSet>(bell) || !Inventory.OnPerson(sim, who, bell) || SetProblem(sim, cell) != null)
            return false;
        var item = Inventory.TakeFromPerson(sim, who, bell, 1);
        sim.PlaceItemNear(item, cell, byHand: true);
        w.Add(item, new TripSet { By = who, RangAt = -1 });
        sim.Messages.Add(MessageKind.Info, $"{w.Get<Creature>(who).Name} sets a trip-bell.", w.Get<Thing>(item).Cell);
        return true;
    }

    /// <summary>Wind a sprung bell again, where it lies.</summary>
    public static bool Rewind(Simulation sim, Entity who, Entity bell)
    {
        var w = sim.World;
        if (!IsSet(sim, bell) || !w.Get<TripSet>(bell).Sprung)
            return false;
        ref var set = ref w.Get<TripSet>(bell);
        set.Sprung = false;
        set.Jammed = false;
        set.By = who;
        return true;
    }

    /// <summary>Take a set bell up again, into the pack or hands.</summary>
    public static bool TakeUp(Simulation sim, Entity who, Entity bell)
    {
        var w = sim.World;
        if (!IsSet(sim, bell))
            return false;
        w.Remove<TripSet>(bell);
        ref var carrying = ref w.Get<Carrying>(who);
        carrying.Item = Inventory.PickUp(sim, who, bell, carrying.Item);
        if (w.IsAlive(bell) && w.Get<Thing>(bell).Spawned)
            w.Add(bell, new TripSet { By = who, RangAt = -1 }); // nowhere to put it: it stays set
        return true;
    }

    /// <summary>What F does at a set bell, as its prompt.</summary>
    public static string Prompt(Simulation sim, Entity bell) =>
        sim.World.Get<TripSet>(bell).Sprung ? "Wind the tripped trip-bell again" : "Take up the trip-bell";

    /// <summary>A bell tripped by a raider: it rings the alarm and shakes them, unless it jams.</summary>
    internal static void Trip(Simulation sim, Entity bell, Entity raider)
    {
        var w = sim.World;
        ref var set = ref w.Get<TripSet>(bell);
        set.Sprung = true;
        if (w.TryGet<Contraption>(bell, out var c) && c.Binding && sim.Rand.Chance(Contraptions.JamChance))
        {
            set.Jammed = true;
            return;
        }
        set.RangAt = sim.Clock.Tick;
        var cell = w.Get<Thing>(bell).Cell;
        Afterlife.Hauntings.Unnerve(sim, cell);
        var raid = sim.Director.Active.OfType<RaidCrisis>().FirstOrDefault(r => r.Raiders.Contains(raider));
        if (raid != null)
            raid.Sound(sim, cell, bell);
        else
            sim.Messages.Add(MessageKind.Danger, "A trip-bell rings out: something's crossed it!", cell, "Trip-bell!");
    }

    /// <summary>
    /// Where an AI tinker sets a bell for a raid warned of: a little way in along the road it'll come
    /// by, toward the town. Null if there's nowhere fit.
    /// </summary>
    public static int3? SpotFor(Simulation sim, int side)
    {
        if (Raids.Toward(sim) is not { } target)
            return null;
        var entry = Raids.RoadEntry(sim, side, target) ?? Raids.EdgeEntry(sim, side, target);
        if (entry is not { } from)
            return null;
        var dx = target.X - from.X;
        var dy = target.Y - from.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 1f)
            return null;
        var step = Math.Min(SetIn, (int)length / 2);
        var x = from.X + (int)MathF.Round(dx * step / length);
        var y = from.Y + (int)MathF.Round(dy * step / length);
        if (sim.Map.NearestStandable(x, y, from.Z, sim.Map.Depth) is not { } near)
            return null;
        foreach (var c in sim.SpiralFrom(near, 4))
            if (SetProblem(sim, c) == null && sim.Regions.CanReach(c, target, PathEnd.Touch))
                return c;
        return null;
    }

    /// <summary>Is a bell already set (and not sprung) within its reach of this spot?</summary>
    public static bool Guarded(Simulation sim, int3 spot) =>
        All(sim).Exists(e => !sim.World.Get<TripSet>(e).Sprung && Near(sim.World.Get<Thing>(e).Cell, spot, Apart));

    private static bool Near(int3 a, int3 b, int reach) =>
        Math.Abs(a.Z - b.Z) <= 1 && Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)) <= reach;

    internal static bool Crosses(int3 bell, int3 at) => Near(bell, at, Reach);
}

/// <summary>
/// Watches the set trip-bells for raiders crossing them, and raids not yet seen for someone to see
/// them (<see cref="RaidCrisis.Watch"/>). Often enough that a bell rings as it's crossed.
/// </summary>
public sealed class TripBellSystem : ISimSystem
{
    public string Name => "TripBells";
    public int Interval => 10;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        foreach (var bell in TripBells.All(sim))
        {
            if (w.Get<TripSet>(bell).Sprung)
                continue;
            var cell = w.Get<Thing>(bell).Cell;
            foreach (var e in sim.Creatures())
            {
                var c = w.Get<Creature>(e);
                if (!Factions.Attacker(c.Faction) || c.Def.Race == Race.Dragon || c.Dead || c.Downed
                    || !TripBells.Crosses(cell, w.Get<Position>(e).Cell))
                    continue;
                TripBells.Trip(sim, bell, e);
                break;
            }
        }
        foreach (var raid in sim.Director.Active.OfType<RaidCrisis>())
            raid.Watch(sim);
    }
}

/// <summary>
/// A raid's been called and the road it's coming by has no bell: an AI tinker fits one at the bench
/// (an iron bar and a piece of wood, from on it or the shop's chest) and sets it across that road.
/// </summary>
public sealed class TripBellJob(Entity bench, Entity chest, int3 spot) : Job
{
    public const int Ticks = 900;

    public override Entity Focus => bench;

    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!sim.Round.Live || w.Has<Avatar>(pawn) || !sim.Militia.Open || sim.Militia.Mustering
            || !w.TryGet<Trade>(pawn, out var trade) || trade.Def.Role != TownRole.TinkerShop
            || sim.Director.WarnedSide is not { } side || TripBells.SpotFor(sim, side) is not { } spot || TripBells.Guarded(sim, spot))
            return null;
        var shop = Shops.ShopSystem.WorkplaceOf(sim, trade.Def);
        var bench = Carpentry.Carpenters.StationOf(sim, shop, Tinkers.BenchDef);
        var chest = Carpentry.Carpenters.ChestOf(sim, shop);
        if (TripBells.Carried(sim, pawn).IsNull && (bench.IsNull || !HasMakings(sim, pawn, chest) || sim.Reservations.IsReservedByOther(bench, pawn)))
            return null;
        var from = w.Get<Position>(pawn).Cell;
        if (!Navigation.CanReach(sim, pawn, from, spot, PathEnd.OnTile))
            return null;
        return new TripBellJob(bench, chest, spot);
    }

    private static int Count(Simulation sim, Entity pawn, Entity chest, string def) =>
        Inventory.CarriedOf(sim, pawn, sim.Defs.Thing(def)).Sum(e => sim.World.Get<Stack>(e).Count) + Carpentry.Carpenters.CountIn(sim, chest, def);

    private static bool HasMakings(Simulation sim, Entity pawn, Entity chest) =>
        Count(sim, pawn, chest, Tinkers.BarDef) >= 1 && Count(sim, pawn, chest, "Wood") >= 1;

    /// <summary>Use up one of a thing, from on the pawn first, then the chest.</summary>
    private static bool UseOne(Simulation sim, Entity pawn, Entity chest, string def)
    {
        if (Inventory.CarriedOf(sim, pawn, sim.Defs.Thing(def)).FirstOrDefault(Entity.Null) is { IsNull: false } stack)
        {
            sim.DestroyThing(Inventory.TakeFromPerson(sim, pawn, stack, 1));
            return true;
        }
        return Carpentry.Carpenters.TakeFrom(sim, chest, def, 1);
    }

    protected override IEnumerable<JobStatus> Run()
    {
        var bell = TripBells.Carried(Sim, Pawn);
        if (bell.IsNull)
        {
            Report = "Fitting a trip-bell";
            if (!Reserve(bench)) yield break;
            foreach (var s in GoTo(Get<Thing>(bench).Cell, PathEnd.Touch)) yield return s;
            if (HasFailed) yield break;
            foreach (var s in Wait(Ticks)) yield return s;
            if (!Alive(bench) || !HasMakings(Sim, Pawn, chest))
            {
                Fail("nothing to make it from");
                yield break;
            }
            UseOne(Sim, Pawn, chest, Tinkers.BarDef);
            UseOne(Sim, Pawn, chest, "Wood");
            SkillRules.Learn(Sim, Pawn, Skill.Crafting, SkillRules.XpPerWorkTick * Ticks);
            bell = Sim.CreateItem(Sim.Defs.Thing(TripBells.Def), 1);
            Tinkers.Made(Sim, [bell], Pawn);
            Inventory.Stow(Sim, Pawn, bell);
        }
        Report = "Setting a trip-bell on the road";
        foreach (var s in GoTo(spot, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        if (!Alive(bell) || !Inventory.OnPerson(Sim, Pawn, bell) || TripBells.Guarded(Sim, spot) || !TripBells.Set(Sim, Pawn, bell, spot))
            Fail("couldn't set it there");
    }
}
