using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Environment;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Fighting fire with buckets from the well: filling, throwing, soaking, and the AI's bucket runs.</summary>
public class BucketTests
{
    private static Simulation Warm()
    {
        var sim = Create();
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Avatar(Simulation sim, int3 at)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Bearer", at, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        sim.SetSkills(e);
        sim.World.Add(e, new Avatar());
        return e;
    }

    private static void Hold(Simulation sim, Entity who, string bucket) =>
        sim.World.Get<Carrying>(who).Item = sim.CreateItem(sim.Thing(bucket), 1);

    private static string HeldName(Simulation sim, Entity who) =>
        sim.World.Get<Thing>(sim.World.Get<Carrying>(who).Item).Def.Name;

    [Fact]
    public void WaterPutsOutAPatchAndItWontCatchTillItDries()
    {
        var sim = Warm();
        for (var x = 8; x <= 12; x++)
            sim.SpawnStructure(sim.Thing("WoodWall"), C(x, 10));
        sim.RebuildDerivedIfNeeded();
        foreach (var x in new[] { 9, 10, 11 })
            Assert.True(sim.Fire.Ignite(sim, C(x, 10), 1f));

        Buckets.Splash(sim, C(10, 10));
        Assert.Equal(0f, sim.Map.Fire[sim.Map.Idx(C(10, 10))]); // where it landed: out
        Assert.True(sim.Map.Fire[sim.Map.Idx(C(9, 10))] < 1f);  // round it: knocked down
        Assert.True(sim.Fire.Soaked(sim, sim.Map.Idx(C(10, 10))));
        Assert.False(sim.Fire.Ignite(sim, C(10, 10), 1f), "wet: nothing to burn");

        // A wet patch dies down under the flames and doesn't take a spark till it's dry.
        sim.TickMany(SimClock.HoursToTicks(1));
        Assert.Equal(0f, sim.Map.Fire[sim.Map.Idx(C(9, 10))]);
        Assert.Equal(0f, sim.Map.Fire[sim.Map.Idx(C(11, 10))]);
        sim.TickMany(SimClock.HoursToTicks(Buckets.SoakHours));
        Assert.False(sim.Fire.Soaked(sim, sim.Map.Idx(C(10, 10))));
        Assert.True(sim.Fire.Ignite(sim, C(10, 10), 1f), "dry again");
    }

    [Fact]
    public void ASplashIsRecordedForTheRendererWithTheFlamesItPutOut()
    {
        var sim = Warm();
        sim.SpawnStructure(sim.Thing("WoodWall"), C(10, 10));
        sim.RebuildDerivedIfNeeded();
        Assert.True(sim.Fire.Ignite(sim, C(10, 10), 1f));
        var fire = sim.Map.Fire[sim.Map.Idx(C(10, 10))];
        var me = Avatar(sim, C(7, 10));

        Buckets.Splash(sim, C(10, 10), me);
        var ev = Assert.Single(sim.RecentSplashes);
        Assert.Equal(me, ev.Thrower);
        Assert.Equal(C(10, 10), ev.At);
        Assert.Equal(sim.World.Get<Position>(me).Pos, ev.From);
        Assert.Contains((sim.Map.Idx(C(10, 10)), fire), ev.Doused);

        // Gone after a few seconds, and never saved: it's only for the eye.
        Assert.Empty(SaveGame.Read(SaveGame.Write(sim), sim.Defs).Item1.RecentSplashes);
        sim.TickMany(Buckets.SplashShowTicks + 1);
        Buckets.Splash(sim, C(20, 20));
        var dry = Assert.Single(sim.RecentSplashes);
        Assert.Empty(dry.Doused);
        Assert.True(dry.Thrower.IsNull);
    }

