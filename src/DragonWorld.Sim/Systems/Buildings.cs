using DragonWorld.Sim.Components;
using DragonWorld.Sim.Core;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Maps;

namespace DragonWorld.Sim.Systems;

/// <summary>
/// Preset buildings (<see cref="BuildingDef"/>, docs/plans/preset-buildings.md): placing one
/// claims its plot. Its foundation, walls and roof each go up as one site (a generated def,
/// <see cref="BuildingVariant.PhaseDefs"/>) that any number of villagers (or the player, holding
/// F) deliver to and up to <see cref="MaxBuilders"/> work on together; finished, it lays all that
/// phase's parts at once. The furniture it comes with goes in as ordinary blueprints. Finished,
/// it takes extras only in its <see cref="BuildingDef.Slots"/>. Everything else is derived from
/// the map and the <see cref="PartOf"/> tags, so a load or a fire needs no bookkeeping beyond the phase.
/// </summary>
public static class Buildings
{
    /// <summary>A plot cell: where it is (off the map: -1, -1, -1) and its plan symbol and place.</summary>
    public readonly record struct PlotCell(int3 Cell, char Symbol, int Col, int Row);

    /// <summary>A part a phase lays: its def, cell and facing.</summary>
    public readonly record struct Stamp(ThingDef Def, int3 Cell, int Facing);

    private static readonly int3 Off = new(-1, -1, -1);

    /// <summary>The plot's cells on the ground level, in plan order (rows back to front).</summary>
    public static IEnumerable<PlotCell> Plot(Map map, BuildingDef def, int3 anchor, int facing)
    {
        for (var row = 0; row < def.Depth; row++)
        for (var col = 0; col < def.Width; col++)
        {
            var ch = def.At(col, row);
            if (ch == ' ')
                continue;
            yield return new PlotCell(CellOf(map, def, anchor, facing, col, row), ch, col, row);
        }
    }

    /// <summary>Where a plan cell (column, row) lands on the map, or (-1, -1, -1) off it.</summary>
    public static int3 CellOf(Map map, BuildingDef def, int3 anchor, int facing, int col, int row)
    {
        var l = def.Local(col, row);
        var d = Footprint.Rotate(l.X, l.Y, facing);
        var c = new int3(anchor.X + d.X, anchor.Y + d.Y, anchor.Z);
        return map.InBounds(c) ? c : Off;
    }

    /// <summary>The plan symbol under a map cell on the plot's level, ' ' if it's off the plot.</summary>
    public static char SymbolAt(BuildingDef def, int3 anchor, int facing, int3 cell)
    {
        if (cell.Z != anchor.Z)
            return ' ';
        var d = Footprint.Rotate(cell.X - anchor.X, cell.Y - anchor.Y, (4 - facing) & 3); // undo the turn
        return def.At(d.X - Footprint.Lo(def.Width), d.Y - Footprint.Lo(def.Depth));
    }

    /// <summary>
    /// The building whose plot holds <paramref name="cell"/>: on its ground level, or the level of
    /// its roof just above. Entity.Null if none.
    /// </summary>
    public static Entity At(Simulation sim, int3 cell)
    {
        var w = sim.World;
        foreach (var e in w.Each<PresetBuilding>())
        {
            var b = w.Get<PresetBuilding>(e);
            if (SymbolAt(b.Def, b.Anchor, b.Facing, cell) != ' ')
                return e;
            if (b.VariantDef.Resolved.RoofDef != null && cell.Z == b.Anchor.Z + 1
                && SymbolAt(b.Def, b.Anchor, b.Facing, new int3(cell.X, cell.Y, b.Anchor.Z)) != ' ')
                return e;
        }
        return Entity.Null;
    }

    /// <summary>The building a structure belongs to, or Entity.Null.</summary>
    public static Entity Of(Simulation sim, Entity thing) =>
        sim.World.TryGet<PartOf>(thing, out var p) && sim.World.IsAlive(p.Building) && sim.World.Has<PresetBuilding>(p.Building) ? p.Building : Entity.Null;

    /// <summary>"dwelling" or "thatched dwelling": the def, and the variant when there's a choice.</summary>
    public static string Label(PresetBuilding b) =>
        b.Def.Variants.Count > 1 ? $"{b.VariantDef.Label.ToLowerInvariant()} {b.Def.Label}" : b.Def.Label;

    // ---------------- Placing ----------------

