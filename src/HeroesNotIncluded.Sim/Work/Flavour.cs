using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>The four things a pot can taste of. Appended only: tastes and saves name them by number.</summary>
public enum FlavourKind : byte
{
    Savoury,
    Sweet,
    Hot,
    Fresh,
}

/// <summary>
/// What a pot (or a bowl from it, or a ration) tastes of: so much of each flavour. A pot's is the
/// blend of what went in, the way a drink's <c>DrinkMix</c> is the sum of its adds, so the cook's
/// choice of ingredients is a choice of taste. Plain to anyone who looks in or eats it: not secret.
/// </summary>
public readonly record struct Flavour(float Savoury, float Sweet, float Hot, float Fresh)
{
    public static readonly FlavourKind[] Kinds = Enum.GetValues<FlavourKind>();

    /// <summary>Shares under this don't count as a taste of their own.</summary>
    public const float Noticed = 0.15f;

    public float this[FlavourKind k] => k switch
    {
        FlavourKind.Savoury => Savoury,
        FlavourKind.Sweet => Sweet,
        FlavourKind.Hot => Hot,
        _ => Fresh,
    };

    public float Total => Savoury + Sweet + Hot + Fresh;

    public static Flavour Of(FlavourProps? p) => p == null ? default : new(p.Savoury, p.Sweet, p.Hot, p.Fresh);

    public static Flavour operator +(Flavour a, Flavour b) => new(a.Savoury + b.Savoury, a.Sweet + b.Sweet, a.Hot + b.Hot, a.Fresh + b.Fresh);

    public static Flavour operator *(Flavour a, float k) => new(a.Savoury * k, a.Sweet * k, a.Hot * k, a.Fresh * k);

    /// <summary>The same blend, as shares that add up to 1 (or nothing, for nothing).</summary>
    public Flavour Shares => Total <= 0.0001f ? default : this * (1f / Total);

    /// <summary>The flavour it tastes most of, if it tastes of anything.</summary>
    public FlavourKind? Leading
    {
        get
        {
            if (Total <= 0.0001f)
                return null;
            var best = FlavourKind.Savoury;
            foreach (var k in Kinds)
                if (this[k] > this[best])
                    best = k;
            return best;
        }
    }

    /// <summary>Does it taste noticeably of this?</summary>
    public bool Has(FlavourKind k) => Shares[k] >= Noticed;

    /// <summary>
    /// How well rounded it is, 0..1: one flavour alone is flat, two well matched are good, three
    /// or more in proportion are best. One less the sum of the squared shares, scaled.
    /// </summary>
    public float Balance
    {
        get
        {
            var s = Shares;
            if (s.Total <= 0f)
                return 0f;
            var sq = s.Savoury * s.Savoury + s.Sweet * s.Sweet + s.Hot * s.Hot + s.Fresh * s.Fresh;
            return Math.Clamp((1f - sq) / 0.6f, 0f, 1f);
        }
    }

    public static string Word(FlavourKind k) => k switch
    {
        FlavourKind.Savoury => "savoury",
        FlavourKind.Sweet => "sweet",
        FlavourKind.Hot => "hot",
        _ => "fresh",
    };

    /// <summary>How it tastes, in words: "savoury, with some heat"; "sweet and fresh"; "of nothing much".</summary>
    public string Words()
    {
        var s = Shares;
        if (s.Total <= 0f)
            return "of nothing much";
        var order = Kinds.Where(k => s[k] >= Noticed).OrderByDescending(k => s[k]).ToList();
        var lead = order.Count == 0 ? Leading!.Value : order[0];
        var rest = order.Skip(1).ToList();
        if (rest.Count == 0)
            return Word(lead);
        if (rest.Count == 1 && s[rest[0]] < 0.3f)
            return $"{Word(lead)}, with {Hint(rest[0])}";
        return $"{string.Join(", ", order.Take(order.Count - 1).Select(Word))} and {Word(order[^1])}";
    }

    private static string Hint(FlavourKind k) => k switch
    {
        FlavourKind.Savoury => "something meaty to it",
        FlavourKind.Sweet => "a little sweetness",
        FlavourKind.Hot => "some heat",
        _ => "something green",
    };

    /// <summary>What a diner says, ordering, when they fancy this.</summary>
    public static string Craving(FlavourKind k) => k switch
    {
        FlavourKind.Savoury => "Something with meat in it, if there is any.",
        FlavourKind.Sweet => "Something sweet in it, if you can.",
        FlavourKind.Hot => "Something with a bit of heat to it.",
        _ => "Something fresh. I've had nothing green in days.",
    };
}

/// <summary>
/// A bowl as it came out of its pot (<see cref="Cooking.Ladle"/>): its flavour, what most let it down
/// (tasted by whoever eats it), and whether it was the house dish (<see cref="HouseDish"/>).
/// </summary>
public struct Dish
{
    public Flavour Flavour;
    public DishFault Fault;
    public bool House;
}
