using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Systems;
using static SupportingCast.Sim.Tests.TestWorld;

namespace SupportingCast.Sim.Tests;

/// <summary>The sorcerer's tower: what counts as built, and settling the pact.</summary>
public class TowerPactTests
{
    /// <summary>A flat map with a pact in force, the ley stone just west of where the tests build.</summary>
    private static Simulation PactMap(string? difficulty = "Normal")
    {
        var sim = Create(30, 30, depth: 6);
        sim.Story.Start = StartKind.Pact;
        sim.Story.PendingDifficulty = difficulty;
        TowerPact.Begin(sim, C(4, 10));
        return sim;
    }

    /// <summary>Rooms 7x7 stacked <paramref name="levels"/> high (floors between them), walled with <paramref name="wall"/>.</summary>
    private static void BuildTower(Simulation sim, int levels, string wall = "StoneWall")
    {
        for (var z = 1; z <= levels; z++)
        {
            if (z > 1)
                for (var y = 7; y <= 13; y++)
                for (var x = 7; x <= 13; x++)
                    sim.SpawnStructure(sim.Thing("WoodFloor"), new int3(x, y, z));
            sim.BuildWallRing(new int3(7, 7, z), new int3(13, 13, z), wall, door: z == 1 ? new int3(10, 7, 1) : null);
        }
        sim.RebuildDerivedIfNeeded();
    }

    private static void Furnish(Simulation sim)
    {
        sim.SpawnStructure(sim.Thing("Bed"), C(9, 9));
        sim.SpawnStructure(sim.Thing("Table"), new int3(9, 9, 2));
        sim.SpawnStructure(sim.Thing("Chest"), new int3(11, 11, 2));
        sim.SpawnStructure(sim.Thing("Campfire"), new int3(10, 10, 3));
        sim.RebuildDerivedIfNeeded();
    }

    [Fact]
    public void AStoneTowerThreeStoreysHighWithItsFurnitureSettlesThePact()
    {
        var sim = PactMap();
        var pact = TowerPact.Of(sim)!;
        Assert.False(pact.Done);
        Assert.All(pact.Met, Assert.False);
        Assert.Null(sim.Storyteller.Difficulty); // the sorcerer's protection holds
        Assert.Contains(sim.World.Query<Thing>(), e => sim.World.Get<Thing>(e).Def.Name == "LeyStone");

        BuildTower(sim, 3);
        TowerPact.Evaluate(sim);
        Assert.Equal(3, pact.Levels);
        Assert.True(pact.Met[0] && pact.Met[1]);
        Assert.False(pact.Done, "still unfurnished");

        Furnish(sim);
        sim.TickMany(SimClock.TicksPerHour); // the hourly check finds it
        Assert.True(pact.Done);
        Assert.False(TowerPact.Active(sim));
        Assert.Equal("Normal", sim.Storyteller.Difficulty?.Name);
        Assert.Null(sim.Story.PendingDifficulty);
    }

    [Fact]
    public void WoodenWallsDontCount()
    {
        var sim = PactMap();
        BuildTower(sim, 3, "WoodWall");
        Furnish(sim);
        TowerPact.Evaluate(sim);
        var pact = TowerPact.Of(sim)!;
        Assert.True(pact.Met[0]);
        Assert.False(pact.Met[1]);
        Assert.False(pact.Done);
    }

    [Fact]
    public void TwoStoreysAreNotATower()
    {
        var sim = PactMap(difficulty: null);
        BuildTower(sim, 2);
        sim.SpawnStructure(sim.Thing("Bed"), C(9, 9));
        sim.SpawnStructure(sim.Thing("Table"), C(11, 9));
        sim.SpawnStructure(sim.Thing("Chest"), new int3(11, 11, 2));
        sim.SpawnStructure(sim.Thing("Campfire"), new int3(10, 10, 2));
        TowerPact.Evaluate(sim);
        var pact = TowerPact.Of(sim)!;
        Assert.Equal(2, pact.Levels);
        Assert.False(pact.Met[0]);
        Assert.True(pact.Met.Skip(1).All(m => m));
        Assert.False(pact.Done);
    }

