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
}

/// <summary>A player waiting in the lobby: who they'll be, and what they'd like to do.</summary>
public sealed class LobbySeat
{
    /// <summary><see cref="Guid.Empty"/> for single player, whose character belongs to no player id.</summary>
    public Guid Player { get; init; }

    public string Name = "";
    public Appearance? Look;
    public List<string> Traits = [];
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

    public string Name => "Round";
    public int Interval => SimClock.TicksPerDay;

    public RoundPhase Phase { get; private set; }

    /// <summary>Who's in the lobby, in the order they sat down.</summary>
    public IReadOnlyList<LobbySeat> Seats => _seats;

    public void Update(Simulation sim) { }

    public LobbySeat? SeatOf(Guid player) => _seats.Find(s => s.Player == player);

    /// <summary>Open the lobby of a village that has no round yet.</summary>
    public void OpenLobby()
    {
        if (Phase == RoundPhase.None)
            Phase = RoundPhase.Lobby;
    }

    /// <summary>A player takes a seat, or changes who they'll be. Their preferences stay.</summary>
    public void Sit(Guid player, string name, Appearance? look, IReadOnlyList<string>? traits, IReadOnlyList<string>? clothes)
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
        seat.Traits = traits?.Take(TraitRules.MaxTraits * 2).ToList() ?? [];
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
            if (!dealt.ContainsKey(seat) && open.Find(t => !seat.Never.Contains(t)) is { } any)
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
        var dealt = Deal(sim.Defs);
        var free = OpenVillagers(sim);
        foreach (var (seat, trade) in dealt)
        {
            Entity body;
            if (free.Count > 0)
            {
                body = free[0];
                free.RemoveAt(0);
            }
            else
            {
                body = Players.SpawnNewcomer(sim, seat.Name);
            }
            Players.Inhabit(sim, seat.Player, body);
            Players.TakeOnLook(sim, body, seat.Name, seat.Look, seat.Traits, seat.Clothes);
            Assign(sim, body, trade);
            PlaceAtWork(sim, body, trade);
        }

        var open = sim.Defs.AllTrades.Where(t => !t.Unlimited && !dealt.Exists(d => d.Trade == t)).ToList();
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
        Phase = RoundPhase.Playing;
    }

    /// <summary>
    /// A late joiner takes over an AI villager, keeping that villager's name and look so the town
    /// sees nothing change. Their wanted trades come first, then named trades over the unlimited
    /// one. Characters whose players left aren't open: those players can still come back.
    /// </summary>
    public Entity TakeTrade(Simulation sim, Guid player, IReadOnlyList<TradeDef>? wanted, IReadOnlyList<TradeDef>? never)
    {
        if (Phase != RoundPhase.Playing || !Players.CharacterOf(sim, player).IsNull)
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
        if (trade?.WorkplaceDef is not { } def)
            return;
        var w = sim.World;
        foreach (var b in w.Query<PresetBuilding>())
        {
            var building = w.Get<PresetBuilding>(b);
            if (building.Def != def || building.Phase != BuildPhase.Done || building.Demolishing)
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

        if (w.TryGet<Skills>(e, out var skills))
            foreach (var (skill, least) in trade.Skills)
                skills.Level[(int)skill] = Math.Max(skills.Level[(int)skill], least);
        foreach (var station in trade.KnownStations)
            foreach (var recipe in station.Recipes)
                RecipeBooks.Learn(sim, e, recipe);
        // Tools of the trade go on the belt: they work from there, and an AI villager's work pack
        // is emptied at the end of every job.
        foreach (var item in trade.Kit)
        {
            var made = sim.CreateItem(item.ThingDef, item.Count, Simulation.MaterialFor(item.ThingDef, null));
            if (item.Count != 1 || item.ThingDef.Tool == null || Toolbelt.PutOn(sim, e, made).IsNull)
                Inventory.Stow(sim, e, made);
        }
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
            WriteStrings(w, seat.Traits);
            w.Bool(seat.Clothes != null);
            if (seat.Clothes != null)
                WriteStrings(w, seat.Clothes);
            WriteTrades(w, seat.Wanted);
            WriteTrades(w, seat.Never);
            w.Bool(seat.Ready);
        }
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
            seat.Traits = ReadStrings(r);
            seat.Clothes = r.Bool() ? ReadStrings(r) : null;
            seat.Wanted = ReadTrades(r);
            seat.Never = ReadTrades(r);
            seat.Ready = r.Bool();
            _seats.Add(seat);
        }
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
