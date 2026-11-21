using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Where the AI takes things from in a round (<see cref="Simulation.AiMayTake"/>): its own workplace's
/// stock and the stockpile, never another's chest or what a person set down; and nothing is hauled.
/// </summary>
public class AiSourcingTests
{
    private static Simulation Round(string trade = "Empath")
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Guid.Empty, "Arin", default));
        sim.Round.SetPreferences(Guid.Empty, [sim.Defs.Trade(trade)], []);
        sim.ApplyNow(new StartRoundCommand());
        sim.TickMany(SimClock.TicksPerHour / 4);
        return sim;
    }

    private static Entity Holder(Simulation sim, string trade) =>
        sim.Creatures().First(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == trade && !Players.Steered(sim, e));

    private static Entity ChestOf(Simulation sim, Func<BuildingDef, bool> which) =>
        sim.World.Query<Thing>().First(e => sim.World.Get<Thing>(e).Def.Name == "Chest" && Buildings.Of(sim, e) is { IsNull: false } b
            && which(sim.World.Get<PresetBuilding>(b).Def));

    /// <summary>Open ground in the street: no stockpile, no building's plot.</summary>
    private static int3 Open(Simulation sim) =>
        sim.SpiralFrom(VillageRound.StockpileMiddle(sim), 30).First(c => sim.Map.InBounds(c) && sim.Map.Passable(c) && sim.Map.Zone[sim.Map.Idx(c)] == 0
            && Buildings.At(sim, c).IsNull && sim.Map.Item[sim.Map.Idx(c)].IsNull && Maps.Map.Distance(c, VillageRound.StockpileMiddle(sim)) > 8);

    private static Entity Iron(Simulation sim) => sim.CreateItem(sim.Defs.Thing("IronBar"), 2);

    [Fact]
    public void AnAiSmithTakesFromItsOwnChestButNotAnothersOrAHomes()
    {
        var sim = Round();
        var smith = Holder(sim, "Blacksmith");
        var smithy = ChestOf(sim, d => sim.World.Get<Trade>(smith).Def.WorksAt(d));
        var tinkers = ChestOf(sim, d => d.Name == "TinkerShop");
        var home = ChestOf(sim, d => d.Home);
        Entity In(Entity chest)
        {
            var bar = sim.CreateItem(sim.Defs.Thing("IronSpear"), 1); // one of a kind: it won't merge into the chest's own stock
            Assert.True(sim.Store(chest, bar).IsNull, $"room in the {sim.World.Get<Thing>(chest).Def.Name} at {sim.World.Get<PresetBuilding>(Buildings.Of(sim, chest)).Def.Name}");
            return bar;
        }
        Assert.True(sim.AiMayTake(In(smithy), smith), "the smithy's stock is the smith's");
        Assert.False(sim.AiMayTake(In(tinkers), smith), "the tinker's is the tinker's");
        Assert.False(sim.AiMayTake(In(home), smith), "a household's is theirs");
        Assert.False(sim.IsAvailable(In(home), smith));
    }

    [Fact]
    public void WhatAPersonSetsDownIsLeftUnlessItsInTheStockpile()
    {
        var sim = Round();
        var smith = Holder(sim, "Blacksmith");
        var square = sim.PlaceItemNear(Iron(sim), Open(sim), byHand: true);
        Assert.False(sim.Map.Zone[sim.Map.Idx(sim.World.Get<Thing>(square).Cell)] > 0, "set down out of the stockpile");
        Assert.False(sim.AiMayTake(square, smith));
        var free = sim.Map.ZonedCells.Select(sim.Map.CellOf).OrderBy(c => c.X).ThenBy(c => c.Y).First(c => sim.Map.Item[sim.Map.Idx(c)].IsNull);
        var barn = sim.PlaceItemNear(Iron(sim), free, byHand: true);
        Assert.True(sim.AiMayTake(barn, smith), "the stockpile is the town's");
        var left = sim.PlaceItemNear(Iron(sim), Open(sim));
        Assert.True(sim.AiMayTake(left, smith), "what the world leaves lying is anyone's");
    }

    [Fact]
    public void NothingSetDownIsHauledInARound()
    {
        var sim = Round();
        var at = Open(sim);
        var wood = sim.PlaceItemNear(sim.CreateItem(sim.Defs.Thing("Planks"), 5, sim.Defs.Material("Pine")), at, byHand: true);
        var cell = sim.World.Get<Thing>(wood).Cell;
        sim.TickMany(SimClock.TicksPerHour * 8);
        Assert.True(sim.World.IsAlive(wood) && sim.World.Get<Thing>(wood) is { Spawned: true } t && t.Cell == cell, "still where it was put down");
    }

    [Fact]
    public void ABotLeavesAnotherShopsChestAlone()
    {
        var sim = Round();
        var me = sim.World.Query<Avatar>().Single();
        var tinkers = ChestOf(sim, d => d.Name == "TinkerShop");
        var bread = sim.CreateItem(sim.Defs.Thing("Bread"), 2);
        sim.Store(tinkers, bread);
        Assert.True(sim.AiMayTake(bread, me), "a player takes what they like, by hand");
        Assert.False(sim.Ours(bread, me), "but a bot minding its manners doesn't");
    }

    [Fact]
    public void AVillagerDoesntFireAnotherTradesForge()
    {
        var sim = Round("Blacksmith");
        var forge = sim.World.Query<Fueled>().First(e => sim.World.Get<Thing>(e).Def.Name == "Forge");
        var smith = sim.World.Query<Avatar>().Single();
        var tinker = Holder(sim, "Tinker");
        Assert.True(WorkGivers.MayWorkAt(sim, smith, forge));
        Assert.False(WorkGivers.MayWorkAt(sim, tinker, forge), "the smithy's forge is its smith's, a player's or not");
    }
}
