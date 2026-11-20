using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Saving;

namespace HeroesNotIncluded.Sim.Director;

/// <summary>Start a crisis of a kind now: the Director's own rolls, the round's difficulty, the beat's strength. Null if it can't come.</summary>
public delegate ICrisis? CrisisBegin(Simulation sim, SimRandom rand, DifficultyDef? difficulty, float strength);

/// <summary>
/// One kind of crisis the Director can send (<see cref="SimRegistry.Crisis"/>): how it starts, how a
/// save brings it back, and the words for it afterwards. A kind is a registration, not a switch arm
/// (docs/MODDING.md), so a mod's crisis is weighed, sent, saved and toasted like the game's.
/// </summary>
public sealed class CrisisType(CrisisKind kind, CrisisBegin begin, Func<SaveReader, ICrisis> read)
{
    public CrisisKind Kind { get; } = kind;
    public CrisisBegin Begin { get; } = begin;
    public Func<SaveReader, ICrisis> Read { get; } = read;

    /// <summary>Its name on the replay's timeline ("Raiders"); the kind's name if none.</summary>
    public string Label { get; init; } = "";

    /// <summary>What the festival's speech says of it, met or not; a plain line if none.</summary>
    public Func<bool, string>? FestivalLine { get; init; }

    /// <summary>Game hours it runs at most, if not the season's <c>crisisHours</c> (the dragon's longer).</summary>
    public float? Hours { get; init; }
}

/// <summary>The crisis kinds registered (the game's, then each mod's): looked up by name.</summary>
public static class CrisisTypes
{
    /// <summary>Every kind, the game's first, in registration order.</summary>
    public static IReadOnlyList<CrisisType> All => SimContent.CrisisTypes;

    /// <summary>The kind of that name (any case), or null if nothing registers it (its mod is off).</summary>
    public static CrisisType? Find(string name) => SimContent.CrisisTypes.FirstOrDefault(t => string.Equals(t.Kind.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The registration for a kind, or null.</summary>
    public static CrisisType? Of(CrisisKind kind) => Find(kind.Name);

    /// <summary>How long a crisis of this kind runs at most, in game hours.</summary>
    public static float Hours(CrisisKind kind, SeasonDef season) => Of(kind)?.Hours ?? season.CrisisHours;

    /// <summary>Its name on a timeline.</summary>
    public static string Label(CrisisKind kind) => Of(kind) is { Label.Length: > 0 } t ? t.Label : kind.Name;

    /// <summary>What the festival says of it.</summary>
    public static string FestivalLine(CrisisKind kind, bool met) =>
        Of(kind)?.FestivalLine?.Invoke(met) ?? (met ? "When trouble came, we saw it off." : "When trouble came, it had its way.");
}

/// <summary>The game's own crises, registered as a mod's would be.</summary>
internal sealed class CoreCrises : ISimMod
{
    public void Register(SimRegistry registry)
    {
        registry.Crisis(new CrisisType(CrisisKind.Raid, (sim, rand, difficulty, strength) => RaidCrisis.Begin(sim, rand, difficulty, strength), RaidCrisis.Read)
        {
            Label = "Raiders",
            FestivalLine = met => met ? "When raiders came down the road, we drove them off." : "When raiders came down the road, they had the better of us.",
        });
        registry.Crisis(new CrisisType(CrisisKind.Fire, (sim, rand, _, _) => FireCrisis.Begin(sim, rand), FireCrisis.Read)
        {
            Label = "Fire",
            FestivalLine = met => met ? "When fire took hold, we beat it out." : "When fire took hold, it burned what it would.",
        });
        registry.Crisis(new CrisisType(CrisisKind.Shortage, (sim, _, _, _) => ShortageCrisis.Begin(sim), ShortageCrisis.Read)
        {
            Label = "The shortage",
            FestivalLine = met => met ? "When the stores ran low, we made do." : "When the stores ran low, we went hungry.",
        });
        registry.Crisis(new CrisisType(CrisisKind.Plague, (sim, rand, _, _) => PlagueCrisis.Begin(sim, rand), PlagueCrisis.Read)
        {
            Label = "Fever",
            FestivalLine = met => met ? "When the fever came, we nursed each other through it." : "When the fever came, it took its toll.",
        });
        registry.Crisis(new CrisisType(CrisisKind.Dragon, (sim, rand, _, _) => DragonCrisis.Begin(sim, rand), DragonCrisis.Read)
        {
            Label = "The dragon",
            FestivalLine = met => met ? "And the dragon! We stood up to a dragon, friends!" : "And the dragon... we'll not speak of the dragon.",
            Hours = DragonCrisis.Hours,
        });
        registry.Crisis(new CrisisType(CrisisKind.Brawl, (sim, rand, _, strength) => Hospitality.BrawlCrisis.Begin(sim, rand, strength), Hospitality.BrawlCrisis.Read)
        {
            Label = "The brawl",
            FestivalLine = met => met ? "When a rowdy crowd tore up the inn, we saw them off with our bare hands." : "When the brawl broke out at the inn, it got the better of us.",
        });
    }
}
