using HeroesNotIncluded.Sim.Components;

namespace HeroesNotIncluded.Sim.Systems;

/// <summary>
/// Skill lookups and practice. Creatures without <see cref="Skills"/> (animals, dragons) count as
/// <see cref="Skills.Average"/>, so skills only ever make people better or worse than before.
/// </summary>
public static class SkillRules
{
    /// <summary>Experience per tick of skilled work: 18000 for a nonstop day, against 7000 to go from level 6 to 7.</summary>
    public const float XpPerWorkTick = 0.5f;

    /// <summary>Experience for an attack in combat, and extra when it lands.</summary>
    public const float XpPerAttack = 60f, XpPerHit = 60f;

    /// <summary>Accuracy added per level above <see cref="Skills.Average"/> (taken away below it).</summary>
    public const float AccuracyPerLevel = 0.02f;

    public static int LevelOf(Simulation sim, Entity e, Skill s) =>
        sim.World.TryGet<Skills>(e, out var skills) ? skills[s] : Skills.Average;

    public static float WorkFactor(Simulation sim, Entity e, Skill s) => Skills.WorkFactor(LevelOf(sim, e, s));

    public static float Accuracy(Simulation sim, Entity e, Skill s) => (LevelOf(sim, e, s) - Skills.Average) * AccuracyPerLevel;

    /// <summary>Practice a skill, announcing a new level.</summary>
    public static void Learn(Simulation sim, Entity e, Skill s, float xp)
    {
        if (!sim.World.TryGet<Skills>(e, out var skills) || !skills.Learn(s, xp))
            return;
        RecipeBooks.OnLevelUp(sim, e, s, skills[s]);
        var c = sim.World.Get<Creature>(e);
        if (c.Faction == Faction.Village)
            sim.Messages.Add(MessageKind.Info, $"{c.Name} has reached {s} {skills[s]}.", sim.LocationOf(e));
    }

    /// <summary>
    /// Starting levels: mostly middling, with two passions well above. Rolled from a stream of its
    /// own (seeded by the sim seed and the entity) so spawning people doesn't shift <see cref="Simulation.Rand"/>.
    /// </summary>
    public static Skills Roll(ulong seed, Entity e)
    {
        var rand = new SimRandom(seed ^ ((ulong)(e.Index + 1) * 0x9E3779B97F4A7C15UL) ^ 0x5C111u);
        var skills = new Skills();
        // The original seven, exactly as they rolled before more skills were added.
        for (var i = 0; i < Skills.LegacyCount; i++)
            skills.Level[i] = rand.Range(2, 9);
        for (var n = 0; n < 2; n++)
        {
            var i = rand.NextInt(Skills.LegacyCount);
            skills.Level[i] = Math.Min(Skills.Max, skills.Level[i] + rand.Range(4, 8));
        }
        RollAdded(seed, e, skills, all: true);
        return skills;
    }

    /// <summary>
    /// Levels for the skills added after the first seven, from a stream of their own: on spawning
    /// (<paramref name="all"/>), or on loading a save from before they existed (only those it
    /// lacks, marked -1 by the codec). One in four people has a knack (+3..5) for one of them.
    /// </summary>
    public static void RollAdded(ulong seed, Entity e, Skills skills, bool all = false)
    {
        var rand = new SimRandom(seed ^ ((ulong)(e.Index + 1) * 0x9E3779B97F4A7C15UL) ^ 0x5C112u);
        var levels = new int[Skills.Count];
        for (var i = Skills.LegacyCount; i < Skills.Count; i++)
            levels[i] = rand.Range(2, 9);
        if (rand.NextInt(4) == 0)
        {
            var i = Skills.LegacyCount + rand.NextInt(Skills.Count - Skills.LegacyCount);
            levels[i] = Math.Min(Skills.Max, levels[i] + rand.Range(3, 6));
        }
        for (var i = Skills.LegacyCount; i < Skills.Count; i++)
            if (all || skills.Level[i] < 0)
            {
                skills.Level[i] = levels[i];
                skills.Xp[i] = 0;
            }
    }
}
