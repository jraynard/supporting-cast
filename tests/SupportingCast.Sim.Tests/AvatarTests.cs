using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

/// <summary>Adventure mode: the player's own character, driven by commands instead of a brain.</summary>
public class AvatarTests
{
    private static Simulation Warm(int depth = Simulation.DefaultDepth)
    {
        var sim = TestWorld.Create(depth: depth);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Avatar(Simulation sim, int3 at)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Bearer", at, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        sim.SetSkills(e);
        sim.World.Add(e, new Avatar());
        return e;
    }

    private static int3 Cell(Simulation sim, Entity e) => sim.World.Get<Position>(e).Cell;

    [Fact]
    public void BrainNeverPicksAJobForTheAvatar()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        // Plenty a villager would do: haul loose wood to a stockpile, chop a marked tree.
        sim.SpawnItem(sim.Thing("Wood"), 20, C(8, 8));
        sim.Map.SetZone(sim.Map.Idx(C(12, 12)), 1);
        var tree = sim.SpawnStructure(sim.Thing("Pine"), C(10, 5));
        sim.Map.Designate(sim.Map.Idx(C(10, 5)), Maps.Designation.Chop);

        sim.TickMany(SimClock.TicksPerHour);

        Assert.Null(sim.World.Get<Mind>(me).Job);
        Assert.Equal(C(5, 5), Cell(sim, me));
        Assert.True(sim.World.IsAlive(tree));
    }

    [Fact]
    public void HeldDirectionWalksStepByStepAndStopsOnRelease()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        sim.Enqueue(new AvatarMoveCommand(me, new int2(1, 0)));
        sim.TickMany(SimClock.TicksPerSecond * 3);
        var moved = Cell(sim, me);
        Assert.Equal(5, moved.Y);
        Assert.True(moved.X >= 9, $"walked east, at {moved}");

        sim.Enqueue(new AvatarMoveCommand(me, default));
        sim.TickMany(SimClock.TicksPerSecond); // finishes the step it was on
        var stopped = Cell(sim, me);
        sim.TickMany(SimClock.TicksPerSecond * 2);
        Assert.Equal(stopped, Cell(sim, me));
        Assert.Equal(new int2(1, 0), sim.World.Get<Avatar>(me).Facing);
    }

    [Fact]
    public void ALateStopLandsOnTheCellNamed()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        sim.Enqueue(new AvatarMoveCommand(me, new int2(1, 0)));
        sim.TickMany(SimClock.TicksPerSecond / 2);
        // A co-op client let go two cells ahead of where the sim has got to (it predicts its walk).
        var target = Cell(sim, me) + new int3(2, 0, 0);
        sim.Enqueue(new AvatarMoveCommand(me, default, target));
        sim.TickMany(SimClock.TicksPerSecond * 4);
        Assert.Equal(target, Cell(sim, me));
        Assert.False(sim.World.Get<Avatar>(me).HasPending);
    }

    [Fact]
    public void ATurnWaitsForItsCell()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        sim.Enqueue(new AvatarMoveCommand(me, new int2(1, 0)));
        sim.TickMany(2);
        var corner = Cell(sim, me) + new int3(3, 0, 0);
        sim.Enqueue(new AvatarMoveCommand(me, new int2(0, 1), corner));
        sim.TickMany(SimClock.TicksPerSecond * 4);
        var at = Cell(sim, me);
        Assert.Equal(corner.X, at.X); // turned north exactly at the corner
        Assert.True(at.Y > corner.Y + 2, $"walked north from the corner, at {at}");
    }

    [Fact]
    public void AStopAlreadyPassedAppliesAtOnce()
    {
        var sim = Warm();
        var me = Avatar(sim, C(10, 5));
        sim.Enqueue(new AvatarMoveCommand(me, new int2(1, 0)));
        sim.TickMany(SimClock.TicksPerSecond);
        var behind = Cell(sim, me) - new int3(2, 0, 0);
        sim.Enqueue(new AvatarMoveCommand(me, default, behind));
        sim.TickMany(SimClock.TicksPerSecond);
        var stopped = Cell(sim, me);
        sim.TickMany(SimClock.TicksPerSecond * 2);
        Assert.Equal(stopped, Cell(sim, me)); // stopped (a step on at most), not walking on forever
        Assert.True(stopped.X - behind.X <= 4, $"stopped near the named cell, at {stopped}");
    }

    [Fact]
    public void AScheduledTurnSurvivesASave()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        sim.Enqueue(new AvatarMoveCommand(me, new int2(1, 0)));
        sim.TickMany(2);
        sim.Enqueue(new AvatarMoveCommand(me, default, Cell(sim, me) + new int3(3, 0, 0)));
        sim.Tick();
        Assert.True(sim.World.Get<Avatar>(me).HasPending);
        AiSystem.InterruptAll(sim);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(sim.World.Get<Avatar>(me).PendingAt, loaded.World.Get<Avatar>(me).PendingAt);
        sim.TickMany(SimClock.TicksPerSecond * 3);
        loaded.TickMany(SimClock.TicksPerSecond * 3);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
    }

    [Fact]
    public void WalkingDiagonallyIntoAWallSlidesAlongIt()
    {
        var sim = Warm();
        sim.FillBlocks(new int3(0, 8, 1), new int3(39, 8, 1)); // a wall along y = 8
        var me = Avatar(sim, C(5, 7));
        sim.Enqueue(new AvatarMoveCommand(me, new int2(1, 1)));
        sim.TickMany(SimClock.TicksPerSecond * 2);
        var at = Cell(sim, me);
        Assert.Equal(7, at.Y);
        Assert.True(at.X > 6, $"slid east along the wall, at {at}");
    }

    [Fact]
    public void ChoppingOnlyProgressesWhileTheWorkKeyIsHeld()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        var tree = sim.SpawnStructure(sim.Thing("Pine"), C(6, 5));
        var action = AvatarSystem.ActionAt(sim, me, C(6, 5));
        Assert.Equal(AvatarActionKind.Harvest, action.Kind);
        Assert.StartsWith("Chop", action.Label);

        // Tap the key: the job starts, but letting go stalls it.
        sim.Enqueue(new AvatarWorkCommand(me, true, C(6, 5)));
        sim.Tick();
        sim.Enqueue(new AvatarWorkCommand(me, false));
        sim.TickMany(SimClock.TicksPerHour);
        Assert.True(sim.World.IsAlive(tree));
        Assert.IsType<HarvestJob>(sim.World.Get<Mind>(me).Job);

        // Hold it and the tree comes down.
        sim.Enqueue(new AvatarWorkCommand(me, true));
        sim.TickMany(SimClock.TicksPerSecond * 30);
        Assert.False(sim.World.IsAlive(tree));
        Assert.Null(sim.World.Get<Mind>(me).Job);
        Assert.Contains(sim.SpawnedItems(), e => sim.World.Get<Thing>(e).Def.Name == "Wood");
    }

    [Fact]
    public void WalkingAwayAbandonsTheWork()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        sim.SpawnStructure(sim.Thing("Pine"), C(6, 5));
        sim.Enqueue(new AvatarWorkCommand(me, true, C(6, 5)));
        sim.TickMany(30);
        sim.Enqueue(new AvatarMoveCommand(me, new int2(-1, 0)));
        sim.Tick();
        Assert.Null(sim.World.Get<Mind>(me).Job);
    }

    [Fact]
    public void OutOfReachDoesNothing()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        sim.SpawnStructure(sim.Thing("Pine"), C(9, 5));
        Assert.Equal(AvatarActionKind.None, AvatarSystem.ActionAt(sim, me, C(9, 5)).Kind);
    }

    [Fact]
    public void CarriesTheEggAcrossJobsAndPutsItDown()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        sim.SpawnItem(sim.Thing("DragonEgg"), 1, C(5, 6));
        sim.SpawnStructure(sim.Thing("Pine"), C(4, 5));

        sim.Enqueue(new AvatarWorkCommand(me, true, C(5, 6)));
        sim.Tick();
        var egg = sim.World.Get<Carrying>(me).Item;
        Assert.False(egg.IsNull);
        Assert.True(sim.World.Has<Egg>(egg));

        // Chop a tree with the egg in hand; finishing (or abandoning) the job keeps it there.
        sim.Enqueue(new AvatarWorkCommand(me, true, C(4, 5)));
        sim.TickMany(SimClock.TicksPerSecond * 30);
        Assert.Null(sim.World.Get<Mind>(me).Job);
        Assert.Equal(egg, sim.World.Get<Carrying>(me).Item);

        // Walk a few steps; the egg's location follows.
        sim.Enqueue(new AvatarMoveCommand(me, new int2(0, 1)));
        sim.TickMany(SimClock.TicksPerSecond * 2);
        sim.Enqueue(new AvatarMoveCommand(me, default));
        sim.TickMany(SimClock.TicksPerSecond);
        Assert.Equal(Cell(sim, me), sim.LocationOf(egg));

        var drop = Cell(sim, me) + new int3(1, 0, 0);
        // F never drops what you carry; X does.
        Assert.NotEqual(AvatarActionKind.PutDown, AvatarSystem.ActionAt(sim, me, drop).Kind);
        Assert.Equal(AvatarActionKind.PutDown, AvatarSystem.ActionAt(sim, me, drop, AvatarTool.Drop).Kind);
        sim.Enqueue(new AvatarDropCommand(me, drop));
        sim.Tick();
        Assert.True(sim.World.Get<Carrying>(me).Item.IsNull);
        Assert.Equal(egg, sim.Map.Item[sim.Map.Idx(drop)]);
    }

    [Fact]
    public void DeliversToABlueprintTwoCellsOff()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        var site = sim.PlaceBlueprint(sim.Thing("WoodWall"), C(7, 5));
        var far = sim.PlaceBlueprint(sim.Thing("WoodWall"), C(8, 5));
        sim.SpawnItem(sim.Thing("Wood"), 20, C(5, 5));
        sim.Enqueue(new AvatarWorkCommand(me, true, C(5, 5)));
        sim.Tick();
        Assert.False(sim.World.Get<Carrying>(me).Item.IsNull);

        Assert.Equal(AvatarActionKind.Deliver, AvatarSystem.ActionAt(sim, me, C(7, 5)).Kind);
        Assert.Equal(AvatarActionKind.None, AvatarSystem.ActionAt(sim, me, C(8, 5)).Kind); // three off: too far
        sim.Enqueue(new AvatarWorkCommand(me, true, C(7, 5)));
        sim.Tick();
        Assert.True(sim.World.Get<Construction>(site).MaterialsComplete);
        Assert.False(sim.World.Get<Construction>(far).MaterialsComplete);
    }

    [Fact]
    public void NothingIsPutDownOnAWallBlueprintNorWalledIn()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        var wood = sim.CreateItem(sim.Thing("Wood"), 10);
        sim.World.Get<Carrying>(me).Item = wood;
        var site = sim.PlaceBlueprint(sim.Thing("WoodWall"), C(6, 5));
        Assert.False(sim.CanAcceptItem(C(6, 5), sim.Thing("Wood"), out _));
        Assert.Equal(AvatarActionKind.None, AvatarSystem.ActionAt(sim, me, C(6, 5), AvatarTool.Drop).Kind);

        // A blueprint laid over an item lying there: building the wall pushes it aside.
        sim.SpawnItem(sim.Thing("Stone"), 5, C(9, 9));
        var stone = sim.Map.Item[sim.Map.Idx(C(9, 9))];
        var over = sim.PlaceBlueprint(sim.Thing("WoodWall"), C(9, 9));
        var c = sim.World.Get<Construction>(over);
        for (var k = 0; k < c.Delivered.Length; k++)
            c.Delivered[k] = c.Target.Building!.Cost[k].Count;
        sim.CompleteConstruction(over);
        Assert.True(sim.Map.Item[sim.Map.Idx(C(9, 9))].IsNull);
        Assert.True(sim.World.Get<Thing>(stone).Spawned);
        Assert.NotEqual(C(9, 9), sim.World.Get<Thing>(stone).Cell);
        Assert.True(sim.World.Has<Construction>(site));
    }

    [Fact]
    public void FeedsAndRelightsTheFireFromYourHands()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        var fire = sim.SpawnStructure(sim.Thing("Campfire"), C(6, 5));
        var capacity = sim.Thing("Campfire").Building!.Fuel!.Capacity;
        sim.World.Get<Fueled>(fire).Fuel = 0; // burned out
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("Wood"), capacity + 3);

        var action = AvatarSystem.ActionAt(sim, me, C(6, 5));
        Assert.Equal(AvatarActionKind.Refuel, action.Kind);
        Assert.StartsWith("Relight", action.Label);
        sim.Enqueue(new AvatarWorkCommand(me, true, C(6, 5)));
        sim.Tick();
        Assert.Equal(capacity, sim.World.Get<Fueled>(fire).Fuel, 3);
        Assert.Equal(3, sim.World.Get<Stack>(sim.World.Get<Carrying>(me).Item).Count); // the rest stays in hand
        Assert.NotEqual(AvatarActionKind.Refuel, AvatarSystem.ActionAt(sim, me, C(6, 5)).Kind); // full now
    }

    [Fact]
    public void DeliversHeldMaterialAndBuilds()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        sim.SpawnItem(sim.Thing("Wood"), 20, C(5, 6));
        var site = sim.PlaceBlueprint(sim.Thing("WoodWall"), C(6, 5));

        sim.Enqueue(new AvatarWorkCommand(me, true, C(5, 6))); // pick up the wood
        sim.Tick();
        Assert.Equal(AvatarActionKind.Deliver, AvatarSystem.ActionAt(sim, me, C(6, 5)).Kind);
        sim.Enqueue(new AvatarWorkCommand(me, true, C(6, 5)));
        sim.Tick();
        var held = sim.World.Get<Carrying>(me).Item;
        Assert.Equal(15, sim.World.Get<Stack>(held).Count);
        Assert.True(sim.World.Get<Construction>(site).MaterialsComplete);

        // Put the rest down (X), then build with the key held.
        sim.Enqueue(new AvatarDropCommand(me, C(4, 5)));
        sim.Tick();
        Assert.Equal(AvatarActionKind.Build, AvatarSystem.ActionAt(sim, me, C(6, 5)).Kind);
        sim.Enqueue(new AvatarWorkCommand(me, true, C(6, 5)));
        sim.TickMany(SimClock.TicksPerSecond * 30);
        var wall = sim.Map.Structure[sim.Map.Idx(C(6, 5))];
        Assert.False(sim.World.Has<Construction>(wall));
        Assert.Equal("WoodWall", sim.World.Get<Thing>(wall).Def.Name);
    }

    [Fact]
    public void EatsFoodFromItsHands()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        var food = sim.Defs.Need("Food");
        sim.World.Get<Needs>(me).Values[food.Index] = 0.3f;
        sim.SpawnItem(sim.Thing("Berries"), 10, C(5, 6));
        sim.Enqueue(new AvatarWorkCommand(me, true, C(5, 6)));
        sim.Tick();
        var berries = sim.World.Get<Carrying>(me).Item;
        // F never eats: it's picked from the item itself.
        Assert.NotEqual(AvatarActionKind.Eat, AvatarSystem.ActionAt(sim, me, C(5, 5)).Kind);
        sim.Enqueue(new EatCommand(me, berries));
        sim.TickMany(120);
        Assert.True(sim.World.Get<Needs>(me).Get(food) > 0.8f);
    }

    [Fact]
    public void EatsFoodLyingInReachButNotFarAway()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        var food = sim.Defs.Need("Food");
        sim.World.Get<Needs>(me).Values[food.Index] = 0.3f;
        sim.SpawnItem(sim.Thing("Berries"), 10, C(9, 5));
        var far = sim.Map.Item[sim.Map.Idx(C(9, 5))];
        Assert.False(AvatarSystem.CanEat(sim, me, far));
        sim.Enqueue(new EatCommand(me, far));
        sim.TickMany(120);
        Assert.Equal(0.3f, sim.World.Get<Needs>(me).Get(food), 2);

        sim.SpawnItem(sim.Thing("Berries"), 10, C(6, 5));
        var near = sim.Map.Item[sim.Map.Idx(C(6, 5))];
        Assert.True(AvatarSystem.CanEat(sim, me, near));
        sim.Enqueue(new EatCommand(me, near));
        sim.TickMany(120);
        Assert.True(sim.World.Get<Needs>(me).Get(food) > 0.8f);
        Assert.True(!sim.World.IsAlive(near) || sim.World.Get<Stack>(near).Count < 10);
    }

    [Fact]
    public void ClickingTheGroundWalksThereAndHoldingFollowsTheCursor()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        sim.Enqueue(new AvatarGoToCommand(me, C(12, 9)));
        sim.TickMany(20);
        var job = Assert.IsType<AvatarWalkJob>(sim.World.Get<Mind>(me).Job);
        // The cursor moves on while the button's held: same walk, new destination.
        sim.Enqueue(new AvatarGoToCommand(me, C(14, 3)));
        sim.Tick();
        Assert.Same(job, sim.World.Get<Mind>(me).Job);
        sim.TickMany(SimClock.TicksPerSecond * 15);
        Assert.Equal(C(14, 3), Cell(sim, me));
        Assert.Null(sim.World.Get<Mind>(me).Job);
    }

    [Fact]
    public void WalkingWithTheKeysCancelsAClickedWalk()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        sim.Enqueue(new AvatarGoToCommand(me, C(20, 5)));
        sim.TickMany(SimClock.TicksPerSecond);
        sim.Enqueue(new AvatarMoveCommand(me, new int2(0, 1)));
        sim.Tick();
        Assert.Null(sim.World.Get<Mind>(me).Job);
        sim.Enqueue(new AvatarMoveCommand(me, int2.Zero));
        sim.TickMany(SimClock.TicksPerSecond * 10);
        Assert.True(Cell(sim, me).X < 20, $"stopped short of the click, at {Cell(sim, me)}");
    }

    [Fact]
    public void ClickingATreeWalksOverAndChopsWhileTheButtonIsHeld()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        var tree = sim.SpawnStructure(sim.Thing("Pine"), C(12, 5));
        Assert.Equal(AvatarActionKind.Harvest, AvatarSystem.ActionAt(sim, me, C(12, 5), from: C(12, 5)).Kind);
        Assert.Equal(AvatarActionKind.None, AvatarSystem.ActionAt(sim, me, C(12, 5)).Kind);

        sim.Enqueue(new AvatarGoToCommand(me, C(12, 5), Interact: true));
        for (var t = 0; t < SimClock.TicksPerSecond * 10 && sim.World.Get<Mind>(me).Job is not HarvestJob; t++)
            sim.Tick();
        Assert.True(AvatarSystem.InReach(Cell(sim, me), C(12, 5)), $"walked up to the tree, at {Cell(sim, me)}");
        Assert.IsType<HarvestJob>(sim.World.Get<Mind>(me).Job);
        sim.TickMany(SimClock.TicksPerSecond * 30);
        Assert.False(sim.World.IsAlive(tree));

        // Let go on the way and it waits for the button (or F), like F does.
        var other = sim.SpawnStructure(sim.Thing("Pine"), C(16, 8));
        sim.Enqueue(new AvatarGoToCommand(me, C(16, 8), Interact: true));
        sim.Tick();
        sim.Enqueue(new AvatarWorkCommand(me, false));
        sim.TickMany(SimClock.TicksPerHour);
        Assert.True(sim.World.IsAlive(other));
        Assert.IsType<HarvestJob>(sim.World.Get<Mind>(me).Job);
    }

    [Fact]
    public void ClickingABedWalksOntoItToSleep()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        sim.World.Get<Needs>(me).Values[sim.Defs.Need("Rest").Index] = 0.2f;
        var bed = sim.SpawnStructure(sim.Thing("Bed"), C(11, 7));
        sim.Enqueue(new AvatarGoToCommand(me, C(11, 7), Interact: true));
        sim.TickMany(SimClock.TicksPerSecond * 10);
        Assert.Equal(C(11, 7), Cell(sim, me));
        Assert.IsType<SleepJob>(sim.World.Get<Mind>(me).Job);
        Assert.Equal(bed, sim.World.Get<BedOwner>(me).Bed);
    }

    [Fact]
    public void ClickedWalksAreDeterministic()
    {
        static ulong Play()
        {
            var sim = Warm();
            var me = Avatar(sim, C(5, 5));
            sim.SpawnStructure(sim.Thing("Pine"), C(12, 6));
            sim.Enqueue(new AvatarGoToCommand(me, C(9, 12)));
            sim.TickMany(30);
            sim.Enqueue(new AvatarGoToCommand(me, C(14, 10)));
            sim.TickMany(SimClock.TicksPerSecond * 8);
            sim.Enqueue(new AvatarGoToCommand(me, C(12, 6), Interact: true));
            sim.TickMany(SimClock.TicksPerHour);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void SameInputsGiveTheSameGame()
    {
        static ulong Play()
        {
            var sim = Warm();
            var me = Avatar(sim, C(5, 5));
            sim.SpawnStructure(sim.Thing("Pine"), C(12, 6));
            int2[] dirs = [new(1, 0), new(1, 1), new(0, 0)];
            foreach (var d in dirs)
            {
                sim.Enqueue(new AvatarMoveCommand(me, d));
                sim.TickMany(SimClock.TicksPerSecond * 2);
            }
            sim.Enqueue(new AvatarWorkCommand(me, true, Cell(sim, me) + new int3(1, 0, 0)));
            sim.TickMany(SimClock.TicksPerHour);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void AvatarAndItsHeldEggSurviveASave()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        sim.SpawnItem(sim.Thing("DragonEgg"), 1, C(5, 6));
        sim.Enqueue(new AvatarWorkCommand(me, true, C(5, 6)));
        sim.Enqueue(new AvatarMoveCommand(me, new int2(1, 0)));
        sim.Tick();

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var avatar = Assert.Single(loaded.World.Query<Avatar>());
        Assert.Equal(new int2(1, 0), loaded.World.Get<Avatar>(avatar).MoveDir);
        Assert.True(loaded.World.Get<Avatar>(avatar).WorkHeld);
        var egg = loaded.World.Get<Carrying>(avatar).Item;
        Assert.True(loaded.World.Has<Egg>(egg));
    }

    [Fact]
    public void InAFightCompanionsAreAiAndTheAvatarIsThePlayers()
    {
        var sim = Warm(depth: 6);
        var me = Avatar(sim, C(10, 10));
        var friend = sim.SpawnCreature(sim.Defs.Creature("Human"), "Friend", C(10, 12), ageDays: 25 * 365);
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Ember", C(11, 10), ageDays: 5);
        sim.World.Get<Dragon>(dragon).Keeper = me;
        sim.World.Get<Dragon>(dragon).Affinity = 0.2f;
        sim.SpawnCreature(sim.Defs.Creature("Human"), "Bandit", C(20, 10), Faction.Raider, ageDays: 25 * 365);
        sim.Combat.Start(sim);

        Assert.False(sim.Combat.AiControls(sim.Combat.Unit(me)!));
        Assert.True(sim.Combat.AiControls(sim.Combat.Unit(friend)!));
        Assert.True(sim.Combat.AiControls(sim.Combat.Unit(dragon)!), "a dragon that barely knows you won't take orders");

        sim.World.Get<Dragon>(dragon).Affinity = AvatarSystem.CommandDragonAffinity;
        Assert.True(Encounter.PlayerCommands(sim, dragon));
    }

    [Fact]
    public void WithoutAnAvatarThePlayerCommandsEveryone()
    {
        var sim = Warm(depth: 6);
        var a = sim.SpawnCreature(sim.Defs.Creature("Human"), "A", C(10, 10), ageDays: 25 * 365);
        sim.SpawnCreature(sim.Defs.Creature("Human"), "Bandit", C(20, 10), Faction.Raider, ageDays: 25 * 365);
        sim.Combat.Start(sim);
        Assert.False(sim.Combat.AiControls(sim.Combat.Unit(a)!));
    }
}

/// <summary>Adventure mode's separate dig key and newcomers moving into free beds.</summary>
public class AdventureSettlementTests
{
    private static Simulation Warm()
    {
        var sim = TestWorld.Create(depth: 4);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Avatar(Simulation sim, int3 at)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Bearer", at, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        sim.SetSkills(e);
        sim.World.Add(e, new Avatar());
        return e;
    }

    [Fact]
    public void DiggingIsOnlyOnTheDigKeyAndNeedsItHeld()
    {
        var sim = Warm();
        sim.FillBlocks(new int3(6, 0, 1), new int3(6, 39, 1)); // a ridge east of us
        var me = Avatar(sim, C(5, 5));
        Toolbelt.GiveStartingTools(sim, me); // rock wants a pick
        Assert.Equal(AvatarActionKind.None, AvatarSystem.ActionAt(sim, me, C(6, 5)).Kind);
        Assert.Equal(AvatarActionKind.Dig, AvatarSystem.ActionAt(sim, me, C(6, 5), AvatarTool.Dig).Kind);
        // The dig key ignores trees and items.
        sim.SpawnStructure(sim.Thing("Pine"), C(4, 5));
        Assert.Equal(AvatarActionKind.None, AvatarSystem.ActionAt(sim, me, C(4, 5), AvatarTool.Dig).Kind);

        sim.Enqueue(new AvatarWorkCommand(me, true, C(6, 5), AvatarTool.Dig));
        sim.Tick();
        sim.Enqueue(new AvatarWorkCommand(me, false));
        sim.TickMany(SimClock.TicksPerHour);
        Assert.True(sim.Map.IsSolid(sim.Map.Idx(C(6, 5))));

        sim.Enqueue(new AvatarWorkCommand(me, true));
        sim.TickMany(SimClock.TicksPerHour);
        Assert.False(sim.Map.IsSolid(sim.Map.Idx(C(6, 5))));
    }

    /// <summary>A small walled, roofed room with a bed in it.</summary>
    private static Entity House(Simulation sim)
    {
        sim.BuildWallRing(C(20, 20), C(25, 25));
        var bed = sim.SpawnStructure(sim.Thing("Bed"), C(22, 22));
        sim.RebuildDerivedIfNeeded();
        Assert.True(SettlerSystem.IsHomeBed(sim, bed), "the bed should be indoors");
        return bed;
    }

    private static List<Entity> Others(Simulation sim, Entity me) =>
        sim.Creatures().Where(e => e != me && sim.World.Get<Creature>(e).Def.Race == Defs.Race.Human).ToList();

    [Fact]
    public void AFreeBedIndoorsDrawsANewcomerWhoClaimsIt()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        var bed = House(sim);
        sim.TickMany(SimClock.HoursToTicks(SettlerSystem.MaxDelayHours + 1));

        var newcomer = Assert.Single(Others(sim, me));
        Assert.Equal(Faction.Village, sim.World.Get<Creature>(newcomer).Faction);
        Assert.Equal(bed, sim.World.Get<BedOwner>(newcomer).Bed);

        // Taken now: nobody else comes for it.
        sim.TickMany(SimClock.HoursToTicks(SettlerSystem.MaxDelayHours + 1));
        Assert.Single(Others(sim, me));
    }

    [Fact]
    public void YourOwnBedAndBedsOutdoorsDrawNobody()
    {
        var sim = Warm();
        var me = Avatar(sim, C(5, 5));
        var bed = House(sim);
        sim.World.Add(me, new BedOwner { Bed = bed });
        sim.SpawnStructure(sim.Thing("Bed"), C(10, 10)); // out in the open
        sim.TickMany(SimClock.HoursToTicks(SettlerSystem.MaxDelayHours + 1));
        Assert.Empty(Others(sim, me));
    }

    [Fact]
    public void NoNewcomersWithoutAnAvatar()
    {
        var sim = Warm();
        House(sim);
        sim.TickMany(SimClock.HoursToTicks(SettlerSystem.MaxDelayHours + 1));
        Assert.Empty(sim.Creatures());
    }
}

public class EggBearerStartTests
{
    [Fact]
    public void StartsAloneWithTheEggAndTheChosenLookWhichSurvivesASave()
    {
        var sim = Simulation.CreateStandard(TestWorld.LoadDefs(), 96, 96, 7, Maps.MapGenerator.RecommendedDepth);
        var center = Maps.MapGenerator.Generate(sim, new Maps.MapGenerator.Options { Villagers = 1 });
        sim.RebuildDerivedIfNeeded();
        Scenarios.SliceDemo.ApplyVillageDefaults(sim, center);
        var look = new Appearance { Body = BodyType.Female, Hair = HairStyle.Long, HairColor = 0xC8A050, EyeColor = 0x3A6EC8, SkinColor = 0x8C5A3A };
        var me = Scenarios.EggBearerStart.Setup(sim, "Wren", look);

        Assert.Single(sim.Creatures(), e => sim.World.Get<Creature>(e).Def.Race == Defs.Race.Human);
        Assert.True(sim.World.Has<Egg>(sim.World.Get<Carrying>(me).Item));

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var avatar = Assert.Single(loaded.World.Query<Avatar>());
        Assert.Equal("Wren", loaded.World.Get<Creature>(avatar).Name);
        Assert.Equal(look, loaded.World.Get<Appearance>(avatar));
    }
}

/// <summary>Adventure mode: asking companions to follow you or guard the egg.</summary>
public class CompanionRequestTests
{
    private static Simulation Warm()
    {
        var sim = TestWorld.Create(60, 40);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Person(Simulation sim, int3 at, string name)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), name, at, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        sim.SetSkills(e);
        return e;
    }

    private static int Dist(Simulation sim, Entity a, int3 b) => Maps.Map.Distance(sim.World.Get<Position>(a).Cell, b);

    [Fact]
    public void AFollowerKeepsUpWithTheAvatar()
    {
        var sim = Warm();
        var me = Person(sim, C(5, 5), "Bearer");
        sim.World.Add(me, new Avatar());
        var friend = Person(sim, C(8, 8), "Wren");
        sim.SpawnStructure(sim.Thing("Pine"), C(30, 30));
        sim.Map.Designate(sim.Map.Idx(C(30, 30)), Maps.Designation.Chop); // work they'd rather do

        sim.Enqueue(new RequestCommand(me, friend, RequestKind.Follow));
        sim.Enqueue(new AvatarMoveCommand(me, new int2(1, 0)));
        sim.TickMany(SimClock.TicksPerSecond * 6);
        sim.Enqueue(new AvatarMoveCommand(me, default));
        sim.TickMany(SimClock.TicksPerSecond * 8);

        Assert.True(Dist(sim, friend, sim.World.Get<Position>(me).Cell) <= 2);
        Assert.IsType<EscortJob>(sim.World.Get<Mind>(friend).Job);

        sim.Enqueue(new RequestCommand(me, friend, null));
        sim.Tick();
        Assert.False(sim.World.Has<Request>(friend));
    }

    [Fact]
    public void AGuardStaysByTheEggThenGuardsTheHatchling()
    {
        var sim = Warm();
        var me = Person(sim, C(5, 5), "Bearer");
        sim.World.Add(me, new Avatar());
        sim.World.Add(me, new Keeper { Dragon = Entity.Null });
        var friend = Person(sim, C(20, 20), "Wren");
        sim.SpawnItem(sim.Thing("DragonEgg"), 1, C(40, 10));
        var egg = sim.World.Query<Egg>().Single();

        sim.Enqueue(new RequestCommand(me, friend, RequestKind.GuardEgg));
        sim.TickMany(SimClock.TicksPerSecond * 20);
        Assert.True(Dist(sim, friend, C(40, 10)) <= GuardJob.Radius);
        Assert.Equal(egg, sim.World.Get<Request>(friend).Target);

        // It hatches: they watch over the dragon instead.
        sim.World.Get<Egg>(egg).Progress = 0.9999f;
        sim.Map.Temperature[sim.Map.Idx(C(40, 10))] = 40f;
        Array.Fill(sim.Map.Temperature, 40f);
        sim.TickMany(SimClock.TicksPerHour);
        var dragon = sim.World.Query<Dragon>().Single();
        sim.TickMany(SimClock.TicksPerSecond * 10);
        Assert.Equal(dragon, sim.World.Get<Request>(friend).Target);
    }

    [Fact]
    public void UrgentNeedsComeBeforeARequest()
    {
        var sim = Warm();
        var me = Person(sim, C(5, 5), "Bearer");
        sim.World.Add(me, new Avatar());
        var friend = Person(sim, C(8, 8), "Wren");
        sim.World.Get<Needs>(friend).Values[sim.Defs.Need("Food").Index] = 0.1f;
        sim.SpawnItem(sim.Thing("Berries"), 10, C(20, 8));
        sim.Enqueue(new RequestCommand(me, friend, RequestKind.Follow));
        sim.TickMany(SimClock.TicksPerSecond * 30);
        Assert.True(sim.World.Get<Needs>(friend).Get(sim.Defs.Need("Food")) > 0.3f, "they went and ate first");
    }

    [Fact]
    public void YouCantAskYourselfOrAStranger()
    {
        var sim = Warm();
        var me = Person(sim, C(5, 5), "Bearer");
        sim.World.Add(me, new Avatar());
        var bandit = sim.SpawnCreature(sim.Defs.Creature("Human"), "Mott", C(9, 9), Faction.Raider, ageDays: 9000);
        Assert.NotNull(Requests.CantAsk(sim, me, me));
        Assert.NotNull(Requests.CantAsk(sim, me, bandit));
    }
}

