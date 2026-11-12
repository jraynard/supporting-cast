using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim;

/// <summary>Spawning, moving and destroying things and creatures.</summary>
public sealed partial class Simulation
{
    // ---------------- Structures ----------------

    /// <summary>
    /// Can a blueprint (or finished thing) of <paramref name="def"/> go in this cell? Floors need an
    /// empty floor slot and support nearby; everything else needs an empty structure slot and
    /// something to stand on (buildable ground, a floor, or for walls another wall below).
    /// Blueprints count as support here, so whole storeys can be planned at once;
    /// <see cref="CanBuildNow"/> holds construction until the real support exists.
    /// </summary>
    public bool CanPlaceStructure(int3 cell, ThingDef? def = null, int facing = 0)
    {
        if (def != null && Footprint.IsWide(def) && !def.IsFloor)
        {
            foreach (var c in FootprintCells(def, cell, facing))
                if (!CanPlaceStructureCell(c, def))
                    return false;
            return true;
        }
        return CanPlaceStructureCell(cell, def);
    }

    /// <summary>
    /// The cells a structure anchored at <paramref name="anchor"/> and turned to <paramref name="facing"/>
    /// covers (<see cref="Maps.Footprint.Cells"/>), the anchor among them. Cells off the map come back
    /// as (-1, -1, -1) (placing then fails).
    /// </summary>
    public IEnumerable<int3> FootprintCells(ThingDef def, int3 anchor, int facing) =>
        Footprint.Cells(Map, def, anchor, facing);

    /// <summary>The cells a placed thing covers.</summary>
    public IEnumerable<int3> FootprintCells(Entity e)
    {
        var t = World.Get<Thing>(e);
        return FootprintCells(t.Def, t.Cell, t.Facing);
    }

    private bool CanPlaceStructureCell(int3 cell, ThingDef? def)
    {
        if (!Map.InBounds(cell))
            return false;
        var i = Map.Idx(cell);
        if (Map.IsSolid(i))
            return false;
        if (def?.IsRoof == true)
            return Map.Floor[i].IsNull && Map.Structure[i].IsNull && RoofOverAir(cell) && !StairsBelow(cell) && FloorSupported(cell, def, finishedOnly: false);
        if (def?.IsFloor == true)
            return Map.Floor[i].IsNull && !StairsBelow(cell) && FloorSupported(cell, def, finishedOnly: false);
        if (!Map.Structure[i].IsNull)
            return false;
        // Nothing goes up on a roof (finished or planned): it isn't a floor.
        if (!Map.Floor[i].IsNull && World.Get<Thing>(Map.Floor[i]).Def.IsRoof)
            return false;
        // Stairs need the cell above open: that's the stairwell.
        if (def?.Building?.Climb == ClimbKind.Ramp && cell.Z + 1 < Map.Depth && !Map.Floor[i + Map.LayerSize].IsNull)
            return false;
        if (!Map.Floor[i].IsNull)
            return true; // on a floor, finished or planned
        if (Map.GroundUnder(i) is { } ground)
            return ground is { CanBuildOn: true, Passable: true };
        return def?.Building?.IsWall == true && WallBelow(cell, finishedOnly: false);
    }

    /// <summary>Whether construction on a site can go ahead: whatever holds it up is finished.</summary>
    public bool CanBuildNow(Entity site)
    {
        var thing = World.Get<Thing>(site);
        if (thing.Def.PhaseOf != null)
            return Buildings.PhaseReady(this, site); // a whole phase: room for all its parts
        var cell = thing.Cell;
        var i = Map.Idx(cell);
        if (thing.Def.IsFloor)
            return FloorSupported(cell, thing.Def, finishedOnly: true);
        return Map.Standable(i) || (thing.Def.Building?.IsWall == true && WallBelow(cell, finishedOnly: true));
    }

    /// <summary>
    /// A roof shelters something: the cell below is open (not rock), and the roof itself isn't
    /// sitting on the ground (a cell you could stand in is a floor's place, not a roof's).
    /// </summary>
    private bool RoofOverAir(int3 cell)
    {
        if (cell.Z == 0)
            return false;
        var i = Map.Idx(cell);
        var below = i - Map.LayerSize;
        return !Map.IsSolid(below) && !Map.Standable(i);
    }

    /// <summary>Stairs (finished or planned) in the cell below: a floor there would close the stairwell.</summary>
    private bool StairsBelow(int3 cell)
    {
        if (cell.Z == 0)
            return false;
        var s = Map.Structure[Map.Idx(cell) - Map.LayerSize];
        return !s.IsNull && World.Get<Thing>(s).Def.Building?.Climb == ClimbKind.Ramp;
    }

    /// <summary>
    /// A floor is held up by solid ground or a wall one level down, within its support span.
    /// <paramref name="without"/> is a wall cell to pretend isn't there (checking a deconstruction).
    /// </summary>
    public bool FloorSupported(int3 cell, ThingDef floor, bool finishedOnly, int3? without = null)
    {
        var span = floor.Building!.SupportSpan;
        for (var dy = -span; dy <= span; dy++)
        for (var dx = -span; dx <= span; dx++)
        {
            var c = new int3(cell.X + dx, cell.Y + dy, cell.Z);
            if (!Map.InBounds(c) || c.Z == 0)
                continue;
            var below = Map.Idx(c) - Map.LayerSize;
            if (Map.IsSolid(below) || (WallBelow(c, finishedOnly) && Map.CellOf(below) != without))
                return true;
        }
        return false;
    }

    /// <summary>A wall or a door (its frame carries a lintel) directly below the cell.</summary>
    private bool WallBelow(int3 cell, bool finishedOnly)
    {
        if (cell.Z == 0)
            return false;
        var s = Map.Structure[Map.Idx(cell) - Map.LayerSize];
        if (s.IsNull || World.Get<Thing>(s).Def.Building is not ({ IsWall: true } or { IsDoor: true }))
            return false;
        return !finishedOnly || !World.Has<Construction>(s);
    }

