using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Preset buildings (<see cref="Buildings"/>): plans, phases, extras, taking them down.</summary>
public class PresetBuildingTests
{
    [Fact]
    public void EveryPresetCanBeGotAboutOnceFurnished()
    {
        var problems = TestWorld.LoadDefs().AllBuildings.Select(b => b.LayoutProblem()).OfType<string>().ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static Simulation World(bool presetMode = true)
    {
        var sim = TestWorld.Create(30, 30, depth: 5);
        sim.Presets.PresetMode = presetMode;
        return sim;
    }

    private static BuildingDef Plan(Simulation sim, string name) => sim.Defs.Building(name);

    /// <summary>The building's unbuilt parts, by def name.</summary>
    private static List<string> Sites(Simulation sim, Entity building) =>
        Buildings.Parts(sim, building).Where(p => sim.World.Has<Construction>(p)).Select(p => sim.World.Get<Thing>(p).Def.Name).Distinct().Order().ToList();

    /// <summary>Finish every part laid so far (as if built), which lays the next phase.</summary>
    private static void FinishPhase(Simulation sim, Entity building)
    {
        foreach (var p in Buildings.Parts(sim, building))
            if (sim.World.Has<Construction>(p))
                sim.CompleteConstruction(p);
    }

    [Fact]
    public void APlanTurnsWithItsFacing()
    {
        var sim = World();
        foreach (var def in sim.Defs.AllBuildings)
        for (var facing = 0; facing < 4; facing++)
        {
            var anchor = C(15, 15);
            var cells = Buildings.Plot(sim.Map, def, anchor, facing).ToList();
            Assert.Equal(string.Concat(def.Layout).Count(ch => ch != ' '), cells.Select(p => p.Cell).Distinct().Count());
            foreach (var p in cells)
                Assert.Equal(p.Symbol, Buildings.SymbolAt(def, anchor, facing, p.Cell));
        }
        // A quarter turn swaps how wide and deep the plot is.
        var longhouse = Plan(sim, "Longhouse");
        var turned = Buildings.Plot(sim.Map, longhouse, C(15, 15), 1).Select(p => p.Cell).ToList();
        Assert.Equal(longhouse.Depth, turned.Max(c => c.X) - turned.Min(c => c.X) + 1);
        Assert.Equal(longhouse.Width, turned.Max(c => c.Y) - turned.Min(c => c.Y) + 1);
    }

    [Fact]
    public void ADwellingGoesUpPhaseByPhase()
    {
        var sim = World();
        var b = Buildings.Place(sim, Plan(sim, "Dwelling"), C(12, 12), 0);
        Assert.False(b.IsNull);
        var state = sim.World.Get<PresetBuilding>(b);

        // Each raised phase is one site, costing what all its parts do.
        Assert.Equal(BuildPhase.Foundation, state.Phase);
        Assert.Equal(["Dwelling_Timber_Foundation"], Sites(sim, b));
        var site = Buildings.PhaseSite(sim, b);
        Assert.Equal(30, sim.World.Get<Construction>(site).Target.Building!.Cost.Single(c => c.ThingDef.Name == "Wood").Count); // a timber sill per cell
        Assert.Equal(0, sim.World.Count<Construction>() - 1); // nothing laid cell by cell

        FinishPhase(sim, b);
        Assert.Equal(BuildPhase.Walls, state.Phase);
        Assert.Equal(["Dwelling_Timber_Walls"], Sites(sim, b));
        Assert.Equal(30, Buildings.Parts(sim, b).Count(p => sim.World.Get<Thing>(p).Def.Name == "Sill")); // all at once

        FinishPhase(sim, b);
        Assert.Equal(BuildPhase.Roof, state.Phase);
        Assert.Equal(["Dwelling_Timber_Roof"], Sites(sim, b));
        Assert.Equal(16, Buildings.Parts(sim, b).Count(p => sim.World.Get<Thing>(p).Def.Name == "TimberWall")); // a gap left for the fireplace

        FinishPhase(sim, b);
        Assert.All(Buildings.Parts(sim, b).Where(p => sim.World.Get<Thing>(p).Def.IsRoof), p => Assert.Equal(2, sim.World.Get<Thing>(p).Cell.Z));
        Assert.Equal(BuildPhase.Furnish, state.Phase);
        Assert.Equal(["Bed", "Fireplace"], Sites(sim, b));

        FinishPhase(sim, b);
        Assert.Equal(BuildPhase.Done, state.Phase);
        Assert.Empty(Sites(sim, b));
        sim.RebuildDerivedIfNeeded();
        var inside = Buildings.CellOf(sim.Map, state.Def, state.Anchor, 0, 2, 2);
        Assert.True(sim.Rooms.RoomAt(inside)?.Indoors, "its walls, fireplace and roof make a room");
        Assert.True(sim.Map.Covered[sim.Map.Idx(inside)], "under its own roof");
        Assert.Contains(sim.Messages.Messages, m => m.Text == "The timber-framed dwelling is finished.");
    }

    [Fact]
    public void APlotNeedsLevelOpenGround()
    {
        var sim = World();
        var dwelling = Plan(sim, "Dwelling");
        Assert.Null(Buildings.PlacementProblem(sim, dwelling, C(12, 12), 0));
        Assert.NotNull(Buildings.PlacementProblem(sim, dwelling, C(1, 1), 0)); // off the map's edge

        sim.SpawnStructure(sim.Thing("StoneWall"), C(12, 12));
        Assert.NotNull(Buildings.PlacementProblem(sim, dwelling, C(12, 12), 0));

        var hill = World();
        hill.FillBlocks(new int3(13, 12, 1), new int3(13, 12, 1)); // one cell raised in the plot
        Assert.NotNull(Buildings.PlacementProblem(hill, dwelling, C(12, 12), 0));

        // Another building's plot, even one still only planned, is taken.
        var planned = World();
        Assert.False(Buildings.Place(planned, dwelling, C(12, 12), 0).IsNull);
        Assert.NotNull(Buildings.PlacementProblem(planned, dwelling, C(14, 13), 0));
    }

    [Fact]
    public void TreesOnThePlotAreFelledFirst()
    {
        var sim = World();
        var corner = Buildings.CellOf(sim.Map, Plan(sim, "Dwelling"), C(12, 12), 0, 0, 0); // where a wall goes
        var tree = sim.SpawnStructure(sim.Thing("Oak"), corner);
        var b = Buildings.Place(sim, Plan(sim, "Dwelling"), C(12, 12), 0);
        Assert.False(b.IsNull, "a tree doesn't stop it being placed");
        Assert.True((sim.Map.Designations[sim.Map.Idx(corner)] & Designation.Chop) != 0, "it's marked to be felled");

        FinishPhase(sim, b);
        Assert.Equal(BuildPhase.Walls, sim.World.Get<PresetBuilding>(b).Phase); // floors go under the tree
        FinishPhase(sim, b);
        Assert.Equal(BuildPhase.Walls, sim.World.Get<PresetBuilding>(b).Phase); // ...but a wall can't go up through it
        Assert.Equal(1, Buildings.Progress(sim, b, BuildPhase.Walls).Blocked);

        sim.DestroyThing(tree); // felled
        Buildings.Tend(sim, b);
        FinishPhase(sim, b);
        Assert.Equal(BuildPhase.Roof, sim.World.Get<PresetBuilding>(b).Phase);
    }

    [Fact]
    public void ExtrasGoInTheirSlots()
    {
        var sim = World();
        var b = Buildings.SpawnFinished(sim, Plan(sim, "Dwelling"), C(12, 12), 0);
        var state = sim.World.Get<PresetBuilding>(b);
        Assert.Equal(BuildPhase.Done, state.Phase);
        var bed = sim.Thing("Bed");
        var spots = Buildings.Slots(sim, state).ToList();

        // The two bed slots take a bed each (or a straw pallet), then there's no free spot for a third.
        foreach (var spot in spots.Where(s => s.Slot.Accepts(bed)))
        {
            Assert.Equal(spot, Buildings.FreeSlotFor(sim, b, bed, spot.Cell));
            sim.ApplyNow(new FillSlotCommand(b, spot.Index, bed));
            Assert.False(Buildings.InSlot(sim, b, spot).IsNull);
        }
        Assert.Null(Buildings.FreeSlotFor(sim, b, bed, state.Anchor));
        Assert.Equal("That spot is taken.", Buildings.SlotProblem(sim, b, spots.First(s => s.Slot.Accepts(bed)).Index, bed));

        // Only at a slot, exactly: the build order inside goes by the same rule.
        var chestSpot = spots.First(s => s.Slot.Accepts(sim.Thing("Chest")));
        Assert.Null(Buildings.PlayerProblem(sim, sim.Thing("Chest"), chestSpot.Cell, chestSpot.Facing, out var into));
        Assert.Equal(b, into);
        Assert.NotNull(Buildings.PlayerProblem(sim, sim.Thing("Chest"), state.Anchor, 0, out _)); // not a slot
        Assert.NotNull(Buildings.PlayerProblem(sim, sim.Thing("Forge"), chestSpot.Cell, chestSpot.Facing, out _)); // not in a house
        Assert.Contains("takes", Buildings.SlotProblem(sim, b, chestSpot.Index, bed));

        // Not before it's finished.
        var going = Buildings.Place(sim, Plan(sim, "Dwelling"), C(22, 22), 0);
        Assert.Contains("isn't finished", Buildings.SlotProblem(sim, going, 0, bed));

        // Outside: only what may stand outdoors (a fence, a campfire), unless preset buildings are off.
        Assert.NotNull(Buildings.PlayerProblem(sim, sim.Thing("Table"), C(3, 3), 0, out _));
        Assert.NotNull(Buildings.PlayerProblem(sim, sim.Thing("WoodWall"), C(3, 3), 0, out _));
        Assert.Null(Buildings.PlayerProblem(sim, sim.Thing("Fence"), C(3, 3), 0, out _));
        Assert.Null(Buildings.PlayerProblem(sim, sim.Thing("Campfire"), C(3, 3), 0, out _));
        sim.Presets.PresetMode = false;
        Assert.Null(Buildings.PlayerProblem(sim, sim.Thing("WoodWall"), C(3, 3), 0, out _));
        Assert.NotNull(Buildings.PlayerProblem(sim, sim.Thing("LogWall"), C(3, 3), 0, out _)); // parts never alone
    }

    [Fact]
    public void TheSmithyComesWithItsBellowsAnvilTubAndGrindstone()
    {
        var sim = World();
        var b = Buildings.SpawnFinished(sim, Plan(sim, "Smithy"), C(12, 12), 0);
        var parts = Buildings.Parts(sim, b).Select(p => sim.World.Get<Thing>(p).Def.Name).ToList();
        foreach (var name in new[] { "Forge", "Anvil", "QuenchTub", "Grindstone", "ToolRack", "FuelBin", "Chest" })
            Assert.Contains(name, parts);
        var forge = Buildings.Parts(sim, b).First(p => sim.World.Get<Thing>(p).Def.Name == "Forge");
        Assert.Same(sim.Thing("Bellows").Building!.Bellows, Crafting.BellowsOf(sim, forge));
    }

    [Fact]
    public void ChestsGoInTheSmithysSlots()
    {
        var sim = World();
        var b = Buildings.SpawnFinished(sim, Plan(sim, "Smithy"), C(12, 12), 0);
        var state = sim.World.Get<PresetBuilding>(b);
        var chest = sim.Thing("Chest");
        // One chest comes with it (where the carter leaves the ore); the other is a slot.
        var spots = Buildings.Slots(sim, state).Where(s => s.Slot.Accepts(chest)).ToList();
        Assert.Single(spots);
        var beside = spots[0].Cell;
        Assert.NotNull(Buildings.PlayerProblem(sim, chest, Buildings.CellOf(sim.Map, state.Def, state.Anchor, 0, 3, 2), 0, out _)); // in front of the forge: no slot

        // Through the order a player gives, the chest becomes part of the smithy.
        sim.ApplyNow(new AreaOrderCommand(AreaTool.Build, new int2(beside.X, beside.Y), new int2(beside.X, beside.Y), beside.Z, 4, chest, spots[0].Facing));
        var site = sim.Map.Structure[sim.Map.Idx(beside)];
        Assert.Equal(b, Buildings.Of(sim, site));
        var built = sim.CompleteConstruction(site);
        Assert.Equal(b, Buildings.Of(sim, built));
    }

    [Fact]
    public void DemolishingTakesItDownTopDownAndLeavesNothing()
    {
        var sim = World();
        var b = Buildings.SpawnFinished(sim, Plan(sim, "Dwelling"), C(12, 12), 0);
        sim.ApplyNow(new DemolishBuildingCommand(b));
        for (var round = 0; round < 20 && sim.World.IsAlive(b); round++)
        {
            // Do what deconstructors would: everything marked that can come down now.
            foreach (var p in Buildings.Parts(sim, b))
            {
                var t = sim.World.Get<Thing>(p);
                if ((sim.Map.Designations[sim.Map.Idx(t.Cell)] & Designation.Deconstruct) != 0 && sim.CanDeconstruct(p, out _))
                {
                    sim.Deconstruct(p);
                    sim.Map.ClearDesignation(sim.Map.Idx(t.Cell), Designation.Deconstruct);
                }
            }
            Buildings.Tend(sim, b);
        }
        Assert.False(sim.World.IsAlive(b));
        Assert.Equal(0, sim.World.Count<PartOf>());
    }

    [Fact]
    public void CancellingABuildingGoingUpGivesTheMaterialsBack()
    {
        var sim = World();
        var b = Buildings.Place(sim, Plan(sim, "Dwelling"), C(12, 12), 0);
        var plinth = Buildings.Parts(sim, b)[0];
        sim.World.Get<Construction>(plinth).Delivered[0] = 1;
        sim.ApplyNow(new DemolishBuildingCommand(b));
        Assert.Equal(0, sim.World.Count<PartOf>());
        Assert.False(sim.World.IsAlive(b));
        Assert.Contains(sim.SpawnedItems(), e => sim.World.Get<Thing>(e).Def.Name == "Wood");
    }

    [Fact]
    public void ABuildingHalfwayUpSurvivesASave()
    {
        var sim = World();
        var b = Buildings.Place(sim, Plan(sim, "Dwelling"), C(12, 12), 0, variant: 2);
        FinishPhase(sim, b);

        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
        Assert.True(loaded.Presets.PresetMode);
        var again = Assert.Single(loaded.World.Query<PresetBuilding>());
        var state = loaded.World.Get<PresetBuilding>(again);
        Assert.Equal((BuildPhase.Walls, 2), (state.Phase, state.Variant));
        Assert.Equal(["Dwelling_Thatch_Walls"], Sites(loaded, again)); // the thatched look's walls
        FinishPhase(loaded, again);
        Assert.Contains(Buildings.Parts(loaded, again), p => loaded.World.Get<Thing>(p).Def.Name == "StaveWall");
        Assert.Equal(BuildPhase.Roof, state.Phase);
    }

    /// <summary>Hours for <paramref name="builders"/> villagers to raise a dwelling, and the most seen working on one phase at once.</summary>
    private static (int Hours, int MostAtOnce, Simulation Sim, Entity Building) Raise(int builders)
    {
        var sim = World();
        for (var k = 0; k < 4; k++)
            sim.SpawnItem(sim.Thing("Wood"), 50, C(4 + k, 4));
        sim.SpawnItem(sim.Thing("Stone"), 40, C(4, 6));
        for (var n = 0; n < builders; n++)
        {
            var pawn = sim.SpawnCreature(sim.Defs.Creature("Human"), $"Builder {n}", C(6 + n, 6));
            sim.SetSkills(pawn);
            Array.Fill(sim.World.Get<Needs>(pawn).Values, 1f);
            var mind = sim.World.Get<Mind>(pawn);
            Array.Fill(mind.WorkPriority, 0);
            mind.WorkPriority[(int)WorkType.Construct] = 1;
            mind.WorkPriority[(int)WorkType.Haul] = 2;
        }
        sim.ApplyNow(new PlaceBuildingCommand(Plan(sim, "Dwelling"), C(16, 16), 1, 0));
        var b = Assert.Single(sim.World.Query<PresetBuilding>());
        var most = 0;
        var ticks = 0;
        for (; ticks < SimClock.TicksPerHour * 48 && sim.World.Get<PresetBuilding>(b).Phase != BuildPhase.Done; ticks += 60)
        {
            sim.TickMany(60);
            if (Buildings.PhaseSite(sim, b) is { IsNull: false } site)
                most = Math.Max(most, Buildings.Builders(sim, site, Entity.Null));
        }
        return ((int)Math.Ceiling(ticks / (float)SimClock.TicksPerHour), most, sim, b);
    }

    [Fact]
    public void SeveralVillagersRaiseAPhaseTogetherAndFaster()
    {
        var (alone, _, _, _) = Raise(1);
        var (together, most, sim, b) = Raise(3);
        Assert.Equal(BuildPhase.Done, sim.World.Get<PresetBuilding>(b).Phase);
        Assert.True(most >= 2, $"at most {most} worked on one phase at once");
        Assert.True(together < alone, $"three took {together} h, one {alone} h");
    }

    [Fact]
    public void VillagersRaiseADwellingFromItsBlueprint()
    {
        var sim = World();
        sim.SpawnItem(sim.Thing("Wood"), 50, C(4, 4));
        sim.SpawnItem(sim.Thing("Wood"), 50, C(4, 5));
        sim.SpawnItem(sim.Thing("Wood"), 50, C(5, 5));
        sim.SpawnItem(sim.Thing("Stone"), 40, C(5, 4));
        foreach (var name in (string[])["Astrid", "Bjorn"])
        {
            var pawn = sim.SpawnCreature(sim.Defs.Creature("Human"), name, C(6, 6));
            Array.Fill(sim.World.Get<Needs>(pawn).Values, 1f);
            var mind = sim.World.Get<Mind>(pawn);
            Array.Fill(mind.WorkPriority, 0);
            mind.WorkPriority[(int)WorkType.Construct] = 1;
            mind.WorkPriority[(int)WorkType.Haul] = 2;
        }
        sim.ApplyNow(new PlaceBuildingCommand(Plan(sim, "Dwelling"), C(16, 16), 1, 0));
        var b = Assert.Single(sim.World.Query<PresetBuilding>());

        for (var h = 0; h < 36 && sim.World.Get<PresetBuilding>(b).Phase != BuildPhase.Done; h++)
            sim.TickMany(SimClock.TicksPerHour);
        var left = Buildings.Parts(sim, b).Where(p => sim.World.Has<Construction>(p))
            .Select(p => $"{sim.World.Get<Thing>(p).Def.Name}@{sim.World.Get<Thing>(p).Cell} canBuild={sim.CanBuildNow(p)} done={sim.World.Get<Construction>(p).MaterialsComplete}").ToList();
        Assert.True(sim.World.Get<PresetBuilding>(b).Phase == BuildPhase.Done,
            $"stuck at {sim.World.Get<PresetBuilding>(b).Phase}: {string.Join("; ", left.Take(6))}\n{string.Join("\n", sim.Messages.Messages.TakeLast(15).Select(m => m.Text))}");
        Assert.Contains(Buildings.Parts(sim, b), p => sim.World.Get<Thing>(p).Def.Name == "Bed" && !sim.World.Has<Construction>(p));
    }
}

public class PresetBuildingHeatTests
{
    /// <summary>
    /// A lit fireplace keeps a dwelling comfortable: a campfire there once pushed the room to 44°C
    /// (50 felt beside it, people's limit), and villagers fled it instead of building inside.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(18f)]
    public void AFireplaceKeepsADwellingComfortable(float outdoors)
    {
        var sim = TestWorld.Create(30, 30, depth: 5);
        sim.Climate.BaseTemperature = outdoors;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, outdoors);
        var b = Buildings.SpawnFinished(sim, sim.Defs.Building("Dwelling"), C(12, 12), 0);
        var hearth = Buildings.Parts(sim, b).Single(p => sim.World.Get<Thing>(p).Def.Name == "Fireplace");
        sim.World.Get<Fueled>(hearth).Fuel = 20;
        sim.TickMany(SimClock.TicksPerHour * 12);
        sim.World.Get<Fueled>(hearth).Fuel = 20;
        sim.TickMany(SimClock.TicksPerHour * 4);

