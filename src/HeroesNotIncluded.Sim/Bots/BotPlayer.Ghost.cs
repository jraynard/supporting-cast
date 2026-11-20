using HeroesNotIncluded.Sim.Afterlife;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Bots;

/// <summary>
/// A dead bot, as a dead player spends a round: it answers a calling back first; with raiders in
/// town it shakes them with a spook, or gets into a beast near and sets it on them; at night it
/// whispers into a sleeping player's dream (to call it back, or a warning); otherwise it lingers
/// over its unfinished business. Only through the ghost's commands, only what a ghost perceives.
/// </summary>
public sealed partial class BotPlayer
{
    /// <summary>How far off (cells) a ghost takes raiders as its business.</summary>
    public const int HauntRange = 24;

    /// <summary>Presence a ghost keeps back from a beast or a dream, so it can still spook.</summary>
    public const float PresenceSpare = 0.2f;

    // Where it's drifting (sent every tick: a ghost moves a step a command), and when it next decides.
    private float3? _ghostGoal;
    private long _nextHaunt;
    private readonly HashSet<Entity> _whispered = [];
    private int _whisperDay = -1;

    /// <summary>The ghost's turn, every tick: it decides every <see cref="ThinkTicks"/>, and drifts toward its goal between.</summary>
    private void Haunt(Simulation sim, Entity ghost)
    {
        var now = sim.Clock.Tick;
        if (now >= _nextHaunt)
        {
            _nextHaunt = now + ThinkTicks;
            _ghostGoal = null;
            Decide(sim, ghost, now);
        }
        if (_ghostGoal is { } goal && Possession.Ridden(sim, ghost).IsNull)
            Send(new GhostMoveCommand(ghost, goal.X, goal.Y, (int)goal.Z));
    }

    private void Decide(Simulation sim, Entity ghost, long now)
    {
        var w = sim.World;
        var view = Perception.For(sim, ghost);
        var body = view.MyBody();
        if (body?.CallingFor is not null)
        {
            Doing = "answering the calling";
            Send(body.CanAnswer ? new AnswerCommand(ghost) : new GhostMoveCommand(ghost, 0, 0, 0, ToBody: true));
            return;
        }
        var g = w.Get<Ghost>(ghost);
        var here = w.Get<Position>(ghost).Pos;
        var raider = Raider(sim, view, ghost);
        if (Possession.Ridden(sim, ghost) is { IsNull: false } beast)
        {
            if (raider.IsNull || g.Presence < 0.05f)
            {
                Doing = "letting the beast go";
                Send(new ReleaseCommand(ghost));
            }
            else
                Ride(sim, ghost, beast, raider, now);
            return;
        }
        if (!raider.IsNull)
        {
            var at = w.Get<Position>(raider).Pos;
            // A beast close by: into it, and at them (raiders; a brawler only wants a fright).
            if (Factions.Attacker(w.Get<Creature>(raider).Faction) && g.Presence >= Possession.Cost + PresenceSpare && Possession.Nearest(sim, ghost) is { IsNull: false } ride)
            {
                Doing = $"getting into the {w.Get<Creature>(ride).Def.Label}";
                Send(new PossessCommand(ghost, ride));
                return;
            }
            Doing = $"haunting {w.Get<Creature>(raider).Name}";
            if (Flat(here, at) <= Hauntings.UnnerveReach - 2 && (int)here.Z == (int)at.Z)
            {
                if (Hauntings.Problem(sim, ghost, HauntKind.Spook) == null && !Hauntings.IsUnnerved(sim, raider))
                    Send(new HauntCommand(ghost, HauntKind.Spook));
            }
            else
                _ghostGoal = at;
            return;
        }
        if (Whisper(sim, ghost, view, body, g, now))
            return;
        // Nothing doing: see to some unfinished business, if any's in sight.
        if (view.Business().FirstOrDefault() is { Errand: not null } b && b.Progress is null)
        {
            Doing = $"lingering: {b.Errand.Label}";
            _ghostGoal = new float3(b.Cell.X + 0.5f, b.Cell.Y + 0.5f, b.Cell.Z);
        }
    }

