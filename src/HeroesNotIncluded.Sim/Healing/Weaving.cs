using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Healing;

/// <summary>A weave that made a mending: which one, and how many lead runes past its base.</summary>
public readonly record struct Woven(MendingDef Mending, int Extra);

/// <summary>
/// What a mix of runes makes (docs/DESIGN.md, "Healer"). Order doesn't matter, only the mix: a
/// mending's base runes, plus any number of extra copies of its lead rune. Anything else makes
/// nothing, and fizzles.
/// </summary>
public static class Weaving
{
    /// <summary>Mana a weave that makes nothing costs, for each rune in it.</summary>
    public const float FizzlePerRune = 4f;

    /// <summary>The mending these runes make, or null.</summary>
    public static Woven? Match(DefDatabase defs, IReadOnlyList<RuneDef> runes)
    {
        if (runes.Count == 0)
            return null;
        var counts = Count(defs, runes);
        foreach (var m in defs.AllMendings)
            if (Extra(defs, m, counts) is { } extra)
                return new Woven(m, extra);
        return null;
    }

    /// <summary>What a weave costs: its mending's mana, or a little a rune for one that fizzles.</summary>
    public static float Cost(Woven? woven, int runes) =>
        woven is { } w ? w.Mending.CostAt(w.Extra) : FizzlePerRune * runes;

    /// <summary>The base runes of a mending, then its lead, for someone weaving it from the book.</summary>
    public static List<RuneDef> Recipe(MendingDef m, int extra = 0)
    {
        var runes = new List<RuneDef>(m.RuneDefs);
        for (var i = 0; i < extra; i++)
            runes.Add(m.LeadRune);
        return runes;
    }

    /// <summary>A new empath's book: the mendings every empath is taught.</summary>
    public static Spellbook NewBook(Simulation sim)
    {
        var book = new Spellbook();
        foreach (var m in sim.Defs.AllMendings)
            if (m.KnownAtStart)
                book.Known.Add(m);
        return book;
    }

    /// <summary>Whether this empath has the mending written in their book (never, for someone without the gift).</summary>
    public static bool Knows(Simulation sim, Entity who, MendingDef m) =>
        sim.World.TryGet<Spellbook>(who, out var book) && book.Known.Contains(m);

    private static int[] Count(DefDatabase defs, IReadOnlyList<RuneDef> runes)
    {
        var counts = new int[defs.AllRunes.Count];
        foreach (var r in runes)
            counts[r.Index]++;
        return counts;
    }

    /// <summary>Extra lead runes past the base if these counts make this mending, else null.</summary>
    private static int? Extra(DefDatabase defs, MendingDef m, int[] counts)
    {
        var need = new int[counts.Length];
        foreach (var r in m.RuneDefs)
            need[r.Index]++;
        var extra = 0;
        for (var i = 0; i < counts.Length; i++)
        {
            var over = counts[i] - need[i];
            if (over < 0 || (over > 0 && i != m.LeadRune.Index))
                return null;
            extra += over;
        }
        return extra;
    }

    /// <summary>At load: no weave may make two mendings, or the book would lie.</summary>
    internal static void Validate(DefDatabase defs)
    {
        foreach (var a in defs.AllMendings)
            for (var extra = 0; a.Runes.Count + extra <= MendingDef.MaxRunes; extra++)
            {
                var counts = Count(defs, Recipe(a, extra));
                foreach (var b in defs.AllMendings)
                    if (b != a && Extra(defs, b, counts) != null)
                        throw new Exception($"Mendings '{a.Name}' and '{b.Name}' can be woven from the same runes");
            }
    }
}