    /// <summary>
    /// Why a building can't be placed here, or null if it can. Its plot needs level, firm, open
    /// ground (trees, bushes and boulders are fine: they're cleared first) and room above for the
    /// roof, clear of every other building's plot.
    /// </summary>
    public static string? PlacementProblem(Simulation sim, BuildingDef def, int3 anchor, int facing, int variant = 0)
    {
        var map = sim.Map;
        var w = sim.World;
        if (!map.InBounds(anchor) || anchor.Z == 0 || anchor.Z + 1 >= map.Depth)
            return "It doesn't fit there.";
        var roof = def.Variant(variant).Resolved.RoofDef != null;
        foreach (var p in Plot(map, def, anchor, facing))
        {
            if (p.Cell == Off)
                return "It doesn't fit on the map.";
            var i = map.Idx(p.Cell);
            if (map.IsSolid(i))
                return "Rock is in the way.";
            if (!map.Standable(i) || map.GroundUnder(i) is not { CanBuildOn: true, Passable: true })
                return "It needs level, firm ground.";
            if (!map.Floor[i].IsNull)
                return "Something is built there.";
            if (map.Structure[i] is { IsNull: false } s && !Clearable(sim, s))
                return $"The {w.Get<Thing>(s).Label} is in the way.";
            if (!At(sim, p.Cell).IsNull)
                return "Another building's plot is there.";
            if (roof)
            {
                var j = i + map.LayerSize;
                if (map.IsSolid(j) || !map.Floor[j].IsNull || !map.Structure[j].IsNull || !At(sim, map.CellOf(j)).IsNull)
                    return "There's no room for the roof.";
            }
        }
        return null;
    }

    /// <summary>Lay out a building: claim the plot, clear it, and lay the first phase. Entity.Null if it can't go there.</summary>
    public static Entity Place(Simulation sim, BuildingDef def, int3 anchor, int facing, int variant = 0)
    {
        variant = Math.Clamp(variant, 0, def.Variants.Count - 1);
        facing &= 3;
        if (PlacementProblem(sim, def, anchor, facing, variant) != null)
            return Entity.Null;
        var e = sim.World.Create();
        sim.World.Add(e, new PresetBuilding { Def = def, Variant = variant, Anchor = anchor, Facing = (byte)facing });
        foreach (var p in Plot(sim.Map, def, anchor, facing))
            if (sim.Map.Structure[sim.Map.Idx(p.Cell)] is { IsNull: false } s)
                Clear(sim, s);
        Tend(sim, e);
        return e;
    }

    /// <summary>
    /// Raise a building whole, up to (not including) <paramref name="upTo"/>: every part spawned
    /// finished, the plot cleared (demos, tests, scenarios). <see cref="BuildPhase.Done"/>: the lot.
    /// </summary>
    public static Entity SpawnFinished(Simulation sim, BuildingDef def, int3 anchor, int facing, int variant = 0, BuildPhase upTo = BuildPhase.Done)
    {
        variant = Math.Clamp(variant, 0, def.Variants.Count - 1);
        facing &= 3;
        if (PlacementProblem(sim, def, anchor, facing, variant) != null)
            return Entity.Null;
        var e = sim.World.Create();
        var b = new PresetBuilding { Def = def, Variant = variant, Anchor = anchor, Facing = (byte)facing };
        sim.World.Add(e, b);
        foreach (var p in Plot(sim.Map, def, anchor, facing))
            if (sim.Map.Structure[sim.Map.Idx(p.Cell)] is { IsNull: false } s)
                sim.DestroyThing(s);
        for (; b.Phase < upTo; b.Phase++)
            foreach (var st in Stamps(sim.Map, b, b.Phase))
                if (st.Cell != Off && sim.CanPlaceStructure(st.Cell, st.Def, st.Facing))
                    sim.World.Add(sim.SpawnStructure(st.Def, st.Cell, st.Facing), new PartOf { Building = e });
        Tend(sim, e); // lays the next phase's blueprints
        return e;
    }

    // ---------------- Phases ----------------

    /// <summary>The most villagers who work on one phase at a time (any number may bring materials).</summary>
    public const int MaxBuilders = 4;

