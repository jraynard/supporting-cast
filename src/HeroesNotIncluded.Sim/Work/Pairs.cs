using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// The helper's half of a two-person job (<see cref="Pairs"/>): its own game, with its own skill
/// and its own way to go wrong, played against what the lead calls. It follows the lead from one
/// session to the next (a heat at the anvil, then the next once the piece is hot again), and puts
/// its tool down once the lead's work is done.
/// </summary>
public abstract class HelperSession : WorkSession
{
    /// <summary>Whose work it is, as named when it began (the lead is <see cref="WorkSession.Partner"/>).</summary>
    public string LeadName { get; internal set; } = "";

    /// <summary>A new session of the lead's that this helper goes on helping with (the next heat at the same anvil).</summary>
    public abstract bool Follows(WorkSession lead);

    /// <summary>The lead's still at the work this helper's for, between sessions too (a piece back in the fire).</summary>
    protected abstract bool LeadAtIt(Simulation sim);

    /// <summary>The helper's where the work is.</summary>
    protected abstract bool InPlace(Simulation sim);

    public override bool StillValid(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Partner) || !w.TryGet<Creature>(Partner, out var lead) || lead.Dead)
            return false;
        if (!LeadAtIt(sim))
        {
            Done = true; // the work's finished: down tools, and the help's counted
            return true;
        }
        return InPlace(sim);
    }

    public override void Finish(Simulation sim) =>
        sim.Messages.Add(MessageKind.Info, $"{LeadName}'s work is done: there's no more need of you there.", sim.World.Get<Position>(Worker).Cell);

    protected override void WriteState(SaveWriter w) => w.String(LeadName);

    protected override void ReadState(SaveReader r) => LeadName = r.String();
}

/// <summary>
/// An AI villager lending a player a hand (<see cref="HelpJob"/>): it answers the lead's calls
/// steadily, never brilliantly, a moment late. Sim-side only.
/// </summary>
public sealed class Helping
{
    public Entity Lead;

    /// <summary>When the answer to the last call comes down, or -1 with nothing to answer.</summary>
    public long AnswerAt = -1;

    public int Blows;
}

/// <summary>
/// Two pairs of hands (docs/DESIGN.md, "Two pairs of hands"). Some work is too much for one: a great
/// blade wants a striker at the sledge. The lead's session and the helper's are each a game of
/// their own, each driven only by its own player's inputs, so both can be run ahead on their own
/// screens; what passes between them goes as <see cref="Beat"/>s the sim hands over between steps
/// (<see cref="Exchange"/>), and the predictor takes a session again from the sim when one arrives.
/// Help never makes ordinary work easier: it opens up work one pair of hands can't do, and bad help
/// is worse than none.
/// </summary>
public static class Pairs
{
    /// <summary>The least an AI villager may think of someone and still come when they call for a hand.</summary>
    public const float MinOpinion = -10f;

    /// <summary>The longest an AI helper stays, should the work never end.</summary>
    public const int HelpTicks = SimClock.TicksPerHour * 3;

    /// <summary>How long after a call an AI helper's blow lands, in ticks (a moment to see it, then the swing).</summary>
    public const int AnswerMin = 18, AnswerMax = 34;

    /// <summary>Someone's answering this lead's work: a player's helper session, or an AI villager at it.</summary>
    public static bool Linked(Simulation sim, WorkSession lead)
    {
        var w = sim.World;
        var p = lead.Partner;
        if (!w.IsAlive(p))
            return false;
        return (w.TryGet<WorkSession>(p, out var other) && other.Partner == lead.Worker)
            || (w.TryGet<Helping>(p, out var help) && help.Lead == lead.Worker);
    }

    /// <summary>This person's at work that wants a second pair of hands, and has none.</summary>
    public static bool WantsHand(Simulation sim, Entity lead) =>
        sim.World.IsAlive(lead) && sim.World.TryGet<WorkSession>(lead, out var s) && s.TakesHelper && !Linked(sim, s);

    /// <summary>Whose work at this station someone could lend a hand to, or Entity.Null.</summary>
    public static Entity WantsHandAt(Simulation sim, Entity helper, Entity station) =>
        sim.World.IsAlive(station) && sim.World.TryGet<Thing>(station, out var t) ? WantsHandAt(sim, helper, t.Cell) : Entity.Null;

    /// <summary>Whose work centred on this cell (a station, a body, a wagon) someone could lend a hand to, or Entity.Null.</summary>
    public static Entity WantsHandAt(Simulation sim, Entity helper, int3 cell)
    {
        var w = sim.World;
        if (w.Has<WorkSession>(helper))
            return Entity.Null;
        foreach (var e in w.Query<WorkSession>())
        {
            var s = w.Get<WorkSession>(e);
            if (e != helper && s.TakesHelper && !Linked(sim, s) && s.Focus(sim) == cell)
                return e;
        }
        return Entity.Null;
    }

