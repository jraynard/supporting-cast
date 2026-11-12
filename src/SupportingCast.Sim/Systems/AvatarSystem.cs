using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Maps;

namespace SupportingCast.Sim.Systems;

public enum AvatarActionKind
{
    None,
    PutDown,
    Deliver,

    /// <summary>Eat food you carry or that's in reach: never the work key's, only <see cref="Core.EatCommand"/> (kept for the enum's saved values).</summary>
    Eat,
    PickUp,
    Build,
    Harvest,
    Dig,
    Sleep,

    /// <summary>Put what you hold into a container.</summary>
    Store,

    /// <summary>Feed what you hold into a fire that burns it (wood into a campfire), done at once.</summary>
    Refuel,

    /// <summary>Put on (or wield) gear lying in reach, into a slot that's empty; done at once.</summary>
    Equip,

    /// <summary>Take apart something marked for it that can't be built but can be salvaged (an emptied wagon), worked while held.</summary>
    Dismantle,

    /// <summary>Look inside a container (the game opens its window; taking things out is a <see cref="Core.TakeFromStorageCommand"/>).</summary>
    OpenStorage,

    /// <summary>Use a workbench: the game opens its recipes (picking one is a <see cref="Core.CraftCommand"/>).</summary>
    OpenStation,

    /// <summary>Work the craft ready at a station (everything brought), while the key is held.</summary>
    Craft,

    /// <summary>Beat out the flames in a burning cell, while the key is held.</summary>
    Extinguish,

    /// <summary>Cast into the water beside the bank you stand on (<see cref="Ai.FishJob"/>), while the key is held.</summary>
    Fish,

    /// <summary>Shoot or strike at a wild animal (<see cref="Ai.AvatarHuntJob"/>), while the key is held.</summary>
    Hunt,
    /// <summary>Cut up an animal's carcass into meat and hide (<see cref="Ai.ButcherJob"/>), while the key is held.</summary>
    Butcher,
    /// <summary>Pick up a downed hatchling (or a carcass, for the butcher spot), or set down what's in your arms (<see cref="Rescues"/>), done at once.</summary>
    Carry,

    /// <summary>Draw a drink from a cask (<see cref="Work.PourSession"/>), begun at once.</summary>
    Pour,

    /// <summary>Climb up onto a table or counter beside you (<see cref="ClimbJob"/>); any step takes you down.</summary>
    Climb,

    /// <summary>The next step with the piece in your tongs at a smithy station (<see cref="Work.Smithing.Start"/>), begun at once.</summary>
    Smith,

    /// <summary>At a stew pot: put food in, tend it, or ladle a bowl (<see cref="Work.Cooking.Start"/>), begun at once.</summary>
    Cook,

    /// <summary>Wash dirty crockery at the tub, or knock a keg into a cask (<see cref="Work.BarWork.Start"/>), begun at once.</summary>
    BarWork,
}

/// <summary>Which of the avatar's keys: use (F: everything but digging and dropping), dig (Z) or drop (X: put down what you hold).</summary>
public enum AvatarTool
{
    Use,
    Dig,
    Drop,
}

/// <summary>
/// What the work key would do at a cell; <see cref="Label"/> is the prompt the game shows.
/// <see cref="Item"/> is what's used up or handed over (delivered, burned, eaten): what's in hand,
/// or a stack from the pack.
/// </summary>
public readonly record struct AvatarAction(AvatarActionKind Kind, string Label, int3 Cell, Entity Target)
{
    public static readonly AvatarAction None = new(AvatarActionKind.None, "", default, Entity.Null);

    public Entity Item { get; init; } = Entity.Null;

    /// <summary>Done at once on the key press, rather than worked at while it's held.</summary>
    public bool Instant => Kind is AvatarActionKind.PutDown or AvatarActionKind.Deliver or AvatarActionKind.PickUp
        or AvatarActionKind.Store or AvatarActionKind.OpenStorage 
        or AvatarActionKind.Refuel or AvatarActionKind.Equip or AvatarActionKind.OpenStation
        or AvatarActionKind.Carry or AvatarActionKind.Pour or AvatarActionKind.Cook or AvatarActionKind.BarWork;

    /// <summary>Handled by the game rather than the sim: it opens a window (or the dialogue, or the "set out?" question).</summary>
    public bool OpensWindow => Kind is AvatarActionKind.OpenStorage or AvatarActionKind.OpenStation;
}

/// <summary>
/// Adventure mode: drives creatures marked <see cref="Avatar"/> from player input. A held direction
/// walks one <see cref="NavGraph"/> step at a time (so slopes, stairs and the no-corner-cutting rule
/// all apply, and nobody walks off a cliff); the work key starts an ordinary job on the cell in
/// reach, which only progresses while the key is held (see <see cref="Job"/>.Work). Runs before
/// <see cref="AiSystem"/> and movement, so a held key chains steps without a pause.
/// </summary>
public sealed class AvatarSystem : ISimSystem
{
    public string Name => "Avatar";
    public int Interval => 1;

    /// <summary>
    /// The player can command their dragon in combat once its affinity with them reaches this
    /// (the same bond that lets them ride it).
    /// </summary>
    public const float CommandDragonAffinity = 0.5f;

    private readonly NavGraph.Step[] _steps = new NavGraph.Step[NavGraph.MaxSteps];

