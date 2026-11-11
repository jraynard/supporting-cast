using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Combat;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Core;
using DragonWorld.Sim.Saving;
using DragonWorld.Sim.Scenarios;
using DragonWorld.Sim.Systems;
using static DragonWorld.Sim.Tests.TestWorld;

namespace DragonWorld.Sim.Tests;

/// <summary>Escape's opening: the cage, the jailer's supper round, the break-out, the chains, and getting caught.</summary>
public class EscapeTests
{
    private static Simulation NewSim(ulong seed = 61) =>
        Simulation.CreateStandard(LoadDefs(), 96, 96, seed, Maps.MapGenerator.RecommendedDepth);

    private static readonly Hero Wren = new("Wren", null, null, "Ashwing", null);

    private static bool TickUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (var t = 0; t < maxTicks && !done(); t++)
            sim.Tick();
        return done();
    }

    /// <summary>The camp at supper time, the jailer in the cage beside you.</summary>
    private static (Simulation Sim, Entity Me, Entity Jailer) AtSupper(ulong seed = 61)
    {
        var sim = NewSim(seed);
        var me = AdventureStart.Setup(sim, StartKind.Escape, Wren, "Normal");
        Assert.True(TickUntil(sim, () => sim.World.Get<Quest>(me).Stage == QuestStage.Opportunity, SimClock.HoursToTicks(3)),
            $"the jailer should come at supper (stage {sim.World.Get<Quest>(me).Stage}, {sim.Clock.TimeString()})");
        return (sim, me, EscapeCamp.Jailer(sim));
    }

    [Fact]
    public void YouStartLockedUpWithYourDragonInChainsAndNoFight()
    {
        var sim = NewSim();
        var me = AdventureStart.Setup(sim, StartKind.Escape, Wren, "Normal");
        var w = sim.World;
        var camp = EscapeCamp.Of(sim)!;
        Assert.True(w.Has<Captive>(me));
        Assert.Equal(QuestStage.Captive, w.Get<Quest>(me).Stage);
        Assert.True(camp.InCage(w.Get<Position>(me).Cell));
        Assert.Equal("CageGate", w.Get<Thing>(EscapeCamp.Gate(sim)).Def.Name);
        Assert.True(EscapeCamp.GateLocked(sim));
        Assert.False(sim.Map.Passable(camp.Gate), "a locked gate can't be walked through");
        Assert.True(sim.PackOf(me).IsNull, "they took your pack");
        Assert.Contains(sim.StoredIn(camp.Chest), e => w.Get<Thing>(e).Def.Name == "ShortSword");
        var dragon = Companions.DragonOf(sim, me);
        Assert.True(w.Has<Chained>(dragon));
        Assert.NotNull(RidingSystem.CantMount(sim, me, dragon));
        Assert.Equal(3, w.Query<StoryNpc>().Count(e => w.Get<Creature>(e).Faction == Faction.Captors)); // the jailer and two guards
        Assert.All(w.Query<StoryNpc>(), e => Assert.DoesNotContain(Combat.CombatRules.Attacks(sim, e), a => a.Ranged)); // nobody shoots
        Assert.Null(sim.Storyteller.Difficulty); // raids wait for the wild

        // Nothing to do in the cage but wait: no digging out, no taking it apart, no walking off.
        var gate = camp.Gate;
        Assert.Equal(AvatarActionKind.None, AvatarSystem.ActionAt(sim, me, gate).Kind);
        Assert.Equal(AvatarActionKind.None, AvatarSystem.ActionAt(sim, me, w.Get<Position>(me).Cell - new int3(0, 0, 1), AvatarTool.Dig).Kind);
        sim.Enqueue(new AvatarMoveCommand(me, new int2(0, 1)));
        sim.TickMany(SimClock.TicksPerSecond * 10);
        Assert.True(camp.InCage(w.Get<Position>(me).Cell));
        Assert.False(sim.Combat.Active);
        Assert.Contains("supper", Prologue.Objective(sim, me));

        // And the dragon stays where it's chained.
        var spot = w.Get<Position>(dragon).Cell;
        sim.TickMany(SimClock.TicksPerHour / 2);
        Assert.Equal(spot, w.Get<Position>(dragon).Cell);
    }

    [Fact]
    public void AtSupperTheJailerOpensUpAndYouCanJumpHim()
    {
        var (sim, me, jailer) = AtSupper();
        var w = sim.World;
        var camp = EscapeCamp.Of(sim)!;
        Assert.False(EscapeCamp.GateLocked(sim), "the gate stands open");
        Assert.True(sim.Map.Passable(camp.Gate));
        var at = w.Get<Position>(jailer).Cell;
        Assert.Equal(AvatarActionKind.Overpower, AvatarSystem.ActionAt(sim, me, at).Kind);

        sim.Enqueue(new AvatarWorkCommand(me, true, at));
        sim.Tick();
        Assert.True(w.Get<Creature>(jailer).Downed || w.Get<Creature>(jailer).Dead);
        Assert.False(EscapeCamp.HasKey(sim, me), "the key falls at his feet");
        Assert.False(w.Has<Captive>(me));
        Assert.Equal(QuestStage.Escaping, w.Get<Quest>(me).Stage);
        Assert.All(w.Query<StoryNpc>(), e => Assert.Equal(Faction.Raider, w.Get<Creature>(e).Faction));
        Assert.True(sim.Combat.Active, "the alarm starts the fight");
        Assert.Equal(Combat.Side.Village, sim.Combat.Phase);
        Assert.Null(sim.Combat.Unit(Companions.DragonOf(sim, me))); // chained: not in it yet

        // His key and club lie in reach: picking them up costs time units, your turn first.
        var unit = sim.Combat.Unit(me)!;
        var loot = sim.Combat.ItemsInReach(sim, me);
        var key = loot.Single(i => w.Get<Thing>(i).Def.Name == "ChainKey");
        var club = loot.Single(i => w.Get<Thing>(i).Def.Name == "Club");
        Assert.Contains("chain key", Prologue.Objective(sim, me));
        Assert.Null(sim.Combat.PickUp(sim, me, club));
        Assert.Equal("Club", w.Get<Thing>(w.Get<Equipment>(me).Weapon).Def.Name);
        Assert.Null(sim.Combat.PickUp(sim, me, key));
        Assert.True(EscapeCamp.HasKey(sim, me));
        Assert.Equal(TimeUnits.PerTurn(sim, me) - 2 * TimeUnits.Share(sim, me, TimeUnits.PickUp), unit.TimeUnits);
    }

    [Fact]
    public void LettingTheMomentPassLocksYouUpAgain()
    {
        var (sim, me, _) = AtSupper();
        var camp = EscapeCamp.Of(sim)!;
        Assert.True(TickUntil(sim, () => EscapeCamp.GateLocked(sim), SimClock.HoursToTicks(3)), "the gate should be locked after supper");
        Assert.Equal(QuestStage.Captive, sim.World.Get<Quest>(me).Stage);
        Assert.False(sim.Combat.Active);
    }

    [Fact]
    public void FreeTheDragonAndFlyOffMidFight()
    {
        var (sim, me, jailer) = AtSupper();
        var w = sim.World;
        sim.Enqueue(new AvatarWorkCommand(me, true, w.Get<Position>(jailer).Cell));
        sim.Tick();
        var key = sim.Combat.ItemsInReach(sim, me).Single(i => w.Get<Thing>(i).Def.Name == "ChainKey");
        Assert.Null(sim.Combat.PickUp(sim, me, key));
        var dragon = Companions.DragonOf(sim, me);
        Assert.True(EscapeCamp.ChainedDragonInReach(sim, me).IsNull); // across the camp

        // Beside it (however you got there), the key frees it for TU; then you can leap on and go, fight or no fight.
        var spot = w.Get<Position>(dragon).Cell;
        w.Get<Position>(me) = Position.At(sim.SpiralFrom(spot, 2).First(c => c != spot && sim.Map.Passable(c) && Pathfinder.Touches(c, spot)));
        Assert.Equal(dragon, EscapeCamp.ChainedDragonInReach(sim, me));
        Assert.Contains("Free", Prologue.Objective(sim, me));
        var unit = sim.Combat.Unit(me)!;
        var before = unit.TimeUnits;
        Assert.Null(sim.Combat.Interact(sim, me, TimeUnits.Interact, () => EscapeCamp.Unchain(sim, me, dragon), "freed"));
        Assert.Equal(before - TimeUnits.Share(sim, me, TimeUnits.Interact), unit.TimeUnits);
        Assert.False(w.Has<Chained>(dragon));
        Assert.Equal(QuestStage.TakeWing, w.Get<Quest>(me).Stage);
        Assert.True(sim.Combat.Active);
        Assert.Null(Departure.CantFlyOff(sim, me));

        var party = Travel.Pack(sim, me, Entity.Null, Travel.FlightHours);
        Assert.Equal(StartKind.Escape, party.Start);
        var refuge = NewSim(62);
        var arrived = Travel.Arrive(refuge, party);
        Assert.False(Companions.DragonOf(refuge, arrived).IsNull);
        Assert.False(refuge.World.Has<Chained>(Companions.DragonOf(refuge, arrived)));
        Assert.Null(EscapeCamp.Of(refuge));
        Assert.Null(TowerPact.Of(refuge));
        refuge.TickMany(SimClock.TicksPerHour);
    }

    [Fact]
    public void GoDownInTheFightAndYouWakeUpBackInTheCage()
    {
        var (sim, me, jailer) = AtSupper();
        var w = sim.World;
        sim.Enqueue(new AvatarWorkCommand(me, true, w.Get<Position>(jailer).Cell));
        sim.Tick();
        Assert.True(sim.Combat.Active);

        // Too weak to stand: the fight's lost at once.
        w.Get<Health>(me).Blood = 0.44f;
        sim.Combat.VillageAuto = true;
        sim.Combat.Resolve(sim, maxRounds: 10);
        Assert.False(sim.Combat.Active);
        Assert.True(TickUntil(sim, () => w.Get<Quest>(me).Stage == QuestStage.Captive, SimClock.TicksPerSecond * 5), "dragged back to the cage");

        var camp = EscapeCamp.Of(sim)!;
        Assert.True(w.Has<Captive>(me));
        Assert.True(camp.InCage(w.Get<Position>(me).Cell));
        Assert.False(EscapeCamp.HasKey(sim, me));
        Assert.True(w.Has<Chained>(Companions.DragonOf(sim, me)));
        // Locked again (once nobody's in the gateway), and it stays locked the rest of this evening: no second supper.
        Assert.True(TickUntil(sim, () => EscapeCamp.GateLocked(sim), SimClock.TicksPerSecond * 30), "the gate should be locked again");
        Assert.True(sim.Clock.HourOfDay < EscapeCamp.LockUpHour, "(still supper time when it happened)");
        Assert.False(EscapeCamp.SupperTime(sim));
        Assert.False(TickUntil(sim, () => !EscapeCamp.GateLocked(sim), SimClock.HoursToTicks(2)), "no second supper tonight");
        // The key's back on whoever's acting jailer (Varrick is still out cold).
        var holder = camp.KeyHolder;
        Assert.False(holder.IsNull);
        Assert.True(w.Get<Creature>(holder).Able);
        Assert.DoesNotContain(w.Query<Thing>(), e => w.Get<Thing>(e).Def.Name == "ChainKey");
        Assert.All(w.Query<StoryNpc>().Where(e => !w.Get<Creature>(e).Dead), e => Assert.Equal(Faction.Captors, w.Get<Creature>(e).Faction));
        Assert.All(w.Get<Health>(me).Injuries, i => Assert.True(i.Tended));
        // ...and the next evening, another chance.
        Assert.True(TickUntil(sim, () => w.Get<Quest>(me).Stage == QuestStage.Opportunity || !w.Get<Creature>(me).Able, SimClock.TicksPerDay + SimClock.HoursToTicks(3)));
    }

    [Fact]
    public void TheKeyFallsWheneverItsHolderGoesDown()
    {
        var sim = NewSim();
        AdventureStart.Setup(sim, StartKind.Escape, Wren, "Normal");
        var w = sim.World;
        var camp = EscapeCamp.Of(sim)!;
        var jailer = EscapeCamp.Jailer(sim);
        Assert.Equal(jailer, camp.KeyHolder);

        // Brought down in a fight rather than jumped at supper: the key still falls.
        HealthSystem.SetDowned(sim, jailer, ref w.Get<Creature>(jailer), true);
        Assert.True(camp.KeyHolder.IsNull);
        var key = w.Query<Thing>().Single(e => w.Get<Thing>(e).Def.Name == "ChainKey");
        Assert.True(w.Get<Thing>(key).Spawned);
        Assert.True(Pathfinder.Touches(w.Get<Thing>(key).Cell, w.Get<Position>(jailer).Cell) || w.Get<Thing>(key).Cell == w.Get<Position>(jailer).Cell);
        // With him laid up, a guard stands in as jailer.
        Assert.NotEqual(jailer, EscapeCamp.Jailer(sim));
        Assert.False(EscapeCamp.Jailer(sim).IsNull);
    }

    [Fact]
    public void KnockedDownInAFightYouWonIsNoRecapture()
    {
        var (sim, me, jailer) = AtSupper();
        var w = sim.World;
        sim.Enqueue(new AvatarWorkCommand(me, true, w.Get<Position>(jailer).Cell));
        sim.Tick();
        Assert.True(sim.Combat.Active);
        // Everyone else goes down: the fight's won.
        foreach (var e in w.Query<StoryNpc>())
            if (w.Get<Creature>(e).Able)
            {
                w.Get<Health>(e).Blood = 0.3f;
                HealthSystem.SetDowned(sim, e, ref w.Get<Creature>(e), true);
            }
        sim.Combat.Wait(sim, me);
        sim.Combat.EndPhase(sim);
        sim.Combat.EndPhase(sim);
        Assert.False(sim.Combat.Active);
        Assert.False(sim.Combat.LastFightLost);
        // Then you collapse from your own wounds: nobody's left to drag you anywhere.
        w.Get<Health>(me).Blood = 0.44f;
        sim.TickMany(SimClock.TicksPerSecond * 10);
        Assert.NotEqual(QuestStage.Captive, w.Get<Quest>(me).Stage);
        Assert.False(w.Has<Captive>(me));
    }

    [Fact]
    public void YouCanSleepInTheCageOnThePalletOrTheFloor()
    {
        var sim = NewSim();
        var me = AdventureStart.Setup(sim, StartKind.Escape, Wren, "Normal");
        var w = sim.World;
        var camp = EscapeCamp.Of(sim)!;
        var pallet = w.Get<BedOwner>(me).Bed;
        var spot = w.Get<Thing>(pallet).Cell;
        Assert.True(camp.InCage(spot));
        Assert.Equal("StrawPallet", w.Get<Thing>(pallet).Def.Name);

        // On the pallet: F sleeps in it.
        w.Get<Position>(me) = Position.At(spot);
        var onPallet = AvatarSystem.ActionAt(sim, me, spot);
        Assert.Equal(AvatarActionKind.Sleep, onPallet.Kind);
        Assert.Equal(pallet, onPallet.Target);

        // Elsewhere in the cage, F on the floor does nothing (however tired), but Sleep (N) lies down there.
        var floor = camp.CageCenter;
        w.Get<Position>(me) = Position.At(floor);
        var rest = sim.Defs.Need("Rest");
        w.Get<Needs>(me).Values[rest.Index] = 0.3f;
        Assert.Equal(AvatarActionKind.None, AvatarSystem.ActionAt(sim, me, floor).Kind);
        sim.Enqueue(new SleepCommand(me));
        sim.TickMany(SimClock.TicksPerHour);
        Assert.True(w.Get<Mind>(me).Asleep);
        Assert.True(w.Get<Needs>(me).Get(rest) > 0.3f);
    }

    [Fact]
    public void TheCampSurvivesASave()
    {
        var sim = NewSim();
        var me = AdventureStart.Setup(sim, StartKind.Escape, Wren, "Normal");
        sim.TickMany(SimClock.TicksPerSecond * 30);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var camp = EscapeCamp.Of(loaded)!;
        Assert.Equal(EscapeCamp.Of(sim)!.Gate, camp.Gate);
        Assert.Equal(EscapeCamp.Of(sim)!.Chest, camp.Chest);
        Assert.True(loaded.World.Has<Captive>(me));
        Assert.True(loaded.World.Has<Chained>(Companions.DragonOf(loaded, me)));
        AiSystem.InterruptAll(sim);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
    }

    [Fact]
    public void ALockedGateBarsTheWayUntilItsUnlocked()
    {
        var sim = Create(20, 20);
        sim.BuildWallRing(C(5, 5), C(9, 9), "WoodWall");
        sim.DestroyThing(sim.Map.Structure[sim.Map.Idx(C(7, 9))]);
        var gate = sim.SpawnStructure(sim.Thing("CageGate"), C(7, 9));
        sim.RebuildDerivedIfNeeded();
        Assert.True(sim.Map.Passable(C(7, 9)));
        Assert.True(sim.Regions.CanReach(C(7, 7), C(7, 12), PathEnd.OnTile));

        Assert.True(sim.SetLocked(gate, true));
        sim.RebuildDerivedIfNeeded();
        Assert.False(sim.Map.Passable(C(7, 9)));
        Assert.False(sim.Regions.CanReach(C(7, 7), C(7, 12), PathEnd.OnTile));

        Assert.True(sim.SetLocked(gate, false));
        sim.RebuildDerivedIfNeeded();
        Assert.True(sim.Regions.CanReach(C(7, 7), C(7, 12), PathEnd.OnTile));
        Assert.False(sim.SetLocked(sim.Map.Structure[sim.Map.Idx(C(5, 5))], true)); // a wall has no lock
    }
}
