using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Environment;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Ai;

/// <summary>Spatial queries shared by brains and work givers.</summary>
public static class AiQueries
{
    private static int Dist(int3 a, int3 b) => Map.Distance(a, b);

    /// <summary>Nearest spawned, unreserved, reachable item matching a predicate.</summary>
    public static Entity FindNearestItem(Simulation sim, Entity pawn, Func<Entity, ThingDef, bool> match, int maxDistance = 200)
    {
        var from = sim.World.Get<Position>(pawn).Cell;
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        foreach (var e in sim.AvailableItems())
        {
            var thing = sim.World.Get<Thing>(e);
            if (!match(e, thing.Def) || sim.Reservations.IsReservedByOther(e, pawn))
                continue;
            var d = Dist(from, thing.Cell);
            if (d >= bestDist || d > maxDistance)
                continue;
            if (sim.Map.Fire[sim.Map.Idx(thing.Cell)] > 0)
                continue;
            if (!Navigation.CanReach(sim, pawn, from, thing.Cell, PathEnd.OnTile))
                continue;
            best = e;
            bestDist = d;
        }
        return best;
    }

    public static bool InStockpile(Simulation sim, int3 tile) => sim.Map.Zone[sim.Map.Idx(tile)] > 0;

    /// <summary>
    /// Some stockpile tile the pawn can reach would take this item and isn't claimed: what
    /// <see cref="FindStockpileTile"/> would find something for, without looking for the nearest.
    /// </summary>
    public static bool HasStockpileRoom(Simulation sim, Entity pawn, ThingDef def, MaterialDef? material)
    {
        var map = sim.Map;
        var from = sim.World.Get<Position>(pawn).Cell;
        foreach (var i in map.ZonedCells)
        {
            var t = map.CellOf(i);
            if (sim.CanAcceptItem(t, def, out _, material) && !sim.Reservations.IsTileReservedByOther(t, pawn)
                && Navigation.CanReach(sim, pawn, from, t, PathEnd.OnTile))
                return true;
        }
        return false;
    }

    /// <summary>Nearest stockpile tile that can take the item and isn't claimed.</summary>
    public static int3? FindStockpileTile(Simulation sim, Entity pawn, ThingDef def, int3 near, MaterialDef? material = null)
    {
        int3? best = null;
        var bestDist = int.MaxValue;
        var map = sim.Map;
        var from = sim.World.Get<Position>(pawn).Cell;
        foreach (var i in map.ZonedCells)
        {
            var t = map.CellOf(i);
            var d = Dist(near, t);
            if (d >= bestDist)
                continue;
            if (!sim.CanAcceptItem(t, def, out _, material) || sim.Reservations.IsTileReservedByOther(t, pawn))
                continue;
            if (!Navigation.CanReach(sim, pawn, from, t, PathEnd.OnTile))
                continue;
            best = t;
            bestDist = d;
        }
        return best;
    }

    /// <summary>
    /// Search reachable tiles near <paramref name="center"/> for one whose temperature is within
    /// [min, max]. Falls back to the tile closest to the range if none qualifies.
    /// </summary>
    public static int3? FindTemperatureSpot(
        Simulation sim,
        Entity pawn,
        int3 center,
        float min,
        float max,
        int radius,
        Func<int3, bool>? accept = null,
        bool felt = true
    )
    {
        var map = sim.Map;
        var from = sim.World.Get<Position>(pawn).Cell;
        // What a creature would feel there (a fire's warmth too, beside it rather than in it), or the bare air (for eggs).
        var sources = felt ? Warmth.Sources(sim) : null;
        int3? best = null;
        var bestScore = float.MaxValue;
        foreach (var t in sim.SpiralFrom(center, radius))
        {
            var i = map.Idx(t);
            if (map.PathCost[i] <= 0 || map.Fire[i] > 0 || map.GroundUnder(i) is { IsWater: true })
                continue;
            if (sim.Reservations.IsTileReservedByOther(t, pawn))
                continue;
            if (accept != null && !accept(t))
                continue;
            if (sources != null && Warmth.IsHearth(sources, t))
                continue;
            var temp = sources != null ? Warmth.FeltAt(sim, t, sources) : map.Temperature[i];
            var miss = temp < min ? min - temp : temp > max ? temp - max : 0f;
            // Being in range dominates; then prefer close tiles.
            var score = miss * 100f + Dist(center, t);
            if (score >= bestScore)
                continue;
            if (!Navigation.CanReach(sim, pawn, from, t, PathEnd.OnTile))
                continue;
            best = t;
            bestScore = score;
        }
        return best;
    }

