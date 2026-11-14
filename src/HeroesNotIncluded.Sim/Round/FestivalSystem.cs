using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Round;

/// <summary>Where the festival is in its evening (<see cref="FestivalDef"/>).</summary>
public enum FestivalPart : byte
{
    /// <summary>No festival yet (or no round).</summary>
    None,

    /// <summary>The bell's rung: down tools, and to the square.</summary>
    Opening,

    /// <summary>The feast, the dancing, the show table and the games.</summary>
    Revels,

    /// <summary>Toasts, honours and the verdict.</summary>
    Ceremony,
}

/// <summary>
/// The ceremony's steps, in order. Each is entered once, says what it says, and holds the square
/// for its time. <see cref="Accusation"/> is a place kept for the traitors (M4): the town's last
/// chance to name one before the verdict. It does nothing yet.
/// </summary>
public enum CeremonyStep : byte
{
    Bell,
    Toasts,
    Honours,
    Accusation,
    Verdict,
    Done,
}

/// <summary>Someone called up at the ceremony, and what for.</summary>
public sealed record Honour(string Title, string Name, Entity Who);

/// <summary>How a festival game's going: who's best so far, and how many have had a go.</summary>
public sealed class GameStanding
{
    public required FestivalGameDef Game { get; init; }
    public Entity Best = Entity.Null;
    public string BestName = "";
    public int BestScore = -1;
    public int Turns;
    public long NextAiTurn;
}

/// <summary>
/// The season's last evening, made an event: the props go up in the square, the town and its
/// visitors feast and dance and look over the show table and try the games, then the ceremony
/// gives each party still in town its toast (counted there, aloud, so the town watches its
/// Renown move), calls the honours and reads the verdict. <see cref="RoundSystem"/> owns the
/// festival's start and the round's end; this owns everything in between.
/// </summary>
public sealed class FestivalSystem(ulong seed) : ISimSystem, ISaveState
{
    private readonly SimRandom _rand = new(seed ^ 0xFE57_1BA1UL);

    /// <summary>What the map puts up: def, cell, facing.</summary>
    private readonly List<(ThingDef Def, int3 Cell, int Facing)> _layout = [];

    private readonly List<Entity> _props = [];
    private readonly List<GameStanding> _games = [];
    private readonly HashSet<Entity> _toasted = [];
    private readonly HashSet<(Entity Who, Entity Piece)> _admired = [];
    private readonly Dictionary<Entity, float> _watched = [];
    private readonly Dictionary<Entity, float> _praise = [];
    private readonly List<int> _parties = [];
    private readonly List<Honour> _honours = [];

    public string Name => "Festival";

    /// <summary>A game minute: the ceremony's steps are counted in them.</summary>
    public int Interval => SimClock.TicksPerHour / 60;

    /// <summary>When the festival began; -1 before.</summary>
    public long StartTick { get; private set; } = -1;

    public FestivalPart Part { get; private set; }

    /// <summary>The ceremony's step, once it's begun.</summary>
    public CeremonyStep Step { get; private set; }

    private long _stepUntil;
    private int _stepIndex;

    /// <summary>Who's leading the ceremony from the well: chosen when it begins.</summary>
    public Entity Crier { get; private set; } = Entity.Null;

    /// <summary>The tick the festival's bell last rang (the opening, the ceremony), for the sound. -1 never.</summary>
    public long BellTick { get; private set; } = -1;

    /// <summary>The verdict's been read aloud: the round only waits for it to be heard.</summary>
    public bool VerdictRead { get; private set; }

    /// <summary>Adventurers counted at their party's toast: the reckoning's final count passes over them.</summary>
    public IReadOnlySet<Entity> Toasted => _toasted;

    public IReadOnlyList<GameStanding> Games => _games;

    /// <summary>Who the ceremony calls up, chosen as it begins.</summary>
    public IReadOnlyList<Honour> Honours => _honours;

