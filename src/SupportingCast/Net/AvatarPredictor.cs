using SupportingCast.Sim;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Ecs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Net;

/// <summary>
/// Client-side prediction for the local player's WASD walk in co-op. A move command only takes
/// effect once it has been to the host and back inside a turn, so without this the avatar (and
/// the camera following it) starts late and overshoots when the key is let go. The predictor walks
/// a copy of the avatar straight away over the same <see cref="AvatarSystem.StepToward"/> steps
/// and speed the sim uses, and the game draws that instead. Turns and stops are sent with the cell
/// where they happened (<see cref="AvatarMoveCommand.At"/>), so the sim catches up onto the same
/// cell. Purely presentation: if the two ever disagree (blocked by something new, knocked down,
/// started a job) the picture eases back to the sim.
/// </summary>
public sealed class AvatarPredictor
{
    /// <summary>Further than this (cells) from the sim and the prediction is given up.</summary>
    private const float MaxDivergence = 2.5f;

    /// <summary>Seconds to wait, stopped, for the sim to arrive before giving up on it.</summary>
    private const float SettleTimeout = 1f;

    /// <summary>How fast a correction fades out (per second, exponential).</summary>
    private const float BlendRate = 12f;

    private readonly AvatarSystem _steps = new();
    private int3 _from, _to;
    private float _progress = 1f;
    private int2 _dir;
    private bool _stopping;
    private int3 _stopAt;
    private float _settling;
    private float3 _offset;

    /// <summary>True while the drawn avatar is the predicted one.</summary>
    public bool Active { get; private set; }

    private float3 Predicted => Position.CenterOf(_from) + (Position.CenterOf(_to) - Position.CenterOf(_from)) * Math.Min(_progress, 1f);

    /// <summary>
    /// The held direction changed: start or steer the predicted walk, and return the cell to name
    /// in the command (null: apply at once, as when starting from a standstill).
    /// </summary>
    public int3? DirectionChanged(Simulation sim, Entity avatar, int2 dir)
    {
        if (!Eligible(sim, avatar))
        {
            Active = false;
            return null;
        }
        if (!Active)
        {
            if (dir == int2.Zero)
                return null;
            var mover = sim.World.Get<Mover>(avatar);
            var cell = sim.World.Get<Position>(avatar).Cell;
            if (mover.Moving)
                (_from, _to, _progress) = (mover.StepFrom, mover.Path![mover.PathIndex], mover.StepProgress);
            else
                (_from, _to, _progress) = (cell, cell, 1f);
            _offset = default;
            Active = true;
            _dir = dir;
            _stopping = false;
            return null;
        }
        // Mid-step the sim finishes the step too (steps can't be cancelled), so the change happens
        // on the cell being stepped onto.
        _dir = dir;
        _stopping = dir == int2.Zero;
        _stopAt = _to;
        _settling = 0;
        return _to;
    }

    /// <summary>Advance the predicted walk by a frame. <paramref name="speed"/> is the game speed multiple (0 paused).</summary>
    public void Update(Simulation sim, Entity avatar, float dt, float speed)
    {
        _offset *= MathF.Exp(-BlendRate * dt);
        if (!Active)
            return;
        if (!Eligible(sim, avatar))
        {
            GiveUp(sim, avatar);
            return;
        }

        var map = sim.Map;
        var perTick = MovementSystem.WalkSpeedPerTick(sim, avatar);
        var budget = dt * speed * SimClock.TicksPerSecond; // ticks of walking this frame
        for (var guard = 0; budget > 0 && guard < 8; guard++)
        {
            if (_progress < 1f)
            {
                var cost = map.InBounds(_to) ? map.PathCost[map.Idx(_to)] : 0f;
                if (cost <= 0)
                {
                    // Blocked (something built there): stay put; the sim will say where we are.
                    (_to, _progress) = (_from, 1f);
                    break;
                }
                var perStep = perTick / MovementSystem.GroundStepLength(_from, _to, cost);
                var need = (1f - _progress) / perStep;
                if (need > budget)
                {
                    _progress += budget * perStep;
                    break;
                }
                budget -= need;
                _progress = 1f;
            }
            // On a cell: walk on, unless this is where we stop.
            if (_dir == int2.Zero || (_stopping && _to == _stopAt) || _steps.StepToward(map, _to, _dir) is not { } next)
                break;
            (_from, _to, _progress) = (_to, next, 0f);
        }

        var sim3 = sim.World.Get<Position>(avatar).Pos;
        var d = Predicted - sim3;
        if (MathF.Abs(d.X) > MaxDivergence || MathF.Abs(d.Y) > MaxDivergence || MathF.Abs(d.Z) > MaxDivergence)
        {
            GiveUp(sim, avatar);
            return;
        }
        if (_progress >= 1f && (_dir == int2.Zero || _stopping))
        {
            // Standing where we stopped: hand back to the sim once it has arrived (seamless), or after a while.
            var a = sim.World.Get<Avatar>(avatar);
            var arrived = sim.World.Get<Position>(avatar).Cell == _to && !sim.World.Get<Mover>(avatar).Moving
                && a.MoveDir == int2.Zero && !a.HasPending;
            _settling += dt;
            if (arrived || _settling > SettleTimeout)
                GiveUp(sim, avatar);
        }
    }

    /// <summary>Where to draw the avatar: the prediction, or the sim's position plus a fading correction.</summary>
    public float3 DrawPos(float3 simDrawPos) => Active ? Predicted : simDrawPos + _offset;

    /// <summary>Stop predicting, easing from what was shown to the sim's position.</summary>
    private void GiveUp(Simulation sim, Entity avatar)
    {
        if (Active && sim.World.IsAlive(avatar))
            _offset = Predicted - sim.World.Get<Position>(avatar).Pos;
        Active = false;
    }

    /// <summary>Forget everything (the sim was replaced).</summary>
    public void Reset()
    {
        Active = false;
        _offset = default;
    }

    /// <summary>Only plain walking is predicted: not being carried or lying down.</summary>
    private static bool Eligible(Simulation sim, Entity avatar)
    {
        var w = sim.World;
        if (!w.IsAlive(avatar) || !w.Has<Avatar>(avatar) || w.Has<Carried>(avatar))
            return false;
        var c = w.Get<Creature>(avatar);
        if (c.Dead || c.Downed || w.Get<Mind>(avatar).Asleep)
            return false;
        var cell = w.Get<Position>(avatar).Cell;
        return sim.Map.InBounds(cell) && sim.Map.Standable(sim.Map.Idx(cell));
    }
}
