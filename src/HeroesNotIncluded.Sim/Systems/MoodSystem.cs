using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Ecs;
using SupportingCast.Sim.Environment;

namespace SupportingCast.Sim.Systems;

/// <summary>
/// Mood for humans and dragons: memories count down, situational thoughts are re-checked, and
/// the value eases toward 50 plus their sum. Mood has no breaks yet; it only scales work speed and
/// dragon obedience (<see cref="MoodRules"/>). Memories are added only through <see cref="Remember"/>.
/// </summary>
public sealed class MoodSystem : ISimSystem
{
    public string Name => "Mood";
    public int Interval => 60;

    private const float UpdatesPerHour = SimClock.TicksPerHour / 60f;

    /// <summary>How far mood moves toward its target per game hour (0–1 scale).</summary>
    public const float EasePerHour = 0.12f;

    public const float PainThreshold = 0.15f, SeverePainThreshold = 0.4f, DarkBelow = 0.2f;

    /// <summary>Humans and dragons have moods; animals don't.</summary>
    public static bool HasMood(CreatureDef def) => def.Race is Race.Human or Race.Dragon;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in sim.Creatures())
        {
            var c = w.Get<Creature>(e);
            if (!HasMood(c.Def))
                continue;
            if (!w.TryGet<Mood>(e, out var mood))
            {
                mood = new Mood(); // a save from before moods
                w.Add(e, mood);
            }

            for (var i = mood.Memories.Count - 1; i >= 0; i--)
                if ((mood.Memories[i].TicksLeft -= Interval) <= 0)
                    mood.Memories.RemoveAt(i);

            var traits = TraitRules.Of(sim, e);
            mood.Situational.Clear();
            foreach (var def in sim.Defs.AllThoughts)
                if (def.Situational && def.AppliesTo(c.Def.Race) && TraitRules.Factor(traits, def) != 0 && Holds(sim, e, c, def))
                    mood.Situational.Add(def);
            // Ravenous replaces hungry: of the bands that hold on one need, only the lowest counts.
            mood.Situational.RemoveAll(t => t.NeedDef != null && mood.Situational.Exists(o => o.NeedDef == t.NeedDef && o.Below < t.Below));

            mood.Target = Math.Clamp(0.5f + Offset(mood, traits) / 100f, 0f, 1f);
            var step = EasePerHour / UpdatesPerHour;
            mood.Value = Math.Clamp(mood.Target, mood.Value - step, mood.Value + step);
        }
    }

    /// <summary>The sum of all current thoughts in mood points (stacked memories falling off, scaled by traits), plus the traits' own.</summary>
    public static float Offset(Mood mood, Traits? traits = null)
    {
        var sum = TraitRules.Mood(traits);
        foreach (var def in mood.Situational)
            sum += TraitRules.Factor(traits, def) * def.Mood;
        for (var i = 0; i < mood.Memories.Count; i++)
            sum += Weight(mood, i) * TraitRules.Factor(traits, mood.Memories[i].Def) * mood.Memories[i].Def.Mood;
        return sum;
    }

    /// <summary>
    /// How much the memory at <paramref name="i"/> counts: falloff per earlier copy, none past the
    /// stack limit. Mood counts copies of a thought about anyone; an opinion (<paramref name="perPerson"/>)
    /// only those about the same person, so chatting with three friends doesn't cheapen the third.
    /// </summary>
    public static float Weight(Mood mood, int i, bool perPerson = false)
    {
        var m = mood.Memories[i];
        var earlier = 0;
        for (var j = 0; j < i; j++)
            if (mood.Memories[j].Def == m.Def && (!perPerson || mood.Memories[j].Other == m.Other))
                earlier++;
        return earlier >= m.Def.StackLimit ? 0f : DetMath.PowInt(m.Def.StackFalloff, earlier);
    }

    /// <summary>
    /// Add a memory (no-op for creatures without a mood, a race the thought doesn't apply to, or a
    /// def the def set lacks). A repeat refreshes it, or stacks up to the thought's limit.
    /// </summary>
    public static void Remember(Simulation sim, Entity e, ThoughtDef? def, Entity? about = null)
    {
        var w = sim.World;
        var other = about ?? Entity.Null;
        if (def == null || def.Situational || !w.IsAlive(e) || !w.TryGet<Mood>(e, out var mood) || !def.AppliesTo(w.Get<Creature>(e).Def.Race))
            return;
        var ticks = SimClock.HoursToTicks(def.DurationHours);
        Memory? oldest = null;
        var copies = 0;
        foreach (var m in mood.Memories)
        {
            if (m.Def != def || m.Other != other)
                continue;
            copies++;
            if (oldest == null || m.TicksLeft < oldest.TicksLeft)
                oldest = m;
        }
        if (oldest != null && copies >= def.StackLimit)
            oldest.TicksLeft = ticks;
        else
            mood.Memories.Add(new Memory { Def = def, TicksLeft = ticks, Other = other });
    }

    /// <summary>Everyone with a mood in the village (alive) gets the memory.</summary>
    public static void RememberVillage(Simulation sim, ThoughtDef? def)
    {
        if (def == null)
            return;
        foreach (var e in sim.Creatures())
            if ( sim.World.Get<Creature>(e).Faction == Faction.Village)
                Remember(sim, e, def);
    }

    /// <summary>A villager died: the village grieves.</summary>
    public static void OnDeath(Simulation sim, Entity dead)
    {
        var w = sim.World;
        var c = w.Get<Creature>(dead);
        if (c.Faction != Faction.Village || !HasMood(c.Def))
            return;
        var known = sim.Defs.Thoughts;
        foreach (var e in sim.Creatures())
        {
            if (e == dead || w.Get<Creature>(e).Faction != Faction.Village)
                continue;
            var opinion = MoodRules.Opinion(sim, e, dead);
            var thought = opinion >= MoodRules.FriendAt ? known.FriendDied
                : opinion <= MoodRules.RivalAt ? known.RivalDied
                : known.VillagerDied;
            Remember(sim, e, thought, dead);
        }
    }

    /// <summary>Does the situational thought hold for <paramref name="e"/> right now (whether or not its mood has updated)?</summary>
    public static bool Holds(Simulation sim, Entity e, ThoughtDef def) => Holds(sim, e, sim.World.Get<Creature>(e), def);

    private static bool Holds(Simulation sim, Entity e, Creature c, ThoughtDef def)
    {
        var w = sim.World;
        var when = def.When;
        switch (when)
        {
            case ThoughtCondition.NeedBelow:
            {
                var needs = w.Get<Needs>(e);
                return needs.Has[def.NeedDef!.Index] && needs.Get(def.NeedDef) < def.Below;
            }
            case ThoughtCondition.Cold:
                return Warmth.Felt(sim, e) < c.Tolerance(sim.Clock.Tick).ComfortMin;
            case ThoughtCondition.Hot:
                return Warmth.Felt(sim, e) > c.Tolerance(sim.Clock.Tick).ComfortMax;
            case ThoughtCondition.InPain or ThoughtCondition.SeverePain:
            {
                if (!w.TryGet<Health>(e, out var health))
                    return false;
                var pain = health.Pain;
                return when == ThoughtCondition.SeverePain ? pain >= SeverePainThreshold : pain >= PainThreshold && pain < SeverePainThreshold;
            }
            case ThoughtCondition.DarkIndoors:
            {
                if (w.Get<Mind>(e).Asleep)
                    return false;
                var cell = w.Get<Position>(e).Cell;
                return sim.Map.Light[sim.Map.Idx(cell)] < DarkBelow && sim.Rooms.RoomAt(cell) is { Indoors: true };
            }
            case ThoughtCondition.FoodPoisoned:
                return FoodPoisoning.Severity(sim, e) > 0;
            case ThoughtCondition.Imprisoned:
                return w.Has<Prisoner>(e);
            case ThoughtCondition.WearingFinery:
                return w.TryGet<Apparel>(e, out var dressed) && w.IsAlive(dressed.Neck)
                    && !Clothing.IsClothes(w.Get<Thing>(dressed.Neck).Def); // jewelry, not a wool scarf
            default:
                return false;
        }
    }
}