    /// <summary>What a phase lays, in a fixed order.</summary>
    public static IEnumerable<Stamp> Stamps(Map map, PresetBuilding b, BuildPhase phase)
    {
        var parts = b.VariantDef.Resolved;
        switch (phase)
        {
            case BuildPhase.Foundation when parts.FoundationDef is { } f:
                foreach (var p in Plot(map, b.Def, b.Anchor, b.Facing))
                    yield return new Stamp(f, p.Cell, b.Facing);
                break;
            case BuildPhase.Walls:
                foreach (var p in Plot(map, b.Def, b.Anchor, b.Facing))
                    if (parts.ForSymbol(p.Symbol) is { } d)
                        yield return new Stamp(d, p.Cell, b.Facing);
                break;
            case BuildPhase.Roof when parts.RoofDef is { } r:
                foreach (var p in Plot(map, b.Def, b.Anchor, b.Facing))
                    yield return new Stamp(r, p.Cell == Off ? Off : new int3(p.Cell.X, p.Cell.Y, p.Cell.Z + 1), b.Facing);
                break;
            case BuildPhase.Furnish:
                foreach (var f in b.Def.Furnish)
                    yield return new Stamp(f.ThingDef, CellOf(map, b.Def, b.Anchor, b.Facing, f.At[0], f.At[1]), (f.Facing + b.Facing) & 3);
                break;
        }
    }

    /// <summary>What holds a stamp's cell in its layer (floor or structure).</summary>
    private static Entity Occupant(Simulation sim, Stamp s) =>
        (s.Def.IsFloor ? sim.Map.Floor : sim.Map.Structure)[sim.Map.Idx(s.Cell)];

    /// <summary>The stamp's cell holds this building's part (finished, unless <paramref name="orPlanned"/>).</summary>
    private static bool Laid(Simulation sim, Entity building, Stamp s, bool orPlanned = false) =>
        Occupant(sim, s) is { IsNull: false } o && Of(sim, o) == building && (orPlanned || !sim.World.Has<Construction>(o));

    /// <summary>
    /// Keep a building going. A raised phase (foundation, walls, roof) is one site, built as a
    /// whole (<see cref="PhaseSite"/>); furniture goes in piece by piece, as blueprints. Clears
    /// what's in the way of the phase (a sapling where a wall goes), lays what's missing, and once
    /// it's all built moves on. A building being demolished is marked down instead.
    /// </summary>
    public static void Tend(Simulation sim, Entity e)
    {
        var w = sim.World;
        if (!w.IsAlive(e) || !w.TryGet<PresetBuilding>(e, out var b))
            return;
        if (b.Demolishing)
        {
            TendDemolition(sim, e);
            return;
        }
        while (b.Phase < BuildPhase.Done)
        {
            var stamps = Stamps(sim.Map, b, b.Phase).Where(s => s.Cell != Off).ToList();
            if (b.Phase == BuildPhase.Furnish)
            {
                if (!LayEach(sim, e, stamps))
                    return;
            }
            else if (!stamps.All(s => Laid(sim, e, s)))
            {
                foreach (var s in stamps)
                    if (!Laid(sim, e, s))
                        ClearFor(sim, e, s);
                if (PhaseSite(sim, e).IsNull && b.VariantDef.PhaseDef(b.Phase) is { } def)
                {
                    var site = w.Create();
                    w.Add(site, new Thing { Def = def, Cell = b.Anchor, Hp = def.MaxHp, Facing = b.Facing }); // off the map's layers: it's the whole plot
                    w.Add(site, new Construction { Target = def, Delivered = new int[def.Building!.Cost.Count] });
                    w.Add(site, new PartOf { Building = e });
                }
                return;
            }
            Advance(sim, b);
        }
    }

    /// <summary>Lay each of the stamps as its own blueprint; true once they're all built.</summary>
    private static bool LayEach(Simulation sim, Entity e, List<Stamp> stamps)
    {
        var done = true;
        foreach (var s in stamps)
        {
            if (Laid(sim, e, s, orPlanned: true))
            {
                done &= Laid(sim, e, s);
                continue;
            }
            done = false;
            ClearFor(sim, e, s);
            var site = sim.PlaceBlueprint(s.Def, s.Cell, s.Facing);
            if (!site.IsNull)
                sim.World.Add(site, new PartOf { Building = e });
        }
        return done;
    }

    private static void Advance(Simulation sim, PresetBuilding b)
    {
        var finished = b.Phase;
        b.Phase = finished + 1;
        var label = Label(b);
        if (b.Phase == BuildPhase.Done)
            sim.Messages.Add(MessageKind.Good, $"The {label} is finished.", b.Anchor);
        else if (Stamps(sim.Map, b, finished).Any())
            sim.Messages.Add(MessageKind.Info, finished switch
            {
                BuildPhase.Foundation => $"The {label}'s foundation is laid.",
                BuildPhase.Walls => $"The {label}'s walls are up.",
                BuildPhase.Roof => $"The {label}'s roof is on.",
                _ => $"The {label} is furnished.",
            }, b.Anchor);
    }