    /// <summary>
    /// For flyers: the highest open-sky ground among a few random columns near <paramref name="center"/>
    /// (a hilltop or cliff edge to perch on), if it's clearly above the center. Null otherwise.
    /// </summary>
    public static int3? FindPerch(Simulation sim, Entity pawn, int3 center, int radius, Func<int3, bool>? accept = null)
    {
        var from = sim.World.Get<Position>(pawn).Cell;
        int3? best = null;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var x = center.X + sim.Rand.Range(-radius, radius + 1);
            var y = center.Y + sim.Rand.Range(-radius, radius + 1);
            if (!sim.Map.InBounds(x, y, 0))
                continue;
            var z = sim.Map.SurfaceZ(x, y);
            var c = new int3(x, y, z);
            if (z < 0 || z <= (best?.Z ?? center.Z + 1) || !sim.Map.Passable(c) || sim.Map.CoveredAbove(sim.Map.Idx(c)))
                continue;
            if (Navigation.CanReach(sim, pawn, from, c, PathEnd.OnTile) && (accept == null || accept(c)))
                best = c;
        }
        return best;
    }

    public static int3? RandomReachableNear(Simulation sim, Entity pawn, int3 center, int radius, SimRandom? rand = null)
    {
        rand ??= sim.Rand;
        var from = sim.World.Get<Position>(pawn).Cell;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            if (sim.Map.NearestStandable(center.X + rand.Range(-radius, radius + 1), center.Y + rand.Range(-radius, radius + 1), center.Z) is not { } t)
                continue;
            var i = sim.Map.Idx(t);
            if (sim.Map.PathCost[i] <= 0 || sim.Map.Fire[i] > 0)
                continue;
            if (Navigation.CanReach(sim, pawn, from, t, PathEnd.OnTile))
                return t;
        }
        return null;
    }

    /// <summary>
    /// The creature's own bed, else the nearest free one of the right kind: a nest, a prisoner bed
    /// nobody else owns (<paramref name="prisoner"/>), or an ordinary bed.
    /// </summary>
    public static Entity FindFreeBed(Simulation sim, Entity pawn, bool nest, bool prisoner = false)
    {
        var w = sim.World;
        if (w.TryGet<BedOwner>(pawn, out var owner) && w.IsAlive(owner.Bed))
            return owner.Bed;
        HashSet<Entity>? owned = null;
        if (prisoner)
        {
            owned = [];
            foreach (var o in w.Each<BedOwner>())
                if (o != pawn)
                    owned.Add(w.Get<BedOwner>(o).Bed);
        }
        var from = w.Get<Position>(pawn).Cell;
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        foreach (var e in w.Query<Thing>())
        {
            var thing = w.Get<Thing>(e);
            if (thing.Def.Building is not { IsBed: true } b || b.IsNest != nest || w.Has<Construction>(e))
                continue;
            if (w.Has<ForPrisoners>(e) != prisoner || owned?.Contains(e) == true)
                continue;
            if (sim.Reservations.IsReservedByOther(e, pawn))
                continue;
            var d = Dist(from, thing.Cell);
            if (d < bestDist && Navigation.CanReach(sim, pawn, from, thing.Cell, PathEnd.OnTile))
            {
                best = e;
                bestDist = d;
            }
        }
        return best;
    }

    public static bool IsNight(Simulation sim)
    {
        var h = sim.Clock.HourOfDay;
        return h >= 22f || h < 6f;
    }
}

/// <summary>Decides what a creature should do next.</summary>
public interface IBrain
{
    Job? Think(Simulation sim, Entity pawn);
}

public sealed class HumanBrain : IBrain
{
    private readonly WorkGivers _work = new();

