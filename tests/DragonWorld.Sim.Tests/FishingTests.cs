using DragonWorld.Sim.Components;
using DragonWorld.Sim.Maps;
using DragonWorld.Sim.Saving;
using DragonWorld.Sim.Systems;
using static DragonWorld.Sim.Tests.TestWorld;

namespace DragonWorld.Sim.Tests;

/// <summary>Fishing from marked bank cells, and fish stocks that thin out and recover.</summary>
public class FishingTests
{
    /// <summary>A map with a lake filling everything from x = 20 east.</summary>
    private static Simulation Lakeside()
    {
        var sim = Create();
        var water = sim.Defs.Block("Water");
        for (var y = 0; y < sim.Map.Height; y++)
        for (var x = 20; x < sim.Map.Width; x++)
            sim.Map.Block[sim.Map.Idx(x, y, 0)] = water;
        sim.Map.MarkStructureChanged();
        sim.RebuildDerivedIfNeeded();
        return sim;
    }

    private static int Fish(Simulation sim) =>
        sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e) is { Spawned: true } t && t.Def.Name == "RawFish")
            .Sum(e => sim.World.Get<Stack>(e).Count);

    [Fact]
    public void ThePlayerFishesFromTheBankWhileHoldingTheKey()
    {
        var sim = Lakeside();
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Arin", C(19, 10), ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(me).Values, 1f);
        sim.World.Add(me, new Avatar());

        // No marked spot needed: F at the water beside you (or your own feet), and not from inland.
        Assert.Equal(AvatarActionKind.Fish, AvatarSystem.ActionAt(sim, me, C(20, 10)).Kind);
        Assert.Equal(AvatarActionKind.Fish, AvatarSystem.ActionAt(sim, me, C(19, 10)).Kind);
        Assert.NotEqual(AvatarActionKind.Fish, AvatarSystem.ActionAt(sim, me, C(19, 10), from: C(15, 10)).Kind);

        // Pressed and let go: the cast waits for the key.
        sim.ApplyNow(new Core.AvatarWorkCommand(me, true, C(20, 10)));
        sim.Tick();
        sim.ApplyNow(new Core.AvatarWorkCommand(me, false, C(20, 10)));
        sim.TickMany(Fishing.CastWork * Fishing.CastsPerTrip * 2);
        Assert.Equal(0, Fish(sim));
        Assert.IsType<Ai.FishJob>(sim.World.Get<Mind>(me).Job);

        // Held, it lands fish (the water's fully stocked: most casts get a bite).
        sim.ApplyNow(new Core.AvatarWorkCommand(me, true, C(20, 10)));
        sim.TickMany(Fishing.CastWork * Fishing.CastsPerTrip + 10);
        Assert.True(Fish(sim) > 0, "no fish after a trip's casts");
    }

    [Fact]
    public void OnlyBankCellsCanBeFishingSpots()
    {
        var sim = Lakeside();
        Assert.True(Fishing.CanFishFrom(sim, C(19, 10)));
        Assert.True(Fishing.CanFishFrom(sim, C(19, 0)));
        Assert.False(Fishing.CanFishFrom(sim, C(10, 10)), "inland");
        Assert.False(Fishing.CanFishFrom(sim, C(25, 10)), "out on the water");
        Assert.Equal(1, Fishing.Designate(sim, [C(19, 10), C(10, 10), C(25, 10)]));
    }

    [Fact]
    public void AFisherLandsFishAndTheWaterThinsOut()
    {
        var sim = Lakeside();
        sim.SpawnCreature(sim.Defs.Creature("Human"), "Aldric", C(10, 10));
        Fishing.Designate(sim, [C(19, 10)]);
        var patch = Fishing.PatchOf(sim, C(19, 10));
        sim.TickMany(SimClock.HoursToTicks(10));

        Assert.True(sim.Wildlife.FishCaught >= 4, $"only {sim.Wildlife.FishCaught} fish in 10 hours");
        Assert.Equal(sim.Wildlife.FishCaught, Fish(sim));
        Assert.True(sim.Wildlife.FishStock(sim, patch) < 1f);
    }

    [Fact]
    public void FishedOutWaterRecoversAndItsStockIsSaved()
    {
        var sim = Lakeside();
        var patch = Fishing.PatchOf(sim, C(19, 10));
        while (sim.Wildlife.FishCaught < 8)
            sim.Wildlife.Cast(sim, patch);
        var thinned = sim.Wildlife.FishStock(sim, patch);
        Assert.True(thinned <= 1f - 8 * Fishing.CatchDepletion + 1e-4f);
        Assert.Equal(1f, sim.Wildlife.FishStock(sim, Fishing.PatchOf(sim, C(19, 30)))); // another stretch of water is untouched

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(thinned, loaded.Wildlife.FishStock(loaded, patch));
        Assert.Equal(8, loaded.Wildlife.FishCaught);

        sim.TickMany(SimClock.DaysToTicks(1));
        Assert.True(sim.Wildlife.FishStock(sim, patch) > thinned + 0.2f, "the stock should recover over a day");
    }
}