    /// <summary>Spawn a finished building, floor, plant or resource in a cell.</summary>
    public Entity SpawnStructure(ThingDef def, int3 cell, int facing = 0)
    {
        var i = Map.Idx(cell);
        var layer = def.IsFloor ? Map.Floor : Map.Structure;
        if (!layer[i].IsNull)
            throw new InvalidOperationException($"Cell {cell} already has a {(def.IsFloor ? "floor" : "structure")}");

        if (Footprint.IsWide(def) && !def.IsFloor)
            foreach (var c in FootprintCells(def, cell, facing))
                if (!Map.InBounds(c) || !layer[Map.Idx(c)].IsNull)
                    throw new InvalidOperationException($"Cell {c} under a {def.Label} at {cell} is taken or off the map");

        var e = World.Create();
        World.Add(e, new Thing { Def = def, Cell = cell, Hp = def.MaxHp, Spawned = true, Facing = (byte)(facing & 3) });
        if (def.Harvest is { DestroyOnHarvest: false })
            World.Add(e, new PlantState());
        if (def.Building?.Fuel is { } fuel)
            World.Add(e, new Fueled { Fuel = fuel.Capacity * 0.5f });
        if (def.Building?.Workbench != null)
            World.Add(e, new Workbench());
        layer[i] = e;
        if (def.IsRoof)
            Map.HasRoof[i] = true;
        else if (def.IsFloor)
            Map.HasFloor[i] = true;
        Map.MarkStructureChanged(i);
        Cover(e, def, cell, facing);

        // Anything lying where a wall or solid furniture goes gets pushed aside.
        if (def.Impassable)
            PushItemsOff(def, cell, facing);
        return e;
    }

    /// <summary>Move items lying in a solid thing's cells to the nearest free spot beside it.</summary>
    private void PushItemsOff(ThingDef def, int3 cell, int facing)
    {
        foreach (var c in FootprintCells(def, cell, facing))
        {
            if (!Map.InBounds(c) || Map.Item[Map.Idx(c)] is not { IsNull: false } item)
                continue;
            DespawnThing(item);
            PlaceItemNear(item, c);
        }
    }

    /// <summary>
    /// Old saves (from before furniture was solid) can have items on tables and benches: put them
    /// down beside instead, where everyone can reach them. Nothing to do in a current save.
    /// </summary>
    internal void PushItemsOffFurniture()
    {
        foreach (var e in World.Query<Thing>())
        {
            var t = World.Get<Thing>(e);
            if (t.Spawned && t.Def.Solid && !World.Has<Construction>(e) && Map.InBounds(t.Cell) && Map.Structure[Map.Idx(t.Cell)] == e)
                PushItemsOff(t.Def, t.Cell, t.Facing);
        }
    }

    /// <summary>Place a construction site for <paramref name="def"/>. Returns Entity.Null if blocked.</summary>
    public Entity PlaceBlueprint(ThingDef def, int3 cell, int facing = 0)
    {
        if (def.Building == null || !CanPlaceStructure(cell, def, facing))
            return Entity.Null;
        var e = World.Create();
        World.Add(e, new Thing { Def = def, Cell = cell, Hp = def.MaxHp, Spawned = true, Facing = (byte)(facing & 3) });
        World.Add(e, new Construction { Target = def, Delivered = new int[def.Building.Cost.Count] });
        var i = Map.Idx(cell);
        (def.IsFloor ? Map.Floor : Map.Structure)[i] = e;
        Map.MarkStructureChanged(i);
        Cover(e, def, cell, facing);
        return e;
    }

    /// <summary>A structure wider than a cell holds the rest of its footprint too (<see cref="ThingDef.Size"/>).</summary>
    private void Cover(Entity e, ThingDef def, int3 anchor, int facing)
    {
        if (!Footprint.IsWide(def) || def.IsFloor)
            return;
        foreach (var c in FootprintCells(def, anchor, facing))
        {
            if (!Map.InBounds(c))
                continue;
            var j = Map.Idx(c);
            if (Map.Structure[j] == e)
                continue;
            Map.Structure[j] = e;
            Map.MarkStructureChanged(j);
        }
    }

    /// <summary>
    /// After a load: set <see cref="Map.HasRoof"/> from the roofs in the floor layer, and make every
    /// structure hold exactly its def's footprint. A save from before a def
    /// grew (a one-cell bed, now 1x2) takes the free cells it now covers (a blocked one is left to
    /// whatever holds it), and a def that shrank lets go of the cells it no longer covers. Changes
    /// nothing when the defs match the save, so a round trip keeps its hash.
    /// </summary>
    internal void RefitFootprints()
    {
        // Roof flags aren't saved: they follow the finished roofs in the floor layer.
        Array.Clear(Map.HasRoof);
        foreach (var e in World.Query<Thing>())
        {
            var t = World.Get<Thing>(e);
            if (t.Spawned && t.Def.IsRoof && !World.Has<Construction>(e) && Map.InBounds(t.Cell) && Map.Floor[Map.Idx(t.Cell)] == e)
                Map.HasRoof[Map.Idx(t.Cell)] = true;
        }

        foreach (var e in World.Query<Thing>())
        {
            var t = World.Get<Thing>(e);
            if (!t.Spawned || t.Def.Building == null || t.Def.IsFloor || !Map.InBounds(t.Cell) || Map.Structure[Map.Idx(t.Cell)] != e)
                continue;
            // Let go of cells outside the footprint (only the ones round it a shrunk def could have held).
            var reach = Math.Max(t.Def.SizeX, t.Def.SizeY) + 2;
            for (var dy = -reach; dy <= reach; dy++)
            for (var dx = -reach; dx <= reach; dx++)
            {
                var c = new int3(t.Cell.X + dx, t.Cell.Y + dy, t.Cell.Z);
                if ((dx == 0 && dy == 0) || !Map.InBounds(c))
                    continue;
                var j = Map.Idx(c);
                if (Map.Structure[j] == e && !Footprint.Contains(t.Def, t.Cell, t.Facing, c))
                {
                    Map.Structure[j] = Entity.Null;
                    Map.MarkStructureChanged(j);
                }
            }
            if (!Footprint.IsWide(t.Def))
                continue;
            foreach (var c in FootprintCells(t.Def, t.Cell, t.Facing))
            {
                if (!Map.InBounds(c))
                    continue;
                var j = Map.Idx(c);
                if (Map.Structure[j].IsNull && !Map.IsSolid(j))
                {
                    Map.Structure[j] = e;
                    Map.MarkStructureChanged(j);
                }
            }
        }
    }

    // ---------------- Deconstruction ----------------