    /// <summary>Clear what's in a stamp's way (in every cell of a wide piece of furniture).</summary>
    private static void ClearFor(Simulation sim, Entity e, Stamp s)
    {
        if (s.Def.IsFloor)
            return;
        foreach (var c in sim.FootprintCells(s.Def, s.Cell, s.Facing))
            if (sim.Map.InBounds(c) && sim.Map.Structure[sim.Map.Idx(c)] is { IsNull: false } other && Of(sim, other) != e)
                Clear(sim, other);
    }

    /// <summary>A plant or boulder on the plot: something clearing it would make room for.</summary>
    private static bool Clearable(Simulation sim, Entity s) =>
        !sim.World.Has<Construction>(s) && sim.World.Get<Thing>(s).Def.Category is ThingCategory.Plant or ThingCategory.Resource;

    /// <summary>Trees get felled and boulders mined (orders, for whoever does that work); undergrowth is trampled flat.</summary>
    private static void Clear(Simulation sim, Entity s)
    {
        if (!Clearable(sim, s))
            return;
        var t = sim.World.Get<Thing>(s);
        var i = sim.Map.Idx(t.Cell);
        switch (t.Def.Harvest?.WorkType)
        {
            case WorkType.Chop:
                sim.Map.Designate(i, Designation.Chop);
                break;
            case WorkType.Mine:
                sim.Map.Designate(i, Designation.Mine);
                break;
            default:
                sim.DestroyThing(s);
                break;
        }
    }

    /// <summary>The parts of a building that still stand (finished or not, its phase site among them), in store order.</summary>
    public static List<Entity> Parts(Simulation sim, Entity building)
    {
        var list = new List<Entity>();
        foreach (var e in sim.World.Each<PartOf>())
            if (sim.World.Get<PartOf>(e).Building == building)
                list.Add(e);
        return list;
    }

    /// <summary>The site the building's current phase is being raised as, or Entity.Null.</summary>
    public static Entity PhaseSite(Simulation sim, Entity building)
    {
        var w = sim.World;
        foreach (var e in w.Each<PartOf>())
            if (w.Get<PartOf>(e).Building == building && w.Has<Construction>(e) && w.Get<Thing>(e).Def.PhaseOf != null)
                return e;
        return Entity.Null;
    }

    /// <summary>Whether <paramref name="site"/> is a phase raised as a whole (not one part on its own).</summary>
    public static bool IsPhaseSite(Simulation sim, Entity site) => sim.World.TryGet<Thing>(site, out var t) && t.Def.PhaseOf != null;

    /// <summary>
    /// A phase site can be worked on: every part it'll lay has room to go (the tree in the way is
    /// down, the walls under the roof are up). <see cref="Simulation.CanBuildNow"/> for phases.
    /// </summary>
    public static bool PhaseReady(Simulation sim, Entity site)
    {
        var building = Of(sim, site);
        if (building.IsNull)
            return false;
        var b = sim.World.Get<PresetBuilding>(building);
        return Blocked(sim, building, b, sim.World.Get<Thing>(site).Def.Phase) == 0;
    }

    /// <summary>How many of a phase's parts have no room to go yet.</summary>
    private static int Blocked(Simulation sim, Entity building, PresetBuilding b, BuildPhase phase)
    {
        var n = 0;
        foreach (var s in Stamps(sim.Map, b, phase))
            if (s.Cell != Off && !Laid(sim, building, s, orPlanned: true) && !(Occupant(sim, s).IsNull && sim.CanPlaceStructure(s.Cell, s.Def, s.Facing)))
                n++;
        return n;
    }

    /// <summary>The share of a site's materials delivered so far, 0..1.</summary>
    public static float Delivered(Construction c)
    {
        var cost = c.Target.Building!.Cost;
        int got = 0, need = 0;
        for (var i = 0; i < cost.Count; i++)
        {
            got += Math.Min(c.Delivered[i], cost[i].Count);
            need += cost[i].Count;
        }
        return need == 0 ? 1 : got / (float)need;
    }

    /// <summary>
    /// How far a phase can be built with what's been brought: work keeps pace with the materials,
    /// so builders start once the first loads are in, while others are still carrying.
    /// </summary>
    public static float WorkAllowed(Construction c) =>
        c.MaterialsComplete ? c.Target.Building!.Work : c.Target.Building!.Work * Delivered(c);

    /// <summary>There's building to do on a phase site now (a little at least, so nobody comes for a moment's work).</summary>
    public static bool HasWork(Construction c) =>
        c.MaterialsComplete || c.WorkDone < WorkAllowed(c) - c.Target.Building!.Work * 0.03f;