    [Fact]
    public void AnyShapeOfTableFurnishesTheTower()
    {
        var sim = PactMap(difficulty: null);
        BuildTower(sim, 2);
        sim.SpawnStructure(sim.Thing("LongTable"), C(11, 9));
        TowerPact.Evaluate(sim);
        var pact = TowerPact.Of(sim)!;
        var table = 2 + pact.Tower.ContainsDefs.FindIndex(d => d.Name == "Table");
        Assert.True(pact.Met[table]);
        Assert.True(sim.Thing("LongTable").IsA(sim.Thing("Table")));
        Assert.False(sim.Thing("Table").IsA(sim.Thing("LongTable")));
    }

    [Fact]
    public void ThePactAndItsProgressSurviveASave()
    {
        var sim = PactMap();
        BuildTower(sim, 3);
        sim.TickMany(SimClock.TicksPerHour);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var pact = TowerPact.Of(loaded)!;
        Assert.Equal(TowerPact.Of(sim)!.Site, pact.Site);
        Assert.Equal(3, pact.Levels);
        Assert.Equal(TowerPact.Of(sim)!.Met, pact.Met);
        Assert.Equal("SorcerersTower", pact.Tower.Name);
    }
}

/// <summary>The Pact's opening: moonpetal, the sorcerer's bargain, and the flight to the tower's site.</summary>
public class EnchantedForestTests
{
    private static Simulation NewSim(ulong seed = 41) =>
        Simulation.CreateStandard(LoadDefs(), 96, 96, seed, Maps.MapGenerator.RecommendedDepth);

    private static readonly Hero Wren = new("Wren", null, null, "Ashwing", null);

    private static Entity Sorcerer(Simulation sim) => sim.World.Query<StoryNpc>().Single();

    [Fact]
    public void TheForestHasEnoughMoonpetalAndTheSorcererWaitsInHisCircle()
    {
        var sim = NewSim();
        var me = AdventureStart.Setup(sim, StartKind.Pact, Wren, "Hard");
        var w = sim.World;
        Assert.Equal(QuestStage.GatherHerbs, w.Get<Quest>(me).Stage);
        Assert.Contains("moonpetal", Prologue.Objective(sim, me));
        Assert.Equal("Hard", sim.Story.PendingDifficulty);
        Assert.Null(sim.Storyteller.Difficulty);
        Assert.Null(TowerPact.Of(sim)); // the tower's owed only after the bargain
        var flowers = w.Query<Thing>().Count(e => w.Get<Thing>(e).Def.Name == "MoonpetalFlower");
        Assert.True(flowers >= EnchantedForest.HerbsNeeded, $"only {flowers} moonpetal");
        Assert.True(w.Query<Thing>().Count(e => w.Get<Thing>(e).Def.Name == "StandingStone") >= 4);

        var sorcerer = Sorcerer(sim);
        Assert.Equal(Faction.Stranger, w.Get<Creature>(sorcerer).Faction);
        var home = w.Get<StoryNpc>(sorcerer).Home;
        sim.TickMany(SimClock.TicksPerHour * 6);
        Assert.False(w.Get<Creature>(sorcerer).Dead);
        Assert.True(Maps.Map.Distance(home, w.Get<Position>(sorcerer).Cell) <= 4, "he keeps to his circle until called");
    }

