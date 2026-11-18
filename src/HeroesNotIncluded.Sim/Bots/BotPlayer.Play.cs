using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Expeditions;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Bots;

public sealed partial class BotPlayer
{
    // Mini-game state, per session.
    private long _session = -1;
    private bool _resting, _stopped;
    private int _lastRune = -1;

    // Lockwork: the feel needle smoothed, where it rose past the mark, the middle it found, and
    // how long it's been working a trigger.
    private float _feel;
    private float? _rose, _middle;
    private float _skipTo;
    private int _pin, _slips, _working, _dir = 1;
    private float _progress;

    // The tinker's games: the spoke last worked on the wheel, the tells read.
    private int _spoke = -1, _read;

    // The kitchen: the stirring rhythm, the seasoning's pinch-and-taste, and whether this pot's been seasoned yet.
    private int _stir, _seasonBeat;
    private bool _potSeasoned;

    /// <summary>The pot as it last looked: it remembers, walking off to the shelf or the larder.</summary>
    private KnownPot? _pot;

    // The tub and the tap: one press per stroke or swing; and the brush's up-and-down at the tub.
    private bool _struck;
    private int _axis2;

    // Weaving: the runes it means to key (indices into the defs' runes), and the one to key this frame.
    private List<int> _weave = [];
    private int _rune = -1;

    /// <summary>The session's other action (Space) this frame: set by <see cref="Play"/>.</summary>
    private bool _act;

    /// <summary>Done with the pot's seasoning: step away, which keeps it.</summary>
    private bool _seasoned;

    // ---------------- Playing the mini-games ----------------

