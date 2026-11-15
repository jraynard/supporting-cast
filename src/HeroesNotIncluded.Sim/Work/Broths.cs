using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>Fed on a broth (<see cref="Broths"/>): wounds close faster until then.</summary>
public struct Nourished
{
    public long Until;
}

/// <summary>
/// Broths, the kitchen's help to the healer (docs/DESIGN.md, "Cook"): a bowl from a pot that tastes
/// fresh (herbs, greens, enough of them) is a broth. Whoever eats one heals faster for half a day,
/// and an empath gets back a little of the mana the gift spends. It's plain from the taste, so the
/// cook can make one on purpose, and the healer can send the hurt to the hatch.
/// </summary>
public static class Broths
{
    /// <summary>How fresh a dish must taste to be a broth (its share of the flavour).</summary>
    public const float From = 0.3f;

    /// <summary>How much faster wounds close on one, and for how long.</summary>
    public const float HealFactor = 1.5f, Hours = 12f;

    /// <summary>Mana a bowl gives back to one who has the gift.</summary>
    public const float Mana = 12f;

    /// <summary>A cooked dish fresh enough to be a broth.</summary>
    public static bool IsBroth(Simulation sim, Entity item) =>
        sim.World.IsAlive(item) && sim.World.TryGet<Dish>(item, out var d) && sim.World.Get<Thing>(item).Def.Cooked && IsBroth(d.Flavour);

    public static bool IsBroth(Flavour f) => f.Shares.Fresh >= From;

    /// <summary><paramref name="eater"/> has eaten <paramref name="item"/>: if it was a broth, it does them good.</summary>
    public static void Eaten(Simulation sim, Entity eater, Entity item)
    {
        if (!IsBroth(sim, item))
            return;
        var w = sim.World;
        w.Add(eater, new Nourished { Until = sim.Clock.Tick + SimClock.HoursToTicks(Hours) });
        if (w.TryGet<Mana>(eater, out var mana))
            w.Get<Mana>(eater).Current = Math.Min(mana.Max, mana.Current + Mana);
        if (w.Has<Avatar>(eater))
            sim.Messages.Tell(eater, MessageKind.Good, w.Has<Mana>(eater) ? "A good broth: it warms you through, and something of the gift comes back with it."
                : "A good broth: it warms you through. Hurts mend the better for it.", w.Get<Position>(eater).Cell);
    }

    /// <summary>How much faster <paramref name="e"/>'s wounds close for what they've eaten.</summary>
    public static float HealFactorOf(Simulation sim, Entity e) =>
        sim.World.TryGet<Nourished>(e, out var n) && n.Until > sim.Clock.Tick ? HealFactor : 1f;
}
