using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Hospitality;

/// <summary>
/// Diners' tastes (docs/DESIGN.md, "Cook"). Each kind of adventurer likes the taste of something
/// (<see cref="Defs.AdventurerKindDef.Tastes"/>). Ordering a meal, they may say what they fancy,
/// in the same breath as the order, so whoever hears the order hears that too. Served a bowl that
/// tastes of it, they're pleased; served one that doesn't, a little put out. Like a named drink at
/// the bar, but asked of the pot: the cook chooses what goes in for the room they've got.
/// </summary>
public static class Tastes
{
    /// <summary>The chance a diner with tastes says what they fancy.</summary>
    public const float CravingChance = 0.5f;

    /// <summary>Satisfaction for a bowl that tastes of what they fancied, and off for one that doesn't.</summary>
    public const float RightMood = 0.4f, WrongMood = 0.2f;

    /// <summary>What an adventurer ordering a meal fancies, if they say: one of their kind's tastes. Null if nothing in particular.</summary>
    public static FlavourKind? Crave(Simulation sim, Entity who, SimRandom rand)
    {
        if (!sim.World.TryGet<Adventurer>(who, out var a) || a.Kind is not { Tastes.Count: > 0 } kind)
            return null;
        if (rand.NextFloat() >= CravingChance)
            return null;
        return kind.Tastes[rand.NextInt(kind.Tastes.Count)];
    }

    /// <summary>
    /// Served their meal: if they said what they fancied, does it taste of it? They say so. A bowl
    /// with no flavour to it (from no pot at all) disappoints a craving like any other miss.
    /// </summary>
    public static void Served(Simulation sim, Entity who, Flavour? flavour, Order meal)
    {
        var w = sim.World;
        if (meal.Craving is not { } wanted || !w.Has<Adventurer>(who))
            return;
        var right = flavour is { } f && f.Has(wanted);
        w.Get<Adventurer>(who).Satisfaction += right ? RightMood : -WrongMood;
        var word = Flavour.Word(wanted);
        sim.Speech.Say(sim, who, right ? Pleased(wanted) : $"I'd hoped for something {word}. Still, it's food.", Volume.Say);
    }

    private static string Pleased(FlavourKind k) => k switch
    {
        FlavourKind.Savoury => "Meat in it. Good.",
        FlavourKind.Sweet => "Ah, that's sweet. Just the thing.",
        FlavourKind.Hot => "There's the heat. Lovely.",
        _ => "Something green at last.",
    };
}