    /// <summary>Villagers other than <paramref name="except"/> working on a site now.</summary>
    public static int Builders(Simulation sim, Entity site, Entity except)
    {
        var n = 0;
        foreach (var e in sim.World.Each<Mind>())
            if (e != except && sim.World.Get<Mind>(e).Job is Ai.BuildJob job && job.Site == site)
                n++;
        return n;
    }

    /// <summary>Cells where the phase puts up something solid: nobody may be standing in them as it goes up.</summary>
    public static List<int3> SolidCells(Simulation sim, Entity site)
    {
        var building = Of(sim, site);
        if (building.IsNull)
            return [];
        var b = sim.World.Get<PresetBuilding>(building);
        return Stamps(sim.Map, b, sim.World.Get<Thing>(site).Def.Phase)
            .Where(s => s.Cell != Off && s.Def.BlocksMovement && !Laid(sim, building, s)).Select(s => s.Cell).ToList();
    }

    /// <summary>
    /// A phase site's work is done: every part it lays goes up at once, made of the material most
    /// brought. Waits (returns Entity.Null, the site kept) while something's in the way or someone
    /// stands where a wall goes. Returns one of the parts laid.
    /// </summary>
    internal static Entity CompletePhase(Simulation sim, Entity site)
    {
        var w = sim.World;
        var building = Of(sim, site);
        if (building.IsNull || !PhaseReady(sim, site))
            return Entity.Null;
        var solid = SolidCells(sim, site);
        foreach (var e in sim.Creatures())
            if (solid.Contains(w.Get<Position>(e).Cell))
                return Entity.Null;
        var b = w.Get<PresetBuilding>(building);
        var material = w.Get<Construction>(site).MainMaterial;
        var phase = w.Get<Thing>(site).Def.Phase;
        sim.DestroyThing(site);
        var first = Entity.Null;
        foreach (var s in Stamps(sim.Map, b, phase))
        {
            if (s.Cell == Off || Laid(sim, building, s, orPlanned: true))
                continue;
            var part = sim.SpawnStructure(s.Def, s.Cell, s.Facing);
            ref var t = ref w.Get<Thing>(part);
            t.Material = material != null && s.Def.Building!.Cost.Any(c => material.Fits(c.ThingDef)) ? material : null;
            t.Hp = t.MaxHp;
            if (t.Material != null)
                sim.Map.MarkStructureChanged(sim.Map.Idx(s.Cell)); // its material changes how well it insulates
            w.Add(part, new PartOf { Building = building });
            if (first.IsNull)
                first = part;
        }
        Tend(sim, building);
        return first;
    }

    /// <summary>How a phase stands: parts built of all it lays, the share of materials brought and of work done, parts with no room yet.</summary>
    public readonly record struct PhaseProgress(int Built, int Total, float Materials, float Work, int Blocked);

    public static PhaseProgress Progress(Simulation sim, Entity e, BuildPhase phase)
    {
        var b = sim.World.Get<PresetBuilding>(e);
        var stamps = Stamps(sim.Map, b, phase).Where(s => s.Cell != Off).ToList();
        var built = stamps.Count(s => Laid(sim, e, s));
        if (phase < b.Phase)
            return new(stamps.Count, stamps.Count, 1, 1, 0); // done (what's burned since doesn't hold it back)
        if (phase > b.Phase)
            return new(0, stamps.Count, 0, 0, 0);
        var site = PhaseSite(sim, e);
        if (!site.IsNull)
        {
            var c = sim.World.Get<Construction>(site);
            return new(built, stamps.Count, Delivered(c), Math.Clamp(c.WorkDone / c.Target.Building!.Work, 0, 1), Blocked(sim, e, b, phase));
        }
        return new(built, stamps.Count, 0, 0, Blocked(sim, e, b, phase));
    }

    // ---------------- Slots ----------------

    /// <summary>One of a building's slots, where it lands on the map.</summary>
    public readonly record struct SlotSpot(int Index, BuildingSlot Slot, int3 Cell, int Facing);

    public static IEnumerable<SlotSpot> Slots(Simulation sim, PresetBuilding b)
    {
        for (var i = 0; i < b.Def.Slots.Count; i++)
        {
            var slot = b.Def.Slots[i];
            yield return new SlotSpot(i, slot, CellOf(sim.Map, b.Def, b.Anchor, b.Facing, slot.At[0], slot.At[1]), (slot.Facing + b.Facing) & 3);
        }
    }

