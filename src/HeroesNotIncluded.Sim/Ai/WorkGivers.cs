using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Ai;

/// <summary>
/// Finds work for a villager, honoring their per-work-type priorities (1 = highest, 0 = disabled).
/// Within the same priority, work types are tried in <see cref="WorkType"/> enum order.
/// </summary>
public sealed class WorkGivers
{
    private static readonly WorkType[] Order = Enum.GetValues<WorkType>();

    /// <summary>The work type of the last job <see cref="TryGiveJob"/> gave (a dragon's obedience depends on it).</summary>
    public WorkType LastType { get; private set; }

    public Job? TryGiveJob(Simulation sim, Entity pawn)
    {
        var mind = sim.World.Get<Mind>(pawn);
        var creature = sim.World.Get<Creature>(pawn);
        var stage = creature.Stage(sim.Clock.Tick);
        // A broker's trade is its own work, before any other.
        if (Expeditions.BrokerJob.For(sim, pawn) is { } brokering)
            return brokering;
        for (var priority = 1; priority <= 4; priority++)
        {
            foreach (var type in Order)
            {
                if (mind.WorkPriority[(int)type] != priority || !creature.Def.CanDo(type, stage))
                    continue;
                var job = type switch
                {
                    WorkType.Firefight => Firefight(sim, pawn),
                    WorkType.Doctor => TendCritical(sim, pawn) ?? Rescue(sim, pawn) ?? TendWounds(sim, pawn) ?? FeedPatient(sim, pawn) ?? Shops.HealOrders.For(sim, pawn) ?? Capture(sim, pawn) ?? TendWounds(sim, pawn, enemies: true),
                    WorkType.Warden => FeedPrisoner(sim, pawn) ?? VisitPrisoner(sim, pawn),
                    WorkType.Refuel => Refuel(sim, pawn),
                    WorkType.Construct => Shops.SellJob.For(sim, pawn) ?? Construct(sim, pawn) ?? Deconstruct(sim, pawn),
                    WorkType.Craft => Shops.SellJob.For(sim, pawn) ?? Shops.MendJob.For(sim, pawn) ?? Tinkering.OpenBoxJob.For(sim, pawn) ?? Tinkering.AppraiseJob.For(sim, pawn) ?? Tinkering.WagonJob.For(sim, pawn) ?? Tinkering.MendBreachJob.For(sim, pawn) ?? Craft(sim, pawn, type), // a sale is quick, a mend is not
                    WorkType.Cook => Shops.ServeJob.For(sim, pawn) ?? Shops.LodgeJob.For(sim, pawn) ?? Shops.SellJob.For(sim, pawn) ?? Shops.TapJob.For(sim, pawn) ?? Shops.WashJob.For(sim, pawn) ?? Shops.CookPotJob.For(sim, pawn) ?? Shops.MakeBedJob.For(sim, pawn) ?? Shops.CleanJob.For(sim, pawn) ?? Craft(sim, pawn, type),
                    WorkType.Hunt => Butcher(sim, pawn) ?? Hunt(sim, pawn),
                    WorkType.Fish => Fish(sim, pawn),
                    WorkType.Mine => Harvest(sim, pawn, type) ?? Dig(sim, pawn),
                    WorkType.Chop or WorkType.Harvest => Harvest(sim, pawn, type),
                    WorkType.Haul => Favours.ErrandJob.For(sim, pawn) ?? Haul(sim, pawn),
                    _ => null,
                };
                if (job != null)
                {
                    LastType = type;
                    return job;
                }
            }
        }
        return null;
    }

    private static int Dist(int3 a, int3 b) => Map.Distance(a, b);

    // ---------------- Burning (dragons) ----------------

    // ---------------- Hunting ----------------

