using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Nights and evenings in a round: the night passing while every player sleeps, waking at dawn, supper at the inn.</summary>
public class NightTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bea = new("00000000-0000-0000-0000-00000000000b");

    private static Simulation Round(params (Guid Player, string Trade)[] players)
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        foreach (var (player, trade) in players)
        {
            sim.ApplyNow(new JoinLobbyCommand(player, player == Ana ? "Ana" : "Bea", default, []));
            sim.ApplyNow(new SetTradePreferencesCommand(player, [sim.Defs.Trade(trade)], []));
        }
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    /// <summary>Play on until the clock reads <paramref name="hour"/> (today, or tomorrow if it's past).</summary>
    private static void PlayUntilHour(Simulation sim, float hour, Action? each = null)
    {
        var target = sim.Clock.Tick + (long)(((hour - sim.Clock.HourOfDay + 24f) % 24f) * SimClock.TicksPerHour);
        while (sim.Clock.Tick < target)
        {
            sim.Tick();
            each?.Invoke();
        }
    }

    private static void SetRest(Simulation sim, Entity e, float rest) =>
        sim.World.Get<Needs>(e).Values[sim.Defs.Need("Rest").Index] = rest;

    [Fact]
    public void TheNightPassesOnlyWhenEveryPlayerSleeps()
    {
        var sim = Round((Ana, "Healer"), (Bea, "Blacksmith"));
        var ana = Players.CharacterOf(sim, Ana);
        var bea = Players.CharacterOf(sim, Bea);
        Assert.False(Players.AllAsleep(sim));

        sim.ApplyNow(new SleepCommand(ana));
        sim.TickMany(10);
        Assert.True(sim.World.Get<Mind>(ana).Asleep, sim.World.Get<Mind>(ana).Job?.Report ?? "no job");
        Assert.False(Players.AllAsleep(sim), "Bea's still up");

        sim.ApplyNow(new SleepCommand(bea));
        sim.TickMany(10);
        Assert.True(Players.AllAsleep(sim));

        // Bea gets up: the night stops passing.
        sim.ApplyNow(new AvatarGoToCommand(bea, sim.World.Get<Position>(bea).Cell + new int3(1, 0, 0)));
        sim.TickMany(10);
        Assert.False(Players.AllAsleep(sim));
    }

    [Fact]
    public void APlayerGetsUpAtDawnOnceRestedEnough()
    {
        var sim = Round((Ana, "Healer"));
        var ana = Players.CharacterOf(sim, Ana);
        PlayUntilHour(sim, 23f);
        SetRest(sim, ana, 0.05f);
        sim.ApplyNow(new SleepCommand(ana));
        PlayUntilHour(sim, SleepJob.DawnHour - 0.25f);
        Assert.True(sim.World.Get<Mind>(ana).Asleep, "still asleep before dawn");

        PlayUntilHour(sim, SleepJob.DawnHour + 0.25f);
        var rest = sim.World.Get<Needs>(ana).Get(sim.Defs.Need("Rest"));
        Assert.False(sim.World.Get<Mind>(ana).Asleep, $"up at dawn, rest {rest:0.00}");
        Assert.InRange(rest, SleepJob.DawnRest, 0.99f);
    }

    [Fact]
    public void ABotGoesToBedAtNightSoTheNightCanPass()
    {
        var sim = Round((Ana, "Healer"));
        var bot = new BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 5);
        var ana = Players.CharacterOf(sim, Ana);
        PlayUntilHour(sim, 22.5f);
        SetRest(sim, ana, 0.4f);
        for (var t = 0; t < SimClock.TicksPerHour && !sim.World.Get<Mind>(ana).Asleep; t++)
        {
            sim.Tick();
            bot.Tick();
        }
        Assert.True(sim.World.Get<Mind>(ana).Asleep, $"bot {bot.Doing}");
        Assert.True(Players.AllAsleep(sim));
    }

    [Fact]
    public void VillagersBuyTheirSupperAtTheInn()
    {
        var sim = Round((Ana, "Healer"));
        // Someone the AI runs who doesn't cook: the town's labourer, or the tinker.
        var w = sim.World;
        var villager = sim.Creatures().First(e => !w.Has<Avatar>(e) && w.TryGet<Trade>(e, out var t) && !t.Def.Serving(ServiceKind.Meal)
            && w.Get<Creature>(e).Faction == Faction.Village);
        PlayUntilHour(sim, ShopSystem.SupperFromHour);
        w.Get<Needs>(villager).Values[sim.Defs.Need("Food").Index] = 0.3f;
        var purse = Coin.Count(sim, villager); // the purse they were dealt, and the day's takings
        Assert.True(purse > 0);

        Order? Supper() => sim.Shops.All.FirstOrDefault(e => e.Kind.Kind == ServiceKind.Meal && e.Poster == villager);
        for (var i = 0; i < 16 && Supper() is not { Status: OrderStatus.Done }; i++)
            sim.TickMany(SimClock.TicksPerHour / 4);

        var supper = Supper();
        Assert.NotNull(supper);
        Assert.Equal(OrderStatus.Done, supper.Status);
        Assert.Equal(purse - supper.Price, Coin.Count(sim, villager));
    }
}
