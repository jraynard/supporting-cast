using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Scenarios.Lessons;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The blacksmith's lessons in the practice yard, played through by a careful hand with the same
/// commands a client sends: each step named as it comes, the lesson following back when a blade is
/// refused, and done at the end.
/// </summary>
public class SmithLessonTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private sealed record Yard(Simulation Sim, Entity Me, LessonRun Run)
    {
        public LessonStage Stage => Lessons.Stage(Sim, Me, Run);

        public SmithyKit Kit => new(Run.Station(Sim, "Forge"), Run.Station(Sim, "Anvil"), Run.Station(Sim, "QuenchTub"), Run.Station(Sim, "Grindstone"));

        /// <summary>Stand within reach of a station or a person.</summary>
        public void StandBy(Entity target)
        {
            var w = Sim.World;
            var station = w.Has<Thing>(target) && !w.Has<Creature>(target);
            var at = station ? w.Get<Thing>(target).Cell : w.Get<Position>(target).Cell;
            var cell = Sim.SpiralFrom(at, 3).First(c => c != at && c.Z == at.Z && Sim.Map.Passable(c)
                && (!station || AvatarSystem.InReach(Sim, c, target)));
            w.Get<Position>(Me).Cell = cell;
            w.Get<Position>(Me).Pos = Position.CenterOf(cell);
        }

        /// <summary>Tick until the step moves on from <paramref name="step"/>, up to some hours.</summary>
        public void WaitPast(string step, int hours = 12)
        {
            for (var t = 0; t < hours * 12 && Stage.Step == step && !Stage.Done; t++)
                Sim.TickMany(SimClock.TicksPerHour / 12);
        }

        /// <summary>Work the piece in the tongs to the end, stepping over to each station.</summary>
        public void Drive() => Kit.Drive(Sim, Me, goTo: StandBy);

        /// <summary>The forge lit and hot, as a smith keeps it.</summary>
        public void LightForge()
        {
            var forge = Run.Station(Sim, "Forge");
            Sim.World.Get<Fueled>(forge).Fuel = 10;
            Sim.Map.Temperature[Sim.Map.Idx(Sim.World.Get<Thing>(forge).Cell)] = 1200;
        }
    }

    private static Yard Begin(string lesson)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(SmithLessons.Trade)], []));
        sim.ApplyNow(new StartRoundCommand());
        sim.Round.Practice = true;
        var me = Players.CharacterOf(sim, Ana);
        var trade = sim.Defs.Trade(SmithLessons.Trade);
        var index = trade.Lessons.FindIndex(l => l.Id == lesson);
        var run = new LessonRun(sim, me, trade, trade.Lessons[index]);
        sim.ApplyNow(new PracticeCommand(me, PracticeAction.Lesson, index));
        var yard = new Yard(sim, me, run);
        yard.LightForge();
        return yard;
    }

    private static KnownOrder MyOrder(Yard y) =>
        Perception.For(y.Sim, y.Me).Orders().First(o => o.Kind == ServiceKind.Repair && o.PostedTick >= y.Run.StartTick && (o.MineToDo || o.Status == OrderStatus.Open));

    /// <summary>Take the order, stand by them, and have the blade handed over.</summary>
    private static Entity TakeTheBlade(Yard y)
    {
        y.WaitPast("wait");
        Assert.Equal("take", y.Stage.Step);
        var order = MyOrder(y);
        Assert.Equal(order.Poster, y.Stage.Point);
        y.Sim.ApplyNow(new TakeOrderCommand(y.Me, order.Id));
        Assert.Equal("handover", y.Stage.Step);
        for (var t = 0; t < 24 && y.Stage.Step == "handover"; t++)
        {
            y.StandBy(order.Poster); // they keep their place in the line: follow them
            y.Sim.TickMany(SimClock.TicksPerHour / 24);
        }
        var blade = Lessons.EntrustedBy(y.Sim, y.Me, order.Poster);
        Assert.False(blade.IsNull, "they hand it over once you're close");
        return blade;
    }

    private static void GiveBack(Yard y, Entity blade, bool early = false)
    {
        var owner = y.Sim.World.Get<Entrusted>(blade).Owner;
        if (!early)
            Assert.Equal(owner, y.Stage.Point);
        y.StandBy(owner);
        y.Sim.ApplyNow(new GiveCommand(y.Me, owner, blade, 1));
    }

    [Fact]
    public void EveryStepALessonCanNameHasWords()
    {
        var defs = LoadDefs();
        // The basics: TutorialTests. A mod that's off (registered by another test's load) has its own words checked with it.
        foreach (var lesson in Lessons.All.Where(l => l.Trade != BasicsLesson.AnyTrade && defs.TryTrade(l.Trade, out _)))
        {
            var def = defs.Trade(lesson.Trade).Lessons.SingleOrDefault(l => l.Id == lesson.Id);
            Assert.True(def != null, $"{lesson.Trade}/{lesson.Id} has no words in trades.json");
            foreach (var step in lesson.Steps)
                Assert.True(def.IndexOf(step) >= 0, $"{lesson.Trade}/{lesson.Id}: no words for step '{step}'");
        }
    }

    [Fact]
    public void SharpeningADullBlade()
    {
        var y = Begin("sharpen");
        var blade = TakeTheBlade(y);
        Assert.False(Lessons.LooksChipped(Perception.For(y.Sim, y.Me).Condition(blade)!), $"{y.Sim.LabelOf(blade)}: {Perception.For(y.Sim, y.Me).Condition(blade)}");
        Assert.Equal("grind", y.Stage.Step);
        Assert.Equal(y.Run.Station(y.Sim, "Grindstone"), y.Stage.Point);

        y.StandBy(y.Kit.Grindstone);
        y.Kit.Grind(y.Sim, y.Me);
        Assert.True(WeaponWear.Mended(y.Sim, blade), WeaponWear.Describe(y.Sim, blade));
        Assert.Equal("giveback", y.Stage.Step);

        var purse = Coin.Count(y.Sim, y.Me);
        GiveBack(y, blade);
        Assert.True(y.Stage.Done);
        Assert.True(Coin.Count(y.Sim, y.Me) > purse, "paid");
    }

    [Fact]
    public void MendingAChippedBladeFollowsARefusalBack()
    {
        var y = Begin("mend");
        var blade = TakeTheBlade(y);
        Assert.Equal("mend", y.Stage.Step);

        // The forge gone out comes first.
        var forge = y.Run.Station(y.Sim, "Forge");
        y.Sim.World.Get<Fueled>(forge).Fuel = 0;
        Assert.Equal("fuel", y.Stage.Step);
        y.LightForge();

        y.StandBy(forge);
        y.Sim.ApplyNow(new RepairCommand(y.Me, forge, blade));
        Assert.Equal("heat", y.Stage.Step);
        y.Drive();
        for (var tries = 0; tries < 3 && y.Stage.Step == "mend"; tries++) // a middling hammering leaves chips: again
        {
            y.StandBy(forge);
            y.Sim.ApplyNow(new RepairCommand(y.Me, forge, blade));
            y.Drive();
        }
        Assert.Equal("grind", y.Stage.Step);

        // Handed back still dull: refused, and the lesson goes back to taking the order.
        GiveBack(y, blade, early: true);
        Assert.Equal("take", y.Stage.Step);
        Assert.False(y.Stage.Done);
        blade = TakeTheBlade(y);
        Assert.Equal("grind", y.Stage.Step);

        y.StandBy(y.Kit.Grindstone);
        y.Kit.Grind(y.Sim, y.Me);
        Assert.Equal("giveback", y.Stage.Step);
        GiveBack(y, blade);
        Assert.True(y.Stage.Done);
    }

    [Fact]
    public void ForgingAShortSwordFromTheBars()
    {
        var y = Begin("forge");
        Assert.Equal("choose", y.Stage.Step);
        var forge = y.Run.Station(y.Sim, "Forge");
        Assert.Equal(forge, y.Stage.Point);

        y.StandBy(forge);
        y.Sim.ApplyNow(new CraftCommand(y.Me, forge, y.Sim.Defs.Recipe("ForgeSword")));
        Assert.Equal("heat", y.Stage.Step);
        y.Drive();
        Assert.Equal("grind", y.Stage.Step);
        Assert.False(y.Stage.Done);

        y.StandBy(y.Kit.Grindstone);
        y.Kit.Grind(y.Sim, y.Me);
        Assert.True(y.Stage.Done, y.Stage.Step);
    }

    [Fact]
    public void SmeltingABloomThenSettingAPrice()
    {
        var y = Begin("smelt");
        var chest = y.Run.Station(y.Sim, "Chest");
        Assert.Equal("ore", y.Stage.Step);
        Assert.Equal(chest, y.Stage.Point);

        Entity Stack(Entity container, string def) =>
            Perception.For(y.Sim, y.Me).InContainer(container).First(e => y.Sim.World.Get<Thing>(e).Def.Name == def);
        y.StandBy(chest);
        y.Sim.ApplyNow(new TakeFromStorageCommand(y.Me, Stack(chest, "IronOre"), 4));
        Assert.Equal("charcoal", y.Stage.Step);
        var bin = y.Run.Station(y.Sim, "FuelBin");
        y.StandBy(bin);
        y.Sim.ApplyNow(new TakeFromStorageCommand(y.Me, Stack(bin, "Charcoal"), 2));
        Assert.Equal("smelt", y.Stage.Step);

        var forge = y.Run.Station(y.Sim, "Forge");
        y.StandBy(forge);
        y.Sim.ApplyNow(new CraftCommand(y.Me, forge, y.Sim.Defs.Recipe("SmeltIron")));
        Assert.Equal("fire", y.Stage.Step);
        y.Drive();
        Assert.Equal("prices", y.Stage.Step);
        Assert.False(y.Stage.Done);

        var repair = Perception.For(y.Sim, y.Me).Prices(y.Run.Workplace).First(p => p.Kind == ServiceKind.Repair);
        y.Sim.ApplyNow(new SetPriceCommand(y.Me, y.Run.Workplace, ServiceKind.Repair, repair.Price + 2, true));
        Assert.True(y.Stage.Done);
    }
}
