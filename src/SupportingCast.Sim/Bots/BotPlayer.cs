using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Speech;
using SupportingCast.Sim.Systems;
using SupportingCast.Sim.Work;

namespace SupportingCast.Sim.Bots;

/// <summary>
/// A stand-in player, for scenario tests and solo playtests. It plays only through commands and
/// knows only what <see cref="PerceptionView"/> tells it, exactly as a person at the keyboard
/// would, so a test driven by bots proves the game is playable from the player's side of the line.
/// </summary>
/// <param name="world">The current sim: a resync replaces it.</param>
public sealed class BotPlayer(Func<Simulation> world, Guid player, Action<ISimCommand> send, ulong seed)
{
    /// <summary>Ticks between decisions. Mini-games are played every tick regardless.</summary>
    public const int ThinkTicks = 30;

    /// <summary>The fewest ticks between things it says, so two bots don't talk forever.</summary>
    public const int TalkCooldownTicks = SimClock.TicksPerSecond * 20;

    private readonly SimRandom _rand = new(seed);
    private readonly WorkPredictor _work = new();
    private readonly HashSet<Entity> _greeted = [];
    private PerceptionView? _view;
    private long _lastTick = -1;
    private long _nextThink;
    private long _nextTalk;
    private long _heardUpTo;
    private bool _sentToWork;

    public Guid Player => player;

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

        _work.Update(sim, me, steps, ShouldStrike(), false, 0, send);
        if (_work.Session != null || now < _nextThink)
            return;
        _nextThink = now + ThinkTicks;
        Think(sim, me, _view, now);
    }

    private bool ShouldStrike() =>
        _work.Session is HammerSession { Ready: true } hammer && MathF.Abs(hammer.Marker - hammer.Mark) <= hammer.HalfWidth * 0.5f;

    private void Think(Simulation sim, Entity me, PerceptionView view, long now)
    {
        GoToWork(sim, me);
        if (now < _nextTalk)
            return;
        if (Reply(view, me) is { } reply || Greeting(view, me) is { } greeting && (reply = greeting) != null)
        {
            send(new SayCommand(me, reply, Volume.Say));
            _nextTalk = now + TalkCooldownTicks;
        }
    }

    private void GoToWork(Simulation sim, Entity me)
    {
        if (_sentToWork)
            return;
        _sentToWork = true;
        if (!sim.World.TryGet<Trade>(me, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return;
        foreach (var e in sim.World.Each<PresetBuilding>()) // the town's layout is common knowledge
        {
            var building = sim.World.Get<PresetBuilding>(e);
            if (building.Def != workplace)
                continue;
            var spot = sim.SpiralFrom(building.Anchor, 8).FirstOrDefault(c => sim.Map.Passable(c), building.Anchor);
            send(new AvatarGoToCommand(me, spot));
            return;
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