    public Job? Think(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var creature = w.Get<Creature>(pawn);
        var def = creature.Def;
        var needs = w.Get<Needs>(pawn);
        var tile = w.Get<Position>(pawn).Cell;
        var temp = Warmth.FeltAt(sim, tile);
        var tol = creature.Tolerance(sim.Clock.Tick);
        var food = sim.Defs.Need("Food");
        var rest = sim.Defs.Need("Rest");

        // 1. Dangerous temperature or standing in fire.
        if (sim.Map.Fire[sim.Map.Idx(tile)] > 0 || temp < tol.SafeMin + 2 || temp > tol.SafeMax - 2)
            if (SeekComfort(sim, pawn, tol, temp, urgent: true) is { } escape)
                return escape;

        // ...or a boar coming at them (hunters stand and shoot: see AiSystem.InDanger).
        if (AnimalBrain.Charger(sim, pawn) is { } charger && AnimalBrain.FleeSpot(sim, pawn, charger, 12) is { } away)
            return new FleeJob(away);

        // ...or standing in a poison dragon's cloud: out of it.
        if (VenomClouds.CloudAt(sim, tile) is { } cloud && AnimalBrain.FleeSpot(sim, pawn, cloud.Cell, (int)MathF.Ceiling(cloud.Radius) + 4) is { } clear)
            return new FleeJob(clear);

        // 2. Urgent needs. Only real hunger makes villagers touch the dragon's meat.
        if (needs.Get(food) < food.UrgentBelow && EatSomething(sim, pawn, def) is { } eat)
            return eat;
        if (needs.Get(rest) < rest.UrgentBelow)
            return Sleep(sim, pawn);
        if (AiQueries.IsNight(sim) && needs.Get(rest) < 0.7f)
            return Sleep(sim, pawn);

        // 2b. What the player's avatar asked of them (follow, guard the egg), until told otherwise.
        if (Requests.JobFor(sim, pawn) is { } asked)
            return asked;

        // 3. Gear lying around (a quick errand between jobs), then work by the pawn's priorities.
        if ((FindGear(sim, pawn) ?? FindTool(sim, pawn)) is { } gear)
            return gear;
        if (_work.TryGiveJob(sim, pawn) is { } job)
            return job;

        // 4. Non-urgent needs and comfort.
        if (needs.Get(food) < food.SeekBelow && EatSomething(sim, pawn, def, allowMeat: false) is { } snack)
            return snack;
        if (temp < tol.ComfortMin || temp > tol.ComfortMax)
            if (SeekComfort(sim, pawn, tol, temp) is { } comfort)
                return comfort;

        // 5. Idle.
        return AiQueries.RandomReachableNear(sim, pawn, tile, 6) is { } wander
            ? new WanderJob(wander, sim.Rand.Range(60, 240))
            : null;
    }

    /// <summary>Armor or a pack for a bare slot, or a weapon for empty hands (unless they won't fight), from nearby.</summary>
    /// <remarks>
    /// The player's <see cref="Outfit"/> comes first: an item they were told to put on, and slots
    /// they ordered kept bare, which this never fills.
    /// </remarks>
    internal static Job? FindGear(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var outfit = w.TryGet<Outfit>(pawn, out var o) ? o : new Outfit();
        if (!outfit.Wanted.IsNull)
        {
            var wanted = outfit.Wanted;
            if (w.IsAlive(wanted) && sim.IsAvailable(wanted) && !sim.Reservations.IsReservedByOther(wanted, pawn)
                && Navigation.CanReach(sim, pawn, w.Get<Position>(pawn).Cell, w.Get<Thing>(wanted).Cell, PathEnd.OnTile))
                return new EquipJob(wanted);
            w.Get<Outfit>(pawn).Wanted = Entity.Null; // gone, taken or out of reach: drop the order
        }
        var dressed = w.TryGet<Apparel>(pawn, out var worn);
        var need = 0; // a bit per bare apparel slot
        for (var i = 0; i < ApparelSlotCount; i++)
        {
            var slot = (ApparelSlot)i;
            if ((!dressed || !w.IsAlive(worn[slot])) && !outfit.KeepBare(Outfit.Of(slot)))
                need |= 1 << i;
        }
        var needWeapon = !w.Has<NonViolent>(pawn) && !outfit.Unarmed && (!w.TryGet<Equipment>(pawn, out var eq) || !w.IsAlive(eq.Weapon));
        if (need == 0 && !needWeapon)
            return null;
        var item = AiQueries.FindNearestItem(sim, pawn, (_, d) => d.Apparel is { } a ? (need & (1 << (int)a.Slot)) != 0 : needWeapon && d.Weapon != null, 40);
        return item.IsNull ? null : new EquipJob(item);
    }

    private static readonly int ApparelSlotCount = Enum.GetValues<ApparelSlot>().Length;

    /// <summary>The tool each kind of gathering work wants on the belt.</summary>
    private static readonly (WorkType Work, string Kind)[] ToolWork = [(WorkType.Chop, "Axe"), (WorkType.Mine, "Pick"), (WorkType.Fish, "Rod")];

    /// <summary>
    /// A tool for the gathering work the pawn does (<see cref="ToolWork"/>), onto its belt: one of
    /// each kind, swapped for a faster one when there's one lying about (or kept in a chest).
    /// </summary>
    internal static Job? FindTool(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var mind = w.Get<Mind>(pawn);
        foreach (var (work, kind) in ToolWork)
        {
            if (mind.WorkPriority[(int)work] <= 0)
                continue;
            var have = Crafting.ToolSpeed(sim, pawn, kind);
            if (!Toolbelt.HasRoom(sim, pawn) && Toolbelt.Best(sim, pawn, kind).IsNull)
                continue;
            var item = AiQueries.FindNearestItem(sim, pawn, (_, d) => d.Tool is { } t && t.Kind == kind && t.Speed > have, 40);
            if (!item.IsNull)
                return new FetchToolJob(item);
        }
        return null;
    }

