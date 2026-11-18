using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Round;

/// <summary>Where a village is in a round. <see cref="None"/> is a village with no round at all: tests, demos, the bench.</summary>
public enum RoundPhase : byte
{
    None,
    Lobby,
    Playing,

    /// <summary>The season's last evening: no new parties or quests, the town gathers, and then the reckoning.</summary>
    Festival,

    /// <summary>The round has ended (<see cref="RoundSystem.Summary"/>). The session stands still, as in the lobby.</summary>
    Over,
}

/// <summary>A player waiting in the lobby: who they'll be, and what they'd like to do.</summary>
public sealed class LobbySeat
{
    /// <summary><see cref="Guid.Empty"/> for single player, whose character belongs to no player id.</summary>
    public Guid Player { get; init; }

    public string Name = "";
    public Appearance? Look;
    public List<string>? Clothes;

    /// <summary>Trades this player wants, best first.</summary>
    public List<TradeDef> Wanted = [];

    /// <summary>Trades this player won't be dealt.</summary>
    public List<TradeDef> Never = [];

    public bool Ready;
}

/// <summary>
/// The round's lobby and its deal. The lobby lives in the sim, with the session paused, so joining
/// it is an ordinary drop-in and every peer deals the same hand from the same state. Players
/// inhabit AI villagers rather than arriving beside them: the cast stays fixed, and nobody can
/// tell from the street who just became a person.
/// </summary>
public sealed class RoundSystem(ulong seed) : ISimSystem, ISaveState
{
    /// <summary>More seats than any session can fill; a limit on what a bad command can grow.</summary>
    public const int MaxSeats = 32;

    /// <summary>More pieces than anyone can wear; a limit on what's kept from a command.</summary>
    private const int MaxClothes = 16;

    private readonly SimRandom _rand = new(seed ^ 0x7EADE5EEDUL);
    private readonly List<LobbySeat> _seats = [];

    private readonly List<(Entity Who, int Coin)> _startCoin = [];

    public string Name => "Round";
    public int Interval => SimClock.TicksPerHour / 12;

    public RoundPhase Phase { get; private set; }

    /// <summary>A round is under way: playing, or at its festival. What happens only in a round checks this.</summary>
    public bool Live => Phase is RoundPhase.Playing or RoundPhase.Festival;

    /// <summary>The tick the trades were dealt; -1 before.</summary>
    public long StartTick { get; private set; } = -1;

    /// <summary>
    /// Keeps the round going past its season: no festival, no end. For tests and tools that run a
    /// round for days to watch something else. Not saved.
    /// </summary>
    public bool Endless { get; set; }

    /// <summary>
    /// The practice yard (<see cref="Scenarios.Practice"/>): a round with no season to it. No
    /// festival or end, no Renown or Dread, no party unless sent for. Saved.
    /// </summary>
    public bool Practice { get; set; }

    /// <summary>How the round's played (<see cref="Modding.IGameMode"/>), by name: the season unless the lobby chose otherwise. Saved.</summary>
    public string Mode { get; private set; } = Modding.SeasonMode.Id;

    /// <summary>The round's mode (the season if its mod's off).</summary>
    public Modding.IGameMode ModeOf(Simulation sim) => Modding.GameModes.Of(Mode);

    /// <summary>Choose how the round's played; only while the lobby's open.</summary>
    public void SetMode(string mode)
    {
        if (Phase == RoundPhase.Lobby && Modding.GameModes.All.Any(m => m.Name == mode))
            Mode = mode;
    }

    /// <summary>The host's choices for the round (<see cref="SetRoundOptionsCommand"/>). Saved.</summary>
    public RoundOptions Options { get; set; } = RoundOptions.Default;

    /// <summary>
    /// The town's blows hurt the town. Always, outside a round (tests, demos, the arena, the bench
    /// play as they always have); in one, as the host chose.
    /// </summary>
    public bool FriendlyFire => !Live || Options.FriendlyFire;

    /// <summary>The host sets the round's options; only while the lobby's open. Traitors stay off until M4.</summary>
    public void SetOptions(RoundOptions options)
    {
        if (Phase == RoundPhase.Lobby)
            Options = options with { Traitors = false };
    }