    public static float Flat(float3 a, float3 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>The nearest raider (or predator, or brawler in the inn's brawl) on its feet in sight; never the dragon, which signs don't shake.</summary>
    private static Entity Raider(Simulation sim, PerceptionView view, Entity ghost)
    {
        var w = sim.World;
        var here = w.Get<Position>(ghost).Pos;
        var best = Entity.Null;
        var bestD = float.MaxValue;
        foreach (var e in view.Creatures())
            if (w.Get<Creature>(e) is { Dead: false, Downed: false } c && (Factions.Attacker(c.Faction) || w.Has<Hospitality.Brawler>(e)) && c.Def.Race != Race.Dragon
                && !w.Has<Combat.Leaving>(e) && Flat(here, w.Get<Position>(e).Pos) is var d && d <= HauntRange && d < bestD)
                (best, bestD) = (e, d);
        return best;
    }

    /// <summary>In a beast: at the raider, and a bite when it's close.</summary>
    private void Ride(Simulation sim, Entity ghost, Entity beast, Entity raider, long now)
    {
        var w = sim.World;
        var from = w.Get<Position>(beast).Pos;
        var to = w.Get<Position>(raider).Pos;
        float dx = to.X - from.X, dy = to.Y - from.Y;
        var d = MathF.Sqrt(dx * dx + dy * dy);
        Doing = $"setting the {w.Get<Creature>(beast).Def.Label} on {w.Get<Creature>(raider).Name}";
        if (d <= 1.5f)
        {
            Send(new RideMoveCommand(ghost, 0f, 0f));
            Send(new RideAttackCommand(ghost, false, DetMath.Atan2(dy, dx), Pressed: now));
        }
        else
            Send(new RideMoveCommand(ghost, dx / d, dy / d));
    }

    /// <summary>What a bot whispers into a dream (letters and spaces only: the dead can't say much).</summary>
    private static readonly string[] Warnings = ["watch the north road", "keep the buckets full", "the dead are watching", "mind the dark", "remember me"];

    /// <summary>
    /// At night, a player asleep near: once a night each, a few words into their dream. With its own
    /// body still fresh, it asks to be called back; otherwise a warning.
    /// </summary>
    private bool Whisper(Simulation sim, Entity ghost, PerceptionView view, KnownBody? body, Ghost g, long now)
    {
        var w = sim.World;
        var day = (int)(now / SimClock.TicksPerDay);
        if (day != _whisperDay)
        {
            _whisperDay = day;
            _whispered.Clear();
        }
        if (g.Presence < Dreams.Cost + PresenceSpare)
            return false;
        var here = w.Get<Position>(ghost).Pos;
        var sleeper = Entity.Null;
        var bestD = float.MaxValue;
        foreach (var e in view.Creatures())
            if (e != g.Body && w.Has<PlayerCharacter>(e) && !_whispered.Contains(e) && w.Get<Creature>(e).Able && w.Get<Mind>(e).Asleep
                && Flat(here, w.Get<Position>(e).Pos) is var d && d < bestD)
                (sleeper, bestD) = (e, d);
        if (sleeper.IsNull)
            return false;
        var name = w.Get<Creature>(sleeper).Name;
        Doing = $"whispering to {name}";
        if (bestD > Dreams.Reach - 0.5f)
        {
            _ghostGoal = w.Get<Position>(sleeper).Pos;
            return true;
        }
        var words = body is { Fresh: true } && w.Has<Mana>(sleeper) ? $"call me back {w.Get<Ghost>(ghost).Name}" : Warnings[_rand.NextInt(Warnings.Length)];
        Send(new WhisperCommand(ghost, sleeper, words));
        _whispered.Add(sleeper);
        return true;
    }
}