        var state = sim.World.Get<PresetBuilding>(b);
        var human = sim.Defs.Creature("Human");
        foreach (var (col, row) in new[] { (1, 3), (4, 3), (3, 1), (2, 1) }) // the room, and right in front of the fire
        {
            var cell = Buildings.CellOf(sim.Map, state.Def, state.Anchor, 0, col, row);
            var felt = Environment.Warmth.FeltAt(sim, cell);
            Assert.InRange(felt, human.ComfortTempMin, human.ComfortTempMax);
        }
        // ...and the fireplace cooks what a campfire does.
        Assert.Subset(sim.World.Get<Thing>(hearth).Def.Recipes.ToHashSet(), sim.Thing("Campfire").Recipes.ToHashSet());
    }

    /// <summary>
    /// A fireplace is set in the wall, open to the room: its heat, light and warmth go into the
    /// room in front of it, and the street behind the wall gets none of it.
    /// </summary>
    [Fact]
    public void AFireplaceWarmsTheRoomNotTheStreetBehindIt()
    {
        var sim = TestWorld.Create(30, 30, depth: 5);
        sim.Climate.BaseTemperature = 0;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 0f);
        var b = Buildings.SpawnFinished(sim, sim.Defs.Building("Dwelling"), C(12, 12), 0);
        var state = sim.World.Get<PresetBuilding>(b);
        var fire = Buildings.Parts(sim, b).Single(p => sim.World.Get<Thing>(p).Def.Name == "Fireplace");
        sim.World.Get<Fueled>(fire).Fuel = 20;
        sim.TickMany(SimClock.TicksPerHour * 6);

