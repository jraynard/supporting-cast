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

    /// <summary>Swing it back (F held) until it'd land this far out, then let go.</summary>
    private static void ThrowTo(Simulation sim, Entity me, float cells, int turn = 0, int turnSteps = 0)
    {
        var predictor = new WorkPredictor();
        var let = false;
        for (var t = 0; t < 60 * 10 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var hold = !let;
            if (predictor.Session is ThrowSession s && s.Swing > 0f && 1f + s.Swing * (Buckets.MaxRange - 1) >= cells)
                let = true;
            predictor.Update(sim, me, 1, false, hold && !let, t < turnSteps ? turn : 0, c => sim.ApplyNow(c));
        }
        sim.Tick();
    }

    [Fact]
    public void AFullBucketIsThrownWhereYouPointAsFarAsYouSwingIt()
    {
        var sim = Warm();
        for (var x = 13; x <= 15; x++)
            sim.SpawnStructure(sim.Thing("WoodWall"), C(x, 10));
        sim.RebuildDerivedIfNeeded();
        Assert.True(sim.Fire.Ignite(sim, C(14, 10), 1f));
        var me = Avatar(sim, C(10, 10));
        Hold(sim, me, Buckets.Full);

        var throwing = AvatarSystem.ActionAt(sim, me, C(14, 10)); // four cells off: out of arm's reach
        Assert.Equal(AvatarActionKind.ThrowWater, throwing.Kind);
        Assert.Contains("fire", throwing.Label);
        Assert.Equal(AvatarActionKind.None, AvatarSystem.ActionAt(sim, me, C(10 + Buckets.MaxRange + 2, 10)).Kind); // too far
        AvatarSystem.Perform(sim, me, throwing);
        Assert.IsType<ThrowSession>(sim.World.Get<WorkSession>(me));

        ThrowTo(sim, me, 4f);
        Assert.False(sim.World.Has<WorkSession>(me));
        Assert.Equal(Buckets.Empty, HeldName(sim, me));
        Assert.Equal(0f, sim.Map.Fire[sim.Map.Idx(C(14, 10))]);
        Assert.True(sim.Fire.Soaked(sim, sim.Map.Idx(C(14, 10))));
        Assert.False(sim.Fire.Soaked(sim, sim.Map.Idx(C(11, 10))), "it went out to the fire, not down at your feet");
    }

    [Fact]
    public void TheThrowTurnsAsItSwingsAndFollowsWhereYouPoint()
    {
        var sim = Warm();
        var me = Avatar(sim, C(10, 10));
        Hold(sim, me, Buckets.Full);
        AvatarSystem.Perform(sim, me, AvatarSystem.ActionAt(sim, me, C(14, 10))); // east, four out

        // Pointed elsewhere (the mouse): the throw goes that way instead.
        sim.ApplyNow(new AimThrowCommand(me, C(10, 14)));
        var s = (ThrowSession)sim.World.Get<WorkSession>(me);
        Assert.Equal(C(10, 14), s.Aim);
        ThrowTo(sim, me, 4f);
        Assert.True(sim.Fire.Soaked(sim, sim.Map.Idx(C(10, 14))), "thrown where it was last pointed");
        Assert.False(sim.Fire.Soaked(sim, sim.Map.Idx(C(14, 10))));

        // Turned with A/D through a quarter turn while swinging: off the way it was first aimed.
        var sim2 = Warm();
        var me2 = Avatar(sim2, C(10, 10));
        Hold(sim2, me2, Buckets.Full);
        AvatarSystem.Perform(sim2, me2, AvatarSystem.ActionAt(sim2, me2, C(14, 10)));
        var quarter = (int)(MathF.PI / 2 / (ThrowSession.TurnPerSecond / SimClock.TicksPerSecond));
        ThrowTo(sim2, me2, 4f, turn: 1, turnSteps: quarter);
        Assert.False(sim2.Fire.Soaked(sim2, sim2.Map.Idx(C(14, 10))), "turned away from where it was first aimed");
        Assert.True(sim2.Fire.Soaked(sim2, sim2.Map.Idx(C(10, 14))) || sim2.Fire.Soaked(sim2, sim2.Map.Idx(C(10, 6))), "a quarter turn round");
    }

    [Fact]
    public void ATurnedThrowSurvivesASave()
    {
        var sim = Warm();
        var me = Avatar(sim, C(10, 10));
        Hold(sim, me, Buckets.Full);
        AvatarSystem.Perform(sim, me, AvatarSystem.ActionAt(sim, me, C(14, 10)));
        sim.ApplyNow(new AimThrowCommand(me, C(13, 13)));
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var s = (ThrowSession)loaded.World.Get<WorkSession>(me);
        Assert.Equal(C(13, 13), s.Aim);
        Assert.Equal(((ThrowSession)sim.World.Get<WorkSession>(me)).Heading, s.Heading);
    }

    [Fact]
    public void ASwingLetGoEarlyFallsShort()
    {
        var sim = Warm();
        var me = Avatar(sim, C(10, 10));
        Hold(sim, me, Buckets.Full);
        AvatarSystem.Perform(sim, me, AvatarSystem.ActionAt(sim, me, C(16, 10)));
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
