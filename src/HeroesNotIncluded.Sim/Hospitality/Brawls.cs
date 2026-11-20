using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Hospitality;

/// <summary>
/// Bar fights (docs/DESIGN.md, "Drink"): a rowdy adventurer loud with drink, with someone from
/// another party close by, now and then starts something. It's fists, not blades (their own
/// <see cref="Defs.CreatureDef.Attacks"/>, the weapon left alone), a few rounds of it, and over
/// before anyone's badly hurt: bruises for the healer, ale on the floor for the keeper, a story
/// for everyone. Real blows (<see cref="Combat.Blows"/>), so whoever's in the way gets one too.
/// </summary>
public static class Brawls
{
    /// <summary>How near (cells) someone must be to be picked a fight with.</summary>
    public const int Reach = 3;

    /// <summary>Hurt this much (all wounds), either one stops.</summary>
    public const float EnoughDamage = 10f;

    /// <summary>
    /// The hour's chance for one rowdy drunk: a fight with the nearest adventurer from another
    /// party, if there is one. Decided by a hash, not the dice. True if one started.
    /// </summary>
    public static bool MaybeStart(Simulation sim, Entity e)
    {
        var w = sim.World;
        if (!w.TryGet<Adventurer>(e, out var a) || Adventurers.Tempers.Of(sim, e) != Adventurers.Temper.Rowdy || InOne(sim, e) || !Ready(sim, e)
            || SimRandom.Hash(e.Index, (int)(sim.Clock.Tick / SimClock.TicksPerHour), (uint)sim.Seed ^ 0xB4A7u) % 3 != 0)
            return false;
        var here = w.Get<Position>(e).Cell;
        var foe = Entity.Null;
        var best = int.MaxValue;
        foreach (var o in sim.Creatures())
            if (o != e && w.TryGet<Adventurer>(o, out var b) && b.Party != a.Party && Ready(sim, o) && !InOne(sim, o)
                && w.Get<Position>(o).Cell is var at && at.Z == here.Z && Maps.Map.Distance(at, here) is var d && d <= Reach && d < best)
                (foe, best) = (o, d);
        if (foe.IsNull)
            return false;
        Start(sim, e, foe);
        return true;
    }

    /// <summary>A fight between these two, now: words first, then fists, both ways.</summary>
    public static void Start(Simulation sim, Entity e, Entity foe)
    {
        var w = sim.World;
        Barks.Say(sim, e, "brawl_start", Volume.Shout, "You looking at me?", who: w.Get<Creature>(foe).Name);
        Barks.Say(sim, foe, "brawl_reply", Volume.Shout, "Right. Outside. No, here's fine.", who: w.Get<Creature>(e).Name);
        sim.Messages.Add(MessageKind.Warning, $"A fight breaks out between {w.Get<Creature>(e).Name} and {w.Get<Creature>(foe).Name}!", w.Get<Position>(e).Cell);
        AiSystem.Order(sim, e, new BrawlJob(foe));
        AiSystem.Order(sim, foe, new BrawlJob(e));
    }

    /// <summary>In a fight now.</summary>
    public static bool InOne(Simulation sim, Entity e) => sim.World.TryGet<Mind>(e, out var m) && m.Job is BrawlJob;

    /// <summary>On their feet, not on their way out, not hurt enough to stop: fit to fight.</summary>
    public static bool Ready(Simulation sim, Entity e)
    {
        var w = sim.World;
        return w.IsAlive(e) && w.Get<Creature>(e).Able && !w.Has<Combat.Leaving>(e) && !Players.Steered(sim, e) && !w.Has<Brawler>(e)
            && (!w.TryGet<Health>(e, out var h) || h.Injuries.Sum(i => i.Damage) < EnoughDamage);
    }
}

/// <summary>Brawling with <paramref name="foe"/> (<see cref="Brawls"/>): close in, swing, a few rounds, then sit down.</summary>
public sealed class BrawlJob(Entity foe) : Job
{
    /// <summary>Swings each side takes at most.</summary>
    public const int Rounds = 10;

    /// <summary>Ticks between swings.</summary>
    public const int Between = 70;

    public override Entity Focus => foe;

    protected override IEnumerable<JobStatus> Run()
    {
        var fists = Get<Creature>(Pawn).Def.Attacks.FirstOrDefault(a => !a.Ranged);
        if (fists == null || !Alive(foe))
        {
            Fail("nobody to fight");
            yield break;
        }
        Report = $"Brawling with {Get<Creature>(foe).Name}";
        for (var round = 0; round < Rounds; round++)
        {
            if (!Brawls.Ready(Sim, Pawn) || !Brawls.Ready(Sim, foe))
                break;
            foreach (var s in GoToEntity(foe, PathEnd.Touch)) yield return s;
            if (HasFailed || !Alive(foe))
                yield break;
            var from = Get<Position>(Pawn).Pos;
            var to = Get<Position>(foe).Pos;
            Combat.Blows.Begin(Sim, Pawn, foe, fists, aim: DetMath.Atan2(to.Y - from.Y, to.X - from.X));
            // The odd tankard goes over.
            if (round == 1)
                Sim.Mess.Spill(Sim, MessKind.SpilledAle, PawnCell, 0.4f);
            foreach (var s in Wait(Between)) yield return s;
        }
    }
}
