using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

/// <summary>Workbenches, bills and recipes: villagers making things, and the player making them by hand.</summary>
public class CraftingTests
{
    private static Simulation Warm()
    {
        var sim = TestWorld.Create();
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Villager(Simulation sim, int3 at, params WorkType[] only)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Crafter", at, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        sim.SetSkills(e);
        var mind = sim.World.Get<Mind>(e);
        Array.Fill(mind.WorkPriority, 0);
        foreach (var w in only)
            mind.WorkPriority[(int)w] = 1;
        return e;
    }

    private static Entity Station(Simulation sim, string def, int3 at)
    {
        var e = sim.SpawnStructure(sim.Thing(def), at);
        sim.RebuildDerivedIfNeeded();
        return e;
    }

    private static Workbench Bench(Simulation sim, Entity station) => sim.World.Get<Workbench>(station);

    private static void AddBill(Simulation sim, Entity station, string recipe, BillMode mode = BillMode.Count, int target = 1) =>
        sim.ApplyNow(new AddBillCommand(station, sim.Defs.Recipe(recipe), mode, target));

    private static int Count(Simulation sim, string def, string? material = null) =>
        sim.AvailableItems().Where(e => sim.World.Get<Thing>(e).Def.Name == def
                && (material == null || sim.World.Get<Thing>(e).Material?.Name == material))
            .Sum(e => sim.World.Get<Stack>(e).Count);

    [Fact]
    public void ABillSawsOakIntoOakPlanks()
    {
        var sim = Warm();
        Villager(sim, C(5, 5), WorkType.Craft);
        var saw = Station(sim, "Sawhorse", C(12, 12));
        sim.SpawnItem(sim.Thing("Wood"), 14, C(6, 8), sim.Defs.Material("Oak"));
        AddBill(sim, saw, "SawPlanks", target: 2);

        sim.TickMany(SimClock.HoursToTicks(2));

        Assert.Equal(20, Count(sim, "Planks", "Oak"));
        Assert.Equal(2, Count(sim, "Wood"));
        Assert.Empty(Bench(sim, saw).Bills); // a count bill goes away once it's done
        Assert.True(Bench(sim, saw).Idle);
        var planks = sim.AvailableItems().First(e => sim.World.Get<Thing>(e).Def.Name == "Planks");
        Assert.Equal("oak planks", sim.World.Get<Thing>(planks).Label);
    }

    [Fact]
    public void ASawyerWorksFromBesideTheWideSawhorseNotOnIt()
    {
        var sim = Warm();
        var sawyer = Villager(sim, C(5, 5), WorkType.Craft);
        var saw = Station(sim, "Sawhorse", C(12, 12));
        var footprint = sim.FootprintCells(saw).ToHashSet();
        Assert.Equal(2, footprint.Count);
        sim.SpawnItem(sim.Thing("Wood"), 14, C(6, 8));
        AddBill(sim, saw, "SawPlanks", target: 2);

        var worked = 0;
        for (var t = 0; t < SimClock.HoursToTicks(2); t++)
        {
            sim.Tick();
            if (sim.World.Get<Mind>(sawyer).Job is CraftJob job && job.IsWorking(sim.Clock.Tick))
            {
                var at = sim.World.Get<Position>(sawyer).Cell;
                Assert.DoesNotContain(at, footprint);
                Assert.True(footprint.Any(c => Math.Abs(c.X - at.X) <= 1 && Math.Abs(c.Y - at.Y) <= 1), $"sawing from {at}, not beside it");
                worked++;
            }
        }
        Assert.True(worked > 0);
        Assert.Equal(20, Count(sim, "Planks"));
    }

    [Fact]
    public void UntilYouHaveStopsAtTheTarget()
    {
        var sim = Warm();
        Villager(sim, C(5, 5), WorkType.Craft);
        var saw = Station(sim, "Sawhorse", C(12, 12));
        sim.SpawnItem(sim.Thing("Wood"), 30, C(6, 8));
        AddBill(sim, saw, "SawPlanks", BillMode.UntilYouHave, 15);

        sim.TickMany(SimClock.HoursToTicks(3));

        Assert.Equal(20, Count(sim, "Planks")); // 10 wasn't enough, 20 is
        Assert.Equal(18, Count(sim, "Wood"));
        Assert.Single(Bench(sim, saw).Bills); // it stays, for when planks run low again
    }

