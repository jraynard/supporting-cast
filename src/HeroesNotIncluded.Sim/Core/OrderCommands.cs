using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Core;

// The overseer's orders as commands: what the toolbar and the inspector do. Before these existed
// the game changed sim state straight from the UI, which a lockstep peer can't replay.

/// <summary>An area tool dragged over a rectangle.</summary>
public enum AreaTool : byte
{
    Chop,
    Mine,
    Dig,
    Deconstruct,
    Harvest,
    Burn,
    Build,
    Stockpile,
    ClearZone,
    Hunt,
    Fish,
    Cancel,
}

/// <summary>
/// Apply an area tool to the columns <paramref name="Min"/>..<paramref name="Max"/>.
/// <paramref name="Z"/> is the level the drag started on (Build, Deconstruct, Dig);
/// the other tools act on each column's visible cell below <paramref name="CutZ"/>, as on screen.
/// </summary>
public sealed record AreaOrderCommand(AreaTool Tool, int2 Min, int2 Max, int Z, int CutZ, ThingDef? Def = null, int Facing = 0) : ISimCommand
{
    public void Apply(Simulation sim)
    {
        var map = sim.Map;
        var min = new int2(Math.Clamp(Math.Min(Min.X, Max.X), 0, map.Width - 1), Math.Clamp(Math.Min(Min.Y, Max.Y), 0, map.Height - 1));
        var max = new int2(Math.Clamp(Math.Max(Min.X, Max.X), 0, map.Width - 1), Math.Clamp(Math.Max(Min.Y, Max.Y), 0, map.Height - 1));
        // Clamped to the map, so a bad packet can't make every peer loop for long.
        if (Z < 0 || Z >= map.Depth)
            return;
        var w = sim.World;

        if (Tool is AreaTool.Hunt or AreaTool.Cancel)
        {
            // Animals anywhere in the columns: they stand on whatever level the ground is.
            foreach (var e in w.Query<Creature>())
            {
                var at = w.Get<Position>(e).Cell;
                if (at.X < min.X || at.X > max.X || at.Y < min.Y || at.Y > max.Y || w.Get<Creature>(e) is not { Def.Race: Race.Animal, Dead: false })
                    continue;
                if (Tool == AreaTool.Hunt && !w.Has<HuntOrder>(e))
                    w.Add(e, new HuntOrder());
                else if (Tool == AreaTool.Cancel)
                    w.Remove<HuntOrder>(e);
            }
            if (Tool == AreaTool.Hunt)
                return;
        }

        if (Tool == AreaTool.Build)
        {
            if (Def?.Building == null)
                return;
            // On the level the drag started on, so upper storeys can be laid out over open air.
            var isWall = Def.Building.IsWall;
            var placed = 0;
            string? why = null;
            for (var y = min.Y; y <= max.Y; y++)
            for (var x = min.X; x <= max.X; x++)
            {
                var edge = x == min.X || x == max.X || y == min.Y || y == max.Y;
                // Walls dragged as a box only go around the outside; other buildings fill the area.
                if (isWall && !edge)
                    continue;
                // Preset buildings have their say first: only what fits goes in one.
                var cell = new int3(x, y, Z);
                if (Buildings.PlayerProblem(sim, Def, cell, Facing & 3, out var building) is { } problem)
                {
                    why ??= problem;
                    continue;
                }
                var site = sim.PlaceBlueprint(Def, cell, Facing & 3);
                if (site.IsNull)
                    continue;
                placed++;
                if (!building.IsNull)
                    w.Add(site, new PartOf { Building = building }); // an extra: it goes with the building
            }
            if (placed == 0)
                sim.Messages.Add(MessageKind.Info, why ?? "Can't build there.", new int3(min.X, min.Y, Z));
            return;
        }

        if (Tool is AreaTool.Deconstruct or AreaTool.Dig)
        {
            // Rock harder than any pick in the village can break stays marked, but say so once.
            var hardest = 0;
            var tooHard = 0;
            var villageTier = -1;
            for (var y = min.Y; y <= max.Y; y++)
            for (var x = min.X; x <= max.X; x++)
            {
                var cell = new int3(x, y, Z);
                if (Tool == AreaTool.Deconstruct && !sim.DeconstructTarget(cell).IsNull)
                    map.Designate(map.Idx(cell), Designation.Deconstruct);
                else if (Tool == AreaTool.Dig && sim.CanDig(cell))
                {
                    map.Designate(map.Idx(cell), Designation.Dig);
                    var tier = Mining.ShownTier(sim, cell);
                    if (tier <= 0)
                        continue;
                    if (villageTier < 0)
                        villageTier = Mining.VillageTier(sim, "Pick");
                    if (tier > villageTier)
                    {
                        tooHard++;
                        hardest = Math.Max(hardest, tier);
                    }
                }
            }
            if (tooHard > 0)
                sim.Messages.Add(MessageKind.Info,
                    $"{tooHard} {(tooHard == 1 ? "cell needs" : "cells need")} {sim.Defs.ToolOfTier(hardest, "Pick")} to dig.", new int3(min.X, min.Y, Z));
            return;
        }

        for (var y = min.Y; y <= max.Y; y++)
        for (var x = min.X; x <= max.X; x++)
        {
            var cell = map.VisibleCell(x, y, CutZ);
            var i = map.Idx(cell);
            var s = map.Structure[i];
            var harvest = s.IsNull ? null : w.Get<Thing>(s).Def.Harvest;
            switch (Tool)
            {
                case AreaTool.Chop when harvest?.WorkType == WorkType.Chop:
                    map.Designate(i, Designation.Chop);
                    break;
                case AreaTool.Mine when harvest?.WorkType == WorkType.Mine:
                    map.Designate(i, Designation.Mine);
                    break;
                case AreaTool.Harvest when harvest?.WorkType == WorkType.Harvest:
                    map.Designate(i, Designation.Harvest);
                    break;
                case AreaTool.Fish:
                    Fishing.Designate(sim, [cell]);
                    break;
                case AreaTool.Stockpile when !map.IsSolid(i) && map.GroundUnder(i) is { Passable: true, IsWater: false }:
                    map.SetZone(i, 1);
                    break;
                case AreaTool.ClearZone:
                    map.SetZone(i, 0);
                    break;
                case AreaTool.Cancel:
                    map.SetDesignations(i, Designation.None);
                    if (!s.IsNull && w.Has<Construction>(s))
                        sim.CancelConstruction(s);
                    break;
            }
        }
    }
}