    /// <summary>
    /// Whose work wanting a hand is at or right beside this cell (its focus, or the lead), with the work in the helper's
    /// reach, or Entity.Null. F near work for two lends the hand before anything else there: a busy workshop's stacks,
    /// benches and seats kept offering themselves to a would-be pitman.
    /// </summary>
    public static Entity WantsHandNear(Simulation sim, Entity helper, int3 cell, int3 here)
    {
        var w = sim.World;
        if (w.Has<WorkSession>(helper))
            return Entity.Null;
        foreach (var e in w.Query<WorkSession>())
        {
            var s = w.Get<WorkSession>(e);
            if (e == helper || !s.TakesHelper || Linked(sim, s) || s.Focus(sim) is not { } at || at.Z != cell.Z)
                continue;
            var near = Math.Max(Math.Abs(at.X - cell.X), Math.Abs(at.Y - cell.Y)) <= 1
                || w.TryGet<Position>(e, out var p) && p.Cell == cell;
            if (near && AvatarSystem.InReach(here, at))
                return e;
        }
        return Entity.Null;
    }

    /// <summary>Lend a hand to this lead's work: the helper's session begins, and the two are partners.</summary>
    public static bool Join(Simulation sim, Entity helper, Entity lead)
    {
        var w = sim.World;
        if (helper == lead || !w.IsAlive(lead) || !w.TryGet<WorkSession>(lead, out var s) || !s.TakesHelper || Linked(sim, s)
            || s.HelperFor(sim, helper) is not { } h)
            return false;
        h.LeadName = w.Get<Creature>(lead).Name;
        WorkSystem.Start(sim, helper, h);
        h.Partner = lead;
        s.Partner = helper;
        s.Revise(); // the lead's copy running ahead learns it has a partner now
        return true;
    }

    /// <summary>
    /// A lead begins a new session (the next heat): whoever was helping with the last goes on with
    /// this one, a player's helper session if it follows it, or an AI villager standing by.
    /// </summary>
    internal static void Relink(Simulation sim, Entity worker, WorkSession session)
    {
        if (!session.TakesHelper)
            return;
        var w = sim.World;
        foreach (var e in w.Query<WorkSession>())
            if (e != worker && w.Get<WorkSession>(e) is HelperSession h && h.Partner == worker && h.Follows(session))
            {
                session.Partner = e;
                session.Revise();
                return;
            }
        foreach (var e in w.Query<Helping>())
            if (w.Get<Helping>(e).Lead == worker)
            {
                session.Partner = e;
                session.Revise();
                return;
            }
    }

    /// <summary>
    /// The sim's half of the pairing, each tick: what each session sent its partner is handed over,
    /// and AI helpers' answers come down when they're due.
    /// </summary>
    internal static void Exchange(Simulation sim)
    {
        var w = sim.World;
        if (w.Count<WorkSession>() == 0 && w.Count<Helping>() == 0)
            return;
        foreach (var e in w.Query<WorkSession>())
        {
            var s = w.Get<WorkSession>(e);
            if (s.Outbox.Count == 0)
                continue;
            var p = s.Partner;
            if (w.IsAlive(p) && w.TryGet<WorkSession>(p, out var other) && other.Partner == e)
                foreach (var b in s.Outbox)
                    other.Receive(b);
            else if (w.IsAlive(p) && w.TryGet<Helping>(p, out var help) && help.Lead == e)
                foreach (var b in s.Outbox)
                    if (b.Kind == BeatKind.Call)
                        help.AnswerAt = sim.Clock.Tick + AnswerMin + sim.Rand.NextInt(AnswerMax - AnswerMin + 1);
            s.ClearOutbox();
        }
        if (w.Count<Helping>() == 0)
            return;
        foreach (var e in w.Query<Helping>())
        {
            var help = w.Get<Helping>(e);
            // Jobs aren't saved: after a load the helper's job is gone, and so is the help.
            if (!w.TryGet<Mind>(e, out var mind) || mind.Job is not HelpJob job || job.Lead != help.Lead)
            {
                w.Remove<Helping>(e);
                continue;
            }
            if (help.AnswerAt < 0 || sim.Clock.Tick < help.AnswerAt)
                continue;
            help.AnswerAt = -1;
            if (!w.TryGet<WorkSession>(help.Lead, out var lead) || lead.Partner != e)
                continue;
            lead.Receive(lead.AiAnswer(sim, e));
            help.Blows++;
        }
    }