/// <summary>The wagon: a container that holds stacks off the map, usable by the avatar and companions.</summary>
public class WagonTests
{
    private static Simulation Warm()
    {
        var sim = TestWorld.Create();
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Wagon(Simulation sim, int3 at) => sim.SpawnStructure(sim.Thing("Wagon"), at);

    private static Entity Stash(Simulation sim, Entity wagon, string def, int count)
    {
        var item = sim.CreateItem(sim.Thing(def), count);
        Assert.True(sim.Store(wagon, item).IsNull);
        return sim.StoredIn(wagon).Last();
    }

    [Fact]
    public void AnEmptiedWagonComesApartForItsWood()
    {
        var sim = Warm();
        var wagon = Wagon(sim, C(10, 10));
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Bearer", C(9, 10), ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(me).Values, 1f);
        sim.World.Add(me, new Avatar());
        var berries = Stash(sim, wagon, "Berries", 5);

        // Loaded, it's still storage: F opens it, and nobody can take it apart.
        Assert.Equal(AvatarActionKind.OpenStorage, AvatarSystem.ActionAt(sim, me, C(10, 10)).Kind);
        Assert.False(sim.CanDeconstruct(wagon, out var why));
        Assert.Contains("emptied", why);

        sim.Enqueue(new TakeFromStorageCommand(me, berries));
        sim.Tick();
        sim.World.Get<Carrying>(me).Item = Entity.Null; // eaten, say
        // Empty, F still just opens it: it only comes apart once marked (the wagon window's button).
        Assert.Equal(AvatarActionKind.OpenStorage, AvatarSystem.ActionAt(sim, me, C(10, 10)).Kind);
        Assert.True(AvatarSystem.CanDismantle(sim, me, wagon));
        sim.Map.Designate(sim.Map.Idx(C(10, 10)), Maps.Designation.Deconstruct);
        Assert.Equal(AvatarActionKind.Dismantle, AvatarSystem.ActionAt(sim, me, C(10, 10)).Kind);

        // Hold F until it's done.
        sim.Enqueue(new AvatarWorkCommand(me, true, C(10, 10)));
        for (var t = 0; t < SimClock.TicksPerHour && sim.World.IsAlive(wagon); t++)
            sim.Tick();
        Assert.False(sim.World.IsAlive(wagon));
        var wood = sim.SpawnedItems().Where(e => sim.World.Get<Thing>(e).Def.Name == "Wood").ToList();
        Assert.Equal(30, wood.Sum(e => sim.World.Get<Stack>(e).Count));
        Assert.All(wood, e => Assert.Equal("Oak", sim.World.Get<Thing>(e).Material?.Name));
    }

    [Fact]
    public void AMisplacedBuildingComesApartOnceMarked()
    {
        var sim = Warm();
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Bearer", C(9, 10), ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(me).Values, 1f);
        sim.World.Add(me, new Avatar());
        var bed = sim.SpawnStructure(sim.Thing("Bed"), C(10, 10));
        sim.SpawnItem(sim.Thing("Wood"), 3, C(9, 11));
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("Wood"), 2); // hands full don't stop it

        // Unmarked, F never takes it apart.
        Assert.NotEqual(AvatarActionKind.Dismantle, AvatarSystem.ActionAt(sim, me, C(10, 10)).Kind);

        // The Deconstruct tool marks it (alone in Adventure mode too), then F does the work.
        sim.ApplyNow(new AreaOrderCommand(AreaTool.Deconstruct, new int2(10, 10), new int2(10, 10), 1, 1));
        var action = AvatarSystem.ActionAt(sim, me, C(10, 10));
        Assert.Equal(AvatarActionKind.Dismantle, action.Kind);
        Assert.Equal(bed, action.Target);

        sim.Enqueue(new AvatarWorkCommand(me, true, C(10, 10)));
        for (var t = 0; t < SimClock.TicksPerHour && sim.World.IsAlive(bed); t++)
            sim.Tick();
        Assert.False(sim.World.IsAlive(bed));
        // Half its cost back, like any deconstruction.
        var refund = sim.SpawnedItems().Where(e => sim.World.Get<Thing>(e).Cell == C(10, 10) && sim.World.Get<Thing>(e).Def.Name == "Wood");
        Assert.Equal(5, refund.Sum(e => sim.World.Get<Stack>(e).Count));
    }