/// <summary>Mark (or unmark) one structure to be taken apart.</summary>
public sealed record DeconstructOrderCommand(int3 Cell, bool Marked) : ISimCommand
{
    public void Apply(Simulation sim)
    {
        if (!sim.Map.InBounds(Cell))
            return;
        var i = sim.Map.Idx(Cell);
        if (!Marked)
            sim.Map.ClearDesignation(i, Designation.Deconstruct);
        else if (!sim.DeconstructTarget(Cell).IsNull)
            sim.Map.Designate(i, Designation.Deconstruct);
    }
}

/// <summary>
/// Right-click: send a drafted creature, a ridden pair or a mounted dragon to a cell. <see cref="Fly"/>
/// (Ctrl+right-click, or the rider's Fly toggle): a flyer takes off for it, however short the way.
/// </summary>
public sealed record MoveOrderCommand(Entity Pawn, int3 Cell, bool Fly = false) : ISimCommand, ISubjectCommand
{
    public Entity Subject => Pawn;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Pawn) || !w.TryGet<Creature>(Pawn, out var c) || c.Dead || c.Downed || !sim.Map.InBounds(Cell))
            return;
        if (w.TryGet<Mind>(Pawn, out var mind) && mind.Drafted)
            Orders.MoveTo(sim, Pawn, Cell, Fly);
    }
}

/// <summary>Draft a villager (they drop their work and wait for move orders) or let them go.</summary>
public sealed record DraftCommand(Entity Pawn, bool Drafted) : ISimCommand, ISubjectCommand
{
    public Entity Subject => Pawn;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Pawn) || !w.TryGet<Mind>(Pawn, out var mind) || w.Has<Avatar>(Pawn) || mind.Drafted == Drafted)
            return;
        AiSystem.EndJob(sim, Pawn, mind, false);
        mind.Drafted = Drafted;
    }
}

/// <summary>Set one work priority (0 = never, 1 highest .. 4).</summary>
public sealed record WorkPriorityCommand(Entity Pawn, WorkType Work, int Priority) : ISimCommand, ISubjectCommand
{
    public Entity Subject => Pawn;

    public void Apply(Simulation sim)
    {
        if (!sim.World.IsAlive(Pawn) || !sim.World.TryGet<Mind>(Pawn, out var mind) || (int)Work < 0 || (int)Work >= mind.WorkPriority.Length)
            return;
        mind.WorkPriority[(int)Work] = Math.Clamp(Priority, 0, 4);
    }
}

