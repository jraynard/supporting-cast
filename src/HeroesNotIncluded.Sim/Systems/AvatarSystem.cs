using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;

namespace HeroesNotIncluded.Sim.Systems;

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

    /// <summary>No longer offered: a fight (or a hunt) is the mouse's, weapon drawn (<see cref="Combat.Blows"/>). Kept for the enum's saved values.</summary>
    Hunt,
    /// <summary>Cut up an animal's carcass into meat and hide (<see cref="Ai.ButcherJob"/>), while the key is held.</summary>
    Butcher,
    /// <summary>Pick up someone downed (or a carcass, for the butcher spot), or set down what's in your arms (<see cref="Rescues"/>), done at once.</summary>
    Carry,

    /// <summary>Draw a drink from a cask (<see cref="Work.PourSession"/>), begun at once.</summary>
    Pour,

    /// <summary>Climb up onto a table or counter beside you (<see cref="ClimbJob"/>); any step takes you down.</summary>
    Climb,

    /// <summary>The next step with the piece in your tongs at a smithy station (<see cref="Work.Smithing.Start"/>), begun at once.</summary>
    Smith,

    /// <summary>At a stew pot: put food in, tend it, or ladle a bowl (<see cref="Work.Cooking.Start"/>); at the prep table, pack rations (<see cref="Work.Rations.Start"/>). Begun at once.</summary>
    Cook,

    /// <summary>Wash dirty crockery at the tub, knock a keg into a cask or make up a bed (<see cref="Work.BarWork.Start"/>), begun at once.</summary>
    BarWork,

    /// <summary>Clean up the mess on a cell (<see cref="Hospitality.ScrubSession"/>), begun at once.</summary>
    Clean,

    /// <summary>Read a shop's sign on its front wall (<see cref="Shops.Signs"/>): the game opens it.</summary>
    ReadSign,

    /// <summary>A door's lock or a party's wagon (<see cref="Tinkering.Tinkers.ActionAt"/>): the game opens what can be done there.</summary>
    Tinker,

    /// <summary>Read the notice board (<see cref="Favours.FavourBoard"/>): the game reads it and opens the favours.</summary>
    ReadBoard,

    /// <summary>Brace a span a crisis broke (<see cref="Tinkering.SpanSession"/>), begun at once: the tinker's.</summary>
    Mend,

    /// <summary>Order a meal at a serving hatch (<see cref="Shops.ShopSystem.PlayerOrder"/>), or see where you are in its line.</summary>
    OrderMeal,

    /// <summary>Have a go at a festival game (<see cref="Work.FestivalGames"/>), begun at once.</summary>
    FestivalGame,

    /// <summary>Join the dance round the festival's bonfire (<see cref="Ai.FestivalJob"/>), at once.</summary>
    Dance,
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
        or AvatarActionKind.Store or AvatarActionKind.OpenStorage or AvatarActionKind.Tinker
        or AvatarActionKind.Refuel or AvatarActionKind.Equip or AvatarActionKind.OpenStation
        or AvatarActionKind.Carry or AvatarActionKind.Pour or AvatarActionKind.Cook or AvatarActionKind.BarWork or AvatarActionKind.Clean
        or AvatarActionKind.Mend or AvatarActionKind.OrderMeal or AvatarActionKind.FestivalGame or AvatarActionKind.Dance;

    /// <summary>Handled by the game rather than the sim: it opens a window (or the dialogue, or the "set out?" question).</summary>
    public bool OpensWindow => Kind is AvatarActionKind.OpenStorage or AvatarActionKind.OpenStation or AvatarActionKind.ReadSign or AvatarActionKind.Tinker
        or AvatarActionKind.ReadBoard;
}

/// <summary>
/// Adventure mode: drives creatures marked <see cref="Avatar"/> from player input. They move freely
/// (<see cref="Combat.FreeMoveSystem"/>); walking off abandons the work in hand, and the work key
/// starts an ordinary job on the cell in reach, which only progresses while the key is held (see
/// <see cref="Job"/>.Work). Runs before <see cref="AiSystem"/>.
/// </summary>
public sealed class AvatarSystem : ISimSystem
{
    public string Name => "Avatar";
    public int Interval => 1;

