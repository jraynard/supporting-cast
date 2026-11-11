using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Saving;
using DragonWorld.Sim.Scenarios;
using DragonWorld.Sim.Systems;
using static DragonWorld.Sim.Tests.TestWorld;

namespace DragonWorld.Sim.Tests;

/// <summary>
/// What Adventure's new starts share: an adult dragon bonded to the player, its look, feeding
/// itself by hunting, and the two of them flying off to a new map together.
/// </summary>
public class AdventureFoundationTests
{
    private static readonly DragonLook Teal = new() { Element = DragonElement.Nature, Scales = 0x2E6A4A, Belly = 0xE8C070, Wings = 0x24503A, Horns = 0x3A3436, Eyes = 0x7AE0FF };

    private static Entity Person(Simulation sim, int3 at, string name = "Wren")
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), name, at, ageDays: 25 * 365);
        sim.World.Add(e, new Avatar());
        return e;
    }

    /// <summary>A mild day, so the dragon isn't off warming itself up (test maps start cold).</summary>
    private static Simulation Mild(int size = 60)
    {
        var sim = Create(size, size);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static bool TickUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (var t = 0; t < maxTicks && !done(); t++)
            sim.Tick();
        return done();
    }

    [Fact]
    public void AStartingDragonIsAnAdultBondedToItsKeeper()
    {
        var sim = Create();
        var me = Person(sim, C(10, 10));
        var dragon = Companions.SpawnBondedDragon(sim, me, "  Ashwing\n ", Teal, C(12, 10));
        var w = sim.World;

        Assert.Equal("Ashwing", w.Get<Creature>(dragon).Name);
        Assert.Equal("Adult", w.Get<Creature>(dragon).Stage(sim.Clock.Tick).Name);
        Assert.Equal(me, w.Get<Dragon>(dragon).Keeper);
        Assert.Equal(dragon, w.Get<Keeper>(me).Dragon);
        Assert.Equal(dragon, Companions.DragonOf(sim, me));
        // A partner starts trusting (docs/plans/dragon-interactions.md): it listens four times in five, and lets you ride.
        Assert.Equal("Trusting", Bonding.Level(sim, dragon)!.Name);
        Assert.Equal(0.8f, DragonSystem.Obedience(sim, dragon), 2);
        Assert.Null(RidingSystem.CantMount(sim, me, dragon));
        Assert.Equal(Teal, w.Get<DragonLook>(dragon));
        Assert.Equal(Companions.DefaultName, Companions.CleanName("\t "));
    }

    [Fact]
    public void TheDragonsLookAndTheStorySurviveASave()
    {
        var sim = Create();
        var me = Person(sim, C(10, 10));
        var dragon = Companions.SpawnBondedDragon(sim, me, "Ashwing", Teal, C(12, 10));
        sim.Story.Start = StartKind.Pact;
        sim.Story.PendingDifficulty = "Normal";
        sim.TickMany(60);

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        AiSystem.InterruptAll(sim); // jobs aren't saved: a load equals the original with every job ended
        Assert.Equal(Teal, loaded.World.Get<DragonLook>(dragon));
        Assert.Equal(StartKind.Pact, loaded.Story.Start);
        Assert.Equal("Normal", loaded.Story.PendingDifficulty);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
    }

    [Fact]
    public void AnUnsetStoryLeavesTheStateHashAlone()
    {
        var a = Create();
        var b = Create();
        b.Story.Start = StartKind.None;
        Assert.Equal(a.StateHash(), b.StateHash());
        b.Story.Start = StartKind.Escape;
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void AHungryAdultWithNoMeatHuntsAndEatsItsKill()
    {
        var sim = Mild();
        var me = Person(sim, C(10, 10));
        var dragon = Companions.SpawnBondedDragon(sim, me, "Ashwing", null, C(12, 10));
        var food = sim.Defs.Need("Food");
        sim.World.Get<Needs>(dragon).Values[food.Index] = food.UrgentBelow - 0.05f;
        var deer = sim.SpawnCreature(sim.Defs.Creature("Deer"), "Deer", C(40, 40), Faction.Wild);
        var before = sim.World.Get<Needs>(dragon).Get(food);

        Assert.True(TickUntil(sim, () => !sim.World.IsAlive(deer), SimClock.HoursToTicks(2)), "the dragon should catch and eat the deer");
        Assert.True(sim.World.Get<Needs>(dragon).Get(food) > before + 0.1f);
    }

    [Fact]
    public void MeatLaidOutComesBeforeTheHunt()
    {
        var sim = Mild();
        var me = Person(sim, C(10, 10));
        var dragon = Companions.SpawnBondedDragon(sim, me, "Ashwing", null, C(12, 10));
        var food = sim.Defs.Need("Food");
        sim.World.Get<Needs>(dragon).Values[food.Index] = food.UrgentBelow - 0.05f;
        sim.SpawnItem(sim.Thing("Meat"), 40, C(14, 12));
        var deer = sim.SpawnCreature(sim.Defs.Creature("Deer"), "Deer", C(40, 40), Faction.Wild);

        sim.TickMany(SimClock.TicksPerSecond * 10);
        Assert.True(sim.World.Get<Needs>(dragon).Get(food) > food.SeekBelow, "it ate the meat it was given");
        Assert.True(sim.SpawnedItems().Where(e => sim.World.Get<Thing>(e).Def.Name == "Meat").Sum(e => sim.World.Get<Stack>(e).Count) < 40);
        Assert.False(sim.World.Get<Creature>(deer).Dead);
    }

    [Fact]
    public void DragonsLeaveGameThatFightsBackAlone()
    {
        var sim = Mild();
        var me = Person(sim, C(10, 10));
        var dragon = Companions.SpawnBondedDragon(sim, me, "Ashwing", null, C(12, 10));
        var boar = sim.SpawnCreature(sim.Defs.Creature("Boar"), "Boar", C(30, 30), Faction.Wild);
        Assert.True(sim.World.Get<Creature>(boar).Def.Animal!.Aggressive);
        Assert.Null(DragonHunting.Hunt(sim, dragon));
        sim.World.Get<Creature>(boar).Dead = true; // a dead one's just meat
        sim.World.Add(boar, new Rot());
        Assert.NotNull(DragonHunting.Hunt(sim, dragon));
    }

    [Fact]
    public void YoungDragonsDontHuntAlone()
    {
        var sim = Mild();
        var me = Person(sim, C(10, 10));
        var juvenile = Companions.SpawnBondedDragon(sim, me, "Kit", null, C(12, 10), ageDays: 6);
        sim.SpawnCreature(sim.Defs.Creature("Deer"), "Deer", C(30, 30), Faction.Wild);
        Assert.Null(DragonHunting.Hunt(sim, juvenile));
    }

    [Fact]
    public void YouCanFlyOffOnlyWhenTheStoryAllowsAndYoureOnYourDragon()
    {
        var sim = Create();
        var me = Person(sim, C(10, 10));
        var dragon = Companions.SpawnBondedDragon(sim, me, "Ashwing", Teal, C(11, 10));
        Assert.False(Departure.StoryAllows(sim, me));
        Assert.NotNull(Departure.CantFlyOff(sim, me));

        sim.World.Add(me, new Quest { Stage = QuestStage.TakeWing });
        Assert.Contains("Mount", Departure.CantFlyOff(sim, me));
        RidingSystem.MountUp(sim, me, dragon);
        Assert.Null(Departure.CantFlyOff(sim, me));
    }

    [Fact]
    public void FlyingOffCarriesTheDragonAndBothOfYourWoundsToANewMap()
    {
        var defs = LoadDefs();
        var sim = Simulation.CreateStandard(defs, 96, 96, 21, Maps.MapGenerator.RecommendedDepth);
        Maps.MapGenerator.Generate(sim, new Maps.MapGenerator.Options { Villagers = 0, SpawnStartingKit = false });
        sim.RebuildDerivedIfNeeded();
        sim.Story.Start = StartKind.Escape;
        sim.Story.PendingDifficulty = "Hard";
        var spot = sim.SpiralFrom(new int3(48, 48, Maps.MapGenerator.RecommendedDepth / 2), 30).First(c => sim.Map.Passable(c));
        var me = Person(sim, spot);
        sim.GiveStartingPack(me);
        var dragon = Companions.SpawnBondedDragon(sim, me, "Ashwing", Teal, spot);
        sim.World.Get<Dragon>(dragon).Affinity = 0.9f;
        HealthSystem.Damage(sim, me, 8, Defs.DamageType.Cut);
        HealthSystem.Damage(sim, dragon, 12, Defs.DamageType.Pierce);
        var myWounds = sim.World.Get<Health>(me).Injuries.Count;
        var itsWounds = sim.World.Get<Health>(dragon).Injuries.Count;
        Assert.True(myWounds > 0 && itsWounds > 0);
        var loose = sim.CreateItem(sim.Thing("Berries"), 5);

        var party = Travel.Pack(sim, me, Entity.Null, Travel.FlightHours) with
        {
            Cargo = [new Cargo("Berries", 5, null, 0f)],
        };
        sim.World.Destroy(loose);
        Assert.False(party.Wagon);
        Assert.Equal("Ashwing", party.Dragon!.Name);

        var home = Simulation.CreateStandard(defs, 96, 96, 22, Maps.MapGenerator.RecommendedDepth);
        var arrived = Travel.Arrive(home, party);
        var w = home.World;
        var newDragon = Companions.DragonOf(home, arrived);
        Assert.False(newDragon.IsNull);
        Assert.Equal("Ashwing", w.Get<Creature>(newDragon).Name);
        Assert.Equal(Teal, w.Get<DragonLook>(newDragon));
        Assert.Equal(0.9f, w.Get<Dragon>(newDragon).Affinity);
        Assert.Equal(myWounds, w.Get<Health>(arrived).Injuries.Count);
        Assert.Equal(itsWounds, w.Get<Health>(newDragon).Injuries.Count);
        Assert.DoesNotContain(w.Query<Thing>(), e => w.Get<Thing>(e).Def.Name == "Wagon");
        Assert.Equal(StartKind.Escape, home.Story.Start);
        Assert.Equal("Hard", home.Story.PendingDifficulty);
        Assert.True(home.SpawnedItems().Any(e => w.Get<Thing>(e).Def.Name == "Berries")
            || home.StoredIn(home.PackOf(arrived)).Any(e => w.Get<Thing>(e).Def.Name == "Berries"));
        home.TickMany(SimClock.TicksPerHour); // and the two of them carry on from there
    }
}

