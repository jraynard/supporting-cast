using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Shops;
using SupportingCast.Sim.Expeditions;
using SupportingCast.Sim.Healing;
using SupportingCast.Sim.Locks;
using SupportingCast.Sim.Speech;
using SupportingCast.Sim.Systems;
using SupportingCast.Sim.Work;

namespace SupportingCast.Sim.Bots;

/// <summary>
/// A stand-in player, for scenario tests and solo playtests. It plays only through commands and
/// knows only what <see cref="PerceptionView"/> tells it, exactly as a person at the keyboard
/// would, so a test driven by bots proves the game is playable from the player's side of the line.
/// It plays its trade: the smith takes repairs and judges hammer or stone by what the blade looks
/// like, the innkeeper pours for orders and matches parties to the quests it believes fit, the
/// healer draws wounds and casts the mending whose feel it knows, the carpenter feels a strongbox
/// over for traps and picks its lock by the feel alone. Every mini-game is played
/// by hand, through the same predictor as the game's.
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
    private bool _sentToWork;

    // The order in hand.
    private int _task;
    private long _taskSince;
    private long _lastWalk;

    // Mini-game state, per session.
    private long _session = -1;
    private bool _resting, _stopped;
    private int _lastRune = -1, _lastTurn = -1;

    // Lockwork: the feel needle smoothed, where it rose past the mark, the middle it found, and
    // how long it's been working a trigger.
    private float _feel;
    private float? _rose, _middle;
    private float _skipTo;
    private int _pin, _slips, _working, _dir = 1;
    private float _progress;

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

        var (press, held, axis) = Play(_work.Session);
        _work.Update(sim, me, steps, press, held, axis, send);
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
            _resting = _stopped = false;
            _lastRune = _lastTurn = -1;
            _feel = 0f;
            _rose = _middle = null;
            _skipTo = 0f;
            _pin = _slips = _working = 0;
            _dir = 1;
            _progress = 0f;
        }
        switch (s)
        {
            case HammerSession h:
                return (h.Ready && MathF.Abs(h.Marker - h.Mark) <= h.HalfWidth * 0.5f, false, 0);
            case SharpenSession sh:
            {
                _resting = _resting ? sh.Heat > 0.3f : sh.Heat > 0.8f;
                return (false, !_resting && sh.InBand, Steer(sh.Angle, sh.HalfWidth));
            }
            case PourSession p:
                // Stop where the settled head will bring it to the line.
                _stopped |= p.Level + p.Foam * 0.73f >= p.Line;
                return (false, !_stopped, 1);
            case DrawSession d:
                return (false, true, Steer(d.Composure, d.HalfWidth));
            case CastSession c:
            {
                if (c.Resolved >= c.Mending.Runes.Length || c.Resolved == _lastRune || Math.Abs(c.StepsDone - c.DueOf(c.Resolved)) > 2)
                    return (false, false, 0);
                _lastRune = c.Resolved;
                var rune = c.Mending.Runes[c.Resolved];
                return (rune == 'F', false, rune == 'L' ? -1 : rune == 'R' ? 1 : 0);
            }
            case MeditateSession m:
            {
                if (m.Flinching || m.NextTurn == _lastTurn || Math.Abs(m.StepsDone - m.NextTurn) > 2)
                    return (false, false, 0);
                _lastTurn = m.NextTurn;
                return (true, false, 0);
            }
            case ProbeSession pr:
                return Probe(pr);
            case PickSession pk:
                return Pick(pk);
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

    /// <summary>Lift each pin through where it gives, back to the middle of that, and set it.</summary>
    private (bool, bool, int) Pick(PickSession s)
    {
        if (s.Pin != _pin || s.Slips != _slips)
        {
            (_pin, _slips) = (s.Pin, s.Slips);
            _rose = _middle = null;
            _feel = 0f;
            _dir = 1;
        }
        _feel = _feel * 0.7f + s.Feel * 0.3f;
        if (_middle is { } target)
        {
            var off = target - s.Lift;
            return MathF.Abs(off) <= s.HalfWidth * 0.3f ? (true, false, 0) : (false, false, Math.Sign(off));
        }
        if (Middle(s.Lift, 0.45f) is { } found)
            _middle = found;
        if (s.Lift >= 0.99f || s.Lift <= 0.01f)
        {
            _dir = s.Lift >= 0.99f ? -1 : 1;
            _rose = null;
        }
        return (false, false, _dir);
    }

    /// <summary>Steer a needle back toward the middle of its band.</summary>
    private static int Steer(float needle, float halfWidth)
    {
        var off = needle - 0.5f;
        return MathF.Abs(off) > halfWidth * 0.25f ? -Math.Sign(off) : 0;
    }

    // ---------------- Deciding ----------------

    private void Think(Simulation sim, Entity me, PerceptionView view, long now)
    {
        GoToWork(sim, me);
        if (Mend(sim, me, view))
            return;
        if (_task != 0 && (now - _taskSince > TaskTimeoutTicks || !Work(sim, me, view, now)))
            _task = 0;
        if (_task == 0)
            TakeOn(sim, me, view, now);
        if (_task == 0)
            Chat(view, me, now);
    }

    /// <summary>The order kinds this character's trade answers.</summary>
    private static ServiceKind[] KindsFor(TradeDef trade) => trade.Name switch
    {
        "Blacksmith" => [ServiceKind.Repair],
        "Innkeeper" => [ServiceKind.Drink, ServiceKind.Quest], // the bar and the quest board both
        "Healer" => [ServiceKind.Healing],
        "Carpenter" => [ServiceKind.Unlock],
        _ => trade.Brokers ? [ServiceKind.Quest] : [],
    };

    /// <summary>Take on the oldest open order of the trade's kind it knows of.</summary>
    private void TakeOn(Simulation sim, Entity me, PerceptionView view, long now)
    {
        if (!sim.World.TryGet<Trade>(me, out var trade) || KindsFor(trade.Def) is not { Length: > 0 } kinds)
            return;
        foreach (var e in view.Orders().OrderBy(e => e.PostedTick))
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
            ServiceKind.Drink => Pour(sim, me, poster, now),
            ServiceKind.Healing => Heal(sim, me, view, poster, now),
            ServiceKind.Quest => Broker(sim, me, view, poster, now),
            ServiceKind.Unlock => OpenBox(sim, me, view, poster, now),
            _ => false,
        };
    }

    /// <summary>Cells away past which it waits at its workplace for someone, rather than chasing them.</summary>
    public const int ChaseCells = 20;

    /// <summary>
    /// Walk toward someone (re-aimed now and then, as they move). Far off, it waits at its
    /// workplace instead: customers come to the building their order is served at. True if
    /// they're already in reach.
    /// </summary>
    private bool Reach(Simulation sim, Entity me, Entity who, long now)
    {
        if (Handing.InReach(sim, me, who))
            return true;
        if (now - _lastWalk < ThinkTicks * 2 || !sim.World.TryGet<Position>(who, out var at))
            return false;
        _lastWalk = now;
        var here = sim.World.Get<Position>(me).Cell;
        if (Maps.Map.Distance(here, at.Cell) > ChaseCells && Workplace(sim, me) is { } work)
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
        if (condition.Contains("chip") || condition.Contains("bent") || condition.Contains("breaking"))
        {
            var forge = Nearest(sim, me, "Forge");
            if (forge.IsNull)
                return false;
            if (!AvatarSystem.InReach(sim, w.Get<Position>(me).Cell, forge))
                Walk(sim, me, w.Get<Thing>(forge).Cell, now);
            else
                send(new RepairCommand(me, forge, blade)); // fires the forge; again once it's hot
            return true;
        }
        if (condition.Contains("dull") || condition.Contains("blunt"))
        {
            send(new SharpenCommand(me, blade));
            return true;
        }
        if (Reach(sim, me, owner, now))
            send(new GiveCommand(me, owner, blade, 1));
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
        if (Reach(sim, me, owner, now))
            send(new GiveCommand(me, owner, box, 1));
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

    private bool Pour(Simulation sim, Entity me, Entity drinker, long now)
    {
        var w = sim.World;
        var held = w.Get<Carrying>(me).Item;
        if (w.IsAlive(held) && w.Get<Thing>(held).Def.Drink)
        {
            if (Reach(sim, me, drinker, now))
                send(new GiveCommand(me, drinker, held, 1));
            return true;
        }
        if (!held.IsNull)
        {
            send(new StowCommand(me)); // a free hand to pour with
            return true;
        }
        var cask = ServeJob.Cask(sim, me);
        if (cask.IsNull)
            return false;
        // Walk up to the cask and use it: that starts the pour.
        if (now - _lastWalk >= ThinkTicks * 4)
        {
            send(new AvatarGoToCommand(me, w.Get<Thing>(cask).Cell, Interact: true));
            _lastWalk = now;
        }
        return true;
    }

    private bool Heal(Simulation sim, Entity me, PerceptionView view, Entity patient, long now)
    {
        if (!Reach(sim, me, patient, now))
            return true;
        if (view.FeltWounds(patient) is not { Count: > 0 } wounds)
            return false;
        send(new LayOnHandsCommand(me, patient, wounds[0].Key));
        return true;
    }

    /// <summary>The healer's own upkeep: mend what they carry, or meditate when short of mana. True if it did either.</summary>
    private bool Mend(Simulation sim, Entity me, PerceptionView view)
    {
        if (!sim.World.TryGet<Mana>(me, out var mana))
            return false;
        foreach (var wound in view.FeltWounds(me) ?? [])
        {
            // The feel of a wound tells a practised empath which mending it wants.
            var mending = sim.Defs.AllMendings.FirstOrDefault(m => m.Feels.ContainsValue(wound.Feels));
            if (wound.Absorbed && mending != null && mana.Current >= mending.ManaCost)
            {
                send(new CastMendingCommand(me, wound.Key, mending));
                return true;
            }
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

    private void Walk(Simulation sim, Entity me, int3 cell, long now)
    {
        if (now - _lastWalk < ThinkTicks * 2)
            return;
        var spot = sim.SpiralFrom(cell, 2).FirstOrDefault(c => c != cell && sim.Map.Passable(c), cell);
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
