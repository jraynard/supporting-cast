using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tinkering;

/// <summary>
/// A tinker's contraption (<see cref="ContraptionProps"/>). <see cref="Binding"/> is hidden: a part
/// forced home while it was still tight, so the thing may jam when it's wanted. Nothing shows it
/// (<c>PerceptionView</c> never says), only a contraption that does nothing on the road.
/// </summary>
public struct Contraption
{
    public bool Binding;
    public Entity Maker;
}

/// <summary>A contraption as it went out on a quest with its bearer (<c>Expeditions.MemberRecord</c>).</summary>
public sealed record ContraptionRecord(ThingDef Def, Quality Quality, bool Binding, Entity Maker)
{
    public ContraptionUse Use => Def.Contraption!.Use;
}

/// <summary>
/// Contraptions for the road (docs/DESIGN.md, "Tinker"): made at the tinker's bench, put on show
/// and bought like the smith's gear, and carried on a quest, where each does one thing well. A
/// tapping rod finds traps before they find you, a grapnel stops a fatal fall, a lantern lights
/// dark places, a snare takes the fight out of beasts, a rocket brings help when it's all gone
/// wrong. A binding one may jam and do nothing at all.
/// </summary>
public static class Contraptions
{
    /// <summary>The most contraptions an adventurer will carry; past that they won't buy another.</summary>
    public const int MostCarried = 2;

    /// <summary>What one's worth to a buyer against a plain piece of gear of its tier (before how well it's made).</summary>
    public const float Worth = 1.5f;

    /// <summary>The chance a binding contraption jams on a quest, for good.</summary>
    public const float JamChance = 0.5f;

    public static bool IsContraption(Simulation sim, Entity item) =>
        sim.World.IsAlive(item) && sim.World.Has<Contraption>(item) && sim.World.Get<Thing>(item).Def.Contraption != null;

    /// <summary>The contraptions someone has on them (in hand or in the pack), in a fixed order.</summary>
    public static List<Entity> Carried(Simulation sim, Entity who)
    {
        var w = sim.World;
        var list = new List<Entity>();
        if (w.TryGet<Carrying>(who, out var c) && IsContraption(sim, c.Item))
            list.Add(c.Item);
        if (sim.PackOf(who) is { IsNull: false } pack)
            foreach (var item in sim.StoredIn(pack))
                if (IsContraption(sim, item))
                    list.Add(item);
        return list;
    }

    // ---------------- On a quest ----------------

    public static List<ContraptionRecord> Records(Simulation sim, Entity who)
    {
        var w = sim.World;
        return Carried(sim, who).Select(e => new ContraptionRecord(w.Get<Thing>(e).Def,
            w.TryGet<Crafted>(e, out var c) ? c.Quality : Quality.Plain, w.Get<Contraption>(e).Binding, w.Get<Contraption>(e).Maker)).ToList();
    }

    /// <summary>Contraptions back in a returning bearer's pack.</summary>
    public static void Restore(Simulation sim, Entity who, IEnumerable<ContraptionRecord> carried)
    {
        var w = sim.World;
        foreach (var c in carried)
        {
            var item = sim.CreateItem(c.Def, 1, null);
            w.Add(item, new Contraption { Binding = c.Binding, Maker = c.Maker });
            w.Add(item, new Crafted { Quality = c.Quality, Maker = c.Maker });
            Inventory.Stow(sim, who, item);
        }
    }

    /// <summary>How much better made ones work: a crude one barely, a masterwork well.</summary>
    public static float Knack(Quality q) => q switch
    {
        Quality.Crude => 0.5f,
        Quality.Fine => 1.25f,
        Quality.Masterwork => 1.5f,
        _ => 1f,
    };

    /// <summary>
    /// At the start of a quest: which of a party's contraptions jam (binding ones, by
    /// <paramref name="roll"/>). Jammed ones still come home; they just do nothing. Returns the jammed.
    /// </summary>
    public static List<ContraptionRecord> Jam(IEnumerable<ContraptionRecord> carried, Func<float> roll) =>
        carried.Where(c => c.Binding && roll() < JamChance).ToList();

    /// <summary>What a bearer's working contraptions add to their strength on this quest.</summary>
    public static float StrengthFactor(IEnumerable<ContraptionRecord> working, QuestDef quest)
    {
        var f = 1f;
        foreach (var c in working)
            f *= c.Use switch
            {
                ContraptionUse.Dark when quest.Dark => 1f + 0.12f * Knack(c.Quality),
                ContraptionUse.Beasts when quest.Beasts => 1f + 0.12f * Knack(c.Quality),
                ContraptionUse.Traps => 1f + 0.03f * Knack(c.Quality),
                _ => 1f,
            };
        return f;
    }

    /// <summary>The chance a working tapping rod turns a wound aside before it's taken.</summary>
    public static float TrapsAvoided(ContraptionRecord rod) => 0.25f * Knack(rod.Quality);

    /// <summary>The chance a grapnel turns a death into a bad wound (it's spent either way).</summary>
    public static float FallCaught(ContraptionRecord grapnel) => 0.35f + 0.2f * Knack(grapnel.Quality);

    /// <summary>The chance each death after a rocket's gone up is a member found in time, badly hurt.</summary>
    public static float Rescued(ContraptionRecord rocket) => 0.3f + 0.15f * Knack(rocket.Quality);
}
