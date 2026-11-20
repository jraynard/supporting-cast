using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Modding;

/// <summary>What a trade's work does to a brawler in the inn's brawl (<see cref="ITradeModule.Soothe"/>), weakest first.</summary>
public enum Soothing : byte
{
    None,

    /// <summary>Eased: the heat comes down for them.</summary>
    Calmed,

    /// <summary>Wound up further (a sour tune): the heat goes up for them.</summary>
    Riled,

    /// <summary>Out of it altogether (dancing to a reel instead).</summary>
    Settled,
}

/// <summary>
/// A trade's sim half, as the core sees it: hooks it calls at fixed points of the day (docs/MODDING.md,
/// "A new trade"). Every hook has a default that does nothing, so a module answers only what's its own,
/// and the core never names a trade. Registered with <see cref="SimRegistry.Trade"/>; modules are asked
/// in registration order (the game's, then each mod's in load order), the first to answer wins, so the
/// order is the same on every peer.
/// </summary>
public interface ITradeModule
{
    /// <summary>The trade this module plays (its name in trades.json), or null for one that serves several.</summary>
    string? Trade { get; }

    // ---------------- At a station ----------------

    /// <summary>Whether a player's work at this station on this recipe is this trade's hand game.</summary>
    bool AppliesAt(Simulation sim, Entity worker, Entity station, RecipeDef recipe) => false;

    /// <summary>Start the hand game <see cref="AppliesAt"/> said yes to.</summary>
    void WorkAt(Simulation sim, Entity worker, Entity station, RecipeDef recipe) { }

    /// <summary>Whether mending this piece at this station is this trade's hand game.</summary>
    bool AppliesToRepair(Simulation sim, Entity worker, Entity station, Entity item) => false;

    void BeginMending(Simulation sim, Entity worker, Entity station, Entity item) { }

    /// <summary>Products made the ordinary way (a bill worked by the AI, not played): mark them as the trade's.</summary>
    void Made(Simulation sim, List<Entity> made, Entity maker) { }

    // ---------------- The AI at work ----------------

    /// <summary>
    /// A job for an AI villager doing <paramref name="type"/> work, asked after the core's own jobs of
    /// that type and before its generic crafting: null if this trade has none for them now.
    /// </summary>
    Job? JobFor(Simulation sim, Entity pawn, WorkType type) => null;

    // ---------------- A player's F ----------------

    /// <summary>
    /// What F does at a cell before most else is asked (after serving and taking orders): null if
    /// nothing of this trade's. Return an action with <see cref="AvatarAction.By"/> set to this module,
    /// and <see cref="Perform"/> is called with it.
    /// </summary>
    AvatarAction? EarlyActionAt(Simulation sim, Entity avatar, int3 cell, int3 here) => null;

    /// <summary>What F does at the thing on a cell (a tree, a log), asked before harvesting it. Null if nothing of this trade's.</summary>
    AvatarAction? SiteActionAt(Simulation sim, Entity avatar, Entity site, int3 cell) => null;

    /// <summary>A thing this trade has in hand that nobody else may work (a tree coming down): it isn't harvested.</summary>
    bool Busy(Simulation sim, Entity site) => false;

    /// <summary>Do an action this module offered (see <see cref="EarlyActionAt"/>).</summary>
    void Perform(Simulation sim, Entity avatar, AvatarAction action) { }

    // ---------------- The town ----------------

    /// <summary>Stock a building if it's this trade's workplace (its chest, its stations): as a round's town is set up, or one is rebuilt.</summary>
    void Stock(Simulation sim, Entity building) { }

    /// <summary>A piece of furniture lost (burned away, smashed in a brawl): its thing is already gone.</summary>
    void Lost(Simulation sim, ThingDef def, int3 cell, byte facing) { }

    /// <summary>
    /// What this trade's work is doing to a brawler in the inn's brawl right now (<c>Hospitality.BrawlCrisis</c>):
    /// the musician's tune calming them, riling them, or turning the fight into a dance. Asked every so often.
    /// </summary>
    Soothing Soothe(Simulation sim, Entity brawler) => Soothing.None;