    /// <summary>Where the festival gathers (the well in the square, or the town's gathering place), once it's begun.</summary>
    public int3? Square { get; private set; }

    /// <summary>How the round came out, once it's <see cref="RoundPhase.Over"/>.</summary>
    public RoundSummary? Summary { get; private set; }

    public RoundOutcome Outcome => Summary?.Outcome ?? RoundOutcome.None;

    /// <summary>Who's in the lobby, in the order they sat down.</summary>
    public IReadOnlyList<LobbySeat> Seats => _seats;

    /// <summary>
    /// The tick the festival starts: <see cref="SeasonDef.FestivalHour"/> on the season's last day,
    /// counting the day of the deal as the first. -1 before the deal.
    /// </summary>
    public long FestivalTick(Simulation sim) =>
        StartTick < 0 ? -1 : Math.Max(StartTick, TickAt(sim, Math.Max(1, sim.Defs.Season.Days), sim.Defs.Season.FestivalHour));

    /// <summary>
    /// The tick the festival starts going up (<see cref="SeasonDef.FestivalSetupHour"/> on the
    /// last day): never before the deal, never after the festival. -1 before the deal.
    /// </summary>
    public long SetupTick(Simulation sim) =>
        StartTick < 0 ? -1 : Math.Clamp(TickAt(sim, Math.Max(1, sim.Defs.Season.Days), sim.Defs.Season.FestivalSetupHour), StartTick, FestivalTick(sim));

    /// <summary>The tick at <paramref name="hour"/> on the season's <paramref name="day"/> (1 is the day of the deal). -1 before the deal.</summary>
    public long TickAt(Simulation sim, int day, float hour)
    {
        if (StartTick < 0)
            return -1;
        var startHour = sim.Clock.StartHour * (long)SimClock.TicksPerHour;
        var firstDay = (StartTick + startHour) / SimClock.TicksPerDay;
        return (firstDay + day - 1) * SimClock.TicksPerDay + SimClock.HoursToTicks(hour) - startHour;
    }

    /// <summary>
    /// The tick the festival ends and the town is reckoned: its hours after it began (or is to
    /// begin; a dev's jump starts it early). -1 before the deal.
    /// </summary>
    public long EndTick(Simulation sim) =>
        StartTick < 0 ? -1 : (sim.Festival.StartTick >= 0 ? sim.Festival.StartTick : FestivalTick(sim)) + SimClock.HoursToTicks(sim.Defs.Season.FestivalHours);

    /// <summary>The day of the season it is (1 on the day of the deal), and how many there are.</summary>
    public (int Day, int Of) SeasonDay(Simulation sim)
    {
        var first = StartTick < 0 ? sim.Clock.Day : (int)((StartTick + sim.Clock.StartHour * (long)SimClock.TicksPerHour) / SimClock.TicksPerDay) + 1;
        return (sim.Clock.Day - first + 1, Math.Max(1, sim.Defs.Season.Days));
    }

    public void Update(Simulation sim)
    {
        if (!Live || Endless || Practice)
            return;
        var mode = ModeOf(sim);
        if (mode.Check(sim) is { } ends)
        {
            End(sim, ends.Outcome, ends.Headline);
            return;
        }
        if (!mode.Seasonal)
        {
            if (!AnyoneAlive(sim))
                End(sim, RoundOutcome.TownFell);
            return;
        }
        var now = sim.Clock.Tick;
        if (Phase == RoundPhase.Playing && now >= FestivalTick(sim))
            BeginFestival(sim, FestivalTick(sim));
        // The festival ends on its clock, or sooner once the ceremony's said its piece and the verdict's sunk in.
        if (Phase == RoundPhase.Festival && (now >= EndTick(sim) || sim.Festival.Finished(sim)))
        {
            var reckoning = sim.Reckoning;
            // The ceremony reads the verdict before the end; if it never got that far, it's read now.
            if (!sim.Festival.VerdictRead)
            {
                reckoning.FinalCount(sim, sim.Festival.Toasted);
                ReadTheReckoning(sim);
            }
            End(sim, reckoning.Renown >= Director.Reckoning.RenownTarget(sim) ? RoundOutcome.Won : RoundOutcome.FellShort);
            return;
        }
        if (!AnyoneAlive(sim))
            End(sim, RoundOutcome.TownFell);
    }

