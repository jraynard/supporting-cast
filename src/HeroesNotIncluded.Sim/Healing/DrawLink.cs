using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;

namespace HeroesNotIncluded.Sim.Healing;

/// <summary>
/// The link an empath holds open while drawing a wound across (<see cref="HandsOnSession"/>): their
/// composure (the needle) against the wound's pain, harder the worse the wound, and each kind of
/// wound its own way (<see cref="DrawPattern"/>): a cut jolts, a bite tears twice, a bruise swells,
/// a burn pulls one way, frost numbs the hands so they answer late, venom grows as it comes across.
/// Out of the calm band too long and it snaps. Strain and composure carry from one wound to the
/// next under the same hands, so a hard draw leaves less for the one after. Pure: it reads nothing
/// but itself and the random stream it's given, so a client can run it ahead.
/// </summary>
public sealed class DrawLink
{
    /// <summary>Half the width of the calm band, before skill widens it.</summary>
    public const float BandHalfWidth = 0.14f;

    /// <summary>Steps of drawing a slight wound takes, and how many more a grave one.</summary>
    public const int BaseSteps = 180, StepsPerSeverity = 420;

    /// <summary>
    /// How far one step of steering moves composure, at full lean. Enough to meet a jolt, not so much
    /// that a held key simply pins the needle where it's wanted.
    /// </summary>
    public const float SteerPerStep = 0.0075f;

    /// <summary>Steps out of the band before the link snaps.</summary>
    public const int SnapSteps = 75;

    /// <summary>How much wider the band is with the patient lying in bed.</summary>
    public const float BedsideBand = 0.25f;

    /// <summary>How fast composure settles back to the middle while the link is let go (a breath between wounds).</summary>
    public const float Settle = 0.03f;

    /// <summary>The hands' tolerance (from skill): widens the band.</summary>
    public float Tolerance { get; set; } = 1f;

    /// <summary>The patient lies in bed (a calmer draw).</summary>
    public bool Bedside { get; set; }

    /// <summary>How much a steadying mending widens the band (<see cref="Empathy.SteadiedBand"/>).</summary>
    public float Steady { get; set; }

    /// <summary>How the wound now under the hands moves the needle (from its kind's <see cref="DrawFeelDef"/>).</summary>
    public DrawPattern Pattern { get; private set; }
    public float Rate { get; private set; } = 1f;
    public float Strength { get; private set; } = 1f;

    /// <summary>How grave the wound under the hands is, 0..1 of its part: sets the jolts.</summary>
    public float Gravity { get; private set; }

    /// <summary>Composure, 0..1; the band is centred on 0.5.</summary>
    public float Composure { get; set; } = 0.5f;

    /// <summary>How close the link is to snapping, 0..1.</summary>
    public float Strain { get; set; }

    public bool Snapped { get; private set; }

    private float _push;

    // Numb hands' steering, easing toward what's pressed; a burn's pull's way; a bite's second jolt.
    private float _steerFelt;
    private int _driftWay = 1, _echoIn, _echoWay;

    public float HalfWidth => BandHalfWidth * Tolerance * (1f + Steady) * (Bedside ? 1f + BedsideBand : 1f);
    public bool Calm => MathF.Abs(Composure - 0.5f) <= HalfWidth;

    /// <summary>Steps it takes to draw a wound this grave whole, calm all the way.</summary>
    public static int StepsFor(float gravity) => BaseSteps + (int)(StepsPerSeverity * gravity);

    /// <summary>Turn the link to a wound: its kind sets how it pulls; a burn picks its way afresh.</summary>
    public void Take(DrawPattern pattern, float rate, float strength, float gravity, SimRandom rand)
    {
        (Pattern, Rate, Strength, Gravity) = (pattern, rate, strength, gravity);
        _driftWay = rand.NextFloat() < 0.5f ? -1 : 1;
        _echoIn = 0;
    }