    internal static Job? EatSomething(Simulation sim, Entity pawn, CreatureDef def, bool allowMeat = true)
    {
        // People pass over spoiled food while there's anything fresh (Systems.FoodPoisoning), and eat it
        // only when that's all there is. Dragons eat carrion and don't mind.
        if (def.Race == Race.Human && FindFood(sim, pawn, def, allowMeat, fresh: true) is { IsNull: false } fresh)
            return new EatJob(fresh);
        var food = FindFood(sim, pawn, def, allowMeat, fresh: false);
        return food.IsNull ? null : new EatJob(food);
    }

    private static Entity FindFood(Simulation sim, Entity pawn, CreatureDef def, bool allowMeat, bool fresh)
    {
        // Omnivores prefer a cooked meal, then plant food, and leave the meat for the carnivores (the dragon!).
        // Dragons eat meat while it lasts (it rots soonest), then their cakes (ThingDef.DragonFood).
        Func<Entity, ThingDef, bool> ok = fresh
            ? (e, d) => !FoodPoisoning.IsSpoiled(sim, e)
            : (_, _) => true;
        var food = Entity.Null;
        if (def.EatsPlants && def.Race == Race.Human)
            food = AiQueries.FindNearestItem(sim, pawn, (e, d) => d.Cooked && d.EdibleBy(def) && ok(e, d));
        if (food.IsNull && def.EatsPlants)
            food = AiQueries.FindNearestItem(sim, pawn, (e, d) => !d.IsMeat && !d.DragonFood && d.EdibleBy(def) && ok(e, d));
        if (food.IsNull && def.EatsMeat && (allowMeat || !def.EatsPlants))
            food = AiQueries.FindNearestItem(sim, pawn, (e, d) => d.IsMeat && d.EdibleBy(def) && ok(e, d));
        if (food.IsNull && def.Race == Race.Dragon)
            food = AiQueries.FindNearestItem(sim, pawn, (e, d) => d.DragonFood && ok(e, d));
        return food;
    }

    /// <summary>Go somewhere that feels better than <paramref name="temp"/> (what the pawn feels where it is), if there's one.</summary>
    internal static Job? SeekComfort(Simulation sim, Entity pawn, TempTolerance tol, float temp, bool urgent = false, Func<int3, bool>? accept = null)
    {
        var warmer = temp < tol.ComfortMin;
        var tile = sim.World.Get<Position>(pawn).Cell;
        var spot = AiQueries.FindTemperatureSpot(sim, pawn, tile, tol.ComfortMin, tol.ComfortMax, 25, accept);
        if (spot is not { } t || t == tile)
            return null;
        // Only bother if it's actually better there.
        var there = Warmth.FeltAt(sim, t);
        var missHere = warmer ? tol.ComfortMin - temp : temp - tol.ComfortMax;
        var missThere = there < tol.ComfortMin ? tol.ComfortMin - there : there > tol.ComfortMax ? there - tol.ComfortMax : 0;
        return missThere + 1f < missHere ? new SeekTemperatureJob(t, urgent ? 1200 : 600, warmer, urgent) : null;
    }

    private static Job Sleep(Simulation sim, Entity pawn)
    {
        var bed = AiQueries.FindFreeBed(sim, pawn, nest: false);
        if (!bed.IsNull)
            return new SleepJob(bed);
        // No bed: find somewhere comfortable to lie down rather than wherever we happen to be.
        var tol = sim.World.Get<Creature>(pawn).Tolerance(sim.Clock.Tick);
        var spot = AiQueries.FindTemperatureSpot(sim, pawn, sim.World.Get<Position>(pawn).Cell, tol.ComfortMin, tol.ComfortMax, 20);
        return new SleepJob(Entity.Null, spot);
    }
}

/// <summary>Someone too frail to get up (the prologue's old keeper): rests in their own bed.</summary>
public sealed class BedriddenBrain : IBrain
{
    public Job? Think(Simulation sim, Entity pawn)
    {
        if (!sim.World.TryGet<BedOwner>(pawn, out var owner) || !sim.World.IsAlive(owner.Bed))
            return null;
        return sim.World.Get<Needs>(pawn).Get(sim.Defs.Need("Rest")) < 0.7f ? new SleepJob(owner.Bed) : new ConfinedJob(owner.Bed, 600);
    }
}