    /// <summary>What a careful player would press now in the session in hand.</summary>
    private (bool Press, bool Held, int Axis) Play(WorkSession? s)
    {
        if (s == null)
            return (false, false, 0);
        if (s.StartTick != _session)
        {
            _session = s.StartTick;
            _resting = _stopped = _seasoned = false;
            _lastRune = -1;
            _feel = 0f;
            _rose = _middle = null;
            _skipTo = 0f;
            _pin = _slips = _working = 0;
            _dir = 1;
            _progress = 0f;
            _stir = _seasonBeat = 0;
            _struck = false;
            _spoke = -1;
            _read = 0;
            RollSlip();
        }
        switch (s)
        {
            // The festival's striker: swing at the top of the gauge.
            case StrikerSession striker:
                return (striker.Flight == 0 && striker.Needle >= 0.93f, false, 0);
            case HammerSession h:
                // Raise it to the mark (W held), let go, and keep it over the piece.
                _axis2 = HammerSwing(h);
                return (false, false, HammerCentre(h));
            case BloomSession bloom:
                // Keep it orange: pump below the middle of the band, ease off above. Out once it's grown.
                _act = bloom.Grown;
                return (false, bloom.Temp < (bloom.Lo + bloom.SlagAt) * 0.5f, 0);
            case HeatSession heat:
                // Pump, and draw it out once it glows the colour at the top of its working heat.
                // A slip: impatient, it's drawn out before it's there.
                _act = heat.LooksReady || (_slip && heat.StepsDone > SimClock.TicksPerSecond * 3);
                if (_act && !heat.LooksReady)
                    Slipped();
                return (false, true, 0);
            case QuenchSession q:
            {
                // Keep it under till the steam thins to a hiss. A slip: a hasty hand lifts it while
                // it's still steaming hard; a careless one forgets it till it's cold.
                var lift = _slip ? (Temper.Haste >= 0.5f ? q.Steam < 0.6f : false) : q.Steam < 0.2f;
                if (_slip && q.Dunked)
                    Slipped();
                return (false, !(q.Dunked && lift), 0);
            }
            case SharpenSession sh:
            {
                _resting = _resting ? sh.Heat > 0.3f : sh.Heat > 0.8f;
                return (false, !_resting && sh.InBand, Steer(sh.Angle, sh.HalfWidth));
            }
            case StewSession st:
            {
                // Flames over it: the lid on, before anything. A cold fire under it never cooks: step away and see to it.
                _act = st.Flaring > 0;
                if (!st.Lit)
                    StopKitchenWork();
                // Keep it at a simmer, and stir every couple of seconds.
                if (st.SinceStir > 120)
                    _stir = 20;
                return (false, _stir-- > 0, st.Flame < 0.6f ? 1 : st.Flame > 0.64f ? -1 : 0);
            }
            case SeasonSession se:
            {
                // A pinch, a taste, and stop when it tastes right.
                _potSeasoned = true;
                _seasonBeat = (_seasonBeat + 1) % 30;
                if (se.TastedAt == se.Pinches && se.Taste is "about right" or "a touch salty" or "far too salty")
                {
                    // A slip: one more pinch for luck, after it tasted right.
                    if (_slip && se.Taste == "about right" && !_slipCounted)
                    {
                        Slipped();
                        return (true, false, 0);
                    }
                    _seasoned = true;
                    return (false, false, 0);
                }
                if (_seasonBeat == 0 && (se.TastedAt == se.Pinches || se.Pinches == 0))
                    return (true, false, 0);
                _act = _seasonBeat == 15 && se.TastedAt != se.Pinches;
                return (false, false, 0);
            }
            case WashSession wa:
            {
                // To the grimiest spot, and scrub it once the brush is over it.
                var (gx, gy) = Grimiest(wa);
                float tx = (gx + 0.5f) / WashSession.GridW, ty = (gy + 0.5f) / WashSession.GridH;
                _axis2 = MathF.Abs(ty - wa.BrushY) > 0.04f ? Math.Sign(ty - wa.BrushY) : 0;
                var over = MathF.Abs(tx - wa.BrushX) < WashSession.BrushRadius * 0.6f && MathF.Abs(ty - wa.BrushY) < WashSession.BrushRadius * 0.8f;
                return (false, over && wa.Shine == 0, MathF.Abs(tx - wa.BrushX) > 0.03f ? Math.Sign(tx - wa.BrushX) : 0);
            }
            case Hospitality.ScrubSession sc:
            {
                // Onto the darkest blot's row, then back and forth across it till it's gone.
                var (bx, by) = Darkest(sc);
                float tx = (bx + 0.5f) / Hospitality.ScrubSession.GridW, ty = (by + 0.5f) / Hospitality.ScrubSession.GridH;
                _axis2 = MathF.Abs(ty - sc.MopY) > 0.05f ? Math.Sign(ty - sc.MopY) : 0;
                if (MathF.Abs(tx - sc.MopX) > Hospitality.ScrubSession.Reach)
                    return (false, false, Math.Sign(tx - sc.MopX));
                return (false, false, (sc.StepsDone / 6) % 2 == 0 ? -1 : 1);
            }
            case Hospitality.SignInSession si:
                return (!si.Begun, false, 0);
            case Hospitality.SlopSession sl:
            {
                // A slip: tipped before the sway's come round, and some goes on the floor.
                if (_slip && !_slipCounted && sl.Poured == 0 && !sl.OnCue && sl.Sweep > 0.4f)
                {
                    Slipped();
                    return (true, false, 0);
                }
                var press = sl.OnCue && !_struck;
                _struck = sl.OnCue;
                return (press, false, 0);
            }
            case Hospitality.MakeBedSession mb:
                switch (mb.At)
                {
                    case Hospitality.BedStep.Strip:
                        return (false, true, 0);
                    case Hospitality.BedStep.Spread:
                    {
                        var press = mb.OnCue && !_struck;
                        _struck = mb.OnCue;
                        return (press, false, 0);
                    }
                    default:
                        return (false, false, mb.StepsDone % 4 < 2 ? mb.NextSide : 0); // a tap at the corner that's next
                }
            case PackSession pack:
            {
                // Each corner on its cue, from its side; then draw the cord to the middle of snug and let go.
                if (!pack.Wrapped)
                {
                    var fold = pack.OnCue && !_struck;
                    _struck = pack.OnCue;
                    return (false, false, fold ? pack.NextSide : 0);
                }
                return (false, pack.Tension < (PackSession.SoundAt + PackSession.BitingAt) / 2f, 0);
            }
            case TapSession tap:
            {
                // A slip: one blow struck early, before the mallet's down, and the ale sprays.
                if (_slip && !_slipCounted && tap.Blows == 1 && !tap.OnTheTap && tap.Mallet > 0.5f)
                {
                    Slipped();
                    return (true, false, 0);
                }
                var press = tap.OnTheTap && !_struck;
                _struck = tap.OnTheTap;
                return (press, false, 0);
            }
            case MixSession mx:
            {
                // Each cue's key as it reaches the mark.
                if (mx.Resolved >= mx.Cues.Length || mx.Resolved == _lastRune || mx.StepsDone < MixSession.DueOf(mx.Resolved))
                    return (false, false, 0);
                _lastRune = mx.Resolved;
                var cue = mx.Cues[mx.Resolved];
                // A slip: one cue muddled, the other way.
                if (_slip && !_slipCounted && mx.Resolved == mx.Cues.Length / 2)
                {
                    Slipped();
                    cue = cue == 'A' ? 'D' : 'A';
                }
                return (cue == 'F', false, cue == 'A' ? -1 : cue == 'D' ? 1 : 0);
            }
            case PourSession p:
                // Stop where the settled head will bring it to the line.
                // A slip: looking away, it pours on past the line, and over.
                _stopped |= p.Level + p.Foam * 0.73f >= p.Line + (_slip ? 0.15f : 0f);
                if (_slip && _stopped)
                    Slipped();
                // Tipped while it's low (less foam), brought upright as it nears the line: tipped, it'd run over the low lip.
                return (false, !_stopped, p.Top < p.Line - PourUprightBelow ? 1 : -1);
            case HandsOnSession h:
                return HandsOn(h);
            case BandageSession b:
                // Pull to the middle of the band, then let go; a slip pulls it past, too tight.
                if (_slip && b.Tension > b.High)
                    Slipped();
                return (false, b.Tension < (_slip ? b.High + 0.08f : (b.Low + b.High) / 2f), 0);
            case CastSession { WantsKeeper: true } waiting:
                // A Calling with nobody holding the body: call for a hand now and then, and wait.
                if (waiting.StepsDone % (SimClock.TicksPerSecond * 4) == 1)
                    Send(new AskForHandCommand(waiting.Worker));
                return (false, false, 0);
            case Afterlife.HoldSession hold:
                // Brace just before each pull peaks, and ease off between.
                return (false, hold.PullAge >= Afterlife.HoldSession.PeakAt - hold.Half - 2, 0);
            case Carpentry.PitSession pit:
                // Pull with each stroke the top sawyer starts, and let go between.
                return (false, pit.CallOpen || pit.Down > 0f && pit.Down < 1f, 0);
            case Carpentry.LiftSession lift:
                // Keep it up, and lean against the way it's tipping; set it down to rest once the arms are nearly spent.
                return (false, lift.Strain < 0.9f || lift.Up && lift.Strain < 0.97f, lift.Tilt + lift.Sway * 8f > 0.05f ? -1 : lift.Tilt + lift.Sway * 8f < -0.05f ? 1 : 0);
            case Carpentry.JoinSession { Work: Carpentry.JoinWork.Axle, Cutting: false } axle when axle.Partner.IsNull && axle.StepsDone % (SimClock.TicksPerSecond * 4) == 1:
                Send(new AskForHandCommand(axle.Worker));
                return (false, false, 0);
            case Carpentry.SawSession { Great: true, Hewing: false } great when great.Partner.IsNull && great.StepsDone % (SimClock.TicksPerSecond * 4) == 1:
                Send(new AskForHandCommand(great.Worker));
                return (false, false, 0);
            case SledgeSession sledge:
                // Heave it up and hold it; let it fall on the smith's call.
                return (false, !(sledge.CallOpen && sledge.Raise >= SledgeSession.FullHeave), 0);
            case CastSession c:
            {
                // The book's runes for the mending it means, each on a beat; then let it go.
                if (c.Woven.Count >= _weave.Count)
                    return (c.Woven.Count > 0, false, 0);
                if (c.NextBeat == _lastRune || Math.Abs(c.StepsDone - c.NextBeat) > 1)
                    return (false, false, 0);
                _lastRune = c.NextBeat;
                _rune = _weave[c.Woven.Count];
                // A slip: the wrong rune keyed, halfway through the weave.
                if (_slip && !_slipCounted && c.Woven.Count == _weave.Count / 2)
                {
                    Slipped();
                    _rune = (_rune + 1) % Math.Max(1, world().Defs.AllRunes.Count);
                }
                return (false, false, 0);
            }
            case MeditateSession m:
            {
                // Toward the centre by what it's seen, guessing the unseen open.
                var way = m.Heading();
                _axis2 = way == 0 ? -1 : way == 2 ? 1 : 0;
                return (false, false, way == 1 ? 1 : way == 3 ? -1 : 0);
            }
            case ProbeSession pr:
                return Probe(pr);
            case PickSession pk:
                return Pick(pk);
            case TrueSession tr:
                return True(tr);
            case SpanSession sp:
                return Span(sp);
            case FitSession fit:
                return Fit(fit);
            case LoupeSession lp:
                return Loupe(lp);
        }
        // Each trade's further games, in its own file.
        return PlayKitchen(s) ?? PlayBench(s) ?? PlayWoodshop(s) ?? PlayForge(s) ?? PlayShrine(s) ?? PlayInn(s) ?? (false, false, 0);
    }