    [Fact]
    public void HoldingEnoughMoonpetalBringsTheSorcererAndHisBargainSendsYouFlying()
    {
        var sim = NewSim();
        var me = AdventureStart.Setup(sim, StartKind.Pact, Wren, "Normal");
        var w = sim.World;
        var sorcerer = Sorcerer(sim);

        // Pick a bloom the way a player does: F on a flower, then pick up what it drops.
        var flower = w.Query<Thing>().First(e => w.Get<Thing>(e).Def.Name == "MoonpetalFlower");
        var at = w.Get<Thing>(flower).Cell;
        w.Get<Position>(me) = Position.At(sim.SpiralFrom(at, 2).First(c => c != at && sim.Map.Passable(c) && Pathfinder.Touches(c, at)));
        Assert.Equal(AvatarActionKind.Harvest, AvatarSystem.ActionAt(sim, me, at).Kind);
        sim.Enqueue(new AvatarWorkCommand(me, true, at));
        for (var t = 0; t < 600 && w.IsAlive(flower) && w.Get<Thing>(flower).Spawned; t++)
            sim.Tick();
        sim.Enqueue(new AvatarWorkCommand(me, false));
        sim.Tick();
        Assert.False(w.IsAlive(flower) && w.Get<Thing>(flower).Spawned, "the flower is picked");

        // The rest, straight into the pack.
        sim.Store(sim.PackOf(me), sim.CreateItem(sim.Thing("Moonpetal"), EnchantedForest.HerbsNeeded));
        sim.TickMany(SimClock.TicksPerSecond * 2);
        Assert.Equal(QuestStage.Confronted, w.Get<Quest>(me).Stage);

        // He comes to you.
        var before = Maps.Map.Distance(w.Get<Position>(sorcerer).Cell, w.Get<Position>(me).Cell);
        for (var t = 0; t < SimClock.TicksPerHour && !Pathfinder.Touches(w.Get<Position>(sorcerer).Cell, w.Get<Position>(me).Cell); t++)
            sim.Tick();
        var him = w.Get<Position>(sorcerer).Cell;
        Assert.True(Pathfinder.Touches(him, w.Get<Position>(me).Cell), $"he should walk up to you (was {before} cells off)");
        Assert.Equal(AvatarActionKind.Talk, AvatarSystem.ActionAt(sim, me, him).Kind);
        Assert.NotNull(Prologue.DialogueOf(sim, sorcerer));

        sim.Enqueue(new AvatarWorkCommand(me, true, him));
        sim.Tick();
        Assert.Equal(QuestStage.TakeWing, w.Get<Quest>(me).Stage);
        Assert.Equal(0, EnchantedForest.HerbsHeld(sim, me)); // he took his moonpetal

        // Mount up, and fly to the tower's site.
        var dragon = Companions.DragonOf(sim, me);
        RidingSystem.MountUp(sim, me, dragon);
        Assert.Null(Departure.CantFlyOff(sim, me));
        var party = Travel.Pack(sim, me, Entity.Null, Travel.FlightHours);
        Assert.Equal(StartKind.Pact, party.Start);

        var site = NewSim(42);
        var arrived = Travel.Arrive(site, party);
        Assert.False(Companions.DragonOf(site, arrived).IsNull);
        Assert.True(TowerPact.Active(site));
        Assert.Contains(site.World.Query<Thing>(), e => site.World.Get<Thing>(e).Def.Name == "LeyStone");
        Assert.Equal("Normal", site.Story.PendingDifficulty);
        Assert.Null(site.Storyteller.Difficulty);
        site.TickMany(SimClock.TicksPerHour);
    }

    [Fact]
    public void MoonpetalGlowsInFivePatchesOfThreeEvenOnTheGamesBigMap()
    {
        var sim = Simulation.CreateStandard(LoadDefs(), 160, 160, 1234, Maps.MapGenerator.RecommendedDepth);
        AdventureStart.Setup(sim, StartKind.Pact, Wren, "Normal");
        var w = sim.World;
        var flowers = w.Query<Thing>().Where(e => w.Get<Thing>(e).Def.Name == "MoonpetalFlower").ToList();
        Assert.Equal(EnchantedForest.Patches * EnchantedForest.BloomsPerPatch, flowers.Count);
        Assert.True(w.Get<Thing>(flowers[0]).Def.Glow);
        // Each bloom stands in a clearing: no tree right next to it.
        foreach (var f in flowers)
        {
            var at = w.Get<Thing>(f).Cell;
            foreach (var c in sim.SpiralFrom(at, 1))
                if (sim.Map.Structure[sim.Map.Idx(c)] is { IsNull: false } s)
                    Assert.NotEqual(Defs.ThingCategory.Resource, w.Get<Thing>(s).Def.Category);
        }
    }