    [Fact]
    public void StoresMergesAndRespectsItsSlots()
    {
        var sim = Warm();
        var wagon = Wagon(sim, C(10, 10));
        Stash(sim, wagon, "Berries", 10);
        Stash(sim, wagon, "Berries", 10); // tops up the same stack
        var only = Assert.Single(sim.StoredIn(wagon));
        Assert.Equal(20, sim.World.Get<Stack>(only).Count);
        Assert.False(sim.World.Get<Thing>(only).Spawned);
        Assert.Equal(C(10, 10), sim.LocationOf(only));

        for (var i = 1; i < 16; i++)
            Stash(sim, wagon, "Stone", 50);
        var extra = sim.CreateItem(sim.Thing("Wood"), 5);
        Assert.Equal(extra, sim.Store(wagon, extra)); // full: nothing taken
    }

    [Fact]
    public void AvatarStoresWhatItHoldsAndTakesItBack()
    {
        var sim = Warm();
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Bearer", C(5, 5), ageDays: 9000);
        Array.Fill(sim.World.Get<Needs>(me).Values, 1f);
        sim.World.Add(me, new Avatar());
        var wagon = Wagon(sim, C(6, 5));
        sim.SpawnItem(sim.Thing("Wood"), 20, C(5, 6));

        Assert.Equal(AvatarActionKind.OpenStorage, AvatarSystem.ActionAt(sim, me, C(6, 5)).Kind);
        sim.Enqueue(new AvatarWorkCommand(me, true, C(5, 6)));
        sim.Tick();
        Assert.Equal(AvatarActionKind.Store, AvatarSystem.ActionAt(sim, me, C(6, 5)).Kind);
        sim.Enqueue(new AvatarWorkCommand(me, true, C(6, 5)));
        sim.Tick();
        Assert.True(sim.World.Get<Carrying>(me).Item.IsNull);
        var wood = Assert.Single(sim.StoredIn(wagon));

        sim.Enqueue(new TakeFromStorageCommand(me, wood));
        sim.Tick();
        Assert.Equal(wood, sim.World.Get<Carrying>(me).Item);
        Assert.False(sim.World.Has<Stored>(wood));
        Assert.Empty(sim.StoredIn(wagon));
    }

