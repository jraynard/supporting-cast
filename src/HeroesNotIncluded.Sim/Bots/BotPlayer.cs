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

/// <summary>
/// A stand-in player, for scenario tests and solo playtests. It plays only through commands and
/// knows only what <see cref="PerceptionView"/> tells it, exactly as a person at the keyboard
/// would, so a test driven by bots proves the game is playable from the player's side of the line.
/// It plays its trade: the smith takes repairs and judges hammer or stone by what the blade looks
/// like, the innkeeper pours for orders and matches parties to the quests it believes fit, the
/// healer draws wounds and casts the mending whose feel it knows, the carpenter feels a strongbox
/// over for traps and picks its lock by the feel alone. It keeps its shop's sign as a simple
/// market: down when it hears a price called too dear, up while its line is long. It serves
/// whoever's nearest the counter first. Every mini-game is played by hand, through the same
/// predictor as the game's.
/// </summary>
/// <param name="world">The current sim: a resync replaces it.</param>
public sealed class BotPlayer(Func<Simulation> world, Guid player, Action<ISimCommand> send, ulong seed)
{
    /// <summary>Ticks between decisions. Mini-games are played every tick regardless.</summary>
    public const int ThinkTicks = 30;

    /// <summary>The fewest ticks between things it says, so two bots don't talk forever.</summary>
    public const int TalkCooldownTicks = SimClock.TicksPerSecond * 20;

    /// <summary>A task it hasn't finished in this long is dropped.</summary>
    public const int TaskTimeoutTicks = SimClock.TicksPerHour * 2;

    private readonly SimRandom _rand = new(seed);
    private readonly WorkPredictor _work = new();
    private readonly HashSet<Entity> _greeted = [];
    private readonly HashSet<(int Party, int Quest)> _offered = [];
    private PerceptionView? _view;
    private long _lastTick = -1;
    private long _nextThink;
    private long _nextTalk;
    private long _heardUpTo;

    // Pricing: the last heard line it's weighed, and when it last changed each price.
    private long _pricedUpTo;
    private readonly Dictionary<ServiceKind, long> _repriced = [];
    private bool _sentToWork;

    // The order in hand.
    private int _task;
    private long _taskSince;
    private long _lastWalk;

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

    // Feeling over: who it last ran its hands over, and when.
    private readonly Dictionary<Entity, long> _feltOver = [];

    // Weaving: the runes it means to key (indices into the defs' runes), and the one to key this frame.
    private List<int> _weave = [];
    private int _rune = -1;

    /// <summary>The session's other action (Space) this frame: set by <see cref="Play"/>.</summary>
    private bool _act;

    /// <summary>Done with the pot's seasoning: step away, which keeps it.</summary>
    private bool _seasoned;

    public Guid Player => player;

    /// <summary>What it last decided, for tests and debugging.</summary>
    public string Doing { get; private set; } = "";

    /// <summary>The order the bot is seeing to, if any (for tests).</summary>
    public int Task => _task;