    /// <summary>What fills a slot (finished or planned), or Entity.Null.</summary>
    public static Entity InSlot(Simulation sim, Entity building, SlotSpot spot)
    {
        if (spot.Cell == Off)
            return Entity.Null;
        var s = sim.Map.Structure[sim.Map.Idx(spot.Cell)];
        return !s.IsNull && Of(sim, s) == building && sim.World.Get<Thing>(s) is var t && t.Cell == spot.Cell && spot.Slot.Accepts(t.Def) ? s : Entity.Null;
    }

    /// <summary>Why <paramref name="def"/> can't go in slot <paramref name="index"/> of a building now, or null.</summary>
    public static string? SlotProblem(Simulation sim, Entity building, int index, ThingDef def)
    {
        if (!sim.World.IsAlive(building) || !sim.World.TryGet<PresetBuilding>(building, out var b))
            return "There's no building there.";
        var label = Label(b);
        if (b.Demolishing)
            return $"The {label} is being taken down.";
        if (b.Phase != BuildPhase.Done)
            return $"The {label} isn't finished yet.";
        if (index < 0 || index >= b.Def.Slots.Count)
            return "There's no such spot.";
        var spot = Slots(sim, b).ElementAt(index);
        if (!spot.Slot.Accepts(def))
            return $"That spot takes a {spot.Slot.Label}.";
        if (!InSlot(sim, building, spot).IsNull)
            return "That spot is taken.";
        if (spot.Cell == Off || !sim.CanPlaceStructure(spot.Cell, def, spot.Facing))
            return "Something is in the way.";
        return null;
    }

    /// <summary>Put a blueprint of <paramref name="def"/> in a slot; Entity.Null if it can't go.</summary>
    public static Entity FillSlot(Simulation sim, Entity building, int index, ThingDef def)
    {
        if (SlotProblem(sim, building, index, def) != null)
            return Entity.Null;
        var spot = Slots(sim, sim.World.Get<PresetBuilding>(building)).ElementAt(index);
        var site = sim.PlaceBlueprint(def, spot.Cell, spot.Facing);
        if (!site.IsNull)
            sim.World.Add(site, new PartOf { Building = building });
        return site;
    }

    /// <summary>The free slot for <paramref name="def"/> nearest <paramref name="near"/> in a building, or null.</summary>
    public static SlotSpot? FreeSlotFor(Simulation sim, Entity building, ThingDef def, int3 near)
    {
        if (!sim.World.TryGet<PresetBuilding>(building, out var b))
            return null;
        SlotSpot? best = null;
        var bestDist = int.MaxValue;
        foreach (var spot in Slots(sim, b))
        {
            if (SlotProblem(sim, building, spot.Index, def) != null)
                continue;
            var d = Math.Abs(spot.Cell.X - near.X) + Math.Abs(spot.Cell.Y - near.Y);
            if (d < bestDist)
                (best, bestDist) = (spot, d);
        }
        return best;
    }

    // ---------------- Upgrades ----------------

    /// <summary>The finished furnishing an upgrade goes on (the first of its kind the building has), or Entity.Null.</summary>
    public static Entity UpgradeTarget(Simulation sim, Entity building, BuildingUpgrade upgrade)
    {
        foreach (var p in Parts(sim, building))
            if (sim.World.Get<Thing>(p) is { Spawned: true } t && t.Def.IsA(upgrade.OnDef) && !sim.World.Has<Construction>(p))
                return p;
        return Entity.Null;
    }

    /// <summary>The site an upgrade is being fitted as, or Entity.Null.</summary>
    public static Entity UpgradeSite(Simulation sim, Entity building, BuildingUpgrade upgrade)
    {
        foreach (var p in Parts(sim, building))
            if (sim.World.Get<Thing>(p).Def.UpgradeOf == upgrade && sim.World.Has<Construction>(p))
                return p;
        return Entity.Null;
    }

    /// <summary>Whether a station is a preset building's furnishing fitted with the named upgrade.</summary>
    public static bool HasUpgrade(Simulation sim, Entity station, string upgrade)
    {
        var building = Of(sim, station);
        if (building.IsNull)
            return false;
        var b = sim.World.Get<PresetBuilding>(building);
        return b.Upgrades.Contains(upgrade) && b.Def.Upgrades.Exists(u => u.Name == upgrade && sim.World.Get<Thing>(station).Def.IsA(u.OnDef));
    }