    [Fact]
    public void SuspendedBillsAndUnskilledWorkersAreSkipped()
    {
        var sim = Warm();
        var worker = Villager(sim, C(5, 5), WorkType.Craft);
        sim.SetSkills(worker, 2);
        var bench = Station(sim, "CarpenterBench", C(12, 12));
        sim.SpawnItem(sim.Thing("Planks"), 8, C(6, 8));
        AddBill(sim, bench, "MakeBow");

        sim.TickMany(SimClock.HoursToTicks(1));
        Assert.True(Bench(sim, bench).Idle); // a bow takes Crafting 4
        Assert.True(Bench(sim, bench).Idle);

        sim.SetSkills(worker, 6);
        sim.ApplyNow(new EditBillCommand(bench, 0, sim.Defs.Recipe("MakeBow"), BillMode.Count, 1, Suspended: true, null));
        sim.TickMany(SimClock.HoursToTicks(1));
        Assert.True(Bench(sim, bench).Idle);

        sim.ApplyNow(new EditBillCommand(bench, 0, sim.Defs.Recipe("MakeBow"), BillMode.Count, 1, Suspended: false, null));
        Array.Fill(sim.World.Get<Needs>(worker).Values, 1f); // two idle hours: not off to bed or a meal
        sim.World.Get<Mind>(worker).Asleep = false;
        sim.TickMany(SimClock.HoursToTicks(2)); // a bow is over an hour's work
        // Made (and, being unarmed, the maker took it up).
        Assert.Equal(1, sim.World.Query<Thing>().Count(e => sim.World.Get<Thing>(e).Def.Name == "Bow"));
        Assert.Equal(4, Count(sim, "Planks"));
    }

    [Fact]
    public void MaterialFilterOnlyTakesThatWood()
    {
        var sim = Warm();
        Villager(sim, C(5, 5), WorkType.Craft);
        var saw = Station(sim, "Sawhorse", C(12, 12));
        sim.SpawnItem(sim.Thing("Wood"), 10, C(6, 6), sim.Defs.Material("Pine"));
        sim.SpawnItem(sim.Thing("Wood"), 6, C(20, 20), sim.Defs.Material("Birch"));
        AddBill(sim, saw, "SawPlanks");
        sim.ApplyNow(new EditBillCommand(saw, 0, sim.Defs.Recipe("SawPlanks"), BillMode.Count, 1, false, sim.Defs.Material("Birch")));

        sim.TickMany(SimClock.HoursToTicks(2));

        Assert.Equal(10, Count(sim, "Planks", "Birch"));
        Assert.Equal(10, Count(sim, "Wood", "Pine"));
    }

    [Fact]
    public void AnInterruptedCraftKeepsWhatWasBroughtAndTheWorkDone()
    {
        var sim = Warm();
        var worker = Villager(sim, C(5, 5), WorkType.Craft);
        var saw = Station(sim, "Sawhorse", C(12, 12));
        sim.SpawnItem(sim.Thing("Wood"), 6, C(6, 8));
        AddBill(sim, saw, "SawPlanks");

        for (var t = 0; t < SimClock.HoursToTicks(1) && Bench(sim, saw).Progress < 200; t++)
            sim.Tick();
        Assert.True(Bench(sim, saw).Stocked);
        var done = Bench(sim, saw).Progress;
        Assert.InRange(done, 200, 599);
        Assert.Equal(0, Count(sim, "Wood")); // used up as it was brought: nobody can haul it off

        AiSystem.InterruptAll(sim);
        Assert.Equal(done, Bench(sim, saw).Progress);

        sim.TickMany(SimClock.HoursToTicks(1));
        Assert.Equal(10, Count(sim, "Planks"));
        Assert.Null(sim.World.Get<Mind>(worker).Job is CraftJob ? sim.World.Get<Mind>(worker).Job : null);
    }

