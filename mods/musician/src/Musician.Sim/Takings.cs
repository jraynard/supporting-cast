using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Musician;

/// <summary>A listener's tip given (the day it was), so they give one a day, however many tunes they hear.</summary>
public struct Tipped
{
    public int Day;
}

/// <summary>
/// A tune a listener called out for: whose playing, which tune, until when they'll want it, and whether the musician
/// heard them ask (only then does their window show it: it was said aloud, nowhere else).
/// </summary>
public struct TuneRequest
{
    public Entity Musician;
    public string Tune;
    public long Until;
    public bool Heard;
}

/// <summary>
/// What the music earns: the hat, set down at the musician's feet as they start playing and taken up again, with the
/// silver tossed down by it, when they stop (real coin, lying in the open: anyone could pocket it); a listener's tip into it, once they've heard a good while (more if they danced, more from an
/// adventurer, once a day); and a tune called out for, played while they're there, tipped well.
/// </summary>
public static class Takings
{
    public const string HatDef = "Hat";

    /// <summary>Silver for a tune heard through, and on top for dancing to it, for an adventurer's purse, and for the tune they asked for.</summary>
    public const int ForListening = 1, ForDancing = 1, ForAnAdventurer = 1, ForTheirTune = 3;

    /// <summary>How likely a listener is to call for a tune as they come (an adventurer twice as likely).</summary>
    public const float AskChance = 0.15f;

    /// <summary>How long a call for a tune stands.</summary>
    public const long Wanted = 2 * SimClock.TicksPerHour;

    /// <summary>How far a listener will reach (cells) to put a coin in the hat.</summary>
    public const int HatReach = 8;

    /// <summary>The hat out of <paramref name="musician"/>'s pack and down at their feet, if they've one: where the tips go.</summary>
    public static void SetDown(Simulation sim, Entity musician, PlaySession s)
    {
        if (sim.Defs.AllThings.FirstOrDefault(t => t.Name == HatDef) is not { } def
            || Inventory.CarriedOf(sim, musician, def).FirstOrDefault(Entity.Null) is not { IsNull: false } hat)
            return;
        var item = Inventory.TakeFromPerson(sim, musician, hat, 1);
        s.Hat = sim.PlaceItemNear(item, sim.World.Get<Position>(musician).Cell, byHand: true);
        // Theirs, set down on purpose: no labourer tidies it off to the barn (a playtest's hat went that way).
        if (sim.World.IsAlive(s.Hat))
            sim.World.Add(s.Hat, new SetDownBy { Who = musician });
    }

    /// <summary>Silver tossed by the hat: the musician's, left lying where it fell till they take it up.</summary>
    private static void Claim(Simulation sim, Entity musician, Entity hat)
    {
        foreach (var coins in Tossed(sim, hat))
            if (!sim.World.Has<SetDownBy>(coins))
                sim.World.Add(coins, new SetDownBy { Who = musician });
    }

    /// <summary>The hat taken up again as the playing stops, and the silver by it into the musician's pack (if it's still where they left it).</summary>
    public static void TakeUp(Simulation sim, Entity musician, PlaySession s)
    {
        var hat = s.Hat;
        s.Hat = Entity.Null;
        var w = sim.World;
        if (!w.IsAlive(hat) || !w.IsAlive(musician) || !w.Get<Thing>(hat).Spawned
            || Sim.Maps.Map.Distance(w.Get<Thing>(hat).Cell, w.Get<Position>(musician).Cell) > HatReach)
            return; // taken, or left behind: theirs to go back for
        foreach (var coins in Tossed(sim, hat))
        {
            w.Remove<SetDownBy>(coins);
            Inventory.Stow(sim, musician, sim.TakeFromStack(coins, w.Get<Stack>(coins).Count));
        }
        w.Remove<SetDownBy>(hat);
        Inventory.Stow(sim, musician, sim.TakeFromStack(hat, 1));
    }

    /// <summary>The silver lying by a hat set down (its cell and those round it): what's been tossed to the musician.</summary>
    public static List<Entity> Tossed(Simulation sim, Entity hat)
    {
        var found = new List<Entity>();
        if (!sim.World.IsAlive(hat) || !sim.World.Get<Thing>(hat).Spawned)
            return found;
        var at = sim.World.Get<Thing>(hat).Cell;
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                if (at + new int3(dx, dy, 0) is var c && sim.Map.InBounds(c) && sim.Map.Item[sim.Map.Idx(c)] is { IsNull: false } item && Coin.IsSilver(sim, item))
                    found.Add(item);
        return found;
    }

