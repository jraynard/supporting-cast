using Narsil.Rendering;

namespace HeroesNotIncluded.Rendering;

/// <summary>
/// Small render-only particle pool for dust, embers, chips and Zzz. Lives entirely outside the sim,
/// advances on the animation clock (so it freezes with the game) and draws as translucent billboards.
/// Randomness here is cosmetic, so it uses its own generator, never <c>sim.Rand</c>.
/// </summary>
public sealed class Particles
{
    public struct Particle
    {
        public string Sprite;
        public float3 Pos;
        public float3 Vel;
        public float Gravity;
        public float Drag;
        public float Age;
        public float Life;
        public float Size;
        public float Grow;
        public float4 Color;
        public bool Unlit;

        /// <summary>Emissive (embers): feeds the bloom.</summary>
        public bool Glow;
        public bool FadeIn;
    }

    private const int Max = 2048;
    private readonly List<Particle> _live = [];
    private readonly Random _rand = new(7);

    public int Count => _live.Count;

    public float Range(float a, float b) => a + (float)_rand.NextDouble() * (b - a);

    public void Emit(in Particle p)
    {
        if (_live.Count < Max)
            _live.Add(p);
    }

    /// <summary>Soft puffs kicked up around <paramref name="at"/> (feet landing, hammer blows).</summary>
    public void Dust(float3 at, int count, float spread, float4 color, float size = 0.45f)
    {
        for (var i = 0; i < count; i++)
        {
            var a = Range(0, MathF.Tau);
            var dir = new float3(MathF.Cos(a), MathF.Sin(a), 0);
            Emit(new Particle
            {
                Sprite = "fx/smoke",
                Pos = at + dir * Range(0, spread * 0.3f) + new float3(0, 0, 0.05f),
                Vel = dir * Range(0.4f, 1.1f) * spread + new float3(0, 0, Range(0.2f, 0.6f)),
                Drag = 3f,
                Life = Range(0.5f, 0.9f),
                Size = size * Range(0.7f, 1.2f),
                Grow = 0.8f,
                Color = color,
            });
        }
    }

