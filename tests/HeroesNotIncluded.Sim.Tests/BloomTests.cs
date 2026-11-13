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

    [Fact]
    public void CharcoalInYourPackGoesInTheBloomNotOnTheFire()
    {
        // In the smithy with the ore and charcoal packed: F at the forge opens it (the fire has fuel),
        // and smelting takes the charcoal for the bloom.
        var (sim, me, _) = ExpeditionTests.Round("Blacksmith");
        var w = sim.World;
        var forge = w.Query<Thing>().First(e => w.Get<Thing>(e).Def.Name == "Forge");
        var at = w.Get<Thing>(forge).Cell;
        var stand = sim.SpiralFrom(at, 2).First(c => c != at && c.Z == at.Z && sim.Map.Passable(c) && AvatarSystem.InReach(sim, c, forge));
        w.Get<Position>(me).Cell = stand;
        w.Get<Position>(me).Pos = Position.CenterOf(stand);
        // Nothing within reach but the pack: empty the smithy's chests.
        foreach (var c in CraftCommandRules.ContainersInReach(sim, me))
            foreach (var item in sim.StoredIn(c).ToList())
                sim.DestroyThing(item);
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("IronOre"), 4));
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("Charcoal"), 2));
        Assert.True(w.Get<Fueled>(forge).Fuel > 0 && w.Get<Fueled>(forge).Fuel < sim.Thing("Forge").Building!.Fuel!.Capacity);

        Assert.Equal(AvatarActionKind.OpenStation, AvatarSystem.ActionAt(sim, me, at).Kind);
        sim.ApplyNow(new CraftCommand(me, forge, sim.Defs.Recipe("SmeltIron")));
        Assert.True(Smithing.PieceOf(sim, me)?.IsBloom);

        // Gone out, it's fed first, from the pack too.
        var (sim2, me2, _) = ExpeditionTests.Round("Blacksmith");
        var forge2 = sim2.World.Query<Thing>().First(e => sim2.World.Get<Thing>(e).Def.Name == "Forge");
        var at2 = sim2.World.Get<Thing>(forge2).Cell;
        var stand2 = sim2.SpiralFrom(at2, 2).First(c => c != at2 && c.Z == at2.Z && sim2.Map.Passable(c) && AvatarSystem.InReach(sim2, c, forge2));
        sim2.World.Get<Position>(me2).Cell = stand2;
        sim2.World.Get<Fueled>(forge2).Fuel = 0;
        Inventory.Stow(sim2, me2, sim2.CreateItem(sim2.Thing("Charcoal"), 2));
        Assert.Equal(AvatarActionKind.Refuel, AvatarSystem.ActionAt(sim2, me2, at2).Kind);
    }
}