/// <summary>Toggle the order to hunt an animal (or, with <paramref name="WholeHerd"/>, its whole herd).</summary>
public sealed record HuntOrderCommand(Entity Animal, bool Hunt, bool WholeHerd = false) : ISimCommand
{
    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Animal) || !w.TryGet<Creature>(Animal, out var c) || c.Def.Race != Race.Animal)
            return;
        if (WholeHerd && w.TryGet<Herd>(Animal, out var herd))
        {
            foreach (var mate in w.Query<Herd>())
                if (w.Get<Herd>(mate).Id == herd.Id && !w.Get<Creature>(mate).Dead)
                    Set(w, mate, Hunt);
            return;
        }
        Set(w, Animal, Hunt);
    }

    private static void Set(World w, Entity e, bool on)
    {
        if (on && !w.Has<HuntOrder>(e))
            w.Add(e, new HuntOrder());
        else if (!on)
            w.Remove<HuntOrder>(e);
    }
}

/// <summary>
/// Lay out a preset building (<see cref="Buildings.Place"/>): its plot centred on
/// <paramref name="Anchor"/> (the ground level), turned by <paramref name="Facing"/>, in the look
/// <paramref name="Variant"/>. Says why when it can't go there.
/// </summary>
public sealed record PlaceBuildingCommand(BuildingDef Def, int3 Anchor, int Facing, int Variant) : ISimCommand
{
    public void Apply(Simulation sim)
    {
        if (!sim.Map.InBounds(Anchor) || Variant < 0 || Variant >= Def.Variants.Count)
            return;
        if (Buildings.PlacementProblem(sim, Def, Anchor, Facing & 3, Variant) is { } problem)
        {
            sim.Messages.Add(MessageKind.Info, problem, Anchor);
            return;
        }
        Buildings.Place(sim, Def, Anchor, Facing & 3, Variant);
    }
}

/// <summary>Put a blueprint of <paramref name="Def"/> in a finished building's slot <paramref name="Slot"/> (<see cref="Buildings.FillSlot"/>).</summary>
public sealed record FillSlotCommand(Entity Building, int Slot, ThingDef Def) : ISimCommand
{
    public void Apply(Simulation sim)
    {
        if (Buildings.SlotProblem(sim, Building, Slot, Def) is { } problem)
        {
            if (sim.World.IsAlive(Building) && sim.World.TryGet<PresetBuilding>(Building, out var b))
                sim.Messages.Add(MessageKind.Info, problem, b.Anchor);
            return;
        }
        Buildings.FillSlot(sim, Building, Slot, Def);
    }
}

/// <summary>Order an upgrade fitted to a finished preset building (<see cref="Buildings.StartUpgrade"/>): the hearth's cooking pot.</summary>
public sealed record InstallUpgradeCommand(Entity Building, int Upgrade) : ISimCommand
{
    public void Apply(Simulation sim)
    {
        if (Buildings.UpgradeProblem(sim, Building, Upgrade) is { } problem)
        {
            if (sim.World.IsAlive(Building) && sim.World.TryGet<PresetBuilding>(Building, out var b))
                sim.Messages.Add(MessageKind.Info, problem, b.Anchor);
            return;
        }
        Buildings.StartUpgrade(sim, Building, Upgrade);
    }
}

/// <summary>Take down a preset building, or give up on one going up (<see cref="Buildings.Demolish"/>).</summary>
public sealed record DemolishBuildingCommand(Entity Building) : ISimCommand
{
    public void Apply(Simulation sim)
    {
        if (sim.World.IsAlive(Building) && sim.World.Has<PresetBuilding>(Building))
            Buildings.Demolish(sim, Building);
    }
}

/// <summary>The village's standing order to keep this much raw meat and fish in store (0: off; <see cref="Systems.LarderSystem"/>).</summary>
public sealed record LarderCommand(int KeepMeat) : ISimCommand
{
    public void Apply(Simulation sim) => sim.Larder.KeepMeat = Math.Clamp(KeepMeat, 0, 2000);
}

/// <summary>Toggle the order to capture a raider once they're downed.</summary>
public sealed record CaptureOrderCommand(Entity Raider, bool Capture) : ISimCommand
{
    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Raider) || !w.TryGet<Creature>(Raider, out var c) || c.Faction != Faction.Raider)
            return;
        if (Capture && !w.Has<CaptureOrder>(Raider))
            w.Add(Raider, new CaptureOrder());
        else if (!Capture)
            w.Remove<CaptureOrder>(Raider);
    }
}