/// <summary>
/// A story character (the Pact's sorcerer, Escape's captors): keeps to their spot, and walks up to
/// the player when the story calls for it (<see cref="Scenarios.EnchantedForest"/>, the jailer's
/// supper round in <see cref="Scenarios.EscapeCamp"/>). Their needs are kept full.
/// </summary>
public sealed class StoryNpcBrain : IBrain
{
    public Job? Think(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var npc = w.Get<StoryNpc>(pawn);
        var here = w.Get<Position>(pawn).Cell;
        if (npc.Role == StoryRole.Sorcerer && Scenarios.EnchantedForest.Confronting(sim) is { IsNull: false } avatar)
            return Pathfinder.Touches(here, sim.LocationOf(avatar)) ? new WanderJob(here, 60) : new FollowJob(avatar, 120);
        // Escape's jailer (or whoever stands in for him): at supper time, into the cage with the bowl (the gate's open), and they stay by the prisoner till lock-up.
        if (npc.Role is StoryRole.Jailer or StoryRole.Captor && Scenarios.EscapeCamp.Jailer(sim) == pawn
            && Scenarios.EscapeCamp.SupperFor(sim) is { IsNull: false } prisoner
            && Scenarios.EscapeCamp.Of(sim) is { } camp && !Scenarios.EscapeCamp.GateLocked(sim))
            return Pathfinder.Touches(here, sim.LocationOf(prisoner)) && camp.InCage(here) ? new WanderJob(here, 60) : new FollowJob(prisoner, 120);
        return AiQueries.RandomReachableNear(sim, pawn, npc.Home, 2) is { } spot ? new WanderJob(spot, sim.Rand.Range(300, 900)) : null;
    }
}

/// <summary>
/// Prisoners stay in their bed: sleeping when tired, lying there otherwise, getting out of fire
/// or bitter cold. Wardens bring their food; they don't fetch it.
/// </summary>
public sealed class PrisonerBrain : IBrain
{
    public Job? Think(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var tile = w.Get<Position>(pawn).Cell;
        var temp = Warmth.FeltAt(sim, tile);
        var tol = w.Get<Creature>(pawn).Tolerance(sim.Clock.Tick);
        if (sim.Map.Fire[sim.Map.Idx(tile)] > 0 || temp < tol.SafeMin + 2 || temp > tol.SafeMax - 2)
            if (HumanBrain.SeekComfort(sim, pawn, tol, temp, urgent: true) is { } escape)
                return escape;

        var bed = AiQueries.FindFreeBed(sim, pawn, nest: false, prisoner: true);
        if (bed.IsNull)
            return AiQueries.RandomReachableNear(sim, pawn, tile, 2) is { } spot ? new WanderJob(spot, 600) : null;
        var rest = sim.Defs.Need("Rest");
        if (w.Get<Needs>(pawn).Get(rest) < 0.7f)
            return new SleepJob(bed);
        return new ConfinedJob(bed, 600);
    }
}

/// <summary>
/// Raiders outside turn-based combat: close in on the nearest villager (combat starts once they're
/// near), and run for the map edge when badly hurt.
/// </summary>
public sealed class RaiderBrain : IBrain
{
    public Job? Think(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var tile = w.Get<Position>(pawn).Cell;
        if (w.Has<Escaping>(pawn) && !Armed(sim, pawn) && !WantsToFlee(sim, pawn))
        {
            // A prisoner breaking out snatches up a weapon on the way if one is close.
            var weapon = AiQueries.FindNearestItem(sim, pawn, (_, d) => d.Weapon != null, EscapeWeaponReach);
            if (!weapon.IsNull)
                return new EquipJob(weapon);
        }
        if (WantsToFlee(sim, pawn) || w.Has<Combat.Leaving>(pawn) || w.Has<Escaping>(pawn))
            return sim.NearestEdge(tile) is { } edge ? new LeaveMapJob(edge) : null;

        var best = Entity.Null;
        var bestDist = int.MaxValue;
        foreach (var e in sim.Creatures())
        {
            var c = w.Get<Creature>(e);
            if (!Factions.Hostile(Faction.Raider, c.Faction) || c.Downed)
                continue;
            var at = w.Get<Position>(e).Cell;
            var d = Map.Distance(tile, at);
            if (d < bestDist && sim.Regions.CanReach(tile, at, PathEnd.Touch))
            {
                best = e;
                bestDist = d;
            }
        }
        return best.IsNull ? null : new AssaultJob(best);
    }

    /// <summary>How far an escaping prisoner will go out of their way for a weapon.</summary>
    public const int EscapeWeaponReach = 15;

    public static bool Armed(Simulation sim, Entity pawn) =>
        sim.World.TryGet<Equipment>(pawn, out var eq) && sim.World.IsAlive(eq.Weapon);

    /// <summary>Hurting or hobbled raiders give up.</summary>
    public static bool WantsToFlee(Simulation sim, Entity pawn) =>
        sim.World.TryGet<Health>(pawn, out var h) && (h.Pain > 0.5f || h.Capacity(Capacity.Moving) < 0.4f);
}

