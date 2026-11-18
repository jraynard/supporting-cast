using Narsil.Sim.Core;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// Runs a copy of the worker's session ahead of the sim, for instant feedback, and sends the
/// inputs that drove it in batches for the sim to replay. Used by the game and by
/// <see cref="Bots.BotPlayer"/> alike. The copy only ever shows; if the sim ends the job, it goes.
/// It's kept within <see cref="Lead"/> steps of the sim's clock, and each batch carries every input
/// the sim hasn't played yet: a co-op player whose sim fell behind the wall clock once sent batches
/// too far ahead to be taken, so their last press never arrived and the job was "left unfinished".
/// </summary>
public sealed class WorkPredictor
{
    /// <summary>Steps between batches when nothing is being pressed, so the sim knows the worker is still there.</summary>
    public const int SendEvery = 6;

    /// <summary>How far the copy may run ahead of the sim's clock (well inside <see cref="WorkInputBatchCommand.SlackSteps"/>).</summary>
    public const int Lead = WorkInputBatchCommand.SlackSteps * 2 / 3;

    /// <summary>Inputs kept for sending again until the sim has played past them (a cap against a sim that's gone for good).</summary>
    public const int MaxKept = 256;

    private readonly List<WorkInput> _kept = [];
    private readonly List<WorkInput> _pending = [];
    private WorkSession? _local;
    private int _sentUpTo;
    private bool _held;
    private int _axis, _axis2;
    private int _point = -1;

    /// <summary>The session as the player should see it; null when there's no job.</summary>
    public WorkSession? Session => _local;

    public void Reset()
    {
        _local = null;
        _kept.Clear();
        _pending.Clear();
        _held = false;
        _axis = _axis2 = 0;
        _point = -1;
    }

    /// <summary>
    /// Play this frame's <paramref name="steps"/> with its input, and send a batch if one is due.
    /// <paramref name="rune"/> is a numbered key pressed this frame (0 for 1), or -1; <paramref name="act"/>
    /// the game's other action pressed this frame (<see cref="WorkInputKind.Act"/>); <paramref name="point"/>
    /// the mouse's place on the panel (<see cref="WorkSession.PackPoint"/>), or -1 when it's not over it:
    /// sent only when it's moved; <paramref name="click"/> a left click on it this frame
    /// (<see cref="WorkInputKind.Click"/>), or -1.
    /// </summary>
    public void Update(Simulation sim, Entity worker, int steps, bool press, bool held, int axis, Action<ISimCommand> send, int axis2 = 0, int rune = -1, bool act = false,
        int point = -1, int click = -1, IReadOnlyList<int>? custom = null)
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
        else if (_local.Revision != real.Revision)
        {
            // A partner's beat reached the sim's session (Pairs): take it again from there, and play
            // what's been pressed since on top, back to where the copy had got.
            var ahead = _local.StepsDone;
            var again = real.Clone();
            again.Advance(_kept.FindAll(i => i.Step >= real.StepsDone), ahead);
            _local = again;
        }
        var local = _local;
        local.ClearOutbox(); // only the sim's copy sends its partner anything
        if (!local.Done && local.Failed == null)
        {
            // Inputs wait here for a step to land on (the copy may be held at the sim's clock).
            var at = local.StepsDone;
            if (press)
                _pending.Add(new WorkInput(at, WorkInputKind.Press));
            if (held != _held)
                _pending.Add(new WorkInput(at, WorkInputKind.Hold, held ? 1 : 0));
            if (axis != _axis)
                _pending.Add(new WorkInput(at, WorkInputKind.Axis, axis));
            if (axis2 != _axis2)
                _pending.Add(new WorkInput(at, WorkInputKind.Axis2, axis2));
            if (rune >= 0)
                _pending.Add(new WorkInput(at, WorkInputKind.Rune, rune));
            if (act)
                _pending.Add(new WorkInput(at, WorkInputKind.Act));
            if (point >= 0 && point != _point)
                _pending.Add(new WorkInput(at, WorkInputKind.Point, point));
            if (point >= 0)
                _point = point;
            if (click >= 0)
                _pending.Add(new WorkInput(at, WorkInputKind.Click, click)); // after the point: the session knows where it is first
            if (custom != null)
                foreach (var value in custom)
                    _pending.Add(new WorkInput(at, WorkInputKind.Custom, value)); // a mod's own (a note)
            (_held, _axis, _axis2) = (held, axis, axis2);
            if (_pending.Count > 0 && steps == 0)
                steps = 1; // an input needs a step to land on
            var room = (int)Math.Min(int.MaxValue, sim.Clock.Tick - real.StartTick) + Lead - at;
            steps = Math.Min(steps, Math.Max(0, room));
            if (steps > 0)
            {
                local.Advance(_pending, at + steps);
                _kept.AddRange(_pending);
                _pending.Clear();
            }
        }

        // What the sim has played needn't go again.
        _kept.RemoveAll(i => i.Step < real.StepsDone);
        if (_kept.Count > MaxKept)
            _kept.RemoveRange(0, _kept.Count - MaxKept);
        var finished = local.Done || local.Failed != null;
        var fresh = local.StepsDone > _sentUpTo;
        // Sent again, with all the sim hasn't played, while it's behind: a batch it couldn't take is
        // made good by the next. Once the copy's finished, about every SendEvery steps of the clock.
        var behind = real.StepsDone < local.StepsDone && sim.Clock.Tick % SendEvery == 0;
        var newInput = _kept.Count > 0 && _kept[^1].Step >= _sentUpTo;
        if ((fresh && (newInput || finished || local.StepsDone - _sentUpTo >= SendEvery)) || (finished && behind))
        {
            send(new WorkInputBatchCommand(worker, local.StartTick, _kept.ToArray(), local.StepsDone));
            _sentUpTo = local.StepsDone;
        }
    }
}
