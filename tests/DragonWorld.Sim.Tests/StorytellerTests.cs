using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;

namespace DragonWorld.Sim.Tests;

public class StorytellerTests
{
    /// <summary>A quick storyteller: first raid within hours, so tests don't sit through the grace days.</summary>
    private static DifficultyDef Quick(float graceDays = 0.2f) =>
        new() { Name = "Quick", Label = "Quick", GraceDays = graceDays, MeanDaysBetween = 1, PointsFactor = 1, MaxRaiders = 3 };

    private static Simulation Village(ulong seed = 1, int villagers = 3, bool armed = true)
    {
        var sim = TestWorld.Create(48, 48, seed);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        for (var i = 0; i < villagers; i++)
        {
            var e = sim.SpawnCreature(sim.Defs.Creature("Human"), $"Villager{i}", C(22 + i, 24), ageDays: 25 * 365);
            Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
            Array.Fill(sim.World.Get<Mind>(e).WorkPriority, 0);
            if (armed)
                sim.Equip(e, sim.Thing(i == 0 ? "Bow" : "Axe"));
        }
        return sim;
    }

    private static List<Entity> Raiders(Simulation sim) =>
        sim.Creatures().Where(e => sim.World.Get<Creature>(e).Faction == Faction.Raider).ToList();

    /// <summary>Tick until raiders show up (or give up after a day).</summary>
    private static List<Entity> AwaitRaid(Simulation sim)
    {
        for (var h = 0; h < 24 && Raiders(sim).Count == 0; h++)
            sim.TickMany(SimClock.TicksPerHour);
        return Raiders(sim);
    }

    [Fact]
    public void OffAndPeacefulSendNothing()
    {
        var off = Village();
        var peaceful = Village();
        peaceful.Storyteller.SetDifficulty(peaceful, peaceful.Defs.Difficulty("Peaceful"));
        off.TickMany(SimClock.DaysToTicks(1));
        peaceful.TickMany(SimClock.DaysToTicks(1));
        Assert.Empty(Raiders(off));
        Assert.Empty(Raiders(peaceful));
        Assert.Equal(-1, peaceful.Storyteller.NextRaidTick);
        // Neither touches the sim's own random stream, so the village plays out identically.
        Assert.Equal(off.Rand.Fingerprint, peaceful.Rand.Fingerprint);
    }

    [Fact]
    public void ARaidArrivesFromTheEdgeAfterTheGracePeriod()
    {
        var sim = Village();
        sim.Storyteller.SetDifficulty(sim, Quick());
        var due = sim.Storyteller.NextRaidTick;
        Assert.InRange(due, SimClock.DaysToTicks(0.15f), SimClock.DaysToTicks(0.25f));

        var raiders = AwaitRaid(sim);
        Assert.NotEmpty(raiders);
        Assert.True(sim.Clock.Tick >= due);
        Assert.Equal(1, sim.Storyteller.RaidsSent);
        Assert.True(sim.Storyteller.NextRaidTick > sim.Clock.Tick);
        Assert.Contains(sim.Messages.Messages, m => m.Kind == MessageKind.Danger && m.Text.Contains("coming from the"));
        foreach (var r in raiders)
        {
            Assert.True(sim.World.Has<Equipment>(r), "raiders come armed");
            var at = sim.World.Get<Position>(r).Cell;
            var fromEdge = Math.Min(Math.Min(at.X, sim.Map.Width - 1 - at.X), Math.Min(at.Y, sim.Map.Height - 1 - at.Y));
            Assert.True(fromEdge <= 12, $"{at} should be near the map edge");
        }
    }

    [Fact]
    public void RaidsGrowWithTheVillage()
    {
        var small = Village(villagers: 1, armed: false);
        var big = Village(villagers: 3, armed: true);
        small.Storyteller.SetDifficulty(small, Quick());
        big.Storyteller.SetDifficulty(big, Quick());
        Assert.True(big.Storyteller.RaidPoints(big) > small.Storyteller.RaidPoints(small) + 50);

        var chosen = big.Storyteller.Choose(big, 200, 8);
        Assert.InRange(chosen.Count, 2, 5);
        Assert.True(chosen.Sum(k => k.Cost) <= 200);
        Assert.Single(small.Storyteller.Choose(small, 10, 8)); // always at least someone
    }

    [Fact]
    public void RaidsAreDeterministic()
    {
        (long, ulong) Run()
        {
            var sim = Village(seed: 7);
            sim.Storyteller.SetDifficulty(sim, Quick());
            AwaitRaid(sim);
            sim.TickMany(SimClock.TicksPerHour);
            return (sim.Clock.Tick, sim.StateHash());
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void AStorytellerRaidEndsInAFight()
    {
        var sim = Village();
        sim.Storyteller.SetDifficulty(sim, Quick());
        Assert.NotEmpty(AwaitRaid(sim));
        for (var t = 0; t < SimClock.TicksPerHour * 2 && !sim.Combat.Active; t++)
            sim.Tick();
        Assert.True(sim.Combat.Active, "the raiders should walk in and start a fight");
        sim.Combat.VillageAuto = true;
        sim.Combat.Resolve(sim, maxRounds: 60);
        Assert.False(sim.Combat.Active);
        Assert.NotNull(sim.Combat.Outcome);
    }

    [Fact]
    public void RaidersClimbTheLadderWithTheVillage()
    {
        var stone = Village(armed: false);
        foreach (var e in stone.Creatures().ToList())
            Systems.Toolbelt.GiveStartingTools(stone, e);
        Assert.Equal(1, Systems.Mining.VillageGearTier(stone)); // stone axes and picks
        stone.Storyteller.SetDifficulty(stone, Quick());
        for (var i = 0; i < 20; i++)
            Assert.All(stone.Storyteller.Choose(stone, 400, 8), k => Assert.True(k.Tier <= 2, $"{k.Name} came for a stone-age village"));

        var bronze = Village(armed: false);
        bronze.Equip(bronze.Creatures().First(), bronze.Thing("BronzeSword"));
        Assert.Equal(3, Systems.Mining.VillageGearTier(bronze));
        bronze.Storyteller.SetDifficulty(bronze, Quick());
        var seen = new HashSet<string>();
        for (var i = 0; i < 30; i++)
            foreach (var k in bronze.Storyteller.Choose(bronze, 400, 8))
                seen.Add(k.Name);
        Assert.Contains("BronzeSpearman", seen);
        Assert.Contains("Veteran", seen); // one tier past bronze: iron
    }

    [Fact]
    public void GearKnowsItsTier()
    {
        var defs = TestWorld.LoadDefs();
        Assert.Equal(2, defs.Thing("CopperPick").GearTier);
        Assert.Equal(3, defs.Thing("BronzeHelm").GearTier);
        Assert.Equal(4, defs.Thing("ShortSword").GearTier);
        Assert.Equal(6, defs.Thing("ScaleMail").GearTier);
        Assert.Equal(0, defs.Thing("Wood").GearTier);
        Assert.All(defs.AllRaiderKinds, k => Assert.True(k.Tier > 0, $"{k.Name} has no tier"));
    }
}
