using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;

namespace HeroesNotIncluded.Sim.Combat;

/// <summary>
/// Where a creature has been lately: its position every <see cref="TrailSystem.Every"/> ticks, the
/// newest at <see cref="Head"/>. A player's swing in co-op reaches the host a little after they
/// pressed it; it's judged against where everyone was then (<see cref="Trails.At"/>), so it hits what
/// was in reach on their screen. Saved, so every peer judges it alike.
/// </summary>
public sealed class Trail
{
    public const int Length = 16;
    public readonly float3[] At = new float3[Length];

    /// <summary>The slot written last, and the tick it was written.</summary>
    public int Head;
    public long Tick = -1;
}

/// <summary>Keeps every creature's <see cref="Trail"/>.</summary>
public sealed class TrailSystem : ISimSystem
{
    /// <summary>Ticks between samples: 16 of them reach back just past <see cref="Blows.MaxStampAge"/>.</summary>
    public const int Every = 4;

    public string Name => "Trails";
    public int Interval => Every;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in sim.Creatures())
        {
            if (!w.TryGet<Trail>(e, out var trail))
            {
                trail = new Trail();
                w.Add(e, trail);
                Array.Fill(trail.At, w.Get<Position>(e).Pos);
            }
            trail.Head = (trail.Head + 1) % Trail.Length;
            trail.At[trail.Head] = w.Get<Position>(e).Pos;
            trail.Tick = sim.Clock.Tick;
        }
    }
}

public static class Trails
{
    /// <summary>
    /// Where <paramref name="e"/> stood <paramref name="ago"/> ticks back (between samples, between
    /// them; further back than the trail goes, its oldest). Now, or with no trail, where it is.
    /// </summary>
    public static float3 At(Simulation sim, Entity e, int ago)
    {
        var w = sim.World;
        var now = w.Get<Position>(e).Pos;
        if (ago <= 0 || !w.TryGet<Trail>(e, out var trail) || trail.Tick < 0)
            return now;
        // Steps back from the newest sample, counting the time since it was taken.
        var back = (ago - (sim.Clock.Tick - trail.Tick)) / (float)TrailSystem.Every;
        if (back <= 0)
        {
            // Between the newest sample and now.
            var t = ago / (float)Math.Max(1, sim.Clock.Tick - trail.Tick);
            return now + (trail.At[trail.Head] - now) * Math.Clamp(t, 0f, 1f);
        }
        back = Math.Min(back, Trail.Length - 1);
        var i = (int)back;
        var f = back - i;
        var a = trail.At[(trail.Head - i + Trail.Length) % Trail.Length];
        var b = trail.At[(trail.Head - Math.Min(i + 1, Trail.Length - 1) + Trail.Length) % Trail.Length];
        return a + (b - a) * f;
    }

    internal static void Write(SaveWriter w, Trail t)
    {
        w.Int(t.Head);
        w.Long(t.Tick);
        foreach (var p in t.At)
        {
            w.Float(p.X);
            w.Float(p.Y);
            w.Float(p.Z);
        }
    }

    internal static Trail Read(SaveReader r)
    {
        var t = new Trail { Head = Math.Clamp(r.Int(), 0, Trail.Length - 1), Tick = r.Long() };
        for (var i = 0; i < Trail.Length; i++)
            t.At[i] = new float3(r.Float(), r.Float(), r.Float());
        return t;
    }
}