        var front = Buildings.CellOf(sim.Map, state.Def, state.Anchor, 0, 3, 1);
        Assert.Equal(front, Environment.Warmth.FireCell(sim.World.Get<Thing>(fire)));
        var behind = Buildings.CellOf(sim.Map, state.Def, state.Anchor, 0, 3, -1);
        Assert.True(Environment.Warmth.FeltAt(sim, front) > Environment.Warmth.FeltAt(sim, behind) + 10, "warm in front, cold out back");
        Assert.True(sim.Map.Light[sim.Map.Idx(front)] > sim.Map.Light[sim.Map.Idx(behind)] || sim.Clock.Daylight > 0.5f);
    }
}

public class WoodshopTests
{
    [Fact]
    public void AWoodshopHoldsTheSawhorseAndTheCarpentersBench()
    {
        var sim = TestWorld.Create(30, 30, depth: 5);
        sim.Presets.PresetMode = true;
        var shop = sim.Defs.Building("Woodshop");

        // No foundation: the first site is its walls.
        var going = Buildings.Place(sim, shop, C(8, 8), 0);
        Assert.Equal(BuildPhase.Walls, sim.World.Get<PresetBuilding>(going).Phase);

        var b = Buildings.SpawnFinished(sim, shop, C(20, 20), 0);
        var state = sim.World.Get<PresetBuilding>(b);
        Assert.Contains(Buildings.Parts(sim, b), p => sim.World.Get<Thing>(p).Def.Name == "Sawhorse");
        var bench = sim.Thing("CarpenterBench");
        var spot = Buildings.Slots(sim, state).Single(s => s.Slot.Accepts(bench));
        sim.ApplyNow(new FillSlotCommand(b, spot.Index, bench));
        Assert.False(Buildings.InSlot(sim, b, spot).IsNull);

        // Neither stands out in the open while preset buildings are on.
        Assert.Contains("woodshop", Buildings.PlayerProblem(sim, sim.Thing("Sawhorse"), C(3, 3), 0, out _));
        Assert.Contains("woodshop", Buildings.PlayerProblem(sim, bench, C(3, 3), 0, out _));
    }
}