/// <summary>Hold, recruit or release a prisoner.</summary>
public sealed record PrisonerModeCommand(Entity Prisoner, PrisonerMode Mode) : ISimCommand
{
    public void Apply(Simulation sim)
    {
        if (sim.World.IsAlive(Prisoner) && sim.World.Has<Prisoner>(Prisoner) && Enum.IsDefined(Mode))
            sim.World.Get<Prisoner>(Prisoner).Mode = Mode;
    }
}

/// <summary>Reserve a bed for prisoners, or give it back to the village.</summary>
public sealed record PrisonerBedCommand(Entity Bed, bool ForPrisoners) : ISimCommand
{
    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Bed) || !w.TryGet<Thing>(Bed, out var t) || t.Def.Building is not { IsBed: true } || w.Has<Construction>(Bed))
            return;
        if (ForPrisoners && !w.Has<ForPrisoners>(Bed))
            w.Add(Bed, new ForPrisoners());
        else if (!ForPrisoners)
            w.Remove<ForPrisoners>(Bed);
    }
}

/// <summary>Cancel a construction site (delivered materials are dropped).</summary>
public sealed record CancelConstructionCommand(Entity Site) : ISimCommand
{
    public void Apply(Simulation sim)
    {
        if (sim.World.IsAlive(Site) && sim.World.Has<Construction>(Site))
            sim.CancelConstruction(Site);
    }
}

/// <summary>
/// A developer tool command (the dev toolbar). In a multiplayer session only the host may send these.
/// </summary>
public interface IDevCommand : ISimCommand;

/// <summary>Dev: set fire to a cell.</summary>
public sealed record DevIgniteCommand(int3 Cell) : IDevCommand
{
    public void Apply(Simulation sim)
    {
        if (sim.Map.InBounds(Cell) && !sim.Fire.Ignite(sim, Cell, 0.6f))
            sim.Messages.Add(MessageKind.Info, "Nothing there will burn.", Cell);
    }
}

/// <summary>Dev: drop a stack of an item, or a finished structure, at a cell.</summary>
public sealed record DevSpawnCommand(ThingDef Def, int3 Cell) : IDevCommand
{
    public void Apply(Simulation sim)
    {
        if (!sim.Map.InBounds(Cell))
            return;
        if (Def.Category == ThingCategory.Item)
            sim.SpawnItem(Def, Math.Min(Def.StackLimit, 25), Cell);
        else if (sim.CanPlaceStructure(Cell, Def))
            sim.SpawnStructure(Def, Cell);
    }
}

/// <summary>Dev: shift the weather by some degrees.</summary>
public sealed record DevWeatherCommand(float Offset) : IDevCommand
{
    public void Apply(Simulation sim) => sim.Climate.Offset = Math.Clamp(Offset, -50, 50);
}

/// <summary>
/// Dev: a band of <paramref name="Count"/> raiders of a kind: gathered at <paramref name="At"/>,
/// else coming in off the nearest map edge toward the village (the fight starts once they're in
/// sight). How to start a fight with the storyteller off (co-op, for now).
/// </summary>
public sealed record DevRaidCommand(RaiderKindDef Kind, int Count, int3? At = null) : IDevCommand
{
    public void Apply(Simulation sim)
    {
        var home = Players.Home(sim);
        var edges = Enumerable.Range(0, 4).Select(side => Combat.Raids.EdgeEntry(sim, side, home)).OfType<int3>().ToList();
        if (At is not { } entry || !sim.Map.InBounds(entry))
        {
            if (edges.Count == 0)
                return;
            entry = edges.MinBy(c => Combat.CombatRules.Distance(c, home));
        }
        var raiders = Combat.Raids.Spawn(sim, Enumerable.Repeat(Kind, Math.Clamp(Count, 1, 8)).ToList(), entry, home, sim.Rand);
        if (raiders.Count > 0)
            sim.Messages.Add(MessageKind.Danger, $"{raiders.Count} raider{(raiders.Count == 1 ? "" : "s")} appear.", entry);
    }
}

/// <summary>Debug: a wild event now (<see cref="WildEventKind"/>: a wolf pack, a migration).</summary>
public sealed record DevWildEventCommand(WildEventKind Kind) : IDevCommand
{
    public void Apply(Simulation sim)
    {
        if (Enum.IsDefined(Kind))
            sim.Storyteller.SendWildEvent(sim, Kind);
    }
}