    [Fact]
    public void CompanionsEatFromTheWagonButDontHaulOutOfIt()
    {
        var sim = Warm();
        var wagon = Wagon(sim, C(10, 10));
        Stash(sim, wagon, "Berries", 30);
        Stash(sim, wagon, "Wood", 40);
        for (var x = 20; x < 24; x++)
            sim.Map.SetZone(sim.Map.Idx(C(x, 20)), 1);
        var pal = sim.SpawnCreature(sim.Defs.Creature("Human"), "Wren", C(5, 5), ageDays: 9000);
        sim.World.Get<Needs>(pal).Values[sim.Defs.Need("Food").Index] = 0.1f;

        sim.TickMany(SimClock.TicksPerHour);
        Assert.True(sim.World.Get<Needs>(pal).Get(sim.Defs.Need("Food")) > 0.4f);
        var berries = sim.StoredIn(wagon).Single(e => sim.World.Get<Thing>(e).Def.Name == "Berries");
        Assert.True(sim.World.Get<Stack>(berries).Count < 30);
        Assert.Contains(sim.StoredIn(wagon), e => sim.World.Get<Thing>(e).Def.Name == "Wood" && sim.World.Get<Stack>(e).Count == 40);
    }

    [Fact]
    public void BuildersTakeMaterialFromTheWagon()
    {
        var sim = Warm();
        var wagon = Wagon(sim, C(10, 10));
        Stash(sim, wagon, "Wood", 40);
        var pal = sim.SpawnCreature(sim.Defs.Creature("Human"), "Wren", C(5, 5), ageDays: 9000);
        Array.Fill(sim.World.Get<Needs>(pal).Values, 1f);
        sim.PlaceBlueprint(sim.Thing("WoodWall"), C(14, 14));
        sim.TickMany(SimClock.TicksPerHour);
        var wall = sim.Map.Structure[sim.Map.Idx(C(14, 14))];
        Assert.False(sim.World.Has<Construction>(wall));
        Assert.Equal(35, sim.World.Get<Stack>(Assert.Single(sim.StoredIn(wagon))).Count);
    }

