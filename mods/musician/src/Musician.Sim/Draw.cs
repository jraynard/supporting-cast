using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Musician;

/// <summary>What music has done for an adventurer this stay: how pleased it's made them (capped), and whether they were sent off with a song.</summary>
public struct Entertained
{
    public float Given;
    public bool SentOff;
}

/// <summary>
/// Music's pull on the town's visitors (docs/MUSICIAN-PLAN.md §6): an adventurer who hears it clearly gets thirsty
/// sooner (the inn sells more: the musician's help to the innkeeper and the cook) and is pleased by it, a little, up to
/// a cap a stay (so it can't swamp Renown); one setting out on a quest to a lively, steady tune goes heartened, which
/// the reckoning of the quest counts as morale. Sour music does none of it. Nothing runs with no one playing.
/// </summary>
public sealed class MusicDraw : ISimSystem
{
    public string Name => "MusicDraw";
    public int Interval => 30;

    /// <summary>How much sooner a listener's next drink comes round (a share of the time heard).</summary>
    public const float Thirst = 0.4f;

    /// <summary>Satisfaction a game hour of music gives a visitor, and the most it gives in a stay.</summary>
    public const float PerHour = 0.5f, Cap = 1f;

    /// <summary>Satisfaction for setting out to a song (a little more strength on the road: the quest's morale).</summary>
    public const float SendOff = 0.5f;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        List<(Entity Musician, int3 At, Performance Playing)>? music = null;
        foreach (var e in w.Each<WorkSession>())
            if (w.Get<WorkSession>(e) is PlaySession s && Performance.Of(s) is { Playing: true, Sour: false } p && w.TryGet<Position>(e, out var at))
                (music ??= []).Add((e, at.Cell, p));
        if (music == null)
            return;
        foreach (var e in w.Query<Adventurer>())
        {
            if (!w.TryGet<Position>(e, out var me) || Heard(sim, music, me.Cell) is not { } heard)
                continue;
            ref var a = ref w.Get<Adventurer>(e);
            var had = w.TryGet<Entertained>(e, out var given) ? given : default;
            if (w.Has<Leaving>(e))
            {
                if (a.Quest != 0 && !had.SentOff && heard.Playing.Danceable)
                {
                    a.Satisfaction += SendOff;
                    had.SentOff = true;
                    MoodSystem.Remember(sim, e, sim.Defs.ThoughtOrNull("SentOffWithASong"));
                    sim.Messages.Add(MessageKind.Info, $"{w.Get<Creature>(e).Name} sets out heartened by the music.", me.Cell);
                }
            }
            else
            {
                a.NextDrink -= (long)(Interval * Thirst);
                var more = Math.Min(PerHour * Interval / SimClock.TicksPerHour, Cap - had.Given);
                if (more > 0f)
                {
                    a.Satisfaction += more;
                    had.Given += more;
                }
            }
            w.Add(e, had);
        }
    }

    /// <summary>The music heard clearly from <paramref name="at"/>, if any (the first musician's that carries).</summary>
    private static (Entity Musician, int3 At, Performance Playing)? Heard(Simulation sim, List<(Entity Musician, int3 At, Performance Playing)> music, int3 at)
    {
        foreach (var m in music)
            if (SpeechSystem.Hear(sim, m.At, at, Volume.Say) == Clarity.Clear)
                return m;
        return null;
    }
}