    /// <summary>
    /// The nearest animal carcass nobody's cutting up yet: meat on the ground comes before meat on
    /// the hoof. With a butcher spot in reach, one lying elsewhere is carried there first.
    /// </summary>
    private static Job? Butcher(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var from = w.Get<Position>(pawn).Cell;
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        foreach (var e in w.Each<Creature>())
        {
            var c = w.Get<Creature>(e);
            if (!c.Dead || c.Def.Animal == null || w.Has<Carried>(e) || sim.Reservations.IsReservedByOther(e, pawn))
                continue;
            var at = w.Get<Position>(e).Cell;
            var d = Dist(from, at);
            if (d < bestDist && sim.Map.Fire[sim.Map.Idx(at)] <= 0 && Navigation.CanReach(sim, pawn, from, at, PathEnd.Touch))
            {
                best = e;
                bestDist = d;
            }
        }
        if (best.IsNull)
            return null;
        var lies = w.Get<Position>(best).Cell;
        if (ButcherSpots.SpotAt(sim, lies).IsNull && ButcherSpots.CanCarry(sim, pawn, best)
            && ButcherSpots.Nearest(sim, pawn, lies) is { IsNull: false } spot)
            return new ButcherJob(best, spot);
        return new ButcherJob(best);
    }

    /// <summary>The nearest free fishing spot, preferring well-stocked water.</summary>
    private static Job? Fish(Simulation sim, Entity pawn)
    {
        var map = sim.Map;
        var from = sim.World.Get<Position>(pawn).Cell;
        int3? best = null;
        var bestScore = float.MaxValue;
        List<int>? stale = null;
        foreach (var i in map.Designated(Designation.Fish))
        {
            var cell = map.CellOf(i);
            if (!Systems.Fishing.CanFishFrom(sim, cell))
            {
                (stale ??= []).Add(i); // the bank was dug away or built on
                continue;
            }
            // Fished-out water counts as further away.
            var score = Dist(from, cell) + 40f * (1f - sim.Wildlife.FishStock(sim, Systems.Fishing.PatchOf(sim, cell)));
            if (score >= bestScore || sim.Reservations.IsTileReservedByOther(cell, pawn) || !sim.CanAcceptItem(cell, sim.Defs.Thing("RawFish"), out _))
                continue;
            if (!Navigation.CanReach(sim, pawn, from, cell, PathEnd.OnTile))
                continue;
            best = cell;
            bestScore = score;
        }
        ClearStale(map, stale, Designation.Fish);
        return best is { } b ? new FishJob(b) : null;
    }

    /// <summary>The nearest animal marked for hunting, for someone with a bow.</summary>
    private static Job? Hunt(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (HuntJob.RangedAttack(sim, pawn) == null)
            return null;
        var from = w.Get<Position>(pawn).Cell;
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        foreach (var e in w.Each<HuntOrder>())
        {
            if (!w.Get<Creature>(e).Able || sim.Reservations.IsReservedByOther(e, pawn))
                continue;
            var at = w.Get<Position>(e).Cell;
            var d = Dist(from, at);
            if (d < bestDist && Navigation.CanReach(sim, pawn, from, at, PathEnd.Touch))
            {
                best = e;
                bestDist = d;
            }
        }
        return best.IsNull ? null : new HuntJob(best);
    }

    // ---------------- Firefighting ----------------

    private static Job? Firefight(Simulation sim, Entity pawn)
    {
        var map = sim.Map;
        var from = sim.World.Get<Position>(pawn).Cell;
        int3? best = null;
        var bestDist = int.MaxValue;
        foreach (var i in sim.Fire.BurningTiles)
        {
            var t = map.CellOf(i);
            var d = Dist(from, t);
            if (d > 40 || d >= bestDist || sim.Reservations.IsTileReservedByOther(t, pawn))
                continue;
            if ((map.Designations[i] & Designation.Burn) != 0)
                continue; // a controlled burn: only what spreads beyond it is fought
            if (!sim.Regions.CanReach(from, t, PathEnd.Touch))
                continue;
            best = t;
            bestDist = d;
        }
        return best is { } b ? new FightFireJob(b) : null;
    }

    // ---------------- Doctoring ----------------