    /// <summary>The musician's hat, set down where a listener at <paramref name="at"/> can put a coin in it; Entity.Null if not.</summary>
    public static Entity HatOf(Simulation sim, Entity musician, int3 at) =>
        sim.World.IsAlive(musician) && sim.World.TryGet<WorkSession>(musician, out var w) && w is PlaySession { Hat: var hat }
        && sim.World.IsAlive(hat) && sim.World.Get<Thing>(hat).Spawned && Sim.Maps.Map.Distance(sim.World.Get<Thing>(hat).Cell, at) <= HatReach
            ? hat
            : Entity.Null;

    /// <summary>Silver lying by a hat.</summary>
    public static int InHat(Simulation sim, Entity hat) => Tossed(sim, hat).Sum(c => sim.World.Get<Stack>(c).Count);

    /// <summary>
    /// A listener who's heard a good while tips: into the hat, if there's one in reach, once a day. Returns what went in.
    /// </summary>
    public static int Tip(Simulation sim, Entity listener, Entity musician, bool danced)
    {
        var w = sim.World;
        if (w.TryGet<Tipped>(listener, out var t) && t.Day == sim.Clock.Day)
            return 0;
        var hat = HatOf(sim, musician, w.Get<Position>(listener).Cell);
        if (hat.IsNull)
            return 0;
        var amount = ForListening + (danced ? ForDancing : 0) + (w.Has<Adventurer>(listener) ? ForAnAdventurer : 0);
        var paid = Coin.PayDown(sim, listener, w.Get<Thing>(hat).Cell, amount);
        if (paid > 0)
        {
            w.Add(listener, new Tipped { Day = sim.Clock.Day });
            Landed(sim, musician, listener, paid);
            Claim(sim, musician, hat);
        }
        return paid;
    }

    /// <summary>
    /// <paramref name="listener"/> calls out for <paramref name="tune"/>: said aloud, so who heard is who knows. The
    /// musician's window shows it only if they heard it.
    /// </summary>
    public static void Ask(Simulation sim, Entity listener, Entity musician, SongDef tune)
    {
        var said = Barks.Say(sim, listener, "musician.request", Volume.Say, "Give us {thing}, musician!", thing: tune.Label);
        var heard = said != null && said.Listeners.Exists(h => h.Listener == musician);
        sim.World.Add(listener, new TuneRequest { Musician = musician, Tune = tune.Name, Until = sim.Clock.Tick + Wanted, Heard = heard });
    }

    /// <summary>The tune <paramref name="listener"/> wants of <paramref name="musician"/> now, if any.</summary>
    public static string? Wants(Simulation sim, Entity listener, Entity musician) =>
        sim.World.TryGet<TuneRequest>(listener, out var r) && r.Musician == musician && r.Until > sim.Clock.Tick ? r.Tune : null;

    /// <summary>Their tune played for them: a good tip (whether or not they've tipped today), and the call answered.</summary>
    public static int Answered(Simulation sim, Entity listener, Entity musician)
    {
        var w = sim.World;
        w.Remove<TuneRequest>(listener);
        MoodSystem.Remember(sim, listener, sim.Defs.ThoughtOrNull("HeardTheirTune"));
        var hat = HatOf(sim, musician, w.Get<Position>(listener).Cell);
        var paid = hat.IsNull ? 0 : Coin.PayDown(sim, listener, w.Get<Thing>(hat).Cell, ForTheirTune);
        if (paid > 0)
        {
            Landed(sim, musician, listener, paid);
            Claim(sim, musician, hat);
        }
        return paid;
    }

    /// <summary>A tip landed by the hat: kept on the musician's session, so their window can show who gave it.</summary>
    private static void Landed(Simulation sim, Entity musician, Entity from, int silver)
    {
        if (sim.World.Get<WorkSession>(musician) is not PlaySession s)
            return;
        s.Tips.Add(new PlaySession.Tip(sim.Clock.Tick, from, silver));
        if (s.Tips.Count > PlaySession.TipsKept)
            s.Tips.RemoveAt(0);
    }
}
