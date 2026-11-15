using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Scenarios;

namespace HeroesNotIncluded.Debug;

/// <summary>
/// Headless combat balance (<c>--combat-sweep</c>): raids on the town over a run of seeds, with
/// and without a party in town, the dragon both ways, and one-on-one fights in the ring. The
/// numbers to tune by: see <see cref="CombatSweep"/>.
/// </summary>
public static class CombatSweepRun
{
    public static void Run(string[] args)
    {
        var defs = DefDatabase.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "data", "defs"));
        var n = Value(args, "--seeds") is { } s && int.TryParse(s, out var count) ? count : 8;
        var first = Value(args, "--seed") is { } f && ulong.TryParse(f, out var seed) ? seed : 1ul;
        var seeds = Enumerable.Range(0, n).Select(i => first + (ulong)i).ToArray();
        var all = !args.Any(a => a is "--party" or "--no-party" or "--dragon" or "--duels");
        var parties = args.Contains("--party") ? [true] : args.Contains("--no-party") ? [false] : new[] { true, false };

        if (all || args.Contains("--party") || args.Contains("--no-party"))
            foreach (var party in parties)
                Raids(defs, seeds, party);
        if (all || args.Contains("--dragon"))
            foreach (var party in parties)
                Dragons(defs, seeds, party);
        if (all || args.Contains("--duels"))
            Duels(defs, seeds);
    }

    private static void Raids(DefDatabase defs, ulong[] seeds, bool party)
    {
        Console.WriteLine($"\n== Raids, day {CombatSweep.Day}, {(party ? "a party in town" : "no party")} ==");
        var results = Parallel(seeds, s => CombatSweep.RaidTrial(defs, s, party));
        foreach (var r in results)
            Console.WriteLine($"seed {r.Seed,3}: {(r.RoundLost ? "LOST" : Verdict(r.Met)),-5} {r.Hours,4:F1}h  {r.Raiders} raiders [{r.Band}]: {r.RaidersDown} down, {r.RaidersFled} fled"
                + $" | town {r.TownDead} dead {r.TownDowned} downed | heroes {r.HeroesDead} dead {r.HeroesDowned} downed");
        foreach (var r in results)
            Console.WriteLine($"  seed {r.Seed,3} raiders: {r.RaiderBlows}\n  seed {r.Seed,3} town:    {r.TownBlows}");
        Console.WriteLine($"MET {results.Count(r => r.Met == true)}/{results.Length} ({results.Count(r => r.RoundLost)} lost the round); town dead {results.Sum(r => r.TownDead)}, heroes dead {results.Sum(r => r.HeroesDead)}, "
            + $"raiders down {results.Sum(r => r.RaidersDown)}/{results.Sum(r => r.Raiders)}, fled {results.Sum(r => r.RaidersFled)}");
    }

    private static void Dragons(DefDatabase defs, ulong[] seeds, bool party)
    {
        Console.WriteLine($"\n== The dragon, {(party ? "a party in town" : "no party")} ==");
        var results = Parallel(seeds, s => CombatSweep.DragonTrial(defs, s, party));
        foreach (var r in results)
            Console.WriteLine($"seed {r.Seed,3}: {Verdict(r.Met),-5} {r.Hours,4:F1}h  damage {r.DamageTaken,5:F1}{(r.Downed ? " DOWNED" : r.DrivenOff ? " driven off" : "")}"
                + $" | town {r.TownDead} dead {r.TownDowned} downed | heroes {r.HeroesDead} dead {r.HeroesDowned} downed | blows at it: {r.TownBlows}");
        Console.WriteLine($"MET {results.Count(r => r.Met == true)}/{results.Length}; town dead {results.Sum(r => r.TownDead)}, heroes dead {results.Sum(r => r.HeroesDead)}");
    }

    /// <summary>Raider kinds against a middling villager, a few ways armed, over the seeds.</summary>
    private static void Duels(DefDatabase defs, ulong[] seeds)
    {
        Console.WriteLine("\n== Duels in the ring: villager win % (draws), mean seconds ==");
        (string? Weapon, string[] Apparel)[] loadouts =
        [
            (null, []), ("Club", []), ("ShortSword", []), ("ShortSword", ["LeatherJerkin", "LeatherCap"]), ("IronSpear", ["ChainShirt", "IronHelm"]),
        ];
        // Duels are cheap and noisy: four times the seeds.
        var duelSeeds = Enumerable.Range(0, seeds.Length * 4).Select(i => seeds[0] + (ulong)i).ToArray();
        var kinds = defs.AllRaiderKinds.Select(k => k.Name).ToList();
        Console.Write($"{"",-14}");
        foreach (var (weapon, apparel) in loadouts)
            Console.Write($"{(weapon ?? "fists") + (apparel.Length > 0 ? "+armour" : ""),22}");
        Console.WriteLine();
        foreach (var kind in kinds)
        {
            Console.Write($"{kind,-14}");
            foreach (var (weapon, apparel) in loadouts)
            {
                var results = Parallel(duelSeeds, s => CombatSweep.Duel(defs, s, kind, weapon, Sim.Components.Skills.Average, apparel));
                var won = results.Count(r => r.VillagerWon == true);
                var draws = results.Count(r => r.VillagerWon == null);
                Console.Write($"{$"{100 * won / results.Length}% ({draws}) {results.Average(r => r.Seconds):F0}s",22}");
            }
            Console.WriteLine();
        }
    }

    private static string Verdict(bool? met) => met switch { true => "MET", false => "unmet", null => "-" };

    private static T[] Parallel<T>(ulong[] seeds, Func<ulong, T> run)
    {
        var results = new T[seeds.Length];
        System.Threading.Tasks.Parallel.For(0, seeds.Length, i => results[i] = run(seeds[i]));
        return results;
    }

    private static string? Value(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