    /// <summary>
    /// Where the smoothed feel rose past the mark and where it fell back: the middle of the two is
    /// where the catch is (the smoothing's lag cancels out). Null until it's found one.
    /// </summary>
    private float? Middle(float at, float mark)
    {
        if (_rose == null && _feel > mark)
            _rose = at;
        else if (_rose is { } rose && _feel < mark)
        {
            _rose = null;
            return (rose + at) * 0.5f;
        }
        return null;
    }

    /// <summary>How far below the line a bot brings the tankard upright: the last of the pour builds the head.</summary>
    private const float PourUprightBelow = 0.3f;

    /// <summary>Steps a bot holds the probe still on a catch before pressing in: long enough for a good read.</summary>
    private const int ProbeReadSteps = 60;

    /// <summary>
    /// Sweep the seam light; at a catch, go back to its middle, hold still for a read, press in and
    /// flick the spring out at the middle of the green; then on to the hinge.
    /// </summary>
    private (bool, bool, int) Probe(ProbeSession s)
    {
        _feel = _feel * 0.8f + s.Feel * 0.2f;
        if (_working > 0)
        {
            if (s.Disarmed)
                _working = 0;
            else if (_working > 1)
            {
                _working--;
                return (false, false, 0); // still: the read settling
            }
            else if (s.Pressure < (s.Low + s.High) * 0.5f)
                return (false, true, 0); // pressing in
            else
            {
                _act = true; // in the middle of the green: out it comes (or nothing gives)
                _working = 0;
                return (false, true, 0);
            }
        }
        if (s.Pressure > ProbeSession.LightTouch - 0.08f)
            return (false, false, 0); // never drag weight across the seam
        if (_middle is { } target)
        {
            var off = target - s.Probe;
            if (MathF.Abs(off) > ProbeSession.ProbePerStep)
                return (false, false, Math.Sign(off));
            _middle = null;
            _skipTo = target + ProbeSession.CatchRange;
            _working = ProbeReadSteps;
            return (false, false, 0);
        }
        if (s.Probe >= _skipTo && Middle(s.Probe, 0.5f) is { } found)
            _middle = found;
        return (false, false, 1);
    }