/// <summary>
/// Wild animals: out of fire and bitter cold first, then away from anyone who isn't wild, otherwise
/// grazing around the herd. Random choices come from <see cref="WildlifeSystem.Rand"/>.
/// </summary>
public sealed class AnimalBrain : IBrain
{
    public Job? Think(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var def = w.Get<Creature>(pawn).Def;
        var tile = w.Get<Position>(pawn).Cell;
        var temp = Warmth.FeltAt(sim, tile);
        var tol = w.Get<Creature>(pawn).Tolerance(sim.Clock.Tick);
        if (sim.Map.Fire[sim.Map.Idx(tile)] > 0 || temp < tol.SafeMin + 2 || temp > tol.SafeMax - 2)
            if (HumanBrain.SeekComfort(sim, pawn, tol, temp, urgent: true) is { } escape)
                return escape;

        // Hurt: a boar turns on whoever did it while it still can; everything else bolts, further.
        var attacker = Combat.RealtimeAttacks.ProvokedBy(sim, pawn, ProvokedTicks);
        if (!attacker.IsNull)
        {
            var hurting = w.TryGet<Health>(pawn, out var h) && h.Pain > 0.6f;
            if (def.Animal is { Aggressive: true } && !hurting && Combat.CombatRules.Attacks(sim, pawn).Any(a => !a.Ranged))
                return new AnimalAttackJob(attacker);
            if (FleeSpot(sim, pawn, w.Get<Position>(attacker).Cell, 18) is { } run)
                return new FleeJob(run);
        }

        if (Threat(sim, pawn) is { } threat && FleeSpot(sim, pawn, threat, 10) is { } away)
            return new FleeJob(away);

        // A migrating herd keeps on across the valley and leaves by the far edge.
        if (w.TryGet<Migrating>(pawn, out var migrating))
            return new LeaveMapJob(migrating.To);

        var rand = sim.Wildlife.Rand;
        var spot = AiQueries.RandomReachableNear(sim, pawn, HerdCenter(sim, pawn), 5, rand);
        return spot is { } t ? new GrazeJob(t, rand.Range(300, 900)) : null;
    }

    /// <summary>How long an animal remembers being hurt (and keeps running from, or charging, who did it).</summary>
    public const int ProvokedTicks = SimClock.TicksPerSecond * 30;

    /// <summary>Where an animal charging <paramref name="pawn"/> is, if one is within <paramref name="radius"/>.</summary>
    public static int3? Charger(Simulation sim, Entity pawn, int radius = 8)
    {
        var w = sim.World;
        var tile = w.Get<Position>(pawn).Cell;
        foreach (var e in w.Each<Herd>())
            if (w.Get<Mind>(e).Job is AnimalAttackJob j && j.Target == pawn && Map.Distance(w.Get<Position>(e).Cell, tile) <= radius)
                return w.Get<Position>(e).Cell;
        return null;
    }

    /// <summary>Where the nearest able creature that isn't wild stands, if it's within the animal's flee radius.</summary>
    public static int3? Threat(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var tile = w.Get<Position>(pawn).Cell;
        var radius = w.Get<Creature>(pawn).Def.Animal?.FleeRadius ?? 8f;
        int3? nearest = null;
        var best = radius * radius;
        foreach (var e in sim.Creatures())
        {
            var c = w.Get<Creature>(e);
            if (c.Faction == Faction.Wild || !c.Able)
                continue;
            var at = w.Get<Position>(e).Cell;
            if (Math.Abs(at.Z - tile.Z) > 3)
                continue;
            float dx = at.X - tile.X, dy = at.Y - tile.Y;
            var d = dx * dx + dy * dy;
            if (d < best)
            {
                best = d;
                nearest = at;
            }
        }
        return nearest;
    }

    /// <summary>A reachable cell about <paramref name="distance"/> away from <paramref name="from"/>: straight away first, then veering off.</summary>
    public static int3? FleeSpot(Simulation sim, Entity pawn, int3 from, int distance)
    {
        var tile = sim.World.Get<Position>(pawn).Cell;
        var away = new float2(tile.X - from.X, tile.Y - from.Y);
        var angle = away.X == 0 && away.Y == 0 ? 0f : DetMath.Atan2(away.Y, away.X);
        foreach (var veer in (ReadOnlySpan<float>)[0f, 0.6f, -0.6f, 1.2f, -1.2f, 1.8f, -1.8f])
        {
            var a = angle + veer;
            var x = Math.Clamp(tile.X + (int)MathF.Round(DetMath.Cos(a) * distance), 0, sim.Map.Width - 1);
            var y = Math.Clamp(tile.Y + (int)MathF.Round(DetMath.Sin(a) * distance), 0, sim.Map.Height - 1);
            if (sim.Map.NearestStandable(x, y, tile.Z) is not { } t || t == tile || sim.Map.Fire[sim.Map.Idx(t)] > 0)
                continue;
            if (Navigation.CanReach(sim, pawn, tile, t, PathEnd.OnTile))
                return t;
        }
        return null;
    }

