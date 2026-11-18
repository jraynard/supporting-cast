using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The tinker's trip-bell: a raid's alarm waits till someone sees them, unless they cross a bell
/// set across their road, which rings it far out (or, binding, may jam and stay silent).
/// </summary>
public class TripBellTests
{
    private const int North = 2;

    private static Simulation Round(string trade = "Empath")
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Guid.Empty, "Arin", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Guid.Empty, [sim.Defs.Trade(trade)], []));
        sim.ApplyNow(new StartRoundCommand());
        sim.TickMany(SimClock.TicksPerHour / 6);
        return sim;
    }

    private static Entity Tinker(Simulation sim) =>
        sim.Creatures().First(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Tinker");

    /// <summary>A bell made and set on the road from the north by whoever's the tinker; <paramref name="binding"/> if a part went in crooked.</summary>
    private static Entity SetOnTheRoad(Simulation sim, int side, bool binding = false)
    {
        var tinker = Tinker(sim);
        var bell = sim.CreateItem(sim.Defs.Thing(TripBells.Def), 1);
        sim.World.Add(bell, new Contraption { Binding = binding, Maker = tinker });
        Inventory.Stow(sim, tinker, bell);
        var spot = TripBells.SpotFor(sim, side);
        Assert.NotNull(spot);
        Assert.True(TripBells.Set(sim, tinker, bell, spot.Value));
        Assert.True(TripBells.IsSet(sim, bell));
        return bell;
    }

    /// <summary>Tick until the raid's alarm goes up (or an hour and a bit).</summary>
    private static void UntilAlarmed(Simulation sim, RaidCrisis raid)
    {
        for (var t = 0; t < SimClock.HoursToTicks(1) + 120 && !raid.Alarmed; t += 10)
            sim.TickMany(10);
    }

    [Fact]
    public void ARaidIsKnownOnlyOnceSomeoneSeesThemAndThenTheTownMusters()
    {
        var sim = Round();
        var raid = sim.Director.SendRaid(sim, North)!;
        Assert.NotNull(raid);
        var w = sim.World;
        var seen = raid.Raiders.Any(r => sim.Creatures().Any(v => w.Get<Creature>(v) is { Faction: Faction.Village, Def.Race: Race.Human }
            && Maps.Map.Distance(w.Get<Position>(v).Cell, w.Get<Position>(r).Cell) <= RaidCrisis.LookoutReach));
        Assert.Equal(seen, raid.Alarmed);
        Assert.Equal(seen, sim.Militia.Mustering);

        UntilAlarmed(sim, raid);
        Assert.True(raid.Alarmed, "the town knew of the raid within the hour");
        Assert.True(raid.RungBy.IsNull);
        Assert.True(sim.Militia.Mustering);
        Assert.Contains(sim.Messages.Messages, m => m.Text.StartsWith("The bell rings out: raiders"));
    }

    [Fact]
    public void ABellAcrossTheirRoadRingsTheAlarmAsTheyCrossItAndShakesThem()
    {
        var sim = Round();
        var bell = SetOnTheRoad(sim, North);
        var raid = sim.Director.SendRaid(sim, North)!;
        UntilAlarmed(sim, raid);
        Assert.True(raid.Alarmed);
        Assert.Equal(bell, raid.RungBy);
        var set = sim.World.Get<TripSet>(bell);
        Assert.True(set.Sprung);
        Assert.False(set.Jammed);
        Assert.True(set.RangAt >= raid.Started);
        Assert.True(sim.Militia.Mustering);
        Assert.Contains(raid.Raiders, r => Afterlife.Hauntings.IsUnnerved(sim, r));
        Assert.Contains(sim.Messages.Messages, m => m.Text.StartsWith("A trip-bell rings out on the north road"));
    }

    [Fact]
    public void ABindingBellCanJamAndStaySilent()
    {
        // Half of binding bells jam: trip one bound bell after another until one does.
        var sim = Round();
        var raid = sim.Director.SendRaid(sim, North)!;
        var raider = raid.Raiders[0];
        var jammed = Entity.Null;
        for (var i = 0; i < 30 && jammed.IsNull; i++)
        {
            var bell = sim.CreateItem(sim.Defs.Thing(TripBells.Def), 1);
            sim.World.Add(bell, new Contraption { Binding = true, Maker = Tinker(sim) });
            sim.PlaceItemNear(bell, sim.World.Get<Position>(raider).Cell);
            sim.World.Add(bell, new TripSet { By = Tinker(sim), RangAt = -1 });
            TripBells.Trip(sim, bell, raider);
            if (sim.World.Get<TripSet>(bell).Jammed)
                jammed = bell;
        }
        Assert.False(jammed.IsNull, "no bound bell jammed in thirty");
        var set = sim.World.Get<TripSet>(jammed);
        Assert.True(set.Sprung);
        Assert.Equal(-1, set.RangAt);
        Assert.NotEqual(jammed, raid.RungBy);
    }

    [Fact]
    public void ASetBellIsLeftByTheHaulersWoundAgainAndTakenUp()
    {
        var sim = Round();
        var bell = SetOnTheRoad(sim, North);
        Assert.False(sim.IsAvailable(bell));
        var tinker = Tinker(sim);
        sim.World.Get<TripSet>(bell).Sprung = true;
        Assert.Equal("Wind the tripped trip-bell again", TripBells.Prompt(sim, bell));
        Assert.True(TripBells.Rewind(sim, tinker, bell));
        Assert.False(sim.World.Get<TripSet>(bell).Sprung);
        Assert.False(TripBells.Rewind(sim, tinker, bell));
        Assert.True(TripBells.TakeUp(sim, tinker, bell));
        Assert.False(TripBells.IsSet(sim, bell));
        Assert.True(Inventory.OnPerson(sim, tinker, bell));
    }

    [Fact]
    public void BellsGoOnlyOutOfDoorsAndNotTwoToARoad()
    {
        var sim = Round();
        var bell = SetOnTheRoad(sim, North);
        var at = sim.World.Get<Thing>(bell).Cell;
        Assert.Equal("there's a trip-bell set close by", TripBells.SetProblem(sim, at + new int3(1, 0, 0)));
        var shop = Shops.ShopSystem.WorkplaceOf(sim, sim.Defs.Trade("Tinker"));
        var bench = Systems.Stock.StationOf(sim, shop, Tinkers.BenchDef);
        var indoors = sim.SpiralFrom(sim.World.Get<Thing>(bench).Cell, 3).First(c => sim.Map.Passable(c) && sim.Map.Standable(sim.Map.Idx(c)));
        Assert.NotNull(TripBells.SetProblem(sim, indoors));
    }

    [Fact]
    public void TheTownsBellsArentSoldToParties()
    {
        var def = TestWorld.LoadDefs().Thing(TripBells.Def);
        Assert.False(def.Contraption!.ForTheRoad);
        Assert.False(Shops.Goods.IsGear(def));
        Assert.True(TestWorld.LoadDefs().Thing("TappingRod").Contraption!.ForTheRoad);
    }

    [Fact]
    public void ASetBellAndARaidNotYetSeenSurviveASave()
    {
        var sim = Round();
        var bell = SetOnTheRoad(sim, North);
        sim.World.Get<TripSet>(bell).Sprung = true;
        var raid = sim.Director.SendRaid(sim, North)!;
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var again = Assert.IsType<RaidCrisis>(Assert.Single(loaded.Director.Active));
        Assert.Equal(raid.Alarmed, again.Alarmed);
        var set = loaded.World.Get<TripSet>(bell);
        Assert.True(set.Sprung);
        Assert.Equal(sim.World.Get<TripSet>(bell).By, set.By);
        Assert.Equal(TripBells.All(sim), TripBells.All(loaded));
    }

    [Fact]
    public void AnAITinkerSetsABellOnTheRoadARaidsBeenWarnedOf()
    {
        var sim = Round();
        sim.ApplyNow(new DevRaidWarningCommand());
        Assert.True(sim.Militia.Open);
        var side = sim.Director.WarnedSide;
        Assert.NotNull(side);
        var spot = TripBells.SpotFor(sim, side.Value);
        Assert.NotNull(spot);
        for (var i = 0; i < 4 * 6 && !TripBells.Guarded(sim, spot.Value); i++)
            sim.TickMany(SimClock.TicksPerHour / 6);
        Assert.True(TripBells.Guarded(sim, spot.Value),
            "no bell on the warned road\n" + string.Join("\n", sim.Messages.Messages.TakeLast(10).Select(m => m.Text)));
        var bell = Assert.Single(TripBells.All(sim));
        Assert.Equal(Tinker(sim), sim.World.Get<TripSet>(bell).By);
    }

    [Fact]
    public void ABotTinkerFitsABellAndSetsItOnTheWarnedRoad()
    {
        var ana = Guid.NewGuid();
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(ana, [sim.Defs.Trade("Tinker")], []));
        var bot = new BotPlayer(() => sim, ana, c => sim.ApplyNow(c), 7) { Temper = BotTemperament.Steady };
        sim.ApplyNow(new StartRoundCommand());
        var me = Players.CharacterOf(sim, ana);
        sim.ApplyNow(new DevRaidWarningCommand());
        var spot = TripBells.SpotFor(sim, sim.Director.WarnedSide!.Value)!.Value;
        for (var t = 0; t < SimClock.HoursToTicks(4) && !TripBells.Guarded(sim, spot); t++)
        {
            sim.Tick();
            bot.Tick();
        }
        Assert.True(TripBells.Guarded(sim, spot), $"the bot was {bot.Doing}");
        Assert.Equal(me, sim.World.Get<TripSet>(Assert.Single(TripBells.All(sim))).By);
    }
}