    /// <summary>
    /// A finger on the tension (W/S), kept in the middle of the band; lift the pin until it stiffens,
    /// and set it there (F). Feeling it ease past the stiff point, it's gone too far: back down.
    /// </summary>
    private (bool, bool, int) Pick(PickSession s)
    {
        if (s.Pin != _pin || s.Slips != _slips)
        {
            (_pin, _slips) = (s.Pin, s.Slips);
            _feel = 0f;
            _dir = 1;
        }
        // W/S to keep the tension near the middle of the band.
        var mid = (s.Low + s.High) * 0.5f;
        _axis2 = s.Tension < mid - 0.03f ? -1 : s.Tension > mid + 0.03f ? 1 : 0;
        if (s.Turning || s.Tension < s.Low)
            return (false, false, 0);
        var easing = s.Feel < _feel - 0.01f && _feel > 0.5f;
        _feel = s.Feel;
        if (s.Feel >= s.SetFeel + (1f - s.SetFeel) * 0.4f)
            return (s.InBand, false, 0); // well inside where it sets: set it

        if (easing)
            _dir = -1;
        else if (s.Feel < 0.2f)
            _dir = 1;
        return (false, false, _dir);
    }

    /// <summary>Spin it to chalk the rim; to each spoke the chalk (less the key's turns since) says is out of the band, a turn the way it wants; then spin again.</summary>
    private (bool, bool, int) True(TrueSession s)
    {
        if (s.Spinning)
            return (false, false, 0);
        if (_working > 0)
        {
            _working--; // let the last key go before the next
            return (false, false, 0);
        }
        var worst = TrueSession.Worst(s, s.BandWidth * 0.5f);
        if (s.Turns == 0 || worst < 0)
            return (true, false, 0);
        if (worst != s.Selected)
        {
            var ahead = (worst - s.Selected + TrueSession.Spokes) % TrueSession.Spokes;
            return (false, false, ahead <= TrueSession.Spokes / 2 ? 1 : -1);
        }
        _axis2 = TrueSession.Reckoned(s, worst) > 0 ? -1 : 1; // W tightens a bulge in, S lets a dip out
        _working = 3;
        return (false, false, 0);
    }

