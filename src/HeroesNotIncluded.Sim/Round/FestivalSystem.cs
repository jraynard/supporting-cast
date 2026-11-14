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

    /// <summary>The bell's rung: down tools, to the square, and the crier tells the season.</summary>
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
/// The season's last evening, made an event: the props go up in the square, the crier tells the
/// season's story at the well and says what's on, the town and its visitors feast and dance and
/// look over the show table and try the games (the crier calling them to each now and then), then
/// the ceremony gives each party still in town its toast (counted there, aloud, so the town watches
/// its Renown move), calls the honours and reads the verdict. Everything said aloud is given time
/// to be read (<see cref="SpeechSystem.ReadingTicks"/>), and the round ends once the verdict's sunk in. <see cref="RoundSystem"/> owns the
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

    /// <summary>The crier's tale of the season, built when it starts; the next line to say; when.</summary>
    private readonly List<string> _tale = [];
    private int _taleIndex = -1;
    private long _nextLineAt;

    private long _nextCall;
    private int _callIndex;

    /// <summary>When the revels began; -1 before.</summary>
    public long RevelsTick { get; private set; } = -1;

    /// <summary>When the ceremony's done with, the verdict read and sunk in; -1 till then.</summary>
    public long DoneTick { get; private set; } = -1;

    /// <summary>The crier's telling the season at the well (the opening, after the gathering).</summary>
    public bool Telling => Part == FestivalPart.Opening && _taleIndex >= 0;

    /// <summary>The ceremony's said its piece and the verdict's had time to sink in: the round can end.</summary>
    public bool Finished(Simulation sim) => VerdictRead && DoneTick >= 0 && sim.Clock.Tick >= DoneTick;

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
                _games.Add(new GameStanding { Game = g });
        BellTick = sim.Clock.Tick;
        Crier = RoundSystem.Crier(sim, sim.Round.Square ?? default);
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
        LayTheFeast(sim);
    }

    /// <summary>The feast's dishes laid along each of its tables: the town's spread, on view, there to be helped to by hand.</summary>
    private void LayTheFeast(Simulation sim)
    {
        foreach (var table in PropsOf(sim, sim.Defs.Festival.FeastTable).ToList())
            foreach (var dish in sim.Defs.Festival.Feast)
                if (sim.IsContainer(table) && sim.HasRoomFor(table, dish.ThingDef, null))
                    sim.Store(table, sim.CreateItem(dish.ThingDef, dish.Count));
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
        if (Part != FestivalPart.Ceremony && now >= CeremonyTick(sim))
            BeginCeremony(sim);
        if (Part == FestivalPart.Opening)
            TellTheSeason(sim);
        if (Open)
        {
            Admire(sim);
            AiTurns(sim);
        }
        if (Part == FestivalPart.Revels)
            CallToTheRevels(sim);
        if (Part == FestivalPart.Ceremony)
            RunCeremony(sim);
    }

    /// <summary>Ticks a line's given before the next: its reading time and a beat.</summary>
    public static long LineTicks(Simulation sim, string text) =>
        SpeechSystem.ReadingTicks(text) + (long)(sim.Defs.Festival.BeatSeconds * SimClock.TicksPerSecond);

    /// <summary>Whoever's crying the festival, chosen again if they can't speak now.</summary>
    private Entity CrierNow(Simulation sim)
    {
        if (Crier.IsNull || !sim.World.IsAlive(Crier) || !SpeechSystem.CanAct(sim, Crier))
            Crier = RoundSystem.Crier(sim, sim.Round.Square ?? default);
        return Crier;
    }

    // ---------------- The opening: the season, told ----------------

    /// <summary>
    /// Once the town's had time to gather, the crier tells the season at the well, a line at a
    /// time with room to read each, then says what's on: and the revels begin.
    /// </summary>
    private void TellTheSeason(Simulation sim)
    {
        var now = sim.Clock.Tick;
        if (_taleIndex < 0)
        {
            var gathered = StartTick + SimClock.HoursToTicks(sim.Defs.Festival.GatherMinutes / 60f);
            if (now < gathered)
                return;
            if (now < gathered + SimClock.HoursToTicks(sim.Defs.Festival.TaleWaitMinutes / 60f) && !PlayersInEarshot(sim))
                return;
            _tale.Clear();
            _tale.AddRange(Tale(sim));
            _taleIndex = 0;
            _nextLineAt = now;
        }
        if (now < _nextLineAt)
            return;
        if (_taleIndex < _tale.Count)
        {
            var line = _tale[_taleIndex++];
            Say(sim, CrierNow(sim), line);
            _nextLineAt = now + LineTicks(sim, line);
            return;
        }
        BeginRevels(sim);
    }

    /// <summary>
    /// Every player steering a character (an <see cref="Avatar"/>) who's up and about would hear the crier plainly from where they
    /// stand, so nobody slow to the square misses the start. Those asleep, downed or dead aren't
    /// waited for. True with no players at all.
    /// </summary>
    public bool PlayersInEarshot(Simulation sim)
    {
        var w = sim.World;
        var crier = CrierNow(sim);
        if (!w.IsAlive(crier) || !w.Has<Position>(crier))
            return true;
        var from = w.Get<Position>(crier).Cell;
        foreach (var e in w.Each<Avatar>())
            if (SpeechSystem.CanAct(sim, e)
                && SpeechSystem.Hear(sim, from, w.Get<Position>(e).Cell, Volume.Shout) != Clarity.Clear)
                return false;
        return true;
    }

    /// <summary>The lines of the tale said so far, in order: the festival card keeps them for anyone who missed one. It's all public.</summary>
    public IEnumerable<string> Told => _tale.Take(Math.Clamp(_taleIndex, 0, _tale.Count));

    /// <summary>The tale's been held past the gathering for someone still on their way.</summary>
    public bool WaitingForPlayers(Simulation sim) =>
        Part == FestivalPart.Opening && _taleIndex < 0
        && sim.Clock.Tick >= StartTick + SimClock.HoursToTicks(sim.Defs.Festival.GatherMinutes / 60f);

    private void BeginRevels(Simulation sim)
    {
        Part = FestivalPart.Revels;
        RevelsTick = sim.Clock.Tick;
        foreach (var g in _games)
            g.NextAiTurn = RevelsTick;
        _nextCall = RevelsTick + SimClock.HoursToTicks(sim.Defs.Festival.CallMinutes / 60f);
        var what = new List<string>();
        foreach (var g in _games)
            what.Add(g.Game.Label);
        if (!Prop(sim, BonfireDef).IsNull)
            what.Add("the dance round the bonfire");
        if (PropsOf(sim, "Stool").Any())
            what.Add("the feast tables");
        var show = Prop(sim, ShowTableDef).IsNull ? "" : ", and the show table for your best work";
        // Everyone hears it, wherever they are: the square's loud tonight.
        sim.Messages.Add(MessageKind.Good, $"The revels are open: {string.Join(", ", what)}{show}. The toasts come later.", null);
    }

    /// <summary>The season, as the crier tells it: who came, how their quests went, the crises, the town's own losses.</summary>
    public List<string> Tale(Simulation sim)
    {
        var w = sim.World;
        var ex = sim.Expeditions;
        var lines = new List<string> { "Friends! Neighbours! Gather close. Before we eat, let's remember the season we've had." };

        var parties = sim.Parties.PartiesSent;
        lines.Add(parties switch
        {
            0 => "Hardly a traveller found their way to us this season.",
            1 => "One band of adventurers came through our gates this season.",
            _ => $"{Cap(Num(parties))} bands of adventurers came through our gates this season.",
        });

        int cleared = ex.Succeeded, failed = ex.Failed, lost = ex.Dead;
        var quests = cleared + failed;
        var gone = lost > 0 ? $" We lost {Num(lost)} of them out there." : "";
        if (quests > 0)
            lines.Add((cleared, failed) switch
            {
                (_, 0) => $"They set out on {Num(quests)} {Plural(quests, "quest")}, and came back in triumph every time!{gone}",
                (0, _) => $"They set out on {Num(quests)} {Plural(quests, "quest")}, and not one went well.{gone}",
                _ => $"They set out on {Num(quests)} quests: {Num(cleared)} came back in triumph, {Num(failed)} came to grief.{gone}",
            });
        else if (parties > 0)
            lines.Add("None of them has come back from the hills with a tale to tell. Not yet.");

        var crises = sim.Reckoning.Crises;
        if (crises.Count == 0)
            lines.Add("It was a quiet season, for the valley. We'll take it.");
        else
        {
            foreach (var (kind, met) in crises.Take(3))
                lines.Add(CrisisLine(kind, met));
            if (crises.Count > 3)
                lines.Add($"And more trouble besides: {Num(crises.Count)} times it came, and {Num(crises.Count(c => c.Met))} times we met it.");
        }

        var town = sim.Creatures().Where(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human }).ToList();
        var dead = town.Where(e => w.Get<Creature>(e).Dead).Select(e => w.Get<Creature>(e).Name).ToList();
        if (dead.Count > 0)
            lines.Add($"We lost {Names(dead)} this season. Raise a cup to them tonight.");
        var back = town.Where(e => !w.Get<Creature>(e).Dead && w.Has<Afterlife.Revived>(e)).Select(e => w.Get<Creature>(e).Name).ToList();
        if (back.Count > 0)
            lines.Add($"And {Names(back)} came back to us from beyond. Welcome home!");

        lines.Add("Our guests will toast us at the end of the evening, and tell us what they made of the town.");
        lines.Add(WhatsOn(sim));
        return lines;
    }

    /// <summary>The crier's last word before the revels: everything there is to do, and where.</summary>
    private string WhatsOn(Simulation sim)
    {
        var bits = new List<string>();
        if (!Prop(sim, BonfireDef).IsNull)
            bits.Add("dance round the bonfire");
        foreach (var g in _games)
            bits.Add(g.Game.Name switch
            {
                "Striker" => "ring the bell at the striker",
                "Butts" => "shoot for the gold at the butts",
                _ => $"try {g.Game.Label}",
            });
        if (!Prop(sim, ShowTableDef).IsNull)
            bits.Add("lay your best work on the show table for our guests to see");
        return bits.Count == 0 ? "Now: eat, drink, and be glad!" : $"Now: eat and drink, {Names(bits)}!";
    }

    private static string CrisisLine(CrisisKind kind, bool met) => (kind, met) switch
    {
        (CrisisKind.Raid, true) => "When raiders came down the road, we drove them off.",
        (CrisisKind.Raid, false) => "When raiders came down the road, they had the better of us.",
        (CrisisKind.Fire, true) => "When fire took hold, we beat it out.",
        (CrisisKind.Fire, false) => "When fire took hold, it burned what it would.",
        (CrisisKind.Shortage, true) => "When the stores ran low, we made do.",
        (CrisisKind.Shortage, false) => "When the stores ran low, we went hungry.",
        (CrisisKind.Plague, true) => "When the fever came, we nursed each other through it.",
        (CrisisKind.Plague, false) => "When the fever came, it took its toll.",
        (CrisisKind.Dragon, true) => "And the dragon! We stood up to a dragon, friends!",
        (CrisisKind.Dragon, false) => "And the dragon... we'll not speak of the dragon.",
        _ => met ? "Trouble came, and we met it." : "Trouble came, and it got the better of us.",
    };

    private static readonly string[] Numbers = ["no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve"];

    private static string Num(int n) => n >= 0 && n < Numbers.Length ? Numbers[n] : n.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string Plural(int n, string word) => n == 1 ? word : word + "s";

    /// <summary>"a", "a and b", "a, b and c".</summary>
    private static string Names(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
    };

    // ---------------- The revels: the crier's calls ----------------

    /// <summary>Now and then the crier calls the crowd to something: a game, the dance, the show table, the tables.</summary>
    private void CallToTheRevels(Simulation sim)
    {
        var now = sim.Clock.Tick;
        if (now < _nextCall)
            return;
        _nextCall = now + SimClock.HoursToTicks(sim.Defs.Festival.CallMinutes / 60f);
        var calls = new List<string>();
        foreach (var g in _games)
            calls.Add(g.Game.Name switch
            {
                "Striker" => "Who'll try the striker? Ring the bell and the purse is yours!",
                "Butts" => "Three arrows at the butts! Nearest the gold takes the purse!",
                _ => $"Who'll try {g.Game.Label}? There's a purse for the best!",
            });
        if (!Prop(sim, BonfireDef).IsNull)
            calls.Add("Come and dance, all of you! Round the bonfire!");
        if (!Prop(sim, ShowTableDef).IsNull)
            calls.Add("Have you seen the show table? Lay out your best work, friends: our guests are looking!");
        if (calls.Count == 0)
            return;
        Say(sim, CrierNow(sim), calls[_callIndex++ % calls.Count]);
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
                // The first look at a piece is always remarked on, so its maker hears how it's taken.
                var first = !_admired.Any(p => p.Piece == piece && p.Who != who);
                if (first || _rand.NextFloat() < 0.4f)
                    sim.Speech.Say(sim, who, Remark(made.Quality, w.Get<Thing>(piece).Label), Volume.Say);
                if (first && w.TryGet<Creature>(made.Maker, out var maker))
                    sim.Messages.Add(liking > 0 ? MessageKind.Good : MessageKind.Info,
                        $"A visitor looks over {maker.Name}'s {w.Get<Thing>(piece).Label} on the show table: {(liking > 0 ? "they like it" : "they're not taken with it")}.",
                        cells[0]);
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
        var thing = Prop(sim, game.Def);
        if (score > standing.BestScore)
        {
            var was = standing.Best;
            standing.Best = who;
            standing.BestName = c.Name;
            standing.BestScore = score;
            // A new leader's news: whoever's watching hears of it (a player's own turn tells them itself).
            if (was != who && !sim.World.Has<Avatar>(who))
                sim.Messages.Add(MessageKind.Info, $"{c.Name} leads at {game.Label} with {score} of 100.",
                    thing.IsNull ? null : sim.World.Get<Thing>(thing).Cell);
        }
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
        CrierNow(sim);
        Enter(sim, CeremonyStep.Bell);
    }

    private void RunCeremony(Simulation sim)
    {
        var now = sim.Clock.Tick;
        // The verdict keeps its time: whatever's left of the toasts and honours gives way to it.
        var verdictAt = sim.Round.EndTick(sim) - (long)(sim.Defs.Festival.VerdictSeconds * SimClock.TicksPerSecond);
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
                case CeremonyStep.Toasts when _stepIndex + 1 < _parties.Count && _stepIndex >= -1:
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

    /// <summary>The crier says a line, and the step holds for it to be read.</summary>
    private void Announce(Simulation sim, string line)
    {
        Say(sim, CrierNow(sim), line);
        _stepUntil = sim.Clock.Tick + LineTicks(sim, line);
    }

    private void Enter(Simulation sim, CeremonyStep step)
    {
        Step = step;
        _stepIndex = 0;
        switch (step)
        {
            case CeremonyStep.Bell:
                BellTick = sim.Clock.Tick;
                // A few seconds more than the line: time to come in from the revels.
                Announce(sim, "Gather round, everyone! Round the well! Before the night's out, we raise our cups.");
                _stepUntil += SimClock.TicksPerSecond * 4;
                break;
            case CeremonyStep.Toasts:
                if (_parties.Count == 0)
                {
                    Announce(sim, "No travellers left in town to drink with. Well. We'll drink to them anyway.");
                    _stepIndex = int.MaxValue - 1;
                    break;
                }
                // The crier's word, then each party's toast in turn (from index 0).
                Announce(sim, "Our guests first! Let's hear what they make of us.");
                _stepIndex = -1;
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
                    var said = sim.Round.ReadTheReckoning(sim);
                    VerdictRead = true;
                    // Read it, let it sink in, then the reveal.
                    DoneTick = sim.Clock.Tick + SpeechSystem.ReadingTicks(said) + (long)(sim.Defs.Festival.VerdictPauseSeconds * SimClock.TicksPerSecond);
                }
                _stepUntil = DoneTick;
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
        _stepUntil = sim.Clock.Tick;
        if (_stepIndex < 0 || _stepIndex >= _parties.Count)
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
        var line = ToastLine(mean);
        Say(sim, speaker, line);
        _stepUntil = sim.Clock.Tick + LineTicks(sim, line);
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
        _stepUntil = sim.Clock.Tick;
        if (_stepIndex < _honours.Count)
            Announce(sim, $"And for {_honours[_stepIndex].Title}: {_honours[_stepIndex].Name}!");
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
        w.Int(_tale.Count);
        foreach (var line in _tale)
            w.String(line);
        w.Int(_taleIndex);
        w.Long(_nextLineAt);
        w.Long(_nextCall);
        w.Int(_callIndex);
        w.Long(RevelsTick);
        w.Long(DoneTick);
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
        _tale.Clear();
        (_taleIndex, _nextLineAt, _nextCall, _callIndex, RevelsTick, DoneTick) = (-1, 0, 0, 0, -1, -1);
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
        if (r.Version < 31)
        {
            // Before the tale: a festival saved in its opening goes straight on to the revels.
            if (Part == FestivalPart.Opening)
                _taleIndex = 0;
            if (Part != FestivalPart.Opening)
                RevelsTick = StartTick;
            return;
        }
        for (var n = r.Count(1000); n > 0; n--)
            _tale.Add(r.String());
        _taleIndex = r.Int();
        _nextLineAt = r.Long();
        _nextCall = r.Long();
        _callIndex = r.Int();
        RevelsTick = r.Long();
        DoneTick = r.Long();
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