    /// <summary>What's been put up for it, while it lasts.</summary>
    public IReadOnlyList<Entity> Props => _props;

    /// <summary>Has the map anything to put up?</summary>
    public bool Planned => _layout.Count > 0;

    /// <summary>The revels are on, or the town's still gathering for them: the games and the show table are open.</summary>
    public bool Open => Part is FestivalPart.Opening or FestivalPart.Revels;

    /// <summary>Set by the map: what goes up in the square when the festival comes.</summary>
    public void Plan(MapDef map)
    {
        _layout.Clear();
        foreach (var p in map.Festival)
            _layout.Add((p.ThingDef, new int3(p.At[0], p.At[1], map.StandZ(p.At[0], p.At[1])), p.Facing));
    }

    /// <summary>The first prop of a def still standing, or Entity.Null.</summary>
    public Entity Prop(Simulation sim, string def)
    {
        foreach (var e in _props)
            if (sim.World.IsAlive(e) && sim.World.Get<Thing>(e).Def.Name == def)
                return e;
        return Entity.Null;
    }

    /// <summary>The props of a def still standing.</summary>
    public IEnumerable<Entity> PropsOf(Simulation sim, string def)
    {
        foreach (var e in _props)
            if (sim.World.IsAlive(e) && sim.World.Get<Thing>(e).Def.Name == def)
                yield return e;
    }

    public const string BonfireDef = "Bonfire", ShowTableDef = "ShowTable";

    /// <summary>A prop of the festival's (put up for it), not the town's own.</summary>
    public bool IsProp(Entity e) => _props.Contains(e);

    /// <summary>The game played at a thing, if it's one of the festival's.</summary>
    public GameStanding? GameAt(Simulation sim, Entity thing)
    {
        if (!sim.World.IsAlive(thing) || !IsProp(thing))
            return null;
        var def = sim.World.Get<Thing>(thing).Def;
        return _games.Find(g => g.Game.ThingDef == def);
    }

    /// <summary>
    /// The festival begins (<see cref="RoundSystem"/>): the props go up, the bell rings. Called once.
    /// </summary>
    internal void Begin(Simulation sim, long at)
    {
        if (StartTick >= 0)
            return;
        StartTick = at;
        Part = FestivalPart.Opening;
        PutUp(sim);
        _games.Clear();
        foreach (var g in sim.Defs.Festival.Games)
            if (_props.Exists(e => sim.World.IsAlive(e) && sim.World.Get<Thing>(e).Def == g.ThingDef))
                _games.Add(new GameStanding { Game = g, NextAiTurn = StartTick + SimClock.HoursToTicks(sim.Defs.Festival.OpeningHours) });
        BellTick = sim.Clock.Tick;
    }

    /// <summary>Everything the map lists, where it goes (or the nearest free cell): lit, if it burns.</summary>
    private void PutUp(Simulation sim)
    {
        var w = sim.World;
        foreach (var (def, cell, facing) in _layout)
        {
            var at = Free(sim, def, cell, facing);
            if (at is not { } place)
                continue;
            var e = sim.SpawnStructure(def, place, facing);
            if (def.Building?.Fuel is { } fuel && w.Has<Fueled>(e))
                w.Get<Fueled>(e).Fuel = fuel.Capacity;
            _props.Add(e);
        }
        sim.Map.MarkStructureChanged();
    }

    /// <summary>The cell nearest <paramref name="cell"/> it fits on, with nobody standing there; null if none close.</summary>
    private static int3? Free(Simulation sim, ThingDef def, int3 cell, int facing)
    {
        var w = sim.World;
        foreach (var c in sim.SpiralFrom(cell, 2))
        {
            if (c.Z != cell.Z || !sim.CanPlaceStructure(c, def, facing) || !sim.Map.Standable(sim.Map.Idx(c)))
                continue;
            var taken = false;
            foreach (var f in sim.FootprintCells(def, c, facing))
                foreach (var e in sim.Creatures())
                    if (w.Get<Position>(e).Cell == f)
                        taken = true;
            if (!taken)
                return c;
        }
        return null;
    }

