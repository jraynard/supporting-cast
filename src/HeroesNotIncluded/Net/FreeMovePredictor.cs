using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;

namespace HeroesNotIncluded.Net;

/// <summary>
/// Co-op's prediction for free movement (<see cref="FreeMoveSystem"/>), as <see cref="AvatarPredictor"/>
/// is for the grid: the local player's keys move a copy of their character at once, tick for tick by
/// the sim's own rules (<see cref="FreeMoveSystem.Wanted"/>, <see cref="FreeMoveSystem.Slide"/>), and
/// that copy is what's drawn. Its position goes with each command (<see cref="Sim.Core.FreeMoveCommand"/>'s
/// At), and the sim takes it when it's near (<see cref="FreeMoveSystem.Claim"/>), so the host ends up
/// where the player saw themselves. Presentation only: when the two part (knocked down, a job's walk,
/// too far apart) the picture eases back to the sim.
/// </summary>
public sealed class FreeMovePredictor
{
    /// <summary>Further than this (cells) from the sim and the prediction is given up.</summary>
    private const float MaxDivergence = 3f;

    /// <summary>How fast a correction fades (per second, exponential).</summary>
    private const float BlendRate = 12f;

    private float3 _pos;
    private int3 _cell;
    private float2 _vel;
    private float2 _dashDir;
    private int _dashTicks;
    private float _ticks;
    private float3 _offset;

    /// <summary>True while the drawn character is the predicted one.</summary>
    public bool Active { get; private set; }

    /// <summary>Where the player is on their own screen, to send with a command.</summary>
    public float3 Pos => _pos;
    public int Level => _cell.Z;

    /// <summary>Advance by a frame with the keys held now. <paramref name="speed"/> is the game speed multiple (0 paused).</summary>
    public void Update(Simulation sim, Entity me, float2 wish, float dt, float speed)
    {
        _offset *= MathF.Exp(-BlendRate * dt);
        if (!Eligible(sim, me))
        {
            GiveUp(sim, me);
            return;
        }
        var w = sim.World;
        var simPos = w.Get<Position>(me).Pos;
        if (!Active)
        {
            if (wish == float2.Zero)
                return;
            (_pos, _cell, _vel, _ticks, _dashTicks) = (simPos, w.Get<Position>(me).Cell, w.Get<FreeMover>(me).Vel, 0, 0);
            Active = true;
        }
        var drawn = w.Get<FreeMover>(me).Drawn;
        // Let go, it stops dead where you let go: the stop sent says that spot (FreeMoveSystem.Claim, stopped).
        if (wish == float2.Zero && _dashTicks == 0)
            _vel = float2.Zero;
        _ticks += dt * speed * SimClock.TicksPerSecond;
        for (var guard = 0; _ticks >= 1f && guard < 8; guard++)
        {
            _ticks -= 1f;
            var dashing = _dashTicks > 0;
            var want = FreeMoveSystem.Wanted(sim, me, wish, drawn, dashing ? _dashDir : null);
            _vel = dashing ? want : _vel + (want - _vel) * FreeMoveSystem.Ease;
            if (dashing)
                _dashTicks--;
            if (want == float2.Zero && _vel.X * _vel.X + _vel.Y * _vel.Y < FreeMoveSystem.StopBelow * FreeMoveSystem.StopBelow)
                _vel = float2.Zero;
            else
                (_pos, _cell, _vel) = FreeMoveSystem.Slide(sim, _pos, _cell, _vel);
        }
        var d = _pos - simPos;
        if (MathF.Abs(d.X) > MaxDivergence || MathF.Abs(d.Y) > MaxDivergence || MathF.Abs(d.Z) > 1.5f)
        {
            GiveUp(sim, me);
            return;
        }
        // Stood still and the sim's caught up: hand back, seamlessly.
        if (wish == float2.Zero && _vel == float2.Zero && _dashTicks == 0 && d.X * d.X + d.Y * d.Y < 0.0025f
            && w.Get<FreeMover>(me).Vel == float2.Zero)
            GiveUp(sim, me);
    }

    /// <summary>A dodge: the same dash the sim will make (<see cref="FreeMoveSystem.Dash"/>), at once.</summary>
    public void Dash(Simulation sim, Entity me, float2 dir)
    {
        if (!Active && Eligible(sim, me))
        {
            var w = sim.World;
            (_pos, _cell, _vel, _ticks) = (w.Get<Position>(me).Pos, w.Get<Position>(me).Cell, w.Get<FreeMover>(me).Vel, 0);
            Active = true;
        }
        var len = MathF.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
        var aim = sim.World.Get<FreeMover>(me).Aim;
        _dashDir = len > 0.1f ? dir * (1f / len) : new float2(-MathF.Cos(aim), -MathF.Sin(aim));
        _dashTicks = FreeMoveSystem.DashTicks;
    }

    /// <summary>Where to draw the character: the prediction, or the sim's position plus a fading correction.</summary>
    public float3 DrawPos(float3 simDrawPos) => Active ? _pos : simDrawPos + _offset;

    private void GiveUp(Simulation sim, Entity me)
    {
        if (Active && sim.World.IsAlive(me))
            _offset = _pos - sim.World.Get<Position>(me).Pos;
        Active = false;
    }

    public void Reset()
    {
        Active = false;
        _offset = default;
    }

    /// <summary>Only the keys' walk is predicted: not a job's walk, being carried, down or asleep.</summary>
    private static bool Eligible(Simulation sim, Entity me)
    {
        var w = sim.World;
        return w.IsAlive(me) && w.Has<FreeMover>(me) && !w.Has<Carried>(me) && w.Get<Creature>(me).Able
            && !w.Get<Mind>(me).Asleep && !w.Get<Mover>(me).Moving;
    }
}
