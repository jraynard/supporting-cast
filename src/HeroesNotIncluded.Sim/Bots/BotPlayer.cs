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
public sealed partial class BotPlayer(Func<Simulation> world, Guid player, Action<ISimCommand> send, ulong seed)
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

    public Guid Player => player;

    /// <summary>What it last decided, for tests and debugging.</summary>
    public string Doing { get; set; } = "";

    /// <summary>What it's done this round, for the season sweep.</summary>
    public BotStats Stats { get; } = new();

    private long _countedSession = -1;
    private bool _wasGhost;
    private readonly HashSet<int> _counted = [];

    /// <summary>Every command goes out through here, so what it says is counted.</summary>
    public void Send(ISimCommand command)
    {
        if (command is SayCommand)
            Stats.Said++;
        send(command);
    }

    /// <summary>The order the bot is seeing to, if any (for tests).</summary>
    public int Task => _task;

    /// <summary>Call once per frame or tick. Does nothing until the bot has a character.</summary>
    public void Tick()
    {
        var sim = world();
        var me = Players.CharacterOf(sim, player);
        if (me.IsNull && Afterlife.Ghosts.Of(sim, player) is { IsNull: false } ghost)
        {
            Haunt(sim, ghost);
            _wasGhost = true;
            return;
        }
        if (_wasGhost && !me.IsNull)
        {
            // Back from the dead: a word for whoever brought it back, when it can.
            _wasGhost = false;
            _toAnswer = ("revived", "");
        }
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
        _custom.Clear();
        var (press, held, axis) = _work.Session?.StartTick == live ? Play(_work.Session) : (false, false, 0);
        // At the anvil it puts the hammer down as the colour fades, and takes the piece back to the fire.
        // A slip: it keeps striking after the colour's gone, on metal too cold to take it.
        if (_work.Session is HammerSession { Workable: var heat } && heat < 0.95f && !_stopped)
        {
            if (_slip)
                Slipped();
            if (!_slip || heat < 0.45f)
            {
                _stopped = true;
                Send(new StopWorkCommand(me));
            }
        }
        if (_work.Session is SeasonSession && _seasoned && !_stopped)
        {
            _stopped = true;
            Send(new StopWorkCommand(me));
        }
        _work.Update(sim, me, steps, press, held, axis, Send, _axis2, _rune, _act, custom: _custom.Count > 0 ? _custom : null);
        if (_work.Session is { } started && started.StartTick != _countedSession)
        {
            _countedSession = started.StartTick;
            BotStats.Count(Stats.Sessions, started.GetType().Name);
        }
        if (_work.Session == null && !sim.World.Has<WorkSession>(me) && Throwing(sim, me, now))
            return; // a throw's timed to the tick, as a fight's dodge is
        if (_work.Session == null && !sim.World.Has<WorkSession>(me) && Fight(sim, me, _view, now))
            return; // a fight's every tick: a dodge can't wait for the next think
        if (_work.Session != null || sim.World.Has<WorkSession>(me) || now < _nextThink)
            return;
        _nextThink = now + ThinkTicks;
        Think(sim, me, _view, now);
    }

    // ---------------- Deciding ----------------

    private void Think(Simulation sim, Entity me, PerceptionView view, long now)
    {
        Shout(sim, me, view, now);
        if (Douse(sim, me, view, now) || Safety(sim, me, view, now) || BindOwn(sim, me) || PitchIn(sim, me, view, now)
            || SeeTheEmpath(sim, me, now) || Bedtime(sim, me, now))
            return;
        var festival = sim.Round.Phase == Round.RoundPhase.Festival;
        if (!festival)
            GoToWork(sim, me);
        Price(sim, me, view, now);
        if (Mend(sim, me, view) || CarryHamper(sim, me, now))
            return;
        if (_task != 0 && (now - _taskSince > TaskTimeoutTicks * (1.25f - 0.75f * Temper.Haste) || !Work(sim, me, view, now)))
            _task = 0;
        if (_task == 0 && !festival && PutKeyBack(sim, me, now))
            return;
        if (_task == 0)
            TakeOn(sim, me, view, now);
        // A meal's a bonus (WellBeing): only with nothing better to do.
        if (_task == 0 && Supper(sim, me, now))
            return;
        if (_task == 0 && festival)
        {
            Festival(sim, me, now);
            return;
        }
        if (_task == 0 && MendTown(sim, me, view, now))
            return;
        // What its trade keeps in (the smith's charcoal): made before the rest once it's running out, but never with a
        // piece in the tongs; whenever it's short, with nothing else to do.
        if (_task == 0 && view.Piece(me) == null && KeepIn(sim, me, view, now, urgent: true))
            return;
        if (_task == 0 && Smith(sim, me, view, now))
            return;
        if (_task == 0 && view.Piece(me) == null && KeepIn(sim, me, view, now, urgent: false))
            return;
        // Each trade's work between orders, in its own file.
        if (_task == 0 && SendForHamper(sim, me, now))
            return;
        if (_task == 0 && (Kitchen(sim, me, view, now) || Bench(sim, me, view, now) || TradesBetween(sim, me, view, now) || Shrine(sim, me, view, now) || Labour(sim, me, view, now)))
            return;
        // The hasty let the chores go half the time (an hour on, an hour off).
        if (_task == 0 && (Temper.Haste < 0.65f || now / SimClock.TicksPerHour % 2 == 0) && Upkeep(sim, me, view, now))
            return;
        if (_task == 0)
            Chat(sim, view, me, now);
    }

    /// <summary>
    /// The order kinds this character's trade answers: the services served at its workplace that
    /// it sees to (the def's <c>serves</c>), and quests if it brokers them. Read from the defs, so a
    /// new service needs only a handler in <see cref="Work"/>.
    /// </summary>
    private static ServiceKind[] KindsFor(Simulation sim, TradeDef trade) =>
        [.. sim.Defs.AllServices
            .Where(s => (trade.HasWorkplace && s.AtRole == trade.Role && trade.Serving(s.Kind)) || (s.Kind == ServiceKind.Quest && trade.Brokers)
                || (s.Kind == ServiceKind.Goods && trade.HasWorkplace) // gear on show: the seller hands it over
                || (s.Kind == ServiceKind.Meal && trade.Role == TownRole.Tavern && Hospitality.Provisions.Split(sim))) // a lodger's board, where the inn has no kitchen
            .Select(s => s.Kind)
            .Distinct()];

    /// <summary>Take on the oldest open order of the trade's kind it knows of.</summary>
    private void TakeOn(Simulation sim, Entity me, PerceptionView view, long now)
    {
        if (!sim.World.TryGet<Trade>(me, out var trade) || KindsFor(sim, trade.Def) is not { Length: > 0 } kinds)
            return;
        foreach (var e in view.Orders().OrderBy(e => FromCounter(sim, me, e.Poster)).ThenBy(e => e.PostedTick))
        {
            if (e.Status != OrderStatus.Open || e.Mine || !kinds.Contains(e.Kind) || (e.AssigneeName.Length > 0 && !e.MineToDo))
                continue;
            if (e.Kind == ServiceKind.Quest && !QuestFor(sim, view, e.Poster).HasValue)
                continue;
            if (e.Kind == ServiceKind.Goods && GoodsFor(sim, me, e).IsNull)
                continue; // someone else's piece
            // The innkeeper serves only a lodger's board (said at their own bar); the cook, a hamper not yet up.
            if (e.Kind == ServiceKind.Meal && trade.Def.Role == TownRole.Tavern && !(sim.Shops.Find(e.Id) is { } meal && Hospitality.Provisions.IsBoard(sim, meal)))
                continue;
            if (e.Kind == ServiceKind.Provisions && (Hospitality.Provisions.HamperUp(sim) || !HamperInReach(sim, me, view)))
                continue;
            if (e.Kind != ServiceKind.Quest && !e.MineToDo)
                Send(new TakeOrderCommand(me, e.Id));
            _task = e.Id;
            if (_counted.Add(e.Id))
                BotStats.Count(Stats.Taken, e.Kind.ToString());
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
        foreach (var trade in TradeBots) // a trade's own orders (the woodshop's arrows and mends)
            if (trade.Order(sim, me, view, order.Kind, poster, now) is { } handled)
                return handled;
        return order.Kind switch
        {
            ServiceKind.Repair => Repair(sim, me, view, poster, now),
            ServiceKind.CrossbowRepair => BenchMend(sim, me, view, poster, now, Tinkering.Tinkers.BenchDef),
            ServiceKind.Drink => Pour(sim, me, view, poster, now),
            ServiceKind.MixedDrink => view.WantedDrink(_task) is { Mix: not null } named ? MixFor(sim, me, view, poster, named, now) : Pour(sim, me, view, poster, now),
            ServiceKind.Meal when sim.Shops.Find(_task) is { } board && Hospitality.Provisions.IsBoard(sim, board) => Board(sim, me, view, poster, now),
            ServiceKind.Meal => Serve(sim, me, view, poster, now),
            ServiceKind.Provisions => PackHamper(sim, me, view, now),
            ServiceKind.Lodging => ShowUp(sim, me, view, poster, now),
            ServiceKind.Healing => Heal(sim, me, view, poster, now),
            ServiceKind.Quest => Broker(sim, me, view, poster, now),
            ServiceKind.Unlock => OpenBox(sim, me, view, poster, now),
            ServiceKind.WagonRepair => MendWagon(sim, me, view, poster, now),
            ServiceKind.Appraise => Appraise(sim, me, view, poster, now),
            ServiceKind.Goods => Sell(sim, me, view, poster, now),
            _ => false,
        };
    }

    /// <summary>The shop this character keeps, if their trade works from one.</summary>
    public static Entity? MyShop(Simulation sim, Entity me) =>
        sim.World.TryGet<Trade>(me, out var trade) && trade.Def.HasWorkplace ? ShopSystem.WorkplaceOf(sim, trade.Def) : null;

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
    public bool Reach(Simulation sim, Entity me, Entity who, long now, bool returning = false)
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
                Send(new AvatarGoToCommand(me, work));
            return false;
        }
        Send(new AvatarGoToCommand(me, at.Cell));
        return false;
    }

    /// <summary>A standable cell by this character's trade building, if it has one.</summary>
    private static int3? Workplace(Simulation sim, Entity me)
    {
        if (!sim.World.TryGet<Trade>(me, out var trade) || !trade.Def.HasWorkplace)
            return null;
        foreach (var e in sim.World.Each<PresetBuilding>()) // the town's layout is common knowledge
        {
            var building = sim.World.Get<PresetBuilding>(e);
            if (trade.Def.WorksAt(building.Def))
                return sim.SpiralFrom(building.Anchor, 8).FirstOrDefault(c => sim.Map.Passable(c), building.Anchor);
        }
        return null;
    }

    /// <summary>A clean vessel of a kind it's actually carrying: what's on a shelf it happens to stand by won't come with it to the next station.</summary>
    private static Entity Carried(Simulation sim, Entity me, string vessel) => AvatarSystem.Supply(sim, me, d => d.Name == vessel);

    /// <summary>Walk to a station, then use it. False if there's none.</summary>
    public bool AtStation(Simulation sim, Entity me, Entity station, long now, Action<Entity> use)
    {
        if (station.IsNull)
            return false;
        var here = sim.World.Get<Position>(me).Cell;
        var at = sim.World.Get<Thing>(station).Cell;
        if (AvatarSystem.InReach(sim, here, station))
        {
            use(station);
            return true;
        }
        // Behind a locked door it has the key to (the storeroom, a room upstairs): unlock it first.
        if (!sim.Regions.CanReach(here, at, PathEnd.Touch)
            && Tinkering.Locksmithing.KeyedDoor(sim, me, here, at, PathEnd.Touch, player: true) is { IsNull: false } door)
        {
            Doing = $"unlocking the {sim.World.Get<Thing>(door).Label}";
            if (AvatarSystem.InReach(sim, here, door))
                Send(new Tinkering.TurnKeyCommand(me, door, false));
            else
                Walk(sim, me, sim.World.Get<Thing>(door).Cell, now);
            return true;
        }
        Walk(sim, me, at, now);
        return true;
    }

    /// <summary>Units of an item in its hands and pack.</summary>
    public static int OnMe(Simulation sim, Entity me, string def)
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

    /// <summary>The piece someone handed this character to mend.</summary>
    public static Entity Entrusted(Simulation sim, Entity me, Entity owner)
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

    private static readonly int3 Entity3None = new(int.MinValue, int.MinValue, int.MinValue);

    public void Walk(Simulation sim, Entity me, int3 cell, long now)
    {
        if (now - _lastWalk < ThinkTicks * 2)
            return;
        // Beside it, where it can be reached from (not just near it, which may be behind a wall).
        var spot = sim.SpiralFrom(cell, 1).FirstOrDefault(c => c != cell && sim.Map.Passable(c) && Ai.Pathfinder.Touches(c, cell), Entity3None);
        if (spot == Entity3None)
            spot = sim.SpiralFrom(cell, 2).FirstOrDefault(c => c != cell && sim.Map.Passable(c), cell);
        Send(new AvatarGoToCommand(me, spot));
        _lastWalk = now;
    }

    /// <summary>The nearest standing thing of a kind (the town's furniture is common knowledge).</summary>
    public static Entity Nearest(Simulation sim, Entity me, string def)
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

    private void GoToWork(Simulation sim, Entity me)
    {
        if (_sentToWork)
            return;
        _sentToWork = true;
        if (Workplace(sim, me) is { } spot)
            Send(new AvatarGoToCommand(me, spot));
    }
}