    [Fact]
    public void RideCallsTheDragonDownAndYouClimbOn()
    {
        var sim = NewSim();
        var me = AdventureStart.Setup(sim, StartKind.Pact, Wren, "Normal");
        var w = sim.World;
        var dragon = Companions.DragonOf(sim, me);
        // Off somewhere in the sky when you call.
        w.Get<Position>(dragon) = Position.At(w.Get<Position>(me).Cell + new int3(12, 6, 4));
        sim.ApplyNow(new MountOrderCommand(me, dragon));
        Assert.IsType<AwaitRiderJob>(w.Get<Mind>(dragon).Job);
        for (var t = 0; t < SimClock.TicksPerSecond * 40 && !w.Has<Riding>(me); t++)
            sim.Tick();
        Assert.True(w.Has<Riding>(me), "the dragon should come down and let you on");
        Assert.Equal(dragon, w.Get<Riding>(me).Mount);
    }

    [Fact]
    public void ARiderCanAskToFlyEvenAShortWay()
    {
        var sim = NewSim();
        var me = AdventureStart.Setup(sim, StartKind.Pact, Wren, "Normal");
        var w = sim.World;
        var dragon = Companions.DragonOf(sim, me);
        AiSystem.EndJob(sim, dragon, w.Get<Mind>(dragon), false);
        var here = w.Get<Position>(me).Cell;
        w.Get<Position>(dragon) = Position.At(here);
        RidingSystem.MountUp(sim, me, dragon);
        var there = sim.SpiralFrom(here + new int3(6, 0, 0), 3).First(c => sim.Map.Passable(c) && sim.Regions.CanReach(here, c, PathEnd.OnTile));

        sim.ApplyNow(new MoveOrderCommand(me, there));
        Assert.DoesNotContain(w.Get<Mover>(dragon).Path!, c => Combat.TacticalMoves.Airborne(sim, c)); // a short way: it walks

        sim.ApplyNow(new MoveOrderCommand(me, there, Fly: true));
        var path = w.Get<Mover>(dragon).Path!;
        Assert.Contains(path, c => Combat.TacticalMoves.Airborne(sim, c)); // up it goes
        Assert.Equal(there, path[^1]);
        for (var t = 0; t < SimClock.TicksPerSecond * 30 && w.Get<Mover>(dragon).Moving; t++)
            sim.Tick();
        Assert.Equal(there, w.Get<Position>(dragon).Cell); // and lands there, rider and all
        Assert.True(w.Has<Riding>(me));
    }

    [Fact]
    public void YourDragonBreathesFireWhereYouPointFromTheSaddle()
    {
        var sim = NewSim();
        var me = AdventureStart.Setup(sim, StartKind.Pact, Wren, "Normal");
        var w = sim.World;
        var dragon = Companions.DragonOf(sim, me);
        AiSystem.EndJob(sim, dragon, w.Get<Mind>(dragon), false);
        var here = w.Get<Position>(me).Cell;
        w.Get<Position>(dragon) = Position.At(here);
        RidingSystem.MountUp(sim, me, dragon);
        var reach = w.Get<Creature>(dragon).Stage(sim.Clock.Tick).BreathRange;
        Assert.True(reach > 0);
        var target = sim.SpiralFrom(here + new int3(3, 0, 0), 2).First(c => c != here && sim.Map.Passable(c));

        sim.ApplyNow(new BreathOrderCommand(dragon, target));
        Assert.IsType<BreatheFireJob>(w.Get<Mind>(dragon).Job); // ridden (so drafted), it still does what it's told
        for (var t = 0; t < SimClock.TicksPerSecond * 5 && sim.RecentBreaths.Count == 0; t++)
            sim.Tick();
        Assert.Contains(sim.RecentBreaths, b => b.Source == dragon);
        Assert.True(w.Has<Riding>(me), "still in the saddle");
    }

