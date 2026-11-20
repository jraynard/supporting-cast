using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Expeditions;
using HeroesNotIncluded.Sim.Saving;

namespace HeroesNotIncluded.Sim.Director;

/// <summary>
/// A crisis under way: what it is, and whether the town has met it yet. Each kind keeps what it
/// needs to tell (the raiders it sent, the building it lit) and saves it.
/// </summary>
public interface ICrisis
{
    CrisisKind Kind { get; }
    long Started { get; }

    /// <summary>Null while it's going on; then whether the town met it (drove them off, put it out).</summary>
    bool? Check(Simulation sim);

    /// <summary>What the town hears once it's over.</summary>
    string Outcome(bool met);

    void Save(SaveWriter w);
}

/// <summary>
/// The season's pressure (docs/DESIGN.md, "Pressure and traitors"), paced like a game master
/// (docs/DIRECTOR-PLAN.md). In a round it sends the crises <see cref="SeasonDef.Beats"/> lays out:
/// a scare late on the first night, something worse on the second day, the dragon before the
/// festival. It reads the town as it goes (<see cref="Tension"/>) and answers it
/// (<see cref="RuleMind"/>): a town that's coasting gets its trouble sooner and harder, one that's
/// struggling later and gentler, and a beat waits in its window for the last trouble to settle
/// before the next comes. What kind each beat is gets settled a raid's warning ahead of it, weighed
/// toward trouble somebody can meet and the season hasn't had. A beat may follow a quest instead
/// (<see cref="CrisisBeat.FollowsQuest"/>): what the party stirs up there comes to town after.
/// In a lull, it sends a small incident (<see cref="Incidents"/>).
/// A raid can be seen coming: some <see cref="SeasonDef.RaidWarningHours"/> ahead, word of it
/// reaches the town (how many, from which road), the raid is fixed as told, and the town calls for
/// arms (<see cref="Militia.MilitiaSystem"/>). Each crisis is met or not, and <see cref="Reckoning"/>
/// hears which. Off until the game turns it on for a round (<see cref="Enable"/>), so tests and the
/// bench stay scripted; outside a round the old <c>Storyteller</c> is the one that sends raids.
/// Its rolls are its own stream.
/// </summary>
public sealed class Director(ulong seed) : ISimSystem, ISaveState
{
    public string Name => "Director";
    public int Interval => SimClock.TicksPerHour / 6;

    private readonly SimRandom _rand = new(seed ^ 0xD1EC_7012u);

    /// <summary>Each beat's planned moment in its window; -1 for one waiting on its quest.</summary>
    private readonly List<long> _beatAt = [];
    private readonly List<bool> _fired = [];

    /// <summary>Each beat's kind, once settled (a warning's lead before it's due); null before.</summary>
    private readonly List<CrisisKind?> _kind = [];
    private readonly List<PlannedRaid?> _planned = [];

    /// <summary>A beat whose moment won't move again: a raid the town's been told of, one set off by its quest.</summary>
    private readonly List<bool> _fixed = [];
    private readonly List<ICrisis> _active = [];

    /// <summary>When the town was last anything but quiet (calm, and nothing under way); -1 before the first look.</summary>
    private long _quietSince = -1;

    /// <summary>When the last incident came; -1 for none yet.</summary>
    private long _lastIncident = -1;

    /// <summary>Incidents sent this season, by def name.</summary>
    private readonly SortedDictionary<string, int> _incidents = new(StringComparer.Ordinal);

    /// <summary>Sending crises in rounds. Set by the game when it starts a village for a round.</summary>
    public bool Enabled { get; private set; }

    /// <summary>
    /// What an AI villager counts toward a raid's size, over the season's <c>raidAiShare</c>: the combat sweep's trials
    /// stand a town of AI in for a town of players, and size its raids as for players. Not saved: only trials set it.
    /// </summary>
    public float? RaidAiShare { get; set; }

    /// <summary>How hard it pushes (raid size); null for the default. Peaceful sends nothing.</summary>
    public DifficultyDef? Difficulty { get; private set; }

    public IReadOnlyList<ICrisis> Active => _active;

    /// <summary>The Director's last reading of the town (the dev panel, the sweep). Not saved: read again each update.</summary>
    public Tension Reading { get; private set; }

