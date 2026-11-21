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

        /// <summary>
        /// Colour over its life (a flame cooling to smoke): <see cref="Color"/> at birth, through
        /// <see cref="MidColor"/> at <see cref="MidAt"/> (when that's above zero), to
        /// <see cref="EndColor"/> at its end.
        /// </summary>
        public bool Ramp;
        public float4 MidColor;
        public float MidAt;
        public float4 EndColor;

        /// <summary>Dies on falling below <see cref="Floor"/> (world z): droplets stop at the ground.</summary>
        public bool HasFloor;
        public float Floor;

        /// <summary>
        /// Part of a roof standing on this level (-1: not): drawn while the roofs are, never cut away
        /// above it (as a chimney's smoke is). What rises off a burning building's roof.
        /// </summary>
        public int RoofOf;
    }

    private const int Max = 4096;
    private readonly List<Particle> _live = [];
    private readonly Random _rand = new(7);

    public int Count => _live.Count;

    public float Range(float a, float b) => a + (float)_rand.NextDouble() * (b - a);

    /// <summary>How many to emit this frame for <paramref name="perSecond"/> over <paramref name="dt"/> seconds (the fraction by chance), so a stream's thickness doesn't hang on the frame rate.</summary>
    public int PerFrame(float perSecond, float dt)
    {
        var n = perSecond * Math.Max(0f, dt);
        var whole = (int)n;
        return whole + (Range(0f, 1f) < n - whole ? 1 : 0);
    }

    /// <summary>While not -1, what's emitted belongs to a roof on this level (<see cref="Particle.RoofOf"/>). Set it round the calls, then back.</summary>
    public int OverRoof { get; set; } = -1;

    public void Emit(in Particle p)
    {
        if (_live.Count < Max)
            _live.Add(p with { RoofOf = OverRoof });
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

    /// <summary>Pale water-blue, for droplets and spray.</summary>
    public static readonly float4 Water = new(0.72f, 0.86f, 1f, 0.9f);

    /// <summary>
    /// Drops shed from a thrown bucket's water as it flies, at <paramref name="at"/>, going
    /// <paramref name="vel"/> (cells a second, z in sprite cells as every particle's is): they
    /// scatter a little, fall, and stop at <paramref name="floor"/>.
    /// </summary>
    public void WaterTrail(float3 at, float3 vel, float floor, int count)
    {
        for (var i = 0; i < count; i++)
            Emit(new Particle
            {
                Sprite = "fx/droplet",
                Pos = at + new float3(Range(-0.08f, 0.08f), Range(-0.08f, 0.08f), Range(-0.04f, 0.04f)),
                Vel = vel * Range(0.3f, 0.7f) + new float3(Range(-0.5f, 0.5f), Range(-0.5f, 0.5f), Range(-0.3f, 0.6f)),
                Gravity = 9f,
                Drag = 0.4f,
                Life = Range(0.4f, 0.8f),
                Size = Range(0.06f, 0.12f),
                Color = Water,
                Unlit = true,
                HasFloor = true,
                Floor = floor,
            });
    }

    /// <summary>A drop running off someone soaked, from round <paramref name="at"/>, falling to <paramref name="floor"/>.</summary>
    public void Drip(float3 at, float floor, float spread)
    {
        Emit(new Particle
        {
            Sprite = "fx/droplet",
            Pos = at + new float3(Range(-spread, spread), Range(-spread, spread), Range(-0.25f, 0.25f)),
            Vel = new float3(Range(-0.1f, 0.1f), Range(-0.1f, 0.1f), Range(-0.4f, 0f)),
            Gravity = 7f,
            Life = Range(0.5f, 0.9f),
            Size = Range(0.04f, 0.08f),
            Color = Water,
            Unlit = true,
            HasFloor = true,
            Floor = floor,
        });
    }

    /// <summary>
    /// Water landing at <paramref name="at"/>: a crown of drops thrown up and out, falling back,
    /// and a low mist rolling off. <paramref name="size"/> 1 for a bucketful.
    /// </summary>
    public void SplashBurst(float3 at, float size)
    {
        var count = (int)(26 * size);
        for (var i = 0; i < count; i++)
        {
            var a = Range(0, MathF.Tau);
            var dir = new float3(MathF.Cos(a), MathF.Sin(a), 0);
            Emit(new Particle
            {
                Sprite = "fx/droplet",
                Pos = at + dir * Range(0.05f, 0.35f) + new float3(0, 0, 0.03f),
                Vel = dir * Range(0.8f, 2.4f) * size + new float3(0, 0, Range(2.2f, 4.5f)),
                Gravity = 11f,
                Drag = 0.5f,
                Life = Range(0.5f, 0.9f),
                Size = Range(0.07f, 0.15f),
                Color = Water,
                Unlit = true,
                HasFloor = true,
                Floor = at.Z,
            });
        }
        for (var i = 0; i < 6 * size; i++)
        {
            var a = Range(0, MathF.Tau);
            var dir = new float3(MathF.Cos(a), MathF.Sin(a), 0);
            Emit(new Particle
            {
                Sprite = "fx/smoke",
                Pos = at + dir * Range(0.1f, 0.4f) + new float3(0, 0, 0.05f),
                Vel = dir * Range(0.6f, 1.2f) + new float3(0, 0, Range(0.1f, 0.4f)),
                Drag = 2.5f,
                Life = Range(0.6f, 1.0f),
                Size = Range(0.35f, 0.55f),
                Grow = 0.9f,
                Color = new float4(0.85f, 0.92f, 1f, 0.35f),
                Unlit = true,
            });
        }
    }

    /// <summary>
    /// Fire put out by water at <paramref name="at"/>, burning <paramref name="fire"/> (0..1) hard:
    /// a thick white plume of steam boiling up, and a puff of dark smoke as the flames die.
    /// </summary>
    public void DouseSteam(float3 at, float fire)
    {
        var count = 4 + (int)(fire * 6);
        for (var i = 0; i < count; i++)
            Emit(new Particle
            {
                Sprite = "fx/smoke",
                Pos = at + new float3(Range(-0.3f, 0.3f), Range(-0.3f, 0.3f), Range(0f, 0.15f)),
                Vel = new float3(Range(-0.25f, 0.25f), Range(-0.25f, 0.25f), Range(1.6f, 3.2f)),
                Drag = 1.1f,
                Life = Range(1.1f, 2f),
                Size = Range(0.4f, 0.7f) * (0.7f + 0.5f * fire),
                Grow = 0.9f,
                Color = new float4(0.96f, 0.97f, 1f, 0.55f + 0.25f * fire),
                Unlit = true,
                FadeIn = true,
            });
        for (var i = 0; i < 2; i++)
            Emit(new Particle
            {
                Sprite = "fx/smoke",
                Pos = at + new float3(Range(-0.2f, 0.2f), Range(-0.2f, 0.2f), 0.1f),
                Vel = new float3(Range(-0.15f, 0.15f), Range(-0.15f, 0.15f), Range(1f, 1.8f)),
                Drag = 1.2f,
                Life = Range(1f, 1.5f),
                Size = Range(0.35f, 0.5f),
                Grow = 0.6f,
                Color = new float4(0.25f, 0.24f, 0.24f, 0.5f),
            });
    }

    /// <summary>
    /// A tongue of flame licking up off something burning at <paramref name="at"/> (<paramref name="heat"/>
    /// 0..1): born pale yellow, reddening and shrinking as it rises, gone in half a second.
    /// </summary>
    public void FlameLick(float3 at, float heat)
    {
        Emit(new Particle
        {
            Sprite = "fx/fire",
            Pos = at + new float3(Range(-0.35f, 0.35f), Range(-0.35f, 0.35f), Range(0f, 0.15f)),
            Vel = new float3(Range(-0.2f, 0.2f), Range(-0.2f, 0.2f), Range(1.2f, 2.2f) * (0.7f + 0.5f * heat)),
            Drag = 0.8f,
            Life = Range(0.3f, 0.55f),
            Size = Range(0.25f, 0.42f) * (0.6f + 0.6f * heat),
            Grow = -0.9f,
            Color = new float4(1f, 0.92f, 0.68f, 0.9f),
            MidColor = new float4(1f, 0.55f, 0.2f, 0.75f),
            MidAt = 0.4f,
            EndColor = new float4(0.55f, 0.1f, 0.04f, 0.1f),
            Ramp = true,
            Unlit = true,
            Glow = true,
        });
    }

    /// <summary>
    /// Embers lifted off a fire on its heat: slower and longer-lived than a spark, drifting up and
    /// away from <paramref name="at"/>, winking out as they cool.
    /// </summary>
    public void DriftingEmbers(float3 at, int count, float spread = 0.3f)
    {
        for (var i = 0; i < count; i++)
            Emit(new Particle
            {
                Sprite = "fx/ember",
                Pos = at + new float3(Range(-spread, spread), Range(-spread, spread), Range(0f, 0.2f)),
                Vel = new float3(Range(-0.45f, 0.45f), Range(-0.45f, 0.45f), Range(1.0f, 2.4f)),
                Gravity = 0.25f,
                Drag = 0.5f,
                Life = Range(1.1f, 2.2f),
                Size = Range(0.05f, 0.1f),
                Grow = -0.3f,
                Color = new float4(1f, 0.82f, 0.45f, 1f),
                EndColor = new float4(0.9f, 0.25f, 0.08f, 0f),
                Ramp = true,
                Unlit = true,
                Glow = true,
            });
    }

    /// <summary>
    /// Smoke pouring off a burning roof at <paramref name="at"/> (<paramref name="heat"/> 0..1): great dark
    /// billows that climb into a column, greying and spreading as they go, leaning a little downwind.
    /// </summary>
    public void RoofSmoke(float3 at, float heat)
    {
        Emit(new Particle
        {
            Sprite = "fx/smoke",
            Pos = at + new float3(Range(-0.4f, 0.4f), Range(-0.4f, 0.4f), Range(0f, 0.3f)),
            Vel = new float3(Range(0.1f, 0.5f), Range(-0.4f, 0f), Range(1.4f, 2.4f)),
            Drag = 0.35f,
            Life = Range(2.6f, 4f),
            Size = Range(0.9f, 1.4f) * (0.8f + 0.4f * heat),
            Grow = 0.7f,
            Color = new float4(0.13f, 0.11f, 0.1f, 0.7f),
            EndColor = new float4(0.42f, 0.41f, 0.42f, 0.25f),
            Ramp = true,
            FadeIn = true,
        });
    }

    /// <summary>
    /// Woodsmoke off a hearth's chimney or an open fire at <paramref name="at"/>: soft grey puffs
    /// rising, spreading and thinning as they lean downwind (the same way as a burning roof's).
    /// <paramref name="strength"/> 0..1 thickens it.
    /// </summary>
    public void WoodSmoke(float3 at, float strength, float size = 1f, float grey = 0.66f)
    {
        Emit(new Particle
        {
            Sprite = "fx/smoke",
            Pos = at + new float3(Range(-0.08f, 0.08f), Range(-0.08f, 0.08f), 0f),
            Vel = new float3(Range(0.15f, 0.45f), Range(-0.3f, 0f), Range(0.7f, 1.2f)),
            Drag = 0.4f,
            Life = Range(2.4f, 3.6f),
            Size = Range(0.36f, 0.52f) * size,
            Grow = 0.9f,
            Color = new float4(grey, grey, grey + 0.02f, 0.28f + 0.25f * strength),
            EndColor = new float4(grey + 0.14f, grey + 0.14f, grey + 0.16f, 0f),
            Ramp = true,
            FadeIn = true,
        });
    }

    /// <summary>
    /// A dragon soaked: a great white plume of steam bursting off its body at <paramref name="at"/>
    /// (<paramref name="size"/>: how big it is), rolling up and out.
    /// </summary>
    public void SteamPlume(float3 at, float size)
    {
        var count = 14 + (int)(size * 8);
        for (var i = 0; i < count; i++)
        {
            var a = Range(0, MathF.Tau);
            Emit(new Particle
            {
                Sprite = "fx/smoke",
                Pos = at + new float3(MathF.Cos(a), MathF.Sin(a), 0) * Range(0f, 0.6f * size) + new float3(0, 0, Range(-0.2f, 0.4f) * size),
                Vel = new float3(MathF.Cos(a) * Range(0.6f, 1.8f), MathF.Sin(a) * Range(0.6f, 1.8f), Range(1.2f, 3.2f)) * (0.7f + 0.3f * size),
                Drag = 1.3f,
                Life = Range(1.2f, 2.2f),
                Size = Range(0.5f, 0.9f) * size,
                Grow = 1.1f,
                Color = new float4(0.97f, 0.98f, 1f, 0.75f),
                EndColor = new float4(0.85f, 0.88f, 0.92f, 0f),
                Ramp = true,
                Unlit = true,
                FadeIn = true,
            });
        }
    }

    /// <summary>A last wisp of steam off a doused cell, as it settles.</summary>
    public void SteamWisp(float3 at)
    {
        Emit(new Particle
        {
            Sprite = "fx/smoke",
            Pos = at + new float3(Range(-0.35f, 0.35f), Range(-0.35f, 0.35f), 0.05f),
            Vel = new float3(Range(-0.1f, 0.1f), Range(-0.1f, 0.1f), Range(0.8f, 1.4f)),
            Drag = 0.8f,
            Life = Range(1f, 1.6f),
            Size = Range(0.25f, 0.4f),
            Grow = 0.5f,
            Color = new float4(0.94f, 0.96f, 1f, 0.35f),
            Unlit = true,
            FadeIn = true,
        });
    }

    /// <summary>
    /// A tongue of dragon fire from <paramref name="from"/> along <paramref name="to"/>: born white-hot,
    /// cooling through <paramref name="heart"/> to a dark red as it billows out, gone at the far end.
    /// </summary>
    public void FlameJet(float3 from, float3 to, float4 heart, float jitter)
    {
        var d = to - from;
        var life = Range(0.28f, 0.42f);
        var side = new float3(Range(-jitter, jitter), Range(-jitter, jitter), Range(-jitter, jitter) * 0.5f);
        var v = (d + side) / life;
        // Mostly soft billows (a gush of burning gas), now and then a licking flame among them.
        var tongue = Range(0f, 1f) < 0.25f;
        Emit(new Particle
        {
            Sprite = tongue ? "fx/fire" : "fx/smoke",
            Pos = from + d * Range(0f, 0.1f),
            Vel = new float3(v.X, v.Y, v.Z * WorldScale.LevelHeight),
            Life = life,
            Size = tongue ? Range(0.2f, 0.3f) : Range(0.22f, 0.34f),
            Grow = tongue ? Range(1.2f, 1.8f) : Range(1.6f, 2.4f),
            Color = new float4(1f, 0.88f, 0.6f, tongue ? 0.8f : 0.55f),
            MidColor = new float4(heart.X, heart.Y, heart.Z, tongue ? 0.7f : 0.5f),
            MidAt = 0.3f,
            EndColor = new float4(0.35f, 0.06f, 0.03f, 0.25f),
            Ramp = true,
            Unlit = true,
            Glow = true,
        });
    }

    /// <summary>Black smoke rolling up off a breath's path at <paramref name="at"/>, after the fire.</summary>
    public void BreathSmoke(float3 at, float size = 1f)
    {
        Emit(new Particle
        {
            Sprite = "fx/smoke",
            Pos = at + new float3(Range(-0.25f, 0.25f), Range(-0.25f, 0.25f), Range(0f, 0.2f)),
            Vel = new float3(Range(-0.3f, 0.3f), Range(-0.3f, 0.3f), Range(0.8f, 1.6f)),
            Drag = 0.9f,
            Life = Range(1.4f, 2.4f),
            Size = Range(0.45f, 0.75f) * size,
            Grow = 0.8f,
            Color = new float4(0.16f, 0.14f, 0.13f, 0.55f),
            EndColor = new float4(0.35f, 0.34f, 0.34f, 0.3f),
            Ramp = true,
            FadeIn = true,
        });
    }

    /// <summary>Flame bursting out where a breath strikes, in a ring along the ground.</summary>
    public void FlameBurst(float3 at, float4 heart, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var a = Range(0, MathF.Tau);
            var dir = new float3(MathF.Cos(a), MathF.Sin(a), 0);
            Emit(new Particle
            {
                Sprite = "fx/fire",
                Pos = at + new float3(0, 0, 0.05f),
                Vel = dir * Range(1.5f, 3f) + new float3(0, 0, Range(0.5f, 1.8f)),
                Drag = 3.5f,
                Life = Range(0.35f, 0.6f),
                Size = Range(0.3f, 0.5f),
                Grow = 1.2f,
                Color = new float4(1f, 0.9f, 0.65f, 1f),
                EndColor = new float4(heart.X * 0.6f, heart.Y * 0.4f, heart.Z * 0.4f, 0.4f),
                Ramp = true,
                Unlit = true,
                Glow = true,
            });
        }
    }

    /// <summary>A dragon drawing breath: hot specks pulled in from round <paramref name="mouth"/> into it.</summary>
    public void Inhale(float3 mouth, float4 color)
    {
        var a = Range(0, MathF.Tau);
        var e = Range(-0.6f, 0.9f);
        var r = Range(0.7f, 1.2f);
        var start = mouth + new float3(MathF.Cos(a) * MathF.Cos(e) * r, MathF.Sin(a) * MathF.Cos(e) * r, MathF.Sin(e) * r / WorldScale.LevelHeight);
        var life = Range(0.35f, 0.55f);
        var v = (mouth - start) / life;
        Emit(new Particle
        {
            Sprite = "fx/ember",
            Pos = start,
            Vel = new float3(v.X, v.Y, v.Z * WorldScale.LevelHeight),
            Life = life,
            Size = Range(0.06f, 0.11f),
            Grow = -0.1f,
            Color = color,
            Unlit = true,
            Glow = true,
            FadeIn = true,
        });
    }

    /// <summary>
    /// Air shimmering over great heat: faint, warm-pale puffs wobbling up from <paramref name="at"/>.
    /// Not true refraction (the post pass has no distortion), but it reads as the air going thick.
    /// </summary>
    public void HeatHaze(float3 at, float size = 1f)
    {
        Emit(new Particle
        {
            Sprite = "fx/smoke",
            Pos = at + new float3(Range(-0.2f, 0.2f), Range(-0.2f, 0.2f), 0),
            Vel = new float3(Range(-0.35f, 0.35f), Range(-0.35f, 0.35f), Range(1f, 1.8f)),
            Drag = 1.5f,
            Life = Range(0.5f, 0.8f),
            Size = Range(0.35f, 0.55f) * size,
            Grow = 0.6f,
            Color = new float4(1f, 0.85f, 0.65f, 0.12f),
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
            if ((p.HasFloor && p.Pos.Z < p.Floor) || p.Size <= 0f)
            {
                _live[i] = _live[^1];
                _live.RemoveAt(_live.Count - 1);
                continue;
            }
            _live[i] = p;
        }
    }

    private static float4 Ramped(in Particle p, float t)
    {
        if (p.MidAt <= 0f)
            return Lerp(p.Color, p.EndColor, t);
        return t < p.MidAt ? Lerp(p.Color, p.MidColor, t / p.MidAt) : Lerp(p.MidColor, p.EndColor, (t - p.MidAt) / (1f - p.MidAt));
    }

    private static float4 Lerp(float4 a, float4 b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

    public void Draw(WorldRenderer r, int cut)
    {
        foreach (var p in _live)
        {
            if (p.RoofOf < 0 ? p.Pos.Z > cut + 1 : p.RoofOf > cut)
                continue;
            var t = p.Age / p.Life;
            var fade = 1f - t * t;
            if (p.FadeIn)
                fade *= Math.Min(1f, t * 6f);
            var c = p.Ramp ? Ramped(p, t) : p.Color;
            var flags = (p.RoofOf < 0 ? Quad.Level(Math.Max(0, (int)p.Pos.Z)) : Quad.Level(p.RoofOf) | Quad.Roof)
                | (p.Unlit ? Quad.Unlit : 0u) | (p.Glow ? Quad.Glow : 0u);
            r.Billboard(r.Sprite(p.Sprite), p.Pos - WorldScale.Rise(p.Size * 0.5f), p.Size, p.Size, Rgba.Pack(c.X, c.Y, c.Z, c.W * fade), flags, translucent: true);
        }
    }
}
