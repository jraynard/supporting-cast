using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Bots;

/// <summary>
/// How a bot plays, beyond what its trade asks (<see cref="BotTemperDef"/>): a preset drawn by its
/// seed and wobbled a little. It's what makes a solo table play like people: the careless slip at
/// their games, the hasty skip the chores, the greedy price dear, the nervy run from the dragon
/// while the brave throw water, the chatty never stop. <see cref="Steady"/> never slips (tests).
/// </summary>
public readonly record struct BotTemperament(string Name, float Care, float Haste, float Greed, float Nerve, float Chatter)
{
    /// <summary>How far each of a preset's numbers is wobbled for one bot.</summary>
    public const float Wobble = 0.1f;

    /// <summary>A steady hand: no slips, middling otherwise. What a bot is with no tempers in the defs.</summary>
    public static readonly BotTemperament Steady = new("Steady", 1f, 0.4f, 0.4f, 0.6f, 0.5f);

    /// <summary>A bot's temperament, drawn from the defs by its seed (the same seed, the same bot).</summary>
    public static BotTemperament Roll(DefDatabase defs, ulong seed)
    {
        var tempers = defs.AllBotTempers;
        if (tempers.Count == 0)
            return Steady;
        var rand = new SimRandom(seed * 7919 + 17);
        var roll = rand.NextFloat() * tempers.Sum(t => t.Weight);
        var def = tempers[^1];
        foreach (var t in tempers)
            if ((roll -= t.Weight) < 0f)
            {
                def = t;
                break;
            }
        float W(float v) => Math.Clamp(v + rand.Range(-Wobble, Wobble), 0f, 1f);
        return new BotTemperament(def.Name, W(def.Care), W(def.Haste), W(def.Greed), W(def.Nerve), W(def.Chatter));
    }

    /// <summary>The words the barks know it by: its preset's name, and what stands out about it.</summary>
    public IEnumerable<string> Tags()
    {
        yield return Name.ToLowerInvariant();
        if (Care >= 0.75f) yield return "careful";
        if (Care <= 0.35f) yield return "careless";
        if (Haste >= 0.65f) yield return "hasty";
        if (Greed >= 0.65f) yield return "greedy";
        if (Nerve >= 0.65f) yield return "brave";
        if (Nerve <= 0.35f) yield return "nervy";
        if (Chatter >= 0.65f) yield return "chatty";
        if (Chatter <= 0.3f) yield return "quiet";
    }
}