    public void Update(Simulation sim)
    {
        if (sim.Round.Phase != RoundPhase.Festival || StartTick < 0)
            return;
        var now = sim.Clock.Tick;
        var def = sim.Defs.Festival;
        if (Part == FestivalPart.Opening && now >= StartTick + SimClock.HoursToTicks(def.OpeningHours))
            Part = FestivalPart.Revels;
        if (Part != FestivalPart.Ceremony && now >= CeremonyTick(sim))
            BeginCeremony(sim);
        if (Open)
        {
            Admire(sim);
            AiTurns(sim);
        }
        if (Part == FestivalPart.Ceremony)
            RunCeremony(sim);
    }

    /// <summary>When the ceremony begins: its hours before the round's end, and not before the festival.</summary>
    public long CeremonyTick(Simulation sim) =>
        Math.Max(StartTick, sim.Round.EndTick(sim) - SimClock.HoursToTicks(sim.Defs.Festival.CeremonyHours));

    // ---------------- The show table ----------------

    /// <summary>
    /// Visitors near the show table look over what's on it, each piece once: what a town can make is
    /// part of what it's known for. The maker's praised for it (an honour, at the ceremony).
    /// </summary>
    private void Admire(Simulation sim)
    {
        var w = sim.World;
        var table = Prop(sim, ShowTableDef);
        if (table.IsNull)
            return;
        var pieces = sim.StoredIn(table);
        if (pieces.Count == 0)
            return;
        var def = sim.Defs.Festival;
        var cells = sim.FootprintCells(table).ToList();
        foreach (var who in sim.Creatures())
        {
            if (!w.Has<Adventurer>(who) || w.Has<Leaving>(who) || !SpeechSystem.CanAct(sim, who))
                continue;
            var at = w.Get<Position>(who).Cell;
            if (!cells.Exists(c => c.Z == at.Z && Maps.Map.Distance(c, at) <= def.AdmireReach))
                continue;
            foreach (var piece in pieces)
            {
                if (!w.TryGet<Crafted>(piece, out var made) || !_admired.Add((who, piece)))
                    continue;
                var liking = def.AdmireSatisfaction[(int)made.Quality];
                w.Get<Adventurer>(who).Satisfaction += liking;
                if (!made.Maker.IsNull)
                    _praise[made.Maker] = _praise.GetValueOrDefault(made.Maker) + liking;
                if (_rand.NextFloat() < 0.4f)
                    sim.Speech.Say(sim, who, Remark(made.Quality, w.Get<Thing>(piece).Label), Volume.Say);
                break; // one piece a look: they take their time over it
            }
        }
    }

    private static string Remark(Quality quality, string label) => quality switch
    {
        Quality.Masterwork => $"Now that {label} is something. Who made it?",
        Quality.Fine => $"Fine work, that {label}.",
        Quality.Plain => $"An honest {label}.",
        _ => $"Hm. Somebody's first {label}, is it?",
    };

    // ---------------- The games ----------------

    /// <summary>
    /// A turn at a game, by a player's hand game or the AI's roll: the best score so far leads, and
    /// the visitors watching enjoy it. 0 to 100.
    /// </summary>
    public void Played(Simulation sim, Entity who, FestivalGameDef game, int score)
    {
        var standing = _games.Find(g => g.Game == game);
        if (standing == null || !Open || !sim.World.TryGet<Creature>(who, out var c))
            return;
        score = Math.Clamp(score, 0, 100);
        standing.Turns++;
        if (score > standing.BestScore)
        {
            standing.Best = who;
            standing.BestName = c.Name;
            standing.BestScore = score;
        }
        var thing = Prop(sim, game.Def);
        if (thing.IsNull)
            return;
        var at = sim.World.Get<Thing>(thing).Cell;
        var def = sim.Defs.Festival;
        var w = sim.World;
        foreach (var e in sim.Creatures())
        {
            if (e == who || !w.Has<Adventurer>(e) || w.Has<Leaving>(e) || !SpeechSystem.CanAct(sim, e)
                || Maps.Map.Distance(w.Get<Position>(e).Cell, at) > def.WatchReach)
                continue;
            var had = _watched.GetValueOrDefault(e);
            var more = Math.Min(def.WatchSatisfaction, def.WatchCap - had);
            if (more <= 0)
                continue;
            _watched[e] = had + more;
            w.Get<Adventurer>(e).Satisfaction += more;
        }
    }