    /// <summary>A customer sent to the practice yard for this trade's player: make them want what the trade sells.</summary>
    void PracticeCustomer(Simulation sim, Entity player, Entity customer, int index) { }

    /// <summary>A lesson of this trade's is set up in the practice yard (after its stock and customers): anything of its own it needs (a lute put out of tune).</summary>
    void SetUpLesson(Simulation sim, Entity player, LessonDef lesson) { }

    /// <summary>The kinds of work for two this trade leads, to be played as the helper alone (<c>--help-as</c>: the carpenter's "pitman").</summary>
    IEnumerable<string> PairsKinds => [];

    /// <summary>One of <see cref="PairsKinds"/> set out, <paramref name="lead"/> (a bot) at it and <paramref name="helper"/> beside it; what was staged, or null.</summary>
    string? PairsLead(Simulation sim, string kind, Entity lead, Entity helper) => null;

    /// <summary>The practice yard's "every job for two" drill (<c>--pairs-test</c>): set out this trade's part.</summary>
    IEnumerable<string> PairsDrill(Simulation sim) => [];
}

/// <summary>The trade modules registered, in order, and the core's ways of asking them.</summary>
public static class Trades
{
    /// <summary>
    /// The modules of this simulation: the game's own, then each mod's, in load order, as they were when
    /// it was made. A simulation keeps its own (a mod's hooks read that mod's systems, which only a
    /// simulation made with the mod has), so the content changing later can't reach into it.
    /// </summary>
    public static IReadOnlyList<ITradeModule> All(Simulation sim) => sim.TradeModules;

    public static ITradeModule? Of(Simulation sim, string trade) => All(sim).FirstOrDefault(m => m.Trade == trade);

    public static ITradeModule? WorkingAt(Simulation sim, Entity worker, Entity station, RecipeDef recipe) =>
        All(sim).FirstOrDefault(m => m.AppliesAt(sim, worker, station, recipe));

    public static ITradeModule? MendingAt(Simulation sim, Entity worker, Entity station, Entity item) =>
        All(sim).FirstOrDefault(m => m.AppliesToRepair(sim, worker, station, item));

    public static void Made(Simulation sim, List<Entity> made, Entity maker)
    {
        foreach (var m in All(sim))
            m.Made(sim, made, maker);
    }

    public static Job? JobFor(Simulation sim, Entity pawn, WorkType type)
    {
        foreach (var m in All(sim))
            if (m.JobFor(sim, pawn, type) is { } job)
                return job;
        return null;
    }

    public static AvatarAction? EarlyActionAt(Simulation sim, Entity avatar, int3 cell, int3 here)
    {
        foreach (var m in All(sim))
            if (m.EarlyActionAt(sim, avatar, cell, here) is { } a)
                return a with { By = m };
        return null;
    }

    public static AvatarAction? SiteActionAt(Simulation sim, Entity avatar, Entity site, int3 cell)
    {
        foreach (var m in All(sim))
            if (m.SiteActionAt(sim, avatar, site, cell) is { } a)
                return a with { By = m };
        return null;
    }

    public static bool Busy(Simulation sim, Entity site) => All(sim).Any(m => m.Busy(sim, site));

    /// <summary>Every town building offered to every trade, to stock what's its own.</summary>
    public static void StockTown(Simulation sim)
    {
        foreach (var building in sim.World.Query<PresetBuilding>().OrderBy(e => e.Index))
            Stock(sim, building);
    }

    public static void Stock(Simulation sim, Entity building)
    {
        foreach (var m in All(sim))
            m.Stock(sim, building);
    }

    public static void Lost(Simulation sim, ThingDef def, int3 cell, byte facing)
    {
        foreach (var m in All(sim))
            m.Lost(sim, def, cell, facing);
    }

    /// <summary>What every trade's work is doing to a brawler, strongest first: settled, then riled, then calmed.</summary>
    public static Soothing Soothe(Simulation sim, Entity brawler)
    {
        var best = Soothing.None;
        foreach (var m in All(sim))
            if (m.Soothe(sim, brawler) is var s && s > best)
                best = s;
        return best;
    }
}