    /// <summary>Incidents sent this season, by def name.</summary>
    public IReadOnlyDictionary<string, int> IncidentsSent => _incidents;

    /// <summary>The road (a map side, as <see cref="Combat.Raids.SideName"/>) of the next raid the town's been warned of that hasn't come yet; null if none.</summary>
    public int? WarnedSide => Coming?.Side;

    /// <summary>The raid the town's been warned of and is waiting on, if any (as it was told).</summary>
    public PlannedRaid? Coming
    {
        get
        {
            for (var i = 0; i < _planned.Count && i < _fired.Count; i++)
                if (!_fired[i] && _planned[i] is { } plan)
                    return plan;
            return null;
        }
    }
    public int Met { get; private set; }
    public int Unmet { get; private set; }

    /// <summary>Crisis kinds something registers (<see cref="CrisisTypes"/>); the season's other kinds are passed over.</summary>
    public static bool CanSend(CrisisKind kind) => CrisisTypes.Of(kind) != null;

    public void Enable(DifficultyDef? difficulty)
    {
        Enabled = true;
        Difficulty = difficulty;
    }

    /// <summary>
    /// Every beat of the season counted as come and gone, so only crises sent by hand arrive: the
    /// combat sweep (<see cref="Scenarios.CombatSweep"/>) measures one raid at a time, alone.
    /// </summary>
    public void PassOverBeats(Simulation sim)
    {
        if (_beatAt.Count == 0)
            Plan(sim, sim.Defs.Season);
        for (var i = 0; i < _fired.Count; i++)
            _fired[i] = true;
    }

    public void Update(Simulation sim)
    {
        if (!Enabled || !sim.Round.Live || Difficulty is { Peaceful: true })
            return;
        var season = sim.Defs.Season;
        // A mode without the season's crises (a siege, the practice yard) sends none of its own; those sent by hand still play out.
        var beats = sim.Round.ModeOf(sim).Crises;
        if (beats && _beatAt.Count == 0 && season.Beats.Count > 0)
            Plan(sim, season);
        var now = sim.Clock.Tick;
        Reading = Tension.Read(sim, _active.Count);
        var calm = Reading.Harm < season.Pacing.CalmHarm && _active.Count == 0;
        if (!calm || _quietSince < 0)
            _quietSince = now;
        if (beats && sim.Round.Phase == Round.RoundPhase.Playing)
        {
            FollowQuests(sim, season);
            var lead = SimClock.HoursToTicks(season.RaidWarningHours);
            for (var i = 0; i < Beats(season); i++)
            {
                if (_fired[i] || Pending(i) || now < Due(sim, season, i) - lead)
                    continue;
                _kind[i] ??= Decide(sim, season, i);
                if (_kind[i] == CrisisKind.Raid && _planned[i] == null)
                    Warn(sim, season, i);
            }
            // Trouble comes for the heroes' purses, and the heroes are who fights it: a beat waits
            // in its window for a party to be in town and the last trouble to have settled, and
            // comes at its end regardless. A raid the town's been warned of doesn't wait on calm.
            var heroes = Adventurers.PartySystem.Present(sim);
            for (var i = 0; i < Beats(season); i++)
            {
                if (_fired[i] || Pending(i) || now < Due(sim, season, i))
                    continue;
                var ready = now >= To(sim, season, i) || (heroes && (calm || _planned[i] != null));
                if (!ready)
                    continue;
                _fired[i] = true;
                Fire(sim, season, i);
            }
            if (!sim.Round.Practice)
                MaybeIncident(sim, season);
        }
        for (var i = 0; i < _active.Count; i++)
        {
            if (_active[i].Check(sim) is not { } met)
                continue;
            var crisis = _active[i];
            _active.RemoveAt(i--);
            if (met)
                Met++;
            else
                Unmet++;
            sim.Reckoning.CrisisResolved(sim, crisis.Kind, met);
            if (crisis.Kind == CrisisKind.Raid)
                sim.Militia.StandDown(sim);
            sim.Messages.Add(met ? MessageKind.Good : MessageKind.Warning, crisis.Outcome(met), null);
        }
    }

    private int Beats(SeasonDef season) => Math.Min(_beatAt.Count, season.Beats.Count);

    /// <summary>A beat that follows a quest nobody's set out on yet.</summary>
    private bool Pending(int i) => _beatAt[i] < 0;