    /// <summary>
    /// What F at a serving hatch says about a meal: the price to order one, or where you are in the
    /// line for the one you ordered. Null where it isn't yours to order (the cook's, or none served).
    /// </summary>
    private static string? MealOrder(Simulation sim, Entity avatar, Entity hatch)
    {
        var inn = Shops.Hatch.ShopOf(sim, hatch);
        var innDef = inn.IsNull ? null : sim.World.Get<PresetBuilding>(inn).Def;
        if (innDef == null || !innDef.HatchServes.Contains(ServiceKind.Meal)
            || sim.World.TryGet<Trade>(avatar, out var trade) && trade.Def.WorkplaceDef == innDef && trade.Def.Serving(ServiceKind.Meal))
            return null;
        if (sim.Shops.OpenOrderOf(avatar, ServiceKind.Meal) != null)
        {
            var place = sim.Shops.LineAt(sim, inn, hatch: true).IndexOf(avatar);
            return place <= 0 ? "Your meal's ordered: wait here for it" : $"Your meal's ordered: {Ordinal(place + 1)} in line";
        }
        return Shops.ShopPrices.Posted(sim, inn, ServiceKind.Meal) is { Offered: true } sign ? $"Order a meal ({sign.Price} silver)" : null;
    }

    private static string Ordinal(int n) => n switch { 2 => "2nd", 3 => "3rd", _ => $"{n}th" };