    public void Update(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Query<Avatar>())
        {
            var c = w.Get<Creature>(e);
            if (c.Dead || c.Downed)
            {
                // Falling down drops what you hold (the egg included), and whoever's in your arms.
                Rescues.Drop(sim, e);
                ref var carrying = ref w.Get<Carrying>(e);
                if (carrying.HasItem)
                {
                    var item = carrying.Item;
                    carrying.Item = Entity.Null;
                    if (w.IsAlive(item))
                        sim.PlaceItemNear(item, w.Get<Position>(e).Cell);
                }
                continue;
            }
            if (w.Has<Carried>(e))
                continue;

            ref var a = ref w.Get<Avatar>(e);
            if (a.HasPending && !w.Get<Mover>(e).Moving)
                ResolvePending(sim.Map, w.Get<Position>(e).Cell, ref a);
            if (a.MoveDir.X == 0 && a.MoveDir.Y == 0)
            {
                if (w.Get<Mind>(e).Job is AvatarWalkJob { ReadyToAct: true } walk)
                    ActOnArrival(sim, e, walk);
                else
                    ContinueChain(sim, e, ref a);
                continue;
            }
            a.Chain = AvatarActionKind.None; // walking off ends a run of work
            var mind = w.Get<Mind>(e);
            if (mind.Job != null)
                AiSystem.EndJob(sim, e, mind, false); // walking off abandons the work
            var mover = w.Get<Mover>(e);
            if (mover.Moving)
                continue;
            var here = w.Get<Position>(e).Cell;
            if (StepToward(sim.Map, here, a.MoveDir) is not { } next)
                continue;
            a.Facing = new int2(Math.Sign(next.X - here.X), Math.Sign(next.Y - here.Y)); // the step taken, so F's "in front" is where you went
            mover.Path = [next];
            mover.PathIndex = 0;
            mover.StepProgress = 0;
            mover.StepFrom = here;
        }
    }

    /// <summary>Steps between two cells for scheduling a turn: diagonal moves count once, levels add.</summary>
    public static int Distance(int3 a, int3 b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)) + Math.Abs(a.Z - b.Z);

    /// <summary>
    /// Standing on a cell between steps with a turn waiting: take it here if this is the cell, or
    /// if walking on in the current direction wouldn't get any closer (blocked, or already past it).
    /// </summary>
    private void ResolvePending(Map map, int3 here, ref Avatar a)
    {
        var apply = here == a.PendingAt;
        if (!apply)
        {
            var distance = Distance(here, a.PendingAt);
            apply = StepToward(map, here, a.MoveDir) is not { } next || Distance(next, a.PendingAt) >= distance;
        }
        if (!apply)
            return;
        a.MoveDir = a.PendingDir;
        a.HasPending = false;
    }

    /// <summary>How many steps ahead a blocked diagonal looks to choose which way to slide.</summary>
    public const int SlideLookahead = 3;

    /// <summary>
    /// The walkable neighbor in a direction: on the level, else up or down a slope. A blocked
    /// diagonal slides along an open axis, so walking into a wall at an angle follows it. When both
    /// axes are open it takes the one that gets further in the held direction over the next few
    /// steps: into a doorway or round a corner rather than along the wall past it. A climb breaks
    /// a tie, so a diagonal at the foot of a stair goes up it. Pure, so a co-op client predicts the same steps.
    /// </summary>
    public int3? StepToward(Map map, int3 from, int2 dir) => StepToward(map, from, dir, SlideLookahead);

    private int3? StepToward(Map map, int3 from, int2 dir, int lookahead)
    {
        if (!map.InBounds(from))
            return null;
        var count = NavGraph.Neighbors(new MapNav(map), map.Idx(from), _steps);
        if (Find(dir) is { } straight)
            return straight;
        if (dir.X == 0 || dir.Y == 0)
            return null;
        var alongX = Find(new int2(dir.X, 0));
        var alongY = Find(new int2(0, dir.Y));
        if (alongX is not { } x || alongY is not { } y || lookahead == 0)
            return alongX ?? alongY;
        // (Find is done with _steps: the look ahead below refills it.)
        return Progress(y) > Progress(x) ? y : x;

        float Progress(int3 start)
        {
            var at = start;
            for (var i = 0; i < lookahead; i++)
            {
                if (StepToward(map, at, dir, 0) is not { } next)
                    break;
                at = next;
            }
            var gained = (at.X - from.X) * dir.X + (at.Y - from.Y) * dir.Y;
            return gained + (start.Z != from.Z ? 0.5f : 0f);
        }

        int3? Find(int2 d)
        {
            int3? climb = null;
            for (var k = 0; k < count; k++)
            {
                var c = map.CellOf(_steps[k].Index);
                if (c.X - from.X != d.X || c.Y - from.Y != d.Y)
                    continue;
                if (c.Z == from.Z)
                    return c;
                climb ??= c;
            }
            return climb;
        }
    }

    /// <summary>Is a cell close enough to work on or pick up from?</summary>
    public static bool InReach(int3 at, int3 cell) => at == cell || Pathfinder.Touches(at, cell);

    /// <summary>Is any cell of a placed thing's footprint in reach (either end of a bench or bed)?</summary>
    public static bool InReach(Simulation sim, int3 at, Entity thing)
    {
        var t = sim.World.Get<Thing>(thing);
        if (!Maps.Footprint.IsWide(t.Def))
            return InReach(at, t.Cell);
        foreach (var c in sim.FootprintCells(t.Def, t.Cell, t.Facing))
            if (sim.Map.InBounds(c) && InReach(at, c))
                return true;
        return false;
    }

    /// <summary>How far (cells, a level up or down) the avatar can hand material to a blueprint: a toss, not a reach.</summary>
    public const int DeliverReach = 2;

    public static bool InDeliverReach(int3 at, int3 cell) =>
        Math.Abs(at.Z - cell.Z) <= 1 && Math.Max(Math.Abs(at.X - cell.X), Math.Abs(at.Y - cell.Y)) <= DeliverReach;

    /// <summary>
    /// What the work key would do at a cell right now, or <see cref="AvatarAction.None"/>.
    /// <paramref name="from"/> asks as if the avatar stood there instead (the game passes the cell
    /// itself to ask what a click would walk over and do).
    /// </summary>
    public static AvatarAction ActionAt(Simulation sim, Entity avatar, int3 cell, AvatarTool tool = AvatarTool.Use, int3? from = null)
    {
        var w = sim.World;
        var map = sim.Map;
        var here = from ?? w.Get<Position>(avatar).Cell;
        if (!map.InBounds(cell))
            return AvatarAction.None;
        var i = map.Idx(cell);
        var site = map.Structure[i];
        // A preset building's phase going up is one site over its whole plot (F anywhere on it), and
        // an upgrade being fitted is worked at its furnishing.
        if ((site.IsNull || !w.Has<Construction>(site)) && Buildings.SiteAt(sim, cell) is { IsNull: false } raising)
            site = raising;
        var held = w.Get<Carrying>(avatar).Item;
        // Arms full of a creature (a hatchling for its nest, a carcass for the butcher spot): set it down.
        if (Rescues.CarriedBy(sim, avatar) is { IsNull: false } load)
            return tool is AvatarTool.Use or AvatarTool.Drop && InReach(here, cell) ? Rescues.SetDownAt(sim, load, cell) : AvatarAction.None;
        // Game: shot from as far as the bow carries (or struck from beside it); a carcass in reach is butchered.
        if (tool == AvatarTool.Use && Ai.AvatarHunting.ActionAt(sim, avatar, cell, here) is { } hunting)
            return hunting;
        // A downed hatchling (or anyone small enough) to carry to its nest or bed.
        if (tool == AvatarTool.Use && held.IsNull && InReach(here, cell) && Rescues.DownedAt(sim, cell, avatar) is { IsNull: false } hurt
            && Rescues.CanCarry(sim, avatar, hurt))
            return new(AvatarActionKind.Carry, $"Pick up {w.Get<Creature>(hurt).Name}", cell, hurt);
        if (!InReach(here, cell))
        {
            // Out of arm's reach, the one thing you can still do is hand material to a blueprint close by.
            if (tool == AvatarTool.Use && InDeliverReach(here, cell) && !site.IsNull && w.TryGet<Construction>(site, out var far)
                && Supply(sim, avatar, d => NeedsMaterial(far, d)) is { IsNull: false } toss)
                return new AvatarAction(AvatarActionKind.Deliver, $"Deliver {ItemLabel(sim, avatar, toss)}", cell, site) { Item = toss };
            return AvatarAction.None;
        }
        // Putting down has its own key (X), so F never drops what you carry by accident.
        if (tool == AvatarTool.Drop)
        {
            if (held.IsNull)
                return AvatarAction.None;
            var carried = w.Get<Thing>(held);
            return sim.CanAcceptItem(cell, carried.Def, out _, carried.Material)
                ? new(AvatarActionKind.PutDown, $"Put down {carried.Label}", cell, held)
                : AvatarAction.None;
        }
        // Digging has its own key, so pointing at the ground never offers it by accident.
        if (tool == AvatarTool.Dig)
            return map.IsSolid(i) && sim.CanDig(cell) && Mining.CanBreak(sim, avatar, map.Block[i])
                ? new(AvatarActionKind.Dig, $"Dig {Mining.ShownBlock(sim, i).Label}", cell, Entity.Null)
                : AvatarAction.None;

        // A fire comes before anything else there: you can't chop a burning tree or pick up a burning stack.
        if (map.Fire[i] > 0 && !sim.Reservations.IsTileReservedByOther(cell, avatar))
            return new(AvatarActionKind.Extinguish, "Beat out the fire", cell, Entity.Null);

        var item = map.Item[i];
        if (item.IsNull || sim.Reservations.IsReservedByOther(item, avatar))
            item = Entity.Null;
        var pack = sim.PackOf(avatar);

        // Gear lying in reach goes straight on when its slot is empty (your hands needn't be).
        if (!item.IsNull && EmptySlotFor(sim, avatar, w.Get<Thing>(item).Def))
        {
            var gear = w.Get<Thing>(item);
            return new(AvatarActionKind.Equip, $"{(gear.Def.Weapon != null ? "Wield" : "Wear")} {gear.Label}", cell, item);
        }

        // A piece in the tongs goes to the fire, the anvil or the tub; a blade to the grindstone.
        if (Work.Smithing.StepAt(sim, avatar, site) is { } step && !sim.Reservations.IsReservedByOther(site, avatar))
            return new(AvatarActionKind.Smith, step, cell, site);

        // A stew pot takes the food you carry, wants tending, or gives a bowl.
        if (Work.Cooking.StepAt(sim, avatar, site) is { } cooking && !sim.Reservations.IsReservedByOther(site, avatar))
            return new(AvatarActionKind.Cook, cooking, cell, site);

        // Dirty crockery to the wash tub; a keg in your arms to a dry cask.
        if (Work.BarWork.StepAt(sim, avatar, site) is { } upkeep && !sim.Reservations.IsReservedByOther(site, avatar))
            return new(AvatarActionKind.BarWork, upkeep, cell, site);

        // A craft that has everything it needs is worked on the spot.
        if (Crafting.IsStation(sim, site) && w.Get<Workbench>(site) is { Active: { } making, Stocked: true } ready
            && (ready.By == avatar || (ready.By.IsNull && Crafting.CanMake(sim, avatar, making)))
            && !sim.Reservations.IsReservedByOther(site, avatar))
            return new(AvatarActionKind.Craft, $"Make {making.Label}", cell, site);

        // Marked to come apart (the Deconstruct tool, or a container's window): F does the work,
        // whatever's in your hands. Unmarked, F never takes anything apart, so an empty wagon kept
        // for storage or a fire you meant to feed is safe from a stray key press.
        if ((map.Designations[i] & Designation.Deconstruct) != 0 && sim.DeconstructTarget(cell) is var marked && CanDismantle(sim, avatar, marked))
            return new(AvatarActionKind.Dismantle, $"Take the {w.Get<Thing>(marked).Label} apart", cell, marked);

        // Things only items in hand (or in the pack) can do: feed a construction site or a fire.
        // (Eating isn't one: it's picked from the item itself, so F never eats what you meant to use.)
        if (!site.IsNull && w.TryGet<Construction>(site, out var construction)
            && Supply(sim, avatar, d => NeedsMaterial(construction, d)) is { IsNull: false } material)
            return new AvatarAction(AvatarActionKind.Deliver, $"Deliver {ItemLabel(sim, avatar, material)}", cell, site) { Item = material };
        if (Supply(sim, avatar, d => FuelRoom(sim, site, d) > 0) is { IsNull: false } fuel)
        {
            var fire = w.Get<Thing>(site);
            var verb = w.Get<Fueled>(site).Fuel <= 0 ? "Relight" : "Feed";
            return new AvatarAction(AvatarActionKind.Refuel, $"{verb} the {fire.Def.Label} with {ItemLabel(sim, avatar, fuel)}", cell, site) { Item = fuel };
        }
        if (Crafting.IsStation(sim, site))
            return new(AvatarActionKind.OpenStation, $"Use the {w.Get<Thing>(site).Label}", cell, site);
        if (!site.IsNull && w.Get<Thing>(site).Def.Building?.Cask is { } cask && (held.IsNull || w.Get<Thing>(held).Def.Name == cask.Into)
            && !w.Has<Work.WorkSession>(avatar))
            return new(AvatarActionKind.Pour, Work.PourSession.ServingsLeft(sim, site) <= 0 ? $"The {w.Get<Thing>(site).Label} is dry"
                : Work.Vessels.CleanOn(sim, avatar, cask.Into).IsNull ? $"Pour {sim.Defs.Thing(cask.Pours).Label} (no clean {sim.Defs.Thing(cask.Into).Label})"
                : $"Pour {sim.Defs.Thing(cask.Pours).Label}", cell, site);
        if (!held.IsNull && IsContainer(sim, site) && sim.HasRoomFor(site, w.Get<Thing>(held).Def, w.Get<Thing>(held).Material))
            return new(AvatarActionKind.Store, $"Put {w.Get<Thing>(held).Label} in the {w.Get<Thing>(site).Label}", cell, site);
        // A pack lying about opens like the wagon (you're already wearing one, or it'd go on).
        if (!item.IsNull && sim.IsPack(item))
            return new(AvatarActionKind.OpenStorage, $"Open the {w.Get<Thing>(item).Label}", cell, item);
        // Picking up: into the pack when it fits, else into empty hands. Items at a tree's foot come
        // before working the tree, so nothing's lost under it.
        if (!item.IsNull)
        {
            var t = w.Get<Thing>(item);
            // A tool goes on the belt while there's room (Toolbelt): hands and pack stay free.
            if (t.Def.Tool != null && Toolbelt.HasRoom(sim, avatar))
                return new(AvatarActionKind.PickUp, $"Put the {t.Def.Label} on your belt", cell, item);
            var packs = !pack.IsNull && sim.ContainerRoom(pack, t.Def, t.Material) > 0;
            if (packs || held.IsNull)
                return new(AvatarActionKind.PickUp, $"Pick up {t.Label}{(packs ? "" : PackNote(sim, pack, t.Def))}", cell, item);
        }
        if (held.IsNull)
        {
            if (IsContainer(sim, site))
                return new(AvatarActionKind.OpenStorage, $"Open the {w.Get<Thing>(site).Label}", cell, site);
        }

        // Work on what's there: hands full or not (you can chop wood with the egg on your back).
        if (!site.IsNull && !sim.Reservations.IsReservedByOther(site, avatar))
        {
            var thing = w.Get<Thing>(site);
            if (w.TryGet<Construction>(site, out var c))
            {
                if ((c.MaterialsComplete || (Buildings.IsPhaseSite(sim, site) && Buildings.HasWork(c))) && sim.CanBuildNow(site))
                    return new(AvatarActionKind.Build, $"Build {c.Target.Label}", cell, site);
            }
            else if (thing.Def.Harvest is { } harvest && (!w.TryGet<PlantState>(site, out var plant) || plant.Ready))
            {
                var verb = harvest.WorkType switch
                {
                    Defs.WorkType.Chop => "Chop",
                    Defs.WorkType.Mine => "Mine",
                    _ => "Harvest",
                };
                return new(AvatarActionKind.Harvest, $"{verb} {thing.Label}", cell, site);
            }
            else if (thing.Def.Building is { IsBed: true } && cell == here)
            {
                return new(AvatarActionKind.Sleep, $"Sleep in {thing.Label}", cell, site);
            }
        }
        // Up onto a table or counter beside you (solid furniture is walked round, not over).
        if (!site.IsNull && cell != here && cell.Z == here.Z && !w.Has<Perched>(avatar) && map.Standable(i)
            && w.Get<Thing>(site).Def is { Solid: true, Climbable: true } && !w.Has<Construction>(site))
            return new(AvatarActionKind.Climb, $"Climb onto the {w.Get<Thing>(site).Label}", cell, site);
        // Fishing: from the dry bank you stand on, into the water beside it (or pointing at your own feet).
        if (Fishing.CanFishFrom(sim, here) && !sim.Reservations.IsTileReservedByOther(here, avatar)
            && (cell == here || (Maps.Map.Distance(here, cell) <= 1 && cell.Z == here.Z && !map.IsSolid(i) && map.GroundUnder(i) is { IsWater: true })))
        {
            var stock = sim.Wildlife.FishStock(sim, Fishing.PatchOf(sim, here));
            var note = stock > 0.66f ? "" : stock > 0.33f ? " (fewer bites here now)" : " (fished out for now)";
            return new(AvatarActionKind.Fish, $"Fish{note}", here, Entity.Null);
        }
        // (Sleeping on the bare ground is never the work key's: it's the Sleep key or button, SleepCommand.)
        return AvatarAction.None;
    }

    /// <summary>
    /// What to use for something that takes an item: what's in hand if it will do, else the first
    /// stack in the pack that will. Entity.Null if neither.
    /// </summary>
    public static Entity Supply(Simulation sim, Entity avatar, Func<Defs.ThingDef, bool> fits)
    {
        var w = sim.World;
        var held = w.Get<Carrying>(avatar).Item;
        if (!held.IsNull && w.IsAlive(held) && fits(w.Get<Thing>(held).Def))
            return held;
        var pack = sim.PackOf(avatar);
        if (pack.IsNull)
            return Entity.Null;
        foreach (var e in sim.StoredIn(pack))
            if (fits(w.Get<Thing>(e).Def))
                return e;
        return Entity.Null;
    }

    /// <summary>"12 oak wood", noting "(pack)" when it comes out of the pack rather than the hands.</summary>
    private static string ItemLabel(Simulation sim, Entity avatar, Entity item) =>
        sim.World.Get<Carrying>(avatar).Item == item ? sim.World.Get<Thing>(item).Label
            : $"{sim.World.Get<Stack>(item).Count} {sim.World.Get<Thing>(item).Label} (pack)";

    /// <summary>Why an item goes to the hands rather than the pack, for the prompt.</summary>
    private static string PackNote(Simulation sim, Entity pack, Defs.ThingDef def) =>
        pack.IsNull || def.Bulky || def.Container != null ? ""
            : $" (pack full: {sim.LoadOf(pack):0.#}/{sim.World.Get<Thing>(pack).Def.Container!.Capacity:0.#} kg)";

    /// <summary>Is this gear, with its slot (or the weapon hand) empty on the avatar?</summary>
    public static bool EmptySlotFor(Simulation sim, Entity avatar, Defs.ThingDef def)
    {
        var w = sim.World;
        if (def.Apparel is { } a)
            return !w.TryGet<Apparel>(avatar, out var worn) || !w.IsAlive(worn[a.Slot]);
        if (def.Weapon != null)
            return !w.TryGet<Equipment>(avatar, out var eq) || !w.IsAlive(eq.Weapon);
        return false;
    }

    /// <summary>
    /// A building that can come apart now (anything buildable, or with a salvage like an empty
    /// wagon).
    /// </summary>
    public static bool CanDismantle(Simulation sim, Entity avatar, Entity e) =>
        !e.IsNull && !sim.World.Has<Construction>(e)
        && sim.World.Get<Thing>(e).Def.Building is { Deconstructable: true } && sim.CanDeconstruct(e, out _)
        && !sim.Reservations.IsReservedByOther(e, avatar);

    /// <summary>Whole units of <paramref name="fuel"/> a fueled building can take now (0 if it burns something else, or is full).</summary>
    private static int FuelRoom(Simulation sim, Entity e, Defs.ThingDef fuel)
    {
        var w = sim.World;
        if (e.IsNull || w.Has<Construction>(e) || !w.TryGet<Fueled>(e, out var fueled)
            || w.Get<Thing>(e).Def.Building?.Fuel is not { } props || !props.Takes(fuel))
            return 0;
        return (int)MathF.Ceiling(props.Capacity - fueled.Fuel);
    }

    private static bool IsContainer(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.Get<Thing>(e).Def.Building is { StorageStacks: > 0 } && !sim.World.Has<Construction>(e);

    private static bool NeedsMaterial(Construction c, Defs.ThingDef def)
    {
        var cost = c.Target.Building!.Cost;
        for (var k = 0; k < cost.Count; k++)
            if (cost[k].ThingDef == def && c.Delivered[k] < cost[k].Count)
                return true;
        return false;
    }

    /// <summary>
    /// Work that chains (<see cref="Avatar.Chain"/>): building (handing over what you carry to the
    /// next blueprint when it still needs it) and beating out fires.
    /// </summary>
    private static AvatarActionKind ChainOf(AvatarActionKind kind) => kind switch
    {
        AvatarActionKind.Build or AvatarActionKind.Deliver => AvatarActionKind.Build, // keep F held after delivering and it's built
        AvatarActionKind.Extinguish => AvatarActionKind.Extinguish,
        _ => AvatarActionKind.None,
    };

    /// <summary>
    /// With the key still held and the last piece of chained work done, start on the next one in
    /// reach: the nearest such cell (same level first, then in cell order, so it's deterministic).
    /// A build chain also delivers what you carry to a blueprint that needs it, then builds it.
    /// </summary>
    private static void ContinueChain(Simulation sim, Entity e, ref Avatar a)
    {
        if (a.Chain == AvatarActionKind.None || !a.WorkHeld || sim.World.Get<Mind>(e).Job != null)
            return;
        var here = sim.World.Get<Position>(e).Cell;
        AvatarAction? deliver = null;
        foreach (var dz in (ReadOnlySpan<int>)[0, 1, -1])
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var cell = here + new int3(dx, dy, dz);
                    if (!sim.Map.InBounds(cell) || !InReach(here, cell))
                        continue;
                    var action = ActionAt(sim, e, cell);
                    if (action.Kind == a.Chain)
                    {
                        Perform(sim, e, action);
                        return;
                    }
                    if (a.Chain == AvatarActionKind.Build && action.Kind == AvatarActionKind.Deliver && deliver == null)
                        deliver = action;
                }
        if (deliver is { } d)
        {
            Perform(sim, e, d); // the site may be ready next tick, and the chain builds it then
            return;
        }
        a.Chain = AvatarActionKind.None; // nothing left in reach
    }

    /// <summary>
    /// A click-to-move walk got within reach of what was clicked: do what the work key would there
    /// (with the mouse button standing in for it). Nothing to do from beside a cell you can stand
    /// in (a bed) steps onto it first. Windows are the game's to open, so those just stop the walk.
    /// </summary>
    private static void ActOnArrival(Simulation sim, Entity e, AvatarWalkJob walk)
    {
        var w = sim.World;
        var mind = w.Get<Mind>(e);
        var action = ActionAt(sim, e, walk.Target);
        if (action.Kind == AvatarActionKind.None && !walk.OnTile && w.Get<Position>(e).Cell != walk.Target
            && sim.Map.InBounds(walk.Target) && sim.Map.Standable(sim.Map.Idx(walk.Target)))
        {
            walk.StepOnto();
            return;
        }
        if (action.Kind != AvatarActionKind.None && !action.OpensWindow)
            Perform(sim, e, action);
        if (mind.Job == walk)
            AiSystem.EndJob(sim, e, mind, true);
    }

    /// <summary>
    /// Can the avatar eat this now: food its kind eats, in its hands or pack, in a container in
    /// reach, or lying in reach, and nobody else's?
    /// </summary>
    public static bool CanEat(Simulation sim, Entity avatar, Entity food)
    {
        var w = sim.World;
        if (!w.IsAlive(food) || !w.TryGet<Thing>(food, out var t) || !t.Def.IsFood || !w.Has<Stack>(food))
            return false;
        if (!t.Def.EdibleBy(w.Get<Creature>(avatar).Def))
            return false;
        return CanEatFrom(sim, avatar, food) && !sim.Reservations.IsReservedByOther(food, avatar);
    }

    internal static bool CanEatFrom(Simulation sim, Entity avatar, Entity food)
    {
        var w = sim.World;
        if (w.Get<Carrying>(avatar).Item == food || Inventory.InOwnPack(sim, avatar, food))
            return true;
        if (w.TryGet<Stored>(food, out var stored))
            return Inventory.CanReachContainer(sim, avatar, stored.In);
        var t = w.Get<Thing>(food);
        return t.Spawned && InReach(w.Get<Position>(avatar).Cell, t.Cell);
    }

    /// <summary>Carry out an action from <see cref="ActionAt"/>: instant ones now, the rest as a job.</summary>
    public static void Perform(Simulation sim, Entity avatar, AvatarAction action)
    {
        var w = sim.World;
        w.Get<Avatar>(avatar).Chain = ChainOf(action.Kind);
        ref var carrying = ref w.Get<Carrying>(avatar);
        switch (action.Kind)
        {
            case AvatarActionKind.PickUp when w.Get<Thing>(action.Target).Def.Tool != null && Toolbelt.HasRoom(sim, avatar):
                Toolbelt.PutOn(sim, avatar, action.Target);
                break;
            case AvatarActionKind.PickUp:
                carrying.Item = Inventory.PickUp(sim, avatar, action.Target, carrying.Item);
                break;
            case AvatarActionKind.Equip:
                Inventory.PutOn(sim, avatar, sim.TakeFromStack(action.Target, 1));
                break;
            case AvatarActionKind.PutDown:
                sim.PlaceItemNear(carrying.Item, action.Cell);
                carrying.Item = Entity.Null;
                break;
            case AvatarActionKind.Store:
                carrying.Item = sim.Store(action.Target, carrying.Item);
                break;
            case AvatarActionKind.Deliver:
            {
                var left = Deliver(sim, action.Target, action.Item);
                if (carrying.Item == action.Item)
                    carrying.Item = left;
                break;
            }
            case AvatarActionKind.Refuel:
            {
                var fuel = action.Item;
                var take = Math.Min(FuelRoom(sim, action.Target, w.Get<Thing>(fuel).Def), w.Get<Stack>(fuel).Count);
                var props = w.Get<Thing>(action.Target).Def.Building!.Fuel!;
                ref var fueled = ref w.Get<Fueled>(action.Target);
                fueled.Fuel = Math.Min(props.Capacity, fueled.Fuel + take);
                if (take > 0)
                    fueled.Loaded = w.Get<Thing>(fuel).Def;
                if ((w.Get<Stack>(fuel).Count -= take) <= 0)
                {
                    w.Destroy(fuel);
                    if (carrying.Item == fuel)
                        carrying.Item = Entity.Null;
                }
                break;
            }
            case AvatarActionKind.Build:
                AiSystem.Order(sim, avatar, new BuildJob(action.Target));
                break;
            case AvatarActionKind.Extinguish:
                AiSystem.Order(sim, avatar, new FightFireJob(action.Cell));
                break;
            case AvatarActionKind.Pour:
                Work.PourSession.TryStart(sim, avatar, action.Target);
                break;
            case AvatarActionKind.Smith:
                Work.Smithing.Start(sim, avatar, action.Target);
                break;
            case AvatarActionKind.Cook:
                Work.Cooking.Start(sim, avatar, action.Target);
                break;
            case AvatarActionKind.BarWork:
                Work.BarWork.Start(sim, avatar, action.Target);
                break;
            case AvatarActionKind.Craft:
                if (w.Get<Workbench>(action.Target).Active is { } recipe)
                    Crafting.WorkAt(sim, avatar, action.Target, recipe);
                break;
            case AvatarActionKind.Harvest:
                AiSystem.Order(sim, avatar, new HarvestJob(action.Target));
                break;
            case AvatarActionKind.Dismantle:
                sim.Map.Designate(sim.Map.Idx(action.Cell), Designation.Deconstruct);
                AiSystem.Order(sim, avatar, new DeconstructJob(action.Target));
                break;
            case AvatarActionKind.Hunt:
                AiSystem.Order(sim, avatar, new AvatarHuntJob(action.Target));
                break;
            case AvatarActionKind.Butcher:
                AiSystem.Order(sim, avatar, new ButcherJob(action.Target));
                break;
            case AvatarActionKind.Carry:
                Rescues.Carry(sim, avatar, action.Target, action.Cell);
                break;
            case AvatarActionKind.Fish:
                AiSystem.Order(sim, avatar, new FishJob(action.Cell, ordered: false));
                break;
            case AvatarActionKind.Climb:
                AiSystem.Order(sim, avatar, new ClimbJob(action.Cell));
                break;
            case AvatarActionKind.Dig:
                sim.Map.Designate(sim.Map.Idx(action.Cell), Designation.Dig);
                AiSystem.Order(sim, avatar, new DigJob(action.Cell));
                break;
            case AvatarActionKind.Sleep:
                // Sleeping in a bed makes it yours, so newcomers look for another (see SettlerSystem).
                if (!action.Target.IsNull && (!w.TryGet<BedOwner>(avatar, out var own) || !w.IsAlive(own.Bed)))
                    w.Add(avatar, new BedOwner { Bed = action.Target });
                AiSystem.Order(sim, avatar, new SleepJob(action.Target)); // a null bed: on the ground where you stand
                break;
        }
    }

    /// <summary>Put held material into a construction site; returns what's left in hand.</summary>
    private static Entity Deliver(Simulation sim, Entity site, Entity held)
    {
        var w = sim.World;
        var construction = w.Get<Construction>(site);
        var thing = w.Get<Thing>(held);
        var cost = construction.Target.Building!.Cost;
        ref var stack = ref w.Get<Stack>(held);
        for (var k = 0; k < cost.Count && stack.Count > 0; k++)
        {
            if (cost[k].ThingDef != thing.Def)
                continue;
            var take = Math.Min(stack.Count, cost[k].Count - construction.Delivered[k]);
            if (take <= 0)
                continue;
            construction.Delivered[k] += take;
            stack.Count -= take;
            if (thing.Material is { } m)
                construction.AddMaterial(m, take);
        }
        if (stack.Count > 0)
            return held;
        w.Destroy(held);
        return Entity.Null;
    }
}