    /// <summary>
    /// The festival begins (on the clock, or now for a dev): the square's found, the props go up
    /// (<see cref="FestivalSystem"/>) and everyone's called to it. Only from play.
    /// </summary>
    /// <param name="at">When it began: on the clock, its hour (even if the clock jumped past it); for a dev, now.</param>
    public void BeginFestival(Simulation sim, long? at = null)
    {
        if (Phase != RoundPhase.Playing)
            return;
        Phase = RoundPhase.Festival;
        Square = FindSquare(sim);
        sim.Festival.Begin(sim, Math.Min(at ?? sim.Clock.Tick, sim.Clock.Tick));
        // Everyone hears the bell, wherever they are.
        var to = GatheringPlace(sim) is { } place ? place : "the well in the square";
        sim.Messages.Add(MessageKind.Good, $"The festival bell! Everyone to {to}: the crier tells the season, then the feast, the dance and the games.", null, "The festival");
    }

    /// <summary>
    /// End the round: build the reveal and stand the session still. Only once; a round that isn't
    /// live has nothing to end.
    /// </summary>
    public void End(Simulation sim, RoundOutcome outcome, string? headline = null)
    {
        if (!Live || outcome == RoundOutcome.None)
            return;
        var reckoning = sim.Reckoning;
        var ex = sim.Expeditions;
        Summary = new RoundSummary
        {
            Outcome = outcome, EndedTick = sim.Clock.Tick,
            Renown = reckoning.Renown, Dread = reckoning.Dread,
            RenownTarget = Director.Reckoning.RenownTarget(sim), DreadLimit = Director.Reckoning.DreadLimit(sim),
            RenownByHour = reckoning.RenownByHour.ToList(), DreadByHour = reckoning.DreadByHour.ToList(),
            Parties = sim.Parties.PartiesSent, AdventurersServed = reckoning.AdventurersCounted,
            QuestsCleared = ex.Succeeded, QuestsFailed = ex.Failed, AdventurersLost = ex.Dead + reckoning.AdventurersDiedInTown,
            People = RoundSummary.PeopleOf(sim, _startCoin),
            Honours = sim.Festival.Honours.Select(h => new HonourRow(h.Name, h.Title)).ToList(),
            Quests = [.. ex.Tales],
            Told = headline,
        };
        Phase = RoundPhase.Over;
        sim.Messages.Add(outcome == RoundOutcome.Won ? MessageKind.Good : MessageKind.Danger, Summary.Headline, null, outcome == RoundOutcome.Won ? "The season is won" : "The season is lost");
    }

    /// <summary>
    /// Where a festival gathers: the town's gathering place if it has one (<see cref="Defs.BuildingProps.Gathering"/>:
    /// a lake town's mast on its boats), else its well; failing both, wherever the first villager stands.
    /// </summary>
    public static int3? FindSquare(Simulation sim)
    {
        var w = sim.World;
        int3? well = null;
        foreach (var e in w.Query<Thing>())
        {
            var t = w.Get<Thing>(e);
            if (t.Def.Building?.Gathering != null)
                return t.Cell;
            if (well == null && t.Def.Name == "Well")
                well = t.Cell;
        }
        if (well != null)
            return well;
        foreach (var e in sim.Creatures())
            if (w.Get<Creature>(e).Faction == Faction.Village)
                return w.Get<Position>(e).Cell;
        return null;
    }

    /// <summary>
    /// The town's own gathering place for its festival, in words ("the boats in the harbour"), or
    /// null for the well in the square (<see cref="Defs.BuildingProps.Gathering"/>).
    /// </summary>
    public static string? GatheringPlace(Simulation sim)
    {
        if ((sim.Round.Square ?? FindSquare(sim)) is { } at && sim.Map.InBounds(at) && sim.Map.Structure[sim.Map.Idx(at)] is { IsNull: false } e
            && sim.World.Get<Thing>(e).Def.Building?.Gathering is { } words)
            return words;
        return null;
    }

