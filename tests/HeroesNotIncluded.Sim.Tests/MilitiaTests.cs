using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Militia;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Word of a raid ahead of it, the call to arms, the town purse and the muster rack.</summary>
public class MilitiaTests
{
    private static Simulation Round(bool director = true)
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        if (director)
            sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Guid.Empty, "Arin", default, []));
        sim.ApplyNow(new StartRoundCommand());
        sim.TickMany(SimClock.TicksPerHour / 4); // the beats are planned, the purse filled
        return sim;
    }

    private static Entity Rack(Simulation sim) => sim.World.Query<Thing>().Single(e => MilitiaSystem.IsRack(sim, e));

    private static Entity Me(Simulation sim) => sim.World.Query<Avatar>().Single();

    private static Entity Spear(Simulation sim, Entity maker)
    {
        var spear = sim.CreateItem(sim.Defs.Thing("IronSpear"), 1);
        sim.World.Add(spear, new Crafted { Quality = Quality.Plain, Maker = maker });
        return spear;
    }

    private static List<Entity> Raiders(Simulation sim) =>
        sim.Creatures().Where(e => sim.World.Get<Creature>(e) is { Faction: Faction.Raider, Dead: false }).ToList();

    private static bool Armed(Simulation sim, Entity e) => sim.World.TryGet<Equipment>(e, out var eq) && sim.World.IsAlive(eq.Weapon);

    [Fact]
    public void WordOfTheFirstRaidComesHoursAheadAndItComesAsTold()
    {
        var sim = Round();
        var beat = sim.Defs.Season.Beats[0];
        var hours = sim.Defs.Season.RaidWarningHours;
        sim.Clock.Tick = sim.Round.TickAt(sim, beat.Day, beat.FromHour - hours - 1);
        while (sim.Director.Coming == null && sim.Clock.Tick < sim.Round.TickAt(sim, beat.Day, beat.ToHour))
            sim.TickMany(SimClock.TicksPerHour / 6);
        var plan = Assert.IsType<PlannedRaid>(sim.Director.Coming);
        var warned = sim.Clock.Tick;
        Assert.Contains(sim.Messages.Messages, m => m.Text.StartsWith("A shepherd runs in") && m.Text.Contains(Raids.SideName(plan.Side)));
        Assert.True(sim.Militia.Open);
        Assert.False(sim.Militia.Mustering, "the town goes about its day till they come");
        Assert.Empty(Raiders(sim));

        // It keeps through a save.
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(plan.Kinds.Select(k => k.Name), loaded.Director.Coming!.Kinds.Select(k => k.Name));
        Assert.True(loaded.Militia.Open);
        Assert.False(loaded.Militia.Mustering);
        Assert.Equal(sim.Militia.Wanted, loaded.Militia.Wanted);
        sim = loaded;

        while (sim.Director.Active.Count == 0 && sim.Clock.Tick < sim.Round.TickAt(sim, beat.Day, beat.ToHour) + SimClock.TicksPerHour)
            sim.TickMany(SimClock.TicksPerHour / 6);
        var raid = Assert.IsType<RaidCrisis>(Assert.Single(sim.Director.Active));
        Assert.True(sim.Clock.Tick - warned >= SimClock.HoursToTicks(hours) - SimClock.TicksPerHour / 3, "the warning came hours ahead");
        Assert.Equal(plan.Kinds.Count, raid.Raiders.Count);
        Assert.True(sim.Militia.Mustering, "the bandits are here: the town takes up arms");
        Assert.Contains(sim.Messages.Messages, m => m.Text.StartsWith("The bell rings out") && m.Location == null);

        // Over, and the muster stands down.
        foreach (var e in raid.Raiders)
            NeedsSystem.Kill(sim, e, ref sim.World.Get<Creature>(e));
        sim.TickMany(SimClock.TicksPerHour / 6 + 1);
        Assert.Empty(sim.Director.Active);
        Assert.False(sim.Militia.Open);
        Assert.False(sim.Militia.Mustering);
    }

    [Fact]
    public void TheTownPaysOnceForEachNewWeaponWhileItWantsThem()
    {
        var sim = Round(director: false);
        var me = Me(sim);
        var rack = Rack(sim);
        sim.Militia.Call(sim, "tonight");
        var wanted = sim.Militia.Wanted;
        Assert.True(wanted >= 2, $"{wanted} wanted");
        var price = sim.Defs.Season.ArmsPrice;
        var purse = sim.Militia.Coffer;
        var silver = Coin.Count(sim, me);

        // Hung on the rack from the pack (the work key does the same from the hands).
        sim.World.Get<Position>(me).Cell = sim.SpiralFrom(sim.World.Get<Thing>(rack).Cell, 2)
            .First(c => sim.Map.Passable(c) && AvatarSystem.InReach(c, sim.World.Get<Thing>(rack).Cell));
        var spear = Spear(sim, me);
        Inventory.Stow(sim, me, spear);
        sim.ApplyNow(new StoreFromPackCommand(me, rack, spear));
        Assert.Equal(rack, sim.World.Get<Stored>(spear).In);
        Assert.True(sim.World.Has<TownArms>(spear));
        Assert.Equal(silver + price, Coin.Count(sim, me));
        Assert.Equal(purse - price, sim.Militia.Coffer);

        // Taken down (the raid's come) and hung up again: nothing more.
        sim.Militia.Muster(sim);
        sim.ApplyNow(new TakeFromStorageCommand(me, spear));
        Assert.True(Inventory.InOwnPack(sim, me, spear)); // off the rack, into the pack
        sim.ApplyNow(new StoreFromPackCommand(me, rack, spear));
        Assert.Equal(silver + price, Coin.Count(sim, me));

        // Nothing for what nobody made, and nothing that isn't a weapon goes on.
        var found = sim.CreateItem(sim.Defs.Thing("IronSpear"), 1);
        sim.Militia.Racked(sim, me, found);
        Assert.Equal(silver + price, Coin.Count(sim, me));
        Assert.Equal(0, sim.ContainerRoom(rack, sim.Defs.Thing("IronBar"), null));

        // No more than it asked for.
        for (var i = 0; i < wanted + 2; i++)
            sim.Militia.Racked(sim, me, Spear(sim, me));
        Assert.Equal(silver + price * wanted, Coin.Count(sim, me));
        Assert.False(sim.Militia.Paying);
    }

    [Fact]
    public void TheRacksArmsAreLeftAloneTillTheRaidComesThenTheUnarmedTakeThem()
    {
        var sim = Round(director: false);
        var rack = Rack(sim);
        var spears = Enumerable.Range(0, 2).Select(_ => Spear(sim, Entity.Null)).ToList();
        foreach (var s in spears)
        {
            Assert.True(sim.Store(rack, s).IsNull);
            sim.World.Add(s, new TownArms());
        }
        sim.TickMany(SimClock.TicksPerHour * 2);
        Assert.All(spears, s => Assert.Equal(rack, sim.World.Get<Stored>(s).In));

        // Nor can a player lift one outside a muster.
        var me = Me(sim);
        sim.World.Get<Position>(me).Cell = sim.SpiralFrom(sim.World.Get<Thing>(rack).Cell, 2)
            .First(c => sim.Map.Passable(c) && AvatarSystem.InReach(c, sim.World.Get<Thing>(rack).Cell));
        sim.ApplyNow(new TakeFromStorageCommand(me, spears[0]));
        Assert.True(sim.World.Has<Stored>(spears[0]));

        // Word of a raid isn't the raid: the rack's still left alone.
        sim.Militia.Call(sim, "tonight");
        sim.TickMany(SimClock.TicksPerHour * 2);
        Assert.All(spears, s => Assert.Equal(rack, sim.World.Get<Stored>(s).In));

        sim.Militia.Muster(sim);
        for (var h = 0; h < 6 && spears.Any(s => sim.World.Has<Stored>(s)); h++)
            sim.TickMany(SimClock.TicksPerHour);
        var w = sim.World;
        Assert.All(spears, s => Assert.Contains(sim.Creatures(), e => !w.Has<Avatar>(e) && w.TryGet<Equipment>(e, out var eq) && eq.Weapon == s));

        // The muster stands down: the town's arms go back on the rack.
        sim.Militia.StandDown(sim);
        for (var h = 0; h < 8 && spears.Any(s => !w.Has<Stored>(s)); h++)
            sim.TickMany(SimClock.TicksPerHour);
        Assert.All(spears, s => Assert.Equal(rack, w.Get<Stored>(s).In));
    }

    [Fact]
    public void TheTownsOwnArmsDontSwellTheNextRaid()
    {
        var sim = Round(director: false);
        var w = sim.World;
        var villager = sim.Creatures().First(e => !w.Has<Avatar>(e) && w.Get<Creature>(e).Faction == Faction.Village && !Armed(sim, e));
        var before = Raids.Points(sim);
        var town = Spear(sim, Entity.Null);
        w.Add(town, new TownArms());
        sim.EquipItem(villager, town);
        Assert.Equal(before, Raids.Points(sim));
        sim.EquipItem(villager, Spear(sim, Entity.Null));
        Assert.Equal(before + 15f, Raids.Points(sim));
    }

    [Fact]
    public void AVillagerFetchesAWeaponKeptInAChest()
    {
        var sim = Round(director: false);
        var w = sim.World;
        // A cottage's chest: a solid cell, reached from beside it (the smithy's is the smith's stock).
        var chest = w.Query<Thing>().First(e => w.Get<Thing>(e).Def.Name == "Chest" && Buildings.Of(sim, e) is { IsNull: false } b
            && w.Get<PresetBuilding>(b).Def.Home);
        var spear = Spear(sim, Entity.Null);
        Assert.True(sim.Store(chest, spear).IsNull);
        for (var h = 0; h < 8 && w.Has<Stored>(spear); h++)
            sim.TickMany(SimClock.TicksPerHour);
        Assert.Contains(sim.Creatures(), e => w.TryGet<Equipment>(e, out var eq) && eq.Weapon == spear);
    }

    /// <summary>A shop that keeps its stock (<see cref="Defs.BuildingDef.KeepsStock"/>): the smithy's chest is the smith's.</summary>
    [Fact]
    public void TheSmithysWoodIsTheSmithsNotTheTownsFirewood()
    {
        var sim = Round(director: false);
        var w = sim.World;
        var chest = w.Query<Thing>().First(e => w.Get<Thing>(e).Def.Name == "Chest" && Buildings.Of(sim, e) is { IsNull: false } b
            && w.Get<PresetBuilding>(b).Def.Name == "Smithy");
        var wood = sim.StoredIn(chest).First(e => w.Get<Thing>(e).Def.Name == "Wood");
        var smith = sim.Creatures().First(e => w.TryGet<Trade>(e, out var t) && t.Def.WorkplaceDef?.Name == "Smithy");
        var other = sim.Creatures().First(e => w.TryGet<Trade>(e, out var t) && t.Def.WorkplaceDef?.Name != "Smithy");
        Assert.False(sim.IsAvailable(wood), "no job of the town's takes it");
        Assert.False(sim.IsAvailable(wood, other), "nor anyone working elsewhere");
        Assert.True(sim.IsAvailable(wood, smith), "the smith can use it");

        var count = w.Get<Stack>(wood).Count;
        sim.TickMany(SimClock.TicksPerHour * 6);
        Assert.True(w.IsAlive(wood) && w.Has<Stored>(wood) && w.Get<Stack>(wood).Count == count, "still in the chest");
    }

    [Fact]
    public void TheRaidDrillArmsYouStocksTheRackAndSendsBanditsDownTheNorthRoad()
    {
        var sim = Round();
        var me = Me(sim);
        var raid = Assert.IsType<RaidCrisis>(RaidDrill.Stage(sim, me));
        var w = sim.World;

        Assert.Equal(RaidDrill.Sword, w.Get<Thing>(w.Get<Equipment>(me).Weapon).Def.Name);
        var racked = sim.StoredIn(Rack(sim)).Where(e => w.Get<Thing>(e).Def.Name == RaidDrill.Spear).ToList();
        Assert.Equal(RaidDrill.Spears, racked.Count);
        Assert.All(racked, e => Assert.True(w.Has<TownArms>(e)));

        Assert.Contains(raid, sim.Director.Active);
        Assert.NotEmpty(raid.Raiders);
        Assert.True(sim.Militia.Mustering);
        Assert.Contains(sim.Messages.Messages, m => m.Text == "The bell rings out: raiders on the north road!");
        // They come in on the road itself, where it leaves the map's north edge.
        var at = w.Get<Position>(raid.Raiders[0]).Cell;
        Assert.True(at.Y <= 8, $"raiders gathered at {at}, not by the north edge");
        Assert.Contains(raid.Raiders, e => sim.Map.GroundUnder(sim.Map.Idx(w.Get<Position>(e).Cell)) is { Name: "Road" });

        // The town arms itself from the rack.
        sim.TickMany(SimClock.TicksPerHour / 2);
        Assert.Contains(sim.Creatures(), e => e != me && Armed(sim, e) && w.Has<TownArms>(w.Get<Equipment>(e).Weapon));
    }

    [Fact]
    public void InTheDrillNobodyIsMobbedAndTheTownDrivesThemOff()
    {
        var sim = Round();
        var me = Me(sim);
        var raid = RaidDrill.Stage(sim, me)!;
        var w = sim.World;
        int mostOnMe = 0, mostOnOthers = 0;
        var end = sim.Clock.Tick + SimClock.HoursToTicks(sim.Defs.Season.CrisisHours) + SimClock.TicksPerHour;
        while (sim.Director.Active.Contains(raid) && sim.Clock.Tick < end)
        {
            sim.Tick();
            foreach (var e in sim.Creatures())
            {
                var n = Engagement.Pressers(sim, e, Entity.Null);
                if (e == me)
                    mostOnMe = Math.Max(mostOnMe, n);
                else
                    mostOnOthers = Math.Max(mostOnOthers, n);
            }
        }
        Assert.True(mostOnMe <= Engagement.OnPlayer, $"{mostOnMe} pressed the player at once");
        Assert.True(mostOnOthers <= Engagement.OnOthers, $"{mostOnOthers} pressed one villager at once");
        Assert.False(sim.Director.Active.Contains(raid), "the raid should be over");
    }

    [Fact]
    public void ABandAtYourElbowTakesItInTurns()
    {
        var sim = Round(director: false);
        var me = Me(sim);
        var w = sim.World;
        var here = w.Get<Position>(me).Cell;
        var band = Raids.Spawn(sim, Enumerable.Repeat(sim.Defs.AllRaiderKinds.First(k => k.Name == "Swordsman"), 3).ToList(), here, here, sim.Rand);
        Assert.Equal(3, band.Count);
        long firstHurt = -1, downed = -1;
        var mostOnMe = 0;
        for (var t = 0; t < SimClock.TicksPerSecond * 90 && downed < 0; t++)
        {
            sim.Tick();
            mostOnMe = Math.Max(mostOnMe, Engagement.Pressers(sim, me, Entity.Null));
            if (firstHurt < 0 && w.Get<Health>(me).Injuries.Count > 0)
                firstHurt = sim.Clock.Tick;
            if (w.Get<Creature>(me).Downed)
                downed = sim.Clock.Tick;
        }
        Assert.True(firstHurt >= 0, "they came at you");
        Assert.Equal(Engagement.OnPlayer, mostOnMe);
        // Standing there doing nothing, you've still a few blows' grace to answer: it was ~5 s when all three swung at once.
        var lasted = (downed < 0 ? sim.Clock.Tick : downed) - firstHurt;
        Assert.True(lasted >= SimClock.TicksPerSecond * 6, $"downed {lasted / (float)SimClock.TicksPerSecond:0.0} s after the first wound");
    }
}