    /// <summary>
    /// The nearest creature (not us) matching a predicate, unreserved and reachable: one of ours
    /// (villagers and prisoners), or with <paramref name="enemies"/> a downed raider once the
    /// fighting is over.
    /// </summary>
    /// <remarks>
    /// With <paramref name="triage"/>, whoever will bleed out soonest comes first (walking anywhere
    /// on the map takes minutes; bleeding out takes hours), then the nearest.
    /// </remarks>
    private static Entity NearestPatient(Simulation sim, Entity pawn, Func<Entity, Creature, bool> match, bool enemies = false, bool triage = false)
    {
        var w = sim.World;
        var from = w.Get<Position>(pawn).Cell;
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        var bestHours = float.MaxValue;
        foreach (var e in sim.Creatures())
        {
            var c = w.Get<Creature>(e);
            var ours = enemies ? c.Faction == Faction.Raider && c.Downed : c.Faction is Faction.Village or Faction.Prisoner;
            if (e == pawn || !ours || !match(e, c) || sim.Reservations.IsReservedByOther(e, pawn))
                continue;
            var at = sim.LocationOf(e);
            var d = Dist(from, at);
            var hours = triage ? HoursToBleedOut(w, e) : 0f;
            if (hours > bestHours || (hours == bestHours && d >= bestDist) || !sim.Regions.CanReach(from, at, PathEnd.Touch))
                continue;
            best = e;
            bestDist = d;
            bestHours = hours;
        }
        return best;
    }

    /// <summary>Game hours of blood left at the current (untended) bleeding, rounded to the half hour so near-ties go to the nearest.</summary>
    private static float HoursToBleedOut(World w, Entity e)
    {
        if (!w.TryGet<Health>(e, out var h) || h.BleedPerHour <= 1e-4f)
            return float.MaxValue / 2;
        return MathF.Round(h.Blood / h.BleedPerHour * 2f) / 2f;
    }

    /// <summary>A downed villager lying outside a bed goes to the nearest free one. (Dragons are too heavy.)</summary>
    private static Job? Rescue(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var patient = NearestPatient(sim, pawn, (e, c) =>
            c.Downed && Rescues.CanCarry(sim, pawn, e) && !w.Has<Carried>(e) && !Systems.HealthSystem.InBed(sim, e));
        if (patient.IsNull)
            return null;
        var bed = Rescues.BedFor(sim, pawn, patient);
        return bed.IsNull || sim.Reservations.IsReservedByOther(bed, pawn) ? null : new RescueJob(patient, bed);
    }

    /// <summary>
    /// A downed villager or dragon who can't feed themselves gets something they can eat brought to
    /// them where they lie: an adult dragon too big to carry to its nest goes hungry otherwise.
    /// </summary>
    private static Job? FeedPatient(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var food = sim.Defs.Need("Food");
        var patient = NearestPatient(sim, pawn, (e, c) =>
            c.Downed && c.Faction == Faction.Village && !w.Has<Carried>(e) && w.TryGet<Needs>(e, out var n) && n.Has[food.Index] && n.Get(food) < food.SeekBelow);
        if (patient.IsNull)
            return null;
        var eater = w.Get<Creature>(patient).Def;
        var meal = AiQueries.FindNearestItem(sim, pawn, (e, d) => d.EdibleBy(eater) && !Systems.FoodPoisoning.IsSpoiled(sim, e));
        if (meal.IsNull)
            meal = AiQueries.FindNearestItem(sim, pawn, (_, d) => d.EdibleBy(eater));
        return meal.IsNull ? null : new FeedPrisonerJob(patient, meal, byWarden: false);
    }

    /// <summary>
    /// Bandage whoever has untended wounds; the downed (who can't wait) first, the one bleeding
    /// out soonest before the nearest. With
    /// <paramref name="enemies"/>, stabilize downed raiders where they lie, so they can walk off
    /// rather than bleed out.
    /// </summary>
    private static Job? TendWounds(Simulation sim, Entity pawn, bool enemies = false)
    {
        var w = sim.World;
        var now = sim.Clock.Tick;
        bool Untended(Entity e) => w.TryGet<Health>(e, out var h) && h.Injuries.Exists(i => i.NeedsTending(now));
        var patient = NearestPatient(sim, pawn, (e, c) => c.Downed && !w.Has<Carried>(e) && Untended(e), enemies, triage: true);
        if (patient.IsNull && !enemies)
            patient = NearestPatient(sim, pawn, (e, c) => !w.Has<Carried>(e) && Untended(e));
        return patient.IsNull ? null : new TendJob(patient);
    }

    /// <summary>Bleeding out within this many game hours, a downed patient is bandaged before anything else.</summary>
    public const float CriticalHours = 3f;

