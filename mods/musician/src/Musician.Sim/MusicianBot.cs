using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Musician;

/// <summary>
/// How a bot plays the musician (<see cref="IBotTrade"/>), through the keys a player has and the session it sees.
/// Of an evening it goes to the inn, takes out the pipe, opens the book and plays its tune's opening, then keeps the
/// beat, breathes at the marks and shapes the long notes till the tune's done; then it rests a while and plays again.
/// </summary>
public sealed class MusicianBot(BotPlayer bot) : IBotTrade
{
    /// <summary>The hours it plays at the inn (from, to): the town's evening, when the common room's fullest.</summary>
    public const int EveningFrom = 17, EveningTo = 22;

    /// <summary>Game minutes it rests between tunes.</summary>
    public const int RestMinutes = 40;

    /// <summary>Steps of quiet before an opening (the book listens for one after a pause), and between its notes.</summary>
    public const int QuietSteps = 50, NoteSteps = 20;

    public BotPlayer Bot => bot;

    private long _session = -1, _restUntil;
    private int _bookAt = -1;
    private bool _opened, _stopped, _played;

    public bool Between(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(me, out var trade) || trade.Def.Name != MusicianModule.TradeName || w.Has<WorkSession>(me))
            return false;
        if (_played)
        {
            _played = false;
            _restUntil = now + SimClock.TicksPerHour * RestMinutes / 60;
        }
        var hour = sim.Clock.HourOfDay;
        if (now < _restUntil || hour < EveningFrom || hour >= EveningTo || MusicianModule.InstrumentOf(sim, me).IsNull)
            return false;
        var inn = ShopSystem.BuildingFor(sim, TownRole.Tavern);
        if (inn.IsNull || !w.TryGet<PresetBuilding>(inn, out var plan))
            return false;
        var here = w.Get<Position>(me).Cell;
        // In the common room, by its middle: where the most will hear.
        if (Buildings.At(sim, here) != inn || HeroesNotIncluded.Sim.Maps.Map.Distance(here, plan.Anchor) > 3)
        {
            bot.Walk(sim, me, plan.Anchor, now);
            return true;
        }
        bot.Send(new AvatarWorkCommand(me, true, here)); // F where it stands: the instrument in hand, or the pipe
        bot.Send(new AvatarWorkCommand(me, false));
        return true;
    }

    public (bool Press, bool Held, int Axis)? Play(WorkSession s)
    {
        if (s is not PlaySession p)
            return null;
        if (p.StartTick != _session)
        {
            (_session, _bookAt, _opened, _stopped) = (p.StartTick, -1, false, false);
            _played = true; // rest after this session, however it goes
        }
        // A tune played through, or slipped away: put it away, and rest.
        if (p.Finished + p.Slipped > 0 && p.Carrying == null)
        {
            if (!_stopped)
            {
                _stopped = true;
                bot.Send(new StopWorkCommand(p.Worker));
            }
            return (false, false, 0);
        }
        if (p.Carrying != null)
            return Carry(p);
        if (p.Mode == PlayMode.Free)
        {
            // Open the book, once (Space): then play the opening of the tune it opens at, the instrument's own.
            bot.Act = !_opened;
            _opened = true;
            return (false, false, 0);
        }
        Opening(p);
        return (false, false, 0);
    }

    /// <summary>The first tune in the book's opening, a note every <see cref="NoteSteps"/> after a quiet, each let go halfway.</summary>
    private void Opening(PlaySession p)
    {
        if (p.Book.Count == 0)
            return;
        if (_bookAt < 0)
            _bookAt = p.StepsDone;
        var tune = p.Book[Math.Clamp(p.Picked, 0, p.Book.Count - 1)];
        var t = p.StepsDone - _bookAt - QuietSteps;
        if (t < 0 || t % (NoteSteps / 2) != 0)
            return;
        var k = t / NoteSteps;
        if (k >= tune.Call.Count)
            return;
        var note = tune.Call[k];
        var on = t % NoteSteps == 0;
        var index = note.Step;
        bot.Custom.Add(PlaySession.Pack(index <= 7 ? index : index - 7, index > 7, on, note.Accidental > 0));
    }

    /// <summary>The hands carrying the tune: F on each beat, a breath at each mark, F held through each long note.</summary>
    private (bool, bool, int) Carry(PlaySession p)
    {
        // A slip of temper: a breath forgotten now and then (the air runs low, and the mind with it).
        var breathe = p.InGap && !(p.GapTaken && p.Breath >= 1f) && !(bot.Slip && p.Breaths % 7 == 3);
        if (breathe != p.Breathing)
            bot.Custom.Add(PlaySession.PackBreath(breathe));
        var swell = p.LongAt >= 0 && p.RingingNote != null;
        var tap = !p.BeatKept && !p.Held && p.StepsDone >= (int)MathF.Ceiling(p.Beat);
        return (tap, swell, 0);
    }
}
