using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Expeditions;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;

namespace HeroesNotIncluded.Sim.Director;

/// <summary>
/// The town's standing over a round (docs/DESIGN.md, "The loop"). <b>Renown</b> is what travellers
/// say of the town: it rises as adventurers leave pleased and quests come good. <b>Dread</b> is the
/// dungeon going unanswered: it grows for every quest nobody takes on, and for the ones that go
/// badly. Renown at the festival decides the season; Dread reaching the season's limit ends it
/// early. Weights are in data/defs/season.json. Nobody in town sees the numbers, only a word for
/// each (<c>PerceptionView.Standing</c>); the reveal shows them.
/// </summary>
public sealed class Reckoning : ISimSystem, ISaveState
{
    public string Name => "Reckoning";
    public int Interval => SimClock.TicksPerHour;

    public float Renown { get; private set; }
    public float Dread { get; private set; }

    /// <summary>Adventurers whose opinion of the town has been counted, as they left or at the festival.</summary>
    public int AdventurersCounted { get; private set; }

    private readonly List<float> _renownByHour = [];
    private readonly List<float> _dreadByHour = [];
    public IReadOnlyList<float> RenownByHour => _renownByHour;
    public IReadOnlyList<float> DreadByHour => _dreadByHour;

    // The expedition tallies as of the last update: what's new since is reckoned.
    private int _cleared, _failed, _lost;

    public void Update(Simulation sim)
    {
        if (!sim.Round.Live)
            return;
        var season = sim.Defs.Season;
        var ex = sim.Expeditions;
        var open = 0;
        foreach (var q in ex.Quests)
            if (q.Status == QuestStatus.Open)
                open++;
        AddDread(season, open * season.DreadPerOpenQuestHour);

        var cleared = ex.Succeeded - _cleared;
        var failed = ex.Failed - _failed;
        var lost = ex.Dead - _lost;
        (_cleared, _failed, _lost) = (ex.Succeeded, ex.Failed, ex.Dead);
        Renown += cleared * season.RenownPerQuestCleared + lost * season.RenownPerAdventurerLost;
        AddDread(season, cleared * season.DreadPerQuestCleared + failed * season.DreadPerQuestFailed + lost * season.DreadPerAdventurerLost);

        _renownByHour.Add(Renown);
        _dreadByHour.Add(Dread);
        if (Dread >= season.DreadLimit)
            sim.Round.End(sim, RoundOutcome.Dread);
    }

    private void AddDread(Defs.SeasonDef season, float amount) =>
        Dread = Math.Clamp(Dread + amount, 0f, season.DreadLimit);

    /// <summary>Move the town's standing by hand: crises reckon themselves through this, and tests.</summary>
    internal void Add(Simulation sim, float renown, float dread)
    {
        Renown += renown;
        AddDread(sim.Defs.Season, dread);
    }

    /// <summary>An adventurer leaves town for good: what they thought of it goes into the town's name.</summary>
    public void AdventurerLeft(Simulation sim, float satisfaction)
    {
        if (!sim.Round.Live)
            return;
        var season = sim.Defs.Season;
        var range = season.SatisfactionRange is [var lo, var hi] ? (lo, hi) : (-5f, 10f);
        Renown += Math.Clamp(satisfaction, range.Item1, range.Item2) * season.RenownPerSatisfaction;
        AdventurersCounted++;
    }

    /// <summary>At the festival's end: everyone still in town has their say too.</summary>
    public void FinalCount(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Query<Adventurer>())
            if (!w.Has<Leaving>(e) && !w.Get<Creature>(e).Dead)
                AdventurerLeft(sim, w.Get<Adventurer>(e).Satisfaction);
    }

    /// <summary>What travellers say of the town, in words, for those in it.</summary>
    public static string RenownWord(float renown, float target) => (renown / Math.Max(1f, target)) switch
    {
        < 0f => "Travellers warn each other off this town",
        < 0.25f => "Nobody's heard of this town",
        < 0.5f => "A name or two passes along the road",
        < 0.75f => "Travellers speak of the town",
        < 1f => "The town's name is spreading",
        _ => "The whole valley sings of the town",
    };

    /// <summary>How the dungeon weighs on the valley, in words.</summary>
    public static string DreadWord(float dread, float limit) => (dread / Math.Max(1f, limit)) switch
    {
        < 0.2f => "The valley feels safe",
        < 0.45f => "Folk glance toward the hills",
        < 0.7f => "A pall hangs over the valley",
        < 0.9f => "Dread grips the valley",
        _ => "The valley is on the edge of despair",
    };

    public void Save(SaveWriter w, Simulation sim)
    {
        w.Float(Renown);
        w.Float(Dread);
        w.Int(AdventurersCounted);
        w.Int(_cleared);
        w.Int(_failed);
        w.Int(_lost);
        RoundSummary.Floats(w, _renownByHour);
        RoundSummary.Floats(w, _dreadByHour);
    }

    public void Load(SaveReader r, Simulation sim)
    {
        Renown = r.Float();
        Dread = r.Float();
        AdventurersCounted = r.Int();
        _cleared = r.Int();
        _failed = r.Int();
        _lost = r.Int();
        _renownByHour.Clear();
        _renownByHour.AddRange(RoundSummary.Floats(r));
        _dreadByHour.Clear();
        _dreadByHour.AddRange(RoundSummary.Floats(r));
    }
}
