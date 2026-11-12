using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Rooms by the night, beds to make up, and the mess on the floor.</summary>
public class InnRoomsTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static (Simulation Sim, List<Entity> Party) Tavern(string playerTrade)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(playerTrade)], []));
        sim.ApplyNow(new StartRoundCommand());
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        return (sim, party);
    }

    private static Entity Inn(Simulation sim) => ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));

    private static List<Entity> Beds(Simulation sim) =>
        Buildings.Parts(sim, Inn(sim)).Where(p => sim.World.Get<Thing>(p).Def.Building is { IsBed: true }).ToList();

    /// <summary>A cell of the inn's ground floor by its plan's column and row (the inn stands turned on the map).</summary>
    private static int3 Plan(Simulation sim, int col, int row)
    {
        var b = sim.World.Get<PresetBuilding>(Inn(sim));
        return Buildings.CellOf(sim.Map, b.Def, b.Anchor, b.Facing, col, row);
    }

    /// <summary>A cell on the inn's ground floor, in the common room by the door.</summary>
    private static int3 CommonRoom(Simulation sim) => Plan(sim, 6, 10);

    private static void Put(Simulation sim, Entity who, int3 cell)
    {
        sim.World.Get<Position>(who).Cell = cell;
        sim.World.Get<Position>(who).Pos = Position.CenterOf(cell);
    }

    private static void TickTill(Simulation sim, int day, float hour)
    {
        while (sim.Clock.Day < day || (sim.Clock.Day == day && sim.Clock.HourOfDay < hour))
            sim.TickMany(SimClock.TicksPerHour / 4);
    }

    [Fact]
    public void AnUnpaidNightEndsTheStayAndLeavesTheBedUnmade()
    {
        var (sim, party) = Tavern("Innkeeper"); // the keeper's a player who never lets them another night
        var guest = party[0];
        foreach (var a in party)
            sim.World.Get<Adventurer>(a).LeaveTick = sim.Clock.Tick + SimClock.TicksPerDay * 4;
        var bed = Beds(sim)[0];
        TickTill(sim, 1, 20);
        Lodging.Let(sim, guest, bed);
        Assert.True(Lodging.Lodged(sim, guest));

        TickTill(sim, 2, 20);
        Assert.Equal(bed, Lodging.Kept(sim, guest)); // the room's still theirs while they're in town
        TickTill(sim, 3, Lodging.LapsedHour + 0.5f);
        Assert.False(sim.World.Has<LodgeStay>(guest));
        Assert.False(sim.World.TryGet<BedOwner>(guest, out var own) && own.Bed == bed);
        Assert.True(Lodging.IsUnmade(sim, bed));
    }

    [Fact]
    public void AGuestStayingOnPaysForAnotherNightAndKeepsTheirRoom()
    {
        var (sim, party) = Tavern("Healer");
        // Whole, so nobody bleeds out untreated (the player here is the healer, and idle) before the
        // second night; and no quest on offer to take them off the map.
        var inn = Inn(sim);
        if (!sim.World.Has<ShopPrices>(inn))
            sim.World.Add(inn, ShopPrices.Defaults(sim.Defs));
        sim.World.Get<ShopPrices>(inn).Offered[(int)ServiceKind.Quest] = false;
        foreach (var a in party)
        {
            sim.World.Get<Adventurer>(a).LeaveTick = sim.Clock.Tick + SimClock.TicksPerDay * 3;
            sim.World.Get<Health>(a).Injuries.Clear();
        }
        var guest = party[0];
        Inventory.Stow(sim, guest, sim.CreateItem(sim.Thing(Coin.Silver), 60)); // enough for the stay, whatever else they buy
        bool Paid(int night) => sim.World.TryGet<LodgeStay>(guest, out var s) && s.Night >= night;
        TickTill(sim, 1, ShopSystem.LodgingFromHour);
        for (var i = 0; i < 24 && !Paid(1); i++)
            sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.True(Paid(1), "a room the first night");
        var bed = sim.World.Get<LodgeStay>(guest).Bed;

        TickTill(sim, 2, ShopSystem.LodgingFromHour);
        for (var i = 0; i < 24 && !Paid(2); i++)
            sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.True(Paid(2), "paid again the second night");
        Assert.Equal(bed, sim.World.Get<LodgeStay>(guest).Bed);
    }

    [Fact]
    public void AnUnmadeBedIsLetOnlyWhenThereIsNoOther()
    {
        var (sim, _) = Tavern("Innkeeper");
        var beds = Beds(sim);
        foreach (var b in beds.Skip(1))
            sim.World.Add(b, new Unmade());
        Assert.Equal(beds[0], ShopSystem.FreeLetBed(sim, Inn(sim)));
        sim.World.Add(beds[0], new Unmade());
        Assert.True(Lodging.IsUnmade(sim, ShopSystem.FreeLetBed(sim, Inn(sim))));
    }

    [Fact]
    public void ABedIsMadeUpOnCue()
    {
        var (sim, _) = Tavern("Innkeeper");
        var me = Players.CharacterOf(sim, Ana);
        var bed = Beds(sim)[0];
        sim.World.Add(bed, new Unmade());
        var at = sim.World.Get<Thing>(bed).Cell;
        Put(sim, me, sim.SpiralFrom(at, 2).First(c => c != at && c.Z == at.Z && sim.Map.Passable(c) && AvatarSystem.InReach(sim, c, bed)));
        Assert.Equal("Make the bed", AvatarSystem.ActionAt(sim, me, at).Label);
        sim.ApplyNow(new BarWorkCommand(me, bed));
        var predictor = new WorkPredictor();
        var was = false;
        for (var t = 0; t < 60 * 30 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var press = false;
            if (predictor.Session is MakeBedSession s)
            {
                press = s.OnCue && !was;
                was = s.OnCue;
            }
            predictor.Update(sim, me, 1, press, false, 0, c => sim.ApplyNow(c));
        }
        sim.Tick();
        Assert.False(Lodging.IsUnmade(sim, bed));
    }

    // ---------------- Mess ----------------

    [Fact]
    public void ASpilledPourLeavesAPuddle()
    {
        var sim = Create(40, 40);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Inna", C(10, 10), ageDays: 25 * 365);
        sim.Round.OpenLobby();
        sim.Round.Sit(Ana, "Inna", null, null, null);
        sim.Round.Start(sim);
        var cask = sim.SpawnStructure(sim.Thing("AleCask"), C(11, 10));
        sim.RebuildDerivedIfNeeded();
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("Tankard"), 1);
        PourSession.TryStart(sim, me, cask);
        var s = (PourSession)sim.World.Get<WorkSession>(me);
        s.Advance([new WorkInput(0, WorkInputKind.Hold, 1)], 2000);
        sim.Tick();
        Assert.Equal(MessKind.SpilledAle, sim.Mess.At(sim, C(10, 10))?.Kind);
        Assert.Equal("Mop up the spilled ale", AvatarSystem.ActionAt(sim, me, C(10, 10)).Label);
    }

    [Fact]
    public void AMessyRoomSoursCustomersUntilItsMopped()
    {
        var (sim, party) = Tavern("Innkeeper");
        var me = Players.CharacterOf(sim, Ana);
        var room = CommonRoom(sim);
        var drinker = party[0];
        // Nothing else to please or annoy them for the hour: whole, fed and not thirsty.
        foreach (var a in party)
        {
            sim.World.Get<Health>(a).Injuries.Clear();
            sim.World.Get<Adventurer>(a).NextDrink = sim.Clock.Tick + SimClock.TicksPerDay;
            sim.World.Get<Needs>(a).Values[sim.Defs.Need("Food").Index] = 1f;
        }
        foreach (var col in new[] { 5, 7, 8 })
            sim.Mess.Spill(sim, MessKind.SpilledAle, Plan(sim, col, 11));
        var before = sim.World.Get<Adventurer>(drinker).Satisfaction;
        for (var t = 0; t < SimClock.TicksPerHour + 10; t++)
        {
            Put(sim, drinker, room); // stood at the bar all the while
            sim.Tick();
        }
        Assert.True(sim.World.Get<Adventurer>(drinker).Satisfaction < before, $"they noticed: {before} -> {sim.World.Get<Adventurer>(drinker).Satisfaction}");

        // Mop one up, as a player: back and forth until it's gone.
        var spot = Plan(sim, 5, 11);
        Put(sim, me, Plan(sim, 5, 10));
        Toolbelt.PutOn(sim, me, sim.CreateItem(sim.Thing("Mop"), 1));
        sim.ApplyNow(new ScrubCommand(me, spot));
        var scrub = (ScrubSession)sim.World.Get<WorkSession>(me);
        Assert.True(scrub.Mop);
        scrub.Advance(Enumerable.Range(0, 20).Select(i => new WorkInput(i * 4, WorkInputKind.Axis, i % 2 == 0 ? 1 : -1)).ToList(), 90);
        sim.Tick();
        Assert.Null(sim.Mess.At(sim, spot));
    }

    [Fact]
    public void BloodIsADarkStainAndScrubsAwayWithoutATrace()
    {
        var (sim, _) = Tavern("Innkeeper");
        var me = Players.CharacterOf(sim, Ana);
        var room = CommonRoom(sim);
        sim.Mess.Spill(sim, MessKind.Blood, room);
        Put(sim, me, Plan(sim, 6, 9));
        var seen = Perception.For(sim, me).Messes().Single(m => m.Cell == room);
        Assert.Equal(MessLook.Stain, seen.Look);
        Assert.Equal("dark stain", seen.Label);
        sim.ApplyNow(new ScrubCommand(me, room));
        var scrub = (ScrubSession)sim.World.Get<WorkSession>(me);
        scrub.Advance(Enumerable.Range(0, 40).Select(i => new WorkInput(i * 4, WorkInputKind.Axis, i % 2 == 0 ? 1 : -1)).ToList(), 170);
        sim.Tick();
        Assert.Null(sim.Mess.At(sim, room));
    }

    [Fact]
    public void OutdoorsMessWashesAwayByItself()
    {
        var sim = Create(40, 40);
        sim.Mess.Spill(sim, MessKind.Mud, C(10, 10));
        sim.TickMany(SimClock.HoursToTicks(MessSystem.OutdoorHours + 1));
        Assert.Null(sim.Mess.At(sim, C(10, 10)));
    }

    [Fact]
    public void AnAIInnkeeperMopsUpAndMakesTheBeds()
    {
        var (sim, _) = Tavern("Healer");
        var room = CommonRoom(sim);
        sim.Mess.Spill(sim, MessKind.Mud, room);
        sim.Mess.Spill(sim, MessKind.SpilledAle, Plan(sim, 7, 9));
        var bed = Beds(sim)[0];
        sim.World.Add(bed, new Unmade());
        for (var i = 0; i < 24 && (sim.Mess.At(sim, room) != null || sim.Mess.At(sim, Plan(sim, 7, 9)) != null || Lodging.IsUnmade(sim, bed)); i++)
            sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.Null(sim.Mess.At(sim, room));
        Assert.Null(sim.Mess.At(sim, Plan(sim, 7, 9)));
        Assert.False(Lodging.IsUnmade(sim, bed));
    }

    [Fact]
    public void TheInnkeeperStartsWithAMop()
    {
        var (sim, _) = Tavern("Innkeeper");
        Assert.False(Toolbelt.Best(sim, Players.CharacterOf(sim, Ana), "Mop").IsNull);
    }

    [Fact]
    public void MessAndStaysSurviveASave()
    {
        var (sim, party) = Tavern("Innkeeper");
        sim.Mess.Spill(sim, MessKind.Crumbs, CommonRoom(sim), 0.5f);
        var bed = Beds(sim)[1];
        Lodging.Let(sim, party[0], bed);
        sim.World.Add(Beds(sim)[2], new Unmade());

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(sim.Mess.At(sim, CommonRoom(sim)), loaded.Mess.At(loaded, CommonRoom(sim)));
        Assert.Equal(bed, loaded.World.Get<LodgeStay>(party[0]).Bed);
        Assert.True(Lodging.IsUnmade(loaded, Beds(sim)[2]));
    }
}