/// <summary>
/// The avatar eats food it picked to eat (<see cref="Core.EatCommand"/>): from its hands or pack,
/// a container beside it, or the ground in reach. Walking off (or away from it) stops the meal.
/// </summary>
public sealed class EatHeldJob(Entity food) : Job
{
    public override Entity Focus => food;

    protected override IEnumerable<JobStatus> Run()
    {
        if (!Alive(food))
            yield break;
        var def = Get<Thing>(food).Def;
        // What isn't on you could be taken while you eat; on you, nobody else can reach it.
        if (!Inventory.OnPerson(Sim, Pawn, food) && !Reserve(food))
            yield break;
        Report = $"Eating {def.Label}";
        foreach (var s in Wait(90)) yield return s;
        if (!Alive(food) || Get<Thing>(food).Def != def || !AvatarSystem.CanEatFrom(Sim, Pawn, food))
        {
            Fail("food is gone");
            yield break;
        }
        var inHand = Get<Carrying>(Pawn).Item == food;
        var count = Math.Min(EatJob.Wanted(Sim, Pawn, def), Get<Stack>(food).Count);
        var eaten = Sim.TakeFromStack(food, count);
        if (inHand && eaten == food)
            Get<Carrying>(Pawn).Item = Entity.Null;
        var rot = FoodPoisoning.RotOf(Sim, eaten);
        Sim.World.Destroy(eaten);
        EatJob.Nourish(Sim, Pawn, def, count, rot);
    }
}