    /// <summary>Each beat's moment, picked in its window at the start of the round; one following a quest waits on it.</summary>
    private void Plan(Simulation sim, SeasonDef season)
    {
        foreach (var beat in season.Beats)
        {
            if (beat.FollowsQuest.Length > 0)
                _beatAt.Add(-1);
            else
            {
                var from = sim.Round.TickAt(sim, beat.Day, beat.FromHour);
                var to = Math.Max(from, sim.Round.TickAt(sim, beat.Day, beat.ToHour));
                _beatAt.Add(from + (long)((to - from) * _rand.NextFloat()));
            }
            _fired.Add(false);
            _kind.Add(null);
            _planned.Add(null);
            _fixed.Add(false);
        }
    }

    /// <summary>
    /// Beats following a quest whose party has now set out: each set for its hours after, in
    /// place of the beat it replaces. It doesn't come if it would run into the festival, or if what
    /// it replaces has already come (or been warned of).
    /// </summary>
    private void FollowQuests(Simulation sim, SeasonDef season)
    {
        for (var i = 0; i < Beats(season); i++)
        {
            var beat = season.Beats[i];
            if (_fired[i] || !Pending(i) || !SetOut(sim, beat.FollowsQuest))
                continue;
            var kind = _kind[i] ?? Decide(sim, season, i);
            var hours = beat.AfterHours[0] + (beat.AfterHours[1] - beat.AfterHours[0]) * _rand.NextFloat();
            var at = sim.Clock.Tick + SimClock.HoursToTicks(hours);
            var lasts = kind is { } k ? CrisisTypes.Hours(k, season) : season.CrisisHours;
            var replaced = beat.Replaces.Length == 0 ? -1 : season.Beats.FindIndex(b => b.Id == beat.Replaces);
            if (kind == null || at + SimClock.HoursToTicks(lasts) > sim.Round.SetupTick(sim)
                || (replaced >= 0 && replaced < _fired.Count && (_fired[replaced] || _planned[replaced] != null)))
            {
                _fired[i] = true; // it stays asleep
                continue;
            }
            if (replaced >= 0 && replaced < _fired.Count)
                _fired[replaced] = true;
            _beatAt[i] = at;
            _kind[i] = kind;
            _fixed[i] = true;
            if (beat.Omen.Length > 0)
                sim.Messages.Add(MessageKind.Warning, beat.Omen, null);
        }
    }

    /// <summary>A party has set out on a quest of this name (taken it and left town).</summary>
    private static bool SetOut(Simulation sim, string quest)
    {
        var ex = sim.Expeditions;
        foreach (var q in ex.Quests)
        {
            if (q.Def.Name != quest || q.Status == QuestStatus.Open)
                continue;
            if (q.Status == QuestStatus.Cleared)
                return true;
            foreach (var e in ex.Out)
                if (e.Quest == q.Id && e.SetOutBy == 0)
                    return true;
        }
        return false;
    }

    /// <summary>Which kind a beat is, weighed against the town now (<see cref="RuleMind.Weigh(Simulation, IEnumerable{ValueTuple{CrisisKind, float}}, IReadOnlyCollection{CrisisKind})"/>); null if it has none the game can send.</summary>
    private CrisisKind? Decide(Simulation sim, SeasonDef season, int i)
    {
        var kinds = season.Beats[i].Weights().Where(k => CanSend(k.Kind)).ToList();
        var options = RuleMind.Weigh(sim, kinds, Seen());
        // Every kind weighed away: the beat's own weights, as written.
        if (options.Count == 0)
            options = kinds;
        return options.Count == 0 ? null : RuleMind.Pick(_rand, options);
    }

    /// <summary>The kinds this season's beats have already brought.</summary>
    private List<CrisisKind> Seen()
    {
        var seen = new List<CrisisKind>();
        for (var i = 0; i < _fired.Count; i++)
            if (_fired[i] && _kind[i] is { } k && !seen.Contains(k))
                seen.Add(k);
        return seen;
    }

    private long From(Simulation sim, SeasonDef season, int i) =>
        _fixed[i] || season.Beats[i].FollowsQuest.Length > 0 ? _beatAt[i] : sim.Round.TickAt(sim, season.Beats[i].Day, season.Beats[i].FromHour);