    /// <summary>
    /// What a deconstruct order in this cell takes apart: the building there if any, else the
    /// floor. Only finished things the player could have built. Entity.Null if nothing.
    /// </summary>
    public Entity DeconstructTarget(int3 cell)
    {
        if (!Map.InBounds(cell))
            return Entity.Null;
        var i = Map.Idx(cell);
        foreach (var e in (ReadOnlySpan<Entity>)[Map.Structure[i], Map.Floor[i]])
            if (!e.IsNull && !World.Has<Construction>(e) && World.Get<Thing>(e).Def.Building is { Deconstructable: true })
                return e;
        return Entity.Null;
    }

    /// <summary>
    /// Whether taking this apart would leave something hanging: a floor still carrying a
    /// building, or a wall/door with something resting on it or a floor that needs it.
    /// Things come down top-down.
    /// </summary>
    public bool CanDeconstruct(Entity e, out string reason)
    {
        reason = "";
        var thing = World.Get<Thing>(e);
        var i = Map.Idx(thing.Cell);
        if (IsContainer(e) && StoredIn(e).Any())
        {
            reason = $"the {thing.Label} has to be emptied first";
            return false;
        }
        if (thing.Def.IsFloor)
        {
            if (!Map.Structure[i].IsNull)
            {
                reason = $"the {World.Get<Thing>(Map.Structure[i]).Def.Label} on it has to go first";
                return false;
            }
            return true;
        }
        if (thing.Def.Building is not ({ IsWall: true } or { IsDoor: true }) || thing.Cell.Z + 1 >= Map.Depth)
            return true;
        var above = new int3(thing.Cell.X, thing.Cell.Y, thing.Cell.Z + 1);
        var ai = Map.Idx(above);
        if (!Map.Structure[ai].IsNull && Map.Floor[ai].IsNull)
        {
            reason = $"the {World.Get<Thing>(Map.Structure[ai]).Def.Label} on top has to go first";
            return false;
        }
        // Floors up there that only this wall holds up.
        foreach (var f in World.Query<Thing>())
        {
            var ft = World.Get<Thing>(f);
            if (!ft.Spawned || !ft.Def.IsFloor || ft.Cell.Z != above.Z || World.Has<Construction>(f))
                continue;
            var span = ft.Def.Building!.SupportSpan;
            if (Math.Abs(ft.Cell.X - above.X) > span || Math.Abs(ft.Cell.Y - above.Y) > span)
                continue;
            if (!FloorSupported(ft.Cell, ft.Def, finishedOnly: true, without: thing.Cell))
            {
                reason = ft.Def.IsRoof ? "it holds up the roof above" : "it holds up the floor above";
                return false;
            }
        }
        return true;
    }

    /// <summary>What taking a building apart gives back: its def's salvage, else half its cost (rounded up).</summary>
    public List<(ThingDef Def, int Count, MaterialDef? Material)> SalvageOf(Entity e)
    {
        var thing = World.Get<Thing>(e);
        var building = thing.Def.Building!;
        var list = new List<(ThingDef, int, MaterialDef?)>();
        if (building.Salvage.Count > 0)
            foreach (var c in building.Salvage)
                list.Add((c.ThingDef, c.Count, c.MaterialDef ?? MaterialFor(c.ThingDef, thing.Material)));
        else
            foreach (var c in building.Cost)
                list.Add((c.ThingDef, (c.Count + 1) / 2, MaterialFor(c.ThingDef, thing.Material)));
        return list;
    }

    /// <summary>Take a building or floor apart, getting half its materials back (or its whole salvage).</summary>
    public void Deconstruct(Entity e)
    {
        var thing = World.Get<Thing>(e);
        var building = thing.Def.Building!;
        var salvage = SalvageOf(e);
        DestroyThing(e);
        foreach (var (def, count, material) in salvage)
            SpawnItem(def, count, thing.Cell, material);
        Messages.Add(MessageKind.Info, $"Took apart the {thing.Label}.", thing.Cell);
    }

    /// <summary>Turn a finished construction site into the real building.</summary>
    public Entity CompleteConstruction(Entity site)
    {
        var thing = World.Get<Thing>(site);
        if (thing.Def.PhaseOf != null)
            return Buildings.CompletePhase(this, site); // lays the phase's parts; Null while it has to wait
        if (thing.Def.UpgradeOf != null)
            return Buildings.CompleteUpgrade(this, site); // fits it to its furnishing
        var construction = World.Get<Construction>(site);
        var def = construction.Target;
        var partOf = World.TryGet<PartOf>(site, out var part) ? part.Building : Entity.Null;
        DestroyThing(site);
        var built = SpawnStructure(def, thing.Cell, thing.Facing);
        if (!partOf.IsNull)
            World.Add(built, new PartOf { Building = partOf });
        ref var made = ref World.Get<Thing>(built);
        made.Material = construction.MainMaterial;
        made.Hp = made.MaxHp;
        if (made.Material != null)
            Map.MarkStructureChanged(Map.Idx(thing.Cell)); // its material changes how well it insulates
        Messages.Add(MessageKind.Info, $"Built {made.Label}.", thing.Cell);
        RecipeBooks.Discover(this, Entity.Null, "built:" + def.Name);
        if (!partOf.IsNull)
            Buildings.OnPartBuilt(this, partOf); // may lay the next phase
        return built;
    }

    /// <summary>Cancel a construction site, refunding delivered materials.</summary>
    public void CancelConstruction(Entity site)
    {
        var thing = World.Get<Thing>(site);
        var c = World.Get<Construction>(site);
        var cost = c.Target.Building!.Cost;
        DestroyThing(site);
        for (var i = 0; i < cost.Count; i++)
            if (c.Delivered[i] > 0)
                SpawnItem(cost[i].ThingDef, c.Delivered[i], thing.Cell, MaterialFor(cost[i].ThingDef, c.MainMaterial));
    }

    /// <summary>"fine leather cap": the label with how well it was made, for crafted gear.</summary>
    public string LabelOf(Entity e)
    {
        var label = World.Get<Thing>(e).Label;
        if (World.TryGet<Charm>(e, out var charm))
            return $"{label} of {charm.Mending.Label.ToLowerInvariant()}{(charm.Extra > 0 ? $" ({new string('+', charm.Extra)})" : "")}";
        return World.TryGet<Crafted>(e, out var c) && c.Quality != Quality.Plain ? $"{Crafting.QualityLabel(c.Quality)} {label}" : label;
    }

