using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Environment;
using Narsil.Rendering;

namespace HeroesNotIncluded;

/// <summary>
/// A bucket's water seen thrown (<see cref="Simulation.RecentSplashes"/>): it flies from the
/// thrower's hands in an arc, shedding drops, and lands in a splash and a spreading ripple; where
/// it hits fire, steam boils up. The sim has put the flames out the moment it's thrown, so while
/// the water's in the air the renderer keeps drawing them as they were: they go out when it lands.
/// Cosmetic only, on the animation clock (it freezes with the game).
/// </summary>
public sealed partial class WorldRenderer
{
    /// <summary>One throw as drawn: where its arc runs, when it was first seen, and whether it's landed yet.</summary>
    private sealed class SplashFx
    {
        public float3 From, To;
        public float Start, Flight, Peak;
        public bool Landed;
    }

    private readonly Dictionary<SplashEvent, SplashFx> _splashes = new(ReferenceEqualityComparer.Instance);

    /// <summary>How long the ripple and the settling steam go on after the water lands, seconds.</summary>
    private const float RippleSeconds = 0.7f, SettleSeconds = 2.2f;

    private void DrawSplashes(Simulation sim, Func<int3, bool> inView, float time)
    {
        var map = sim.Map;
        var now = _anim.Clock;
        if (_splashes.Count > 0)
            foreach (var k in _splashes.Keys.ToList())
                if (!sim.RecentSplashes.Contains(k))
                    _splashes.Remove(k);
        foreach (var ev in sim.RecentSplashes)
        {
            if (!_splashes.TryGetValue(ev, out var fx))
            {
                // First sight: fix the arc, from the hands of whoever threw it as they're drawn now.
                var to = Position.CenterOf(ev.At) with { Z = GroundBelow(map, ev.At) + 0.05f };
                var from = !ev.Thrower.IsNull && sim.World.IsAlive(ev.Thrower) && sim.World.TryGet<Position>(ev.Thrower, out var tp)
                    ? DrawPos(sim, ev.Thrower, tp.Pos) : ev.From;
                from += WorldScale.Rise(1f);
                var d = to - from;
                var dist = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
                fx = new SplashFx { From = from, To = to, Start = now, Flight = 0.22f + 0.035f * dist, Peak = 0.3f + 0.07f * dist };
                _splashes[ev] = fx;
                // A splash from long ago (a load, a join): just the wet ground, no replay.
                if (sim.Clock.Tick - ev.Tick > SimClock.TicksPerSecond)
                    fx.Landed = true;
            }
            var t = (now - fx.Start) / fx.Flight;
            if (!inView(ev.At))
                continue;

            if (t < 1f)
            {
                DrawWaterInFlight(fx, t);
                // Not out yet, as far as anyone can see: the flames burn on till the water arrives.
                foreach (var (i, fire) in ev.Doused)
                    if (map.Fire[i] < fire)
                        DrawFlames(i, map.CellOf(i), fire, time);
                continue;
            }
            if (!fx.Landed)
            {
                fx.Landed = true;
                _particles.SplashBurst(fx.To, 1f);
                foreach (var (i, fire) in ev.Doused)
                {
                    var c = map.CellOf(i);
                    _particles.DouseSteam(new float3(c.X + 0.5f, c.Y + 0.5f, c.Z), fire);
                }
            }
            var after = now - fx.Start - fx.Flight;
            if (after < RippleSeconds)
            {
                var r = after / RippleSeconds;
                var size = 0.7f + 2.1f * (1f - (1f - r) * (1f - r));
                Decal(S("fx/splash_ring"), fx.To with { Z = fx.To.Z - 0.03f }, size, Rgba.Pack(0.78f, 0.9f, 1f, 0.75f * (1f - r)),
                    Quad.Level(ev.At.Z) | Quad.Unlit, translucent: true);
            }
            if (after < SettleSeconds && ev.Doused.Count > 0)
            {
                var fade = 1f - after / SettleSeconds;
                foreach (var (i, _) in ev.Doused)
                    for (var n = _particles.PerFrame(2.5f * fade, _animDt); n > 0; n--)
                    {
                        var c = map.CellOf(i);
                        _particles.SteamWisp(new float3(c.X + 0.5f, c.Y + 0.5f, c.Z));
                    }
            }
        }
    }

    /// <summary>
    /// The water in the air, <paramref name="t"/> of the way: a gleaming slug of it riding the arc,
    /// drawn out behind into a tail, and drops shed as it goes.
    /// </summary>
    private void DrawWaterInFlight(SplashFx fx, float t)
    {
        var drop = S("fx/droplet");
        var level = Quad.Level(Math.Max(0, (int)MathF.Max(fx.From.Z, fx.To.Z))) | Quad.Unlit;
        for (var k = 0; k < 7; k++)
        {
            var tk = t - k * 0.045f;
            if (tk < 0f)
                break;
            var p = Arc(fx, tk);
            var size = (0.34f - k * 0.035f) * (0.75f + 0.25f * t);
            var spread = 0.05f + k * 0.02f;
            for (var j = 0; j < 2; j++)
            {
                var a = (k * 2.3f + j * 3.1f) + t * 9f;
                var off = new float3(MathF.Cos(a) * spread, MathF.Sin(a) * spread, 0);
                Billboard(drop, p + off - WorldScale.Rise(size * 0.5f), size, size, Rgba.Pack(0.72f, 0.86f, 1f, 0.85f - k * 0.08f), level, translucent: true);
            }
        }
        var head = Arc(fx, t);
        var ahead = Arc(fx, Math.Min(1f, t + 0.05f));
        var vel = (ahead - head) / (0.05f * fx.Flight);
        vel = new float3(vel.X, vel.Y, vel.Z * WorldScale.LevelHeight);
        _particles.WaterTrail(head, vel, fx.To.Z, _particles.PerFrame(70f, _animDt));
    }

    /// <summary>Where the water is, <paramref name="t"/> (0..1) along its throw: a straight line bowed up into a parabola.</summary>
    private static float3 Arc(SplashFx fx, float t) =>
        fx.From + (fx.To - fx.From) * t + new float3(0, 0, fx.Peak * 4f * t * (1f - t));
}