    /// <summary>What someone says of their own turn (the AI's; a player sees theirs in the game).</summary>
    public static string TurnLine(FestivalGameDef game, int score) => game.Name switch
    {
        "Striker" => score >= 90 ? "Ha! Rang the bell!" : score >= 60 ? "Nearly! Nearly had it." : "Bah. Mallet's crooked.",
        "Butts" => score >= 80 ? "Gold! Did you see that?" : score >= 50 ? "In the reds. Not bad." : "The wind took it, I swear.",
        _ => score >= 70 ? "Beat that!" : "Next time.",
    };

    /// <summary>Now and then, someone the AI runs who's watching a game has a go: their skill and some luck.</summary>
    private void AiTurns(Simulation sim)
    {
        if (Part != FestivalPart.Revels)
            return;
        var w = sim.World;
        var now = sim.Clock.Tick;
        foreach (var g in _games)
        {
            if (now < g.NextAiTurn)
                continue;
            g.NextAiTurn = now + SimClock.HoursToTicks(1f / 6f);
            if (_rand.NextFloat() >= sim.Defs.Festival.TryChance)
                continue;
            var thing = Prop(sim, g.Game.Def);
            if (thing.IsNull)
                continue;
            // Whoever's watching it (Ai.FestivalJob at this game), not a player.
            var watchers = new List<Entity>();
            foreach (var e in sim.Creatures())
                if (!w.Has<Avatar>(e) && w.Get<Mind>(e).Job is Ai.FestivalJob { Activity: FestivalActivity.Games } job && job.Focus == thing
                    && SpeechSystem.CanAct(sim, e))
                    watchers.Add(e);
            if (watchers.Count == 0)
                continue;
            var who = watchers[_rand.NextInt(watchers.Count)];
            var level = SkillRules.LevelOf(sim, who, g.Game.Skill);
            var score = (int)Math.Clamp(25f + level * 4.5f + _rand.Range(-20f, 20f), 0f, 100f);
            Played(sim, who, g.Game, score);
            sim.Speech.Say(sim, who, TurnLine(g.Game, score), Volume.Say);
        }
    }

    // ---------------- The ceremony ----------------

    private void BeginCeremony(Simulation sim)
    {
        Part = FestivalPart.Ceremony;
        var w = sim.World;
        var def = sim.Defs.Festival;
        // The games close: their winners take the purse, and travellers talk of a good night.
        foreach (var g in _games)
            if (!g.Best.IsNull && w.IsAlive(g.Best) && w.TryGet<Creature>(g.Best, out var c) && !c.Dead)
            {
                if (def.PrizeSilver > 0)
                    Inventory.Stow(sim, g.Best, sim.CreateItem(sim.Defs.Thing(Shops.Coin.Silver), def.PrizeSilver));
                sim.Reckoning.Add(sim, def.GameRenown, 0);
            }
        _parties.Clear();
        foreach (var e in sim.Creatures())
            if (w.TryGet<Adventurer>(e, out var a) && !w.Has<Leaving>(e) && !w.Get<Creature>(e).Dead && !_parties.Contains(a.Party))
                _parties.Add(a.Party);
        _parties.Sort();
        ChooseHonours(sim);
        Crier = RoundSystem.Crier(sim, sim.Round.Square ?? default);
        Enter(sim, CeremonyStep.Bell);
    }