    /// <summary>
    /// A fueled building that's burning now: it has fuel, and if it's only fired for work (a kiln),
    /// there's work for it. Heat, light and fuel use all go by this.
    /// </summary>
    public bool Burning(Entity e)
    {
        if (!World.TryGet<Fueled>(e, out var fueled) || fueled.Fuel <= 0)
            return false;
        return World.Get<Thing>(e).Def.Building?.Fuel is not { OnDemand: true } || Crafting.WantsFiring(this, e);
    }

    /// <summary>The material refunded items of <paramref name="def"/> come in: the building's, if it's a kind of that item.</summary>
    internal static MaterialDef? MaterialFor(ThingDef def, MaterialDef? material) => material != null && material.Fits(def) ? material : null;

    // ---------------- Blocks ----------------

    /// <summary>True if the block can be designated for digging: diggable, with nothing built resting on it.</summary>
    public bool CanDig(int3 cell)
    {
        if (!Map.InBounds(cell))
            return false;
        var i = Map.Idx(cell);
        if (!Map.Block[i].Solid || Map.Block[i].DigWork <= 0)
            return false;
        if (cell.Z + 1 >= Map.Depth)
            return true;
        var above = i + Map.LayerSize;
        return Map.Structure[above].IsNull && Map.Floor[above].IsNull;
    }

    /// <summary>Replace a solid block with air, dropping its yield and anything that was resting on it.</summary>
    public void DigOut(int3 cell, Entity? digger = null)
    {
        var by = digger ?? Entity.Null;
        var i = Map.Idx(cell);
        var block = Map.Block[i];
        Map.Block[i] = Defs.Block("Air");
        Map.ClearDesignation(i, Designation.Dig);
        Map.MarkStructureChanged(i);
        Mining.RevealAround(this, i, by);

        if (cell.Z + 1 < Map.Depth && Map.Item[i + Map.LayerSize] is { IsNull: false } above)
        {
            DespawnThing(above);
            PlaceItemNear(above, cell);
        }
        foreach (var y in block.DigYield)
        {
            SpawnItem(y.ThingDef, Mining.OreYield(this, by, block, y.ThingDef, y.Count), cell, y.MaterialDef);
            // Whoever digs ore out has handled it: it teaches what it teaches (smelting copper).
            if (!by.IsNull && Defs.HandleTriggers.TryGetValue(y.ThingDef, out var trigger))
                RecipeBooks.Discover(this, by, trigger);
        }
        if (block.DigBonus.Count > 0)
            Mining.RollBonus(this, block.DigBonus, cell, by, 7013u);
    }

    // ---------------- Items ----------------

    /// <summary>
    /// Can someone take this item: lying on the map, or kept in a container. What's in a pack is
    /// its wearer's (or lies dropped inside it), so no job takes it out; and what's up for sale
    /// (<see cref="Shops.ForSale"/>) is left where its keeper put it.
    /// </summary>
    public bool IsAvailable(Entity item) =>
        World.IsAlive(item) && !World.Has<Shops.ForSale>(item)
        && (World.Get<Thing>(item).Spawned || (World.TryGet<Stored>(item, out var s) && !OnSomeone(s.In)));

    /// <summary>Items lying on the map plus items kept in containers (anything a job could take), less what's up for sale.</summary>
    public IEnumerable<Entity> AvailableItems()
    {
        foreach (var e in World.Query<Stack>())
            if ((World.Get<Thing>(e).Spawned || (World.TryGet<Stored>(e, out var s) && !OnSomeone(s.In))) && !World.Has<Shops.ForSale>(e))
                yield return e;
    }

    /// <summary>
    /// A container whose contents aren't up for grabs: one someone carries (a pack, or a creature's
    /// own tool belt), or a serving hatch (what's put up there is for the customer at it).
    /// </summary>
    private bool OnSomeone(Entity container) => IsPack(container) || World.Has<Creature>(container)
        || (World.TryGet<Thing>(container, out var t) && t.Def.Building is { Hatch: true });

    /// <summary>The stacks kept in a container, in entity order.</summary>
    public List<Entity> StoredIn(Entity container)
    {
        var items = new List<Entity>();
        foreach (var e in World.Query<Stored>())
            if (World.Get<Stored>(e).In == container)
                items.Add(e);
        return items;
    }

    /// <summary>Holds items: a storage building (the wagon) or a pack.</summary>
    public bool IsContainer(Entity e) =>
        World.TryGet<Thing>(e, out var t) && (t.Def.Building is { StorageStacks: > 0 } || t.Def.Container != null);

    /// <summary>An item that carries others by weight (<see cref="ThingDef.Container"/>).</summary>
    public bool IsPack(Entity e) => World.TryGet<Thing>(e, out var t) && t.Def.Container != null;

    /// <summary>What a creature wears on its back, if it's a pack; Entity.Null otherwise.</summary>
    public Entity PackOf(Entity creature) =>
        World.TryGet<Apparel>(creature, out var worn) && World.IsAlive(worn.Back) && IsPack(worn.Back) ? worn.Back : Entity.Null;

    /// <summary>Weight of an item in kg: every unit, plus what's inside it for a pack.</summary>
    public float MassOf(Entity item)
    {
        var t = World.Get<Thing>(item);
        var mass = t.UnitMass * (World.TryGet<Stack>(item, out var stack) ? stack.Count : 1);
        return t.Def.Container != null ? mass + LoadOf(item) : mass;
    }

    /// <summary>What a pack holds, in kg (its own weight not counted).</summary>
    public float LoadOf(Entity pack)
    {
        var load = 0f;
        foreach (var e in StoredIn(pack))
            load += MassOf(e);
        return load;
    }