    /// <summary>
    /// Whoever's lying downed and about to bleed out (<see cref="CriticalHours"/>), the soonest first:
    /// villagers, or raiders once the fight's over. A doctor otherwise sees to every villager's
    /// scratches first, and a raider who could have walked off dies where he fell.
    /// </summary>
    private static Job? TendCritical(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var now = sim.Clock.Tick;
        bool Critical(Entity e, Creature c) => c.Downed && !w.Has<Carried>(e) && w.TryGet<Health>(e, out var h)
            && h.Injuries.Exists(i => i.NeedsTending(now)) && HoursToBleedOut(w, e) <= CriticalHours;
        var ours = NearestPatient(sim, pawn, Critical, triage: true);
        var theirs = NearestPatient(sim, pawn, Critical, enemies: true, triage: true);
        var patient = theirs.IsNull || (!ours.IsNull && HoursToBleedOut(w, ours) <= HoursToBleedOut(w, theirs)) ? ours : theirs;
        return patient.IsNull ? null : new TendJob(patient);
    }

    /// <summary>Carry a downed raider the player marked for capture to a free prisoner bed.</summary>
    private static Job? Capture(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var captive = NearestPatient(sim, pawn, (e, _) => w.Has<CaptureOrder>(e) && !w.Has<Carried>(e) && Systems.Prisoners.Capturable(sim, e), enemies: true);
        if (captive.IsNull)
            return null;
        var bed = AiQueries.FindFreeBed(sim, captive, prisoner: true);
        return bed.IsNull || sim.Reservations.IsReservedByOther(bed, pawn) ? null : new CaptureJob(captive, bed);
    }

    // ---------------- Wardens ----------------

    /// <summary>The nearest unreserved, reachable, up-and-about-or-not prisoner matching a predicate.</summary>
    private static Entity NearestPrisoner(Simulation sim, Entity pawn, Func<Entity, Prisoner, bool> match)
    {
        var w = sim.World;
        var from = w.Get<Position>(pawn).Cell;
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        foreach (var e in w.Query<Prisoner>())
        {
            if (w.Get<Creature>(e).Dead || w.Has<Carried>(e) || !match(e, w.Get<Prisoner>(e)) || sim.Reservations.IsReservedByOther(e, pawn))
                continue;
            var at = sim.LocationOf(e);
            var d = Dist(from, at);
            if (d >= bestDist || !sim.Regions.CanReach(from, at, PathEnd.Touch))
                continue;
            best = e;
            bestDist = d;
        }
        return best;
    }

    /// <summary>A hungry prisoner gets a meal: plant food if there is any (the meat is the dragon's).</summary>
    private static Job? FeedPrisoner(Simulation sim, Entity pawn)
    {
        var food = sim.Defs.Need("Food");
        var prisoner = NearestPrisoner(sim, pawn, (e, _) => sim.World.Get<Needs>(e).Get(food) < food.SeekBelow);
        if (prisoner.IsNull)
            return null;
        var meal = AiQueries.FindNearestItem(sim, pawn, (_, d) => d.IsFood && !d.IsMeat);
        if (meal.IsNull)
            meal = AiQueries.FindNearestItem(sim, pawn, (_, d) => d.IsFood);
        return meal.IsNull ? null : new FeedPrisonerJob(prisoner, meal);
    }

    /// <summary>Let go those marked for release; talk daily with those being recruited.</summary>
    private static Job? VisitPrisoner(Simulation sim, Entity pawn)
    {
        var now = sim.Clock.Tick;
        var prisoner = NearestPrisoner(sim, pawn, (_, p) =>
            p.Mode == PrisonerMode.Release || (p.Mode == PrisonerMode.Recruit && now - p.LastVisit >= SimClock.TicksPerDay));
        return prisoner.IsNull ? null : new WardenVisitJob(prisoner);
    }

    // ---------------- Tending eggs and dragons ----------------

    // ---------------- Construction ----------------