/// <summary>
/// Click-to-move: walk to a cell, or (<see cref="Interact"/>) up to what's there, where
/// <see cref="AvatarSystem"/> then does what the work key would. Holding the button and moving
/// the cursor moves <see cref="Target"/> (<see cref="Retarget"/>) without restarting the walk: the
/// step under way finishes and the route goes on from there, so the character never jerks back.
/// Not saved, like every job: a load stops the walk.
/// </summary>
public sealed class AvatarWalkJob(int3 target, bool interact) : Job
{
    public int3 Target { get; private set; } = target;
    public bool Interact { get; } = interact;

    /// <summary>In reach of what was clicked: AvatarSystem acts on it at its next update.</summary>
    public bool ReadyToAct { get; private set; }

    /// <summary>Walking onto the target cell rather than up to it.</summary>
    public bool OnTile { get; private set; } = !interact;

    private bool _replan = true;

    public override int3? FocusCell => Interact ? Target : null;

    public void Retarget(int3 cell)
    {
        if (cell == Target)
            return;
        Target = cell;
        OnTile = !Interact;
        _replan = true;
    }

    /// <summary>Nothing to do from beside it: walk onto the cell itself (a bed you sleep in).</summary>
    public void StepOnto()
    {
        OnTile = true;
        ReadyToAct = false;
        _replan = true;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        var mover = Get<Mover>(Pawn);
        while (true)
        {
            Report = Interact ? "Walking over" : "Walking";
            if (_replan)
            {
                _replan = false;
                ReadyToAct = false;
                if (!Replan(mover))
                {
                    Fail($"no path to {Target}");
                    yield break;
                }
            }
            if (!mover.Moving)
            {
                if (!Arrived(Target, OnTile ? PathEnd.OnTile : PathEnd.Touch))
                {
                    Fail("could not reach destination");
                    yield break;
                }
                if (!Interact)
                    yield break;
                ReadyToAct = true;
            }
            yield return JobStatus.Running;
        }
    }