    /// <summary>
    /// How many units of this kind of item fit in a container now: free slots and partial stacks
    /// for a storage building, spare weight for a pack (never a bulky item, so never another pack).
    /// </summary>
    public int ContainerRoom(Entity container, ThingDef def, MaterialDef? material)
    {
        var cdef = World.Get<Thing>(container).Def;
        if (cdef.Container is { } pack)
        {
            var unit = def.Mass * (material?.MassFactor ?? 1f);
            if (def.Bulky || unit <= 0)
                return 0;
            // A hair of slack so a load that's exactly full doesn't round down a unit.
            return Math.Max(0, (int)MathF.Floor((pack.Capacity - LoadOf(container)) / unit + 1e-4f));
        }
        var held = StoredIn(container);
        var room = Math.Max(0, (cdef.Building?.StorageStacks ?? 0) - held.Count) * def.StackLimit;
        foreach (var e in held)
        {
            var t = World.Get<Thing>(e);
            if (t.Def == def && t.Material == material)
                room += Math.Max(0, def.StackLimit - World.Get<Stack>(e).Count);
        }
        return room;
    }

    /// <summary>
    /// Put an unspawned item into a container: as many units as fit (<see cref="ContainerRoom"/>),
    /// topping up stacks of the same kind first, then as new stacks. Returns what didn't fit
    /// (Entity.Null when it all went in).
    /// </summary>
    public Entity Store(Entity container, Entity item)
    {
        var t = World.Get<Thing>(item);
        var room = ContainerRoom(container, t.Def, t.Material);
        if (room <= 0)
            return item;
        var rest = Entity.Null;
        if (room < World.Get<Stack>(item).Count)
        {
            rest = item;
            item = TakeFromStack(item, room);
        }
        foreach (var other in StoredIn(container))
        {
            var o = World.Get<Thing>(other);
            if (o.Def != t.Def || o.Material != t.Material)
                continue;
            ref var into = ref World.Get<Stack>(other);
            var moved = Math.Min(t.Def.StackLimit - into.Count, World.Get<Stack>(item).Count);
            if (moved <= 0)
                continue;
            if (World.TryGet<Rot>(item, out var adding) && World.Has<Rot>(other))
            {
                ref var rot = ref World.Get<Rot>(other);
                rot.Progress = (rot.Progress * into.Count + adding.Progress * moved) / (into.Count + moved);
            }
            into.Count += moved;
            if ((World.Get<Stack>(item).Count -= moved) == 0)
            {
                World.Destroy(item);
                return rest;
            }
        }
        // The rest goes in as new stacks, none past the stack limit (a hauler's load can be).
        var cell = World.Get<Thing>(container).Cell;
        while (true)
        {
            var part = World.Get<Stack>(item).Count > t.Def.StackLimit ? TakeFromStack(item, t.Def.StackLimit) : item;
            ref var thing = ref World.Get<Thing>(part);
            thing.Cell = cell;
            thing.Spawned = false;
            World.Add(part, new Stored { In = container });
            if (part == item)
                return rest;
        }
    }

    /// <summary>
    /// Move what fits from one container into another (trading up packs, later upgrading one by
    /// crafting); what doesn't fit stays where it was.
    /// </summary>
    public void TransferContents(Entity from, Entity to)
    {
        foreach (var item in StoredIn(from))
        {
            var t = World.Get<Thing>(item);
            var room = Math.Min(ContainerRoom(to, t.Def, t.Material), World.Get<Stack>(item).Count);
            if (room <= 0)
                continue;
            // The whole stack comes out of 'from' (TakeFromStack drops its Stored), or a part splits off.
            if (!Store(to, TakeFromStack(item, room)).IsNull)
                throw new InvalidOperationException("room was counted, so it must fit");
        }
    }

    /// <summary>Is there room in a container for (some of) this kind of item?</summary>
    public bool HasRoomFor(Entity container, ThingDef def, MaterialDef? material) =>
        ContainerRoom(container, def, material) > 0;

    public IEnumerable<Entity> SpawnedItems()
    {
        foreach (var e in World.Query<Stack>())
            if (World.Get<Thing>(e).Spawned)
                yield return e;
    }

    /// <summary>True if an item of this def and material (count units) could be dropped in the cell.</summary>
    public bool CanAcceptItem(int3 cell, ThingDef def, out int space, MaterialDef? material = null)
    {
        space = 0;
        if (!Map.InBounds(cell))
            return false;
        var i = Map.Idx(cell);
        if (Map.IsSolid(i))
            return false;
        if (!Map.HasFloor[i] && Map.GroundUnder(i) is not { Passable: true, IsWater: false })
            return false;
        // Not into a wall, nor onto a wall's blueprint (drawn as a see-through wall, so an item there
        // looks walled in; building it would push the item aside anyway).
        var s = Map.Structure[i];
        if (!s.IsNull && World.Get<Thing>(s).Def.Impassable)
            return false;
        var existing = Map.Item[i];
        if (existing.IsNull)
        {
            space = def.StackLimit;
            return true;
        }
        var t = World.Get<Thing>(existing);
        if (t.Def != def || t.Material != material)
            return false;
        space = def.StackLimit - World.Get<Stack>(existing).Count;
        return space > 0;
    }

    /// <summary>Create an item entity that isn't on the map yet.</summary>
    public Entity CreateItem(ThingDef def, int count, MaterialDef? material = null)
    {
        var e = World.Create();
        World.Add(e, new Thing { Def = def, Hp = def.MaxHp * (material?.HpFactor ?? 1f), Spawned = false, Cell = new int3(-1, -1, -1), Material = material });
        World.Add(e, new Stack { Count = count });
        if (def.RotDays > 0)
            World.Add(e, new Rot());
        return e;
    }

    /// <summary>
    /// Spawn items as close to <paramref name="cell"/> as possible, merging with stacks and splitting
    /// if needed. <paramref name="rot"/> is how far gone they already are (meat from an old carcass).
    /// </summary>
    public void SpawnItem(ThingDef def, int count, int3 cell, MaterialDef? material = null, float rot = 0)
    {
        while (count > 0)
        {
            var n = Math.Min(count, def.StackLimit);
            var e = CreateItem(def, n, material);
            if (rot > 0 && World.Has<Rot>(e))
                World.Get<Rot>(e).Progress = rot;
            PlaceItemNear(e, cell);
            count -= n;
        }
    }

    /// <summary>A split-off part of a stack is as far gone as the stack.</summary>
    private void CopyRot(Entity from, Entity to)
    {
        if (World.TryGet<Rot>(from, out var rot) && World.Has<Rot>(to))
            World.Get<Rot>(to).Progress = rot.Progress;
    }