    private void RunCeremony(Simulation sim)
    {
        var now = sim.Clock.Tick;
        // The verdict keeps its time: whatever's left of the toasts and honours gives way to it.
        var verdictAt = sim.Round.EndTick(sim) - SimClock.HoursToTicks(sim.Defs.Festival.VerdictMinutes / 60f);
        while (Step != CeremonyStep.Done && (now >= _stepUntil || (now >= verdictAt && Step < CeremonyStep.Verdict)))
        {
            if (now >= verdictAt && Step < CeremonyStep.Verdict)
            {
                Enter(sim, CeremonyStep.Verdict);
                continue;
            }
            switch (Step)
            {
                case CeremonyStep.Bell:
                    Enter(sim, CeremonyStep.Toasts);
                    break;
                case CeremonyStep.Toasts when _stepIndex + 1 < _parties.Count:
                    _stepIndex++;
                    Toast(sim);
                    break;
                case CeremonyStep.Toasts:
                    Enter(sim, CeremonyStep.Honours);
                    break;
                case CeremonyStep.Honours when _stepIndex + 1 < _honours.Count:
                    _stepIndex++;
                    CallHonour(sim);
                    break;
                case CeremonyStep.Honours:
                    Enter(sim, CeremonyStep.Accusation);
                    break;
                case CeremonyStep.Accusation:
                    Enter(sim, CeremonyStep.Verdict);
                    break;
                default:
                    Step = CeremonyStep.Done;
                    break;
            }
        }
    }

    private long Minutes(Simulation sim, float minutes) => sim.Clock.Tick + SimClock.HoursToTicks(minutes / 60f);

    private void Enter(Simulation sim, CeremonyStep step)
    {
        Step = step;
        _stepIndex = 0;
        var def = sim.Defs.Festival;
        switch (step)
        {
            case CeremonyStep.Bell:
                BellTick = sim.Clock.Tick;
                Say(sim, Crier, "Gather round, everyone! Round the well! Before the night's out, we raise our cups.");
                _stepUntil = Minutes(sim, def.BellMinutes);
                break;
            case CeremonyStep.Toasts:
                if (_parties.Count == 0)
                {
                    Say(sim, Crier, "No travellers left in town to drink with. Well. We'll drink to them anyway.");
                    _stepUntil = Minutes(sim, def.ToastMinutes / 2);
                    _stepIndex = int.MaxValue - 1;
                    break;
                }
                Say(sim, Crier, "Our guests first! Let's hear what they make of us.");
                Toast(sim);
                break;
            case CeremonyStep.Honours:
                if (_honours.Count == 0)
                {
                    _stepUntil = sim.Clock.Tick;
                    _stepIndex = int.MaxValue - 1;
                    break;
                }
                CallHonour(sim);
                break;
            case CeremonyStep.Accusation:
                // The traitors' hook (M4): the town may name one here, before the verdict. Nothing yet.
                _stepUntil = sim.Clock.Tick;
                break;
            case CeremonyStep.Verdict:
                if (!VerdictRead)
                {
                    sim.Reckoning.FinalCount(sim, _toasted);
                    sim.Round.ReadTheReckoning(sim);
                    VerdictRead = true;
                }
                _stepUntil = sim.Round.EndTick(sim);
                break;
        }
    }

    private static void Say(Simulation sim, Entity who, string text)
    {
        if (!who.IsNull && sim.World.IsAlive(who) && SpeechSystem.CanAct(sim, who))
            sim.Speech.Say(sim, who, text, Volume.Shout);
    }

