using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

public class MoodTests
{
    private static Simulation Mild()
    {
        var sim = TestWorld.Create(20, 20);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Person(Simulation sim, int x = 5, int y = 5)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), $"P{x}", C(x, y), ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        return e;
    }

    private static Mood MoodOf(Simulation sim, Entity e) => sim.World.Get<Mood>(e);

    private static void UpdateMood(Simulation sim) => new MoodSystem().Update(sim);

    [Fact]
    public void HumansAndDragonsHaveMoodsAnimalsDont()
    {
        var sim = Mild();
        var human = Person(sim);
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "D", C(7, 5));
        var deer = sim.SpawnCreature(sim.Defs.Creature("Deer"), "Deer", C(9, 5), Faction.Wild);
        Assert.Equal(0.5f, MoodOf(sim, human).Value);
        Assert.True(sim.World.Has<Mood>(dragon));
        Assert.False(sim.World.Has<Mood>(deer));
    }

    [Fact]
    public void MemoriesFadeAndMoodEasesBack()
    {
        var sim = Mild();
        var e = Person(sim);
        var bad = sim.Defs.Thoughts.SleptOnGround!;
        MoodSystem.Remember(sim, e, bad);
        Assert.Single(MoodOf(sim, e).Memories);

        UpdateMood(sim);
        var mood = MoodOf(sim, e);
        Assert.Equal(0.5f + bad.Mood / 100f, mood.Target, 3);
        // Eases rather than jumps: one update moves at most the hourly ease over the updates in an hour.
        Assert.Equal(0.5f - MoodSystem.EasePerHour * 60 / SimClock.TicksPerHour, mood.Value, 4);

        for (var t = 0; t < SimClock.HoursToTicks(bad.DurationHours) + 60; t += 60)
            UpdateMood(sim);
        Assert.Empty(mood.Memories);
        Assert.Equal(0.5f, mood.Target, 3);
    }

    [Fact]
    public void RepeatsRefreshAndStacksFallOff()
    {
        var sim = Mild();
        var e = Person(sim);
        var a = Person(sim, 7);
        var b = Person(sim, 9);
        var c = Person(sim, 11);
        var died = sim.Defs.Thoughts.VillagerDied!;
        var mood = MoodOf(sim, e);

        // A one-off thought refreshes on a repeat.
        var ground = sim.Defs.Thoughts.SleptOnGround!;
        MoodSystem.Remember(sim, e, ground);
        mood.Memories[0].TicksLeft = 10;
        MoodSystem.Remember(sim, e, ground);
        Assert.Single(mood.Memories);
        Assert.Equal(SimClock.HoursToTicks(ground.DurationHours), mood.Memories[0].TicksLeft);
        mood.Memories.Clear();

        // A stacking one adds copies up to its limit, then refreshes the oldest.
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "D", C(13, 5));
        var fed = sim.Defs.Thoughts.FedByKeeper!;
        for (var i = 0; i < fed.StackLimit + 2; i++)
            MoodSystem.Remember(sim, dragon, fed, e);
        Assert.Equal(fed.StackLimit, MoodOf(sim, dragon).Memories.Count);

        MoodSystem.Remember(sim, e, died, a);
        MoodSystem.Remember(sim, e, died, b);
        MoodSystem.Remember(sim, e, died, c);
        Assert.Equal(3, mood.Memories.Count);
        var f = died.StackFalloff;
        Assert.Equal(died.Mood * (1 + f + f * f), MoodSystem.Offset(mood), 3);
    }

    [Fact]
    public void ThoughtsOnlyApplyToTheirRace()
    {
        var sim = Mild();
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "D", C(7, 5));
        var human = Person(sim);
        Ai.EatJob.Nourish(sim, dragon, sim.Thing("Meat"), 1);
        Ai.EatJob.Nourish(sim, human, sim.Thing("Meat"), 1);
        Ai.EatJob.Nourish(sim, human, sim.Thing("Berries"), 1);
        Assert.Empty(MoodOf(sim, dragon).Memories);
        Assert.Equal([sim.Defs.Thoughts.AteRawMeat], MoodOf(sim, human).Memories.Select(m => m.Def));
    }

    [Fact]
    public void SituationalThoughtsFollowTheirConditions()
    {
        var sim = Mild();
        var e = Person(sim);
        var food = sim.Defs.Need("Food");
        UpdateMood(sim);
        Assert.Empty(MoodOf(sim, e).Situational);

        sim.World.Get<Needs>(e).Values[food.Index] = 0.2f;
        sim.Climate.BaseTemperature = -10;
        Array.Fill(sim.Map.Temperature, -10f);
        UpdateMood(sim);
        Assert.Equal(["Hungry", "Cold"], MoodOf(sim, e).Situational.Select(t => t.Name).Order().Reverse());

        // Ravenous replaces hungry rather than adding to it.
        sim.World.Get<Needs>(e).Values[food.Index] = 0.05f;
        UpdateMood(sim);
        Assert.Contains(sim.Defs.Thought("Ravenous"), MoodOf(sim, e).Situational);
        Assert.DoesNotContain(sim.Defs.Thought("Hungry"), MoodOf(sim, e).Situational);
    }

    [Fact]
    public void ADeathSaddensTheVillageAndHitsTheKeeperHardest()
    {
        var sim = Mild();
        var keeper = Person(sim);
        var friend = Person(sim, 9);
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "D", C(7, 5));
        EggSystem.AssignKeeper(sim, dragon, keeper);

        ref var c = ref sim.World.Get<Creature>(dragon);
        NeedsSystem.Kill(sim, dragon, ref c);

        Assert.Equal([sim.Defs.Thoughts.MyDragonDied], MoodOf(sim, keeper).Memories.Select(m => m.Def));
        Assert.Equal([sim.Defs.Thoughts.VillagerDied], MoodOf(sim, friend).Memories.Select(m => m.Def));
        Assert.Equal(dragon, MoodOf(sim, friend).Memories[0].Other);
    }

    [Fact]
    public void MoodPacesWorkAndDragonObedience()
    {
        Assert.Equal(1f, MoodRules.WorkFactor(0.5f));
        Assert.Equal(1f, MoodRules.WorkFactor(MoodRules.NeutralLow));
        Assert.Equal(MoodRules.MinWorkFactor, MoodRules.WorkFactor(0f), 4);
        Assert.Equal(MoodRules.MaxWorkFactor, MoodRules.WorkFactor(1f), 4);

        var sim = Mild();
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "D", C(7, 5));
        sim.World.Get<Dragon>(dragon).Affinity = 0.5f;
        var neutral = DragonSystem.Obedience(sim, dragon);
        MoodOf(sim, dragon).Value = 1f;
        Assert.Equal(neutral + MoodRules.ObedienceRange / 2, DragonSystem.Obedience(sim, dragon), 4);
        MoodOf(sim, dragon).Value = 0f;
        Assert.True(DragonSystem.Obedience(sim, dragon) < neutral);
    }

    [Fact]
    public void MoodSurvivesASaveAndOldSavesGetOne()
    {
        var sim = Mild();
        var e = Person(sim);
        var other = Person(sim, 9);
        MoodSystem.Remember(sim, e, sim.Defs.Thoughts.VillagerDied, other);
        MoodOf(sim, e).Value = 0.31f;

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var mood = loaded.World.Get<Mood>(e);
        Assert.Equal(0.31f, mood.Value);
        Assert.Equal(sim.Defs.Thoughts.VillagerDied, mood.Memories.Single().Def);
        Assert.Equal(other, mood.Memories.Single().Other);

        // A creature saved before moods existed gets a neutral one on the next update.
        loaded.World.Remove<Mood>(other);
        UpdateMood(loaded);
        Assert.Equal(0.5f, loaded.World.Get<Mood>(other).Value, 2);
    }
}