    [Fact]
    public void AnEmptyBucketIsFilledAtTheWell()
    {
        var sim = Warm();
        var well = sim.SpawnStructure(sim.Thing("Well"), C(10, 10));
        sim.RebuildDerivedIfNeeded();
        var me = Avatar(sim, C(9, 10));
        Hold(sim, me, Buckets.Empty);
        var fill = AvatarSystem.ActionAt(sim, me, C(10, 10));
        Assert.Equal(AvatarActionKind.FillBucket, fill.Kind);
        AvatarSystem.Perform(sim, me, fill);
        Assert.Equal(Buckets.Full, HeldName(sim, me));
        Assert.NotEqual(AvatarActionKind.FillBucket, AvatarSystem.ActionAt(sim, me, C(10, 10)).Kind); // full already
        Assert.False(well.IsNull);
    }

    /// <summary>Swing it (the right button held) until it'd land this far out, then throw (the left) along <paramref name="aim"/>.</summary>
    private static void ThrowTo(Simulation sim, Entity me, float cells, float aim = 0f)
    {
        sim.ApplyNow(new BucketCommand(me, true, false, aim, sim.Clock.Tick));
        Assert.True(sim.World.Has<BucketSwing>(me), "swinging");
        for (var t = 0; t < 60 * 5 && Buckets.ThrowDistance(Buckets.SwingOf(sim, me)) < cells; t++)
            sim.Tick();
        sim.ApplyNow(new BucketCommand(me, true, true, aim, sim.Clock.Tick));
        sim.Tick();
    }

    [Fact]
    public void AFullBucketIsSwungAndThrownTheWayYouAimAsFarAsItSwung()
    {
        var sim = Warm();
        for (var x = 13; x <= 15; x++)
            sim.SpawnStructure(sim.Thing("WoodWall"), C(x, 10));
        sim.RebuildDerivedIfNeeded();
        Assert.True(sim.Fire.Ignite(sim, C(14, 10), 1f));
        var me = Avatar(sim, C(10, 10));
        Hold(sim, me, Buckets.Full);
        // Not a click on the fire any more: the buttons, as a bow's.
        Assert.NotEqual(AvatarActionKind.ThrowWater, AvatarSystem.ActionAt(sim, me, C(14, 10)).Kind);

        ThrowTo(sim, me, 4f); // east, four out
        Assert.False(sim.World.Has<BucketSwing>(me));
        Assert.False(sim.World.Has<WorkSession>(me), "no game to sit in: you're free the whole time");
        Assert.Equal(Buckets.Empty, HeldName(sim, me));
        Assert.Equal(0f, sim.Map.Fire[sim.Map.Idx(C(14, 10))]);
        Assert.True(sim.Fire.Soaked(sim, sim.Map.Idx(C(14, 10))));
        Assert.False(sim.Fire.Soaked(sim, sim.Map.Idx(C(11, 10))), "it went out to the fire, not down at your feet");
    }

    [Fact]
    public void TheThrowGoesTheWayYouAimWhenItsThrown()
    {
        var sim = Warm();
        var me = Avatar(sim, C(10, 10));
        Hold(sim, me, Buckets.Full);
        ThrowTo(sim, me, 4f, aim: MathF.PI / 2); // south (+y)
        Assert.True(sim.Fire.Soaked(sim, sim.Map.Idx(C(10, 14))), "thrown the way it was aimed");
        Assert.False(sim.Fire.Soaked(sim, sim.Map.Idx(C(14, 10))));
    }

    [Fact]
    public void YouWalkWithItSwinging()
    {
        var sim = Warm();
        var me = Avatar(sim, C(10, 10));
        Hold(sim, me, Buckets.Full);
        sim.ApplyNow(new BucketCommand(me, true, false, 0f, sim.Clock.Tick));
        sim.ApplyNow(new FreeMoveCommand(me, 1f, 0f, 0f));
        var before = sim.World.Get<Position>(me).Pos;
        sim.TickMany(SimClock.TicksPerSecond * 2);
        Assert.True(sim.World.Get<Position>(me).Pos.X > before.X + 0.5f, "walked east with it swinging");
        Assert.True(sim.World.Has<BucketSwing>(me), "still swinging");
    }