    /// <summary>
    /// A party's toast: one of them stands and says what they make of the town, and what each of
    /// them thinks is counted now, toward its Renown (as leaving town would have).
    /// </summary>
    private void Toast(Simulation sim)
    {
        _stepUntil = Minutes(sim, sim.Defs.Festival.ToastMinutes);
        if (_stepIndex >= _parties.Count)
            return;
        var w = sim.World;
        var party = _parties[_stepIndex];
        var members = sim.Creatures()
            .Where(e => w.TryGet<Adventurer>(e, out var a) && a.Party == party && !w.Has<Leaving>(e) && !w.Get<Creature>(e).Dead && !_toasted.Contains(e))
            .ToList();
        if (members.Count == 0)
            return;
        var mean = members.Average(e => w.Get<Adventurer>(e).Satisfaction);
        var speaker = members.FirstOrDefault(e => SpeechSystem.CanAct(sim, e), Entity.Null);
        Say(sim, speaker, ToastLine(mean));
        foreach (var e in members)
        {
            sim.Reckoning.AdventurerLeft(sim, w.Get<Adventurer>(e).Satisfaction);
            _toasted.Add(e);
        }
    }

    /// <summary>What a party's toast says, from how they found the town.</summary>
    public static string ToastLine(float satisfaction) => satisfaction switch
    {
        >= 6f => "To this town! Best fare, best folk this side of the hills. We'll tell everyone we meet.",
        >= 3f => "To the town: you did right by us. We'll speak well of you on the road.",
        >= 0f => "To the town, I suppose. It served.",
        >= -3f => "We'll drink, but not to you. We've had better on a muddy road.",
        _ => "To the road out of here. We won't be back, and we'll say why.",
    };

    private void CallHonour(Simulation sim)
    {
        _stepUntil = Minutes(sim, sim.Defs.Festival.HonourMinutes);
        if (_stepIndex < _honours.Count)
            Say(sim, Crier, $"And for {_honours[_stepIndex].Title}: {_honours[_stepIndex].Name}!");
    }

