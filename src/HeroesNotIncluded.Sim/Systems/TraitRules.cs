using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Ecs;

namespace SupportingCast.Sim.Systems;

/// <summary>Rolling traits, and what they do to thoughts and interactions.</summary>
public static class TraitRules
{
    public const int MaxTraits = 2;

    /// <summary>
    /// 0–2 traits (a quarter none, half one, a quarter two), weighted by commonness and never two
    /// that conflict. Rolled from a stream of its own (sim seed and entity), like skills, so
    /// spawning people doesn't shift <see cref="Simulation.Rand"/>.
    /// </summary>
    public static Traits Roll(DefDatabase defs, ulong seed, Entity e)
    {
        var rand = new SimRandom(seed ^ ((ulong)(e.Index + 1) * 0x9E3779B97F4A7C15UL) ^ 0x7EA175u);
        var traits = new Traits();
        var roll = rand.NextFloat();
        var count = roll < 0.25f ? 0 : roll < 0.75f ? 1 : 2;
        for (var n = 0; n < count; n++)
        {
            var allowed = defs.AllTraits.Where(t => !traits.List.Exists(t.ConflictsWithTrait)).ToList();
            var total = allowed.Sum(t => t.Commonness);
            if (total <= 0)
                break;
            var pick = rand.Range(0f, total);
            foreach (var t in allowed)
                if ((pick -= t.Commonness) <= 0 || t == allowed[^1])
                {
                    traits.List.Add(t);
                    break;
                }
        }
        return traits;
    }

    /// <summary>Replace someone's traits with chosen ones (the avatar, from the character creator), dropping unknown names and conflicts.</summary>
    public static void Set(Simulation sim, Entity e, IEnumerable<string> names)
    {
        var traits = new Traits();
        foreach (var name in names)
            if (sim.Defs.TryTrait(name, out var def) && traits.List.Count < MaxTraits && !traits.List.Exists(def.ConflictsWithTrait))
                traits.List.Add(def);
        if (sim.World.Has<Traits>(e))
            sim.World.Remove<Traits>(e);
        sim.World.Add(e, traits);
    }

    /// <summary>How much a thought weighs for someone with these traits (mood and opinion alike).</summary>
    public static float Factor(Traits? traits, ThoughtDef def)
    {
        var f = 1f;
        if (traits != null)
            foreach (var t in traits.List)
                if (t.ThoughtFactorDefs.TryGetValue(def, out var k))
                    f *= k;
        return f;
    }

    /// <summary>How much likelier (or less) someone with these traits is to start an interaction.</summary>
    public static float Factor(Traits? traits, InteractionDef def)
    {
        var f = 1f;
        if (traits != null)
            foreach (var t in traits.List)
                if (t.InteractionFactorDefs.TryGetValue(def, out var k))
                    f *= k;
        return f;
    }

    /// <summary>The traits' constant mood offset, in points.</summary>
    public static float Mood(Traits? traits) => traits?.List.Sum(t => t.Mood) ?? 0f;

    public static Traits? Of(Simulation sim, Entity e) => sim.World.TryGet<Traits>(e, out var t) ? t : null;
}