    /// <summary>Call once per frame or tick. Does nothing until the bot has a character.</summary>
    public void Tick()
    {
        var sim = world();
        var me = Players.CharacterOf(sim, player);
        if (me.IsNull || !sim.World.Has<Avatar>(me))
            return;
        var now = sim.Clock.Tick;
        var steps = _lastTick < 0 ? 0 : (int)Math.Clamp(now - _lastTick, 0, SimClock.TicksPerSecond);
        _lastTick = now;
        if (_view == null || !_view.IsFor(sim, me))
            _view = Perception.For(sim, me);

        // A press meant for the last step (drawing the piece from the fire) mustn't land on the next.
        var live = sim.World.TryGet<WorkSession>(me, out var s) ? s.StartTick : -1;
        _axis2 = 0;
        _rune = -1;
        _act = false;
        var (press, held, axis) = _work.Session?.StartTick == live ? Play(_work.Session) : (false, false, 0);
        // At the anvil it puts the hammer down as the colour fades, and takes the piece back to the fire.
        if (_work.Session is HammerSession { Workable: < 0.95f } && !_stopped)
        {
            _stopped = true;
            send(new StopWorkCommand(me));
        }
        if (_work.Session is SeasonSession && _seasoned && !_stopped)
        {
            _stopped = true;
            send(new StopWorkCommand(me));
        }
        _work.Update(sim, me, steps, press, held, axis, send, _axis2, _rune, _act);
        if (_work.Session == null && !sim.World.Has<WorkSession>(me) && Fight(sim, me, _view, now))
            return; // a fight's every tick: a dodge can't wait for the next think
        if (_work.Session != null || sim.World.Has<WorkSession>(me) || now < _nextThink)
            return;
        _nextThink = now + ThinkTicks;
        Think(sim, me, _view, now);
    }

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
        }
        switch (s)
        {
            case HammerSession h:
                return (h.Ready && MathF.Abs(h.Marker - h.Mark) <= h.HalfWidth * 0.5f, false, 0);
            case BloomSession bloom:
                // Keep it orange: pump below the middle of the band, ease off above. Out once it's grown.
                _act = bloom.Grown;
                return (false, bloom.Temp < (bloom.Lo + bloom.SlagAt) * 0.5f, 0);
            case HeatSession heat:
                // Pump, and draw it out once it glows the colour at the top of its working heat.
                _act = heat.LooksReady;
                return (false, true, 0);
            case QuenchSession q:
                // Keep it under till the steam thins to a hiss.
                return (false, !(q.Dunked && q.Steam < 0.2f), 0);
            case SharpenSession sh:
            {
                _resting = _resting ? sh.Heat > 0.3f : sh.Heat > 0.8f;
                return (false, !_resting && sh.InBand, Steer(sh.Angle, sh.HalfWidth));
            }
            case StewSession st:
            {
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
                // Back and forth, a stroke every few steps.
                return (false, false, (sc.StepsDone / 8) % 2 == 0 ? -1 : 1);
            case Hospitality.MakeBedSession mb:
            {
                var press = mb.OnCue && !_struck;
                _struck = mb.OnCue;
                return (press, false, 0);
            }
            case TapSession tap:
            {
                var press = tap.OnTheTap && !_struck;
                _struck = tap.OnTheTap;
                return (press, false, 0);
            }
            case MixSession mx:
            {
                // Each cue's key as it reaches the mark.
                if (mx.Resolved >= mx.Cues.Length || mx.Resolved == _lastRune || mx.StepsDone < mx.DueOf(mx.Resolved))
                    return (false, false, 0);
                _lastRune = mx.Resolved;
                var cue = mx.Cues[mx.Resolved];
                return (cue == 'F', false, cue == 'A' ? -1 : cue == 'D' ? 1 : 0);
            }
            case PourSession p:
                // Stop where the settled head will bring it to the line.
                _stopped |= p.Level + p.Foam * 0.73f >= p.Line;
                return (false, !_stopped, 1);
            case DrawSession d:
                return (false, true, Steer(d.Composure, d.HalfWidth));
            case FeelSession f:
                return (false, true, 1); // a slow, pressed pass from head to feet: it misses nothing
            case BandageSession b:
                return (false, b.Tension < (b.Low + b.High) / 2f, 0); // pull to the middle of the band, then let go
            case CastSession c:
            {
                // The book's runes for the mending it means, each on a beat; then let it go.
                if (c.Woven.Count >= _weave.Count)
                    return (c.Woven.Count > 0, false, 0);
                if (c.NextBeat == _lastRune || Math.Abs(c.StepsDone - c.NextBeat) > 1)
                    return (false, false, 0);
                _lastRune = c.NextBeat;
                _rune = _weave[c.Woven.Count];
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
        return (false, false, 0);
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

    /// <summary>Sweep the seam light; at a catch, go back to its middle and ease the spring out; then on to the hinge.</summary>
    private (bool, bool, int) Probe(ProbeSession s)
    {
        _feel = _feel * 0.8f + s.Feel * 0.2f;
        var mid = (s.Low + s.High) * 0.5f;
        if (_working > 0)
        {
            _working = s.Progress > _progress ? 60 : _working - 1; // stalled: not on it after all
            _progress = s.Progress;
            if (!s.Disarmed && _working > 0)
                return (false, s.Pressure < mid - 0.03f, 0);
            _working = 0;
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
            _working = 90;
            return (false, true, 0);
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
        if (s.Feel > 0.85f)
            return (s.InBand, false, 0); // there: set it

        if (easing)
            _dir = -1;
        else if (s.Feel < 0.2f)
            _dir = 1;
        return (false, false, _dir);
    }

    /// <summary>As each spoke comes past the gauge: a bulge tightened in, a dip slackened out, once a pass.</summary>
    private (bool, bool, int) True(TrueSession s)
    {
        var at = s.Turn * TrueSession.Spokes;
        var frac = at - MathF.Floor(at);
        if (frac is > 0.12f and < 0.88f || s.AtGauge == _spoke)
            return (false, false, 0);
        _spoke = s.AtGauge;
        var mark = s.BandWidth * 0.8f;
        return s.Gauge > mark ? (true, false, 0) : s.Gauge < -mark ? (false, false, -1) : (false, false, 0);
    }

    /// <summary>As the level reaches each stone: a sag wedged up, a high one eased down, once a pass.</summary>
    private (bool, bool, int) Span(SpanSession s)
    {
        var at = s.At * (SpanSession.Stones - 1);
        if (MathF.Abs(at - MathF.Round(at)) > 0.12f || s.AtStone == _spoke)
            return (false, false, 0);
        _spoke = s.AtStone;
        var mark = s.BandWidth * 0.8f;
        return s.Reading < -mark ? (true, false, 0) : s.Reading > mark ? (false, false, -1) : (false, false, 0);
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

    /// <summary>Steer a needle back toward the middle of its band.</summary>
    private static int Steer(float needle, float halfWidth)
    {
        var off = needle - 0.5f;
        return MathF.Abs(off) > halfWidth * 0.25f ? -Math.Sign(off) : 0;
    }

    // ---------------- Fighting (free movement) ----------------

    private Entity _foe = Entity.Null;
    private float2 _sentWish;
    private bool _guarding;

    /// <summary>A bot's foe this close is a fight it takes on, armed and on its feet.</summary>
    private const int FightRange = 8;

    /// <summary>
    /// With free movement (<see cref="Combat.FreeMoveSystem"/>), an armed bot that isn't hurt stands
    /// and fights a raider or predator it can see near, as a player would: weapon drawn, to its
    /// reach, striking the way it aims; seeing a blow coming at it, it steps out of the sweep (or
    /// guards, if it's too winded to dash). Hurt, it gets away (<see cref="Safety"/>). Only what it
    /// can see, only through commands. True while it's fighting.
    /// </summary>
    private bool Fight(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (!w.TryGet<Combat.FreeMover>(me, out var free) || !w.Get<Creature>(me).Able || Ai.RaiderBrain.WantsToFlee(sim, me)
            || !w.TryGet<Equipment>(me, out var eq) || !w.IsAlive(eq.Weapon) || w.Get<Thing>(eq.Weapon).Def.Weapon is not { } weapon)
            return StopFighting(me, free.Drawn);
        var here = w.Get<Position>(me).Pos;
        if (_foe.IsNull || !w.IsAlive(_foe) || !view.CanSee(_foe) || !w.Get<Creature>(_foe).Able)
        {
            _foe = Entity.Null;
            var best = float.MaxValue;
            foreach (var e in view.Creatures())
                if (w.Get<Creature>(e) is { Dead: false, Downed: false } c && Factions.Attacker(c.Faction) && !w.Has<Combat.Leaving>(e)
                    && c.Def.Race != Race.Dragon && w.Get<Position>(e).Cell.Z == w.Get<Position>(me).Cell.Z)
                {
                    var p = w.Get<Position>(e).Pos;
                    var d = (p.X - here.X) * (p.X - here.X) + (p.Y - here.Y) * (p.Y - here.Y);
                    if (d < FightRange * FightRange && d < best)
                        (_foe, best) = (e, d);
                }
            if (_foe.IsNull)
                return StopFighting(me, free.Drawn);
        }
        Doing = $"fighting {w.Get<Creature>(_foe).Name}";
        if (!free.Drawn)
            send(new DrawWeaponCommand(me, true));
        var them = w.Get<Position>(_foe).Pos;
        var to = new float2(them.X - here.X, them.Y - here.Y);
        var dist = MathF.Sqrt(to.X * to.X + to.Y * to.Y);
        var aim = DetMath.Atan2(to.Y, to.X);
        var dir = dist > 1e-3f ? to * (1f / dist) : new float2(1, 0);
        var reach = weapon.Ranged ? Math.Max(2f, weapon.OptimalRange) : Combat.Reach.ShapeOf(sim, me, weapon).Length * 0.75f;

        // A blow coming at it: out of the sweep, to the side; winded, guard instead.
        if (w.TryGet<Combat.Windup>(_foe, out var blow) && blow.Impact - now <= 14 && Combat.Reach.Struck(sim, _foe,
                Combat.CombatRules.Attacks(sim, _foe)[Math.Min(blow.Attack, Combat.CombatRules.Attacks(sim, _foe).Count - 1)], blow.Aim).Contains(me))
        {
            var side = new float2(-DetMath.Sin(blow.Aim), DetMath.Cos(blow.Aim)) * (_rand.Chance(0.5f) ? 1f : -1f);
            if (w.TryGet<Combat.Guard>(me, out var g) && g.Stamina >= Combat.Blows.DodgeCost && blow.Impact - now <= 8)
                send(new DodgeCommand(me, side.X, side.Y, now));
            else if (!_guarding)
            {
                send(new CombatInputCommand(me, CombatInput.GuardUp, now));
                _guarding = true;
            }
            Move(me, side, aim);
            return true;
        }
        if (_guarding)
        {
            send(new CombatInputCommand(me, CombatInput.GuardDown, now));
            _guarding = false;
        }
        var wish = dir * Math.Clamp((dist - reach) * 2f, -1f, 1f);
        Move(me, wish, aim);
        if (dist - Combat.Reach.BodyRadius <= (weapon.Ranged ? weapon.Range : Combat.Reach.ShapeOf(sim, me, weapon).Length) && !w.Has<Combat.Windup>(me))
            send(new AttackCommand(me, _rand.Chance(0.25f) && !weapon.Ranged, aim));
        return true;
    }

    private void Move(Entity me, float2 wish, float aim)
    {
        var len = MathF.Sqrt(wish.X * wish.X + wish.Y * wish.Y);
        var w = len < 0.1f ? float2.Zero : wish;
        send(new FreeMoveCommand(me, w.X, w.Y, aim));
        _sentWish = w;
    }

    /// <summary>The fight's done (or it won't take it on): feet still, weapon put away. False, so it gets on with its day.</summary>
    private bool StopFighting(Entity me, bool drawn)
    {
        if (_foe.IsNull && _sentWish == float2.Zero && !_guarding)
            return false;
        _foe = Entity.Null;
        if (_guarding)
            send(new CombatInputCommand(me, CombatInput.GuardDown, 0));
        _guarding = false;
        if (_sentWish != float2.Zero)
            send(new FreeMoveCommand(me, 0f, 0f, 0f));
        _sentWish = float2.Zero;
        if (drawn)
            send(new DrawWeaponCommand(me, false));
        return false;
    }

    // ---------------- Deciding ----------------

    private void Think(Simulation sim, Entity me, PerceptionView view, long now)
    {
        if (Safety(sim, me, view, now) || Bedtime(sim, me, now) || Supper(sim, me, now))
            return;
        GoToWork(sim, me);
        Price(sim, me, view, now);
        if (Mend(sim, me, view))
            return;
        if (_task != 0 && (now - _taskSince > TaskTimeoutTicks || !Work(sim, me, view, now)))
            _task = 0;
        if (_task == 0)
            TakeOn(sim, me, view, now);
        if (_task == 0 && MendTown(sim, me, view, now))
            return;
        if (_task == 0 && Smith(sim, me, view, now))
            return;
        if (_task == 0 && !Upkeep(sim, me, view, now))
            Chat(view, me, now);
    }

    /// <summary>
    /// At night, with nothing in hand and tired, it goes to bed like a player would, so a solo
    /// round's night can pass (time runs on only while every player sleeps). Asleep, it does
    /// nothing at all: any step would wake it. True while it's sleeping or on its way.
    /// </summary>
    private bool Bedtime(Simulation sim, Entity me, long now)
    {
        var w = sim.World;
        if (w.Get<Mind>(me).Asleep)
        {
            Doing = "asleep";
            return true;
        }
        if (_task != 0 || !Ai.AiQueries.IsNight(sim) || w.Get<Needs>(me).Get(sim.Defs.Need("Rest")) > BedtimeRest)
            return false;
        Doing = "going to bed";
        var bed = Ai.AiQueries.FindFreeBed(sim, me);
        if (bed.IsNull || w.Get<Position>(me).Cell == w.Get<Thing>(bed).Cell)
            send(new SleepCommand(me));
        else if (now - _lastWalk >= ThinkTicks * 2)
        {
            send(new AvatarGoToCommand(me, w.Get<Thing>(bed).Cell));
            _lastWalk = now;
        }
        return true;
    }

    /// <summary>
    /// Out of harm's way, as a careful player would be: away from a fire close by, and from raiders,
    /// beasts or a dragon it can see coming (indoors if there's a door near; a dragon, from anywhere
    /// it's seen). It doesn't fight. True while it's getting clear or keeping its head down.
    /// </summary>
    private bool Safety(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        var here = w.Get<Position>(me).Cell;
        // Too hot where it stands (hours at the forge's mouth): a step back to cool off.
        var tol = w.Get<Creature>(me).Tolerance(now);
        if (Environment.Warmth.FeltAt(sim, here) > tol.SafeMax - 8 && Ai.AnimalBrain.FleeSpot(sim, me, here, 5) is { } cooler)
        {
            Doing = "stepping back from the heat";
            Walk(sim, me, cooler, now);
            return true;
        }
        // Fire close by, or anywhere in the building it's in (a roof alight overhead): out, and away.
        var building = Buildings.At(sim, here);
        foreach (var i in sim.Fire.BurningTiles)
        {
            var f = sim.Map.CellOf(i);
            var near = Math.Abs(f.Z - here.Z) <= 3 && Maps.Map.Distance(f, here) <= 3;
            if ((near || (!building.IsNull && Buildings.At(sim, f) == building)) && Ai.AnimalBrain.FleeSpot(sim, me, f, 10) is { } away)
            {
                Doing = "getting away from the fire";
                if (now - _lastWalk >= ThinkTicks * 2)
                {
                    send(new AvatarGoToCommand(me, away));
                    _lastWalk = now;
                }
                return true;
            }
        }
        Entity? foe = null;
        var nearest = int.MaxValue;
        foreach (var e in view.Creatures())
            if (w.Get<Creature>(e) is { Dead: false, Downed: false } c && Factions.Attacker(c.Faction) && !w.Has<Combat.Leaving>(e))
            {
                var d = Maps.Map.Distance(w.Get<Position>(e).Cell, here);
                if ((d <= Ai.Defence.VillagerRange || c.Def.Race == Race.Dragon) && d < nearest)
                    (foe, nearest) = (e, d);
            }
        if (foe is not { } threat)
            return false;
        var at = w.Get<Position>(threat).Cell;
        if (sim.Rooms.RoomAt(here) is { Indoors: true } && nearest > 2)
        {
            Doing = "keeping indoors";
            return true;
        }
        if ((Ai.Defence.Shelter(sim, me, at) ?? Ai.AnimalBrain.FleeSpot(sim, me, at, 12)) is { } safe)
        {
            Doing = "getting out of the way";
            if (now - _lastWalk >= ThinkTicks * 2)
            {
                send(new AvatarGoToCommand(me, safe));
                _lastWalk = now;
            }
            return true;
        }
        return false;
    }

    /// <summary>Food below which a bot goes and eats.</summary>
    private const float HungryBelow = 0.35f;

    /// <summary>Hungry, with nothing in hand: something to eat from the nearest store or pile, eaten where it's kept.</summary>
    private bool Supper(Simulation sim, Entity me, long now)
    {
        var w = sim.World;
        if (_task != 0 || w.Get<Needs>(me).Get(sim.Defs.Need("Food")) >= HungryBelow)
            return false;
        if (w.Get<Mind>(me).Job is EatHeldJob)
        {
            Doing = "eating";
            return true;
        }
        if (_food is { } chosen && (!w.IsAlive(chosen) || now - _foodSince > SimClock.TicksPerSecond * 60))
        {
            // Couldn't get at it in a minute: try something else for a while.
            _noFood.Add(chosen);
            _food = null;
        }
        if (now - _noFoodSince > SimClock.TicksPerHour)
        {
            _noFood.Clear();
            _noFoodSince = now;
        }
        if (_food == null)
        {
            var here = w.Get<Position>(me).Cell;
            var eater = w.Get<Creature>(me).Def;
            var best = int.MaxValue;
            foreach (var e in sim.AvailableItems())
            {
                var d = w.Get<Thing>(e).Def;
                if (!d.IsFood || !d.EdibleBy(eater) || (d.IsMeat && !d.Cooked) || _noFood.Contains(e) || sim.Reservations.IsReservedByOther(e, me))
                    continue;
                var at = sim.LocationOf(e);
                var dist = Maps.Map.Distance(at, here);
                if (dist < best && dist <= 60 && Ai.Navigation.CanReach(sim, me, here, at, w.Get<Thing>(e).Spawned ? PathEnd.OnTile : PathEnd.Touch))
                    (_food, best) = (e, dist);
            }
            _foodSince = now;
        }
        if (_food is not { } food)
            return false;
        Doing = "getting something to eat";
        if (AvatarSystem.CanEat(sim, me, food))
            send(new EatCommand(me, food));
        else
            Walk(sim, me, sim.LocationOf(food), now);
        return true;
    }

    private Entity? _food;
    private long _foodSince, _noFoodSince;
    private readonly HashSet<Entity> _noFood = [];

    /// <summary>Rest below which a bot turns in at night.</summary>
    private const float BedtimeRest = 0.85f;

    /// <summary>
    /// Between orders, the innkeeper keeps the place: beds made up, floors mopped, the dirty
    /// crockery washed. True while it's at it.
    /// </summary>
    private bool Upkeep(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        // Anyone who's asked the way within earshot is told it.
        if (view.WaysAsked().FirstOrDefault() is { Id: > 0 } ask)
        {
            Doing = $"pointing {ask.AskerName} the way";
            send(new Hospitality.DirectCommand(me, ask.Id));
            return true;
        }
        if (!w.TryGet<Trade>(me, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return false;
        var inn = ShopSystem.BuildingOf(sim, workplace);
        if (inn.IsNull || w.Get<PresetBuilding>(inn).Def.Name != "Inn")
            return false;
        var bed = trade.Def.Serving(ServiceKind.Lodging) ? Buildings.Parts(sim, inn).FirstOrDefault(p => Hospitality.Lodging.IsUnmade(sim, p), Entity.Null) : Entity.Null;
        if (!bed.IsNull)
        {
            Doing = "making up a bed";
            return AtStation(sim, me, bed, now, b => send(new BarWorkCommand(me, b)));
        }
        var anchor = w.Get<PresetBuilding>(inn).Anchor;
        var mess = view.Messes().Where(m => Maps.Map.Distance(m.Cell, anchor) <= CleanJob.InnReach && CleanJob.Mine(sim, trade.Def, inn, m.Cell))
            .OrderBy(m => Maps.Map.Distance(m.Cell, w.Get<Position>(me).Cell)).Select(m => (int3?)m.Cell).FirstOrDefault();
        if (mess is { } at)
        {
            Doing = "mopping the floor";
            if (AvatarSystem.InReach(w.Get<Position>(me).Cell, at))
                send(new Hospitality.ScrubCommand(me, at));
            else
                Walk(sim, me, at, now);
            return true;
        }
        // Its own crockery: the bar's tankards, or the kitchen's bowls.
        var mine = WashJob.VesselsOf(sim, me);
        if (BarWork.DirtyOn(sim, me) is { IsNull: false } dirty && w.Get<Thing>(dirty).Def.WashesInto is { } clean && mine.Contains(clean))
        {
            Doing = "washing up";
            return AtStation(sim, me, Nearest(sim, me, "WashTub"), now, tub => send(new BarWorkCommand(me, tub)));
        }
        return false;
    }

    /// <summary>The order kinds this character's trade answers.</summary>
    private static ServiceKind[] KindsFor(TradeDef trade) => trade.Name switch
    {
        "Blacksmith" => [ServiceKind.Repair],
        "Innkeeper" => [ServiceKind.Drink, ServiceKind.MixedDrink, ServiceKind.Lodging, ServiceKind.Quest], // the bar, the rooms and the quest board
        "Cook" => [ServiceKind.Meal], // the pot
        "Healer" => [ServiceKind.Healing],
        "Tinker" => [ServiceKind.Unlock, ServiceKind.WagonRepair, ServiceKind.Appraise],
        _ => trade.Brokers ? [ServiceKind.Quest] : [],
    };

    /// <summary>Take on the oldest open order of the trade's kind it knows of.</summary>
    private void TakeOn(Simulation sim, Entity me, PerceptionView view, long now)
    {
        if (!sim.World.TryGet<Trade>(me, out var trade) || KindsFor(trade.Def) is not { Length: > 0 } kinds)
            return;
        foreach (var e in view.Orders().OrderBy(e => FromCounter(sim, me, e.Poster)).ThenBy(e => e.PostedTick))
        {
            if (e.Status != OrderStatus.Open || e.Mine || !kinds.Contains(e.Kind) || (e.AssigneeName.Length > 0 && !e.MineToDo))
                continue;
            if (e.Kind == ServiceKind.Quest && !QuestFor(sim, view, e.Poster).HasValue)
                continue;
            if (e.Kind != ServiceKind.Quest && !e.MineToDo)
                send(new TakeOrderCommand(me, e.Id));
            _task = e.Id;
            _taskSince = now;
            return;
        }
    }

    /// <summary>One step of the order in hand. False when it's over (done, gone or hopeless).</summary>
    private bool Work(Simulation sim, Entity me, PerceptionView view, long now)
    {
        if (view.Orders().FirstOrDefault(e => e.Id == _task) is not { Status: OrderStatus.Open } order)
            return false;
        var poster = order.Poster;
        if (!view.CanSee(poster) && !sim.World.IsAlive(poster))
            return false;
        return order.Kind switch
        {
            ServiceKind.Repair => Repair(sim, me, view, poster, now),
            ServiceKind.Drink => Pour(sim, me, view, poster, now),
            ServiceKind.MixedDrink => view.WantedDrink(_task) is { Mix: not null } named ? MixFor(sim, me, view, poster, named, now) : Pour(sim, me, view, poster, now),
            ServiceKind.Meal => Serve(sim, me, view, poster, now),
            ServiceKind.Lodging => ShowUp(sim, me, poster, now),
            ServiceKind.Healing => Heal(sim, me, view, poster, now),
            ServiceKind.Quest => Broker(sim, me, view, poster, now),
            ServiceKind.Unlock => OpenBox(sim, me, view, poster, now),
            ServiceKind.WagonRepair => MendWagon(sim, me, view, poster, now),
            ServiceKind.Appraise => Appraise(sim, me, view, poster, now),
            _ => false,
        };
    }

    // ---------------- Pricing ----------------

    /// <summary>Game hours between changes to the same price.</summary>
    public const float RepriceHours = 1;

    /// <summary>The most it will ask, as a multiple of the default price.</summary>
    public const int MaxMarkup = 3;

    /// <summary>People in line it can see before it reckons business is good enough to charge more.</summary>
    public const int BusyLine = 3;

    /// <summary>
    /// A simple market, played from its side of the counter: hearing an adventurer call a price
    /// too dear, it drops that one; seeing a line of <see cref="BusyLine"/> or more, it puts up the
    /// price of what they've asked for. Its sign starts at the defaults. Only what it heard and saw.
    /// </summary>
    private void Price(Simulation sim, Entity me, PerceptionView view, long now)
    {
        if (MyShop(sim, me) is not { IsNull: false } shop || view.Prices(shop) is not { Count: > 0 } sign)
        {
            _pricedUpTo = view.Heard().Select(l => l.Id).DefaultIfEmpty(_pricedUpTo).Max();
            return;
        }
        var changed = new HashSet<ServiceKind>();
        void Set(PostedPrice p, int price)
        {
            var max = sim.Defs.Service(p.Kind).DefaultPrice * MaxMarkup;
            price = Math.Clamp(price, 1, Math.Max(1, max));
            if (price == p.Price || changed.Contains(p.Kind)
                || (_repriced.TryGetValue(p.Kind, out var at) && now - at < SimClock.HoursToTicks(RepriceHours)))
                return;
            send(new SetPriceCommand(me, shop, p.Kind, price, true));
            _repriced[p.Kind] = now;
            changed.Add(p.Kind);
            Doing = $"pricing {p.Label} at {price}";
        }
        foreach (var line in view.Heard())
        {
            if (line.Id <= _pricedUpTo)
                continue;
            _pricedUpTo = line.Id;
            if (line.Speaker is not { } who || who == me || line.Clarity != Clarity.Clear || !sim.World.Has<Adventurer>(who))
                continue;
            foreach (var p in sign)
                if (p.Offered && TooDear(sim.Defs.Service(p.Kind), p.Price, line.Text))
                    Set(p, p.Price - 1);
        }
        var cells = ShopSystem.LineCells(sim, shop);
        var waiting = view.Creatures().Where(e => e != me && sim.World.Has<Adventurer>(e) && cells.Contains(sim.World.Get<Position>(e).Cell)).ToHashSet();
        if (waiting.Count < BusyLine)
            return;
        foreach (var kind in view.Orders().Where(o => o.Status == OrderStatus.Open && waiting.Contains(o.Poster)).Select(o => o.Kind).Distinct())
            if (sign.FirstOrDefault(p => p.Kind == kind) is { Offered: true } p)
                Set(p, p.Price + 1);
    }

    /// <summary>Is this what an adventurer says when a price is more than they'll pay (the def's lines, filled in)?</summary>
    private static bool TooDear(ServiceDef def, int price, string text)
    {
        foreach (var template in def.TooDearLines.Append($"{{0}} silver for {def.Label}? I'll do without."))
        {
            var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(template).Replace(@"\{0}", price.ToString()).Replace(@"\{1}", ".+") + "$";
            if (System.Text.RegularExpressions.Regex.IsMatch(text, pattern))
                return true;
        }
        return false;
    }

    /// <summary>The shop this character keeps, if their trade works from one.</summary>
    private static Entity? MyShop(Simulation sim, Entity me) =>
        sim.World.TryGet<Trade>(me, out var trade) && trade.Def.WorkplaceDef is { } workplace ? ShopSystem.BuildingOf(sim, workplace) : null;

    /// <summary>How far someone stands from the front of this character's line (for serving the front first); large if it can't say.</summary>
    private static int FromCounter(Simulation sim, Entity me, Entity who)
    {
        if (MyShop(sim, me) is not { IsNull: false } shop || ShopSystem.LineCells(sim, shop) is not { Count: > 0 } cells
            || !sim.World.TryGet<Position>(who, out var at))
            return int.MaxValue;
        return Maps.Map.Distance(at.Cell, cells[0]);
    }

    /// <summary>Cells away past which it waits at its workplace for someone, rather than chasing them.</summary>
    public const int ChaseCells = 20;

    /// <summary>
    /// Walk toward someone (re-aimed now and then, as they move). Far off, it waits at its
    /// workplace instead: customers come to the building their order is served at. But a customer
    /// who handed something over doesn't come back for it (they're off seeing to their other
    /// errands), so with <paramref name="returning"/> it takes the finished piece to them wherever
    /// they are. True if they're already in reach.
    /// </summary>
    private bool Reach(Simulation sim, Entity me, Entity who, long now, bool returning = false)
    {
        if (Handing.InReach(sim, me, who))
            return true;
        if (now - _lastWalk < ThinkTicks * 2 || !sim.World.TryGet<Position>(who, out var at))
            return false;
        _lastWalk = now;
        var here = sim.World.Get<Position>(me).Cell;
        if (!returning && Maps.Map.Distance(here, at.Cell) > ChaseCells && Workplace(sim, me) is { } work)
        {
            if (Maps.Map.Distance(here, work) > 6)
                send(new AvatarGoToCommand(me, work));
            return false;
        }
        send(new AvatarGoToCommand(me, at.Cell));
        return false;
    }

    /// <summary>A standable cell by this character's trade building, if it has one.</summary>
    private static int3? Workplace(Simulation sim, Entity me)
    {
        if (!sim.World.TryGet<Trade>(me, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return null;
        foreach (var e in sim.World.Each<PresetBuilding>()) // the town's layout is common knowledge
        {
            var building = sim.World.Get<PresetBuilding>(e);
            if (building.Def == workplace)
                return sim.SpiralFrom(building.Anchor, 8).FirstOrDefault(c => sim.Map.Passable(c), building.Anchor);
        }
        return null;
    }

    private bool Repair(Simulation sim, Entity me, PerceptionView view, Entity owner, long now)
    {
        var w = sim.World;
        var blade = Entrusted(sim, me, owner);
        if (blade.IsNull)
        {
            Reach(sim, me, owner, now); // they hand it over once we're close
            return true;
        }
        var condition = view.Condition(blade) ?? "";
        Doing = $"repair: {condition}";
        // In the tongs: to the fire while it's dark, to the anvil while it glows. A forge gone out is fed first.
        if (view.Piece(me) is { } piece)
        {
            if (!piece.Hot && Nearest(sim, me, "Forge") is { IsNull: false } forge && view.FuelIn(forge) is <= 0f)
                return FeedForge(sim, me, view, forge, now);
            Doing = $"forging: {piece.Label} {(piece.Hot ? "to the anvil" : "to the fire")}";
            return AtStation(sim, me, Nearest(sim, me, piece.Hot ? "Anvil" : "Forge"), now, s => send(new SmithCommand(me, s)));
        }
        if (condition.Contains("chip") || condition.Contains("bent") || condition.Contains("breaking"))
            return AtStation(sim, me, Nearest(sim, me, "Forge"), now, s => send(new RepairCommand(me, s, blade)));
        if (condition.Contains("dull") || condition.Contains("blunt"))
        {
            // The grindstone if the smithy has one, else the whetstone in the kit.
            var wheel = Nearest(sim, me, "Grindstone");
            if (wheel.IsNull)
                send(new SharpenCommand(me, blade));
            else
                AtStation(sim, me, wheel, now, s => send(new SmithCommand(me, s)));
            return true;
        }
        if (Reach(sim, me, owner, now, returning: true))
            send(new GiveCommand(me, owner, blade, 1));
        return true;
    }

    /// <summary>Feed a forge that's gone out: charcoal from the smithy's fuel bin, then into the fire.</summary>
    private bool FeedForge(Simulation sim, Entity me, PerceptionView view, Entity forge, long now)
    {
        var w = sim.World;
        Doing = "feeding the forge";
        bool IsFuel(Entity e) => w.IsAlive(e) && w.Get<Thing>(e).Def.Name is "Charcoal" or "Coal";
        var pack = sim.PackOf(me);
        if (IsFuel(w.Get<Carrying>(me).Item) || (!pack.IsNull && sim.StoredIn(pack).Any(IsFuel)))
            return AtStation(sim, me, forge, now, f =>
            {
                send(new AvatarWorkCommand(me, true, w.Get<Thing>(f).Cell));
                send(new AvatarWorkCommand(me, false));
            });
        return AtStation(sim, me, Nearest(sim, me, "FuelBin"), now, bin =>
        {
            if (view.InContainer(bin).FirstOrDefault(IsFuel, Entity.Null) is { IsNull: false } coal)
                send(new TakeFromStorageCommand(me, coal, 10)); // a few shovelfuls
        });
    }

    /// <summary>A clean vessel of a kind it's actually carrying: what's on a shelf it happens to stand by won't come with it to the next station.</summary>
    private static Entity Carried(Simulation sim, Entity me, string vessel) => AvatarSystem.Supply(sim, me, d => d.Name == vessel);

    /// <summary>Walk to a station, then use it. False if there's none.</summary>
    private bool AtStation(Simulation sim, Entity me, Entity station, long now, Action<Entity> use)
    {
        if (station.IsNull)
            return false;
        if (!AvatarSystem.InReach(sim, sim.World.Get<Position>(me).Cell, station))
            Walk(sim, me, sim.World.Get<Thing>(station).Cell, now);
        else
            use(station);
        return true;
    }

    /// <summary>Feel a strongbox over for traps, then pick it, then hand it back open.</summary>
    private bool OpenBox(Simulation sim, Entity me, PerceptionView view, Entity owner, long now)
    {
        var box = Entrusted(sim, me, owner);
        if (box.IsNull)
        {
            Reach(sim, me, owner, now);
            return true;
        }
        if (view.Strongbox(box) is not { } known)
            return false;
        Doing = known.Locked ? (known.SearchedByMe ? "picking a lock" : "feeling for traps") : "handing back a strongbox";
        if (known.Locked)
        {
            send(known.SearchedByMe ? new PickLockCommand(me, box) : new ProbeBoxCommand(me, box));
            return true;
        }
        if (Reach(sim, me, owner, now, returning: true))
            send(new GiveCommand(me, owner, box, 1));
        return true;
    }

    /// <summary>
    /// Out to the customer's wagon (whose is whose is plain to see), and mend what's wrong with it:
    /// true the wheel, then a new axle (wood from the shop's chest if it hasn't the wood on it).
    /// Paid once nothing's wrong with it.
    /// </summary>
    private bool MendWagon(Simulation sim, Entity me, PerceptionView view, Entity owner, long now)
    {
        var w = sim.World;
        var wagon = Entity.Null;
        foreach (var e in w.Each<Wagon>())
            if (w.Get<Wagon>(e).Owner == owner)
                wagon = e;
        if (wagon.IsNull)
            return false;
        var cell = w.Get<Thing>(wagon).Cell;
        var seen = view.Wagon(wagon);
        if (!AvatarSystem.InReach(sim, w.Get<Position>(me).Cell, wagon) || seen is not { } known)
        {
            // An axle wants wood: from the shop on the way, if it's none on it.
            if (seen is { } s && (s.Damage & WagonDamage.Axle) != 0 && Wagons.WoodOn(sim, me) < Wagons.AxleWood && FetchWood(sim, me, view, now))
                return true;
            Doing = "going out to a wagon";
            Walk(sim, me, cell, now);
            return true;
        }
        if (known.Damage == WagonDamage.None)
            return false;
        Doing = "mending a wagon";
        if ((known.Damage & WagonDamage.Wheel) != 0)
            send(new TrueWheelCommand(me, wagon));
        else if (Wagons.WoodOn(sim, me) >= Wagons.AxleWood)
            send(new FitAxleCommand(me, wagon));
        else
            return FetchWood(sim, me, view, now);
        return true;
    }

    /// <summary>The tinker mends what a crisis broke: wood from the shop, then to the gap, and brace it. True while at it.</summary>
    private bool MendTown(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(me, out var trade) || !trade.Def.MendsTown || sim.Repairs.Breaches.Count == 0)
            return false;
        var breach = sim.Repairs.Breaches[0];
        if (Wagons.WoodOn(sim, me) < Repairs.MendWood)
            return FetchWood(sim, me, view, now);
        var here = w.Get<Position>(me).Cell;
        if (!breach.Cells.Exists(c => AvatarSystem.InReach(here, c.Cell)))
        {
            var near = breach.Cells.Select(c => c.Cell).Where(c => Ai.Navigation.CanReach(sim, me, here, c, PathEnd.Touch))
                .OrderBy(c => Maps.Map.Distance(c, here)).Select(c => (int3?)c).FirstOrDefault();
            if (near is not { } gap)
                return false;
            Doing = $"going to mend the {breach.Label}";
            Walk(sim, me, gap, now);
            return true;
        }
        Doing = $"mending the {breach.Label}";
        send(new MendBreachCommand(me, breach.Id));
        return true;
    }

    /// <summary>Bars it likes to have to hand before it'll smelt more.</summary>
    public const int SmeltBelow = 2;

    private long _noOreUntil;

    /// <summary>
    /// The smith between customers: a piece in the tongs is seen through (to the fire while it's
    /// dark, the anvil while it glows, the tub once it's shaped); in a call to arms, spears for the
    /// muster rack, and each one hung there; short of bars, ore from the smithy chest and charcoal
    /// from the bin go in the forge as a bloom. True while at it.
    /// </summary>
    private bool Smith(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(me, out var trade) || trade.Def.Name != "Blacksmith")
            return false;
        if (view.Piece(me) is { } piece)
        {
            if (!piece.Hot && Nearest(sim, me, "Forge") is { IsNull: false } lit && view.FuelIn(lit) is <= 0f)
                return FeedForge(sim, me, view, lit, now);
            var next = !piece.Hot ? "Forge" : piece.Shaped ? "QuenchTub" : "Anvil";
            Doing = $"forging: {piece.Label} to the {next.ToLowerInvariant()}";
            return AtStation(sim, me, Nearest(sim, me, next), now, st => send(new SmithCommand(me, st)));
        }
        if (sim.Militia.Paying && ArmTheTown(sim, me, view, now))
            return true;
        if (now < _noOreUntil || sim.Defs.AllRecipes.FirstOrDefault(r => r.Bloom) is not { } recipe || OnMe(sim, me, "IronBar") >= SmeltBelow)
            return false;
        var forge = Nearest(sim, me, "Forge");
        if (forge.IsNull)
            return false;
        var ore = recipe.Ingredients[0];
        var fuel = recipe.Ingredients.Count > 1 ? recipe.Ingredients[1] : null;
        if (!w.Get<Carrying>(me).Item.IsNull && w.Get<Thing>(w.Get<Carrying>(me).Item).Def != ore.ThingDef)
        {
            send(new StowCommand(me));
            return true;
        }
        if (OnMe(sim, me, ore.Def) < ore.Count)
            return Fetch(sim, me, view, now, ore, "ore");
        if (fuel != null && OnMe(sim, me, fuel.Def) < fuel.Count)
            return Fetch(sim, me, view, now, fuel, "charcoal");
        Doing = "smelting a bloom";
        return AtStation(sim, me, forge, now, f => send(new CraftCommand(me, f, recipe)));
    }

    /// <summary>The spear the call to arms is answered with: cheap (a bar and two hafts), and quick at the anvil.</summary>
    public const string MilitiaWeapon = "ForgeIronSpear";

    /// <summary>A weapon of its own making to hang on the muster rack, or one to make. False if it can't do either.</summary>
    private bool ArmTheTown(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        var rack = Nearest(sim, me, Militia.MilitiaSystem.RackDef);
        if (rack.IsNull)
            return false;
        bool Spare(Entity e) => Militia.MilitiaSystem.IsWeapon(sim, e) && w.TryGet<Crafted>(e, out var c) && c.Maker == me
            && !w.Has<Militia.TownArms>(e) && !w.Has<Entrusted>(e);
        var pack = sim.PackOf(me);
        if (Spare(w.Get<Carrying>(me).Item))
        {
            Doing = "taking a spear to the muster rack";
            return AtStation(sim, me, rack, now, r =>
            {
                send(new AvatarWorkCommand(me, true, w.Get<Thing>(r).Cell));
                send(new AvatarWorkCommand(me, false));
            });
        }
        if (!pack.IsNull && sim.StoredIn(pack).FirstOrDefault(Spare, Entity.Null) is { IsNull: false } spare)
        {
            Doing = "taking a spear to the muster rack";
            return AtStation(sim, me, rack, now, r => send(new StoreFromPackCommand(me, r, spare)));
        }
        if (sim.Defs.AllRecipes.FirstOrDefault(r => r.Name == MilitiaWeapon) is not { } recipe || !RecipeBooks.Knows(sim, me, recipe))
            return false;
        var bar = recipe.Ingredients[0];
        if (OnMe(sim, me, bar.Def) < bar.Count)
            return false; // smelt first
        foreach (var x in recipe.Ingredients.Skip(1))
            if (OnMe(sim, me, x.Def) < x.Count)
                return Fetch(sim, me, view, now, x, x.ThingDef.Label);
        var forge = Nearest(sim, me, "Forge");
        if (forge.IsNull)
            return false;
        Doing = "forging a spear for the muster";
        return AtStation(sim, me, forge, now, f => send(new CraftCommand(me, f, recipe)));
    }

    /// <summary>Units of an item in its hands and pack.</summary>
    private static int OnMe(Simulation sim, Entity me, string def)
    {
        var w = sim.World;
        var n = 0;
        if (w.Get<Carrying>(me).Item is { IsNull: false } held && w.Get<Thing>(held).Def.Name == def)
            n += w.Get<Stack>(held).Count;
        if (sim.PackOf(me) is { IsNull: false } pack)
            foreach (var e in sim.StoredIn(pack))
                if (w.Get<Thing>(e).Def.Name == def)
                    n += w.Get<Stack>(e).Count;
        return n;
    }

    /// <summary>Take what the forge wants from the containers at its workplace (the chest, the fuel bin). False, and gives up a while, if there's none.</summary>
    private bool Fetch(Simulation sim, Entity me, PerceptionView view, long now, ThingCount want, string what)
    {
        var w = sim.World;
        if (MyShop(sim, me) is not { IsNull: false } shop)
            return false;
        var store = Buildings.Parts(sim, shop).Where(e => w.Get<Thing>(e).Def.Building?.StorageStacks > 0)
            .OrderBy(e => e.Index).ToList();
        // Its own workplace's chests are common knowledge to it, as a keeper knows their shelves.
        var holding = store.FirstOrDefault(e => sim.StoredIn(e).Any(i => w.Get<Thing>(i).Def == want.ThingDef), Entity.Null);
        if (holding.IsNull)
        {
            _noOreUntil = now + SimClock.TicksPerHour;
            return false;
        }
        Doing = $"fetching {what}";
        return AtStation(sim, me, holding, now, s =>
        {
            if (view.InContainer(s).FirstOrDefault(e => w.Get<Thing>(e).Def == want.ThingDef, Entity.Null) is { IsNull: false } item)
                send(new TakeFromStorageCommand(me, item, want.Count * 2));
        });
    }

    /// <summary>Wood from a chest at its workplace, for an axle. False if there's none to be had.</summary>
    private bool FetchWood(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(me, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return false;
        foreach (var e in w.Each<PartOf>())
        {
            var b = w.Get<PartOf>(e).Building;
            if (!w.TryGet<PresetBuilding>(b, out var pb) || pb.Def != workplace || w.Get<Thing>(e).Def.Name != "Chest")
                continue;
            var chest = e;
            if (view.InContainer(chest).FirstOrDefault(i => w.Get<Thing>(i).Def.Name == "Wood", Entity.Null) is not { IsNull: false } wood)
                continue;
            Doing = "fetching wood for an axle";
            return AtStation(sim, me, chest, now, _ => send(new TakeFromStorageCommand(me, wood, Wagons.AxleWood)));
        }
        return false;
    }

    /// <summary>Look a curio over under the loupe, then hand it back with an honest reckoning (its middle).</summary>
    private bool Appraise(Simulation sim, Entity me, PerceptionView view, Entity owner, long now)
    {
        var curio = Entrusted(sim, me, owner);
        if (curio.IsNull)
        {
            Reach(sim, me, owner, now);
            return true;
        }
        if (view.Curio(curio) is not { } known)
            return false;
        if (known.Low == null)
        {
            Doing = "looking a curio over";
            send(new AppraiseCommand(me, curio));
            return true;
        }
        Doing = "handing back a curio";
        if (Reach(sim, me, owner, now, returning: true))
            send(new GiveCommand(me, owner, curio, 1));
        return true;
    }

    /// <summary>The piece someone handed this character to mend.</summary>
    private static Entity Entrusted(Simulation sim, Entity me, Entity owner)
    {
        var w = sim.World;
        var held = w.Get<Carrying>(me).Item;
        if (w.IsAlive(held) && w.TryGet<Entrusted>(held, out var e) && e.Owner == owner)
            return held;
        var pack = sim.PackOf(me);
        if (!pack.IsNull)
            foreach (var item in sim.StoredIn(pack))
                if (w.TryGet<Entrusted>(item, out var x) && x.Owner == owner)
                    return item;
        return Entity.Null;
    }

    /// <summary>
    /// A named drink: the adds from the shelf behind the bar, a clean tankard, then mixed at the bar
    /// to its cues, and handed over. Without what it takes, an ale will have to do.
    /// </summary>
    private bool MixFor(Simulation sim, Entity me, PerceptionView view, Entity drinker, ThingDef named, long now)
    {
        var w = sim.World;
        var held = w.Get<Carrying>(me).Item;
        if (w.IsAlive(held) && w.Get<Thing>(held).Def.Drink)
        {
            if (Reach(sim, me, drinker, now))
                send(new GiveCommand(me, drinker, held, 1));
            return true;
        }
        var bar = Nearest(sim, me, "BarCounter");
        var cask = bar.IsNull ? Entity.Null : MixSession.CaskFor(sim, bar, named);
        if (cask.IsNull)
            return Pour(sim, me, view, drinker, now);
        var into = w.Get<Thing>(cask).Def.Building!.Cask!.Into;
        if (Carried(sim, me, into).IsNull)
            return Crockery(sim, me, view, now, into, cask);
        // Each add, from the shelf behind the bar (or the larder, for berries).
        foreach (var add in MixSession.AddsOf(sim, named, null))
        {
            if (!AvatarSystem.Supply(sim, me, d => d == add).IsNull)
                continue;
            var store = w.Query<Thing>().Where(e => w.Get<Thing>(e) is { Spawned: true } t && t.Def.Building?.StorageStacks > 0
                    && sim.StoredIn(e).Any(i => w.Get<Thing>(i).Def == add))
                .OrderBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, w.Get<Thing>(bar).Cell)).FirstOrDefault(Entity.Null);
            if (store.IsNull)
                return Pour(sim, me, view, drinker, now);
            Doing = $"fetching {add.Label}";
            if (!w.Get<Carrying>(me).Item.IsNull && w.Get<Thing>(w.Get<Carrying>(me).Item).Def.Name != into)
            {
                send(new StowCommand(me));
                return true;
            }
            return AtStation(sim, me, store, now, s =>
            {
                if (view.InContainer(s).FirstOrDefault(i => w.Get<Thing>(i).Def == add, Entity.Null) is { IsNull: false } stack)
                    send(new TakeFromStorageCommand(me, stack));
            });
        }
        if (!held.IsNull && w.Get<Thing>(held).Def.Name != into)
        {
            send(new StowCommand(me));
            return true;
        }
        Doing = $"mixing a {named.Label}";
        return AtStation(sim, me, bar, now, b => send(new MixCommand(me, b, named, null)));
    }

    private bool Pour(Simulation sim, Entity me, PerceptionView view, Entity drinker, long now)
    {
        var w = sim.World;
        var held = w.Get<Carrying>(me).Item;
        if (w.IsAlive(held) && w.Get<Thing>(held).Def.Drink)
        {
            if (Reach(sim, me, drinker, now))
                send(new GiveCommand(me, drinker, held, 1));
            return true;
        }
        var cask = ServeJob.Cask(sim, me);
        if (cask.IsNull)
            return Restock(sim, me, view, now);
        var into = w.Get<Thing>(cask).Def.Building!.Cask!.Into;
        if (Carried(sim, me, into).IsNull)
            return Crockery(sim, me, view, now, into, cask);
        if (!held.IsNull && w.Get<Thing>(held).Def.Name != into)
        {
            send(new StowCommand(me)); // a free hand to pour with
            return true;
        }
        // Walk up to the cask and use it: that starts the pour.
        if (now - _lastWalk >= ThinkTicks * 4)
        {
            send(new AvatarGoToCommand(me, w.Get<Thing>(cask).Cell, Interact: true));
            _lastWalk = now;
        }
        return true;
    }

    /// <summary>A room: walk up to the guest, and the order system shows them up to a bed.</summary>
    private bool ShowUp(Simulation sim, Entity me, Entity guest, long now)
    {
        Doing = "letting a room";
        Reach(sim, me, guest, now);
        return true;
    }

    /// <summary>
    /// A meal from the pot: ladle a bowl and hand it over; with the pot not ready, cook it (fill it
    /// from the larder, tend it till it's done, season it by taste) first.
    /// </summary>
    private bool Serve(Simulation sim, Entity me, PerceptionView view, Entity diner, long now)
    {
        var w = sim.World;
        var held = w.Get<Carrying>(me).Item;
        // Ordered at the hatch: put it up there (a dish waiting already, and it's the diner's to take).
        var shop = MyShop(sim, me) ?? Entity.Null;
        var atHatch = view.Orders().Any(o => o.Status == OrderStatus.Open && o.Poster == diner && o.Kind == ServiceKind.Meal)
            && sim.World.TryGet<PresetBuilding>(shop, out var building) && building.Def.HatchServes.Contains(ServiceKind.Meal);
        var hatch = atHatch ? Hatch.Of(sim, shop) : Entity.Null;
        // As many up as it knows are waiting for one: no more till one's taken.
        if (!hatch.IsNull && view.InContainer(hatch).Where(d => w.Get<Thing>(d).Def.Cooked).Sum(d => w.Get<Stack>(d).Count)
            >= view.Orders().Count(o => o.Status == OrderStatus.Open && o.Kind == ServiceKind.Meal))
        {
            Doing = "waiting on the hatch";
            return true;
        }
        if (w.IsAlive(held) && w.Get<Thing>(held).Def.Cooked)
        {
            if (!hatch.IsNull)
            {
                Doing = "putting a dish up on the hatch";
                return AtStation(sim, me, hatch, now, at =>
                {
                    send(new AvatarWorkCommand(me, true, w.Get<Thing>(at).Cell));
                    send(new AvatarWorkCommand(me, false));
                });
            }
            if (Reach(sim, me, diner, now))
                send(new GiveCommand(me, diner, held, 1));
            return true;
        }
        var pot = Nearest(sim, me, "StewPot");
        if (pot.IsNull)
            return false;
        if (view.Pot(pot) is { } seen)
            _pot = seen;
        if (_pot is not { } p || p.Pot != pot)
            return AtStation(sim, me, pot, now, _ => { });
        if (AvatarSystem.Supply(sim, me, Cooking.IsIngredient) is { IsNull: false } food && (p.Empty || !p.Cooked))
        {
            Doing = "filling the pot";
            _potSeasoned = false;
            return AtStation(sim, me, pot, now, at => send(new PotCommand(me, at, PotStep.Add)));
        }
        if (p.Empty || (p.Bowls == 0 && p.Cooked))
        {
            Doing = "fetching food for the pot";
            _potSeasoned = false;
            return AtStation(sim, me, Nearest(sim, me, "Larder"), now, larder =>
            {
                if (view.InContainer(larder).FirstOrDefault(e => Cooking.IsIngredient(w.Get<Thing>(e).Def), Entity.Null) is { IsNull: false } stack)
                    send(new TakeFromStorageCommand(me, stack, 8)); // an armful for the pot
            });
        }
        if (!p.Cooked)
        {
            Doing = "cooking a stew";
            return AtStation(sim, me, pot, now, at => send(new PotCommand(me, at, PotStep.Tend)));
        }
        if (!_potSeasoned)
        {
            Doing = "seasoning the stew";
            return AtStation(sim, me, pot, now, at => send(new PotCommand(me, at, PotStep.Season)));
        }
        var bowl = Cooking.BowlOf(sim, pot);
        if (Carried(sim, me, bowl).IsNull)
            return Crockery(sim, me, view, now, bowl, pot);
        if (!held.IsNull && w.Get<Thing>(held).Def.Name != bowl)
        {
            send(new StowCommand(me)); // a free hand for the ladle
            return true;
        }
        Doing = "ladling a bowl";
        return AtStation(sim, me, pot, now, at => send(new PotCommand(me, at, PotStep.Ladle)));
    }

    /// <summary>
    /// A clean vessel of a kind to pour or ladle into: from the shelf nearest where it's used, and
    /// if there are none clean, gather up the dirty ones and wash them.
    /// </summary>
    private bool Crockery(Simulation sim, Entity me, PerceptionView view, long now, string clean, Entity usedAt)
    {
        var w = sim.World;
        var dirtyDef = sim.Defs.AllThings.FirstOrDefault(d => d.WashesInto == clean)?.Name;
        var dirtyHeld = BarWork.DirtyOn(sim, me);
        if (!dirtyHeld.IsNull)
        {
            Doing = "washing up";
            return AtStation(sim, me, Nearest(sim, me, "WashTub"), now, tub => send(new BarWorkCommand(me, tub)));
        }
        var at = w.Get<Thing>(usedAt).Cell;
        var shelf = w.Query<Thing>().Where(e => w.Get<Thing>(e) is { Spawned: true, Def.Name: "Shelf" })
            .OrderBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, at)).FirstOrDefault(Entity.Null);
        if (!shelf.IsNull && sim.StoredIn(shelf).Any(e => w.Get<Thing>(e).Def.Name == clean))
        {
            Doing = $"fetching a clean {sim.Defs.Thing(clean).Label}";
            if (!w.Get<Carrying>(me).Item.IsNull && w.Get<Thing>(w.Get<Carrying>(me).Item).Def.Name != clean)
            {
                send(new StowCommand(me));
                return true;
            }
            return AtStation(sim, me, shelf, now, s =>
            {
                if (view.InContainer(s).FirstOrDefault(e => w.Get<Thing>(e).Def.Name == clean, Entity.Null) is { IsNull: false } stack)
                    send(new TakeFromStorageCommand(me, stack, ServeJob.VesselsAtOnce));
            });
        }
        // None clean on the shelf: pick up the dirty ones lying about, then to the tub.
        if (dirtyDef == null)
            return false;
        var dirty = w.Query<Thing>().Where(e => w.Get<Thing>(e) is { Spawned: true } t && t.Def.Name == dirtyDef && view.CanSeeCell(t.Cell))
            .OrderBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, w.Get<Position>(me).Cell)).FirstOrDefault(Entity.Null);
        if (dirty.IsNull)
            return false;
        Doing = "clearing up the dirty crockery";
        if (now - _lastWalk >= ThinkTicks * 4)
        {
            send(new AvatarGoToCommand(me, w.Get<Thing>(dirty).Cell, Interact: true));
            _lastWalk = now;
        }
        return true;
    }

    /// <summary>The cask's dry: fetch a keg from the storeroom and knock it in.</summary>
    private bool Restock(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        var cask = Nearest(sim, me, "AleCask");
        if (cask.IsNull)
            return false;
        Doing = "changing the keg";
        if (!BarWork.KegFor(sim, me, cask).IsNull)
            return AtStation(sim, me, cask, now, c => send(new BarWorkCommand(me, c)));
        if (!w.Get<Carrying>(me).Item.IsNull)
        {
            send(new StowCommand(me));
            return true;
        }
        var keg = w.Get<Thing>(cask).Def.Building!.Cask!.Keg;
        var store = w.Query<Thing>().Where(e => w.Get<Thing>(e) is { Spawned: true } t && t.Def.Building?.StorageStacks > 0
                && sim.StoredIn(e).Any(i => w.Get<Thing>(i).Def.Name == keg))
            .OrderBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, w.Get<Position>(me).Cell)).FirstOrDefault(Entity.Null);
        if (store.IsNull)
            return false;
        return AtStation(sim, me, store, now, s =>
        {
            if (view.InContainer(s).FirstOrDefault(e => w.Get<Thing>(e).Def.Name == keg, Entity.Null) is { IsNull: false } k)
                send(new TakeFromStorageCommand(me, k));
        });
    }

    private bool Heal(Simulation sim, Entity me, PerceptionView view, Entity patient, long now)
    {
        if (!Reach(sim, me, patient, now))
            return true;
        if (view.FeltWounds(patient) is not { } wounds)
            return false;
        if (wounds.Count == 0)
        {
            // Hands over them first, to find what hurts; once a while is enough.
            if (_feltOver.TryGetValue(patient, out var felt) && now - felt < SimClock.TicksPerHour)
                return false;
            _feltOver[patient] = now;
            send(new FeelCommand(me, patient));
            return true;
        }
        send(new LayOnHandsCommand(me, patient, wounds[0].Key));
        return true;
    }

    /// <summary>It knows the mending this wound's feel wants, and has the mana for it.</summary>
    private static bool CanWeave(Simulation sim, Entity me, Healing.FeltWound wound, Mana mana) =>
        sim.Defs.AllMendings.FirstOrDefault(m => m.Feels.ContainsValue(wound.Feels) && Weaving.Knows(sim, me, m)) is { } m && mana.Current >= m.ManaCost;

    /// <summary>The healer's own upkeep: mend what they carry, bind it if they can't, or meditate when short of mana. True if it did any.</summary>
    private bool Mend(Simulation sim, Entity me, PerceptionView view)
    {
        if (!sim.World.TryGet<Mana>(me, out var mana))
            return false;
        foreach (var wound in view.FeltWounds(me) ?? [])
        {
            // The feel of a wound tells a practised empath which mending it wants, and the book its runes.
            var mending = sim.Defs.AllMendings.FirstOrDefault(m => m.Feels.ContainsValue(wound.Feels) && Weaving.Knows(sim, me, m));
            if (wound.Absorbed && mending != null && mana.Current >= mending.ManaCost)
            {
                _weave = mending.RuneDefs.Select(r => r.Index).ToList();
                send(new WeaveCommand(me, wound.Key));
                return true;
            }
        }
        // Short of mana with a drawn wound bleeding: bind it while the mana comes back.
        if (Bandaging.WoundsToBind(sim, me) > 0 && Bandaging.CantBind(sim, me, me) == null
            && (view.FeltWounds(me) ?? []).Any(f => f.Absorbed) && !(view.FeltWounds(me) ?? []).Any(f => f.Absorbed && CanWeave(sim, me, f, mana)))
        {
            send(new BandageCommand(me, me));
            return true;
        }
        if (mana.Current < mana.Max * 0.3f && _task == 0)
        {
            // At the shrine's stone if there is one: it's calmer there.
            var stone = Nearest(sim, me, "MeditationStone");
            if (!stone.IsNull && !AvatarSystem.InReach(sim, sim.World.Get<Position>(me).Cell, stone))
                Walk(sim, me, sim.World.Get<Thing>(stone).Cell, sim.Clock.Tick);
            else
                send(new MeditateCommand(me));
            return true;
        }
        return false;
    }

    private bool Broker(Simulation sim, Entity me, PerceptionView view, Entity member, long now)
    {
        if (QuestFor(sim, view, member) is not { } quest)
            return false;
        if (!Reach(sim, me, member, now))
            return true;
        _offered.Add((sim.World.Get<Adventurer>(member).Party, quest));
        send(new OfferQuestCommand(me, member, quest));
        return false;
    }

    /// <summary>The open quest it believes best fits how strong the party looks, not already offered them.</summary>
    private int? QuestFor(Simulation sim, PerceptionView view, Entity member)
    {
        if (view.PartyLooks(member) is not { } looks || !sim.World.TryGet<Adventurer>(member, out var a))
            return null;
        int? best = null;
        var bestOff = float.MaxValue;
        foreach (var q in view.Quests())
        {
            if (q.Status != QuestStatus.Open || _offered.Contains((a.Party, q.Id))
                || q.Danger < ExpeditionSystem.TooEasy * looks * 1.1f || q.Danger > ExpeditionSystem.TooHard * looks * 0.9f)
                continue;
            var off = MathF.Abs(q.Danger - 0.85f * looks);
            if (off < bestOff)
            {
                best = q.Id;
                bestOff = off;
            }
        }
        return best;
    }

    private static readonly int3 Entity3None = new(int.MinValue, int.MinValue, int.MinValue);

    private void Walk(Simulation sim, Entity me, int3 cell, long now)
    {
        if (now - _lastWalk < ThinkTicks * 2)
            return;
        // Beside it, where it can be reached from (not just near it, which may be behind a wall).
        var spot = sim.SpiralFrom(cell, 1).FirstOrDefault(c => c != cell && sim.Map.Passable(c) && Ai.Pathfinder.Touches(c, cell), Entity3None);
        if (spot == Entity3None)
            spot = sim.SpiralFrom(cell, 2).FirstOrDefault(c => c != cell && sim.Map.Passable(c), cell);
        send(new AvatarGoToCommand(me, spot));
        _lastWalk = now;
    }

    /// <summary>The nearest standing thing of a kind (the town's furniture is common knowledge).</summary>
    private static Entity Nearest(Simulation sim, Entity me, string def)
    {
        var from = sim.World.Get<Position>(me).Cell;
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        foreach (var e in sim.World.Each<Thing>())
        {
            var t = sim.World.Get<Thing>(e);
            if (!t.Spawned || t.Def.Name != def)
                continue;
            var d = Maps.Map.Distance(from, t.Cell);
            if (d < bestDist)
            {
                best = e;
                bestDist = d;
            }
        }
        return best;
    }

    // ---------------- Going to work, and small talk ----------------

    private void GoToWork(Simulation sim, Entity me)
    {
        if (_sentToWork)
            return;
        _sentToWork = true;
        if (Workplace(sim, me) is { } spot)
            send(new AvatarGoToCommand(me, spot));
    }

    private void Chat(PerceptionView view, Entity me, long now)
    {
        if (now < _nextTalk)
            return;
        if (Reply(view, me) is { } reply || Greeting(view, me) is { } greeting && (reply = greeting) != null)
        {
            send(new SayCommand(me, reply, Volume.Say));
            _nextTalk = now + TalkCooldownTicks;
        }
    }

    private string? Greeting(PerceptionView view, Entity me)
    {
        foreach (var e in view.Creatures())
            if (e != me && view.Describe(e) is { } seen && _greeted.Add(e))
                return $"Morning, {seen.Name}.";
        return null;
    }

    private string? Reply(PerceptionView view, Entity me)
    {
        string? answer = null;
        foreach (var line in view.Heard())
        {
            if (line.Id <= _heardUpTo)
                continue;
            _heardUpTo = line.Id;
            if (line.Speaker is { } who && who != me && line.Clarity == Clarity.Clear && _rand.Chance(0.5f))
                answer = $"Aye, {line.Who}.";
        }
        return answer;
    }
}
