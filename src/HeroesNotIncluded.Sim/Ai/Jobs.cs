using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Environment;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Ai;

/// <summary>Idle: stroll somewhere nearby and loiter.</summary>
public sealed class WanderJob(int3 target, int waitTicks) : Job
{
    public override bool Interruptible => true;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Wandering";
        foreach (var s in GoTo(target, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        Report = "Idling";
        foreach (var s in Wait(waitTicks)) yield return s;
    }
}

/// <summary>A wild animal ambles somewhere near its herd and grazes there.</summary>
public sealed class GrazeJob(int3 target, int grazeTicks) : Job
{
    public override bool Interruptible => true;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Wandering";
        foreach (var s in GoTo(target, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        Report = "Grazing";
        foreach (var s in Wait(grazeTicks)) yield return s;
    }
}

/// <summary>A wild animal runs from a threat.</summary>
public sealed class FleeJob(int3 target) : Job
{
    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Fleeing";
        foreach (var s in GoTo(target, PathEnd.OnTile)) yield return s;
    }
}

/// <summary>An idle flyer circles overhead and lands where it took off (<see cref="Navigation.SoaringRoute"/>).</summary>
public sealed class SoarJob(List<int3> route) : Job
{
    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Soaring";
        var mover = Get<Mover>(Pawn);
        mover.Path = route;
        mover.PathIndex = 0;
        mover.StepProgress = 0;
        mover.StepFrom = PawnCell;
        while (mover.Moving)
            yield return JobStatus.Running;
    }
}

/// <summary>Go to a warm (or cool) spot and stay there for a while. Urgent when escaping danger.</summary>
public sealed class SeekTemperatureJob(int3 target, int stayTicks, bool warmer, bool urgent = false) : Job
{
    public override bool Interruptible => !urgent;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = warmer ? "Seeking warmth" : "Cooling off";
        foreach (var s in GoTo(target, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        Report = warmer ? "Warming up" : "Cooling off";
        foreach (var s in Wait(stayTicks)) yield return s;
    }
}

/// <summary>Walk to a food stack and eat from it.</summary>
public sealed class EatJob(Entity food) : Job
{
    /// <summary>The stack it's going to eat from.</summary>
    public Entity Food => food;

