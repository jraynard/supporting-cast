using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Components;

/// <summary>What a person can get better at. Work types and attacks map onto these.</summary>
public enum Skill
{
    Melee,
    Shooting,
    Medicine,
    Construction,
    Mining,
    Plants,
    Social,

    // Added with crafting. Rolled after the first seven from a stream of their own, so the
    // original seven stay what they were for every seed (SkillRules.Roll).
    Crafting,
    Cooking,
    Hunting,
}

/// <summary>
/// A person's skill levels (0..<see cref="Max"/>), which grow with practice. Levels make work
/// faster, attacks more accurate and bandages better; humans have them, animals and dragons don't.
/// </summary>
public sealed class Skills
{
    public const int Max = 20;

    /// <summary>The level work is tuned around: work speed 1, no accuracy bonus.</summary>
    public const int Average = 6;

    public static readonly int Count = Enum.GetValues<Skill>().Length;

    /// <summary>The skills that existed before crafting; <see cref="Systems.SkillRules.Roll"/> keeps their rolls stable.</summary>
    public const int LegacyCount = 7;

    public readonly int[] Level = new int[Count];
    public readonly float[] Xp = new float[Count];

    public int this[Skill s] => Level[(int)s];

    /// <summary>Experience to go from <paramref name="level"/> to the next.</summary>
    public static float XpToNext(int level) => 1000f * (level + 1);

    /// <summary>Add experience; returns true when it brought a new level.</summary>
    public bool Learn(Skill s, float xp)
    {
        var i = (int)s;
        if (Level[i] >= Max)
            return false;
        Xp[i] += xp;
        var up = false;
        while (Level[i] < Max && Xp[i] >= XpToNext(Level[i]))
        {
            Xp[i] -= XpToNext(Level[i]);
            Level[i]++;
            up = true;
        }
        if (Level[i] >= Max)
            Xp[i] = 0;
        return up;
    }

    /// <summary>Work done per tick at a level: 0.7 for a novice, 1 at <see cref="Average"/>, 1.7 at max.</summary>
    public static float WorkFactor(int level) => 0.7f + 0.05f * level;

    /// <summary>The skill a kind of work trains, if any.</summary>
    public static Skill? For(WorkType work) => work switch
    {
        WorkType.Doctor => Skill.Medicine,
        WorkType.Warden => Skill.Social,
        WorkType.Construct => Skill.Construction,
        WorkType.Mine => Skill.Mining,
        WorkType.Chop or WorkType.Harvest => Skill.Plants,
        WorkType.Hunt => Skill.Hunting,
        WorkType.Craft => Skill.Crafting,
        WorkType.Cook => Skill.Cooking,
        _ => null,
    };
}
