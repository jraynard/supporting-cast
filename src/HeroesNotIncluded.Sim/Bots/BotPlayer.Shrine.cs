using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Expeditions;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Bots;

public sealed partial class BotPlayer
{
    // Feeling over: who it last ran its hands over, and when.
    private readonly Dictionary<Entity, long> _feltOver = [];

    private bool Heal(Simulation sim, Entity me, PerceptionView view, Entity patient, long now)
    {
        if (!Reach(sim, me, patient, now))
            return true;
        if (view.FeltWounds(patient) is not { } wounds)
            return false;
        if (wounds.Count == 0)
        {
            // Hands over them first, to find what hurts; once a while is enough.
            if (_feltOver.TryGetValue(patient, out var felt) && now - felt < SimClock.TicksPerHour)
                return false;
            _feltOver[patient] = now;
            Send(new FeelCommand(me, patient));
            return true;
        }
        Send(new LayOnHandsCommand(me, patient, wounds[0].Key));
        return true;
    }

    /// <summary>It knows the mending this wound's feel wants, and has the mana for it.</summary>
    private static bool CanWeave(Simulation sim, Entity me, Healing.FeltWound wound, Mana mana) =>
        sim.Defs.AllMendings.FirstOrDefault(m => m.Feels.ContainsValue(wound.Feels) && Weaving.Knows(sim, me, m)) is { } m && mana.Current >= m.ManaCost;

    /// <summary>The healer's own upkeep: mend what they carry, bind it if they can't, or meditate when short of mana. True if it did any.</summary>
    private bool Mend(Simulation sim, Entity me, PerceptionView view)
    {
        if (!sim.World.TryGet<Mana>(me, out var mana))
            return false;
        foreach (var wound in view.FeltWounds(me) ?? [])
        {
            // The feel of a wound tells a practised empath which mending it wants, and the book its runes.
            var mending = sim.Defs.AllMendings.FirstOrDefault(m => m.Feels.ContainsValue(wound.Feels) && Weaving.Knows(sim, me, m));
            if (wound.Absorbed && mending != null && mana.Current >= mending.ManaCost)
            {
                _weave = mending.RuneDefs.Select(r => r.Index).ToList();
                Send(new WeaveCommand(me, wound.Key));
                return true;
            }
        }
        // Short of mana with a drawn wound bleeding: bind it while the mana comes back.
        if (Bandaging.WoundsToBind(sim, me, me) > 0 && Bandaging.CantBind(sim, me, me) == null
            && (view.FeltWounds(me) ?? []).Any(f => f.Absorbed) && !(view.FeltWounds(me) ?? []).Any(f => f.Absorbed && CanWeave(sim, me, f, mana)))
        {
            Send(new BandageCommand(me, me));
            return true;
        }
        if (mana.Current < mana.Max * 0.3f && _task == 0)
        {
            // At the shrine's stone if there is one: it's calmer there.
            var stone = Nearest(sim, me, "MeditationStone");
            if (!stone.IsNull && !AvatarSystem.InReach(sim, sim.World.Get<Position>(me).Cell, stone))
                Walk(sim, me, sim.World.Get<Thing>(stone).Cell, sim.Clock.Tick);
            else
                Send(new MeditateCommand(me));
            return true;
        }
        return false;
    }
}