    /// <summary>
    /// One step. <paramref name="drawing"/>: the link is held open on a wound (its pain comes
    /// through; the keys steer). Otherwise composure settles back and the strain eases. Returns
    /// whether it's calm.
    /// </summary>
    public bool Step(SimRandom rand, bool drawing, float steer, float drawn, float tremor, int stepsDone)
    {
        if (!drawing)
        {
            _push *= 0.9f;
            _steerFelt = 0f;
            Composure += (0.5f - Composure) * Settle;
            Strain = Math.Max(0f, Strain - 1f / (SnapSteps * 2));
            return Calm;
        }
        // The wound's pain comes in jolts, the worse the wound the harder; they ease off between.
        // A jolt about once a second at worst; each would carry the needle 0.1 to 0.3 off if not
        // steered against (a push decays over half a second). Each kind of wound has its own way.
        var size = (0.003f + 0.006f * Gravity) * (1f + tremor * 2f) * Strength;
        var jolt = rand.Chance((0.008f + 0.016f * Gravity) * Rate * (Pattern is DrawPattern.Drift or DrawPattern.Wave ? 0.4f : 1f));
        var way = jolt && rand.NextFloat() < 0.5f ? -1 : 1;
        var force = 0f;
        switch (Pattern)
        {
            case DrawPattern.Double:
                if (jolt)
                {
                    _push += way * size * 0.75f;
                    (_echoIn, _echoWay) = (8 + rand.NextInt(6), way);
                }
                if (_echoIn > 0 && --_echoIn == 0)
                    _push += _echoWay * size * 0.75f;
                break;
            case DrawPattern.Wave:
                // A slow swell, a couple of seconds each way: ridden with the keys, not caught.
                if (jolt)
                    _push += way * size * 0.5f;
                force = (Triangle(stepsDone, 2.6f) * 2f - 1f) * size * 0.6f;
                break;
            case DrawPattern.Drift:
                if (jolt)
                    _push += way * size * 0.5f;
                force = _driftWay * size * 0.2f;
                break;
            case DrawPattern.Grow:
                if (jolt)
                    _push += way * size * (0.5f + 2f * drawn);
                break;
            default: // Spike and Numb
                if (jolt)
                    _push += way * size;
                break;
        }
        _push *= 0.97f;
        // Numb hands answer the keys late, and keep going after.
        _steerFelt = Pattern == DrawPattern.Numb ? _steerFelt + (steer - _steerFelt) * 0.04f : steer;
        Composure = Math.Clamp(Composure + _push + force + _steerFelt * SteerPerStep, 0f, 1f);
        var calm = Calm;
        Strain = calm ? Math.Max(0f, Strain - 1f / (SnapSteps * 2)) : Strain + 1f / SnapSteps;
        if (Strain >= 1f)
            Snapped = true;
        return calm;
    }

    /// <summary>A jolt of its own: the patient flinching under the hands.</summary>
    public void Jar(float strain, float push)
    {
        Strain = Math.Min(0.95f, Strain + strain);
        _push += push;
    }

    /// <summary>A triangle wave 0..1..0 over <paramref name="period"/> seconds, as <see cref="Work.WorkSession"/> has.</summary>
    public static float Triangle(int steps, float period)
    {
        var t = steps / (float)SimClock.TicksPerSecond / period;
        var f = t - MathF.Floor(t);
        return f < 0.5f ? f * 2f : 2f - f * 2f;
    }

    public DrawLink Copy() => (DrawLink)MemberwiseClone();

    public void Write(SaveWriter w)
    {
        w.Float(Tolerance);
        w.Bool(Bedside);
        w.Float(Steady);
        w.Int((int)Pattern);
        w.Float(Rate);
        w.Float(Strength);
        w.Float(Gravity);
        w.Float(Composure);
        w.Float(Strain);
        w.Bool(Snapped);
        w.Float(_push);
        w.Float(_steerFelt);
        w.Int(_driftWay);
        w.Int(_echoIn);
        w.Int(_echoWay);
    }

    public static DrawLink Read(SaveReader r)
    {
        var l = new DrawLink { Tolerance = r.Float(), Bedside = r.Bool(), Steady = r.Float() };
        l.Pattern = (DrawPattern)r.Int();
        if (!Enum.IsDefined(l.Pattern))
            throw new SaveException("Corrupt save: a draw's pattern is unknown");
        l.Rate = r.Float();
        l.Strength = r.Float();
        l.Gravity = r.Float();
        l.Composure = r.Float();
        l.Strain = r.Float();
        l.Snapped = r.Bool();
        l._push = r.Float();
        l._steerFelt = r.Float();
        l._driftWay = Math.Sign(r.Int());
        l._echoIn = r.Int();
        l._echoWay = Math.Sign(r.Int());
        return l;
    }
}
