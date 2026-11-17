using HeroesNotIncluded.Sim.Afterlife;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Bots;

/// <summary>
/// What a bot says, in its own words (<see cref="Barks"/>, <c>barks.json</c>): it shouts what it
/// sees go wrong (a fire, the dragon, someone down), answers what's said to it, greets new faces,
/// asks the town for what its trade's run out of, and, when it's idle and the talkative sort,
/// passes the time. How much it says is its <see cref="BotTemperament.Chatter"/>. Only what it saw
/// and heard; never anything it couldn't know.
/// </summary>
public sealed partial class BotPlayer
{
    // ---------------- Going to work, and small talk ----------------

    private readonly SimRandom _talkRand = new(seed * 131 + 3);
    private readonly HashSet<string> _alarmed = [];
    private string? _lastSaid;
    private long _nextIdle;

    /// <summary>Something heard that wants an answer when it next has a moment: the bark, and who to (or of).</summary>
    private (string Bark, string Who)? _toAnswer;

    /// <summary>Between things it says: the chatty half as long, the quiet half as long again.</summary>
    private long TalkCooldown => (long)(TalkCooldownTicks * (1.5f - Temper.Chatter));

    /// <summary>Say a line for this event, if the barks have one for it. True if it said something.</summary>
    private bool Say(Simulation sim, Entity me, string bark, Volume volume, long now, string? who = null, string? thing = null, string? place = null)
    {
        if (Barks.Pick(sim.Defs, bark, TagsFor(sim, me), _talkRand, _lastSaid, who, thing, place) is not { } line)
            return false;
        Send(new SayCommand(me, line, volume));
        _lastSaid = line;
        _nextTalk = now + TalkCooldown;
        return true;
    }

    /// <summary>
    /// Every think, before it decides what to do: shout what it's seen go wrong, once for each
    /// thing, busy or not (a player yells "fire!" with a bucket in their hands).
    /// </summary>
    private void Shout(Simulation sim, Entity me, PerceptionView view, long now)
    {
        Listen(sim, me, view);
        var world = sim.World;
        if (!sim.Round.Live || world.Get<Creature>(me).Downed)
            return;
        var here = world.Get<Position>(me).Cell;
        foreach (var e in view.Creatures())
        {
            if (e == me)
                continue;
            var c = world.Get<Creature>(e);
            var d = Maps.Map.Distance(world.Get<Position>(e).Cell, here);
            if (c.Def.Race == Race.Dragon && !c.Dead && Alarm($"dragon:{e.Index}"))
            {
                Say(sim, me, "alarm_dragon", Volume.Shout, now, who: c.Name);
                return;
            }
            if (Factions.Attacker(c.Faction) && c is { Dead: false, Downed: false } && d <= Ai.Defence.VillagerRange && Alarm($"raid:{now / SimClock.TicksPerHour}"))
            {
                Say(sim, me, "alarm_raid", Volume.Shout, now, who: c.Name);
                return;
            }
            if (c.Def.Race != Race.Human || Factions.Attacker(c.Faction) || d > HelpRange)
                continue;
            if (c.Dead && Revival.Fresh(sim, e) && Alarm($"body:{e.Index}"))
            {
                Say(sim, me, "alarm_body", Volume.Shout, now, who: c.Name);
                return;
            }
            if (!c.Dead && c.Downed && !world.Has<Carried>(e) && Alarm($"down:{e.Index}:{now / SimClock.TicksPerDay}"))
            {
                Say(sim, me, "alarm_downed", Volume.Shout, now, who: c.Name);
                return;
            }
        }
        foreach (var i in sim.Fire.BurningTiles)
        {
            var f = sim.Map.CellOf(i);
            if (!view.CanSeeCell(f) || Buildings.At(sim, f) is not { IsNull: false } building || !Alarm($"fire:{building.Index}:{now / SimClock.TicksPerHour}"))
                continue;
            Say(sim, me, "alarm_fire", Volume.Shout, now, place: world.Get<PresetBuilding>(building).Def.Label);
            return;
        }
    }