    /// <summary>Stone by stone along the span and back: carry the level to it, let it settle, then wedge a sag up or ease a high one down till it reads level.</summary>
    private (bool, bool, int) Span(SpanSession s)
    {
        if (_spoke < 0)
            (_spoke, _dir) = (0, 1);
        if (s.AtStone != _spoke)
            return (false, false, Math.Sign(_spoke - s.AtStone));
        if (MathF.Abs(s.At * (SpanSession.Stones - 1) - _spoke) > 0.01f || _working-- > 0)
            return (false, false, 0); // settling, or letting the last wedge show
        _working = 8;
        var mark = s.BandWidth * 0.8f;
        if (s.Reading < -mark)
            return (true, false, 0);
        if (s.Reading > mark)
        {
            _act = true;
            return (false, false, 0);
        }
        if (_spoke + _dir is < 0 or >= SpanSession.Stones)
            _dir = -_dir;
        _spoke += _dir;
        return (false, false, 0);
    }

    /// <summary>Keep each part square over its seat and press it in only while it is; then wind to the catch and latch.</summary>
    private (bool, bool, int) Fit(FitSession s)
    {
        if (s.Winding)
        {
            if (s.Catching)
            {
                _act = true;
                return (false, false, 0);
            }
            _working = (_working + 1) % 24;
            return (_working == 0, false, 0);
        }
        var square = MathF.Abs(s.Offset) <= s.TrueHalfWidth * 0.6f;
        var steer = MathF.Abs(s.Offset) > s.TrueHalfWidth * 0.25f ? -Math.Sign(s.Offset) : 0;
        // A slip: impatient with the middle part, it leans on it while it's still off true.
        if (_slip && s.Part == s.Parts / 2 && s.Settling == 0)
        {
            if (!s.OnTrue && s.Pressure > FitSession.LightTouch)
                Slipped();
            return (false, s.Bind < 0.8f, steer);
        }
        return (false, square && s.Bind < 0.3f, steer);
    }