    /// <summary>
    /// Put an unspawned item entity on the map at or near a cell. Merges into a matching stack
    /// when possible (destroying the merged entity). Returns the entity that now holds the items.
    /// </summary>
    public Entity PlaceItemNear(Entity item, int3 near)
    {
        ref var thing = ref World.Get<Thing>(item);
        var def = thing.Def;
        var material = thing.Material;
        var remaining = World.Get<Stack>(item).Count;

        foreach (var tile in SpiralFrom(near, 12))
        {
            if (!CanAcceptItem(tile, def, out var space, material))
                continue;
            var i = Map.Idx(tile);
            var existing = Map.Item[i];
            if (existing.IsNull)
            {
                if (remaining > def.StackLimit)
                {
                    // Split off what fits and keep looking for the rest.
                    var part = CreateItem(def, def.StackLimit, material);
                    CopyRot(item, part);
                    PlaceAt(part, tile);
                    remaining -= def.StackLimit;
                    World.Get<Stack>(item).Count = remaining;
                    continue;
                }
                PlaceAt(item, tile);
                return item;
            }

            var moved = Math.Min(space, remaining);
            // Fresh and old food mixed in one stack: it's as far gone as the average unit.
            if (World.TryGet<Rot>(item, out var adding) && World.Has<Rot>(existing))
            {
                ref var rot = ref World.Get<Rot>(existing);
                var had = World.Get<Stack>(existing).Count;
                rot.Progress = (rot.Progress * had + adding.Progress * moved) / (had + moved);
            }
            World.Get<Stack>(existing).Count += moved;
            remaining -= moved;
            if (remaining == 0)
            {
                World.Destroy(item);
                return existing;
            }
            World.Get<Stack>(item).Count = remaining;
        }

        // Nowhere to put it: drop it exactly where asked even if it overlaps (rare).
        Messages.Add(MessageKind.Warning, $"No room to drop {def.Label}; it was lost.", near);
        World.Destroy(item);
        return Entity.Null;
    }

    private void PlaceAt(Entity item, int3 tile)
    {
        ref var thing = ref World.Get<Thing>(item);
        thing.Cell = tile;
        thing.Spawned = true;
        Map.Item[Map.Idx(tile)] = item;
    }

    /// <summary>
    /// Take <paramref name="count"/> units from a spawned stack. Returns an unspawned entity holding
    /// the taken units (the original entity if the whole stack was taken).
    /// </summary>
    public Entity TakeFromStack(Entity stack, int count)
    {
        ref var s = ref World.Get<Stack>(stack);
        if (count >= s.Count)
        {
            DespawnThing(stack);
            if (World.Has<Stored>(stack))
                World.Remove<Stored>(stack); // taken out of its container
            return stack;
        }
        s.Count -= count;
        var t = World.Get<Thing>(stack);
        var taken = CreateItem(t.Def, count, t.Material);
        CopyRot(stack, taken);
        return taken;
    }

    /// <summary>
    /// Pour one unspawned stack into another of the same kind (a hauler adding to its load), past the
    /// stack limit if need be: <see cref="PlaceItemNear"/> splits it again when it's put down.
    /// </summary>
    public void MergeStacks(Entity into, Entity from)
    {
        var added = World.Get<Stack>(from).Count;
        if (World.TryGet<Rot>(from, out var adding) && World.Has<Rot>(into))
        {
            ref var rot = ref World.Get<Rot>(into);
            var had = World.Get<Stack>(into).Count;
            rot.Progress = (rot.Progress * had + adding.Progress * added) / (had + added);
        }
        World.Get<Stack>(into).Count += added;
        DestroyThing(from);
    }

    /// <summary>Remove a thing from its map layer without destroying it (picked up).</summary>
    public void DespawnThing(Entity e)
    {
        ref var thing = ref World.Get<Thing>(e);
        if (!thing.Spawned)
            return;
        RemoveFromLayers(e, thing.Cell);
        thing.Spawned = false;
    }

    /// <summary>
    /// Lock or unlock a lockable door (<see cref="Defs.BuildingProps.Lockable"/>). Locked it's
    /// impassable, so the map's paths and regions rebuild. Returns false for anything else.
    /// </summary>
    public bool SetLocked(Entity door, bool locked)
    {
        if (!World.IsAlive(door) || !World.TryGet<Thing>(door, out var t) || t.Def.Building is not { IsDoor: true, Lockable: true })
            return false;
        if (World.Has<Locked>(door) == locked)
            return true;
        if (locked)
            World.Add(door, new Locked());
        else
            World.Remove<Locked>(door);
        if (t.Spawned)
            Map.MarkStructureChanged(Map.Idx(t.Cell));
        return true;
    }

    public bool IsLocked(Entity door) => World.IsAlive(door) && World.Has<Locked>(door);

    public void DestroyThing(Entity e)
    {
        if (!World.IsAlive(e))
            return;
        // A station that's destroyed gives back what was brought for its craft.
        if (World.TryGet<Workbench>(e, out var bench) && !bench.Untouched)
            Crafting.Cancel(this, e);
        // A container that's destroyed (burned, taken apart) spills what it held.
        if (IsContainer(e))
        {
            var cell = LocationOf(e);
            foreach (var item in StoredIn(e))
            {
                World.Remove<Stored>(item);
                PlaceItemNear(item, cell);
            }
        }
        DespawnThing(e);
        World.Destroy(e);
    }

    private void OnEntityDestroying(Entity e)
    {
        Reservations.Forget(e);
        if (World.TryGet<Thing>(e, out var thing) && thing.Spawned)
            RemoveFromLayers(e, thing.Cell);
    }

    private void RemoveFromLayers(Entity e, int3 cell)
    {
        // The rest of a wide structure's footprint (its centre is cleared below).
        if (World.TryGet<Thing>(e, out var wide) && Footprint.IsWide(wide.Def))
            foreach (var c in FootprintCells(wide.Def, cell, wide.Facing))
            {
                if (!Map.InBounds(c) || c == cell)
                    continue;
                var j = Map.Idx(c);
                if (Map.Structure[j] == e)
                {
                    Map.Structure[j] = Entity.Null;
                    Map.MarkStructureChanged(j);
                }
            }
        var i = Map.Idx(cell);
        if (Map.Item[i] == e)
            Map.Item[i] = Entity.Null;
        if (Map.Structure[i] == e)
        {
            Map.Structure[i] = Entity.Null;
            Map.MarkStructureChanged(i);
        }
        if (Map.Floor[i] == e)
        {
            Map.Floor[i] = Entity.Null;
            Map.MarkStructureChanged(i);
            Map.HasRoof[i] = false;
            if (Map.HasFloor[i])
            {
                Map.HasFloor[i] = false;
                DropWhatTheFloorHeld(cell);
            }
        }
    }

