using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Work;

/// <summary>
/// Looks after hands-on jobs: stops one if the worker can't carry on, the target goes or the
/// player has gone quiet, and applies the result when it's done. Play itself comes from the
/// worker's inputs (<see cref="WorkInputBatchCommand"/>), so the rest of the sim runs as usual around it.
/// </summary>
public sealed class WorkSystem : ISimSystem
{
    /// <summary>Silence from the worker after which the job is dropped.</summary>
    public const int StallTicks = SimClock.TicksPerSecond * 5;

    public string Name => "Work";
    public int Interval => 1;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        if (w.Count<WorkSession>() == 0)
            return;
        foreach (var e in w.Query<WorkSession>())
        {
            var session = w.Get<WorkSession>(e);
            if (!Able(sim, e))
            {
                Stop(sim, e, null);
            }
            else if (!session.StillValid(sim))
            {
                Stop(sim, e, $"{session.Title}: stopped.");
            }
            else if (session.Done)
            {
                session.Finish(sim);
                w.Remove<WorkSession>(e);
                SkillRules.Learn(sim, e, session.Skill, session.Xp);
            }
            else if (session.Failed != null)
            {
                w.Remove<WorkSession>(e);
                sim.Messages.Add(MessageKind.Warning, session.Failed, w.Get<Position>(e).Cell);
            }
            else if (sim.Clock.Tick - session.LastInputTick > StallTicks)
            {
                Stop(sim, e, $"{session.Title}: left unfinished.");
            }
        }
    }

    private static bool Able(Simulation sim, Entity e)
    {
        var w = sim.World;
        return w.Has<Avatar>(e) && w.TryGet<Creature>(e, out var c) && !c.Dead && !c.Downed && !w.Has<Carried>(e) && !w.Get<Mind>(e).Asleep;
    }

    /// <summary>Begin a job, replacing any other: the worker stops whatever else they were doing.</summary>
    public static void Start(Simulation sim, Entity worker, WorkSession session)
    {
        var w = sim.World;
        session.Worker = worker;
        session.StartTick = sim.Clock.Tick;
        session.LastInputTick = sim.Clock.Tick;
        session.Hands = Hands.Of(sim, worker, session.Skill);
        session.Rand = new SimRandom(sim.Rand.NextUInt());
        session.Begin();
        AiSystem.EndJob(sim, worker, w.Get<Mind>(worker), false);
        if (w.TryGet<Avatar>(worker, out _))
        {
            ref var avatar = ref w.Get<Avatar>(worker);
            avatar.MoveDir = int2.Zero;
            avatar.WorkHeld = false;
            avatar.HasPending = false;
        }
        if (w.TryGet<WorkSession>(worker, out var before))
            before.Interrupted(sim);
        w.Remove<WorkSession>(worker);
        w.Add(worker, session);
    }

    /// <summary>End a job unfinished. What it used stays where it is.</summary>
    public static void Stop(Simulation sim, Entity worker, string? message)
    {
        var w = sim.World;
        if (w.TryGet<WorkSession>(worker, out var session))
            session.Interrupted(sim);
        if (!w.Remove<WorkSession>(worker) || message == null)
            return;
        sim.Messages.Add(MessageKind.Info, message, w.Get<Position>(worker).Cell);
    }
}

/// <summary>
/// A batch of a worker's inputs, replayed onto their session. Off the network, so a batch that's
/// out of order, for another job, or claims to be further along than time allows is dropped whole.
/// </summary>
public sealed record WorkInputBatchCommand(Entity Actor, long SessionId, IReadOnlyList<WorkInput> Inputs, int UpToStep) : ISimCommand, IActorCommand
{
    /// <summary>How far ahead of the sim's clock a client may be, for frames that ran a little fast.</summary>
    public const int SlackSteps = 30;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Actor) || !w.TryGet<WorkSession>(Actor, out var session) || session.StartTick != SessionId)
            return;
        if (UpToStep <= session.StepsDone || UpToStep > sim.Clock.Tick - session.StartTick + SlackSteps)
            return;
        var last = session.StepsDone;
        foreach (var input in Inputs)
        {
            if (input.Step < last || input.Step >= UpToStep)
                return;
            last = input.Step;
        }
        session.Advance(Inputs, UpToStep);
        session.LastInputTick = sim.Clock.Tick;
    }
}

/// <summary>The worker puts the job down.</summary>
public sealed record StopWorkCommand(Entity Actor) : ISimCommand, IActorCommand
{
    public void Apply(Simulation sim)
    {
        if (sim.World.IsAlive(Actor) && sim.World.TryGet<WorkSession>(Actor, out var session))
            WorkSystem.Stop(sim, Actor, $"{session.Title}: stopped.");
    }
}