    private long To(Simulation sim, SeasonDef season, int i) =>
        _fixed[i] || season.Beats[i].FollowsQuest.Length > 0 ? _beatAt[i] : Math.Max(From(sim, season, i), sim.Round.TickAt(sim, season.Beats[i].Day, season.Beats[i].ToHour));

    /// <summary>When a beat is due: its planned moment, moved in its window by the push (<see cref="RuleMind.Due"/>), unless it's fixed.</summary>
    private long Due(Simulation sim, SeasonDef season, int i) =>
        _fixed[i] ? _beatAt[i] : RuleMind.Due(_beatAt[i], From(sim, season, i), To(sim, season, i), Reading.Push);

    /// <summary>A beat's strength now: its own, eased or pushed.</summary>
    private float StrengthOf(SeasonDef season, CrisisBeat beat) => RuleMind.Strength(season.Pacing, beat.Strength, Reading.Push);

    /// <summary>
    /// Word of a raid hours ahead: it's sized and its road picked now, and the town told so (the
    /// count is honest), and the call goes out for arms. Raiders are bandits on the hills, so the
    /// town's arming after this doesn't make the band any bigger. Its moment is fixed as told.
    /// </summary>
    private void Warn(Simulation sim, SeasonDef season, int i)
    {
        var plan = RaidCrisis.Plan(sim, _rand, Difficulty, StrengthOf(season, season.Beats[i]));
        _beatAt[i] = Due(sim, season, i);
        _fixed[i] = true;
        _planned[i] = plan;
        var due = _beatAt[i];
        var hour = (int)(((due + sim.Clock.StartHour * (long)SimClock.TicksPerHour) % SimClock.TicksPerDay) / SimClock.TicksPerHour);
        var when = hour >= 18 || hour < 5 ? "tonight" : hour >= 12 ? "by evening" : "before noon";
        var many = plan.Kinds.Count == 1 ? "one of them" : plan.Kinds.Count <= 3 ? $"{plan.Kinds.Count} of them" : $"{plan.Kinds.Count} or so";
        sim.Messages.Add(MessageKind.Danger,
            $"A shepherd runs in from the {Combat.Raids.SideName(plan.Side)} road: bandits camped in the hills, {many}. They'll come {when}.", null, "Bandits in the hills");
        sim.Militia.Call(sim, when);
    }

    /// <summary>
    /// The season's next raid, warned of now and due a warning's lead from now (the dev panel).
    /// A beat not yet settled that could be a raid becomes one. False if there's no round under way
    /// or no raid left in it unwarned.
    /// </summary>
    public bool WarnNext(Simulation sim)
    {
        if (!Enabled || sim.Round.Phase != Round.RoundPhase.Playing || Difficulty is { Peaceful: true })
            return false;
        var season = sim.Defs.Season;
        // A mode without the season's crises (a siege, the practice yard) sends none of its own; those sent by hand still play out.
        var beats = sim.Round.ModeOf(sim).Crises;
        if (beats && _beatAt.Count == 0 && season.Beats.Count > 0)
            Plan(sim, season);
        Reading = Tension.Read(sim, _active.Count);
        for (var i = 0; i < Beats(season); i++)
        {
            if (_fired[i] || Pending(i) || _planned[i] != null)
                continue;
            if (_kind[i] == null && season.Beats[i].Weights().Any(k => k.Kind == CrisisKind.Raid))
                _kind[i] = CrisisKind.Raid;
            if (_kind[i] != CrisisKind.Raid)
                continue;
            // A warning's lead from now (never before its window opens).
            var lead = SimClock.HoursToTicks(season.RaidWarningHours);
            _beatAt[i] = Math.Max(From(sim, season, i), sim.Clock.Tick + lead);
            _fixed[i] = true;
            Warn(sim, season, i);
            return true;
        }
        return false;
    }

    private void Fire(Simulation sim, SeasonDef season, int i)
    {
        var beat = season.Beats[i];
        _kind[i] ??= Decide(sim, season, i);
        if (_kind[i] is not { } kind)
            return;
        if (kind == CrisisKind.Raid && _planned[i] is { } plan)
        {
            var raid = RaidCrisis.Begin(sim, _rand, plan, byRoad: true); // by the road the town was told of
            if (raid != null)
                _active.Add(raid);
            else
                sim.Militia.StandDown(sim);
            return;
        }
        Send(sim, kind, StrengthOf(season, beat));
    }