    [Fact]
    public void RemovingTheBillOrTheStationGivesBackWhatWasBrought()
    {
        var sim = Warm();
        Villager(sim, C(5, 5), WorkType.Craft);
        var saw = Station(sim, "Sawhorse", C(12, 12));
        sim.SpawnItem(sim.Thing("Wood"), 6, C(6, 8), sim.Defs.Material("Oak"));
        AddBill(sim, saw, "SawPlanks");
        for (var t = 0; t < SimClock.HoursToTicks(1) && !Bench(sim, saw).Stocked; t++)
            sim.Tick();
        Assert.True(Bench(sim, saw).Stocked);

        sim.ApplyNow(new RemoveBillCommand(saw, 0, sim.Defs.Recipe("SawPlanks")));
        Assert.True(Bench(sim, saw).Idle);
        Assert.Equal(6, Count(sim, "Wood", "Oak"));

        // The same once it's half made, and the sawhorse is taken apart instead.
        AddBill(sim, saw, "SawPlanks");
        for (var t = 0; t < SimClock.HoursToTicks(1) && !Bench(sim, saw).Stocked; t++)
            sim.Tick();
        AiSystem.InterruptAll(sim);
        sim.Deconstruct(saw);
        Assert.Equal(6, Count(sim, "Wood", "Oak"));
    }

    [Fact]
    public void ALoadedCraftCarriesOnLikeTheOriginal()
    {
        var sim = Warm();
        Villager(sim, C(5, 5), WorkType.Craft);
        var saw = Station(sim, "Sawhorse", C(12, 12));
        sim.SpawnItem(sim.Thing("Wood"), 20, C(6, 8), sim.Defs.Material("Pine"));
        AddBill(sim, saw, "SawPlanks", BillMode.Forever);
        sim.ApplyNow(new EditBillCommand(saw, 0, sim.Defs.Recipe("SawPlanks"), BillMode.Forever, 1, false, sim.Defs.Material("Pine")));
        for (var t = 0; t < SimClock.HoursToTicks(1) && Bench(sim, saw).Progress < 100; t++)
            sim.Tick();
        Assert.True(Bench(sim, saw).Progress > 0);

        var bytes = SaveGame.Write(sim);
        var (loaded, _) = SaveGame.Read(bytes, sim.Defs);
        AiSystem.InterruptAll(sim);
        var copy = loaded.World.Get<Workbench>(saw);
        Assert.Equal(Bench(sim, saw).Progress, copy.Progress);
        Assert.Equal("Pine", copy.Bills[0].Material?.Name);

        Assert.Equal(sim.StateHash(), loaded.StateHash());
        for (var h = 0; h < 3; h++)
        {
            sim.TickMany(SimClock.TicksPerHour);
            loaded.TickMany(SimClock.TicksPerHour);
            Assert.True(sim.StateHash() == loaded.StateHash(), $"diverged within hour {h + 1}");
        }
        Assert.Equal(30, Count(loaded, "Planks", "Pine"));
    }

    [Fact]
    public void ThePlayerSawsPlanksFromTheirPackWhileHoldingTheKey()
    {
        var sim = Warm();
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Bearer", C(10, 12), ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(me).Values, 1f);
        sim.SetSkills(me);
        sim.World.Add(me, new Avatar());
        var pack = sim.GiveStartingPack(me);
        sim.Store(pack, sim.CreateItem(sim.Thing("Wood"), 4, sim.Defs.Material("Birch")));
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("Wood"), 3, sim.Defs.Material("Birch"));
        var saw = Station(sim, "Sawhorse", C(11, 12));

        Assert.Equal(AvatarActionKind.OpenStation, AvatarSystem.ActionAt(sim, me, C(11, 12)).Kind);
        sim.ApplyNow(new CraftCommand(me, saw, sim.Defs.Recipe("SawPlanks")));
        Assert.True(Bench(sim, saw).Stocked);
        Assert.Equal(me, Bench(sim, saw).By);
        // The hands' three went first, then three of the pack's four.
        Assert.True(sim.World.Get<Carrying>(me).Item.IsNull);
        Assert.Equal(1, sim.StoredIn(pack).Sum(e => sim.World.Get<Stack>(e).Count));

