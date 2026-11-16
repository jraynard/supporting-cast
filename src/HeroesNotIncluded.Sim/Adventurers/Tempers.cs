using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;

namespace HeroesNotIncluded.Sim.Adventurers;

/// <summary>An adventurer's way with a town (<see cref="Tempers"/>).</summary>
public enum Temper : byte
{
    /// <summary>Patient, pleasant, tips when it's quick.</summary>
    Easy,

    /// <summary>Loud, thirsty, short-fused: drinks sooner, waits less, and wants a fight once it's in its cups.</summary>
    Rowdy,

    /// <summary>Knows what it likes: tastes count half again, waits a little less.</summary>
    Picky,

    /// <summary>Counts every coin: a service is worth less to it.</summary>
    Stingy,

    /// <summary>Pays for being seen to pay, and tells everyone about it.</summary>
    Boastful,

    /// <summary>Jumpy: a sign of the dead near it and it's off early; drinks little.</summary>
    Timid,
}

/// <summary>
/// What each adventurer is like, beyond their kind's numbers: drawn by weight from the kind's
/// <c>tempers</c> (<c>adventurers.json</c>), from a hash of who they are, so it needs no saving and
/// is the same on every peer. It's what keeps a party from being five copies of one customer: the
/// rowdy one's on its third ale while the stingy one argues over two silver.
/// </summary>
public static class Tempers
{
    public static Temper Of(Simulation sim, Entity e)
    {
        if (!sim.World.TryGet<Adventurer>(e, out var a) || a.Kind == null || a.Kind.TemperWeights.Count == 0)
            return Temper.Easy;
        var weights = a.Kind.TemperWeights;
        var roll = SimRandom.Hash(e.Index, a.Party, (uint)sim.Seed ^ 0x7E3Eu) / (float)uint.MaxValue * weights.Sum(w => w.Weight);
        foreach (var (temper, weight) in weights)
            if ((roll -= weight) < 0f)
                return temper;
        return weights[^1].Temper;
    }

    /// <summary>What a service is worth to them, against what it'd be worth to an easy one.</summary>
    public static float Worth(Temper t) => t switch
    {
        Temper.Stingy => 0.85f,
        Temper.Boastful => 1.15f,
        _ => 1f,
    };

    /// <summary>How long they'll wait for an order, against an easy one's.</summary>
    public static float Patience(Temper t) => t switch
    {
        Temper.Rowdy => 0.85f,
        Temper.Picky => 0.95f,
        Temper.Easy => 1.2f,
        _ => 1f,
    };

    /// <summary>The time between drinks, against an easy one's.</summary>
    public static float Thirst(Temper t) => t switch
    {
        Temper.Rowdy => 0.6f,
        Temper.Boastful => 0.85f,
        Temper.Timid => 1.5f,
        _ => 1f,
    };

    /// <summary>How much a dish's taste moves them, against an easy one.</summary>
    public static float Taste(Temper t) => t == Temper.Picky ? 1.5f : 1f;

    /// <summary>The temper as the barks know it ("rowdy").</summary>
    public static string Tag(Temper t) => t.ToString().ToLowerInvariant();
}