    [Fact]
    public void LoweredItThrowsNothing()
    {
        var sim = Warm();
        var me = Avatar(sim, C(10, 10));
        Hold(sim, me, Buckets.Full);
        sim.ApplyNow(new BucketCommand(me, true, false, 0f, sim.Clock.Tick));
        sim.TickMany(30);
        sim.ApplyNow(new BucketCommand(me, false, false, 0f, sim.Clock.Tick));
        sim.ApplyNow(new BucketCommand(me, true, true, 0f, sim.Clock.Tick)); // a click, not swinging
        Assert.False(sim.World.Has<BucketSwing>(me));
        Assert.Equal(Buckets.Full, HeldName(sim, me));
        Assert.Empty(sim.Fire.SoakedCells);
        // An empty bucket isn't swung at all.
        Hold(sim, me, Buckets.Empty);
        Assert.False(Buckets.Prime(sim, me, true, sim.Clock.Tick));
    }

    [Fact]
    public void ASwingSurvivesASave()
    {
        var sim = Warm();
        var me = Avatar(sim, C(10, 10));
        Hold(sim, me, Buckets.Full);
        sim.ApplyNow(new BucketCommand(me, true, false, 0f, sim.Clock.Tick));
        sim.TickMany(40);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(Buckets.SwingOf(sim, me), Buckets.SwingOf(loaded, me));
        Assert.True(Buckets.SwingOf(loaded, me) > 0f);
    }

    [Fact]
    public void ASwingThrownEarlyFallsShort()
    {
        var sim = Warm();
        var me = Avatar(sim, C(10, 10));
        Hold(sim, me, Buckets.Full);
        ThrowTo(sim, me, 2f);
        Assert.True(sim.Fire.Soaked(sim, sim.Map.Idx(C(12, 10))) || sim.Fire.Soaked(sim, sim.Map.Idx(C(13, 10))));
        Assert.False(sim.Fire.Soaked(sim, sim.Map.Idx(C(16, 10))));
    }

    [Fact]
    public void WetGroundSurvivesASave()
    {
        var sim = Warm();
        Buckets.Splash(sim, C(10, 10));
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.True(loaded.Fire.Soaked(loaded, loaded.Map.Idx(C(10, 10))));
        Assert.False(loaded.Fire.Ignite(loaded, C(10, 10), 1f));
    }

    [Fact]
    public void ATownHasBucketsAtItsWellAndTheAIFightsFireWithThem()
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        var w = sim.World;
        var well = Buckets.Wells(sim).First();
        var wellAt = w.Get<Thing>(well).Cell;
        int Hanging() => sim.AvailableItems().Count(e => Buckets.IsBucket(sim, e) && Maps.Map.Distance(w.Get<Thing>(e).Cell, wellAt) < Buckets.StrayFrom);
        Assert.Equal(Buckets.PerWell, Hanging());

        var crisis = Assert.IsType<FireCrisis>(sim.Director.Send(sim, CrisisKind.Fire));
        var soaked = false;
        var thrown = false;
        for (var i = 0; i < 4 * 8 && sim.Fire.BurningTiles.Count > 0; i++)
        {
            sim.TickMany(SimClock.TicksPerHour / 4);
            soaked |= sim.Fire.SoakedCells.Count > 0;
            thrown |= sim.Creatures().Any(e => w.Get<Mind>(e).Job is BucketJob);
        }
        var log = string.Join("\n", sim.Messages.Messages.TakeLast(10).Select(m => m.Text));
        Assert.True(thrown, "someone took a bucket to it: " + log);
        Assert.True(soaked, "and threw water: " + log);
        Assert.Empty(sim.Fire.BurningTiles);
        Assert.NotNull(crisis.Check(sim));

        // The buckets find their way back to the well once it's out.
        for (var i = 0; i < 4 * 12 && Hanging() < Buckets.PerWell; i++)
            sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.Equal(Buckets.PerWell, Hanging());
    }
}