    /// <summary>
    /// Who's called up, from what the whole town saw: the games' winners, the finest work on the show
    /// table (by what the visitors made of it), the busiest counter, and anyone called back from the dead.
    /// </summary>
    private void ChooseHonours(Simulation sim)
    {
        _honours.Clear();
        var w = sim.World;
        var max = sim.Defs.Festival.MaxHonours;
        string NameOf(Entity e) => w.TryGet<Creature>(e, out var c) ? c.Name : "";
        void Add(string title, Entity who)
        {
            if (_honours.Count < max && !who.IsNull && w.IsAlive(who) && NameOf(who) is { Length: > 0 } name)
                _honours.Add(new Honour(title, name, who));
        }
        foreach (var g in _games)
            if (!g.Best.IsNull)
                Add(g.Game.Honour, g.Best);
        var finest = _praise.Where(p => p.Value > 0 && w.IsAlive(p.Key)).OrderByDescending(p => p.Value).ThenBy(p => p.Key.Index).FirstOrDefault();
        if (!finest.Key.IsNull && finest.Value > 0)
            Add("the finest work on the show table", finest.Key);
        var town = sim.Creatures().Where(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human, Dead: false }).ToList();
        var busiest = town.Select(e => (Who: e, Earned: sim.Round.EarnedBy(sim, e))).Where(p => p.Earned > 0)
            .OrderByDescending(p => p.Earned).ThenBy(p => p.Who.Index).FirstOrDefault();
        if (busiest.Earned > 0)
            Add("the busiest counter in town", busiest.Who);
        foreach (var e in town)
            if (w.Has<Afterlife.Revived>(e))
                Add("coming back to us", e);
    }

    // ---------------- Saving ----------------

    public void Save(SaveWriter w, Simulation sim)
    {
        var (a, b, c, d) = _rand.State;
        w.UInt(a); w.UInt(b); w.UInt(c); w.UInt(d);
        w.Int(_layout.Count);
        foreach (var (def, cell, facing) in _layout)
        {
            w.Def(def);
            w.Int(cell.X); w.Int(cell.Y); w.Int(cell.Z);
            w.Int(facing);
        }
        w.Long(StartTick);
        if (StartTick < 0)
            return; // nothing more until the festival's begun
        w.Int((int)Part);
        w.Int((int)Step);
        w.Long(_stepUntil);
        w.Int(_stepIndex);
        w.Entity(Crier);
        w.Long(BellTick);
        w.Bool(VerdictRead);
        w.Int(_props.Count);
        foreach (var e in _props)
            w.Entity(e);
        w.Int(_games.Count);
        foreach (var g in _games)
        {
            w.String(g.Game.Name);
            w.Entity(g.Best);
            w.String(g.BestName);
            w.Int(g.BestScore);
            w.Int(g.Turns);
            w.Long(g.NextAiTurn);
        }
        Entities(w, _toasted.OrderBy(e => e.Index));
        w.Int(_admired.Count);
        foreach (var (who, piece) in _admired.OrderBy(p => p.Who.Index).ThenBy(p => p.Piece.Index))
        {
            w.Entity(who);
            w.Entity(piece);
        }
        Floats(w, _watched);
        Floats(w, _praise);
        w.Int(_parties.Count);
        foreach (var p in _parties)
            w.Int(p);
        w.Int(_honours.Count);
        foreach (var h in _honours)
        {
            w.String(h.Title);
            w.String(h.Name);
            w.Entity(h.Who);
        }
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _rand.State = (r.UInt(), r.UInt(), r.UInt(), r.UInt());
        _layout.Clear();
        for (var n = r.Count(10_000); n > 0; n--)
            _layout.Add((r.Def<ThingDef>(), new int3(r.Int(), r.Int(), r.Int()), r.Int()));
        StartTick = r.Long();
        _props.Clear();
        _games.Clear();
        _toasted.Clear();
        _admired.Clear();
        _watched.Clear();
        _praise.Clear();
        _parties.Clear();
        _honours.Clear();
        if (StartTick < 0)
            return;
        Part = (FestivalPart)r.Int();
        Step = (CeremonyStep)r.Int();
        _stepUntil = r.Long();
        _stepIndex = r.Int();
        Crier = r.Entity();
        BellTick = r.Long();
        VerdictRead = r.Bool();
        for (var n = r.Count(10_000); n > 0; n--)
            _props.Add(r.Entity());
        for (var n = r.Count(100); n > 0; n--)
        {
            var name = r.String();
            var standing = new GameStanding { Game = sim.Defs.Festival.Games.Find(g => g.Name == name) ?? new FestivalGameDef { Name = name } };
            standing.Best = r.Entity();
            standing.BestName = r.String();
            standing.BestScore = r.Int();
            standing.Turns = r.Int();
            standing.NextAiTurn = r.Long();
            if (standing.Game.ThingDef != null!)
                _games.Add(standing);
        }
        for (var n = r.Count(100_000); n > 0; n--)
            _toasted.Add(r.Entity());
        for (var n = r.Count(1_000_000); n > 0; n--)
            _admired.Add((r.Entity(), r.Entity()));
        ReadFloats(r, _watched);
        ReadFloats(r, _praise);
        for (var n = r.Count(100_000); n > 0; n--)
            _parties.Add(r.Int());
        for (var n = r.Count(100); n > 0; n--)
            _honours.Add(new Honour(r.String(), r.String(), r.Entity()));
    }

    private static void Entities(SaveWriter w, IEnumerable<Entity> list)
    {
        var all = list.ToList();
        w.Int(all.Count);
        foreach (var e in all)
            w.Entity(e);
    }

    private static void Floats(SaveWriter w, Dictionary<Entity, float> map)
    {
        w.Int(map.Count);
        foreach (var (e, f) in map.OrderBy(p => p.Key.Index))
        {
            w.Entity(e);
            w.Float(f);
        }
    }

    private static void ReadFloats(SaveReader r, Dictionary<Entity, float> map)
    {
        for (var n = r.Count(100_000); n > 0; n--)
            map[r.Entity()] = r.Float();
    }
}