    /// <summary>
    /// An AI helper's answer: steady, never brilliant, better for a practised hand; now and then it
    /// comes down off the beat.
    /// </summary>
    public static Beat AiBlow(Simulation sim, Entity helper, Skill skill = Skill.Crafting)
    {
        var level = SkillRules.LevelOf(sim, helper, skill);
        if (sim.Rand.NextFloat() < Math.Max(0.02f, 0.08f - 0.006f * level))
            return new Beat(BeatKind.Offbeat, 0.6f);
        return new Beat(BeatKind.Strike, Math.Clamp(0.4f + 0.035f * level + sim.Rand.Range(-0.12f, 0.12f), 0.2f, 0.8f));
    }

    /// <summary>
    /// The lead's still at work that's for two: in a session that takes a hand, or between heats
    /// with a great piece in the tongs.
    /// </summary>
    public static bool AtWork(Simulation sim, Entity lead)
    {
        var w = sim.World;
        if (!w.IsAlive(lead) || !w.TryGet<Creature>(lead, out var c) || c.Dead)
            return false;
        if (w.TryGet<WorkSession>(lead, out var s) && s.TakesHelper)
            return true;
        return Smithing.PieceOf(sim, lead) is { IsMend: false, Recipe.TwoHanded: true };
    }

    /// <summary>An AI villager who'd come if this person called for a hand: awake, free, and not ill-disposed toward them.</summary>
    public static bool Willing(Simulation sim, Entity e, Entity asker)
    {
        var w = sim.World;
        if (e == asker || !w.IsAlive(e) || !w.TryGet<Creature>(e, out var c) || c.Dead || c.Downed || c.Faction != Faction.Village
            || Players.Steered(sim, e) || w.Has<Adventurer>(e) || w.Has<Helping>(e) || w.Has<WorkSession>(e)
            || !w.TryGet<Mind>(e, out var mind) || mind.Asleep || mind.Job is HelpJob or FightFireJob)
            return false;
        return MoodRules.Opinion(sim, e, asker) >= MinOpinion;
    }
}

/// <summary>
/// A player at work for two calls for a hand (the quick line "Help!" while at it): it's shouted,
/// and of the AI villagers who hear it clearly, the one who thinks best of them comes.
/// </summary>
public sealed record AskForHandCommand(Entity Actor) : ISimCommand, IActorCommand
{
    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Actor) || !w.TryGet<WorkSession>(Actor, out var s) || !s.TakesHelper || Pairs.Linked(sim, s))
            return;
        if (sim.Speech.Say(sim, Actor, s.AskForHand, Volume.Shout) is not { } said)
            return;
        var best = Entity.Null;
        var bestOpinion = float.MinValue;
        foreach (var h in said.Listeners)
            if (h.Clarity == Clarity.Clear && Pairs.Willing(sim, h.Listener, Actor))
            {
                var opinion = MoodRules.Opinion(sim, h.Listener, Actor);
                if (opinion > bestOpinion || (opinion == bestOpinion && h.Listener.Index < best.Index))
                    (best, bestOpinion) = (h.Listener, opinion);
            }
        if (best.IsNull)
        {
            sim.Messages.Add(MessageKind.Info, "Nobody comes to lend a hand.", w.Get<Position>(Actor).Cell);
            return;
        }
        AiSystem.Order(sim, best, new HelpJob(Actor));
        sim.Speech.Say(sim, best, s.HelpLine, Volume.Say);
    }
}

/// <summary>An AI villager goes to lend a player a hand, and stays at it till the work's done (<see cref="Helping"/>).</summary>
public sealed class HelpJob(Entity lead) : Job
{
    public Entity Lead => lead;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = $"Lending {Get<Creature>(lead).Name} a hand";
        if (!Sim.World.TryGet<WorkSession>(lead, out var s) || s.Focus(Sim) is not { } at)
        {
            Fail("no work to help with");
            yield break;
        }
        foreach (var step in GoTo(at, PathEnd.Touch)) yield return step;
        if (HasFailed)
            yield break;
        if (!Alive(lead) || !Pairs.AtWork(Sim, lead))
        {
            Fail("the work was done");
            yield break;
        }
        Sim.World.Add(Pawn, new Helping { Lead = lead });
        if (Sim.World.TryGet<WorkSession>(lead, out var now) && now.TakesHelper && !Pairs.Linked(Sim, now))
        {
            now.Partner = Pawn;
            now.Revise();
        }
        Report = $"Lending {Get<Creature>(lead).Name} a hand";
        for (var t = 0; t < Pairs.HelpTicks && Alive(lead) && Pairs.AtWork(Sim, lead); t++)
            yield return JobStatus.Running;
    }

    protected override void OnEnd(bool succeeded) => Sim.World.Remove<Helping>(Pawn);
}
