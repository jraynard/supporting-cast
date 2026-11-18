using HeroesNotIncluded.Sim.Carpentry;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Furniture the fire takes from a town building is remembered, and the carpenter joins a new
/// piece at the bench and sets it where the old one stood (<see cref="Refurnishing"/>).
/// </summary>
public class RefurnishingTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static Simulation Round(string playerTrade)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(playerTrade)], []));
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    /// <summary>A stool in the inn set alight and burned away, the fire put out after: where it stood.</summary>
    private static (int3 Cell, byte Facing) BurnAStool(Simulation sim)
    {
        var inn = ShopSystem.BuildingFor(sim, TownRole.Tavern);
        var stool = Buildings.Parts(sim, inn).First(e => sim.World.Get<Thing>(e).Def.Name == "Stool");
        var (cell, facing) = (sim.World.Get<Thing>(stool).Cell, sim.World.Get<Thing>(stool).Facing);
        sim.World.Get<Thing>(stool).Hp = 0.01f;
        Assert.True(sim.Fire.Ignite(sim, cell, 1f));
        for (var t = 0; t < 600 && sim.World.IsAlive(stool); t++)
            sim.Tick();
        sim.Fire.ExtinguishAll(sim);
        Assert.False(sim.World.IsAlive(stool));
        return (cell, facing);
    }

    private static void Stand(Simulation sim, Entity me, int3 near, Func<int3, bool> ok)
    {
        var cell = sim.SpiralFrom(near, 3).First(c => c != near && sim.Map.Passable(c) && c.Z == near.Z && ok(c));
        sim.World.Get<Position>(me).Cell = cell;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);
    }

    private static void Play(Simulation sim, Entity me)
    {
        var predictor = new WorkPredictor();
        for (var t = 0; t < 60 * 240 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var (press, held, axis, act) = predictor.Session is { } s ? TinkerTradeTests.JoinTrue(s) : (false, false, 0, false);
            predictor.Update(sim, me, 1, press, held, axis, c => sim.ApplyNow(c), 0, act: act);
        }
        sim.Tick();
        Assert.False(sim.World.Has<WorkSession>(me), "the session should have ended");
    }

    [Fact]
    public void AStoolBurnedInTheInnIsRememberedAndKeepsThroughASave()
    {
        var sim = Round("Empath");
        var (cell, facing) = BurnAStool(sim);
        var loss = Assert.Single(sim.Refurnishing.Losses);
        Assert.Equal(("Stool", cell, facing), (loss.Def.Name, loss.Cell, loss.Facing));
        // Public knowledge: anyone can see it's gone.
        Assert.Contains(Perception.For(sim, Players.CharacterOf(sim, Ana)).Losses(), l => l.Cell == cell && l.Piece == "Stool");

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(cell, Assert.Single(loaded.Refurnishing.Losses).Cell);
    }

    [Fact]
    public void ThePlayerCarpenterJoinsANewStoolAndSetsItWhereTheOldOneStood()
    {
        var sim = Round("Carpenter");
        var me = Players.CharacterOf(sim, Ana);
        var (cell, facing) = BurnAStool(sim);
        var shop = ShopSystem.WorkplaceOf(sim, sim.Defs.Trade("Carpenter"));
        var bench = Carpenters.StationOf(sim, shop, Carpenters.BenchDef);
        Stand(sim, me, sim.World.Get<Thing>(bench).Cell, c => AvatarSystem.InReach(sim, c, bench));
        Inventory.Stow(sim, me, sim.CreateItem(sim.Defs.Thing(Carpenters.PlanksDef), 2));
        var planks = Refurnishing.PlanksTo(sim, me);

        sim.ApplyNow(new MakeFurnitureCommand(me, bench, "Stool"));
        var join = Assert.IsType<JoinSession>(sim.World.Get<WorkSession>(me));
        Assert.Equal((JoinWork.Furniture, 3), (join.Work, join.Joints));
        Play(sim, me);
        var piece = Refurnishing.Held(sim, me);
        Assert.False(piece.IsNull);
        Assert.Equal("Stool", sim.World.Get<Furnishing>(piece).Def.Name);
        Assert.Equal(planks - 1, Refurnishing.PlanksTo(sim, me));

        // Carried over, and set down where the old one stood: furniture again, and paid for.
        Stand(sim, me, cell, c => AvatarSystem.InReach(c, cell));
        Assert.Equal(AvatarActionKind.SetFurniture, AvatarSystem.ActionAt(sim, me, cell).Kind);
        var silver = Coin.Count(sim, me);
        sim.ApplyNow(new AvatarWorkCommand(me, true, cell));
        sim.ApplyNow(new AvatarWorkCommand(me, false));
        var stool = sim.Map.Structure[sim.Map.Idx(cell)];
        Assert.False(stool.IsNull);
        Assert.Equal(("Stool", facing), (sim.World.Get<Thing>(stool).Def.Name, sim.World.Get<Thing>(stool).Facing));
        Assert.Empty(sim.Refurnishing.Losses);
        Assert.Equal(silver + Refurnishing.FeePerPlank, Coin.Count(sim, me));
        Assert.True(Refurnishing.Held(sim, me).IsNull);
    }

    [Fact]
    public void YouCantMakeWhatNothingWantsReplacingOrWithoutThePlanks()
    {
        var sim = Round("Carpenter");
        var me = Players.CharacterOf(sim, Ana);
        Assert.NotNull(Refurnishing.MakeProblem(sim, me, "Stool")); // nothing's burned
        Assert.NotNull(Refurnishing.MakeProblem(sim, me, "Planks")); // not joinery
        BurnAStool(sim);
        foreach (var e in Core.CraftCommandRules.Sources(sim, me).Where(e => sim.World.Get<Thing>(e).Def.Name == Carpenters.PlanksDef).ToList())
            sim.DestroyThing(Inventory.TakeFromPerson(sim, me, e, sim.World.Get<Stack>(e).Count));
        Assert.Contains("plank", Refurnishing.MakeProblem(sim, me, "Stool"));
    }

    [Fact]
    public void AnAICarpenterSetsBackWhatTheFireTook()
    {
        var sim = Round("Empath");
        var (cell, _) = BurnAStool(sim);
        var carpenter = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Carpenter");
        var chest = Carpenters.ChestOf(sim, ShopSystem.WorkplaceOf(sim, sim.Defs.Trade("Carpenter")));
        sim.Store(chest, sim.CreateItem(sim.Defs.Thing(Carpenters.PlanksDef), 4));
        for (var h = 0; h < 12 && sim.Refurnishing.Losses.Count > 0; h++)
            sim.TickMany(SimClock.TicksPerHour);
        Assert.True(sim.Refurnishing.Losses.Count == 0, $"carpenter: {sim.Ai.LastFailure.GetValueOrDefault(carpenter)} / {sim.World.Get<Mind>(carpenter).Job?.Report}");
        var stool = sim.Map.Structure[sim.Map.Idx(cell)];
        Assert.Equal("Stool", sim.World.Get<Thing>(stool).Def.Name);
        Assert.Equal(carpenter, sim.World.Get<Crafted>(stool).Maker);
    }
}
