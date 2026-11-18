using System.Diagnostics;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;

namespace HeroesNotIncluded.Debug;

/// <summary>
/// Headless season balance (<c>--season-sweep</c>): whole rounds played by bots over a run of
/// seeds, how each came out and the totals. The baseline for tuning <c>season.json</c>: run it
/// before and after a change (see <see cref="SeasonSweep"/>).
/// </summary>
public static class SeasonSweepRun
{
    public static void Run(string[] args)
    {
        var defs = Mods.GameContent.Current.LoadDefs();
        var n = Value(args, "--seeds") is { } s && int.TryParse(s, out var count) ? count : 8;
        var first = Value(args, "--seed") is { } f && ulong.TryParse(f, out var seed) ? seed : 1ul;
        var difficulty = Value(args, "--difficulty") ?? "Normal";
        var map = Value(args, "--map") ?? VillageRound.DefaultMap;
        // The bots' table: --trades Innkeeper,Cook,... (one bot each), or the usual six.
        var trades = Value(args, "--trades") is { } t ? t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [.. SeasonSweep.TradesIn(defs)];
        var seeds = Enumerable.Range(0, n).Select(i => first + (ulong)i).ToArray();

        Console.WriteLine($"== Seasons in {map}, {difficulty}, bots as {string.Join(", ", trades)}; seeds {first}..{first + (ulong)n - 1} ==");
        var clock = Stopwatch.StartNew();
        var results = new SeasonResult[seeds.Length];
        Parallel.For(0, seeds.Length, i => results[i] = SeasonSweep.Play(defs, seeds[i], difficulty, trades, map: map));
        foreach (var r in results)
        {
            var s2 = r.Summary;
            Console.WriteLine(s2 == null
                ? $"seed {r.Seed,3}: DID NOT END ({r.Sim.Round.Phase} at {r.Hours:F0}h)"
                : $"seed {r.Seed,3}: {s2.Outcome,-9} day {s2.EndedTick / Sim.Core.SimClock.TicksPerDay + 1}"
                    + $" | Renown {s2.Renown,5:F1}/{s2.RenownTarget:F0} Dread {s2.Dread,5:F1}/{s2.DreadLimit:F0} (peak {Peak(s2.DreadByHour):F1})"
                    + $" | crises {string.Join(",", r.Crises)} met {r.Met}/{r.Crises.Count}"
                    + $" | served {s2.AdventurersServed}, quests {s2.QuestsCleared}+/{s2.QuestsFailed}-, lost {s2.AdventurersLost}"
                    + $" | dead {s2.People.Count(p => !p.Alive)}, back {s2.People.Count(p => p.CameBack)}"
                    + $" | drawn {r.Drawn}, kegs {r.KegsTapped}");
        }

        var ended = results.Where(r => r.Summary != null).Select(r => r.Summary!).ToList();
        Console.WriteLine();
        Console.WriteLine($"OUTCOMES {string.Join(", ", Enum.GetValues<RoundOutcome>().Skip(1).Select(o => $"{o} {ended.Count(e => e.Outcome == o)}"))}"
            + $"{(ended.Count < results.Length ? $", unended {results.Length - ended.Count}" : "")}");
        if (ended.Count > 0)
        {
            Console.WriteLine($"Renown mean {ended.Average(e => e.Renown):F1} of {ended[0].RenownTarget:F0} (min {ended.Min(e => e.Renown):F1}, max {ended.Max(e => e.Renown):F1});"
                + $" Dread peak mean {ended.Average(e => Peak(e.DreadByHour)):F1} of {ended[0].DreadLimit:F0}");
            Console.WriteLine($"Deaths {ended.Average(e => e.People.Count(p => !p.Alive)):F1} a round, came back {ended.Average(e => e.People.Count(p => p.CameBack)):F1};"
                + $" served {ended.Average(e => e.AdventurersServed):F1}, quests cleared {ended.Average(e => e.QuestsCleared):F1} failed {ended.Average(e => e.QuestsFailed):F1}");
        }
        Console.WriteLine($"Casks: drawn {results.Average(r => r.Drawn):F1} servings a season, kegs knocked in {results.Average(r => r.KegsTapped):F1}"
            + $" (drinks served {results.Average(r => r.Sim.Shops.Served(ServiceKind.Drink)):F1}, mixed {results.Average(r => r.Sim.Shops.Served(ServiceKind.MixedDrink)):F1})");
        var kinds = results.SelectMany(r => r.Sim.Reckoning.Crises).GroupBy(c => c.Kind).OrderBy(g => g.Key);
        Console.WriteLine("Crises met: " + string.Join(", ", kinds.Select(g => $"{g.Key} {g.Count(c => c.Met)}/{g.Count()}")));
        // What each trade's bot played: a game no bot starts shows here as missing, not just as a lost round.
        Console.WriteLine();
        Console.WriteLine("BOTS (means a season)");
        foreach (var trade in SeasonSweep.TradesIn(defs))
        {
            var stats = results.Select(r => r.Bots.GetValueOrDefault(trade)).OfType<Sim.Bots.BotStats>().ToList();
            if (stats.Count == 0)
                continue;
            string Mean(Func<Sim.Bots.BotStats, Dictionary<string, int>> of) =>
                string.Join(", ", stats.SelectMany(s => of(s).Keys).Distinct().Order()
                    .Select(k => $"{k} {stats.Average(s => of(s).GetValueOrDefault(k)):F1}"));
            Console.WriteLine($"  {trade}: said {stats.Average(s => s.Said):F1}, slips {stats.Average(s => s.Mistakes):F1}");
            Console.WriteLine($"    taken: {Mean(s => s.Taken)}");
            Console.WriteLine($"    games: {Mean(s => s.Sessions)}");
        }
        Console.WriteLine($"{results.Length} seasons in {clock.Elapsed.TotalSeconds:F0} s");
    }

    private static float Peak(List<float> v) => v.Count == 0 ? 0 : v.Max();

    private static string? Value(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
