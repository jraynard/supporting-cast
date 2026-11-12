using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Ecs;

namespace HeroesNotIncluded.Sim.Systems;

/// <summary>One interaction, kept for a while for speech bubbles and the inspector (render-only, not state).</summary>
public readonly record struct SocialEvent(long Tick, Entity Initiator, Entity Recipient, InteractionDef Def);

/// <summary>
/// People talk: now and then someone awake nearby someone else of their faction (same room, or
/// both outdoors) starts an interaction from <see cref="DefDatabase.AllInteractions"/>, which
/// leaves memories and opinions of each other (<see cref="MoodSystem.Remember"/>); keepers pet
/// their dragons the same way. Talk doesn't interrupt work. Every roll comes from
/// <see cref="Rand"/>, a stream of its own, so chatter never shifts <see cref="Simulation.Rand"/>.
/// </summary>
public sealed class SocialSystem(ulong seed) : ISimSystem, Saving.ISaveState
{
    public string Name => "Social";
    public int Interval => 120;

    private const float UpdatesPerHour = SimClock.TicksPerHour / 120f;

    /// <summary>How often an idle-enough person with company starts an interaction.</summary>
    public const float InteractionsPerHour = 0.8f;

    /// <summary>How close (cells, same level) someone must be to talk to.</summary>
    public const int Range = 4;

    /// <summary>How long a <see cref="SocialEvent"/> is kept in <see cref="Recent"/>.</summary>
    public const int KeepTicks = SimClock.TicksPerHour * 6;

    public SimRandom Rand { get; } = new(seed ^ 0x50C1_A15E_ED5u);

    /// <summary>Recent interactions, oldest first (render-only: not saved or hashed).</summary>
    public List<SocialEvent> Recent { get; } = [];

    public void Update(Simulation sim)
    {
        Recent.RemoveAll(ev => sim.Clock.Tick - ev.Tick > KeepTicks);
        if (sim.Defs.AllInteractions.Count == 0)
            return;
        var people = sim.Creatures().Where(e => CanTalk(sim, e)).ToList(); // ascending entity order
        if (people.Count == 0)
            return;
        var chance = InteractionsPerHour / UpdatesPerHour;
        var weights = new List<(InteractionDef Def, Entity With, float Weight)>();
        foreach (var e in people)
        {
            if (!Rand.Chance(chance))
                continue;
            weights.Clear();
            foreach (var other in Partners(sim, e, people))
                foreach (var def in sim.Defs.AllInteractions)
                    if (Weight(sim, e, other, def) is > 0 and var wgt)
                        weights.Add((def, other, wgt));
            if (weights.Count == 0)
                continue;
            var roll = Rand.Range(0f, weights.Sum(x => x.Weight));
            foreach (var (def, other, wgt) in weights)
            {
                if ((roll -= wgt) > 0)
                    continue;
                Interact(sim, e, other, def);
                break;
            }
        }
    }

    /// <summary>A person who can start or answer an interaction: awake, on their feet, not drafted or held.</summary>
    public static bool CanTalk(Simulation sim, Entity e)
    {
        var w = sim.World;
        var c = w.Get<Creature>(e);
        return c.Def.Race == Race.Human && c.Able && c.Faction is Faction.Village or Faction.Ashford
            && w.Has<Mood>(e) && !w.Get<Mind>(e).Asleep && !w.Get<Mind>(e).Drafted && !w.Has<Carried>(e);
    }

    /// <summary>Everyone <paramref name="e"/> could talk to now, in entity order: people nearby and their own dragon.</summary>
    private static IEnumerable<Entity> Partners(Simulation sim, Entity e, List<Entity> people)
    {
        var w = sim.World;
        var faction = w.Get<Creature>(e).Faction;
        foreach (var other in people)
            if (other != e && w.Get<Creature>(other).Faction == faction && Near(sim, e, other))
                yield return other;
    }

    /// <summary>Within <see cref="Range"/> on the same level and in the same room (or the same open air).</summary>
    public static bool Near(Simulation sim, Entity a, Entity b)
    {
        var ca = sim.World.Get<Position>(a).Cell;
        var cb = sim.World.Get<Position>(b).Cell;
        return ca.Z == cb.Z && Math.Max(Math.Abs(ca.X - cb.X), Math.Abs(ca.Y - cb.Y)) <= Range
            && sim.Rooms.RoomAt(ca) == sim.Rooms.RoomAt(cb);
    }

    /// <summary>How likely <paramref name="from"/> is to pick this interaction with <paramref name="to"/> (0 = not allowed).</summary>
    public static float Weight(Simulation sim, Entity from, Entity to, InteractionDef def)
    {
        var w = sim.World;
        if (def.TargetMoodBelow < 1f && MoodRules.MoodOf(sim, to) >= def.TargetMoodBelow)
            return 0f;
        if (def.Teaches && RecipeBooks.Teachable(sim, from, to) == null)
            return 0f;
        var opinion = MoodRules.Opinion(sim, from, to);
        if (opinion < def.MinOpinion)
            return 0f;
        var social = (SkillRules.LevelOf(sim, from, Skill.Social) - Skills.Average) / (float)Skills.Average;
        return def.Weight * Math.Clamp(1f + def.OpinionFactor * opinion / 100f, 0f, 3f) * Math.Max(0f, 1f + def.SocialFactor * social)
            * TraitRules.Factor(TraitRules.Of(sim, from), def);
    }

    /// <summary>
    /// Carry out an interaction: memories on both sides, practice for the initiator.
    /// </summary>
    public void Interact(Simulation sim, Entity from, Entity to, InteractionDef def)
    {
        MoodSystem.Remember(sim, from, def.InitiatorThoughtDef, to);
        MoodSystem.Remember(sim, to, def.RecipientThoughtDef, from);
        SkillRules.Learn(sim, from, Skill.Social, def.Xp);
        if (def.Teaches)
            RecipeBooks.Teach(sim, from, to);
        Recent.Add(new SocialEvent(sim.Clock.Tick, from, to, def));
    }

    public void Save(Saving.SaveWriter w, Simulation sim)
    {
        var (a, b, c, d) = Rand.State;
        w.UInt(a); w.UInt(b); w.UInt(c); w.UInt(d);
    }

    public void Load(Saving.SaveReader r, Simulation sim) =>
        Rand.State = (r.UInt(), r.UInt(), r.UInt(), r.UInt());
}