    protected override IEnumerable<JobStatus> Run()
    {
        var def = Get<Thing>(food).Def;
        Report = $"Going to eat {def.Label}";
        if (!Reserve(food)) yield break;
        foreach (var s in GoTo(Get<Thing>(food).Cell, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;

        Report = $"Eating {def.Label}";
        foreach (var s in Wait(90)) yield return s;
        if (!Sim.IsAvailable(food))
        {
            Fail("food disappeared");
            yield break;
        }

        var count = Math.Min(Wanted(Sim, Pawn, def), Get<Stack>(food).Count);
        var eaten = Sim.TakeFromStack(food, count);
        var rot = FoodPoisoning.RotOf(Sim, eaten);
        Sim.World.Destroy(eaten);
        Nourish(Sim, Pawn, def, count, rot);
    }

    /// <summary>Units of a food it takes to fill a creature up (at least one).</summary>
    internal static int Wanted(Simulation sim, Entity eater, ThingDef food)
    {
        var hungerFactor = sim.World.Get<Creature>(eater).Stage(sim.Clock.Tick).HungerFactor;
        var missing = 1f - sim.World.Get<Needs>(eater).Get(sim.Defs.Need("Food"));
        return Math.Max(1, (int)MathF.Ceiling(missing * hungerFactor / food.Nutrition));
    }

    /// <summary>
    /// A creature eats <paramref name="count"/> units of a food. Bigger life stages have bigger
    /// stomachs: each unit fills less of the bar, which drains at the same pace (so a fed adult
    /// still sleeps through the night), and they eat <see cref="LifeStage.HungerFactor"/> times as much.
    /// Food past <see cref="FoodPoisoning.SpoiledAt"/> <paramref name="rot"/> may make it ill.
    /// </summary>
    internal static void Nourish(Simulation sim, Entity eater, ThingDef food, int count, float rot = 0f)
    {
        var hungerFactor = sim.World.Get<Creature>(eater).Stage(sim.Clock.Tick).HungerFactor;
        sim.World.Get<Needs>(eater).Add(sim.Defs.Need("Food"), count * food.Nutrition / hungerFactor);
        if (food.IsMeat)
            MoodSystem.Remember(sim, eater, sim.Defs.Thoughts.AteRawMeat);
        else if (food.Cooked)
            MoodSystem.Remember(sim, eater, sim.Defs.Thoughts.AteCookedMeal);
        // Eating in a hall that's made for it (a feast in the longhouse).
        if (Buildings.At(sim, sim.World.Get<Position>(eater).Cell) is { IsNull: false } hall
            && sim.World.Get<PresetBuilding>(hall) is { Phase: BuildPhase.Done, Def.EatThoughtDef: { } feast })
            MoodSystem.Remember(sim, eater, feast);
        FoodPoisoning.AfterEating(sim, eater, rot);
    }
}

/// <summary>Sleep in a bed/nest if given, else at <paramref name="spot"/> if given, else where we stand.</summary>
public sealed class SleepJob(Entity bed, int3? spot = null) : Job
{
    public override Entity Focus => bed;

    protected override IEnumerable<JobStatus> Run()
    {
        if (!bed.IsNull)
        {
            Report = "Going to bed";
            if (!Reserve(bed)) yield break;
            foreach (var s in GoTo(Get<Thing>(bed).Cell, PathEnd.OnTile)) yield return s;
            if (HasFailed) yield break;
        }
        else if (spot is { } t)
        {
            Report = "Looking for a warm place to sleep";
            if (!ReserveTile(t)) yield break;
            foreach (var s in GoTo(t, PathEnd.OnTile)) yield return s;
            if (HasFailed) yield break;
        }

        var mind = Get<Mind>(Pawn);
        var needs = Get<Needs>(Pawn);
        var rest = Sim.Defs.Need("Rest");
        mind.Asleep = true;
        Report = bed.IsNull ? "Sleeping on the ground" : "Sleeping";
        var maxTicks = SimClock.HoursToTicks(10);
        var slept = 0;
        for (; slept < maxTicks && needs.Get(rest) < 0.99f; slept++)
            yield return JobStatus.Running;
        if (slept >= SimClock.HoursToTicks(2))
            RememberNight();
    }

    /// <summary>How the night went: on the ground or in a bed (a nest, for dragons), under the sky or a roof.</summary>
    private void RememberNight()
    {
        var known = Sim.Defs.Thoughts;
        if (bed.IsNull)
            MoodSystem.Remember(Sim, Pawn, known.SleptOnGround);
        else
            MoodSystem.Remember(Sim, Pawn, known.SleptInBed);
        if (Sim.Rooms.RoomAt(PawnCell) is not { Indoors: true })
            MoodSystem.Remember(Sim, Pawn, known.SleptOutdoors);
    }
}

/// <summary>Pick up a weapon or piece of apparel lying around and put it on.</summary>
public sealed class EquipJob(Entity item) : Job
{
    public override Entity Focus => item;

    protected override IEnumerable<JobStatus> Run()
    {
        var label = Get<Thing>(item).Label;
        Report = $"Fetching {label}";
        if (!Reserve(item)) yield break;
        foreach (var s in GoTo(Get<Thing>(item).Cell, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;

        Report = $"Putting on {label}";
        foreach (var s in Wait(60)) yield return s;
        if (!Sim.IsAvailable(item))
        {
            Fail("item disappeared");
            yield break;
        }
        var one = Sim.TakeFromStack(item, 1);
        if (Get<Thing>(one).Def.Apparel != null)
            Sim.WearItem(Pawn, one);
        else
            Sim.EquipItem(Pawn, one);
    }
}

/// <summary>
/// Fetch a tool for the belt (<see cref="Toolbelt"/>): a villager who chops, mines or fishes
/// picks up the best one about, leaving a worse one of the same kind where it stood.
/// </summary>
public sealed class FetchToolJob(Entity item) : Job
{
    public override Entity Focus => item;

    protected override IEnumerable<JobStatus> Run()
    {
        var def = Get<Thing>(item).Def;
        Report = $"Fetching a {def.Label}";
        if (!Reserve(item)) yield break;
        foreach (var s in GoTo(Get<Thing>(item).Cell, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        foreach (var s in Wait(45)) yield return s;
        if (!Sim.IsAvailable(item))
        {
            Fail("tool disappeared");
            yield break;
        }
        if (Toolbelt.Best(Sim, Pawn, def.Tool!.Kind) is { IsNull: false } old)
            Toolbelt.TakeOff(Sim, Pawn, old); // into the hands if they're free, else at the feet
        if (Get<Carrying>(Pawn).Item is { IsNull: false } held && Get<Thing>(held).Def.Tool?.Kind == def.Tool.Kind)
        {
            Get<Carrying>(Pawn).Item = Entity.Null;
            Sim.PlaceItemNear(held, PawnCell);
        }
        if (Toolbelt.PutOn(Sim, Pawn, item).IsNull)
            Fail("no room on the belt");
    }
}

/// <summary>Carry an item (or part of a stack) to a destination tile.</summary>
public sealed class HaulJob(Entity item, int3 destination, int maxCount = int.MaxValue) : Job
{
    protected override IEnumerable<JobStatus> Run()
    {
        var def = Get<Thing>(item).Def;
        var material = Get<Thing>(item).Material;
        Report = $"Hauling {Get<Thing>(item).Label}";
        if (!Reserve(item) || !ReserveTile(destination)) yield break;

        foreach (var s in GoTo(Get<Thing>(item).Cell, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        if (!Sim.IsAvailable(item))
        {
            Fail("item disappeared");
            yield break;
        }

        Sim.CanAcceptItem(destination, def, out var space, material);
        var count = Math.Min(Math.Min(maxCount, Get<Stack>(item).Count), Math.Max(space, 1));
        var carried = Sim.TakeFromStack(item, count);
        Get<Carrying>(Pawn).Item = carried;

        foreach (var s in GoTo(destination, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;

        Get<Carrying>(Pawn).Item = Entity.Null;
        Sim.PlaceItemNear(carried, destination);
    }
}

/// <summary>
/// A big hauler's trip: pick up a stack and more of the same kind lying nearby, then drop the load
/// into as many stockpile cells as it takes. A big creature carries up to <paramref name="stacks"/>
/// stacks in its arms; a person with a pack carries a stack in hand and as much more as the pack
/// holds (<see cref="Inventory.WorkPack"/>). Interrupted, the whole load goes down where it stands
/// (<see cref="AiSystem.ReleasePawn"/>).
/// </summary>
public sealed class BulkHaulJob(Entity item, int stacks) : Job
{
    /// <summary>How far from the first stack it looks for more to pick up.</summary>
    public const int GatherRadius = 6;

    public override Entity Focus => item;

    protected override IEnumerable<JobStatus> Run()
    {
        var def = Get<Thing>(item).Def;
        var material = Get<Thing>(item).Material;
        Report = $"Hauling {Get<Thing>(item).Label}";
        if (!Reserve(item)) yield break;

        // How many units this trip can take: a pack's spare weight on top of a stack in hand.
        var pack = Inventory.WorkPack(Sim, Pawn);
        var capacity = pack.IsNull ? int.MaxValue : def.StackLimit + Sim.ContainerRoom(pack, def, material);
        var sources = new List<Entity> { item };
        var near = Get<Thing>(item).Cell;
        var planned = Get<Stack>(item).Count;
        foreach (var e in Sim.SpawnedItems())
        {
            if (sources.Count >= stacks || planned >= capacity)
                break;
            var t = Get<Thing>(e);
            if (e == item || t.Def != def || t.Material != material || Maps.Map.Distance(t.Cell, near) > GatherRadius)
                continue;
            if (AiQueries.InStockpile(Sim, t.Cell) || Sim.Map.Fire[Sim.Map.Idx(t.Cell)] > 0 || Sim.Reservations.IsReservedByOther(e, Pawn))
                continue;
            if (!Navigation.CanReach(Sim, Pawn, near, t.Cell, PathEnd.OnTile) || !Sim.Reservations.TryReserve(e, Pawn))
                continue;
            sources.Add(e);
            planned += Get<Stack>(e).Count;
        }

        var load = Entity.Null;
        var picked = 0;
        foreach (var source in sources)
        {
            if (picked >= capacity)
                break;
            if (!Sim.IsAvailable(source))
                continue;
            foreach (var s in GoTo(Get<Thing>(source).Cell, PathEnd.OnTile)) yield return s;
            if (HasFailed)
                yield break; // anything picked up so far is put down here (ReleasePawn)
            if (!Sim.IsAvailable(source))
                continue;
            var taken = Sim.TakeFromStack(source, Math.Min(Get<Stack>(source).Count, capacity - picked));
            picked += Get<Stack>(taken).Count;
            if (load.IsNull)
            {
                load = taken;
                Get<Carrying>(Pawn).Item = load;
            }
            else
                Sim.MergeStacks(load, taken);
            // What's past a handful goes into the pack.
            if (!pack.IsNull && Get<Stack>(load).Count > def.StackLimit)
            {
                var over = Sim.Store(pack, Sim.TakeFromStack(load, Get<Stack>(load).Count - def.StackLimit));
                if (!over.IsNull)
                    Sim.MergeStacks(load, over);
            }
        }
        if (load.IsNull)
        {
            Fail("nothing was left to pick up");
            yield break;
        }

        // Fill stockpile cells until the load is gone, refilling the hands from the pack; whatever's
        // left goes down where it stands.
        while (true)
        {
            if (!Alive(load) || Get<Carrying>(Pawn).Item != load)
            {
                load = NextFromPack(pack, def, material);
                if (load.IsNull)
                    yield break;
                Get<Carrying>(Pawn).Item = load;
            }
            if (AiQueries.FindStockpileTile(Sim, Pawn, def, PawnCell, material) is not { } dest || !Sim.Reservations.TryReserveTile(dest, Pawn))
                break;
            foreach (var s in GoTo(dest, PathEnd.OnTile)) yield return s;
            if (HasFailed)
                yield break;
            if (!Sim.CanAcceptItem(dest, def, out var space, material) || space <= 0)
                continue;
            var part = Sim.TakeFromStack(load, Math.Min(space, Get<Stack>(load).Count));
            if (part == load)
                Get<Carrying>(Pawn).Item = Entity.Null; // the last of this handful: once down, it's just a stack on the map
            Sim.PlaceItemNear(part, dest);
        }
        // Out of stockpile room: the rest goes down here (ReleasePawn does the same with the pack).
        if (Get<Carrying>(Pawn).Item == load && Alive(load))
        {
            Get<Carrying>(Pawn).Item = Entity.Null;
            Sim.PlaceItemNear(load, PawnCell);
        }
    }

    /// <summary>The next stack of the load out of the pack, unstored; Entity.Null when it's all unloaded.</summary>
    private Entity NextFromPack(Entity pack, ThingDef def, MaterialDef? material)
    {
        if (pack.IsNull || !Alive(pack))
            return Entity.Null;
        foreach (var e in Sim.StoredIn(pack))
        {
            var t = Get<Thing>(e);
            if (t.Def == def && t.Material == material)
                return Sim.TakeFromStack(e, Get<Stack>(e).Count);
        }
        return Entity.Null;
    }
}

/// <summary>
/// Bring construction materials to a site. With a work pack (<see cref="Inventory.WorkPack"/>) it
/// gathers more stacks of the material nearby until it has <paramref name="count"/> or the pack is
/// full, so one trip can bring all a site still needs.
/// </summary>
public sealed class DeliverJob(Entity site, Entity material, int count) : Job
{
    protected override IEnumerable<JobStatus> Run()
    {
        var def = Get<Thing>(material).Def;
        Report = $"Delivering {Get<Thing>(material).Label}";
        if (!Reserve(material)) yield break;

        foreach (var s in GoTo(Get<Thing>(material).Cell, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        if (!Sim.IsAvailable(material))
        {
            Fail("material disappeared");
            yield break;
        }
        var near = Get<Thing>(material).Cell;
        var carried = Sim.TakeFromStack(material, Math.Min(Math.Min(count, def.StackLimit), Get<Stack>(material).Count));
        Get<Carrying>(Pawn).Item = carried;

        // More of it into the pack: the nearest stacks around the first, while there's need and room.
        var pack = Inventory.WorkPack(Sim, Pawn);
        foreach (var s in GatherNear(pack, def, null, near, count, new Tally { Count = Get<Stack>(carried).Count })) yield return s;
        if (HasFailed) yield break;

        if (!Alive(site))
        {
            Fail("construction cancelled");
            yield break;
        }
        foreach (var s in GoToBeside(site)) yield return s;
        if (HasFailed) yield break;
        if (!Alive(site))
        {
            Fail("construction cancelled");
            yield break;
        }

        Hand(carried);
        Get<Carrying>(Pawn).Item = Entity.Null;
        if (Alive(carried))
            Sim.PlaceItemNear(carried, PawnCell);
        if (!pack.IsNull)
            foreach (var e in Sim.StoredIn(pack))
                if (Get<Thing>(e).Def == def)
                    Hand(e); // what's left of it goes down when the job ends (ReleasePawn)
    }

    /// <summary>Put a stack of the material into the site; destroys it if it's all used.</summary>
    private void Hand(Entity stack)
    {
        var construction = Get<Construction>(site);
        var cost = construction.Target.Building!.Cost;
        var def = Get<Thing>(stack).Def;
        ref var units = ref Get<Stack>(stack);
        for (var i = 0; i < cost.Count && units.Count > 0; i++)
        {
            if (cost[i].ThingDef != def)
                continue;
            var take = Math.Min(units.Count, cost[i].Count - construction.Delivered[i]);
            if (take <= 0)
                continue;
            construction.Delivered[i] += take;
            units.Count -= take;
            if (Get<Thing>(stack).Material is { } m)
                construction.AddMaterial(m, take);
        }
        if (units.Count <= 0)
            Sim.World.Destroy(stack);
    }
}

/// <summary>
/// Do construction work on a site whose materials are all delivered. A preset building's phase
/// (<see cref="Buildings.IsPhaseSite"/>) is worked by several at once, as far as the materials
/// brought so far go (<see cref="Buildings.WorkAllowed"/>); whoever finishes it raises it.
/// </summary>
public sealed class BuildJob(Entity site) : Job
{
    public override Entity Focus => site;

    public override string? ToolKind => "Hammer";

    /// <summary>The site being built (several may work on one phase site).</summary>
    public Entity Site => site;

    protected override IEnumerable<JobStatus> Run()
    {
        var target = Get<Construction>(site).Target;
        var phase = target.PhaseOf != null;
        Report = $"Building {target.Label}";
        if (!phase && !Reserve(site)) yield break;
        foreach (var s in GoToBeside(site)) yield return s;
        if (HasFailed) yield break;

        while (true)
        {
            if (!Alive(site))
            {
                if (!phase) // a phase someone else just finished isn't a failure
                    Fail("construction cancelled");
                yield break;
            }
            var c = Get<Construction>(site);
            var cap = phase ? Buildings.WorkAllowed(c) : target.Building!.Work;
            c.WorkDone = Math.Min(cap, c.WorkDone + Work(Skill.Construction) / (c.MainMaterial?.WorkFactor ?? 1f)); // granite is slow to dress
            if (c.WorkDone >= target.Building!.Work)
                break;
            if (c.WorkDone >= cap)
                yield break; // built as far as the materials go: time to fetch more
            yield return JobStatus.Running;
        }

        // Don't entomb anyone: give others a moment to get off the tiles, and step off them ourselves.
        List<int3> tiles = phase ? Buildings.SolidCells(Sim, site) : target.Impassable ? [Get<Thing>(site).Cell] : [];
        for (var waited = 0; waited < SimClock.TicksPerSecond * 5 && SomeoneElseOn(tiles); waited++)
            yield return JobStatus.Running;
        if (tiles.Contains(PawnCell))
        {
            foreach (var d in Map.Neighbors4)
            {
                var n = Map.Offset(PawnCell, d);
                if (Sim.Map.Passable(n) && !tiles.Contains(n))
                {
                    foreach (var s in GoTo(n, PathEnd.OnTile)) yield return s;
                    break;
                }
            }
        }
        if (Alive(site))
            Sim.CompleteConstruction(site);
    }

    private bool SomeoneElseOn(List<int3> tiles)
    {
        if (tiles.Count == 0)
            return false;
        foreach (var e in Sim.Creatures())
            if (e != Pawn && tiles.Contains(Get<Position>(e).Cell))
                return true;
        return false;
    }
}

/// <summary>Take apart the building or floor in a cell marked for deconstruction.</summary>
public sealed class DeconstructJob(Entity target) : Job
{
    public override Entity Focus => target;

    public override string? ToolKind => "Hammer";

    protected override IEnumerable<JobStatus> Run()
    {
        var def = Get<Thing>(target).Def;
        var cell = Get<Thing>(target).Cell;
        Report = $"Taking apart {def.Label}";
        if (!Reserve(target)) yield break;
        foreach (var s in GoToBeside(target)) yield return s;
        if (HasFailed) yield break;

        for (var work = 0f; work < def.Building!.Work * 0.5f; work += Work(Skill.Construction))
        {
            if (!Alive(target) || (Sim.Map.Designations[Sim.Map.Idx(cell)] & Designation.Deconstruct) == 0)
            {
                Fail("deconstruction cancelled");
                yield break;
            }
            yield return JobStatus.Running;
        }
        if (!Sim.CanDeconstruct(target, out var why))
        {
            Fail(why);
            yield break;
        }
        Sim.Map.ClearDesignation(Sim.Map.Idx(cell), Designation.Deconstruct);
        Sim.Deconstruct(target);
    }
}

/// <summary>Chop a tree, mine a boulder or harvest a bush.</summary>
public sealed class HarvestJob(Entity target) : Job
{
    private string? toolKind;

    public override Entity Focus => target;

    public override string? ToolKind => toolKind;

    protected override IEnumerable<JobStatus> Run()
    {
        var def = Get<Thing>(target).Def;
        var harvest = def.Harvest!;
        Report = harvest.WorkType switch
        {
            WorkType.Chop => $"Chopping {def.Label}",
            WorkType.Mine => $"Mining {def.Label}",
            _ => $"Harvesting {def.Label}",
        };
        if (!Reserve(target)) yield break;
        var tile = Get<Thing>(target).Cell;
        foreach (var s in GoTo(tile, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;

        var skill = Skills.For(harvest.WorkType) ?? Skill.Plants;
        // Felling wants an axe and breaking rock a pick: bare-handed it's slow, and better tools go faster (Crafting.GatherFactor).
        var tool = harvest.WorkType switch { WorkType.Chop => "Axe", WorkType.Mine => "Pick", _ => null };
        toolKind = tool;
        var toolFactor = tool == null ? 1f : Crafting.GatherFactor(Sim, Pawn, tool);
        for (var work = 0f; work < harvest.Work; work += Work(skill) * toolFactor)
        {
            if (!Alive(target))
            {
                Fail("target is gone");
                yield break;
            }
            yield return JobStatus.Running;
        }

        var map = Sim.Map;
        map.ClearDesignation(map.Idx(tile), Designation.Chop | Designation.Mine | Designation.Harvest);
        foreach (var y in harvest.Yield)
            Sim.SpawnItem(y.ThingDef, y.Count, tile, y.MaterialDef);
        Mining.RollBonus(Sim, harvest, tile, Pawn);

        if (harvest.DestroyOnHarvest)
        {
            Sim.DestroyThing(target);
            if (harvest.WorkType == WorkType.Mine && map.GroundUnder(map.Idx(tile)) != null)
                map.Block[map.Idx(tile) - map.LayerSize] = Sim.Defs.Block("Stone");
        }
        else
        {
            Get<PlantState>(target).RegrowTicksLeft = SimClock.DaysToTicks(harvest.RegrowDays);
        }
    }
}

/// <summary>Dig out a solid block, leaving air (and whatever the block yields).</summary>
public sealed class DigJob(int3 cell) : Job
{
    public override int3? FocusCell => cell;

    public override string? ToolKind => "Pick";

    protected override IEnumerable<JobStatus> Run()
    {
        var map = Sim.Map;
        var i = map.Idx(cell);
        var block = map.Block[i];
        Report = $"Digging {Mining.ShownBlock(Sim, i).Label}";
        if (!ReserveTile(cell)) yield break;
        foreach (var s in GoTo(cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;

        if (Mining.BreakProblem(Sim, Pawn, block) is { } why)
        {
            Fail($"{block.Label} {why}");
            yield break;
        }
        var toolFactor = Mining.DigFactor(Sim, Pawn, block); // rock wants a pick (of its tier); soil, any hands
        for (var work = 0f; work < block.DigWork; work += Work(Skill.Mining) * toolFactor)
        {
            if (map.Block[i] != block || (map.Designations[i] & Designation.Dig) == 0)
            {
                Fail("dig cancelled");
                yield break;
            }
            yield return JobStatus.Running;
        }

        Sim.DigOut(cell, Pawn);
    }
}

/// <summary>Carry fuel to a fueled building.</summary>
public sealed class RefuelJob(Entity building, Entity fuel) : Job
{
    protected override IEnumerable<JobStatus> Run()
    {
        var props = Get<Thing>(building).Def.Building!.Fuel!;
        Report = $"Refueling {Get<Thing>(building).Def.Label}";
        if (!Reserve(building) || !Reserve(fuel)) yield break;

        foreach (var s in GoTo(Get<Thing>(fuel).Cell, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        if (!Sim.IsAvailable(fuel))
        {
            Fail("fuel disappeared");
            yield break;
        }
        var needed = (int)MathF.Ceiling(props.Capacity - Get<Fueled>(building).Fuel);
        var carried = Sim.TakeFromStack(fuel, Math.Max(1, Math.Min(needed, Get<Stack>(fuel).Count)));
        Get<Carrying>(Pawn).Item = carried;

        if (!Alive(building))
        {
            Fail("building is gone");
            yield break;
        }
        foreach (var s in GoToBeside(building)) yield return s;
        if (HasFailed || !Alive(building)) yield break;

        foreach (var s in Wait(30)) yield return s;
        ref var fueled = ref Get<Fueled>(building);
        fueled.Fuel = Math.Min(props.Capacity, fueled.Fuel + Get<Stack>(carried).Count);
        fueled.Loaded = Get<Thing>(carried).Def;
        Get<Carrying>(Pawn).Item = Entity.Null;
        Sim.World.Destroy(carried);
    }
}

/// <summary>Beat out the fire on a tile.</summary>
public sealed class FightFireJob(int3 tile) : Job
{
    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Fighting fire";
        if (!ReserveTile(tile)) yield break;
        foreach (var s in GoTo(tile, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;

        var i = Sim.Map.Idx(tile);
        while (Sim.Map.Fire[i] > 0)
        {
            // The player beats at the flames only while the work key is held.
            if (WaitingForWorkKey())
            {
                yield return JobStatus.Running;
                continue;
            }
            foreach (var s in Wait(12)) yield return s;
            Sim.Map.Fire[i] = Math.Max(0, Sim.Map.Fire[i] - 0.2f);
        }
    }
}

/// <summary>Close in on a target (raiders approaching the village; turn-based combat takes over once near).</summary>
public sealed class AssaultJob(Entity target) : Job
{
    public override Entity Focus => target;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = $"Attacking {Get<Creature>(target).Name}";
        if (Combat.CombatRules.Attacks(Sim, Pawn).FirstOrDefault(a => !a.Ranged) is not { } attack)
        {
            Fail("nothing to attack with");
            yield break;
        }
        foreach (var s in GoToEntity(target, PathEnd.Touch, maxTicks: 600)) yield return s;
        if (HasFailed) yield break;
        // Blows land on the attack's cooldown for as long as they stay in reach; if the target
        // steps away the job ends and the brain sends them after it again.
        long nextAttack = 0;
        while (Alive(target) && Get<Creature>(target).Able && Pathfinder.Touches(PawnCell, Sim.LocationOf(target)))
        {
            if (Sim.Clock.Tick >= nextAttack)
            {
                Combat.RealtimeAttacks.TryAttack(Sim, Pawn, target, attack);
                nextAttack = Sim.Clock.Tick + attack.CooldownTicks;
            }
            yield return JobStatus.Running;
        }
    }
}

/// <summary>
/// Stalk an animal marked for hunting to within bow range and shoot until it drops. Gives up when
/// the order is lifted, the hunter is badly hurt, or the trail goes cold.
/// </summary>
public sealed class HuntJob(Entity prey) : Job
{
    /// <summary>Hunters close to this share of their bow's range before they shoot.</summary>
    public const float StalkRange = 0.85f;

    public override Entity Focus => prey;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = $"Hunting {Get<Creature>(prey).Def.Label}";
        if (!Reserve(prey)) yield break;
        var giveUp = Sim.Clock.Tick + SimClock.HoursToTicks(2);
        var mover = Get<Mover>(Pawn);
        long nextShot = 0, nextPlan = 0;
        while (true)
        {
            if (!Alive(prey))
            {
                Fail("prey is gone");
                yield break;
            }
            if (Get<Creature>(prey).Dead)
            {
                Sim.World.Remove<HuntOrder>(prey);
                yield break;
            }
            if (!Sim.World.Has<HuntOrder>(prey))
            {
                Fail("hunt called off");
                yield break;
            }
            if (Sim.Clock.Tick > giveUp)
            {
                Fail("lost the trail");
                yield break;
            }
            if (Sim.World.TryGet<Health>(Pawn, out var h) && h.Pain > 0.5f)
            {
                Fail("too hurt to hunt");
                yield break;
            }
            if (RangedAttack(Sim, Pawn) is not { } bow)
            {
                Fail("no ranged weapon");
                yield break;
            }

            var here = PawnCell;
            var at = Sim.LocationOf(prey);
            if (Combat.CombatRules.Distance(here, at) <= bow.Range * StalkRange && Combat.CombatRules.Chance(Sim, Pawn, prey, bow).Possible)
            {
                if (mover.Moving)
                    mover.Path = mover.Path!.GetRange(0, mover.PathIndex + 1); // finish this step, then stand
                else if (Sim.Clock.Tick >= nextShot)
                {
                    Combat.RealtimeAttacks.TryAttack(Sim, Pawn, prey, bow);
                    nextShot = Sim.Clock.Tick + bow.CooldownTicks;
                }
            }
            else if (!mover.Moving || Sim.Clock.Tick >= nextPlan)
            {
                if (Navigation.PlanRoute(Sim, Pawn, here, at, PathEnd.Touch) is not { } path)
                {
                    Fail($"no path to {at}");
                    yield break;
                }
                mover.Path = path;
                mover.PathIndex = 0;
                mover.StepProgress = 0;
                mover.StepFrom = here;
                nextPlan = Sim.Clock.Tick + 60;
            }
            yield return JobStatus.Running;
        }
    }

    /// <summary>The creature's ranged attack (a bow), if it has one.</summary>
    public static AttackDef? RangedAttack(Simulation sim, Entity e) => Combat.CombatRules.Attacks(sim, e).FirstOrDefault(a => a.Ranged);
}

/// <summary>Stand on a marked bank cell and cast a few times, leaving any catch at your feet for the haulers.</summary>
/// <summary>
/// Cast from a bank cell into the water beside it. Villagers fish marked spots (<see cref="Designation.Fish"/>,
/// <paramref name="ordered"/>); the player's character fishes wherever it stands on a bank, while the key is held.
/// </summary>
public sealed class FishJob(int3 spot, bool ordered = true) : Job
{
    public override int3? FocusCell => Systems.Fishing.WaterNextTo(Sim, spot);

    public override string? ToolKind => "Rod";

    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Fishing";
        if (!ReserveTile(spot)) yield break;
        foreach (var s in GoTo(spot, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;

        var map = Sim.Map;
        var patch = Systems.Fishing.PatchOf(Sim, spot);
        for (var cast = 0; cast < Systems.Fishing.CastsPerTrip; cast++)
        {
            for (var work = 0f; work < Systems.Fishing.CastWork; ) // unskilled: there is no Fishing skill
            {
                if (ordered && (map.Designations[map.Idx(spot)] & Designation.Fish) == 0)
                    yield break;
                if (!WaitingForWorkKey())
                    work += Crafting.GatherFactor(Sim, Pawn, "Rod");
                yield return JobStatus.Running;
            }
            if (Sim.Wildlife.Cast(Sim, patch))
                Sim.SpawnItem(Sim.Defs.Thing("RawFish"), 1, spot);
        }
    }
}

/// <summary>
/// Cut up an animal carcass into raw meat and hide: where it lies, or, given a butcher
/// <paramref name="spot"/> (<see cref="ButcherSpots"/>), carried there first and cut up faster and
/// with less waste.
/// </summary>
public sealed class ButcherJob(Entity carcass, Entity? toSpot = null) : Job
{
    private readonly Entity spot = toSpot ?? Entity.Null;

    /// <summary>Share of the full yield a butcher gets: 0.82 for a novice, 1 at average skill, 1.2 at most.</summary>
    public static float ButcherYield(Simulation sim, Entity butcher) =>
        Math.Clamp(0.82f + 0.03f * SkillRules.LevelOf(sim, butcher, Skill.Hunting), 0.8f, 1.2f);

    public override Entity Focus => carcass;

    protected override IEnumerable<JobStatus> Run()
    {
        var def = Get<Creature>(carcass).Def;
        Report = $"Butchering {def.Label}";
        if (!Reserve(carcass)) yield break;
        foreach (var s in GoToEntity(carcass, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;

        // Carry it to the spot unless it's there already.
        if (!spot.IsNull && ButcherSpots.SpotAt(Sim, Sim.LocationOf(carcass)) != spot)
        {
            if (!Alive(spot) || !Get<Thing>(spot).Spawned || Sim.World.Has<Carried>(carcass) || Get<Creature>(carcass) is not { Dead: true })
            {
                Fail("can't carry it to the butcher spot");
                yield break;
            }
            Sim.World.Add(carcass, new Carried { By = Pawn });
            Report = $"Carrying {def.Label} to the butcher spot";
            foreach (var s in GoToBeside(spot)) yield return s;
            if (HasFailed) yield break; // ReleasePawn puts it down where we stand
            Sim.World.Remove<Carried>(carcass);
            Sim.World.Get<Position>(carcass) = Position.At(PawnCell);
            Report = $"Butchering {def.Label}";
        }
        var at = ButcherSpots.SpotAt(Sim, Sim.LocationOf(carcass));
        var bonus = at.IsNull ? null : Get<Thing>(at).Def.Building!.ButcherSpot!;

        for (var work = 0f; work < def.Animal!.ButcherWork; work += Work(Skill.Hunting) * (bonus?.Speed ?? 1f))
        {
            if (!Alive(carcass))
            {
                Fail("carcass is gone");
                yield break;
            }
            yield return JobStatus.Running;
        }
        var cell = Sim.LocationOf(carcass);
        var rot = Sim.World.TryGet<Rot>(carcass, out var r) ? r.Progress : 0f;
        Sim.World.DestroyDeferred(carcass);
        // A practised butcher wastes less: the full yield at average skill, a fifth less for a novice.
        var share = ButcherYield(Sim, Pawn) + (bonus?.Yield ?? 0f);
        Sim.SpawnItem(Sim.Defs.Thing("Meat"), Math.Max(1, (int)MathF.Round(def.Animal.MeatYield * share)), cell, rot: rot);
        if (def.Animal.HideYield > 0 && Sim.Defs.TryThing("Hide", out var hide))
            Sim.SpawnItem(hide, Math.Max(1, (int)MathF.Round(def.Animal.HideYield * share)), cell, rot: rot);
    }
}

/// <summary>A cornered animal turns on whoever hurt it: charge and gore until it's driven off or they fall.</summary>
public sealed class AnimalAttackJob(Entity target) : Job
{
    /// <summary>The animal breaks off when its target gets this far away (past a bow's reach, so archers get charged).</summary>
    public const int GiveUpDistance = 18;

    public Entity Target => target;
    public override Entity Focus => target;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = $"Charging {Get<Creature>(target).Name}";
        if (Combat.CombatRules.Attacks(Sim, Pawn).FirstOrDefault(a => !a.Ranged) is not { } attack)
        {
            Fail("nothing to attack with");
            yield break;
        }
        var mover = Get<Mover>(Pawn);
        long nextAttack = 0, nextPlan = 0;
        for (var ticks = 0; ticks < SimClock.TicksPerSecond * 20; ticks++)
        {
            if (!Alive(target) || !Get<Creature>(target).Able)
                yield break;
            var here = PawnCell;
            var at = Sim.LocationOf(target);
            if (Maps.Map.Distance(here, at) > GiveUpDistance)
                yield break;
            if (Pathfinder.Touches(here, at))
            {
                mover.Stop();
                if (Sim.Clock.Tick >= nextAttack)
                {
                    Combat.RealtimeAttacks.TryAttack(Sim, Pawn, target, attack);
                    nextAttack = Sim.Clock.Tick + attack.CooldownTicks;
                }
            }
            else if (!mover.Moving || Sim.Clock.Tick >= nextPlan)
            {
                if (Navigation.PlanRoute(Sim, Pawn, here, at, PathEnd.Touch) is not { } path)
                    yield break;
                mover.Path = path;
                mover.PathIndex = 0;
                mover.StepProgress = 0;
                mover.StepFrom = here;
                nextPlan = Sim.Clock.Tick + 30;
            }
            yield return JobStatus.Running;
        }
    }
}

/// <summary>Run to the map edge and leave for good.</summary>
public sealed class LeaveMapJob(int3 edge, string report = "Fleeing") : Job
{
    protected override IEnumerable<JobStatus> Run()
    {
        Report = report;
        foreach (var s in GoTo(edge, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        Sim.Depart(Pawn);
    }
}

/// <summary>Pick up a downed villager and carry them to a bed, where they heal faster and can be tended.</summary>
public sealed class RescueJob(Entity patient, Entity bed) : Job
{
    protected override IEnumerable<JobStatus> Run()
    {
        var name = Get<Creature>(patient).Name;
        Report = $"Rescuing {name}";
        if (!Reserve(patient) || !Reserve(bed)) yield break;
        foreach (var s in GoToEntity(patient, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        if (Get<Creature>(patient).Dead || !Get<Creature>(patient).Downed || Sim.World.Has<Carried>(patient))
        {
            Fail("no longer needs rescuing");
            yield break;
        }
        // The walk took a while: the bed may have burned down or been deconstructed meanwhile.
        if (!Alive(bed) || !Get<Thing>(bed).Spawned)
        {
            Fail("bed is gone");
            yield break;
        }

        Sim.World.Add(patient, new Carried { By = Pawn });
        Report = $"Carrying {name} to bed";
        foreach (var s in GoTo(Get<Thing>(bed).Cell, PathEnd.OnTile)) yield return s;
        if (!HasFailed)
            MoodSystem.Remember(Sim, patient, Sim.Defs.Thoughts.RescuedMe, Pawn);
    }
}

/// <summary>Carry a downed enemy to a prisoner bed and lock them up (<see cref="Prisoners.Imprison"/>).</summary>
public sealed class CaptureJob(Entity captive, Entity bed) : Job
{
    protected override IEnumerable<JobStatus> Run()
    {
        var name = Get<Creature>(captive).Name;
        Report = $"Capturing {name}";
        if (!Reserve(captive) || !Reserve(bed)) yield break;
        foreach (var s in GoToEntity(captive, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        if (!Prisoners.Capturable(Sim, captive) || Sim.World.Has<Carried>(captive))
        {
            Fail("can no longer be captured");
            yield break;
        }
        if (!Alive(bed) || !Get<Thing>(bed).Spawned)
        {
            Fail("bed is gone");
            yield break;
        }

        Sim.World.Add(captive, new Carried { By = Pawn });
        Report = $"Carrying {name} to a prisoner bed";
        foreach (var s in GoTo(Get<Thing>(bed).Cell, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        if (!Alive(bed) || Get<Creature>(captive).Dead)
        {
            Fail("capture fell through");
            yield break;
        }
        Prisoners.Imprison(Sim, captive, bed);
        MoodSystem.Remember(Sim, captive, Sim.Defs.Thoughts.CapturedMe, Pawn);
    }
}

/// <summary>A prisoner keeps to their bed.</summary>
public sealed class ConfinedJob(Entity bed, int ticks) : Job
{
    public override Entity Focus => bed;
    public override bool Interruptible => true;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Held prisoner";
        if (!Reserve(bed)) yield break;
        foreach (var s in GoTo(Get<Thing>(bed).Cell, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        foreach (var s in Wait(ticks)) yield return s;
    }
}

/// <summary>A warden brings a prisoner a meal.</summary>
/// <summary>
/// Bring food to someone who can't fetch it: a prisoner (Warden work), or with
/// <paramref name="byWarden"/> false a downed patient (Doctor work), who eats it from the hand.
/// </summary>
public sealed class FeedPrisonerJob(Entity prisoner, Entity food, bool byWarden = true) : Job
{
    protected override IEnumerable<JobStatus> Run()
    {
        var def = Get<Thing>(food).Def;
        var name = Get<Creature>(prisoner).Name;
        Report = $"Fetching {def.Label} for {name}";
        if (!Reserve(food) || !Reserve(prisoner)) yield break;
        foreach (var s in GoTo(Get<Thing>(food).Cell, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        if (!Sim.IsAvailable(food))
        {
            Fail("food disappeared");
            yield break;
        }
        var carried = Sim.TakeFromStack(food, Math.Min(EatJob.Wanted(Sim, prisoner, def), Get<Stack>(food).Count));
        Get<Carrying>(Pawn).Item = carried;

        Report = $"Bringing {name} a meal";
        foreach (var s in GoToEntity(prisoner)) yield return s;
        if (HasFailed) yield break;
        foreach (var s in Wait(90)) yield return s;
        if (!Alive(prisoner) || Get<Creature>(prisoner).Dead)
        {
            Fail("prisoner is gone");
            yield break;
        }
        EatJob.Nourish(Sim, prisoner, def, Get<Stack>(carried).Count, FoodPoisoning.RotOf(Sim, carried));
        MoodSystem.Remember(Sim, prisoner, byWarden ? Sim.Defs.Thoughts.FedByWarden : Sim.Defs.Thoughts.TendedMe, Pawn);
        Get<Carrying>(Pawn).Item = Entity.Null;
        Sim.World.Destroy(carried);
    }
}

/// <summary>A warden visits a prisoner: a talk toward recruiting them, or letting them go.</summary>
public sealed class WardenVisitJob(Entity prisoner) : Job
{
    protected override IEnumerable<JobStatus> Run()
    {
        var name = Get<Creature>(prisoner).Name;
        var mode = Get<Prisoner>(prisoner).Mode;
        Report = mode == PrisonerMode.Release ? $"Releasing {name}" : $"Talking with {name}";
        if (!Reserve(prisoner)) yield break;
        foreach (var s in GoToEntity(prisoner)) yield return s;
        if (HasFailed) yield break;
        foreach (var s in Wait(mode == PrisonerMode.Release ? 60 : 300)) yield return s;
        if (!Alive(prisoner) || Get<Creature>(prisoner).Dead || !Sim.World.Has<Prisoner>(prisoner))
        {
            Fail("prisoner is gone");
            yield break;
        }
        // The player may have changed their mind while we walked over.
        if (Get<Prisoner>(prisoner).Mode == PrisonerMode.Release)
            Prisoners.Release(Sim, prisoner);
        else if (Get<Prisoner>(prisoner).Mode == PrisonerMode.Recruit)
            Prisoners.Persuade(Sim, prisoner, Pawn);
    }
}

/// <summary>
/// Bandage a villager's (or dragon's) untended wounds, and re-dress infected ones: they stop
/// bleeding and heal faster, by how good the doctor's Medicine is.
/// </summary>
public sealed class TendJob(Entity patient) : Job
{
    public override Entity Focus => patient;

    /// <summary>Work per wound.</summary>
    public const int TicksPerInjury = 150;

    protected override IEnumerable<JobStatus> Run()
    {
        var name = Get<Creature>(patient).Name;
        Report = $"Tending {name}";
        if (!Reserve(patient)) yield break;
        var health = Get<Health>(patient);
        var work = TicksPerInjury * Math.Max(1, health.Injuries.Count(i => i.NeedsTending(Sim.Clock.Tick)));

        // The patient may be up and about: follow them until the work is done.
        for (var done = 0f; done < work;)
        {
            foreach (var s in GoToEntity(patient, PathEnd.Touch)) yield return s;
            if (HasFailed) yield break;
            while (done < work && Pathfinder.Touches(PawnCell, Sim.LocationOf(patient)))
            {
                done += Work(Skill.Medicine);
                yield return JobStatus.Running;
                if (!Alive(patient) || Get<Creature>(patient).Dead)
                {
                    Fail("patient died");
                    yield break;
                }
            }
        }

        var medicine = SkillRules.LevelOf(Sim, Pawn, Skill.Medicine);
        foreach (var inj in health.Injuries)
        {
            if (!inj.NeedsTending(Sim.Clock.Tick))
                continue;
            var quality = Math.Clamp(0.35f + 0.03f * medicine + Sim.Rand.Range(-0.1f, 0.1f), 0.05f, 1f);
            inj.Quality = inj.Tended ? Math.Max(inj.Quality, quality) : quality;
            inj.Tended = true;
            inj.TendedTick = Sim.Clock.Tick;
        }
        health.Changed();
        Sim.Shops.Treated(Sim, patient, Pawn);
        MoodSystem.Remember(Sim, patient, Sim.Defs.Thoughts.TendedMe, Pawn);
        Sim.Messages.Add(MessageKind.Good, $"{Get<Creature>(Pawn).Name} bandaged {name}'s wounds.", Sim.LocationOf(patient));
    }
}

/// <summary>Trails after someone for a while.</summary>
public sealed class FollowJob(Entity leader, int ticks) : Job
{
    public override bool Interruptible => true;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = $"Following {Get<Creature>(leader).Name}";
        for (var t = 0; t < ticks; t += 30)
        {
            foreach (var s in GoToEntity(leader, PathEnd.Touch, maxTicks: 600)) yield return s;
            if (HasFailed) yield break;
            foreach (var s in Wait(30)) yield return s;
        }
    }
}