    /// <summary>
    /// Route to the target from the step under way (kept, so it finishes smoothly) or from where
    /// the pawn stands. A plain walk to a cell nobody can stand in goes up to it instead.
    /// </summary>
    private bool Replan(Mover mover)
    {
        var midStep = mover.Moving && mover.StepProgress > 0;
        var from = midStep ? mover.Path![mover.PathIndex] : PawnCell;
        var route = Route(from, OnTile ? PathEnd.OnTile : PathEnd.Touch);
        if (route == null && OnTile && !Interact)
        {
            OnTile = false;
            route = Route(from, PathEnd.Touch);
        }
        if (route == null && !Interact && NearestReachable(from) is { } near)
        {
            // Somewhere you can't get to (across water, a closed-off room): as close as you can.
            (Target, OnTile) = (near, true);
            route = Route(from, PathEnd.OnTile);
        }
        if (route == null)
            return false;
        if (midStep)
        {
            route.Insert(0, from);
            mover.Path = route;
            mover.PathIndex = 0; // StepFrom and StepProgress carry on
        }
        else
        {
            mover.Path = route;
            mover.PathIndex = 0;
            mover.StepProgress = 0;
            mover.StepFrom = from;
        }
        return true;
    }

    /// <summary>How far round an unreachable click to look for somewhere to stand instead.</summary>
    public const int NearbyRadius = 6;

