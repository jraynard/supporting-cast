using Narsil.Sim.Core;

namespace SupportingCast.Sim.Work;

/// <summary>
/// Runs a copy of the worker's session ahead of the sim, for instant feedback, and sends the
/// inputs that drove it in batches for the sim to replay. Used by the game and by
/// <see cref="Bots.BotPlayer"/> alike. The copy only ever shows; if the sim ends the job, it goes.
/// </summary>
public sealed class WorkPredictor
{
    /// <summary>Steps between batches when nothing is being pressed, so the sim knows the worker is still there.</summary>
    public const int SendEvery = 6;

    private readonly List<WorkInput> _unsent = [];
    private readonly List<WorkInput> _frame = [];
    private WorkSession? _local;
    private int _sentUpTo;
    private bool _held;
    private int _axis, _axis2;

    /// <summary>The session as the player should see it; null when there's no job.</summary>
    public WorkSession? Session => _local;

    public void Reset()
    {
        _local = null;
        _unsent.Clear();
        _held = false;
        _axis = _axis2 = 0;
    }

    /// <summary>Play this frame's <paramref name="steps"/> with its input, and send a batch if one is due.</summary>
    public void Update(Simulation sim, Entity worker, int steps, bool press, bool held, int axis, Action<ISimCommand> send, int axis2 = 0)
    {
        if (!sim.World.IsAlive(worker) || !sim.World.TryGet<WorkSession>(worker, out var real))
        {
            Reset();
            return;
        }
        if (_local == null || _local.StartTick != real.StartTick)
        {
            Reset();
            _local = real.Clone();
            _sentUpTo = real.StepsDone;
        }
        var local = _local;
        if (local.Done || local.Failed != null)
            return;

        var at = local.StepsDone;
        _frame.Clear();
        if (press)
            _frame.Add(new WorkInput(at, WorkInputKind.Press));
        if (held != _held)
            _frame.Add(new WorkInput(at, WorkInputKind.Hold, held ? 1 : 0));
        if (axis != _axis)
            _frame.Add(new WorkInput(at, WorkInputKind.Axis, axis));
        if (axis2 != _axis2)
            _frame.Add(new WorkInput(at, WorkInputKind.Axis2, axis2));
        (_held, _axis, _axis2) = (held, axis, axis2);
        if (_frame.Count > 0 && steps == 0)
            steps = 1; // an input needs a step to land on
        local.Advance(_frame, at + steps);
        _unsent.AddRange(_frame);

        var finished = local.Done || local.Failed != null;
        if (local.StepsDone > _sentUpTo && (_unsent.Count > 0 || finished || local.StepsDone - _sentUpTo >= SendEvery))
        {
            send(new WorkInputBatchCommand(worker, local.StartTick, _unsent.ToArray(), local.StepsDone));
            _unsent.Clear();
            _sentUpTo = local.StepsDone;
        }
    }
}