    /// <summary>
    /// A finished floor just went (burned, removed). Whatever was standing on it falls: items land
    /// on the next standable cell below, structures break. Creatures fall in the movement system.
    /// </summary>
    private void DropWhatTheFloorHeld(int3 cell)
    {
        var i = Map.Idx(cell);
        if (Map.Standable(i))
            return; // ground or a ladder still holds it up
        var landing = cell;
        while (landing.Z > 0 && !Map.Standable(Map.Idx(landing)) && !Map.IsSolid(Map.Idx(landing) - Map.LayerSize))
            landing = new int3(landing.X, landing.Y, landing.Z - 1);
        if (Map.Item[i] is { IsNull: false } item)
        {
            DespawnThing(item);
            PlaceItemNear(item, landing);
        }
        if (Map.Structure[i] is { IsNull: false } structure)
        {
            var def = World.Get<Thing>(structure).Def;
            Messages.Add(MessageKind.Warning, $"A {def.Label} fell and broke.", landing);
            DestroyThing(structure);
        }
    }

    /// <summary>
    /// Standable cells in expanding square rings of columns around a center (center column first).
    /// Each column yields the standable cell nearest the center's level, so the search follows
    /// hills and hollows; columns with none nearby are skipped.
    /// </summary>
    public IEnumerable<int3> SpiralFrom(int3 center, int maxRadius)
    {
        if (Map.NearestStandable(center.X, center.Y, center.Z) is { } c)
            yield return c;
        for (var r = 1; r <= maxRadius; r++)
        {
            for (var dx = -r; dx <= r; dx++)
            {
                if (Map.NearestStandable(center.X + dx, center.Y - r, center.Z) is { } a) yield return a;
                if (Map.NearestStandable(center.X + dx, center.Y + r, center.Z) is { } b) yield return b;
            }
            for (var dy = -r + 1; dy <= r - 1; dy++)
            {
                if (Map.NearestStandable(center.X - r, center.Y + dy, center.Z) is { } a) yield return a;
                if (Map.NearestStandable(center.X + r, center.Y + dy, center.Z) is { } b) yield return b;
            }
        }
    }

    // ---------------- Creatures ----------------

    /// <summary>
    /// A new creature at a cell. Starting need levels are rolled from <paramref name="rand"/> (default
    /// <see cref="Rand"/>), so systems with their own stream (wildlife) don't shift the main one.
    /// </summary>
    public Entity SpawnCreature(CreatureDef def, string name, int3 cell, Faction faction = Faction.Village, float ageDays = 0, SimRandom? rand = null)
    {
        rand ??= Rand;
        var e = World.Create();
        World.Add(e, new Creature
        {
            Def = def,
            Name = name,
            Faction = faction,
            BornTick = Clock.Tick - SimClock.DaysToTicks(ageDays),
            Hp = def.MaxHp,
        });
        World.Add(e, Position.At(cell));
        World.Add(e, new Mover());
        World.Add(e, new Mind());
        World.Add(e, new Carrying { Item = Entity.Null });

        var needs = new Needs
        {
            Values = new float[Defs.AllNeeds.Count],
            Has = new bool[Defs.AllNeeds.Count],
        };
        foreach (var rate in def.Needs)
        {
            needs.Has[rate.NeedDef.Index] = true;
            needs.Values[rate.NeedDef.Index] = rand.Range(0.7f, 0.95f);
        }
        World.Add(e, needs);
        if (def.BodyDef != null)
            World.Add(e, new Health { Body = def.BodyDef, Scale = def.MaxHp / 100f });

        if (def.Race == Race.Human)
        {
            World.Add(e, SkillRules.Roll(Seed, e));
            World.Add(e, TraitRules.Roll(Defs, Seed, e));
        }
        if (MoodSystem.HasMood(def))
            World.Add(e, new Mood());
        return e;
    }

    /// <summary>Put a new weapon in a creature's hands (replacing, and dropping, any it held).</summary>
    public Entity Equip(Entity creature, ThingDef weapon)
    {
        if (weapon.Weapon == null)
            throw new ArgumentException($"{weapon.Name} isn't a weapon");
        var item = CreateItem(weapon, 1);
        EquipItem(creature, item);
        return item;
    }

    /// <summary>Wield an existing weapon item (picked up off the map, or carried).</summary>
    public void EquipItem(Entity creature, Entity item)
    {
        DropWeapon(creature);
        DespawnThing(item);
        World.Add(creature, new Equipment { Weapon = item });
        Crafting.OnPutOn(this, creature, item);
    }

    /// <summary>Dress a creature in new apparel (dropping whatever it wore in that slot).</summary>
    public Entity Wear(Entity creature, ThingDef apparel)
    {
        if (apparel.Apparel == null)
            throw new ArgumentException($"{apparel.Name} isn't apparel");
        var item = CreateItem(apparel, 1);
        WearItem(creature, item);
        return item;
    }

    /// <summary>Put on an existing apparel item, dropping whatever was worn in its slot.</summary>
    public void WearItem(Entity creature, Entity item)
    {
        var slot = World.Get<Thing>(item).Def.Apparel!.Slot;
        TakeOff(creature, slot);
        DespawnThing(item);
        if (!World.Has<Apparel>(creature))
            World.Add(creature, new Apparel());
        World.Get<Apparel>(creature)[slot] = item;
        Crafting.OnPutOn(this, creature, item);
    }

    /// <summary>The pack people start out with (players, newcomers, settlers, the first villagers).</summary>
    public const string StartingPack = "Satchel";