    private static Job? Construct(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var from = w.Get<Position>(pawn).Cell;
        var sites = new List<(Entity Site, int Dist)>();
        foreach (var site in w.Query<Construction>())
        {
            // A preset building's phase is raised by many at once (Buildings.MaxBuilders), so it isn't reserved.
            if (!Buildings.IsPhaseSite(sim, site) && sim.Reservations.IsReservedByOther(site, pawn))
                continue;
            var tile = w.Get<Thing>(site).Cell;
            if (sim.Map.Fire[sim.Map.Idx(tile)] > 0)
                continue;
            if (!sim.CanBuildNow(site))
                continue; // waiting for the floor or wall that will hold it up
            if (!sim.Regions.CanReach(from, tile, PathEnd.Touch))
                continue;
            // Don't wall in someone standing on the site.
            if (w.Get<Construction>(site).MaterialsComplete && w.Get<Construction>(site).Target.Impassable && Occupied(sim, tile, pawn))
                continue;
            sites.Add((site, Dist(from, tile)));
        }
        // Nearest first (stable, so ties keep query order), skipping sites whose material isn't
        // around yet: a nest waiting for stone mustn't hold up the campfire next to it.
        sites.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        Dictionary<ThingDef, Entity>? found = null;
        foreach (var (site, _) in sites)
        {
            var c = w.Get<Construction>(site);
            if (Buildings.IsPhaseSite(sim, site))
            {
                // Building keeps pace with what's been brought; a full crew leaves the rest to carry.
                if (Buildings.HasWork(c) && Buildings.Builders(sim, site, pawn) < Buildings.MaxBuilders)
                    return new BuildJob(site);
                if (c.MaterialsComplete)
                    continue;
            }
            else if (c.MaterialsComplete)
                return new BuildJob(site);

            // Deliver the first missing material. Several pawns may deliver to one site, so count
            // what's already on the way via reservations on the material stacks (approximation).
            var need = c.NextNeededMaterial(out var remaining);
            var def = c.Target.Building!.Cost[need].ThingDef;
            found ??= [];
            if (!found.TryGetValue(def, out var material))
                found[def] = material = AiQueries.FindNearestItem(sim, pawn, (_, d) => d == def);
            if (!material.IsNull)
                // With a pack, the whole remaining need in one trip (DeliverJob gathers it).
                return new DeliverJob(site, material, Inventory.WorkPack(sim, pawn).IsNull ? Math.Min(remaining, def.StackLimit) : remaining);
        }
        return null;
    }

    // ---------------- Stoking (dragons) ----------------

    // ---------------- The other elements' breath (dragons: ElementDef.Work) ----------------

    // ---------------- Crafting and cooking ----------------

    /// <summary>
    /// The nearest station with a bill of this kind of work that wants making, that the pawn is
    /// skilled enough for, with its ingredients to be had (and the heat it needs).
    /// </summary>
    private static Job? Craft(Simulation sim, Entity pawn, WorkType type)
    {
        var w = sim.World;
        var from = w.Get<Position>(pawn).Cell;
        var best = Entity.Null;
        RecipeDef? bestRecipe = null;
        var bestDist = int.MaxValue;
        Job? fetchTool = null;
        foreach (var station in w.Query<Workbench>())
        {
            var bench = w.Get<Workbench>(station);
            if (bench.Bills.Count == 0 || !Crafting.IsStation(sim, station) || sim.Reservations.IsReservedByOther(station, pawn))
                continue;
            var cell = w.Get<Thing>(station).Cell;
            var d = Dist(from, cell);
            if (d >= bestDist || sim.Map.Fire[sim.Map.Idx(cell)] > 0 || !sim.Regions.CanReach(from, cell, PathEnd.Touch))
                continue;
            if (Crafting.NextForVillager(sim, station, pawn, type) is not { } recipe || !Crafting.CanGetHotEnough(sim, station, recipe))
                continue;
            if (Crafting.MissingTool(sim, pawn, station, recipe) is { } kind)
            {
                // The tool it wants is somewhere else (a fresh copper hammer hauled to the stockpile):
                // fetch it onto the belt first, if the ingredients are there to make it worth it.
                fetchTool ??= FetchToolFor(sim, pawn, kind, Math.Max(1, recipe.ToolTier), () => Crafting.HasIngredients(sim, pawn, station, recipe));
                continue;
            }
            if (!Crafting.HasIngredients(sim, pawn, station, recipe))
                continue;
            (best, bestRecipe, bestDist) = (station, recipe, d);
        }
        return bestRecipe == null ? fetchTool : new CraftJob(best, bestRecipe);
    }