/// <summary>What mood changes. Pure functions so tests and the inspector share them.</summary>
public static class MoodRules
{
    public const float MinWorkFactor = 0.85f, MaxWorkFactor = 1.1f, ObedienceRange = 0.2f;

    /// <summary>Mood between these doesn't change work speed: grumbling about a cold night is normal.</summary>
    public const float NeutralLow = 0.35f, NeutralHigh = 0.65f;

    public static float MoodOf(Simulation sim, Entity e) =>
        sim.World.TryGet<Mood>(e, out var mood) ? mood.Value : 0.5f;

    /// <summary>Work speed from mood: ×0.85 miserable, ×1 across the neutral band, ×1.1 elated.</summary>
    public static float WorkFactor(float mood) => mood switch
    {
        < NeutralLow => MinWorkFactor + (1f - MinWorkFactor) * mood / NeutralLow,
        > NeutralHigh => 1f + (MaxWorkFactor - 1f) * (mood - NeutralHigh) / (1f - NeutralHigh),
        _ => 1f,
    };

    /// <summary>A dragon's obedience shift from mood: ±0.1 around neutral.</summary>
    public static float ObedienceShift(float mood) => (mood - 0.5f) * ObedienceRange;

    /// <summary>Opinion points at which someone counts as a friend, or a rival.</summary>
    public const float FriendAt = 25f, RivalAt = -20f, MaxCompatibility = 10f;