    /// <summary>
    /// A small incident, in a lull: the town quiet a while, no incident lately, nothing due soon, and
    /// not struggling. One of those that could happen now, by weight (<see cref="RuleMind.Weigh(Simulation, IncidentDef)"/>).
    /// </summary>
    private void MaybeIncident(Simulation sim, SeasonDef season)
    {
        var pacing = season.Pacing;
        var now = sim.Clock.Tick;
        if (sim.Defs.AllIncidents.Count == 0 || now - _quietSince < SimClock.HoursToTicks(pacing.LullHours)
            || (_lastIncident >= 0 && now - _lastIncident < SimClock.HoursToTicks(pacing.IncidentGapHours))
            || Reading.Push < pacing.IncidentPushFloor)
            return;
        var clear = SimClock.HoursToTicks(pacing.BeatClearHours);
        for (var i = 0; i < Beats(season); i++)
            if (!_fired[i] && !Pending(i) && Due(sim, season, i) - now < clear)
                return;
        var options = Eligible(sim).Select(d => (d, RuleMind.Weigh(sim, d))).Where(o => o.Item2 > 0f).ToList();
        if (RuleMind.Pick(_rand, options) is { } incident)
            Call(sim, incident);
    }

    /// <summary>Incidents that could come now: not yet had as often as they may be, from their day, and possible.</summary>
    private IEnumerable<IncidentDef> Eligible(Simulation sim)
    {
        var day = sim.Round.SeasonDay(sim).Day;
        foreach (var d in sim.Defs.AllIncidents)
            if (_incidents.GetValueOrDefault(d.Name) < d.MaxPerRound && day >= d.MinDay && Incidents.Possible(sim, d, _active))
                yield return d;
    }

    private bool Call(Simulation sim, IncidentDef incident)
    {
        if (!Incidents.Begin(sim, _rand, incident))
            return false;
        _incidents[incident.Name] = _incidents.GetValueOrDefault(incident.Name) + 1;
        _lastIncident = sim.Clock.Tick;
        _quietSince = sim.Clock.Tick;
        return true;
    }

    /// <summary>
    /// A choice from outside the sim (<see cref="DirectorChoiceCommand"/>): the next unsettled beat
    /// is <paramref name="kind"/>, if it's one of that beat's kinds. False if not.
    /// </summary>
    public bool Choose(Simulation sim, CrisisKind kind)
    {
        if (!Enabled || sim.Round.Phase != Round.RoundPhase.Playing || !CanSend(kind))
            return false;
        var season = sim.Defs.Season;
        // A mode without the season's crises (a siege, the practice yard) sends none of its own; those sent by hand still play out.
        var beats = sim.Round.ModeOf(sim).Crises;
        if (beats && _beatAt.Count == 0 && season.Beats.Count > 0)
            Plan(sim, season);
        for (var i = 0; i < Beats(season); i++)
        {
            if (_fired[i] || Pending(i) || _kind[i] != null)
                continue;
            if (!season.Beats[i].Weights().Any(k => k.Kind == kind))
                return false; // the next beat can't be that
            _kind[i] = kind;
            return true;
        }
        return false;
    }

    /// <summary>An incident now, by name, if it could happen (<see cref="DirectorChoiceCommand"/>, the dev panel). False if not.</summary>
    public bool CallIncident(Simulation sim, string name)
    {
        if (!Enabled || sim.Round.Phase != Round.RoundPhase.Playing)
            return false;
        var incident = Eligible(sim).FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
        return incident != null && Call(sim, incident);
    }

    /// <summary>Send a crisis now (the beats, tests and the debug panel). Null if it couldn't come.</summary>
    public ICrisis? Send(Simulation sim, CrisisKind kind, float strength = 1f)
    {
        var crisis = CrisisTypes.Of(kind)?.Begin(sim, _rand, Difficulty, strength);
        if (crisis != null)
            _active.Add(crisis);
        return crisis;
    }