    /// <summary>Not shouted about yet (and now it has been).</summary>
    private bool Alarm(string key) => _alarmed.Add(key);

    /// <summary>
    /// What's been said since it last listened: its own name spoken, a complaint about a key it
    /// handed over, or now and then just something to answer. Kept to answer when it has a moment.
    /// </summary>
    private void Listen(Simulation sim, Entity me, PerceptionView view)
    {
        var w = sim.World;
        var name = w.Get<Creature>(me).Name;
        var keeper = w.TryGet<Trade>(me, out var trade) && trade.Def.Serving(ServiceKind.Lodging) && trade.Def.HasWorkplace;
        foreach (var line in view.Heard())
        {
            if (line.Id <= _heardUpTo)
                continue;
            _heardUpTo = line.Id;
            if (line.Speaker is not { } who || who == me || line.Clarity != Clarity.Clear)
                continue;
            if (keeper && line.Text.Contains("wrong key", StringComparison.OrdinalIgnoreCase))
                _toAnswer = ("blamed_key", Scapegoat(sim, me, line.Who));
            else if (_toAnswer == null && (line.Text.Contains(name, StringComparison.OrdinalIgnoreCase) || _talkRand.Chance(0.15f + 0.35f * Temper.Chatter)))
                _toAnswer = ("reply", line.Who);
        }
    }

    /// <summary>Who a bot that's slipped would sooner it had been: another player in sight, or else whoever complained.</summary>
    private string Scapegoat(Simulation sim, Entity me, string complainer)
    {
        var w = sim.World;
        var others = w.Query<PlayerCharacter>().Where(e => e != me && w.Get<Creature>(e) is { Dead: false }).ToList();
        return others.Count == 0 || Temper.Care >= 0.6f ? complainer : w.Get<Creature>(others[_talkRand.NextInt(others.Count)]).Name;
    }

    /// <summary>
    /// With nothing else to do: answer what wanted answering, greet a new face, or, the talkative
    /// sort, pass the time with a line about its trade.
    /// </summary>
    private void Chat(Simulation sim, PerceptionView view, Entity me, long now)
    {
        if (now < _nextTalk)
            return;
        if (_toAnswer is { } answer)
        {
            _toAnswer = null;
            if (Say(sim, me, answer.Bark, Volume.Say, now, who: answer.Who))
                return;
        }
        foreach (var e in view.Creatures())
            if (e != me && view.Describe(e) is { } seen && !sim.World.Get<Creature>(e).Dead && _greeted.Add(e))
            {
                if (Emotes.Problem(sim, me, Emote.Wave) == null)
                    Send(new EmoteCommand(me, Emote.Wave, null));
                if (Say(sim, me, "greet", Volume.Say, now, who: seen.Name))
                    return;
                Send(new SayCommand(me, $"Morning, {seen.Name}.", Volume.Say)); // no barks in the defs
                _nextTalk = now + TalkCooldown;
                return;
            }
        if (now >= _nextIdle && _talkRand.Chance(Temper.Chatter * 0.25f))
        {
            _nextIdle = now + (long)(SimClock.TicksPerHour / (0.25f + Temper.Chatter));
            Say(sim, me, "idle", Volume.Say, now);
        }
    }

    /// <summary>Its trade's run short of something it can't get itself: the town hears about it (once an hour at most).</summary>
    private void Need(Simulation sim, Entity me, string thing, long now)
    {
        if (now >= _nextTalk && Alarm($"need:{thing}:{now / SimClock.TicksPerHour}"))
            Say(sim, me, "need", Volume.Say, now, thing: thing, place: MyShop(sim, me) is { IsNull: false } shop ? sim.World.Get<PresetBuilding>(shop).Def.Label : null);
    }
}