    [Fact]
    public void ADestroyedWagonSpillsItsLoad()
    {
        var sim = Warm();
        var wagon = Wagon(sim, C(10, 10));
        var meat = Stash(sim, wagon, "Meat", 20);
        sim.DestroyThing(wagon);
        Assert.True(sim.World.Get<Thing>(meat).Spawned);
        Assert.False(sim.World.Has<Stored>(meat));
    }

    [Fact]
    public void FoodInTheWagonStillRots()
    {
        var sim = Warm();
        var wagon = Wagon(sim, C(10, 10));
        var meat = Stash(sim, wagon, "Meat", 20);
        sim.TickMany(SimClock.TicksPerDay);
        Assert.True(sim.World.Get<Rot>(meat).Progress > 0.1f);
    }

    [Fact]
    public void TheEggBearerSetsOutWithTheKitInTheWagon()
    {
        var sim = Simulation.CreateStandard(TestWorld.LoadDefs(), 96, 96, 7, Maps.MapGenerator.RecommendedDepth);
        var center = Maps.MapGenerator.Generate(sim, new Maps.MapGenerator.Options { Villagers = 1 });
        sim.RebuildDerivedIfNeeded();
        var me = Scenarios.EggBearerStart.Setup(sim);
        var wagon = sim.World.Query<Thing>().Single(e => sim.World.Get<Thing>(e).Def.Name == "Wagon");
        var kit = sim.StoredIn(wagon).Select(e => sim.World.Get<Thing>(e).Def.Name).ToList();
        Assert.Contains("Berries", kit);
        Assert.Contains("Wood", kit);
        Assert.Contains("Meat", kit);
        Assert.True(sim.World.Has<Egg>(sim.World.Get<Carrying>(me).Item));
        Assert.DoesNotContain(sim.SpawnedItems(), e => Maps.Map.Distance(sim.World.Get<Thing>(e).Cell, center) < 6);

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var w2 = loaded.World.Query<Thing>().Single(e => loaded.World.Get<Thing>(e).Def.Name == "Wagon");
        Assert.Equal(kit.Count, loaded.StoredIn(w2).Count);
    }
}