    /// <summary>
    /// The player can command their dragon in combat once its affinity with them reaches this
    /// (the same bond that lets them ride it).
    /// </summary>
    public const float CommandDragonAffinity = 0.5f;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Query<Avatar>())
        {
            var c = w.Get<Creature>(e);
            if (c.Dead || c.Downed)
            {
                // Falling down drops what you hold, and whoever's in your arms.
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
            if (!w.Has<Combat.FreeMover>(e))
                Combat.FreeMoveSystem.Join(sim, e);
            // The keys move it (Combat.FreeMoveSystem), and a job's walk (a click, the way to a
            // station) follows its path there while they're let be.
            if (w.Get<Combat.FreeMover>(e).Wish == float2.Zero)
            {
                if (w.Get<Mind>(e).Job is AvatarWalkJob { ReadyToAct: true } arrived)
                    ActOnArrival(sim, e, arrived);
                else
                    ContinueChain(sim, e, ref a);
                continue;
            }
            a.Chain = AvatarActionKind.None; // walking off ends a run of work
            var busy = w.Get<Mind>(e);
            if (busy.Job != null)
                AiSystem.EndJob(sim, e, busy, false); // walking off abandons the work
            w.Get<Mover>(e).Stop();
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
        // Arms full of a creature (someone hurt for a bed, a carcass for the butcher spot): set it down.
        if (Rescues.CarriedBy(sim, avatar) is { IsNull: false } load)
            return tool is AvatarTool.Use or AvatarTool.Drop && InReach(here, cell) ? Rescues.SetDownAt(sim, load, cell) : AvatarAction.None;
        // Game: shot from as far as the bow carries (or struck from beside it); a carcass in reach is butchered.
        if (tool == AvatarTool.Use && Ai.AvatarHunting.ActionAt(sim, avatar, cell, here) is { } hunting)
            return hunting;
        // A gap a crisis tore (the bridge): the tinker braces it.
        if (tool == AvatarTool.Use && sim.Repairs.At(cell) is { } breach && InReach(here, cell)
            && w.TryGet<Trade>(avatar, out var mender) && mender.Def.MendsTown)
            return new(AvatarActionKind.Mend, $"Brace the {breach.Label}", cell, Entity.Null);
        // Anyone downed and small enough to carry to a bed.
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
            return Round.RoundSystem.MayGather(sim, avatar) && map.IsSolid(i) && sim.CanDig(cell) && Mining.CanBreak(sim, avatar, map.Block[i])
                ? new(AvatarActionKind.Dig, $"Dig {Mining.ShownBlock(sim, i).Label}", cell, Entity.Null)
                : AvatarAction.None;

        // A fire comes before anything else there: you can't chop a burning tree or pick up a burning stack.
        if (map.Fire[i] > 0 && !sim.Reservations.IsTileReservedByOther(cell, avatar))
            return new(AvatarActionKind.Extinguish, "Beat out the fire", cell, Entity.Null);

        // A shop's sign on its front wall, read from the street.
        if (Shops.Signs.At(sim, cell) is { IsNull: false } signed && Buildings.At(sim, here) != signed)
            return new(AvatarActionKind.ReadSign, "Read the sign", cell, signed);

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

        // The festival: a game to have a go at, the dance round the bonfire.
        if (tool == AvatarTool.Use && Work.FestivalGames.StepAt(sim, avatar, site) is { } game)
            return new(AvatarActionKind.FestivalGame, game, cell, site);
        if (tool == AvatarTool.Use && held.IsNull && Ai.Festival.CanDance(sim, avatar, site))
            return new(AvatarActionKind.Dance, "Dance round the bonfire", cell, site);

        // A piece in the tongs goes to the fire, the anvil or the tub; a blade to the grindstone.
        if (Work.Smithing.StepAt(sim, avatar, site) is { } step && !sim.Reservations.IsReservedByOther(site, avatar))
            return new(AvatarActionKind.Smith, step, cell, site);

        // A stew pot takes the food you carry, wants tending, or gives a bowl.
        if (Work.Cooking.StepAt(sim, avatar, site) is { } cooking && !sim.Reservations.IsReservedByOther(site, avatar))
            return new(AvatarActionKind.Cook, cooking, cell, site);
        if (Work.Rations.StepAt(sim, avatar, site) is { } packing && !sim.Reservations.IsReservedByOther(site, avatar))
            return new(AvatarActionKind.Cook, packing, cell, site);

        // Dirty crockery to the wash tub; a keg in your arms to a dry cask.
        if (Work.BarWork.StepAt(sim, avatar, site) is { } upkeep && !sim.Reservations.IsReservedByOther(site, avatar))
            return new(AvatarActionKind.BarWork, upkeep, cell, site);

        // The notice board: read what's pinned to it, and pin your own.
        if (tool == AvatarTool.Use && Favours.FavourBoard.IsBoard(sim, site))
            return new(AvatarActionKind.ReadBoard, "Read the notice board", cell, site);

        // A door's lock (locked, or one you could fit or turn), or a party's wagon: what can be done there.
        if (tool == AvatarTool.Use && Tinkering.Tinkers.ActionAt(sim, avatar, site) is { } tinkering)
            return new(AvatarActionKind.Tinker, tinkering, cell, site);

        // A craft that has everything it needs is worked on the spot.
        if (Crafting.IsStation(sim, site) && w.Get<Workbench>(site) is { Active: { } making, Stocked: true } ready
            && (ready.By == avatar || (ready.By.IsNull && Crafting.CanMake(sim, avatar, making)))
            && !sim.Reservations.IsReservedByOther(site, avatar))
            return new(AvatarActionKind.Craft, $"Make {making.Label}", cell, site);

        // Marked to come apart (the Deconstruct tool, or a container's window): F does the work,
        // whatever's in your hands. Unmarked, F never takes anything apart, so an empty wagon kept
        // for storage or a fire you meant to feed is safe from a stray key press.
        // The mark is on the thing's own cell (its anchor), wherever on a wide one you point.
        if (sim.DeconstructTarget(cell) is { IsNull: false } marked && w.TryGet<Thing>(marked, out var markedThing)
            && (map.Designations[map.Idx(markedThing.Cell)] & Designation.Deconstruct) != 0 && CanDismantle(sim, avatar, marked))
            return new(AvatarActionKind.Dismantle, $"Take the {w.Get<Thing>(marked).Label} apart", cell, marked);

        // Things only items in hand (or in the pack) can do: feed a construction site or a fire.
        // (Eating isn't one: it's picked from the item itself, so F never eats what you meant to use.)
        if (!site.IsNull && w.TryGet<Construction>(site, out var construction)
            && Supply(sim, avatar, d => NeedsMaterial(construction, d)) is { IsNull: false } material)
            return new AvatarAction(AvatarActionKind.Deliver, $"Deliver {ItemLabel(sim, avatar, material)}", cell, site) { Item = material };
        if (Supply(sim, avatar, d => FuelRoom(sim, site, d) > 0) is { IsNull: false } fuel && FeedsFrom(sim, avatar, site, fuel))
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
        // A trade's stash (the innkeeper's cudgel under the bar) is theirs alone: anyone else doesn't
        // know it's there. Its holder reaches in for what's kept there, or puts it back.
        var stash = Militia.Stashes.IsStash(sim, site);
        if (stash && Militia.Stashes.Reaches(sim, avatar, site))
        {
            if (!held.IsNull && sim.HasRoomFor(site, w.Get<Thing>(held).Def, w.Get<Thing>(held).Material))
                return new(AvatarActionKind.Store, $"Put {w.Get<Thing>(held).Label} back under the {w.Get<Thing>(site).Label}", cell, site);
            if (sim.StoredIn(site).FirstOrDefault(e => w.Get<Thing>(e).Def.Weapon != null, Entity.Null) is { IsNull: false } kept
                && !sim.Reservations.IsReservedByOther(kept, avatar))
                return new(AvatarActionKind.Equip, $"Reach under the {w.Get<Thing>(site).Label} for {w.Get<Thing>(kept).Label}", cell, kept);
        }
        // The muster rack takes weapons only, and the town may pay for them.
        var rack = Militia.MilitiaSystem.IsRack(sim, site);
        if (!held.IsNull && rack && Militia.MilitiaSystem.IsWeapon(sim, held) && sim.HasRoomFor(site, w.Get<Thing>(held).Def, w.Get<Thing>(held).Material))
            return new(AvatarActionKind.Store, sim.Militia.RackLabel(sim, held), cell, site);
        if (!held.IsNull && !rack && !stash && IsContainer(sim, site) && sim.HasRoomFor(site, w.Get<Thing>(held).Def, w.Get<Thing>(held).Material))
            return new(AvatarActionKind.Store, w.Get<Thing>(site).Def.Building is { Hatch: true }
                ? $"Put {w.Get<Thing>(held).Label} up on the {w.Get<Thing>(site).Label}"
                : $"Put {w.Get<Thing>(held).Label} in the {w.Get<Thing>(site).Label}", cell, site);
        // At the inn's serving hatch with empty hands: order a meal there (not the cook: they serve it).
        if (held.IsNull && !site.IsNull && w.Get<Thing>(site).Def.Building is { Hatch: true } && MealOrder(sim, avatar, site) is { } meal)
            return new(AvatarActionKind.OrderMeal, meal, cell, site);
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
            if (rack)
                return new(AvatarActionKind.OpenStorage, sim.Militia.Mustering ? "Take up arms from the muster rack" : "Look over the muster rack", cell, site);
            if (IsContainer(sim, site) && !stash)
                return new(AvatarActionKind.OpenStorage, $"Open the {w.Get<Thing>(site).Label}", cell, site);
        }

        // Mess on the floor: mop it up.
        if (sim.Mess.At(sim, cell) is { } mess && !w.Has<Work.WorkSession>(avatar))
            return new(AvatarActionKind.Clean, Hospitality.MessSystem.Verb(mess.Kind), cell, Entity.Null);

        // Work on what's there: hands full or not (you can chop wood with a sack on your back).
        if (!site.IsNull && !sim.Reservations.IsReservedByOther(site, avatar))
        {
            var thing = w.Get<Thing>(site);
            if (w.TryGet<Construction>(site, out var c))
            {
                if ((c.MaterialsComplete || (Buildings.IsPhaseSite(sim, site) && Buildings.HasWork(c))) && sim.CanBuildNow(site))
                    return new(AvatarActionKind.Build, $"Build {c.Target.Label}", cell, site);
            }
            else if (thing.Def.Harvest is { } harvest && (!w.TryGet<PlantState>(site, out var plant) || plant.Ready)
                && Round.RoundSystem.MayGather(sim, avatar))
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

    /// <summary>
    /// Should F feed this fuel to the fire? Always from the hands. From the pack, a work station
    /// (the forge) is fed only once it's gone out: otherwise the charcoal you brought for a bloom
    /// goes on the fire, and F never gets as far as opening the station.
    /// </summary>
    private static bool FeedsFrom(Simulation sim, Entity avatar, Entity site, Entity fuel) =>
        sim.World.Get<Carrying>(avatar).Item == fuel || !Crafting.IsStation(sim, site) || sim.World.Get<Fueled>(site).Fuel <= 0;

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
            {
                var put = carrying.Item;
                carrying.Item = sim.Store(action.Target, carrying.Item);
                if (carrying.Item != put && Militia.MilitiaSystem.IsRack(sim, action.Target))
                    sim.Militia.Racked(sim, avatar, put);
                break;
            }
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
                if (Work.Rations.IsPrepTable(sim, action.Target))
                    Work.Rations.Start(sim, avatar, action.Target);
                else
                    Work.Cooking.Start(sim, avatar, action.Target);
                break;
            case AvatarActionKind.BarWork:
                Work.BarWork.Start(sim, avatar, action.Target);
                break;
            case AvatarActionKind.FestivalGame:
                Work.FestivalGames.Start(sim, avatar, action.Target);
                break;
            case AvatarActionKind.Dance:
                if (Ai.Festival.Dance(sim, avatar, action.Target) is { } dance)
                    AiSystem.Order(sim, avatar, dance);
                break;
            case AvatarActionKind.OrderMeal:
                if (Shops.Hatch.ShopOf(sim, action.Target) is { IsNull: false } inn && sim.Shops.OpenOrderOf(avatar, ServiceKind.Meal) == null
                    && sim.Shops.PlayerOrder(sim, avatar, ServiceKind.Meal, inn) is { } why)
                    sim.Messages.Add(MessageKind.Info, $"Can't order: {why}.", action.Cell);
                break;
            case AvatarActionKind.Mend:
                Tinkering.Repairs.StartMend(sim, avatar, sim.Repairs.At(action.Cell));
                break;
            case AvatarActionKind.Clean:
                if (Hospitality.ScrubSession.For(sim, avatar, action.Cell) is { } scrub)
                    Work.WorkSystem.Start(sim, avatar, scrub);
                break;
            case AvatarActionKind.Craft:
                if (w.Get<Workbench>(action.Target).Active is { } recipe)
                    Crafting.WorkAt(sim, avatar, action.Target, recipe);
                break;
            case AvatarActionKind.Harvest:
                AiSystem.Order(sim, avatar, new HarvestJob(action.Target));
                break;
            case AvatarActionKind.Dismantle:
                sim.Map.Designate(sim.Map.Idx(w.Get<Thing>(action.Target).Cell), Designation.Deconstruct);
                AiSystem.Order(sim, avatar, new DeconstructJob(action.Target));
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
    /// the pawn stands. A free mover steers from where it really is (<see cref="Combat.FreeMoveSystem"/>),
    /// so it turns at once rather than finishing the step. A plain walk to a cell nobody can stand
    /// in goes up to it instead.
    /// </summary>
    private bool Replan(Mover mover)
    {
        var midStep = mover.Moving && mover.StepProgress > 0 && !Sim.World.Has<Combat.FreeMover>(Pawn);
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
