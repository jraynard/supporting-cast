using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Speech;

/// <summary>
/// Picking what's said when something happens, from the lines in <c>barks.json</c>
/// (<see cref="BarkDef"/>): of the lines this speaker may say (every tag a line asks for, they
/// have), the more particular to them, the likelier. So a rowdy drunk and a sober innkeeper meet
/// the same event in their own words, and a new turn of phrase is a line of JSON. Deterministic:
/// the caller's random decides (the sim's for townsfolk, a bot's own for a bot).
/// </summary>
public static class Barks
{
    /// <summary>Each tag a line asks for that the speaker has makes it this much likelier than a plain line.</summary>
    public const float PerTag = 2f;

    /// <summary>
    /// A line for this event fit for a speaker with these tags, with its slots filled; null if the
    /// defs have none for it. <paramref name="not"/> (the last said) is passed over if there's another.
    /// </summary>
    public static string? Pick(DefDatabase defs, string id, IReadOnlyCollection<string> tags, SimRandom rand, string? not = null,
        string? who = null, string? thing = null, string? place = null, int? n = null)
    {
        if (defs.Bark(id) is not { } bark)
            return null;
        var total = 0f;
        var fits = new List<(BarkLine Line, float Weight)>();
        foreach (var l in bark.Lines)
        {
            if (!l.Tags.All(tags.Contains))
                continue;
            var weight = l.Weight * (1f + PerTag * l.Tags.Length);
            fits.Add((l, weight));
            total += weight;
        }
        if (fits.Count == 0)
            return null;
        if (not != null && fits.Count > 1 && fits.FindIndex(f => Fill(f.Line.Text, who, thing, place, n) == not) is var skip and >= 0)
        {
            total -= fits[skip].Weight;
            fits.RemoveAt(skip);
        }
        var roll = rand.NextFloat() * total;
        foreach (var (line, weight) in fits)
        {
            roll -= weight;
            if (roll < 0f)
                return Fill(line.Text, who, thing, place, n);
        }
        return Fill(fits[^1].Line.Text, who, thing, place, n);
    }

    /// <summary>The slots filled in: <c>{who}</c>, <c>{thing}</c>, <c>{place}</c>, <c>{n}</c>.</summary>
    public static string Fill(string text, string? who = null, string? thing = null, string? place = null, int? n = null) =>
        text.Replace("{who}", who ?? "you").Replace("{thing}", thing ?? "it").Replace("{place}", place ?? "there")
            .Replace("{n}", n?.ToString() ?? "some");

    /// <summary>
    /// What a creature is, as the barks know them: "adventurer" (and its kind) or "villager", its
    /// trade, its traits and how far into its cups it is. A bot adds its own (temper and "bot").
    /// </summary>
    public static List<string> TagsOf(Simulation sim, Entity e)
    {
        var w = sim.World;
        var tags = new List<string>();
        if (w.TryGet<Adventurer>(e, out var a))
        {
            tags.Add("adventurer");
            tags.Add(a.Kind.Name.ToLowerInvariant());
        }
        else if (w.Has<Creature>(e))
            tags.Add("villager");
        if (w.TryGet<Trade>(e, out var trade))
            tags.Add(trade.Def.Name.ToLowerInvariant());
        if (w.TryGet<Traits>(e, out var traits))
            foreach (var t in traits.List)
                tags.Add(t.Name.ToLowerInvariant());
        if (Hospitality.DrinkingSystem.LevelOf(sim, e) is var drink && drink >= Hospitality.DrinkingSystem.MerryAt)
            tags.Add(drink >= Hospitality.DrinkingSystem.ClumsyAt ? "clumsy" : drink >= Hospitality.DrinkingSystem.LoudAt ? "loud" : "merry");
        return tags;
    }
}