/// <summary>Adventure mode's opening: the village, Orla's handover, and setting out for a new map.</summary>
public class PrologueTests
{
    private static Simulation NewSim(ulong seed = 11) =>
        Simulation.CreateStandard(TestWorld.LoadDefs(), 96, 96, seed, Maps.MapGenerator.RecommendedDepth);

    private static Entity Keeper(Simulation sim) => sim.World.Query<StoryKeeper>().Single();

    private static int3 NextTo(Simulation sim, int3 cell) =>
        sim.SpiralFrom(cell, 2).First(c => c != cell && sim.Map.Passable(c) && Ai.Pathfinder.Touches(c, cell));

    [Fact]
    public void TheVillageIsNotYoursToCommand()
    {
        var sim = NewSim();
        var me = Scenarios.Prologue.Setup(sim, "Wren", difficulty: "Normal");
        var villagers = sim.Creatures().Where(e => e != me && sim.World.Get<Creature>(e).Def.Race == Defs.Race.Human).ToList();
        Assert.Equal(5, villagers.Count);
        Assert.All(villagers, v => Assert.Equal(Faction.Ashford, sim.World.Get<Creature>(v).Faction));
        Assert.All(villagers, v => Assert.NotNull(Requests.CantAsk(sim, me, v)));
        Assert.Equal(QuestStage.MeetKeeper, sim.World.Get<Quest>(me).Stage);
        Assert.Contains("Speak with", Scenarios.Prologue.Objective(sim, me));

        // Village life goes on for a day: Orla stays abed, nobody new moves in, nobody dies.
        sim.TickMany(SimClock.TicksPerDay);
        Assert.Equal(sim.World.Get<Thing>(sim.World.Get<BedOwner>(Keeper(sim)).Bed).Cell, sim.World.Get<Position>(Keeper(sim)).Cell);
        Assert.Equal(6, sim.Creatures().Count(e => sim.World.Get<Creature>(e).Def.Race == Defs.Race.Human));
    }

