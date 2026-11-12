using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;

namespace HeroesNotIncluded.Rendering;

/// <summary>
/// Render-side interpolation between sim ticks. The sim runs at a fixed 60 Hz, but frames land
/// on ticks unevenly (0, 1 or 2 per frame), so drawing the latest position makes creatures and
/// the follow camera move in stutters. Before each tick <see cref="Capture"/> remembers where
/// every moving creature was; the renderer then draws <see cref="Smoothed"/>, which is
/// <see cref="Alpha"/> of the way from there to the current position (one tick behind, but
/// smooth). Pure presentation: nothing here feeds back into the sim.
/// </summary>
public sealed class Interpolation
{
    /// <summary>Further than this between two ticks is a teleport (a load, travel, a rescue): snap.</summary>
    private const float SnapDistance = 2f;

    private float3[] _prev = [];
    private int[] _generation = [];
    private long _capturedTick = -1;

    /// <summary>How far the picture is between the previous tick and the current one, 0..1.</summary>
    public float Alpha { get; set; } = 1f;

    /// <summary>Remember the current positions as the previous tick's. Call just before <c>Simulation.Tick</c>.</summary>
    public void Capture(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Each<Mover>())
        {
            if (!w.TryGet<Position>(e, out var p))
                continue;
            if (e.Index >= _prev.Length)
            {
                var size = Math.Max(64, Math.Max(_prev.Length * 2, e.Index + 1));
                Array.Resize(ref _prev, size);
                Array.Resize(ref _generation, size); // new slots are 0: never a match
            }
            _prev[e.Index] = p.Pos;
            _generation[e.Index] = e.Generation + 1; // +1 so a fresh (zeroed) slot never matches
        }
        _capturedTick = sim.Clock.Tick;
    }

    /// <summary>
    /// Forget everything (a load, a resync, switching maps): the next frames draw the current
    /// positions until a tick has been captured again.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_generation);
        _capturedTick = -1;
        Alpha = 1f;
    }

    /// <summary>
    /// Where to draw <paramref name="e"/>, whose sim position is <paramref name="current"/>.
    /// Only valid right after the tick that followed the last <see cref="Capture"/>; otherwise
    /// (nothing captured, a new entity, a jump) it is just <paramref name="current"/>.
    /// </summary>
    public float3 Smoothed(Simulation sim, Entity e, float3 current)
    {
        if (_capturedTick != sim.Clock.Tick - 1 || e.Index < 0 || e.Index >= _prev.Length
            || _generation[e.Index] != e.Generation + 1)
            return current;
        var prev = _prev[e.Index];
        var d = current - prev;
        if (MathF.Abs(d.X) > SnapDistance || MathF.Abs(d.Y) > SnapDistance || MathF.Abs(d.Z) > SnapDistance)
            return current;
        return prev + d * Alpha;
    }
}