    /// <summary>
    /// Put a <see cref="StartingPack"/> on someone with nothing on their back, and the basic tools on
    /// an empty belt (<see cref="Toolbelt.GiveStartingTools"/>). Returns their pack. Clothes are
    /// <see cref="Clothing"/>'s, put on after a start's other spawns so they don't shift their rolls.
    /// </summary>
    public Entity GiveStartingPack(Entity creature)
    {
        Toolbelt.GiveStartingTools(this, creature);
        if (World.TryGet<Apparel>(creature, out var worn) && World.IsAlive(worn.Back))
            return PackOf(creature);
        return Defs.TryThing(StartingPack, out var def) ? Wear(creature, def) : Entity.Null;
    }

    /// <summary>Take off what's worn in a slot and drop it at the creature's feet.</summary>
    public void TakeOff(Entity creature, ApparelSlot slot)
    {
        if (!World.TryGet<Apparel>(creature, out var worn) || !World.IsAlive(worn[slot]))
            return;
        var item = worn[slot];
        World.Get<Apparel>(creature)[slot] = Entity.Null;
        PlaceItemNear(item, World.Get<Position>(creature).Cell);
    }

    /// <summary>Everything a creature wears.</summary>
    private static readonly ApparelSlot[] ApparelSlots = Enum.GetValues<ApparelSlot>();

    public IEnumerable<Entity> Worn(Entity creature)
    {
        if (!World.TryGet<Apparel>(creature, out var worn))
            yield break;
        foreach (var slot in ApparelSlots) // the renderer asks every frame: no Enum.GetValues array each time
            if (World.IsAlive(worn[slot]))
                yield return worn[slot];
    }

    /// <summary>Drop everything a creature holds and wears (on death).</summary>
    public void DropGear(Entity creature)
    {
        DropWeapon(creature);
        foreach (var slot in Enum.GetValues<ApparelSlot>())
            TakeOff(creature, slot);
        Toolbelt.DropAll(this, creature);
    }

    /// <summary>Drop whatever the creature wields at its feet.</summary>
    public void DropWeapon(Entity creature)
    {
        if (!World.TryGet<Equipment>(creature, out var eq) || !World.IsAlive(eq.Weapon))
            return;
        World.Remove<Equipment>(creature);
        PlaceItemNear(eq.Weapon, World.Get<Position>(creature).Cell);
    }

    /// <summary>A creature leaves the map for good (raiders fleeing, herds moving on), taking what it holds.</summary>
    public void Depart(Entity e)
    {
        var c = World.Get<Creature>(e);
        // Off on a quest: written down to be put back when they return (gear and all).
        var questing = Expeditions.Embark(this, e);
        AiSystem.EndJob(this, e, World.Get<Mind>(e), false);
        if (World.TryGet<Equipment>(e, out var eq) && World.IsAlive(eq.Weapon))
            World.DestroyDeferred(eq.Weapon);
        // What's in their pack and hands goes with them: left behind, it'd be stored in a pack
        // that's gone, off the map, for any job to go looking for.
        foreach (var item in Worn(e))
        {
            foreach (var inside in StoredIn(item))
                World.DestroyDeferred(inside);
            World.DestroyDeferred(item);
        }
        if (World.TryGet<Carrying>(e, out var carrying) && World.IsAlive(carrying.Item))
            World.DestroyDeferred(carrying.Item);
        foreach (var tool in Toolbelt.Of(this, e))
            World.DestroyDeferred(tool);
        if (questing)
        {
            // Said once for the whole party, when they set out (ExpeditionSystem.Offer).
        }
        else if (c.Faction == Faction.Adventurer)
            Messages.Add(MessageKind.Info, $"{c.Name} has taken the road out of town.", World.Get<Position>(e).Cell);
        else if (c.Faction != Faction.Wild)
            Messages.Add(MessageKind.Info, $"{c.Name} has fled the area.", World.Get<Position>(e).Cell);
        World.DestroyDeferred(e);
    }

    /// <summary>The nearest standable cell on the map's edge reachable from <paramref name="from"/>, for fleeing.</summary>
    public int3? NearestEdge(int3 from)
    {
        int3? best = null;
        var bestDist = int.MaxValue;
        foreach (var (x, y) in new[] { (0, from.Y), (Map.Width - 1, from.Y), (from.X, 0), (from.X, Map.Height - 1) })
            for (var slide = 0; slide < Map.Width; slide += 3)
            {
                // Slide along the edge until there's ground to stand on.
                var sx = x == from.X ? Math.Clamp(x + slide, 0, Map.Width - 1) : x;
                var sy = y == from.Y ? Math.Clamp(y + slide, 0, Map.Height - 1) : y;
                if (Map.NearestStandable(sx, sy, from.Z, Map.Depth) is not { } c || !Map.Passable(c) || !Regions.CanReach(from, c, PathEnd.OnTile))
                    continue;
                var d = Map.Distance(from, c);
                if (d < bestDist)
                {
                    best = c;
                    bestDist = d;
                }
                break;
            }
        return best;
    }

    public IEnumerable<Entity> Creatures()
    {
        foreach (var e in World.Query<Creature>())
            if (!World.Get<Creature>(e).Dead)
                yield return e;
    }

    /// <summary>Simulated temperature of an active cell; open sky and solid ground read as outdoor air.</summary>
    public float TemperatureAt(int3 cell) =>
        Map.InBounds(cell) && Map.Active[Map.Idx(cell)] ? Map.Temperature[Map.Idx(cell)] : Climate.OutdoorTemperature;

    /// <summary>
    /// Where a thing is right now: on the map, carried, worn or wielded by a creature, or inside a
    /// container (wherever that is: a pack goes with its wearer).
    /// </summary>
    public int3 LocationOf(Entity e)
    {
        if (World.TryGet<Position>(e, out var p))
            return p.Cell;
        var thing = World.Get<Thing>(e);
        if (thing.Spawned)
            return thing.Cell;
        if (World.TryGet<Stored>(e, out var stored) && World.IsAlive(stored.In))
            return LocationOf(stored.In);
        foreach (var c in World.Each<Carrying>())
            if (World.Get<Carrying>(c).Item == e)
                return World.Get<Position>(c).Cell;
        foreach (var c in World.Each<Apparel>())
        {
            var worn = World.Get<Apparel>(c);
            foreach (var slot in ApparelSlots)
                if (worn[slot] == e)
                    return World.Get<Position>(c).Cell;
        }
        foreach (var c in World.Each<Equipment>())
            if (World.Get<Equipment>(c).Weapon == e)
                return World.Get<Position>(c).Cell;
        return thing.Cell;
    }
}