    /// <summary>
    /// What <paramref name="of"/> thinks of <paramref name="about"/> (−100..100): the opinions of
    /// their memories about them, plus how well the two get on by nature.
    /// </summary>
    public static float Opinion(Simulation sim, Entity of, Entity about)
    {
        var sum = Compatibility(sim, of, about);
        var traits = TraitRules.Of(sim, of);
        if (sim.World.TryGet<Mood>(of, out var mood))
            for (var i = 0; i < mood.Memories.Count; i++)
                if (mood.Memories[i].Other == about && mood.Memories[i].Def.Opinion != 0)
                    sum += MoodSystem.Weight(mood, i, perPerson: true) * TraitRules.Factor(traits, mood.Memories[i].Def) * mood.Memories[i].Def.Opinion;
        return Math.Clamp(sum, -100f, 100f);
    }

    /// <summary>How well two get on by nature (±<see cref="MaxCompatibility"/>): fixed per pair and the same both ways, so it needs no state.</summary>
    public static float Compatibility(Simulation sim, Entity a, Entity b)
    {
        if (a == b)
            return 0f;
        var (lo, hi) = a.Index < b.Index ? (a, b) : (b, a);
        var h = sim.Seed ^ 0x50C1_A1B0_0Du;
        foreach (var v in (ReadOnlySpan<long>)[lo.Index, lo.Generation, hi.Index, hi.Generation])
        {
            h ^= (ulong)v;
            h *= 0x9E3779B97F4A7C15UL;
            h ^= h >> 31;
        }
        return ((h >> 40) / (float)(1UL << 24) * 2f - 1f) * MaxCompatibility;
    }

    public static string Relation(float opinion) => opinion switch
    {
        >= FriendAt => "friend",
        <= RivalAt => "rival",
        _ => "",
    };

    public static string Describe(float mood) => mood switch
    {
        < 0.2f => "miserable",
        < 0.35f => "unhappy",
        < 0.45f => "a bit down",
        < 0.6f => "content",
        < 0.8f => "happy",
        _ => "elated",
    };
}
