using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The inn's rooms upstairs: locked doors, keys on the board, and the right key to the right guest.</summary>
public class InnKeysTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static (Simulation Sim, List<Entity> Party, Entity Player) Tavern(string playerTrade)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(playerTrade)], []));
        sim.ApplyNow(new StartRoundCommand());
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        var player = sim.World.Query<PlayerCharacter>().First();
        return (sim, party, player);
    }

    private static Entity Inn(Simulation sim) => ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));

    private static List<Entity> Rooms(Simulation sim) =>
        sim.World.Query<GuestRoom>().OrderBy(e => sim.World.Get<GuestRoom>(e).Number).ToList();

    private static void Put(Simulation sim, Entity who, int3 cell)
    {
        sim.World.Get<Position>(who).Cell = cell;
        sim.World.Get<Position>(who).Pos = Position.CenterOf(cell);
    }

    private static Entity Key(Simulation sim, int room) =>
        InnRooms.OnBoard(sim, Inn(sim), InnRooms.SeedOf(sim, Rooms(sim).First(b => InnRooms.Number(sim, b) == room)));

    /// <summary>Asks for a room now, at the inn, and returns the order.</summary>
    private static Order AskForARoom(Simulation sim, Entity guest)
    {
        var def = sim.Defs.Service(ServiceKind.Lodging);
        Inventory.Stow(sim, guest, sim.CreateItem(sim.Thing(Coin.Silver), 20));
        return sim.Shops.Post(sim, guest, def, "A room.", 4, sim.Clock.Tick + SimClock.HoursToTicks(4), Entity.Null, Inn(sim), 9);
    }

    /// <summary>Hands a room key straight from the board to a guest, as a player would.</summary>
    private static void Hand(Simulation sim, Entity keeper, Entity guest, int room)
    {
        var key = sim.TakeFromStack(Key(sim, room), 1);
        Inventory.Stow(sim, keeper, key);
        Put(sim, keeper, sim.World.Get<Position>(guest).Cell + new int3(1, 0, 0));
        sim.ApplyNow(new GiveCommand(keeper, guest, key, 1));
    }

    [Fact]
    public void EachRoomIsLockedWithItsKeyOnTheBoardAndTheKeeperHoldsASet()
    {
        var (sim, _, player) = Tavern("Innkeeper");
        var w = sim.World;
        var rooms = Rooms(sim);
        Assert.Equal(6, rooms.Select(b => InnRooms.Number(sim, b)).Distinct().Count());
        foreach (var bed in rooms)
        {
            var door = w.Get<GuestRoom>(bed).Door;
            Assert.True(sim.IsLocked(door), $"room {InnRooms.Number(sim, bed)}'s door is locked");
            Assert.False(InnRooms.OnBoard(sim, Inn(sim), InnRooms.SeedOf(sim, bed)).IsNull, "its key hangs on the board");
            Assert.False(InnRooms.KeyOn(sim, player, bed).IsNull, "the keeper has their own key to it");
        }
        Assert.Equal("key to room 3", InnRooms.KeyLabel(sim, Key(sim, 3)));
    }

    [Fact]
    public void TheBoardTakesOnlyKeys()
    {
        var (sim, _, _) = Tavern("Innkeeper");
        var board = InnRooms.Board(sim, Inn(sim));
        Assert.Equal(0, sim.ContainerRoom(board, sim.Thing(Coin.Silver), null));
        Assert.True(sim.ContainerRoom(board, sim.Thing(Locksmithing.KeyDef), null) > 0);
    }

    [Fact]
    public void AGuestHandedAKeyHasThatRoomAndPays()
    {
        var (sim, party, player) = Tavern("Innkeeper");
        var guest = party[0];
        var order = AskForARoom(sim, guest);
        sim.Shops.Accept(sim, player, order.Id);
        var silver = Coin.Count(sim, player);
        Hand(sim, player, guest, 2);
        Assert.False(order.IsOpen);
        Assert.True(Lodging.Lodged(sim, guest));
        Assert.Equal(2, InnRooms.Number(sim, sim.World.Get<LodgeStay>(guest).Bed));
        Assert.True(Coin.Count(sim, player) > silver, "paid for it");
        Assert.True(InnRooms.Board(sim, Inn(sim)) is var board && InnRooms.OnBoard(sim, Inn(sim), InnRooms.SeedOf(sim, sim.World.Get<LodgeStay>(guest).Bed)).IsNull,
            "its key's off the board");
    }

    [Fact]
    public void TheWrongKeyWalksThemInOnSomeoneAndTheyComeBackForAnother()
    {
        var (sim, party, player) = Tavern("Innkeeper");
        var w = sim.World;
        var (first, second) = (party[0], party[1]);
        var bed = Rooms(sim).First(b => InnRooms.Number(sim, b) == 1);
        Lodging.Let(sim, first, bed);
        var spare = Locksmithing.MakeKey(sim, InnRooms.SeedOf(sim, bed), InnRooms.RoomPins, 1f); // a copy of room 1's
        var mood = w.Get<Adventurer>(first).Satisfaction;
        var order = AskForARoom(sim, second);
        sim.Shops.Accept(sim, player, order.Id);
        Inventory.Stow(sim, player, spare);
        Put(sim, player, w.Get<Position>(second).Cell + new int3(1, 0, 0));
        sim.ApplyNow(new GiveCommand(player, second, spare, 1));
        Assert.False(order.IsOpen, "they paid for it");
        Assert.True(w.Has<WalkIn>(second), "they're off up to room 1");
        Assert.False(Lodging.Lodged(sim, second));

        Lodging.WalkedIn(sim, second, bed);
        Assert.True(w.Has<OwedRoom>(second));
        Assert.True(w.Get<Adventurer>(first).Satisfaction < mood, "the one walked in on minds");
        sim.TickMany(SimClock.TicksPerHour / 4);
        var again = sim.Shops.All.FirstOrDefault(e => e.IsOpen && e.Poster == second && e.Kind.Kind == ServiceKind.Lodging);
        Assert.NotNull(again);
        Assert.Equal(0, again!.Price);

        sim.Shops.Accept(sim, player, again.Id);
        Hand(sim, player, second, 4);
        Assert.True(Lodging.Lodged(sim, second));
        Assert.Equal(4, InnRooms.Number(sim, w.Get<LodgeStay>(second).Bed));
        Assert.True(InnRooms.KeyOn(sim, second, bed).IsNull, "they gave the wrong key back");
        Assert.False(w.Has<OwedRoom>(second));
    }

    [Fact]
    public void AnAiKeeperLetsARoomByItsKey()
    {
        var (sim, party, _) = Tavern("Healer");
        var guest = party[0];
        var keeper = sim.World.Query<Trade>().First(e => sim.World.Get<Trade>(e).Def.Name == "Innkeeper");
        foreach (var a in party)
            sim.World.Get<Adventurer>(a).LeaveTick = sim.Clock.Tick + SimClock.TicksPerDay * 3;
        var order = AskForARoom(sim, guest);
        for (var i = 0; i < 40 && order.IsOpen; i++)
            sim.TickMany(SimClock.TicksPerHour / 8);
        Assert.False(order.IsOpen, "the room was let");
        Assert.True(Lodging.Lodged(sim, guest));
        var bed = sim.World.Get<LodgeStay>(guest).Bed;
        Assert.False(InnRooms.KeyOn(sim, guest, bed).IsNull, "they hold its key");
        Assert.NotEqual(Entity.Null, keeper);
    }

    [Fact]
    public void LeavingAGuestHandsTheKeyInOrLeavesItOrTakesIt()
    {
        var outcomes = new HashSet<string>();
        for (uint seed = 1; seed <= 40 && outcomes.Count < 3; seed++)
        {
            var (sim, party, _) = Tavern("Healer");
            sim.Rand.State = new SimRandom(seed * 7919u).State;
            var guest = party[0];
            var bed = Rooms(sim).First(b => InnRooms.Number(sim, b) == 5);
            Lodging.Let(sim, guest, bed);
            Inventory.Stow(sim, guest, sim.TakeFromStack(InnRooms.OnBoard(sim, Inn(sim), InnRooms.SeedOf(sim, bed)), 1));
            Lodging.CheckOut(sim, guest);
            var w = sim.World;
            if (w.Has<ReturningKey>(guest))
                outcomes.Add("returning");
            else if (!InnRooms.KeyOn(sim, guest, bed).IsNull)
                outcomes.Add("taken");
            else if (w.Query<Key>().Any(k => InnRooms.LooseKey(sim, k)))
                outcomes.Add("left");
            Assert.True(Lodging.IsUnmade(sim, bed));
        }
        Assert.True(outcomes.Count == 3, string.Join(",", outcomes));
    }

    [Fact]
    public void AKeyHandedInOnTheCounterGoesBackOnTheBoard()
    {
        var (sim, party, _) = Tavern("Healer");
        var guest = party[0];
        var bed = Rooms(sim).First(b => InnRooms.Number(sim, b) == 3);
        var seed = InnRooms.SeedOf(sim, bed);
        var key = sim.TakeFromStack(InnRooms.OnBoard(sim, Inn(sim), seed), 1);
        sim.PlaceItemNear(key, InnRooms.HandInAt(sim, Inn(sim))!.Value, byHand: true);
        for (var i = 0; i < 40 && InnRooms.OnBoard(sim, Inn(sim), seed).IsNull; i++)
            sim.TickMany(SimClock.TicksPerHour / 8);
        Assert.False(InnRooms.OnBoard(sim, Inn(sim), seed).IsNull, "the keeper hung it up");
        Assert.NotEqual(Entity.Null, guest);
    }

    [Fact]
    public void RoomsAndWalkInsSurviveASave()
    {
        var (sim, party, _) = Tavern("Innkeeper");
        var bed = Rooms(sim)[0];
        sim.World.Add(party[1], new WalkIn { Bed = bed });
        sim.World.Add(party[2], new OwedRoom { Inn = Inn(sim), WrongBed = bed });
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(InnRooms.Number(sim, bed), InnRooms.Number(loaded, bed));
        Assert.Equal(InnRooms.SeedOf(sim, bed), InnRooms.SeedOf(loaded, bed));
        Assert.Equal(bed, loaded.World.Get<WalkIn>(party[1]).Bed);
        Assert.Equal(bed, loaded.World.Get<OwedRoom>(party[2]).WrongBed);
    }
}