    /// <summary>Why upgrade <paramref name="index"/> can't be started on a building now, or null.</summary>
    public static string? UpgradeProblem(Simulation sim, Entity building, int index)
    {
        if (!sim.World.IsAlive(building) || !sim.World.TryGet<PresetBuilding>(building, out var b))
            return "There's no building there.";
        if (index < 0 || index >= b.Def.Upgrades.Count)
            return "There's no such upgrade.";
        var u = b.Def.Upgrades[index];
        var label = Label(b);
        if (b.Demolishing)
            return $"The {label} is being taken down.";
        if (b.Phase != BuildPhase.Done)
            return $"The {label} isn't finished yet.";
        if (b.Upgrades.Contains(u.Name))
            return $"The {label} already has its {u.Label}.";
        if (!UpgradeSite(sim, building, u).IsNull)
            return $"The {u.Label} is already being fitted.";
        if (UpgradeTarget(sim, building, u).IsNull)
            return $"The {label} has no {u.OnDef.Label} to fit it to.";
        return null;
    }

    /// <summary>Order an upgrade fitted: a site at its furnishing, built like any other. Entity.Null if it can't be.</summary>
    public static Entity StartUpgrade(Simulation sim, Entity building, int index)
    {
        if (UpgradeProblem(sim, building, index) != null)
            return Entity.Null;
        var u = sim.World.Get<PresetBuilding>(building).Def.Upgrades[index];
        var on = sim.World.Get<Thing>(UpgradeTarget(sim, building, u));
        var w = sim.World;
        var site = w.Create();
        w.Add(site, new Thing { Def = u.SiteDef, Cell = on.Cell, Hp = u.SiteDef.MaxHp, Facing = on.Facing }); // off the layers: the furnishing holds the cell
        w.Add(site, new Construction { Target = u.SiteDef, Delivered = new int[u.SiteDef.Building!.Cost.Count] });
        w.Add(site, new PartOf { Building = building });
        return site;
    }

    /// <summary>An upgrade's site is built: it's fitted (recorded on the building, drawn on its furnishing).</summary>
    internal static Entity CompleteUpgrade(Simulation sim, Entity site)
    {
        var building = Of(sim, site);
        var u = sim.World.Get<Thing>(site).Def.UpgradeOf!;
        var cell = sim.World.Get<Thing>(site).Cell;
        sim.DestroyThing(site);
        if (building.IsNull)
            return Entity.Null;
        var b = sim.World.Get<PresetBuilding>(building);
        if (!b.Upgrades.Contains(u.Name))
            b.Upgrades.Add(u.Name);
        sim.Map.MarkCellChanged(sim.Map.Idx(cell)); // drawn on the furnishing now
        sim.Messages.Add(MessageKind.Good, $"The {Label(b)}'s {u.OnDef.Label} has its {u.Label} now.", cell);
        return UpgradeTarget(sim, building, u);
    }

    /// <summary>
    /// The site in a cell for F to work on: a phase going up over the plot there, or an upgrade
    /// being fitted to the furnishing in it. Entity.Null if neither.
    /// </summary>
    public static Entity SiteAt(Simulation sim, int3 cell)
    {
        var building = At(sim, cell);
        if (building.IsNull)
            return Entity.Null;
        var phase = PhaseSite(sim, building);
        if (!phase.IsNull)
            return phase;
        foreach (var p in Parts(sim, building))
            if (sim.World.Get<Thing>(p) is { Def.UpgradeOf: not null } t && t.Cell == cell && sim.World.Has<Construction>(p))
                return p;
        return Entity.Null;
    }

    // ---------------- Extras ----------------