/// <summary>Adventure's dragon starts as the title screen makes them: you, your dragon, and the story state.</summary>
public class AdventureStartTests
{
    private static Simulation NewSim(ulong seed = 31) =>
        Simulation.CreateStandard(LoadDefs(), 96, 96, seed, Maps.MapGenerator.RecommendedDepth);

    private static readonly Hero Wren = new("Wren", new Appearance { Body = BodyType.Female, Hair = HairStyle.Long, HairColor = 0x2A2220, EyeColor = 0x3A6EC8, SkinColor = 0xB07A52 },
        ["Hardy"], "Ashwing", new DragonLook { Scales = 0x2E4E8A, Belly = 0xB8B8C8, Wings = 0x283E70, Horns = 0x9AA0A8, Eyes = 0x7AE0FF });

    [Fact]
    public void EscapesRefugeStartsYouFreeWithYourDragonAndRaidsOn()
    {
        // Where the escape leads (--adventure escape --stage site): the camp itself is EscapeTests'.
        var sim = NewSim();
        var me = AdventureStart.Refuge(sim, Wren, "Normal");
        var w = sim.World;
        Assert.Equal("Wren", w.Get<Creature>(me).Name);
        Assert.True(w.Has<Avatar>(me));
        Assert.Equal(Wren.Look, w.Get<Appearance>(me));
        Assert.Equal(["Hardy"], w.Get<Traits>(me).List.Select(t => t.Name));
        var dragon = Companions.DragonOf(sim, me);
        Assert.False(dragon.IsNull);
        Assert.Equal("Ashwing", w.Get<Creature>(dragon).Name);
        Assert.Equal(Wren.DragonLook, w.Get<DragonLook>(dragon));
        Assert.Equal("Adult", w.Get<Creature>(dragon).Stage(sim.Clock.Tick).Name);
        Assert.Equal(StartKind.Escape, sim.Story.Start);
        Assert.Equal("Normal", sim.Storyteller.Difficulty?.Name);
        Assert.Null(sim.Story.PendingDifficulty);
        Assert.Single(sim.Creatures(), e => w.Get<Creature>(e).Def.Race == Defs.Race.Human);
    }

    [Fact]
    public void ThePactHoldsRaidsOffAndKeepsTheDifficultyForLater()
    {
        var sim = NewSim();
        var me = AdventureStart.Setup(sim, StartKind.Pact, Wren, "Hard");
        Assert.Null(sim.Storyteller.Difficulty);
        Assert.Equal("Hard", sim.Story.PendingDifficulty);
        Assert.Equal(StartKind.Pact, sim.Story.Start);
        Assert.False(Companions.DragonOf(sim, me).IsNull);

        // A day goes by without trouble, and it all survives a save.
        sim.TickMany(SimClock.TicksPerDay);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal("Hard", loaded.Story.PendingDifficulty);
        Assert.Equal("Ashwing", loaded.World.Get<Creature>(Companions.DragonOf(loaded, me)).Name);
    }

    [Fact]
    public void OffMeansNoRaidsLaterEither()
    {
        var sim = NewSim();
        AdventureStart.Setup(sim, StartKind.Pact, Wren, "Off");
        Assert.Null(sim.Story.PendingDifficulty);
        Assert.Throws<ArgumentException>(() => AdventureStart.Setup(NewSim(), StartKind.EggBearer, Wren));
    }
}