    [Fact]
    public void OrlaEntrustsTheEggThenPassesAway()
    {
        var sim = NewSim();
        var me = Scenarios.Prologue.Setup(sim, "Wren");
        var keeper = Keeper(sim);
        var bed = sim.World.Get<Position>(keeper).Cell;
        sim.World.Get<Position>(me) = Position.At(NextTo(sim, bed));

        var talk = AvatarSystem.ActionAt(sim, me, bed);
        Assert.Equal(AvatarActionKind.Talk, talk.Kind);
        sim.Enqueue(new AvatarWorkCommand(me, true, bed));
        sim.Tick();

        Assert.Equal(QuestStage.Entrusted, sim.World.Get<Quest>(me).Stage);
        Assert.True(sim.World.Has<Egg>(sim.World.Get<Carrying>(me).Item));
        Assert.True(sim.World.Has<Keeper>(me));
        Assert.NotEqual(AvatarActionKind.Talk, AvatarSystem.ActionAt(sim, me, bed).Kind); // said all she had to say

        sim.TickMany(SimClock.HoursToTicks(Scenarios.Prologue.KeeperHours) + 120);
        Assert.True(sim.World.Get<Creature>(keeper).Dead);
    }

    [Fact]
    public void SettingOutCarriesYouTheEggAndTheWagonsLoadToANewMap()
    {
        var sim = NewSim();
        var look = new Appearance { Body = BodyType.Female, Hair = HairStyle.Short, HairColor = 0xA8402A, EyeColor = 0x4A8A4A, SkinColor = 0xD2A27A };
        var me = Scenarios.Prologue.Setup(sim, "Wren", look, traits: ["Hardy", "DragonFriend"]);
        var wagon = sim.World.Query<Thing>().Single(e => sim.World.Get<Thing>(e).Def.Name == "Wagon");
        Assert.False(Scenarios.Prologue.CanDepart(sim, me, wagon)); // not before Orla's said her piece

        var bed = sim.World.Get<Position>(Keeper(sim)).Cell;
        sim.World.Get<Position>(me) = Position.At(NextTo(sim, bed));
        sim.Enqueue(new AvatarWorkCommand(me, true, bed));
        sim.Tick();
        var wagonCell = sim.World.Get<Thing>(wagon).Cell;
        sim.World.Get<Position>(me) = Position.At(NextTo(sim, wagonCell));
        Assert.Equal(AvatarActionKind.Depart, AvatarSystem.ActionAt(sim, me, wagonCell).Kind);

        var cargo = sim.StoredIn(wagon).Sum(e => sim.World.Get<Stack>(e).Count);
        // Gear and the pack's contents come along too.
        sim.Equip(me, sim.Thing("Axe"));
        var cap = sim.Wear(me, sim.Thing("LeatherCap"));
        sim.World.Get<Thing>(cap).Hp = 20;
        Assert.False(sim.PackOf(me).IsNull, "the prologue starts you with a satchel");
        sim.Store(sim.PackOf(me), sim.CreateItem(sim.Thing("Berries"), 7));
        var party = Scenarios.Travel.Pack(sim, me, wagon);
        Assert.Equal("Wren", party.Name);
        Assert.True(party.EggProgress >= 0);

        var home = NewSim(12);
        var arrived = Scenarios.Travel.Arrive(home, party);
        Assert.Single(home.Creatures(), e => home.World.Get<Creature>(e).Def.Race == Defs.Race.Human);
        Assert.Equal("Wren", home.World.Get<Creature>(arrived).Name);
        Assert.Equal(look, home.World.Get<Appearance>(arrived));
        Assert.Equal(["Hardy", "DragonFriend"], home.World.Get<Traits>(arrived).List.Select(t => t.Name)); // who you are comes along
        Assert.True(home.World.Has<Avatar>(arrived));
        Assert.False(home.World.Has<Quest>(arrived)); // the prologue's over
        Assert.True(home.World.Has<Keeper>(arrived));
        Assert.True(home.World.Has<Egg>(home.World.Get<Carrying>(arrived).Item));
        var newWagon = home.World.Query<Thing>().Single(e => home.World.Get<Thing>(e).Def.Name == "Wagon");
        Assert.Equal(cargo, home.StoredIn(newWagon).Sum(e => home.World.Get<Stack>(e).Count));
        var worn = home.World.Get<Apparel>(arrived);
        Assert.Equal(20f, home.World.Get<Thing>(worn.Head).Hp);
        Assert.Equal("Axe", home.World.Get<Thing>(home.World.Get<Equipment>(arrived).Weapon).Def.Name);
        Assert.Equal(7, home.StoredIn(home.PackOf(arrived)).Sum(e => home.World.Get<Stack>(e).Count));
        Assert.True(home.Clock.Tick > sim.Clock.Tick + SimClock.HoursToTicks(Scenarios.Travel.RoadHours) - 1);
        Assert.InRange(home.Clock.HourOfDay, 7.9f, 8.1f);
    }

    [Fact]
    public void TheWagonHitchesFromAnywhereCloseButNotInThePrologueBeforeTheEgg()
    {
        var sim = NewSim();
        var me = Scenarios.Prologue.Setup(sim, "Wren");
        var wagon = sim.World.Query<Thing>().Single(e => sim.World.Get<Thing>(e).Def.Name == "Wagon");
        var wagonCell = sim.World.Get<Thing>(wagon).Cell;
        int3 At(int dist) => sim.SpiralFrom(wagonCell, dist + 1).First(c => sim.Map.Passable(c)
            && Math.Max(Math.Abs(c.X - wagonCell.X), Math.Abs(c.Y - wagonCell.Y)) == dist);

        sim.World.Get<Position>(me) = Position.At(At(2));
        Assert.NotEqual(AvatarActionKind.Depart, AvatarSystem.ActionAt(sim, me, At(2)).Kind); // no egg yet

        var bed = sim.World.Get<Position>(Keeper(sim)).Cell;
        sim.World.Get<Position>(me) = Position.At(NextTo(sim, bed));
        sim.Enqueue(new AvatarWorkCommand(me, true, bed));
        sim.Tick();

        // Two cells off, F on your own feet hitches it. It can't be taken apart while it's your way out.
        Assert.False(AvatarSystem.CanDismantle(sim, me, wagon));
        var near = At(Scenarios.Prologue.HitchReach);
        sim.World.Get<Position>(me) = Position.At(near);
        var action = AvatarSystem.ActionAt(sim, me, near);
        Assert.Equal(AvatarActionKind.Depart, action.Kind);
        Assert.Equal(wagon, action.Target);
        var far = At(Scenarios.Prologue.HitchReach + 2);
        sim.World.Get<Position>(me) = Position.At(far);
        Assert.NotEqual(AvatarActionKind.Depart, AvatarSystem.ActionAt(sim, me, far).Kind);
    }

