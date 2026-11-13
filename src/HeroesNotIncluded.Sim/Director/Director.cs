using HeroesNotIncluded.Sim.Defs;
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
/// The season's pressure (docs/DESIGN.md, "Pressure and traitors"). In a round it sends the
/// crises <see cref="SeasonDef.Beats"/> lays out: a scare late on the first night, something worse
/// on the second day, the dragon before the festival. Dread brings each sooner in its window and
/// makes it stronger. Each crisis is met or not, and <see cref="Reckoning"/> hears which.
/// Off until the game turns it on for a round (<see cref="Enable"/>), so tests and the bench stay
/// scripted; outside a round the old <c>Storyteller</c> is the one that sends raids. Its rolls are
/// its own stream.
/// </summary>
public sealed class Director(ulong seed) : ISimSystem, ISaveState
{
    public string Name => "Director";
    public int Interval => SimClock.TicksPerHour / 6;

    private readonly SimRandom _rand = new(seed ^ 0xD1EC_7012u);
    private readonly List<long> _beatAt = [];
    private readonly List<bool> _fired = [];
    private readonly List<ICrisis> _active = [];

    /// <summary>Sending crises in rounds. Set by the game when it starts a village for a round.</summary>
    public bool Enabled { get; private set; }

    /// <summary>How hard it pushes (raid size); null for the default. Peaceful sends nothing.</summary>
    public DifficultyDef? Difficulty { get; private set; }

    public IReadOnlyList<ICrisis> Active => _active;
    public int Met { get; private set; }
    public int Unmet { get; private set; }

    /// <summary>Crisis kinds the game can send so far; the season's other kinds are passed over.</summary>
    public static bool CanSend(CrisisKind kind) => kind is CrisisKind.Raid or CrisisKind.Fire or CrisisKind.Shortage or CrisisKind.Plague or CrisisKind.Dragon;

    public void Enable(DifficultyDef? difficulty)
    {
        Enabled = true;
        Difficulty = difficulty;
    }

    public void Update(Simulation sim)
    {
        if (!Enabled || !sim.Round.Live || Difficulty is { Peaceful: true })
            return;
        var season = sim.Defs.Season;
        if (_beatAt.Count == 0 && season.Beats.Count > 0)
            Plan(sim, season);
        var now = sim.Clock.Tick;
        if (sim.Round.Phase == Round.RoundPhase.Playing)
        {
            // Trouble comes for the heroes' purses, and the heroes are who fights it: a beat waits
            // in its window for a party to be in town, and comes at its end regardless.
            var heroes = Adventurers.PartySystem.Present(sim);
            for (var i = 0; i < _beatAt.Count && i < season.Beats.Count; i++)
                if (!_fired[i] && now >= Due(sim, season, i)
                    && (heroes || now >= sim.Round.TickAt(sim, season.Beats[i].Day, season.Beats[i].ToHour)))
                {
                    _fired[i] = true;
                    Fire(sim, season, season.Beats[i]);
                }
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
            sim.Messages.Add(met ? MessageKind.Good : MessageKind.Warning, crisis.Outcome(met), null);
        }
    }

    /// <summary>Each beat's moment, picked in its window at the start of the round.</summary>
    private void Plan(Simulation sim, SeasonDef season)
    {
        foreach (var beat in season.Beats)
        {
            var from = sim.Round.TickAt(sim, beat.Day, beat.FromHour);
            var to = Math.Max(from, sim.Round.TickAt(sim, beat.Day, beat.ToHour));
            _beatAt.Add(from + (long)((to - from) * _rand.NextFloat()));
            _fired.Add(false);
        }
    }

    /// <summary>When a beat is due: its planned moment, pulled toward the start of its window by Dread.</summary>
    private long Due(Simulation sim, SeasonDef season, int i)
    {
        var from = sim.Round.TickAt(sim, season.Beats[i].Day, season.Beats[i].FromHour);
        var dread = Math.Clamp(sim.Reckoning.Dread / Math.Max(1f, season.DreadLimit), 0f, 1f);
        return _beatAt[i] - (long)((_beatAt[i] - from) * dread);
    }

    private void Fire(Simulation sim, SeasonDef season, CrisisBeat beat)
    {
        var kinds = beat.Weights().Where(k => CanSend(k.Kind)).ToList();
        if (kinds.Count == 0)
            return;
        var roll = _rand.NextFloat() * kinds.Sum(k => k.Weight);
        var kind = kinds[^1].Kind;
        foreach (var (k, weight) in kinds)
        {
            if (roll < weight)
            {
                kind = k;
                break;
            }
            roll -= weight;
        }
        var dread = Math.Clamp(sim.Reckoning.Dread / Math.Max(1f, season.DreadLimit), 0f, 1f);
        Send(sim, kind, beat.Strength * (1f + 0.5f * dread));
    }

    /// <summary>Send a crisis now (the beats, tests and the debug panel). Null if it couldn't come.</summary>
    public ICrisis? Send(Simulation sim, CrisisKind kind, float strength = 1f)
    {
        ICrisis? crisis = kind switch
        {
            CrisisKind.Raid => RaidCrisis.Begin(sim, _rand, Difficulty, strength),
            CrisisKind.Fire => FireCrisis.Begin(sim, _rand),
            CrisisKind.Shortage => ShortageCrisis.Begin(sim),
            CrisisKind.Plague => PlagueCrisis.Begin(sim, _rand),
            CrisisKind.Dragon => DragonCrisis.Begin(sim, _rand),
            _ => null,
        };
        if (crisis != null)
            _active.Add(crisis);
        return crisis;
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
        }
        w.Int(Met);
        w.Int(Unmet);
        w.Int(_active.Count);
        foreach (var crisis in _active)
        {
            w.Int((int)crisis.Kind);
            crisis.Save(w);
        }
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _rand.State = (r.UInt(), r.UInt(), r.UInt(), r.UInt());
        Enabled = r.Bool();
        Difficulty = r.DefOrNull<DifficultyDef>();
        _beatAt.Clear();
        _fired.Clear();
        for (var n = r.Count(1000); n > 0; n--)
        {
            _beatAt.Add(r.Long());
            _fired.Add(r.Bool());
        }
        Met = r.Int();
        Unmet = r.Int();
        _active.Clear();
        for (var n = r.Count(1000); n > 0; n--)
        {
            var kind = (CrisisKind)r.Int();
            _active.Add(kind switch
            {
                CrisisKind.Raid => RaidCrisis.Read(r),
                CrisisKind.Fire => FireCrisis.Read(r),
                CrisisKind.Shortage => ShortageCrisis.Read(r),
                CrisisKind.Plague => PlagueCrisis.Read(r),
                CrisisKind.Dragon => DragonCrisis.Read(r),
                _ => throw new SaveException($"Unknown crisis kind {kind}"),
            });
        }
    }
}