public class BuildingUpgradeTests
{
    [Fact]
    public void ACookingPotOnTheFireplaceUnlocksStewAndSurvivesASave()
    {
        var sim = TestWorld.Create(30, 30, depth: 5);
        sim.Presets.PresetMode = true;
        var b = Buildings.SpawnFinished(sim, sim.Defs.Building("Dwelling"), C(12, 12), 0);
        var hearth = Buildings.Parts(sim, b).Single(p => sim.World.Get<Thing>(p).Def.Name == "Fireplace");
        var stew = sim.Defs.Recipe("CookStew");
        Assert.Contains(stew, sim.World.Get<Thing>(hearth).Def.Recipes);
        Assert.False(Crafting.Fitted(sim, hearth, stew), "no pot yet");
        sim.ApplyNow(new AddBillCommand(hearth, stew, BillMode.Count, 1));
        Assert.Empty(sim.World.Get<Workbench>(hearth).Bills); // refused without the pot

        // Not on a building going up; then fitted as a site of its own at the hearth.
        Assert.NotNull(Buildings.UpgradeProblem(sim, Buildings.Place(sim, sim.Defs.Building("Dwelling"), C(22, 22), 0), 0));
        sim.ApplyNow(new InstallUpgradeCommand(b, 0));
        var site = Buildings.UpgradeSite(sim, b, sim.World.Get<PresetBuilding>(b).Def.Upgrades[0]);
        Assert.False(site.IsNull);
        Assert.Equal(sim.World.Get<Thing>(hearth).Cell, sim.World.Get<Thing>(site).Cell);
        Assert.Equal(site, Buildings.SiteAt(sim, sim.World.Get<Thing>(hearth).Cell)); // where F works on it
        sim.CompleteConstruction(site);
        Assert.True(Crafting.Fitted(sim, hearth, stew));
        Assert.NotNull(Buildings.UpgradeProblem(sim, b, 0)); // fitted once

        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
        var again = loaded.World.Query<PresetBuilding>().Single(e => loaded.World.Get<PresetBuilding>(e).Phase == BuildPhase.Done);
        Assert.Equal(["CookingPot"], loaded.World.Get<PresetBuilding>(again).Upgrades);
        var loadedHearth = Buildings.Parts(loaded, again).Single(p => loaded.World.Get<Thing>(p).Def.Name == "Fireplace");
        Assert.True(Crafting.Fitted(loaded, loadedHearth, stew));
    }