    private int3? NearestReachable(int3 from)
    {
        foreach (var c in Sim.SpiralFrom(Target, NearbyRadius))
            if (c != from && Sim.Map.Passable(c) && Navigation.CanReach(Sim, Pawn, from, c, PathEnd.OnTile))
                return c;
        return null;
    }

    private List<int3>? Route(int3 from, PathEnd end)
    {
        var done = end == PathEnd.OnTile ? from == Target : Pathfinder.Touches(from, Target);
        return done ? [] : Navigation.PlanRoute(Sim, Pawn, from, Target, end);
    }
}

/// <summary>
/// A player climbs up onto solid furniture beside them (<see cref="AvatarActionKind.Climb"/>): one
/// slow step into its cell, let through by <see cref="Perched"/>. Stepping off is an ordinary step,
/// which takes the perch away again.
/// </summary>
public sealed class ClimbJob(int3 cell) : Job
{
    public override int3? FocusCell => cell;

    protected override IEnumerable<JobStatus> Run()
    {
        var mover = Get<Mover>(Pawn);
        while (mover.Moving)
            yield return JobStatus.Running; // the step under way first
        var map = Sim.Map;
        var piece = map.InBounds(cell) ? map.Structure[map.Idx(cell)] : Entity.Null;
        if (!AvatarSystem.InReach(PawnCell, cell) || cell.Z != PawnCell.Z || !Alive(piece)
            || Get<Thing>(piece).Def is not { Solid: true, Climbable: true } || Sim.World.Has<Construction>(piece))
        {
            Fail("nothing to climb");
            yield break;
        }
        Report = $"Climbing onto the {Get<Thing>(piece).Label}";
        Sim.World.Add(Pawn, new Perched { Cell = cell });
        mover.Path = [cell];
        mover.PathIndex = 0;
        mover.StepProgress = 0;
        mover.StepFrom = PawnCell;
        while (mover.Moving)
            yield return JobStatus.Running;
        if (PawnCell != cell)
        {
            Sim.World.Remove<Perched>(Pawn);
            Fail("couldn't climb up");
        }
    }
}