    /// <summary>Sweep the piece; at a catch, back to its middle and study it till it's read; then on to the end.</summary>
    private (bool, bool, int) Loupe(LoupeSession s)
    {
        _feel = _feel * 0.8f + s.Catch * 0.2f;
        if (_working > 0)
        {
            _working = s.Reading > _progress || s.ReadCount > _read ? 60 : _working - 1;
            _progress = s.Reading;
            if (s.ReadCount == _read && _working > 0)
                return (false, true, 0);
            (_working, _read) = (0, s.ReadCount);
        }
        if (_middle is { } target)
        {
            var off = target - s.At;
            if (MathF.Abs(off) > LoupeSession.MovePerStep)
                return (false, false, Math.Sign(off));
            _middle = null;
            _skipTo = target + LoupeSession.CatchRange;
            _working = 60;
            return (false, true, 0);
        }
        if (s.At >= _skipTo && Middle(s.At, 0.5f) is { } found)
            _middle = found;
        return (false, false, 1);
    }

    /// <summary>The cell on a piece being washed with the most grime left on it.</summary>
    private static (int X, int Y) Darkest(Hospitality.ScrubSession s)
    {
        var best = (X: Hospitality.ScrubSession.GridW / 2, Y: Hospitality.ScrubSession.GridH / 2);
        var most = 0f;
        for (var y = 0; y < Hospitality.ScrubSession.GridH; y++)
            for (var x = 0; x < Hospitality.ScrubSession.GridW; x++)
                if (s.BlotAt(x, y) > most)
                    (best, most) = ((x, y), s.BlotAt(x, y));
        return best;
    }

    private static (int X, int Y) Grimiest(WashSession s)
    {
        var best = (X: WashSession.GridW / 2, Y: WashSession.GridH / 2);
        var most = 0f;
        for (var y = 0; y < WashSession.GridH; y++)
            for (var x = 0; x < WashSession.GridW; x++)
                if (s.GrimeAt(x, y) > most)
                    (best, most) = ((x, y), s.GrimeAt(x, y));
        return best;
    }

    /// <summary>
    /// Laying on hands: skim toward the feet until there's a warmth under them, stop and press it
    /// till it's read, keep pressing to draw it (steering for the band's middle), then let go and
    /// skim on to the next.
    /// </summary>
    private (bool Press, bool Held, int Axis) HandsOn(HandsOnSession h)
    {
        if (h.Linked)
            return (false, true, Steer(h.Link.Composure, h.Link.HalfWidth));
        if (h.Under >= 0 && h.Spots[h.Under] is var s && s.Sensed > 0f && s.Drawn < 1f)
            return (false, !h.Held || h.Reading, 0); // press; let go a moment if pressing does nothing (a wound just came across)
        return (false, false, 1);
    }

    /// <summary>Steer a needle back toward the middle of its band.</summary>
    /// <summary>A smith's other hand on the hammer: A/D toward the spot to strike when it's off it.</summary>
    public static int HammerCentre(HammerSession h) => MathF.Abs(h.Offset - h.Spot) > h.CentreWidth * 0.3f ? Math.Sign(h.Spot - h.Offset) : 0;

    /// <summary>A smith's arm: hold W till the mark's in reach, then let go; -1 is W held.</summary>
    public static int HammerSwing(HammerSession h) =>
        h.Ready && !h.Swinging && h.Raise < h.Mark - h.WindRate * 0.5f ? -1 : 0;

    private static int Steer(float needle, float halfWidth)
    {
        var off = needle - 0.5f;
        return MathF.Abs(off) > halfWidth * 0.25f ? -Math.Sign(off) : 0;
    }
}
