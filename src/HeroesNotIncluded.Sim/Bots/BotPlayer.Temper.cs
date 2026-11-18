using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Bots;

/// <summary>
/// The bot's temperament at work (<see cref="BotTemperament"/>): how often it slips at a game,
/// and the words it's known by when it talks. A slip is a real input to the real game (lifting a
/// blade from the tub too soon, a key off the wrong hook), never an outcome made up for it.
/// </summary>
public sealed partial class BotPlayer
{
    /// <summary>The most a careless bot slips at a game, before <see cref="Skill"/>.</summary>
    public const float MaxSlip = 0.35f;

    private BotTemperament? _temper;

    /// <summary>Its temperament: drawn from the defs by its seed unless set (tests set <see cref="BotTemperament.Steady"/>).</summary>
    public BotTemperament Temper
    {
        get => _temper ??= BotTemperament.Roll(world().Defs, seed);
        init => _temper = value;
    }

    /// <summary>How good a hand: 1 plays to its temperament, more slips less, less slips more (<c>--bot-skill</c>).</summary>
    public float Skill { get; init; } = 1f;

    /// <summary>The chance it slips at any one game.</summary>
    public float SlipChance => Math.Clamp((1f - Temper.Care) * MaxSlip / Math.Max(Skill, 0.1f), 0f, 0.9f);

    // Its own dice for slips, apart from the rest of its choices, so a steady bot plays as it always has.
    private readonly SimRandom _slipRand = new(seed * 31 + 7);

    // This game's slip, if it's to make one, and whether it's been counted.
    private bool _slip, _slipCounted;

    /// <summary>A new game: will it slip at this one?</summary>
    private void RollSlip()
    {
        _slip = _slipRand.Chance(SlipChance);
        _slipCounted = false;
    }

    /// <summary>The slip's been made: counted once.</summary>
    public void Slipped()
    {
        if (_slipCounted)
            return;
        _slipCounted = true;
        Stats.Mistakes++;
    }

    /// <summary>What the barks know it by: what anyone in town is, and its temper.</summary>
    private List<string> TagsFor(Simulation sim, Entity me)
    {
        var tags = Barks.TagsOf(sim, me);
        tags.Add("bot");
        tags.AddRange(Temper.Tags());
        return tags;
    }
}