    /// <summary>
    /// A raid now, sized as a beat's would be, by the road off one side of the map (0 west, 1 east,
    /// 2 north, 3 south; the map edge there if no road leaves it). The raid drill and tests.
    /// </summary>
    public RaidCrisis? SendRaid(Simulation sim, int side, float strength = 1f)
    {
        var plan = RaidCrisis.Plan(sim, _rand, Difficulty, strength) with { Side = Math.Clamp(side, 0, 3) };
        var raid = RaidCrisis.Begin(sim, _rand, plan, byRoad: true);
        if (raid != null)
            _active.Add(raid);
        return raid;
    }

    /// <summary>A fire now in this building (the fire drill). Null if nothing in it will burn.</summary>
    public FireCrisis? SendFire(Simulation sim, Entity building)
    {
        var fire = FireCrisis.Begin(sim, _rand, building);
        if (fire != null)
            _active.Add(fire);
        return fire;
    }

    public void Save(SaveWriter w, Simulation sim)
    {
        var (a, b, c, d) = _rand.State;
        w.UInt(a); w.UInt(b); w.UInt(c); w.UInt(d);
        w.Bool(Enabled);
        w.Def(Difficulty);
        w.Int(_beatAt.Count);
        for (var i = 0; i < _beatAt.Count; i++)
        {
            w.Long(_beatAt[i]);
            w.Bool(_fired[i]);
            w.NullableString(_kind[i]?.Name);
            w.Bool(_planned[i] != null);
            _planned[i]?.Save(w);
            w.Bool(_fixed[i]);
        }
        w.Int(Met);
        w.Int(Unmet);
        w.Int(_active.Count);
        foreach (var crisis in _active)
        {
            w.String(crisis.Kind.Name);
            crisis.Save(w);
        }
        w.Long(_quietSince);
        w.Long(_lastIncident);
        w.Int(_incidents.Count);
        foreach (var (name, count) in _incidents)
        {
            w.String(name);
            w.Int(count);
        }
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _rand.State = (r.UInt(), r.UInt(), r.UInt(), r.UInt());
        Enabled = r.Bool();
        Difficulty = r.DefOrNull<DifficultyDef>();
        _beatAt.Clear();
        _fired.Clear();
        _kind.Clear();
        _planned.Clear();
        _fixed.Clear();
        for (var n = r.Count(1000); n > 0; n--)
        {
            _beatAt.Add(r.Long());
            _fired.Add(r.Bool());
            if (r.Version >= 65)
            {
                // From format 65 a kind is saved by name (a mod's among them); one whose mod is off is settled again.
                _kind.Add(r.NullableString() is { } name && CrisisTypes.Find(name) is { } type ? type.Kind : null);
                _planned.Add(r.Bool() ? PlannedRaid.Read(r) : null);
                _fixed.Add(r.Bool());
            }
            else if (r.Version >= 60)
            {
                // From format 60 a beat's kind is settled a warning's lead before it's due, and its moment may be fixed.
                var known = r.Bool();
                var kind = r.Int();
                _kind.Add(known ? CrisisKind.FromOld(kind) : null);
                _planned.Add(r.Bool() ? PlannedRaid.Read(r) : null);
                _fixed.Add(r.Bool());
            }
            else if (r.Version >= 13)
            {
                // From format 13 each beat's kind was settled when it was planned (and a raid when it was warned of).
                var kind = r.Int();
                _kind.Add(CrisisKind.FromOld(kind) ?? CrisisKind.Raid);
                var plan = r.Bool() ? PlannedRaid.Read(r) : null;
                _planned.Add(plan);
                _fixed.Add(plan != null);
            }
            else
            {
                _kind.Add(null);
                _planned.Add(null);
                _fixed.Add(false);
            }
        }
        Met = r.Int();
        Unmet = r.Int();
        _active.Clear();
        for (var n = r.Count(1000); n > 0; n--)
        {
            var name = r.Version >= 65 ? r.String() : CrisisKind.FromOld(r.Int())?.Name ?? "";
            var type = CrisisTypes.Find(name) ?? throw new SaveException($"A crisis under way of a kind nothing here knows ({name}): is a mod missing?");
            _active.Add(type.Read(r));
        }
        _quietSince = -1;
        _lastIncident = -1;
        _incidents.Clear();
        if (r.Version >= 60)
        {
            _quietSince = r.Long();
            _lastIncident = r.Long();
            for (var n = r.Count(1000); n > 0; n--)
                _incidents[r.String()] = r.Int();
        }
    }
}