    /// <summary>The middle of the animal's herd (itself if it's alone).</summary>
    private static int3 HerdCenter(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var tile = w.Get<Position>(pawn).Cell;
        if (!w.TryGet<Herd>(pawn, out var herd))
            return tile;
        int x = 0, y = 0, n = 0;
        foreach (var e in w.Each<Herd>())
            if (w.Get<Herd>(e).Id == herd.Id && w.Get<Creature>(e).Able)
            {
                var at = w.Get<Position>(e).Cell;
                x += at.X;
                y += at.Y;
                n++;
            }
        return n == 0 ? tile : new int3(x / n, y / n, tile.Z);
    }
}

public sealed class DragonBrain : IBrain
{
    private readonly WorkGivers _work = new();

    public Job? Think(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var creature = w.Get<Creature>(pawn);
        var def = creature.Def;
        var needs = w.Get<Needs>(pawn);
        var tile = w.Get<Position>(pawn).Cell;
        var temp = Warmth.FeltAt(sim, tile);
        var tol = creature.Tolerance(sim.Clock.Tick);
        var food = sim.Defs.Need("Food");
        var rest = sim.Defs.Need("Rest");
        var bond = sim.Defs.Need("Bond");
        var keeper = w.Get<Dragon>(pawn).Keeper;
        var hasKeeper = w.IsAlive(keeper) && !w.Get<Creature>(keeper).Dead;
        var care = Bonding.Peek(sim, pawn);
        var stage = creature.Stage(sim.Clock.Tick);
        // A hatchling eats from a hand or a trough, unless it's starving (Bonding, LifeStage.HandFed).
        Job? Meal() => stage.HandFed ? Bonding.TroughMeal(sim, pawn) : DragonHunting.Meal(sim, pawn, def);

        if (temp < tol.SafeMin + 3)
        {
            if (HumanBrain.SeekComfort(sim, pawn, tol, temp, urgent: true) is { } warmUp)
                return warmUp;
            // Nothing warm nearby (out working, far from home): head back to the nest or the keeper.
            if (WarmthAtHome(sim, pawn, hasKeeper ? keeper : Entity.Null) is { } goHome)
                return goHome;
        }

        // Her time come, a gravid dragon lays her clutch in a nest (anywhere, if none can be reached).
        if (w.TryGet<Gravid>(pawn, out var gravid) && sim.Clock.Tick >= gravid.LayTick && !w.Has<Mount>(pawn))
            return new LayClutchJob(AiQueries.FindFreeBed(sim, pawn, nest: true));

        // A devoted grown dragon carries its keeper home to bed if they're down (outside a fight).
        if (hasKeeper && w.Get<Creature>(keeper).Downed && !w.Has<Carried>(keeper) && !HealthSystem.InBed(sim, keeper)
            && !w.Has<Mount>(pawn) && Bonding.Reached(sim, pawn, Bonding.Devoted) && Rescues.CanCarry(sim, pawn, keeper)
            && !sim.Reservations.IsReservedByOther(keeper, pawn) && Rescues.BedFor(sim, pawn, keeper) is { IsNull: false } keeperBed
            && !sim.Reservations.IsReservedByOther(keeperBed, pawn))
            return new RescueJob(keeper, keeperBed);

        if (needs.Get(food) < food.UrgentBelow)
        {
            // What's laid out, or a grown dragon goes and catches something (in the order its
            // HuntPolicy asks).
            if (DragonHunting.Meal(sim, pawn, def) is { } eat)
                return eat;
            // Nothing to eat: go pester the keeper.
            if (hasKeeper)
                return new FollowJob(keeper, 300);
        }

        // A devoted dragon brings its keeper a share of what it caught.
        if (hasKeeper && w.IsAlive(care.BringMeat) && sim.IsAvailable(care.BringMeat) && !sim.Reservations.IsReservedByOther(care.BringMeat, pawn))
        {
            var meat = care.BringMeat;
            care.BringMeat = Entity.Null;
            return new BringKillJob(meat, keeper);
        }

        // A flyer can end up where its keeper can't walk (a plateau, a far burn order). Hungry or
        // lonely, it comes back within reach rather than waiting there for care that can't come.
        var reachable = KeeperReachable(sim, pawn, hasKeeper ? keeper : Entity.Null);
        if (hasKeeper && (needs.Get(food) < food.SeekBelow || needs.Get(bond) < bond.SeekBelow) && !reachable(tile) && !w.Has<Mount>(pawn))
            return new FollowJob(keeper, 300);

        if (needs.Get(rest) < rest.UrgentBelow || (AiQueries.IsNight(sim) && needs.Get(rest) < 0.6f))
        {
            // A full belly before a night's sleep (unless dead on its feet): a big dragon wakes up starving otherwise.
            if (needs.Get(rest) >= rest.UrgentBelow && needs.Get(food) < SupperBelow && Meal() is { } supper)
                return supper;
            var nest = AiQueries.FindFreeBed(sim, pawn, nest: true);
            if (!nest.IsNull)
                return new SleepJob(nest);
            var warm = AiQueries.FindTemperatureSpot(sim, pawn, tile, tol.ComfortMin, tol.ComfortMax, 20, reachable);
            return new SleepJob(Entity.Null, warm);
        }

        // A hungry dragon eats before it works: an adult's working day would otherwise run it down.
        if (needs.Get(food) < food.SeekBelow && Meal() is { } snack)
            return snack;

        // Told to stay (the Stay trick): it waits there, needs aside, until called.
        if (care.StayAt is { } stay)
            return new WanderJob(stay, 600);

        // Work for the village (what its life stage allows, see the Work tab) comes before comfort,
        // if it can be bothered: each time there's work it rolls its obedience (better at work it's
        // been trained for). Not on a night cold enough to hurt, though: out in that, a dragon goes
        // home instead. A sulking dragon won't lift a claw.
        if (!care.Sulking && creature.Faction == Faction.Village && temp >= tol.SafeMin + ChillMargin && _work.TryGiveJob(sim, pawn) is { } work
            && sim.Rand.Chance(DragonSystem.Obedience(sim, pawn, _work.LastType)))
            return work;

        if (temp < tol.ComfortMin && HumanBrain.SeekComfort(sim, pawn, tol, temp, accept: reachable) is { } bask)
            return bask;

        if (hasKeeper && needs.Get(bond) < bond.SeekBelow && !care.Sulking)
            return new FollowJob(keeper, 600);

        // Sulking keeps to itself, away from its keeper.
        var home = hasKeeper && !care.Sulking ? sim.LocationOf(keeper) : tile;
        if (stage.SoarChance > 0 && sim.Rand.Chance(stage.SoarChance)
            && Navigation.SoaringRoute(sim, pawn, home, sim.Rand.Range(8, 16)) is { } soar)
            return new SoarJob(soar);
        // Dragons that can fly like a high vantage point now and then.
        if (Navigation.FlyingStage(sim, pawn) != null && sim.Rand.Chance(0.25f) && AiQueries.FindPerch(sim, pawn, home, 14, reachable) is { } perch)
            return new WanderJob(perch, sim.Rand.Range(300, 900));
        return AiQueries.RandomReachableNear(sim, pawn, home, 5) is { } wander
            ? new WanderJob(wander, sim.Rand.Range(120, 400))
            : null;
    }