    public void Embers(float3 at, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var a = Range(0, MathF.Tau);
            Emit(new Particle
            {
                Sprite = "fx/ember",
                Pos = at,
                Vel = new float3(MathF.Cos(a) * Range(0.3f, 1.2f), MathF.Sin(a) * Range(0.3f, 1.2f), Range(0.8f, 2.2f)),
                Gravity = 3.5f,
                Drag = 0.6f,
                Life = Range(0.5f, 1.1f),
                Size = Range(0.08f, 0.16f),
                Color = new float4(1, 1, 1, 1),
                Unlit = true,
                Glow = true,
            });
        }
    }

    /// <summary>
    /// Sparks off steel on a turning grindstone: bright specks thrown from <paramref name="at"/>
    /// mostly along <paramref name="along"/> (the way the wheel's rim runs) and down, falling fast.
    /// </summary>
    public void Sparks(float3 at, float3 along, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var side = new float3(-along.Y, along.X, 0) * Range(-0.35f, 0.35f);
            Emit(new Particle
            {
                Sprite = "fx/ember",
                Pos = at,
                Vel = along * Range(0.9f, 1.8f) + side + new float3(0, 0, Range(-0.5f, 0.4f)),
                Gravity = 4f,
                Drag = 1.2f,
                Life = Range(0.18f, 0.4f),
                Size = Range(0.04f, 0.08f),
                Color = new float4(1f, 0.85f, 0.5f, 1f),
                Unlit = true,
                Glow = true,
            });
        }
    }

    /// <summary>Steam off a quench: pale puffs rising and spreading from the water, more and thicker the hotter the steel (<paramref name="strength"/> 0..1).</summary>
    public void Steam(float3 at, float strength)
    {
        var count = 1 + (int)(strength * 2.5f);
        for (var i = 0; i < count; i++)
        {
            Emit(new Particle
            {
                Sprite = "fx/smoke",
                Pos = at + new float3(Range(-0.15f, 0.15f), Range(-0.15f, 0.15f), 0.02f),
                Vel = new float3(Range(-0.1f, 0.1f), Range(-0.1f, 0.1f), Range(0.9f, 1.6f)),
                Drag = 1.4f,
                Life = Range(0.7f, 1.3f),
                Size = Range(0.18f, 0.3f),
                Grow = 0.5f,
                Color = new float4(0.94f, 0.95f, 0.97f, 0.35f + 0.35f * strength),
                Unlit = true,
            });
        }
    }

    /// <summary>
    /// Motes of light drawn from <paramref name="from"/> into <paramref name="to"/> (a wound coming
    /// across into an empath's hands): glowing specks that drift along the way and fade as they arrive.
    /// </summary>
    public void Wisps(float3 from, float3 to, int count, float4 color)
    {
        for (var i = 0; i < count; i++)
        {
            var start = from + new float3(Range(-0.12f, 0.12f), Range(-0.12f, 0.12f), Range(-0.05f, 0.1f));
            var life = Range(0.45f, 0.7f);
            Emit(new Particle
            {
                Sprite = "fx/ember",
                Pos = start,
                Vel = (to - start) / life + new float3(0, 0, Range(-0.1f, 0.2f)),
                Life = life,
                Size = Range(0.05f, 0.09f),
                Grow = -0.6f,
                Color = color,
                Unlit = true,
                Glow = true,
                FadeIn = true,
            });
        }
    }

    /// <summary>Slow pale motes rising round <paramref name="at"/> (an empath meditating): a stillness you can see.</summary>
    public void Stillness(float3 at, float4 color)
    {
        var a = Range(0, MathF.Tau);
        var r = Range(0.25f, 0.45f);
        Emit(new Particle
        {
            Sprite = "fx/ember",
            Pos = at + new float3(MathF.Cos(a) * r, MathF.Sin(a) * r, Range(0f, 0.3f)),
            Vel = new float3(0, 0, Range(0.15f, 0.3f)),
            Drag = 0.4f,
            Life = Range(1.4f, 2.2f),
            Size = Range(0.04f, 0.07f),
            Color = color,
            Unlit = true,
            Glow = true,
            FadeIn = true,
        });
    }

    /// <summary>A rune's glyph flaring over an empath's hands as it's woven, rising and fading.</summary>
    public void Rune(float3 at, string sprite, float4 color)
    {
        Emit(new Particle
        {
            Sprite = sprite,
            Pos = at,
            Vel = new float3(0, 0, 0.35f),
            Drag = 1.2f,
            Life = 0.9f,
            Size = 0.32f,
            Grow = 0.3f,
            Color = color,
            Unlit = true,
            Glow = true,
        });
    }

    /// <summary>A speech bubble rising from someone's head (an interaction), tinted by how it went.</summary>
    public void Speech(float3 at, float4 color)
    {
        Emit(new Particle
        {
            Sprite = "fx/speech",
            Pos = at,
            Vel = new float3(0, 0, 0.12f),
            Life = 2.4f,
            Size = 0.32f,
            Grow = 0.04f,
            Color = color,
            Unlit = true,
            FadeIn = true,
        });
    }

    public void Zzz(float3 at)
    {
        Emit(new Particle
        {
            Sprite = "fx/zzz",
            Pos = at,
            Vel = new float3(Range(0.05f, 0.15f), Range(-0.05f, 0.05f), 0.35f),
            Life = 2.2f,
            Size = 0.18f,
            Grow = 0.12f,
            Color = new float4(0.9f, 0.95f, 1f, 0.9f),
            Unlit = true,
            FadeIn = true,
        });
    }

    public void Update(float dt)
    {
        if (dt <= 0)
            return;
        for (var i = _live.Count - 1; i >= 0; i--)
        {
            var p = _live[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                _live[i] = _live[^1];
                _live.RemoveAt(_live.Count - 1);
                continue;
            }
            p.Vel.Z -= p.Gravity * dt;
            p.Vel *= MathF.Max(0f, 1f - p.Drag * dt);
            // Heights are in sprite cells, not world levels (taller-looking levels, WorldScale).
            p.Pos += new float3(p.Vel.X, p.Vel.Y, p.Vel.Z / WorldScale.LevelHeight) * dt;
            p.Size += p.Grow * dt;
            _live[i] = p;
        }
    }

    public void Draw(WorldRenderer r, int cut)
    {
        foreach (var p in _live)
        {
            if (p.Pos.Z > cut + 1)
                continue;
            var t = p.Age / p.Life;
            var fade = 1f - t * t;
            if (p.FadeIn)
                fade *= Math.Min(1f, t * 6f);
            var c = p.Color;
            var flags = Quad.Level(Math.Max(0, (int)p.Pos.Z)) | (p.Unlit ? Quad.Unlit : 0u) | (p.Glow ? Quad.Glow : 0u);
            r.Billboard(r.Sprite(p.Sprite), p.Pos - WorldScale.Rise(p.Size * 0.5f), p.Size, p.Size, Rgba.Pack(c.X, c.Y, c.Z, c.W * fade), flags, translucent: true);
        }
    }
}