    [Fact]
    public void FOnYourDragonBesideYouTendsItAndRidingIsAnAction()
    {
        // F at your dragon opens its care window (docs/plans/dragon-interactions.md); climbing on
        // is still the Ride action (R, the toolbar, the window's button).
        var sim = NewSim();
        var me = AdventureStart.Setup(sim, StartKind.Pact, Wren, "Normal");
        var w = sim.World;
        var dragon = Companions.DragonOf(sim, me);
        var here = w.Get<Position>(me).Cell;
        var beside = sim.SpiralFrom(here, 2).First(c => c != here && sim.Map.Passable(c) && Pathfinder.Touches(c, here));
        AiSystem.EndJob(sim, dragon, w.Get<Mind>(dragon), false);
        w.Get<Position>(dragon) = Position.At(beside);
        Bonding.Care(sim, dragon).Want = Defs.DragonWant.None;
        var action = AvatarSystem.ActionAt(sim, me, beside);
        Assert.Equal(AvatarActionKind.TendDragon, action.Kind);
        Assert.True(action.OpensWindow);
        Assert.True(RidingSystem.CanClimbOn(sim, me, dragon), "the partner starts trusting enough to ride");
        AvatarSystem.Perform(sim, me, new AvatarAction(AvatarActionKind.Ride, "", beside, dragon));
        Assert.True(w.Has<Riding>(me));
    }

    [Fact]
    public void TheDialoguesAreInTheDefs()
    {
        var defs = LoadDefs();
        Assert.NotEmpty(defs.Dialogue(Prologue.HandoverDialogue).Lines);
        Assert.NotEmpty(defs.Dialogue(EnchantedForest.BargainDialogue).Lines);
        Assert.Equal(4, defs.Tower(TowerPact.DefaultTower).ContainsDefs.Count);
    }
}

/// <summary>Co-op is the Pact at the tower's site: newcomers fly in on dragons of their own.</summary>
public class PactCoopTests
{
    [Fact]
    public void ANewcomerToAPactVillageArrivesWithAGrownDragonNotAnEgg()
    {
        var sim = Simulation.CreateStandard(LoadDefs(), 96, 96, 51, Maps.MapGenerator.RecommendedDepth);
        var host = AdventureStart.PactSite(sim, new Hero("Arin", null, null, "Vesper", null));
        Assert.True(TowerPact.Active(sim));
        var bea = Guid.NewGuid();
        var teal = new DragonLook { Scales = 0x2E6A4A, Belly = 0xE8C070, Wings = 0x24503A, Horns = 0x3A3436, Eyes = 0x7AE0FF };

        sim.ApplyNow(new CreateCharacterCommand(bea, "Bea", default, [], "Kestrel", teal));
        var me = Players.CharacterOf(sim, bea);
        var w = sim.World;
        Assert.False(me.IsNull);
        Assert.True(w.Get<Carrying>(me).Item.IsNull, "no egg in a Pact village");
        var dragon = Companions.DragonOf(sim, me);
        Assert.Equal("Kestrel", w.Get<Creature>(dragon).Name);
        Assert.Equal(teal, w.Get<DragonLook>(dragon));
        Assert.NotEqual(dragon, Companions.DragonOf(sim, host));

        // A command from an older client (no dragon picked) still gets one, with a derived look.
        var cat = Guid.NewGuid();
        sim.ApplyNow(new CreateCharacterCommand(cat, "Cat", default, []));
        var catsDragon = Companions.DragonOf(sim, Players.CharacterOf(sim, cat));
        Assert.Equal(Companions.DefaultName, w.Get<Creature>(catsDragon).Name);
        Assert.False(w.Has<DragonLook>(catsDragon));
    }

    [Fact]
    public void TheDragonPartsOfTheCommandSurviveTheWire()
    {
        var look = new DragonLook { Element = DragonElement.Lightning, Scales = 1, Belly = 2, Wings = 3, Horns = 4, Eyes = 5 };
        var command = new CreateCharacterCommand(Guid.NewGuid(), "Bea", default, ["Kind"], "Kestrel", look);
        var round = (CreateCharacterCommand)CommandCodec.Decode(CommandCodec.Encode(command, LoadDefs()), LoadDefs());
        Assert.Equal("Kestrel", round.DragonName);
        Assert.Equal(look, round.DragonLook);
    }
}
