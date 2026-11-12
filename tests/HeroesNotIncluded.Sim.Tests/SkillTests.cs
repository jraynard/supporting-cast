using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Maps;

namespace HeroesNotIncluded.Sim.Tests;

public class SkillTests
{
    private static Entity Person(Simulation sim, int3 at, int level, params WorkType[] only)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Worker", at, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        sim.SetSkills(e, level);
        var mind = sim.World.Get<Mind>(e);
        Array.Fill(mind.WorkPriority, 0);
        foreach (var w in only)
            mind.WorkPriority[(int)w] = 1;
        return e;
    }

    /// <summary>Ticks a miner of the given level takes to dig through one block.</summary>
    private static int TicksToDig(int level)
    {
        var sim = TestWorld.Create(20, 10, depth: 4);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        sim.FillBlocks(new int3(10, 0, 1), new int3(10, 9, 1));
        Systems.Toolbelt.GiveStartingTools(sim, Person(sim, C(9, 5), level, WorkType.Mine)); // the same stone pick for both
        var rock = sim.Map.Idx(C(10, 5));
        sim.Map.Designate(rock, Designation.Dig);
        var ticks = 0;
        while (sim.Map.IsSolid(rock) && ticks < SimClock.TicksPerHour)
        {
            sim.Tick();
            ticks++;
        }
        Assert.False(sim.Map.IsSolid(rock));
        return ticks;
    }

    [Fact]
    public void PeopleRollSkillsAndAnimalsHaveNone()
    {
        var sim = TestWorld.Create();
        var a = sim.SpawnCreature(sim.Defs.Creature("Human"), "A", C(5, 5));
        var b = sim.SpawnCreature(sim.Defs.Creature("Human"), "B", C(6, 5));
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "D", C(7, 5));
        var sa = sim.World.Get<Skills>(a);
        Assert.All(sa.Level, l => Assert.InRange(l, 2, Skills.Max));
        Assert.NotEqual(sa.Level, sim.World.Get<Skills>(b).Level);
        Assert.False(sim.World.Has<Skills>(dragon));
        Assert.Equal(Skills.Average, SkillRules.LevelOf(sim, dragon, Skill.Melee));
    }

    /// <summary>The roll as it was before crafting added skills: the first seven must come out the same.</summary>
    private static int[] LegacyRoll(ulong seed, Entity e)
    {
        var rand = new SimRandom(seed ^ ((ulong)(e.Index + 1) * 0x9E3779B97F4A7C15UL) ^ 0x5C111u);
        var levels = new int[7];
        for (var i = 0; i < 7; i++)
            levels[i] = rand.Range(2, 9);
        for (var n = 0; n < 2; n++)
        {
            var i = rand.NextInt(7);
            levels[i] = Math.Min(Skills.Max, levels[i] + rand.Range(4, 8));
        }
        return levels;
    }

    [Theory]
    [InlineData(1234UL)]
    [InlineData(99UL)]
    public void AddedSkillsLeaveTheOriginalRollsAlone(ulong seed)
    {
        for (var i = 0; i < 40; i++)
        {
            var e = new Entity(i, 1);
            var skills = SkillRules.Roll(seed, e);
            Assert.Equal(LegacyRoll(seed, e), skills.Level[..Skills.LegacyCount]);
            Assert.All(skills.Level[Skills.LegacyCount..], l => Assert.InRange(l, 2, Skills.Max));

            // A save from before the new skills (the codec marks them -1) gets the same levels on loading.
            var old = new Skills();
            Array.Copy(skills.Level, old.Level, Skills.LegacyCount);
            Array.Fill(old.Level, -1, Skills.LegacyCount, Skills.Count - Skills.LegacyCount);
            SkillRules.RollAdded(seed, e, old);
            Assert.Equal(skills.Level, old.Level);
        }
    }

    [Fact]
    public void SkilledMinersDigFaster()
    {
        var novice = TicksToDig(0);
        var master = TicksToDig(Skills.Max);
        Assert.True(master < novice * 0.6f, $"master took {master} ticks, novice {novice}");
    }

    [Fact]
    public void WorkTrainsTheSkill()
    {
        var sim = TestWorld.Create(20, 10, depth: 4);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        sim.FillBlocks(new int3(10, 0, 1), new int3(10, 9, 1));
        var miner = Person(sim, C(9, 5), 0, WorkType.Mine);
        for (var y = 0; y < 10; y++)
            sim.Map.Designate(sim.Map.Idx(C(10, y)), Designation.Dig);
        sim.TickMany(SimClock.TicksPerHour * 4);

        var skills = sim.World.Get<Skills>(miner);
        Assert.True(skills[Skill.Mining] >= 1, "a novice should learn from a long dig");
        Assert.Equal(0, skills[Skill.Plants]);
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("Mining 1"));
    }

    [Fact]
    public void LearningCarriesOverAndCapsAtMax()
    {
        var s = new Skills();
        Assert.True(s.Learn(Skill.Social, Skills.XpToNext(0) + 10));
        Assert.Equal(1, s[Skill.Social]);
        Assert.Equal(10, s.Xp[(int)Skill.Social], 3);
        s.Learn(Skill.Social, 1e9f);
        Assert.Equal(Skills.Max, s[Skill.Social]);
        Assert.False(s.Learn(Skill.Social, 100));
    }

    [Fact]
    public void ShootingSkillMovesTheOddsAndPracticeRaisesIt()
    {
        var sim = TestWorld.Create(40, 20);
        var archer = Person(sim, C(5, 10), Skills.Average);
        var target = Person(sim, C(15, 10), Skills.Average);
        sim.World.Get<Creature>(target).Faction = Faction.Raider;
        sim.Equip(archer, sim.Thing("Bow"));
        var bow = CombatRules.Attacks(sim, archer)[0];

        var average = CombatRules.Chance(sim, archer, target, bow);
        Assert.Equal(0, average.Skill, 3);
        sim.SetSkills(archer, Skills.Average + 5);
        var good = CombatRules.Chance(sim, archer, target, bow);
        Assert.Equal(5 * SkillRules.AccuracyPerLevel, good.Skill, 3);
        Assert.True(good.Chance > average.Chance);

        CombatRules.Resolve(sim, archer, target, bow);
        Assert.True(sim.World.Get<Skills>(archer).Xp[(int)Skill.Shooting] >= SkillRules.XpPerAttack);
    }
}
