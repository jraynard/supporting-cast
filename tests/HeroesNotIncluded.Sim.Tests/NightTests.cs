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

    private static Entity Sleeping(Simulation sim, Guid player)
    {
        var e = Players.CharacterOf(sim, player);
        sim.ApplyNow(new SleepCommand(e));
        sim.TickMany(10);
        Assert.True(sim.World.Get<Mind>(e).Asleep, sim.World.Get<Mind>(e).Job?.Report ?? "no job");
        return e;
    }

    [Fact]
    public void AShoutNearbyWakesASleepingPlayer()
    {
        var sim = Round((Ana, "Healer"), (Bea, "Blacksmith"));
        var ana = Sleeping(sim, Ana);
        var bea = Players.CharacterOf(sim, Bea);
        sim.World.Get<Position>(bea) = Position.At(sim.World.Get<Position>(ana).Cell);
        sim.Speech.Say(sim, bea, "Psst", Speech.Volume.Whisper);
        Assert.True(sim.World.Get<Mind>(ana).Asleep, "a whisper doesn't wake anyone");
        var heard = sim.Speech.Say(sim, bea, "Fire at the mill!", Speech.Volume.Shout)!;
        Assert.False(sim.World.Get<Mind>(ana).Asleep);
        Assert.Contains(heard.Listeners, h => h.Listener == ana);
    }

    [Fact]
    public void TheCallToArmsWakesEverySleepingPlayer()
    {
        var sim = Round((Ana, "Healer"));
        var ana = Sleeping(sim, Ana);
        sim.Militia.Call(sim, "tonight");
        Assert.False(sim.World.Get<Mind>(ana).Asleep);
    }

    [Fact]
    public void FireNearbyWakesASleepingPlayer()
    {
        var sim = Round((Ana, "Healer"));
        var ana = Sleeping(sim, Ana);
        var at = sim.World.Get<Position>(ana).Cell;
        Assert.True(sim.Fire.Ignite(sim, at, 0.6f) || sim.Fire.Ignite(sim, at + new int3(1, 0, 0), 0.6f) || sim.Fire.Ignite(sim, at + new int3(0, 1, 0), 0.6f));
        sim.TickMany(61);
        Assert.False(sim.World.Get<Mind>(ana).Asleep);
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

    [Fact]
    public void EveryoneIsDealtAHomeOfTheirOwnNearTheirWork()
    {
        var sim = Round((Ana, "Healer"));
        var w = sim.World;
        var town = sim.Creatures().Where(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human }).ToList();
        var homes = town.Select(e => Homes.HomeOf(sim, e)).ToList();
        Assert.All(homes, h => Assert.True(!h.IsNull && w.Get<PresetBuilding>(h).Def.Home, "a cottage each"));
        Assert.Equal(town.Count, homes.Distinct().Count()); // seven cottages for six: one each

        // Nobody else lies down in a bed that's someone's.
        var other = town.First(e => !w.Has<Avatar>(e));
        foreach (var e in town.Where(e => e != other))
            Assert.NotEqual(w.Get<BedOwner>(e).Bed, Ai.AiQueries.FindFreeBed(sim, other));
        Assert.Equal(w.Get<BedOwner>(other).Bed, Ai.AiQueries.FindFreeBed(sim, other));
    }

    [Fact]
    public void InnStaffWithNoCottageLeftGetARoomThatsNeverLet()
    {
        var sim = Round((Ana, "Healer"));
        var w = sim.World;
        var cook = sim.Creatures().Single(e => w.TryGet<Trade>(e, out var t) && t.Def.Name == "Cook");
        // Every cottage bed spoken for by somebody else.
        w.Remove<BedOwner>(cook);
        foreach (var b in w.Query<PresetBuilding>().Where(b => w.Get<PresetBuilding>(b).Def.Home).ToList())
            foreach (var bed in Buildings.Parts(sim, b).Where(p => w.Get<Thing>(p).Def.Building is { IsBed: true }))
                if (!w.Query<BedOwner>().Any(o => w.Get<BedOwner>(o).Bed == bed))
                {
                    var lodger = sim.SpawnCreature(sim.Defs.Creature("Human"), "Lodger", w.Get<Thing>(bed).Cell);
                    w.Add(lodger, new BedOwner { Bed = bed });
                }
        Assert.True(Homes.Give(sim, cook));
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        Assert.Equal(inn, Homes.HomeOf(sim, cook));
        // That room isn't let to a guest.
        var room = w.Get<BedOwner>(cook).Bed;
        for (var guests = 0; guests < 12; guests++)
            Assert.NotEqual(room, ShopSystem.FreeLetBed(sim, inn));
    }
}