    [Fact]
    public void APrologueSaveKeepsTheStory()
    {
        var sim = NewSim();
        var me = Scenarios.Prologue.Setup(sim, "Wren", difficulty: "Hard");
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var avatar = Assert.Single(loaded.World.Query<Avatar>());
        Assert.Equal(QuestStage.MeetKeeper, loaded.World.Get<Quest>(avatar).Stage);
        Assert.Equal("Hard", loaded.World.Get<Quest>(avatar).Difficulty);
        Assert.Single(loaded.World.Query<StoryKeeper>());
        Assert.Equal(Faction.Ashford, loaded.World.Get<Creature>(loaded.World.Query<StoryKeeper>().Single()).Faction);
    }

    [Fact]
    public void FBeatsOutAFireOnlyWhileHeld()
    {
        var sim = TestWorld.Create();
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Bearer", C(5, 5), ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(me).Values, 1f);
        sim.World.Add(me, new Avatar());
        var grass = C(6, 5);
        sim.Map.Fire[sim.Map.Idx(grass)] = 0.8f;
        var action = AvatarSystem.ActionAt(sim, me, grass);
        Assert.Equal(AvatarActionKind.Extinguish, action.Kind);

        sim.ApplyNow(new AvatarWorkCommand(me, true, grass));
        sim.ApplyNow(new AvatarWorkCommand(me, false));
        Assert.IsType<FightFireJob>(sim.World.Get<Mind>(me).Job);
        var before = sim.Map.Fire[sim.Map.Idx(grass)];
        sim.TickMany(30);
        Assert.True(sim.Map.Fire[sim.Map.Idx(grass)] >= before - 1e-4f, "nothing happens with the key up");

        sim.ApplyNow(new AvatarWorkCommand(me, true, grass));
        for (var t = 0; t < SimClock.TicksPerSecond * 10 && sim.Map.Fire[sim.Map.Idx(grass)] > 0; t++)
        {
            sim.Map.Fire[sim.Map.Idx(grass)] = Math.Min(sim.Map.Fire[sim.Map.Idx(grass)], before); // no fresh fuel for the test
            sim.Tick();
        }
        Assert.Equal(0f, sim.Map.Fire[sim.Map.Idx(grass)]);
    }

    private static Entity ChainAvatar(Simulation sim, int3 at)
    {
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Bearer", at, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(me).Values, 1f);
        sim.SetSkills(me);
        sim.World.Add(me, new Avatar());
        return me;
    }

    [Fact]
    public void HoldingFBuildsTheBlueprintsInReachOneAfterAnother()
    {
        var sim = TestWorld.Create();
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        var me = ChainAvatar(sim, C(10, 10));
        var pack = sim.GiveStartingPack(me);
        sim.Store(pack, sim.CreateItem(sim.Thing("Wood"), 15, sim.Defs.Material("Pine")));
        var sites = new[] { C(11, 9), C(11, 10), C(11, 11) }.Select(c => sim.PlaceBlueprint(sim.Thing("WoodWall"), c)).ToList();
        sim.RebuildDerivedIfNeeded();

        // One press on the first: deliver, then (still held) build it and carry on down the row.
        sim.ApplyNow(new AvatarWorkCommand(me, true, C(11, 9)));
        for (var t = 0; t < SimClock.TicksPerSecond * 30 && sites.Any(s => sim.World.IsAlive(s)); t++)
            sim.Tick();

        Assert.All(sites, s => Assert.False(sim.World.IsAlive(s)));
        foreach (var c in new[] { C(11, 9), C(11, 10), C(11, 11) })
            Assert.Equal("pine wall", sim.World.Get<Thing>(sim.Map.Structure[sim.Map.Idx(c)]).Label);
        sim.TickMany(5);
        Assert.Equal(AvatarActionKind.None, sim.World.Get<Avatar>(me).Chain); // nothing left in reach
    }

    [Fact]
    public void HoldingFBeatsOutAFireCellAfterCellUntilLetGo()
    {
        var sim = TestWorld.Create();
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        var me = ChainAvatar(sim, C(10, 10));
        var burning = new[] { C(11, 10), C(11, 11), C(10, 11) };
        void Fuel()
        {
            foreach (var c in burning)
                if (sim.Map.Fire[sim.Map.Idx(c)] > 0)
                    sim.Map.Fire[sim.Map.Idx(c)] = Math.Min(sim.Map.Fire[sim.Map.Idx(c)], 0.6f); // no spreading for the test
        }
        foreach (var c in burning)
            sim.Map.Fire[sim.Map.Idx(c)] = 0.6f;

        sim.ApplyNow(new AvatarWorkCommand(me, true, burning[0]));
        for (var t = 0; t < SimClock.TicksPerSecond * 4 && sim.Map.Fire[sim.Map.Idx(burning[0])] > 0; t++)
        {
            Fuel();
            sim.Tick();
        }
        Assert.Equal(0f, sim.Map.Fire[sim.Map.Idx(burning[0])]);
        for (var t = 0; t < 10; t++)
        {
            Fuel();
            sim.Tick();
        }
        Assert.Equal(AvatarActionKind.Extinguish, sim.World.Get<Avatar>(me).Chain); // on to the next flames

        // Letting go stops the run: what's still alight stays alight.
        sim.ApplyNow(new AvatarWorkCommand(me, false));
        Assert.Equal(AvatarActionKind.None, sim.World.Get<Avatar>(me).Chain);
        var left = burning.Count(c => sim.Map.Fire[sim.Map.Idx(c)] > 0);
        Assert.True(left >= 1);

        sim.ApplyNow(new AvatarWorkCommand(me, true, burning.First(c => sim.Map.Fire[sim.Map.Idx(c)] > 0)));
        for (var t = 0; t < SimClock.TicksPerSecond * 15 && burning.Any(c => sim.Map.Fire[sim.Map.Idx(c)] > 0); t++)
        {
            Fuel();
            sim.Tick();
        }
        Assert.All(burning, c => Assert.Equal(0f, sim.Map.Fire[sim.Map.Idx(c)]));
    }
}

/// <summary>Sleeping where you stand: "Sleep here", and F on your own cell when tired.</summary>
public class SleepHereTests
{
    [Fact]
    public void SleepHereLiesDownOnTheGroundAndWalkingWakesYou()
    {
        var sim = TestWorld.Create();
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Wren", C(10, 10), ageDays: 25 * 365);
        sim.World.Add(me, new Avatar());
        var rest = sim.Defs.Need("Rest");
        sim.World.Get<Needs>(me).Values[rest.Index] = 0.9f;
        Assert.Equal(AvatarActionKind.None, AvatarSystem.ActionAt(sim, me, C(10, 10)).Kind);
        sim.World.Get<Needs>(me).Values[rest.Index] = 0.2f;
        Assert.Equal(AvatarActionKind.None, AvatarSystem.ActionAt(sim, me, C(10, 10)).Kind); // even dead tired: F never sleeps on the ground
        Assert.Equal(AvatarActionKind.None, AvatarSystem.ActionAt(sim, me, C(12, 10), from: C(12, 10)).Kind); // nor does a click on a tile

        sim.Enqueue(new SleepCommand(me));
        sim.TickMany(60);
        Assert.True(sim.World.Get<Mind>(me).Asleep);
        Assert.Equal("Sleeping on the ground", sim.World.Get<Mind>(me).Job?.Report);

        sim.Enqueue(new AvatarMoveCommand(me, new int2(1, 0)));
        sim.TickMany(30);
        Assert.False(sim.World.Get<Mind>(me).Asleep);
    }
}