        // Nothing happens until the key is held; then it's made, into the pack.
        sim.TickMany(SimClock.TicksPerSecond * 20);
        Assert.Equal(0f, Bench(sim, saw).Progress);
        Assert.Equal(AvatarActionKind.Craft, AvatarSystem.ActionAt(sim, me, C(11, 12)).Kind);
        sim.ApplyNow(new AvatarWorkCommand(me, true, C(11, 12)));
        sim.TickMany(SimClock.TicksPerSecond * 15);
        Assert.True(Bench(sim, saw).Idle);
        var made = sim.StoredIn(pack).Where(e => sim.World.Get<Thing>(e).Def.Name == "Planks").ToList();
        Assert.Equal(10, made.Sum(e => sim.World.Get<Stack>(e).Count));
        Assert.All(made, e => Assert.Equal("Birch", sim.World.Get<Thing>(e).Material?.Name));
    }

    [Fact]
    public void ThePlayerCanBringIngredientsInParts()
    {
        var sim = Warm();
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Bearer", C(10, 12), ageDays: 25 * 365);
        sim.World.Add(me, new Avatar());
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("Wood"), 4);
        var saw = Station(sim, "Sawhorse", C(11, 12));
        var villager = Villager(sim, C(4, 4), WorkType.Craft);
        AddBill(sim, saw, "SawPlanks");
        sim.SpawnItem(sim.Thing("Wood"), 6, C(4, 6));

        sim.ApplyNow(new CraftCommand(me, saw, sim.Defs.Recipe("SawPlanks")));
        Assert.Equal(4, Bench(sim, saw).Staged[0]);
        Assert.True(sim.World.Get<Carrying>(me).Item.IsNull);
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("still needs 2"));

        // The villager leaves the player's craft alone (the bill waits)...
        sim.TickMany(SimClock.HoursToTicks(1));
        Assert.Equal(4, Bench(sim, saw).Staged[0]);
        Assert.False(sim.World.Get<Mind>(villager).Job is CraftJob);

        // ...until the player calls it off and gets the wood back.
        sim.ApplyNow(new CancelCraftCommand(saw));
        Assert.Equal(10, Count(sim, "Wood"));
        sim.TickMany(SimClock.HoursToTicks(1));
        Assert.Equal(10, Count(sim, "Planks"));
    }

    [Fact]
    public void PlankWallsAreBuiltFromPlanksAndKeepTheirWood()
    {
        var sim = Warm();
        Villager(sim, C(5, 5), WorkType.Construct);
        sim.SpawnItem(sim.Thing("Planks"), 6, C(6, 6), sim.Defs.Material("Oak"));
        sim.PlaceBlueprint(sim.Thing("PlankWall"), C(12, 12));
        sim.RebuildDerivedIfNeeded();

        sim.TickMany(SimClock.TicksPerHour);

        var wall = sim.Map.Structure[sim.Map.Idx(C(12, 12))];
        Assert.Equal("oak plank wall", sim.World.Get<Thing>(wall).Label);
        Assert.True(sim.Thing("PlankWall").Building!.Insulation > sim.Thing("WoodWall").Building!.Insulation);
    }

    [Fact]
    public void BillCommandsRefuseStaleAndBadRequests()
    {
        var sim = Warm();
        var saw = Station(sim, "Sawhorse", C(12, 12));
        sim.ApplyNow(new AddBillCommand(saw, sim.Defs.Recipe("MakeBow"), BillMode.Count, 1)); // made at the bench, not here
        Assert.Empty(Bench(sim, saw).Bills);
        for (var i = 0; i < 10; i++)
            AddBill(sim, saw, "SawPlanks");
        Assert.Equal(6, Bench(sim, saw).Bills.Count); // a sawhorse takes six
        sim.ApplyNow(new RemoveBillCommand(saw, 0, sim.Defs.Recipe("MakeBow")));
        Assert.Equal(6, Bench(sim, saw).Bills.Count);
        var blueprint = sim.PlaceBlueprint(sim.Thing("Sawhorse"), C(20, 20));
        sim.ApplyNow(new AddBillCommand(blueprint, sim.Defs.Recipe("SawPlanks"), BillMode.Count, 1));
        Assert.False(sim.World.Has<Workbench>(blueprint));
    }

    [Fact]
    public void RecipesResolveToTheirStations()
    {
        var defs = TestWorld.LoadDefs();
        Assert.Contains(defs.Recipe("SawPlanks"), defs.Thing("Sawhorse").Recipes);
        Assert.Equal(WorkType.Craft, defs.Recipe("SawPlanks").WorkType);
        Assert.True(defs.Material("Oak").Fits(defs.Thing("Planks")));
        Assert.False(defs.Material("Oak").Fits(defs.Thing("Stone")));
        var bad = Assert.Throws<Exception>(() => DefDatabase.FromJson(
            """{ "things": [ { "name": "Rock", "category": "Item", "mass": 1 } ], "recipes": [ { "name": "R", "station": "Rock", "ingredients": [ { "def": "Rock" } ], "products": [ { "def": "Rock" } ] } ] }"""));
        Assert.Contains("isn't a workbench", bad.Message);
    }
}