    [Fact]
    public void EatingInTheLonghouseIsAFeast()
    {
        var sim = TestWorld.Create(30, 30, depth: 5);
        var b = Buildings.SpawnFinished(sim, sim.Defs.Building("Longhouse"), C(15, 15), 0);
        var state = sim.World.Get<PresetBuilding>(b);
        var inside = Buildings.CellOf(sim.Map, state.Def, state.Anchor, 0, 4, 3);
        var outside = C(3, 3);
        var feast = sim.Defs.Thought("FeastedInTheHall");
        foreach (var (at, expect) in new[] { (inside, true), (outside, false) })
        {
            var pawn = sim.SpawnCreature(sim.Defs.Creature("Human"), "Eater", at);
            EatJob.Nourish(sim, pawn, sim.Thing("SimpleMeal"), 1);
            Assert.Equal(expect, sim.World.Get<Mood>(pawn).Memories.Any(m => m.Def == feast));
        }
    }
}

/// <summary>The inn's hanging lanterns (<see cref="BuildingDef.Lamps"/>): no fuel and no floor, so its rooms aren't dark at night.</summary>
public class LanternTests
{
    [Fact]
    public void LanternsLightTheInnAtNight()
    {
        var sim = TestWorld.Create(40, 40, depth: 6);
        var b = Buildings.SpawnFinished(sim, sim.Defs.Building("Inn"), C(20, 20), 0);
        var lamps = Buildings.LitLamps(sim).ToList();
        Assert.Equal(sim.Defs.Building("Inn").Lamps.Count, lamps.Count);
        Assert.All(lamps, l => Assert.True(sim.Map.Structure[sim.Map.Idx(l.Cell)].IsNull, "they take no floor"));
        sim.TickMany(SimClock.TicksPerHour * 13); // night
        Assert.True(sim.Clock.Daylight < 0.1f);
        var state = sim.World.Get<PresetBuilding>(b);
        foreach (var (col, row) in new[] { (6, 8), (9, 9), (5, 2) }) // the common room's middle, by the bar, the kitchen
        {
            var cell = Buildings.CellOf(sim.Map, state.Def, state.Anchor, 0, col, row);
            Assert.True(sim.Map.Light[sim.Map.Idx(cell)] > 0.4f, $"({col},{row}) is lit {sim.Map.Light[sim.Map.Idx(cell)]:F2}");
        }
    }
}
