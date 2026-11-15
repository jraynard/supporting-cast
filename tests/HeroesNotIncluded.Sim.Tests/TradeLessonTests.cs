using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Scenarios.Lessons;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The tinker's, innkeeper's, cook's and healer's lessons in the practice yard: each set up, and
/// walked through its steps with the commands a client sends. Where a step is a hand game, its
/// outcome is set directly (the games have their own tests; <see cref="SmithLessonTests"/> drives
/// the forge's end to end): what's checked here is that each lesson names the right step and
/// points the right way as the world changes, and finishes.
/// </summary>
public class TradeLessonTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private sealed record Yard(Simulation Sim, Entity Me, LessonRun Run)
    {
        public LessonStage Stage => Lessons.Stage(Sim, Me, Run);
        public World W => Sim.World;

        public Entity Station(string def) => Run.Station(Sim, def);

        /// <summary>Stand within reach of a station or a person.</summary>
        public void StandBy(Entity target)
        {
            var station = W.Has<Thing>(target) && !W.Has<Creature>(target);
            var at = station ? W.Get<Thing>(target).Cell : W.Get<Position>(target).Cell;
            var cell = Sim.SpiralFrom(at, 3).First(c => c != at && c.Z == at.Z && Sim.Map.Passable(c)
                && (!station || AvatarSystem.InReach(Sim, c, target)));
            W.Get<Position>(Me).Cell = cell;
            W.Get<Position>(Me).Pos = Position.CenterOf(cell);
        }

        /// <summary>Tick until the step moves on from <paramref name="step"/>, up to some hours.</summary>
        public void WaitPast(string step, int hours = 12)
        {
            for (var t = 0; t < hours * 12 && Stage.Step == step && !Stage.Done; t++)
                Sim.TickMany(SimClock.TicksPerHour / 12);
        }

        /// <summary>Take some of a thing out of a station's store, standing by it.</summary>
        public void TakeFrom(Entity store, string def, int count = 1)
        {
            StandBy(store);
            var stack = Perception.For(Sim, Me).InContainer(store).FirstOrDefault(e => W.Get<Thing>(e).Def.Name == def, Entity.Null);
            Assert.True(!stack.IsNull, $"no {def} in the {W.Get<Thing>(store).Def.Name}: {string.Join(", ", Sim.StoredIn(store).Select(e => W.Get<Thing>(e).Def.Name))}");
            Sim.ApplyNow(new TakeFromStorageCommand(Me, stack, count));
        }

        /// <summary>Put a thing in the player's hand, made by them (a hand game's outcome).</summary>
        public Entity Hold(string def, bool made = true)
        {
            var held = W.Get<Carrying>(Me).Item;
            if (!held.IsNull)
            {
                W.Get<Carrying>(Me).Item = Entity.Null;
                Inventory.Stow(Sim, Me, held);
            }
            var item = Sim.CreateItem(Sim.Defs.Thing(def), 1);
            if (made)
                W.Add(item, new Crafted { Maker = Me });
            W.Get<Carrying>(Me).Item = item;
            return item;
        }

        /// <summary>The workplace's fires all lit.</summary>
        public void LightFires()
        {
            foreach (var e in W.Query<PartOf>())
                if (W.Get<PartOf>(e).Building == Run.Workplace && W.TryGet<Fueled>(e, out _))
                    W.Get<Fueled>(e).Fuel = 5f;
        }

        /// <summary>The customer of the open order of a kind since the lesson began.</summary>
        public KnownOrder Order(ServiceKind kind) =>
            Perception.For(Sim, Me).Orders().First(o => o.Kind == kind && o.PostedTick >= Run.StartTick && o.Status == OrderStatus.Open);

        /// <summary>Walk to them with the order's button and let it play out.</summary>
        public void SeeTo(KnownOrder order, string until)
        {
            Sim.ApplyNow(new SeeToCustomerCommand(Me, order.Id));
            for (var t = 0; t < SeeToCustomerJob.MaxTicks && Stage.Step == until && !Stage.Done; t++)
                Sim.Tick();
        }
    }

    private static Yard Begin(string trade, string lesson)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(trade)], []));
        sim.ApplyNow(new StartRoundCommand());
        sim.Round.Practice = true;
        var me = Players.CharacterOf(sim, Ana);
        var def = sim.Defs.Trade(trade);
        var index = def.Lessons.FindIndex(l => l.Id == lesson);
        var run = new LessonRun(sim, me, def, def.Lessons[index]);
        sim.ApplyNow(new PracticeCommand(me, PracticeAction.Lesson, index));
        return new Yard(sim, me, run);
    }

    [Fact]
    public void EveryLessonOfEveryTradeBeginsOnAStepWithWords()
    {
        var defs = LoadDefs();
        foreach (var trade in defs.AllTrades.Where(t => t.Lessons.Count > 0))
            foreach (var lesson in trade.Lessons)
            {
                Assert.True(Lessons.For(trade.Name, lesson.Id) != null, $"{trade.Name}/{lesson.Id} has words but no logic");
                var y = Begin(trade.Name, lesson.Id);
                y.Sim.TickMany(60);
                Assert.True(lesson.IndexOf(y.Stage.Step) >= 0, $"{trade.Name}/{lesson.Id}: '{y.Stage.Step}'");
                Assert.False(y.Stage.Done, $"{trade.Name}/{lesson.Id} done before it began");
            }
    }

    // ---------------- Tinker ----------------

    [Fact]
    public void AStrongboxIsFeltOverPickedAndTakenBack()
    {
        var y = Begin("Tinker", "box");
        y.WaitPast("wait");
        Assert.Equal("take", y.Stage.Step);
        var order = y.Order(ServiceKind.Unlock);
        y.Sim.ApplyNow(new TakeOrderCommand(y.Me, order.Id));
        Assert.Equal("handover", y.Stage.Step);
        y.SeeTo(order, "handover");
        Assert.Equal("probe", y.Stage.Step);

        var box = Lessons.EntrustedBy(y.Sim, y.Me, order.Poster);
        y.W.Get<Strongbox>(box).SearchedBy = y.Me; // felt over (ProbeSession's outcome)
        y.W.Get<Strongbox>(box).Armed = false;
        Assert.Equal("pick", y.Stage.Step);
        y.W.Get<Strongbox>(box).Locked = false; // picked
        Assert.Equal("giveback", y.Stage.Step);
        Assert.Equal(order.Poster, y.Stage.Point);
        y.SeeTo(order, "giveback");
        Assert.True(y.Stage.Done);
    }

    [Fact]
    public void ACurioIsCollectedAndLookedOver()
    {
        var y = Begin("Tinker", "curio");
        y.WaitPast("wait");
        var order = y.Order(ServiceKind.Appraise);
        y.Sim.ApplyNow(new TakeOrderCommand(y.Me, order.Id));
        y.SeeTo(order, "handover");
        Assert.Equal("appraise", y.Stage.Step);
    }

    [Fact]
    public void ALockIsPinnedFittedAndTurned()
    {
        var y = Begin("Tinker", "lock");
        Assert.Equal("pin", y.Stage.Step);
        Assert.Equal(y.Station(Tinkers.BenchDef), y.Stage.Point);

        var lockItem = y.Sim.CreateItem(y.Sim.Defs.Thing(Locksmithing.LockDef), 1);
        Inventory.Stow(y.Sim, y.Me, Locksmithing.CutLock(y.Sim, lockItem, 77, 3, 0.8f, 0.9f, y.Me));
        Inventory.Stow(y.Sim, y.Me, lockItem);
        Assert.Equal("fit", y.Stage.Step);
        var door = y.Stage.Point;
        Assert.True(Locksmithing.IsDoor(y.Sim, door));

        y.StandBy(door);
        y.Sim.ApplyNow(new FitLockCommand(y.Me, door, lockItem));
        Assert.Equal("turn", y.Stage.Step);
        y.Sim.ApplyNow(new TurnKeyCommand(y.Me, door, true));
        Assert.True(y.Stage.Done);
    }

    [Fact]
    public void ACrossbowIsDoneOnceMade()
    {
        var y = Begin("Tinker", "crossbow");
        Assert.Equal("choose", y.Stage.Step);
        y.Hold("Crossbow");
        Assert.True(y.Stage.Done);
    }

    // ---------------- Innkeeper ----------------

    [Fact]
    public void AnAleIsPouredAndHandedToWhoeverAsked()
    {
        var y = Begin("Innkeeper", "ale");
        y.WaitPast("wait");
        Assert.Equal("tankard", y.Stage.Step);
        var cask = y.Station("AleCask");
        var shelf = y.Stage.Point;
        y.TakeFrom(shelf, "Tankard");
        Assert.Equal("pour", y.Stage.Step);
        Assert.Equal(cask, y.Stage.Point);

        y.Hold("Ale", made: false); // poured (PourSession's outcome)
        var order = Perception.For(y.Sim, y.Me).Orders().First(o => o.Kind is ServiceKind.Drink or ServiceKind.MixedDrink && o.Status == OrderStatus.Open);
        Assert.Equal("give", y.Stage.Step);
        Assert.Equal(order.Poster, y.Stage.Point);
        y.StandBy(order.Poster);
        y.Sim.ApplyNow(new GiveCommand(y.Me, order.Poster, y.W.Get<Carrying>(y.Me).Item, 1));
        Assert.True(y.Stage.Done);
    }

    [Fact]
    public void ANamedDrinkIsMixed()
    {
        var y = Begin("Innkeeper", "mixed");
        y.Sim.Tick();
        Assert.Equal("tankard", y.Stage.Step);
        y.TakeFrom(y.Stage.Point, "Tankard");
        Assert.Equal("mix", y.Stage.Step);
        Assert.Equal(y.Station("BarCounter"), y.Stage.Point);
        y.Hold("Hearthwarmer", made: false);
        Assert.True(y.Stage.Done);
    }

    [Fact]
    public void APartyWantingWorkIsPointedOut()
    {
        var y = Begin("Innkeeper", "quest");
        y.WaitPast("wait");
        Assert.Equal("offer", y.Stage.Step);
        Assert.Equal(y.Order(ServiceKind.Quest).Poster, y.Stage.Point);
    }

    // ---------------- Cook ----------------

    [Fact]
    public void AStewGoesFromTheLarderToTheHatch()
    {
        var y = Begin("Cook", "stew");
        y.Sim.Tick();
        Assert.Equal("larder", y.Stage.Step);
        y.TakeFrom(y.Stage.Point, "Cabbage", 4);
        Assert.Equal("fill", y.Stage.Step);
        var pot = y.Station("StewPot");
        y.StandBy(pot);
        y.Sim.ApplyNow(new PotCommand(y.Me, pot, PotStep.Add));
        // The hearth's lit at the start of a round; put out, the lesson sends you for logs.
        foreach (var e in y.W.Query<PartOf>())
            if (y.W.Get<PartOf>(e).Building == y.Run.Workplace && y.W.TryGet<Fueled>(e, out _))
                y.W.Get<Fueled>(e).Fuel = 0f;
        Assert.Equal("fuel", y.Stage.Step);
        Assert.Equal(y.Station("LogBasket"), y.Stage.Point);
        y.LightFires();
        Assert.Equal("stir", y.Stage.Step);

        y.Hold("Stew", made: false); // ladled
        Assert.Equal("hatch", y.Stage.Step);
        Assert.Equal(Hatch.Of(y.Sim, y.Run.Workplace), y.Stage.Point);
    }

    [Fact]
    public void ARoastIsDoneWhenItsLifted()
    {
        var y = Begin("Cook", "roast");
        y.Sim.Tick();
        Assert.Equal("meat", y.Stage.Step);
        y.TakeFrom(y.Stage.Point, "Meat");
        y.LightFires();
        y.StandBy(y.Station("Spit"));
        Assert.Equal("spit", y.Stage.Step);
        y.Hold(Roasting.Dish, made: false);
        Assert.True(y.Stage.Done);
    }

    [Fact]
    public void BreadIsKneadedProvedAndBaked()
    {
        var y = Begin("Cook", "bread");
        y.Sim.Tick();
        Assert.Equal("flour", y.Stage.Step);
        y.TakeFrom(y.Stage.Point, Baking.Flour, Baking.FlourPerDough);
        Assert.Equal("knead", y.Stage.Step);

        var dough = y.Hold(Baking.DoughDef, made: false);
        y.W.Add(dough, new Dough { Knead = 0.8f, MadeAt = y.Sim.Clock.Tick, Maker = y.Me });
        Assert.Equal("prove", y.Stage.Step);
        y.W.Get<Dough>(dough).MadeAt = y.Sim.Clock.Tick - SimClock.HoursToTicks(1.5f);
        y.LightFires();
        Assert.Equal("oven", y.Stage.Step);
        y.Hold(Baking.Bread, made: false);
        Assert.True(y.Stage.Done);
    }

    // ---------------- Healer ----------------

    [Fact]
    public void AWoundIsFeltDrawnAndMended()
    {
        var y = Begin("Healer", "wound");
        y.Sim.Tick();
        Assert.Equal("feel", y.Stage.Step);
        var patient = y.Stage.Point;
        Assert.Equal(Practice.PatientName, y.W.Get<Creature>(patient).Name);

        y.StandBy(patient);
        foreach (var injury in y.W.Get<Health>(patient).Injuries)
            injury.FeltBy = y.Me; // felt over
        Assert.Equal("draw", y.Stage.Step);

        // Drawn across (DrawSession's outcome): gone from them, carried by the healer.
        var drawn = y.W.Get<Health>(patient).Injuries.ToList();
        y.W.Get<Health>(patient).Injuries.Clear();
        HealthSystem.Damage(y.Sim, y.Me, Practice.WoundDamage, DamageType.Cut, y.W.Get<Health>(y.Me).Body.Parts.FindIndex(p => !p.Vital));
        foreach (var injury in y.W.Get<Health>(y.Me).Injuries)
            injury.Absorbed = true;
        Assert.Equal("weave", y.Stage.Step);
        y.W.Get<Mana>(y.Me).Current = 0f;
        Assert.Equal("meditate", y.Stage.Step);
        y.W.Get<Mana>(y.Me).Current = y.W.Get<Mana>(y.Me).Max;

        y.W.Get<Health>(y.Me).Injuries.Clear(); // woven
        Assert.True(y.Stage.Done);
        Assert.NotEmpty(drawn);
    }

    [Fact]
    public void ManaIsWonBackAtTheStone()
    {
        var y = Begin("Healer", "meditate");
        y.Sim.Tick();
        Assert.Equal(0f, y.W.Get<Mana>(y.Me).Current);
        var stone = y.Station("MeditationStone");
        Assert.Equal(stone, y.Stage.Point);
        y.StandBy(stone);
        Assert.Equal("meditate", y.Stage.Step);
        y.W.Get<Mana>(y.Me).Current = y.W.Get<Mana>(y.Me).Max;
        Assert.True(y.Stage.Done);
    }
}