    /// <summary>
    /// Cells a dragon's keeper could walk up to (to feed it, play, ride): where a kept dragon
    /// rests, basks and perches. Everything, for a dragon with no keeper.
    /// </summary>
    private static Func<int3, bool> KeeperReachable(Simulation sim, Entity dragon, Entity keeper)
    {
        if (keeper.IsNull)
            return _ => true;
        var from = sim.LocationOf(keeper);
        return c => Navigation.CanReach(sim, keeper, from, c, PathEnd.Touch);
    }

    /// <summary>Food below which a dragon eats (or hunts) before bed: a night's sleep costs about half the bar.</summary>
    public const float SupperBelow = 0.7f;

    /// <summary>Degrees above its safe minimum below which a dragon won't take on work.</summary>
    public const float ChillMargin = 6;

    /// <summary>A warm spot around the dragon's nest, else its keeper, for when there's none nearby.</summary>
    private static Job? WarmthAtHome(Simulation sim, Entity pawn, Entity keeper)
    {
        var tol = sim.World.Get<Creature>(pawn).Tolerance(sim.Clock.Tick);
        var nest = AiQueries.FindFreeBed(sim, pawn, nest: true);
        var home = !nest.IsNull ? sim.World.Get<Thing>(nest).Cell : !keeper.IsNull ? sim.LocationOf(keeper) : (int3?)null;
        if (home is not { } center)
            return null;
        var spot = AiQueries.FindTemperatureSpot(sim, pawn, center, tol.ComfortMin, tol.ComfortMax, 12);
        return spot is { } t && t != sim.World.Get<Position>(pawn).Cell ? new SeekTemperatureJob(t, 1200, warmer: true, urgent: true) : null;
    }
}
