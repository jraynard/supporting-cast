using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Director;

/// <summary>
/// The Director's small happenings (<see cref="IncidentDef"/>, data/defs/incidents.json): what
/// fills a lull between the season's crises. Each is built from what the town already has (the
/// fever, rats, the notice board), has no Renown or Dread of its own, and comes only when it could
/// (someone to catch it, a larder to get into) and nothing like it is already going on.
/// </summary>
public static class Incidents
{
    /// <summary>Could it happen now?</summary>
    public static bool Possible(Simulation sim, IncidentDef def, IReadOnlyList<ICrisis> active)
    {
        if (def.ForRole != TownRole.None && RuleMind.CoverOf(sim, def.ForRole) == Cover.Nobody)
            return false; // nobody able to see to it
        return def.Kind switch
        {
            IncidentKind.Fever => !active.Any(c => c.Kind == CrisisKind.Plague) && !sim.Creatures().Any(e => FeverSystem.Has(sim, e))
                && Visitors(sim).Any(),
            IncidentKind.Rats => !active.Any(c => c.Kind == CrisisKind.Shortage) && !sim.World.Query<Vermin>().Any() && Larders(sim).Count > 0,
            IncidentKind.Errand => !Favours.FavourBoard.AnyBoard(sim).IsNull && Visitors(sim).Any(e => Coin.Count(sim, e) >= def.Wants.Min(x => x.Price)),
            _ => false,
        };
    }

    /// <summary>Make it happen. False if, after all, it couldn't.</summary>
    public static bool Begin(Simulation sim, SimRandom rand, IncidentDef def)
    {
        var w = sim.World;
        switch (def.Kind)
        {
            case IncidentKind.Fever:
            {
                var who = Visitors(sim).ToList();
                if (who.Count == 0)
                    return false;
                var one = who[rand.NextInt(who.Count)];
                FeverSystem.Catch(sim, one);
                Say(sim, rand, def, one);
                Tell(sim, def, w.Get<Creature>(one).Name, w.Get<Position>(one).Cell);
                return true;
            }
            case IncidentKind.Rats:
            {
                var larders = Larders(sim);
                if (larders.Count == 0)
                    return false;
                var larder = larders[rand.NextInt(larders.Count)];
                if (RatSystem.Loose(sim, larder, Math.Max(1, def.Count)).Count == 0)
                    return false;
                Tell(sim, def, "", w.Get<Thing>(larder).Cell);
                return true;
            }
            case IncidentKind.Errand:
            {
                var want = def.Wants[rand.NextInt(def.Wants.Count)];
                var who = Visitors(sim).Where(e => Coin.Count(sim, e) >= want.Price).ToList();
                if (who.Count == 0)
                    return false;
                var one = who[rand.NextInt(who.Count)];
                if (sim.Favours.SendErrand(sim, one, want.ThingDef, want.Count, want.Price, (int)MathF.Ceiling(want.Hours)) == null)
                    return false;
                Say(sim, rand, def, one);
                Tell(sim, def, w.Get<Creature>(one).Name, w.Get<Position>(one).Cell);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>Adventurers and travellers in town and staying a while: up, and not on their way out.</summary>
    private static IEnumerable<Entity> Visitors(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in sim.Creatures())
            if (w.Has<Adventurer>(e) && !w.Get<Creature>(e).Downed && !w.Has<Combat.Leaving>(e))
                yield return e;
    }

    /// <summary>The kitchen's larders.</summary>
    private static List<Entity> Larders(Simulation sim)
    {
        var kitchen = ShopSystem.BuildingFor(sim, TownRole.Kitchen);
        return kitchen.IsNull ? [] : Buildings.Parts(sim, kitchen).Where(e => sim.World.Get<Thing>(e).Def.Name == "Larder").ToList();
    }

    private static void Say(Simulation sim, SimRandom rand, IncidentDef def, Entity who)
    {
        if (def.Says.Count > 0 && SpeechSystem.CanAct(sim, who))
            sim.Speech.Say(sim, who, def.Says[rand.NextInt(def.Says.Count)], Volume.Say);
    }

    /// <summary>The headline: to whoever holds the trade it's for, or the whole town if it's nobody's.</summary>
    private static void Tell(Simulation sim, IncidentDef def, string name, int3 at)
    {
        if (def.Message.Length == 0)
            return;
        var text = def.Message.Replace("{name}", name);
        var w = sim.World;
        if (def.ForRole == TownRole.None)
        {
            sim.Messages.Add(MessageKind.Warning, text, at);
            return;
        }
        foreach (var e in w.Query<Trade>())
            if (w.Get<Trade>(e).Def.Role == def.ForRole && !w.Get<Creature>(e).Dead)
                sim.Messages.Tell(e, MessageKind.Warning, text, at);
    }
}
