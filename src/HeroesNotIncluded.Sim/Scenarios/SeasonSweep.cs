using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>
/// A whole round played by bots, headless, from the deal to the reveal: the season as the numbers
/// in <c>season.json</c> make it, before anyone's hands are on it. <c>SeasonScenarioTests</c> plays
/// one; <c>--season-sweep</c> plays a run of seeds, the baseline the tuning playtest's season pass
/// is measured against.
/// </summary>
public static class SeasonSweep
{
    /// <summary>Every trade with a game of its own: the bots' table (an AI labourer runs the errands).</summary>
    public static readonly string[] Trades = ["Innkeeper", "Cook", "Blacksmith", "Healer", "Tinker", "Carpenter"];

    /// <param name="botSeed">The first bot's seed (the rest follow it); by default derived from <paramref name="seed"/>.</param>
    /// <param name="map">The town it's played in (data/defs/maps/): Millbrook unless told otherwise.</param>
    public static SeasonResult Play(DefDatabase defs, ulong seed, string difficulty = "Normal", IReadOnlyList<string>? trades = null, ulong? botSeed = null,
        string map = VillageRound.DefaultMap)
    {
        trades ??= Trades;
        var sim = VillageRound.NewSimulation(defs, seed, map);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim, map);
        sim.Director.Enable(sim.Defs.Difficulty(difficulty));
        sim.Round.OpenLobby();
        var bots = new List<BotPlayer>();
        var first = botSeed ?? seed * 16 + 100;
        for (var i = 0; i < trades.Count; i++)
        {
            var id = new Guid(i + 1, 0, 0, new byte[8]);
            sim.ApplyNow(new JoinLobbyCommand(id, $"Bot {i + 1}", default));
            sim.ApplyNow(new SetTradePreferencesCommand(id, [sim.Defs.Trade(trades[i])], []));
            bots.Add(new BotPlayer(() => sim, id, c => sim.ApplyNow(c), first + (ulong)i));
        }
        sim.ApplyNow(new StartRoundCommand());

        var result = new SeasonResult { Seed = seed, Sim = sim };
        var dead = new HashSet<Entity>();
        var limit = sim.Round.EndTick(sim) + SimClock.TicksPerHour;
        // The casks, watched from outside (nothing in the sim keeps count): a fall is servings drawn, a rise a keg knocked in.
        var casks = sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e).Def.Building?.Cask != null).ToList();
        var left = casks.Select(c => Work.PourSession.ServingsLeft(sim, c)).ToArray();
        while (sim.Round.Phase != RoundPhase.Over && sim.Clock.Tick < limit)
        {
            sim.Tick();
            foreach (var bot in bots)
                bot.Tick();
            result.Festival |= sim.Round.Phase == RoundPhase.Festival;
            if (sim.Clock.Tick % 600 == 0)
                foreach (var b in bots)
                {
                    var words = b.Doing.Split(' ');
                    var key = words[0] + " " + (words.ElementAtOrDefault(1) ?? "");
                    result.Doing[key] = result.Doing.GetValueOrDefault(key) + 1;
                }
            if (sim.Clock.Tick % 60 == 0)
                foreach (var e in sim.World.Query<Avatar>())
                    if (sim.World.Get<Creature>(e).Dead && dead.Add(e))
                        result.Deaths.Add($"[{sim.Clock.TimeString()}] {sim.World.Get<Creature>(e).Name}: "
                            + string.Join(" | ", sim.Messages.Messages.TakeLast(6).Select(m => m.Text)));
            foreach (var c in sim.Director.Active)
                if (c.Started == sim.Clock.Tick)
                    result.Crises.Add(c.Kind);
            if (sim.Clock.Tick % 60 == 0)
                for (var i = 0; i < casks.Count; i++)
                {
                    if (!sim.World.IsAlive(casks[i]))
                        continue; // burned: that's not drinking
                    var now = Work.PourSession.ServingsLeft(sim, casks[i]);
                    if (now < left[i])
                        result.Drawn += left[i] - now;
                    else if (now > left[i])
                        result.KegsTapped++;
                    left[i] = now;
                }
        }
        for (var i = 0; i < trades.Count; i++)
            result.Bots[trades[i]] = bots[i].Stats;
        result.Summary = sim.Round.Summary;
        result.Hours = sim.Clock.Tick / (float)SimClock.TicksPerHour;
        return result;
    }
}

/// <summary>How one bot-played season came out.</summary>
public sealed class SeasonResult
{
    public ulong Seed;

    /// <summary>The round, left as it ended, for tests to look into.</summary>
    public required Simulation Sim;

    /// <summary>The reveal; null if the round didn't end in time.</summary>
    public RoundSummary? Summary;

    public bool Festival;
    public float Hours;

    /// <summary>The crises as they began, in order.</summary>
    public List<CrisisKind> Crises = [];

    /// <summary>Players' characters who died, each with the messages leading up to it.</summary>
    public List<string> Deaths = [];

    /// <summary>What the bots were doing, sampled every 10 s.</summary>
    public Dictionary<string, int> Doing = [];

    /// <summary>Servings drawn from the inn's casks (poured or mixed), and new kegs knocked in.</summary>
    public int Drawn, KegsTapped;

    /// <summary>What each trade's bot played and said, by the trade it sat for.</summary>
    public Dictionary<string, BotStats> Bots = [];

    public int Met => Sim.Director.Met;
    public int Unmet => Sim.Director.Unmet;
}