    /// <summary>
    /// Why the player can't put <paramref name="def"/> here (null if they can, as far as preset
    /// buildings go; <see cref="Simulation.CanPlaceStructure"/> still has its say). Inside a
    /// building it must go in one of its free slots for it, exactly; with preset buildings on, only
    /// <see cref="BuildingProps.Outdoor"/> things go up outside one. <paramref name="building"/> is
    /// the building it'd belong to.
    /// </summary>
    public static string? PlayerProblem(Simulation sim, ThingDef def, int3 cell, int facing, out Entity building)
    {
        building = Entity.Null;
        var props = def.Building;
        if (props == null)
            return null;
        if (props.PartOnly)
            return "It's only built as part of a building.";
        List<int3> cells = def.IsFloor || !Footprint.IsWide(def) ? [cell] : sim.FootprintCells(def, cell, facing).ToList();
        var inside = Entity.Null;
        var outside = false;
        foreach (var c in cells)
        {
            var at = sim.Map.InBounds(c) ? At(sim, c) : Entity.Null;
            if (at.IsNull)
                outside = true;
            else if (inside.IsNull)
                inside = at;
            else if (inside != at)
                return "It would straddle two buildings.";
        }
        if (!inside.IsNull)
        {
            var b = sim.World.Get<PresetBuilding>(inside);
            var label = Label(b);
            if (outside)
                return $"It would stick out of the {label}.";
            if (cell.Z != b.Anchor.Z)
                return $"That's the {label}'s roof.";
            if (!b.Def.Accepts(def))
                return $"A {def.Label} doesn't belong in a {b.Def.Label}.";
            foreach (var spot in Slots(sim, b))
                if (spot.Cell == cell && spot.Facing == (facing & 3) && spot.Slot.Accepts(def))
                {
                    building = inside;
                    return SlotProblem(sim, inside, spot.Index, def);
                }
            return $"A {def.Label} goes in one of the {label}'s spots for it.";
        }
        if (!sim.Presets.PresetMode || props.Outdoor)
            return null;
        // Where it belongs: buildings with a spot for it, or that come with one.
        var homes = sim.Defs.AllBuildings.Where(d => d.Accepts(def) || d.Furnish.Exists(f => def.IsA(f.ThingDef))).Select(d => d.Label).ToList();
        return homes.Count > 0 ? $"It only goes in a {string.Join(" or ", homes)} (the Buildings bar)." : "Raise a building from the Buildings bar instead.";
    }

    /// <summary>How many of <paramref name="def"/> (or its shapes) a building holds, finished or planned.</summary>
    public static int Count(Simulation sim, Entity building, ThingDef def)
    {
        var n = 0;
        foreach (var e in sim.World.Each<PartOf>())
            if (sim.World.Get<PartOf>(e).Building == building && sim.World.Get<Thing>(e).Def.IsA(def))
                n++;
        return n;
    }

    // ---------------- Taking down ----------------

    /// <summary>
    /// Take a building down: what isn't built yet is cancelled (materials back), the rest marked
    /// for deconstruction, which goes top-down (roof, walls, furniture, foundation).
    /// </summary>
    public static void Demolish(Simulation sim, Entity e)
    {
        if (!sim.World.IsAlive(e) || !sim.World.TryGet<PresetBuilding>(e, out var b))
            return;
        b.Demolishing = true;
        TendDemolition(sim, e);
    }

    private static void TendDemolition(Simulation sim, Entity e)
    {
        var w = sim.World;
        var parts = Parts(sim, e);
        if (parts.Count == 0)
        {
            w.Destroy(e);
            return;
        }
        foreach (var p in parts)
        {
            if (w.Has<Construction>(p))
            {
                sim.CancelConstruction(p);
                continue;
            }
            var t = w.Get<Thing>(p);
            if (!t.Spawned)
                continue;
            var i = sim.Map.Idx(t.Cell);
            if ((sim.Map.Designations[i] & Designation.Deconstruct) == 0)
                sim.Map.Designate(i, Designation.Deconstruct);
        }
        if (Parts(sim, e).Count == 0)
            w.Destroy(e); // nothing was built yet: all of it is cancelled
    }

    /// <summary>After a part is built: lay the next phase if that finished this one.</summary>
    internal static void OnPartBuilt(Simulation sim, Entity building) => Tend(sim, building);
}

/// <summary>
/// Keeps preset buildings going (<see cref="Buildings.Tend"/>) a few times an hour, for what no
/// part's completion triggers: a cleared tree making room, a burned wall to lay again, a
/// demolition's next layer. Also holds whether preset buildings are on (<see cref="PresetMode"/>).
/// </summary>
public sealed class BuildingSystem : ISimSystem, Saving.ISaveState
{
    public string Name => "Buildings";
    public int Interval => SimClock.TicksPerHour / 6;

    /// <summary>
    /// Preset buildings replace freeform walls, floors and roofs: outside a building only
    /// <see cref="BuildingProps.Outdoor"/> things may be built. Off by default (tests, older
    /// saves); the game turns it on for new villages.
    /// </summary>
    public bool PresetMode { get; set; }

    public void Update(Simulation sim)
    {
        foreach (var e in sim.World.Query<PresetBuilding>())
        {
            var b = sim.World.Get<PresetBuilding>(e);
            if (b.Phase < BuildPhase.Done || b.Demolishing)
                Buildings.Tend(sim, e);
            else if (Buildings.Parts(sim, e).Count == 0)
                sim.World.Destroy(e); // burned to the ground
        }
    }

    public void Save(Saving.SaveWriter w, Simulation sim) => w.Bool(PresetMode);

    public void Load(Saving.SaveReader r, Simulation sim) => PresetMode = r.Bool();
}