    /// <summary>Whoever of the town stands nearest <paramref name="at"/> and can speak: the festival's crier. Entity.Null if nobody.</summary>
    public static Entity Crier(Simulation sim, int3 at)
    {
        var w = sim.World;
        // One the town runs, if there's any: a player shouldn't find their character making speeches.
        return sim.Creatures()
            .Where(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human, Dead: false } && Speech.SpeechSystem.CanAct(sim, e))
            .OrderBy(e => w.Has<PlayerCharacter>(e))
            .ThenBy(e => Maps.Map.Distance(w.Get<Position>(e).Cell, at))
            .ThenBy(e => e.Index)
            .FirstOrDefault(Entity.Null);
    }

    /// <summary>Silver someone's earned since the deal (what they hold now, less what they started with). 0 for anyone not dealt in.</summary>
    public int EarnedBy(Simulation sim, Entity who)
    {
        foreach (var (e, coin) in _startCoin)
            if (e == who)
                return Shops.Coin.Count(sim, e) - coin;
        return 0;
    }

    /// <summary>
    /// The festival's end: the ceremony's crier (else whoever of the town stands nearest the well)
    /// calls out how the season went, in the words everyone knows it by. The numbers are for the reveal.
    /// </summary>
    /// <returns>What was said; empty if nobody could say it.</returns>
    internal string ReadTheReckoning(Simulation sim)
    {
        var crier = sim.Festival.Crier;
        if (crier.IsNull || !sim.World.IsAlive(crier) || !Speech.SpeechSystem.CanAct(sim, crier))
            crier = Crier(sim, Square ?? default);
        if (crier.IsNull)
            return "";
        var r = sim.Reckoning;
        var renown = Director.Reckoning.RenownWord(r.Renown, Director.Reckoning.RenownTarget(sim));
        var dread = Director.Reckoning.DreadWord(r.Dread, Director.Reckoning.DreadLimit(sim));
        var verdict = r.Renown >= Director.Reckoning.RenownTarget(sim) ? "We've done it, friends: the season's ours!" : "It wasn't enough, friends. Not this season.";
        var line = $"Friends! The season's over. {renown}, and {char.ToLowerInvariant(dread[0])}{dread[1..]}. {verdict}";
        sim.Speech.Say(sim, crier, line, Speech.Volume.Shout);
        return line;
    }

    /// <summary>Is anyone of the village still alive? Someone down may yet get up, or be got up.</summary>
    private static bool AnyoneAlive(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in sim.Creatures())
            if (w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human, Dead: false })
                return true;
        return false;
    }

    public LobbySeat? SeatOf(Guid player) => _seats.Find(s => s.Player == player);

    /// <summary>Open the lobby of a village that has no round yet.</summary>
    public void OpenLobby()
    {
        if (Phase == RoundPhase.None)
            Phase = RoundPhase.Lobby;
    }

    /// <summary>A player takes a seat, or changes who they'll be. Their preferences stay.</summary>
    public void Sit(Guid player, string name, Appearance? look, IReadOnlyList<string>? clothes)
    {
        if (Phase != RoundPhase.Lobby)
            return;
        var seat = SeatOf(player);
        if (seat == null)
        {
            if (_seats.Count >= MaxSeats)
                return;
            _seats.Add(seat = new LobbySeat { Player = player });
        }
        seat.Name = Players.Clean(name);
        seat.Look = look is { } a ? Players.Valid(a) : null;
        seat.Clothes = clothes?.Take(MaxClothes).ToList();
    }

    public void Stand(Guid player)
    {
        if (Phase == RoundPhase.Lobby)
            _seats.RemoveAll(s => s.Player == player);
    }

    public void SetPreferences(Guid player, IReadOnlyList<TradeDef>? wanted, IReadOnlyList<TradeDef>? never)
    {
        if (Phase != RoundPhase.Lobby || SeatOf(player) is not { } seat)
            return;
        (seat.Wanted, seat.Never) = CleanPreferences(wanted, never);
    }

    public void SetReady(Guid player, bool ready)
    {
        if (Phase == RoundPhase.Lobby && SeatOf(player) is { } seat)
            seat.Ready = ready;
    }

    // Off the network: entries may be null or repeated. "Never" wins a conflict, and an unlimited
    // trade can't be refused, because it's what's left when everything else is.
    private static (List<TradeDef> Wanted, List<TradeDef> Never) CleanPreferences(IReadOnlyList<TradeDef?>? wanted, IReadOnlyList<TradeDef?>? never)
    {
        var no = (never ?? []).OfType<TradeDef>().Where(t => !t.Unlimited).Distinct().Take(64).ToList();
        var yes = (wanted ?? []).OfType<TradeDef>().Where(t => !no.Contains(t)).Distinct().Take(64).ToList();
        return (yes, no);
    }

    /// <summary>
    /// Who gets what. Seats are shuffled, then everyone gets a shot at their first choice before
    /// anyone gets a shot at their second. Whoever is left takes an open trade they didn't refuse,
    /// or the unlimited trade.
    /// </summary>
    internal List<(LobbySeat Seat, TradeDef? Trade)> Deal(DefDatabase defs)
    {
        var order = _seats.ToList();
        for (var i = order.Count - 1; i > 0; i--)
        {
            var j = _rand.NextInt(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var open = defs.AllTrades.Where(t => !t.Unlimited).ToList();
        var dealt = new Dictionary<LobbySeat, TradeDef>();
        void Give(LobbySeat seat, TradeDef trade)
        {
            dealt[seat] = trade;
            open.Remove(trade);
        }

        var ranks = order.Count == 0 ? 0 : order.Max(s => s.Wanted.Count);
        for (var rank = 0; rank < ranks; rank++)
            foreach (var seat in order)
                if (!dealt.ContainsKey(seat) && rank < seat.Wanted.Count)
                {
                    var want = seat.Wanted[rank];
                    if (open.Contains(want) || want.Unlimited)
                        Give(seat, want);
                }

        foreach (var seat in order)
            if (!dealt.ContainsKey(seat) && open.Find(t => !seat.Never.Contains(t) && !t.PlayerOnly) is { } any)
                Give(seat, any);

        var fallback = defs.AllTrades.Find(t => t.Unlimited);
        return order.Select(s => (s, dealt.TryGetValue(s, out var t) ? t : fallback)).ToList();
    }

    /// <summary>
    /// Deal the trades and start. Each seat takes over an AI villager (more arrive if players
    /// outnumber them), and the trades nobody drew go to the villagers still run by the AI.
    /// </summary>
    public void Start(Simulation sim)
    {
        if (Phase != RoundPhase.Lobby || _seats.Count == 0)
            return;
        var mode = ModeOf(sim);
        // A mode that starts later in the day (a siege at dusk): the clock's moved on to it before anyone's placed.
        if (mode.StartHour is { } hour && sim.Clock.HourOfDay < hour)
            sim.Clock.Tick += SimClock.HoursToTicks(hour - sim.Clock.HourOfDay);
        var dealt = Deal(sim.Defs);
        var free = OpenVillagers(sim);
        foreach (var (seat, trade) in dealt)
        {
            Entity body;
            if (trade is { PlayerOnly: true })
                body = Players.SpawnNewcomer(sim, seat.Name); // the musician comes to town; the villagers stay who they are
            else if (free.Count > 0)
            {
                body = free[0];
                free.RemoveAt(0);
            }
            else
            {
                body = Players.SpawnNewcomer(sim, seat.Name);
            }
            Players.Inhabit(sim, seat.Player, body);
            Players.TakeOnLook(sim, body, seat.Name, seat.Look, seat.Clothes);
            Assign(sim, body, trade);
            PlaceAtWork(sim, body, trade);
        }

        var open = sim.Defs.AllTrades.Where(t => !t.Unlimited && !t.PlayerOnly && !dealt.Exists(d => d.Trade == t)).ToList();
        var fallback = sim.Defs.AllTrades.Find(t => t.Unlimited);
        foreach (var villager in free)
        {
            var trade = open.Count > 0 ? open[0] : fallback;
            if (open.Count > 0)
                open.RemoveAt(0);
            Assign(sim, villager, trade);
            PlaceAtWork(sim, villager, trade);
        }
        _seats.Clear();
        Homes.DealAll(sim);
        Homes.StockCrocks(sim);
        Phase = RoundPhase.Playing;
        StartTick = sim.Clock.Tick;
        sim.Messages.Add(MessageKind.Good, "Doors open. Smile.", null); // the brand's round start
        _startCoin.Clear();
        var purse = sim.Defs.Season.StartingPurse;
        foreach (var e in sim.Creatures())
            if (sim.World.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human })
            {
                if (purse > 0)
                    Inventory.Stow(sim, e, sim.CreateItem(sim.Defs.Thing(Shops.Coin.Silver), purse));
                _startCoin.Add((e, Shops.Coin.Count(sim, e)));
            }
        mode.Started(sim);
    }

    /// <summary>
    /// A late joiner takes over an AI villager, keeping that villager's name and look so the town
    /// sees nothing change. Their wanted trades come first, then named trades over the unlimited
    /// one. Characters whose players left aren't open: those players can still come back.
    /// </summary>
    public Entity TakeTrade(Simulation sim, Guid player, IReadOnlyList<TradeDef>? wanted, IReadOnlyList<TradeDef>? never)
    {
        // The dead don't come back as someone else: they're ghosts until they're brought back.
        if (!Live || !Players.CharacterOf(sim, player).IsNull || !Afterlife.Ghosts.Of(sim, player).IsNull)
            return Entity.Null;
        var (yes, no) = CleanPreferences(wanted, never);
        var w = sim.World;
        var free = OpenVillagers(sim);
        TradeDef? Held(Entity e) => w.TryGet<Trade>(e, out var t) ? t.Def : null;

        // FirstOrDefault with Entity.Null: a default Entity is index 0, a real one.
        var body = Entity.Null;
        foreach (var want in yes)
        {
            body = free.FirstOrDefault(e => Held(e) == want, Entity.Null);
            if (!body.IsNull)
                break;
        }
        if (body.IsNull)
            body = free.FirstOrDefault(e => Held(e) is { Unlimited: false } t && !no.Contains(t), Entity.Null);
        if (body.IsNull)
            body = free.FirstOrDefault(e => Held(e) is null or { Unlimited: true }, Entity.Null);
        if (body.IsNull)
        {
            body = Players.SpawnNewcomer(sim, "Wanderer");
            Assign(sim, body, sim.Defs.AllTrades.Find(t => t.Unlimited));
        }
        Players.Inhabit(sim, player, body);
        Homes.Give(sim, body);
        return body;
    }

    /// <summary>Villagers no player has a claim on, in a stable order.</summary>
    private static List<Entity> OpenVillagers(Simulation sim)
    {
        var w = sim.World;
        return sim.Creatures()
            .Where(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human, Downed: false }
                && !w.Has<PlayerCharacter>(e) && !w.Has<Avatar>(e) && !w.Has<Carried>(e))
            .ToList();
    }

    /// <summary>
    /// The round opens with everyone at their trade's building, so each player sees at once where
    /// they work (and the AI starts there too). On a free floor cell inside it, nearest its middle;
    /// left where they are if the town has no such building.
    /// </summary>
    public static void PlaceAtWork(Simulation sim, Entity e, TradeDef? trade)
    {
        if (trade is not { HasWorkplace: true })
            return;
        var w = sim.World;
        foreach (var b in w.Query<PresetBuilding>())
        {
            var building = w.Get<PresetBuilding>(b);
            if (!trade.WorksAt(building.Def) || building.Phase != BuildPhase.Done || building.Demolishing)
                continue;
            foreach (var c in sim.SpiralFrom(building.Anchor, 6))
            {
                if (Buildings.At(sim, c) != b || !sim.Map.Passable(c) || !sim.Map.Standable(sim.Map.Idx(c))
                    || sim.Creatures().Any(o => o != e && w.Get<Position>(o).Cell == c))
                    continue;
                AiSystem.EndJob(sim, e, w.Get<Mind>(e), false);
                w.Get<Position>(e) = Position.At(c);
                return;
            }
        }
    }

    /// <summary>
    /// Whether someone may dig, mine, chop or forage: anyone outside a round, and in one only a
    /// trade that gathers (<see cref="TradeDef.Gathers"/>). Players and the AI alike.
    /// </summary>
    public static bool MayGather(Simulation sim, Entity who) =>
        sim.Round.Phase == RoundPhase.None || (sim.World.TryGet<Trade>(who, out var t) && t.Def.Gathers);

    /// <summary>Whether someone may fell trees: whoever may gather, and in a round a trade that fells (<see cref="TradeDef.Fells"/>).</summary>
    public static bool MayFell(Simulation sim, Entity who) =>
        MayGather(sim, who) || (sim.World.TryGet<Trade>(who, out var t) && t.Def.Fells);

    /// <summary>The work only a gathering trade does in a round.</summary>
    public static readonly WorkType[] GatheringWork = [WorkType.Chop, WorkType.Mine, WorkType.Harvest];

    internal static void Assign(Simulation sim, Entity e, TradeDef? trade)
    {
        if (trade == null)
            return;
        var w = sim.World;
        if (w.Has<Trade>(e))
            w.Remove<Trade>(e);
        w.Add(e, new Trade { Def = trade });
        var mind = w.Get<Mind>(e);
        mind.WorkPriority = Mind.DefaultPriorities();
        foreach (var (work, priority) in trade.WorkPriorities)
            mind.WorkPriority[(int)work] = priority;
        if (!trade.Gathers)
        {
            foreach (var work in GatheringWork)
                mind.WorkPriority[(int)work] = 0;
            Toolbelt.TakeStartingTools(sim, e); // the stone axe and pick are the labourer's
        }

        if (w.TryGet<Skills>(e, out var skills))
            foreach (var (skill, least) in trade.Skills)
                skills.Level[(int)skill] = Math.Max(skills.Level[(int)skill], least);
        foreach (var station in trade.KnownStations)
            foreach (var recipe in station.Recipes)
                RecipeBooks.Learn(sim, e, recipe);
        // The trade's own weapon goes in hand (the smith's hammer too: it works from there as from
        // the belt); tools of the trade go on the belt: they work from there, and an AI villager's
        // work pack is emptied at the end of every job.
        foreach (var item in trade.Kit)
        {
            var made = sim.CreateItem(item.ThingDef, item.Count, Simulation.MaterialFor(item.ThingDef, null));
            if (item.Count == 1 && item.ThingDef.Weapon != null && !(w.TryGet<Equipment>(e, out var eq) && w.IsAlive(eq.Weapon)))
                sim.EquipItem(e, made);
            else if (item.Count != 1 || item.ThingDef.Tool == null || Toolbelt.PutOn(sim, e, made).IsNull)
            {
                // An AI villager's pack is emptied after every job, which would leave the kit's stock
                // lying in the street: it goes in their workplace's storage instead. A player keeps it.
                if (!w.Has<PlayerCharacter>(e) && WorkplaceStore(sim, trade, made) is { IsNull: false } store)
                    made = sim.Store(store, made);
                if (!made.IsNull)
                    Inventory.Stow(sim, e, made);
            }
        }
        Militia.Stashes.Fill(sim, trade);
        if (trade.HoldsKeys)
            Tinkering.Locksmithing.GiveWorkplaceKeys(sim, e, trade);
        if (w.Has<Mana>(e))
            w.Remove<Mana>(e);
        if (w.Has<Spellbook>(e))
            w.Remove<Spellbook>(e);
        if (trade.Mana > 0)
        {
            w.Add(e, new Mana { Current = trade.Mana, Max = trade.Mana });
            w.Add(e, Healing.Weaving.NewBook(sim));
        }
    }

    /// <summary>A container in the trade's workplace (not a stash) with room for the item, or Entity.Null.</summary>
    private static Entity WorkplaceStore(Simulation sim, TradeDef trade, Entity item)
    {
        if (Shops.ShopSystem.WorkplaceOf(sim, trade) is not { IsNull: false } building)
            return Entity.Null;
        var w = sim.World;
        var t = w.Get<Thing>(item);
        foreach (var e in w.Query<PartOf>())
            if (w.Get<PartOf>(e).Building == building && sim.IsContainer(e) && !Militia.Stashes.IsStash(sim, e)
                && sim.HasRoomFor(e, t.Def, t.Material))
                return e;
        return Entity.Null;
    }

    public void Save(SaveWriter w, Simulation sim)
    {
        var (a, b, c, d) = _rand.State;
        w.UInt(a); w.UInt(b); w.UInt(c); w.UInt(d);
        w.Int((int)Phase);
        w.Int(_seats.Count);
        foreach (var seat in _seats)
        {
            w.String(seat.Player.ToString("N"));
            w.String(seat.Name);
            w.Bool(seat.Look != null);
            if (seat.Look is { } look)
                ComponentCodecs.For(typeof(Appearance))!.WriteValue(w, look);
            w.Bool(seat.Clothes != null);
            if (seat.Clothes != null)
                WriteStrings(w, seat.Clothes);
            WriteTrades(w, seat.Wanted);
            WriteTrades(w, seat.Never);
            w.Bool(seat.Ready);
        }
        w.Long(StartTick);
        w.Int(_startCoin.Count);
        foreach (var (e, coin) in _startCoin)
        {
            w.Entity(e);
            w.Int(coin);
        }
        w.Bool(Summary != null);
        Summary?.Save(w);
        w.Bool(Square != null);
        if (Square is { } sq)
        {
            w.Int(sq.X);
            w.Int(sq.Y);
            w.Int(sq.Z);
        }
        w.Bool(Practice);
        w.Bool(Options.FriendlyFire);
        w.Bool(Options.Traitors);
        w.String(Mode);
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _rand.State = (r.UInt(), r.UInt(), r.UInt(), r.UInt());
        Phase = (RoundPhase)r.Int();
        _seats.Clear();
        for (var n = r.Count(MaxSeats); n > 0; n--)
        {
            var seat = new LobbySeat { Player = Guid.ParseExact(r.String(), "N"), Name = r.String() };
            if (r.Bool())
                seat.Look = (Appearance)ComponentCodecs.For(typeof(Appearance))!.ReadValue(r);
            if (r.Version < 53)
                ReadStrings(r); // traits, from before they were taken out
            seat.Clothes = r.Bool() ? ReadStrings(r) : null;
            seat.Wanted = ReadTrades(r);
            seat.Never = ReadTrades(r);
            seat.Ready = r.Bool();
            _seats.Add(seat);
        }
        _startCoin.Clear();
        Practice = false;
        Options = new RoundOptions(FriendlyFire: true, Traitors: false); // saves from before options were played with it on
        Summary = null;
        StartTick = -1;
        Square = null;
        if (r.Version < 9)
        {
            // Saves from before the season: the round started when the game did, and nobody's purse was noted.
            if (Phase == RoundPhase.Playing)
                StartTick = 0;
            return;
        }
        StartTick = r.Long();
        for (var n = r.Count(MaxSeats * 4); n > 0; n--)
            _startCoin.Add((r.Entity(), r.Int()));
        if (r.Bool())
            Summary = RoundSummary.Read(r);
        Square = r.Version >= 10 && r.Bool() ? new int3(r.Int(), r.Int(), r.Int()) : null;
        Practice = r.Version >= 28 && r.Bool();
        if (r.Version >= 38)
            Options = new RoundOptions(r.Bool(), r.Bool());
        Mode = r.Version >= 63 ? r.String() : Modding.SeasonMode.Id;
    }

    private static void WriteStrings(SaveWriter w, List<string> list)
    {
        w.Int(list.Count);
        foreach (var s in list)
            w.String(s);
    }

    private static List<string> ReadStrings(SaveReader r)
    {
        var list = new List<string>();
        for (var n = r.Count(1000); n > 0; n--)
            list.Add(r.String());
        return list;
    }

    private static void WriteTrades(SaveWriter w, List<TradeDef> list)
    {
        w.Int(list.Count);
        foreach (var t in list)
            w.Def(t);
    }

    private static List<TradeDef> ReadTrades(SaveReader r)
    {
        var list = new List<TradeDef>();
        for (var n = r.Count(1000); n > 0; n--)
            list.Add(r.Def<TradeDef>());
        return list;
    }
}