    /// <summary>A job to fetch a tool of <paramref name="kind"/> and at least <paramref name="tier"/> onto the belt, or null if there's none (or no room).</summary>
    private static Job? FetchToolFor(Simulation sim, Entity pawn, string kind, int tier, Func<bool> worthIt)
    {
        if (!Toolbelt.HasRoom(sim, pawn) && Toolbelt.Best(sim, pawn, kind).IsNull)
            return null;
        if (!worthIt())
            return null;
        var tool = AiQueries.FindNearestItem(sim, pawn, (_, d) => d.Tool is { } t && t.Kind == kind && t.Tier >= tier, 60);
        return tool.IsNull ? null : new FetchToolJob(tool);
    }

    private static Job? Deconstruct(Simulation sim, Entity pawn)
    {
        var map = sim.Map;
        var from = sim.World.Get<Position>(pawn).Cell;
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        List<int>? stale = null;
        foreach (var i in map.Designated(Designation.Deconstruct))
        {
            var cell = map.CellOf(i);
            var target = sim.DeconstructTarget(cell);
            if (target.IsNull)
            {
                (stale ??= []).Add(i); // already gone
                continue;
            }
            var d = Dist(from, cell);
            if (d >= bestDist || sim.Reservations.IsReservedByOther(target, pawn) || map.Fire[i] > 0)
                continue;
            if (!sim.CanDeconstruct(target, out _) || !sim.Regions.CanReach(from, cell, PathEnd.Touch))
                continue; // waits until what rests on it is gone
            best = target;
            bestDist = d;
        }
        ClearStale(map, stale, Designation.Deconstruct);
        return best.IsNull ? null : new DeconstructJob(best);
    }

    private static bool Occupied(Simulation sim, int3 tile, Entity except)
    {
        var w = sim.World;
        foreach (var e in w.Each<Creature>())
            if (e != except && !w.Get<Creature>(e).Dead && w.Get<Position>(e).Cell == tile)
                return true;
        return false;
    }

    // ---------------- Chop / mine / harvest ----------------

    private static Job? Harvest(Simulation sim, Entity pawn, WorkType type)
    {
        var map = sim.Map;
        var flag = type switch
        {
            WorkType.Chop => Designation.Chop,
            WorkType.Mine => Designation.Mine,
            _ => Designation.Harvest,
        };
        var from = sim.World.Get<Position>(pawn).Cell;
        Entity best = Entity.Null;
        var bestDist = int.MaxValue;
        List<int>? stale = null;
        foreach (var i in map.Designated(flag))
        {
            var e = map.Structure[i];
            if (e.IsNull)
            {
                (stale ??= []).Add(i); // stale designation
                continue;
            }
            var thing = sim.World.Get<Thing>(e);
            if (thing.Def.Harvest?.WorkType != type || sim.Reservations.IsReservedByOther(e, pawn))
                continue;
            if (sim.World.TryGet<PlantState>(e, out var plant) && !plant.Ready)
                continue;
            var d = Dist(from, thing.Cell);
            if (d >= bestDist || map.Fire[i] > 0)
                continue;
            if (!sim.Regions.CanReach(from, thing.Cell, PathEnd.Touch))
                continue;
            best = e;
            bestDist = d;
        }
        ClearStale(map, stale, flag);
        return best.IsNull ? null : new HarvestJob(best);
    }

    private static Job? Dig(Simulation sim, Entity pawn)
    {
        var map = sim.Map;
        var from = sim.World.Get<Position>(pawn).Cell;
        int3? best = null;
        var bestDist = int.MaxValue;
        List<int>? stale = null;
        foreach (var i in map.Designated(Designation.Dig))
        {
            var cell = map.CellOf(i);
            if (!sim.CanDig(cell))
            {
                (stale ??= []).Add(i); // dug already, or something was built on top
                continue;
            }
            var d = Dist(from, cell);
            if (d >= bestDist || sim.Reservations.IsTileReservedByOther(cell, pawn))
                continue;
            if (!Mining.CanBreak(sim, pawn, map.Block[i]))
                continue; // stays marked for someone with a better pick
            if (!sim.Regions.CanReach(from, cell, PathEnd.Touch))
                continue;
            best = cell;
            bestDist = d;
        }
        ClearStale(map, stale, Designation.Dig);
        return best is { } b ? new DigJob(b) : null;
    }

