using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Smelting ore to bars as a bloom: the fire held orange, the slag beaten out, and the carter's ore.</summary>
public class BloomTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static (Simulation Sim, Entity Me, SmithyKit Kit) Bloom()
    {
        var sim = Create(40, 40);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Smith", C(10, 10), ageDays: 25 * 365);
        sim.Round.OpenLobby();
        sim.Round.Sit(Ana, "Smith", null, null, null);
        sim.Round.Start(sim);
        var kit = SmithyKit.Place(sim);
        var r = sim.Defs.Recipe("SmeltIron");
        sim.World.Get<Workbench>(kit.Forge).Begin(r, me);
        Smithing.BeginBloom(sim, me, kit.Forge, r);
        return (sim, me, kit);
    }

    private static int Bars(Simulation sim, Entity me) =>
        sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e).Def.Name == "IronBar").Sum(e => sim.World.Get<Stack>(e).Count);

    [Fact]
    public void SmeltingIsByHandForAPlayerInARound()
    {
        var (sim, me, kit) = Bloom();
        Assert.True(Smithing.Applies(sim, me, kit.Forge, sim.Defs.Recipe("SmeltIron")));
        Assert.True(Smithing.PieceOf(sim, me)!.IsBloom);
        Assert.False(Smithing.NeedsQuench(sim.Defs.Recipe("SmeltIron")));
        sim.ApplyNow(new SmithCommand(me, kit.Forge));
        Assert.IsType<BloomSession>(sim.World.Get<WorkSession>(me));
    }

    [Fact]
    public void ABloomHeldOrangeAndWellBeatenGivesTwoOrThreeBars()
    {
        var (sim, me, kit) = Bloom();
        kit.Drive(sim, me);

        Assert.Null(Smithing.PieceOf(sim, me));
        Assert.Null(sim.World.Get<Workbench>(kit.Forge).Active);
        var bars = Bars(sim, me);
        Assert.InRange(bars, 2, 3);
        Assert.Contains(sim.Messages.Messages, m => m.Text.StartsWith("Beat the bloom out"));
    }

    [Fact]
    public void BlownTooHotTheIronRunsToSlag()
    {
        var (sim, me, kit) = Bloom();
        kit.Drive(sim, me, SmithStyle.BloomTooHot);
        Assert.InRange(Bars(sim, me), 0, 1);
    }

    [Fact]
    public void DrawnOutEarlyItsASmallBloom()
    {
        var (sim, me, kit) = Bloom();
        kit.Drive(sim, me, SmithStyle.BloomEarly);
        Assert.InRange(Bars(sim, me), 0, 1);
    }

    [Fact]
    public void TooLittleBloomWontHoldUnderTheHammer()
    {
        var (sim, me, kit) = Bloom();
        Smithing.PieceOf(sim, me)!.Temp = 1000f;
        sim.ApplyNow(new SmithCommand(me, kit.Anvil));
        Assert.False(sim.World.Has<WorkSession>(me));
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("too little bloom"));
    }

    [Fact]
    public void YieldFollowsTheBloomTheSlagAndTheBlows()
    {
        var recipe = Create(10, 10).Defs.Recipe("SmeltIron");
        int Yield(float reduced, float slag, float blows) =>
            Smithing.BloomYield(recipe, new WorkPiece { Recipe = recipe, Reduced = reduced, Slag = slag, BlowSum = blows, BlowCount = 1 });
        Assert.Equal(3, Yield(1f, 0f, 1f));
        Assert.Equal(2, Yield(1f, 0f, 0.6f));
        Assert.True(Yield(1f, 0.8f, 1f) < Yield(1f, 0f, 1f));
        Assert.True(Yield(0.4f, 0f, 1f) < Yield(1f, 0f, 1f));
        Assert.Equal(0, Yield(0f, 0f, 1f));
    }

    [Fact]
    public void AnAISmithCanSmeltIronOnCharcoalWithTheBellows()
    {
        var sim = Create(40, 40);
        var kit = SmithyKit.Place(sim);
        Assert.True(Crafting.CanGetHotEnough(sim, kit.Forge, sim.Defs.Recipe("SmeltIron")));
    }

    [Fact]
    public void ABloomInTheTongsSurvivesASave()
    {
        var (sim, me, kit) = Bloom();
        kit.Drive(sim, me, maxTicks: 60 * 8); // into the fire, part way
        Assert.True(sim.World.Has<WorkSession>(me));
        var (loaded, _) = SaveGame.Read(SaveGame.Snapshot(sim), sim.Defs);
        var p = Smithing.PieceOf(loaded, me)!;
        Assert.True(p.IsBloom);
        var s = Assert.IsType<BloomSession>(loaded.World.Get<WorkSession>(me));
        Assert.True(s.Reduced >= 0f && s.Temp > 0f);
    }

    [Fact]
    public void TheSmithStartsWithOreAndTheCarterBringsMoreEachMorning()
    {
        var (sim, _, _) = ExpeditionTests.Round("Blacksmith");
        int Ore() => sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e).Def.Name == "IronOre").Sum(e => sim.World.Get<Stack>(e).Count);
        Assert.Equal(VillageRound.SmithyOre, Ore());
        var day = sim.Clock.Day;
        while (sim.Clock.Day == day || sim.Clock.HourOfDay < OreCarterSystem.DeliveryHour + 1)
            sim.TickMany(SimClock.TicksPerHour);
        var delivered = sim.Defs.Season.OreDelivery.Single(t => t.Def == "IronOre").Count;
        Assert.Equal(VillageRound.SmithyOre + delivered, Ore());
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("carter"));
        // Once a morning.
        sim.TickMany(SimClock.TicksPerHour * 3);
        Assert.Equal(VillageRound.SmithyOre + delivered, Ore());
    }
}