    /// <summary>Drop orders found to be stale during a search (they can't be cleared mid-iteration).</summary>
    internal static void ClearStale(Map map, List<int>? stale, Designation flag)
    {
        if (stale == null)
            return;
        foreach (var i in stale)
            map.ClearDesignation(i, flag);
    }

    // ---------------- Hauling & refueling ----------------

    private static Job? Refuel(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        foreach (var e in w.Query<Fueled>())
        {
            var thing = w.Get<Thing>(e);
            var props = thing.Def.Building!.Fuel!;
            var fueled = w.Get<Fueled>(e);
            if (sim.Reservations.IsReservedByOther(e, pawn))
                continue;
            if (props.OnDemand && !Crafting.WantsFiring(sim, e))
                continue; // a cold kiln with nothing to make stays cold
            // A kiln whose fuel can't reach what's wanted (bronze needs more than a wood fire) gets a
            // hotter one put in, however full it is; otherwise only when it runs low.
            var wanted = props.OnDemand ? Crafting.HeatToMake(sim, e, pawn) : 0f;
            var loaded = fueled.Burn(props);
            var upgrade = wanted > 0 && loaded.Reach > 0 && loaded.Reach < wanted;
            if (!upgrade && fueled.Fuel > props.Capacity * props.RefuelAt)
                continue;
            if (!sim.Regions.CanReach(w.Get<Position>(pawn).Cell, thing.Cell, PathEnd.Touch))
                continue;
            foreach (var option in FuelOrder(props, wanted))
            {
                if (upgrade && option.Reach <= loaded.Reach)
                    continue;
                var fuel = AiQueries.FindNearestItem(sim, pawn, (_, d) => d == option.ThingDef);
                if (!fuel.IsNull)
                    return new RefuelJob(e, fuel);
            }
        }
        return null;
    }

    /// <summary>
    /// Fuels to fetch, best first: those that reach <paramref name="wanted"/> in def order (the main
    /// fuel first, so a kiln doesn't burn the charcoal it bakes while wood will do), then the rest,
    /// hottest first.
    /// </summary>
    private static IEnumerable<FuelOption> FuelOrder(FuelProps props, float wanted)
    {
        foreach (var o in props.Options)
            if (o.Reach <= 0 || o.Reach >= wanted)
                yield return o;
        foreach (var o in props.Options.Where(o => o.Reach > 0 && o.Reach < wanted).OrderByDescending(o => o.Reach))
            yield return o;
    }

    private static Job? Haul(Simulation sim, Entity pawn)
    {
        var w = sim.World;

        // Move loose items into stockpiles. Eggs are tended, not hauled; the inn's crockery is the
        // innkeeper's to clear. Only items some stockpile cell can take: a full stockpile for the
        // nearest thing mustn't stop the rest being hauled.
        Dictionary<(ThingDef, MaterialDef?), bool>? room = null;
        bool Storable(Entity e, ThingDef d)
        {
            var key = (d, w.Get<Thing>(e).Material);
            room ??= [];
            if (!room.TryGetValue(key, out var any))
                room[key] = any = AiQueries.HasStockpileRoom(sim, pawn, d, key.Item2);
            return any;
        }
        var item = AiQueries.FindNearestItem(
            sim,
            pawn,
            (e, d) => !w.Has<Stored>(e) && !d.Vessel && d.WashesInto == null && !AiQueries.InStockpile(sim, w.Get<Thing>(e).Cell) && Storable(e, d)
        );
        if (item.IsNull)
            return null;
        var thing = w.Get<Thing>(item);
        if (AiQueries.FindStockpileTile(sim, pawn, thing.Def, thing.Cell, thing.Material) is not { } dest)
            return null;
        var stacks = w.Get<Creature>(pawn).Stage(sim.Clock.Tick).CarryStacks;
        if (!Inventory.WorkPack(sim, pawn).IsNull)
            return new BulkHaulJob(item, int.MaxValue); // as much as hands and pack hold
        return stacks > 1 ? new BulkHaulJob(item, stacks) : new HaulJob(item, dest);
    }
}
