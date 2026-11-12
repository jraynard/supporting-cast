using System.Diagnostics;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;

namespace HeroesNotIncluded.Debug;

/// <summary>Headless sim benchmark: run a generated village for a game day and report per-system cost.</summary>
public static class Bench
{
    public static void Run(ulong seed, int ticks = SimClock.TicksPerDay)
    {
        var defs = DefDatabase.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "data", "defs"));
        var sim = Sim.Scenarios.VillageRound.NewSimulation(defs, seed);
        Sim.Scenarios.VillageRound.Setup(sim);

        // Per-tick times too: frames hitch on the slowest ticks, which the average hides.
        var times = new double[ticks];
        var worst = new List<(double Ms, long Tick, string Systems)>();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < ticks; i++)
        {
            var start = Stopwatch.GetTimestamp();
            sim.Tick();
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (worst.Count < 5 || times[i] > worst[^1].Ms)
            {
                worst.Add((times[i], sim.Clock.Tick, RanOn(sim)));
                worst.Sort((a, b) => b.Ms.CompareTo(a.Ms));
                if (worst.Count > 5)
                    worst.RemoveAt(5);
            }
        }
        sw.Stop();

        Console.WriteLine($"{ticks} ticks in {sw.Elapsed.TotalMilliseconds:F0} ms = {sw.Elapsed.TotalMilliseconds / ticks:F4} ms/tick");
        Console.WriteLine($"6x speed needs {6 * SimClock.TicksPerSecond} ticks/s -> {sw.Elapsed.TotalMilliseconds / ticks * 360:F1} ms per real second");
        foreach (var (name, ms) in sim.SystemTotals.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"  {name,-15} {ms,9:F1} ms total  {ms / ticks * 1000,8:F2} us/tick  {ms / Math.Max(1, sim.SystemRuns.GetValueOrDefault(name)),7:F3} ms/run x{sim.SystemRuns.GetValueOrDefault(name)}");
        var sorted = (double[])times.Clone();
        Array.Sort(sorted);
        Console.WriteLine($"Tick p50 {sorted[ticks / 2]:F3} ms, p99 {sorted[(int)(ticks * 0.99)]:F3} ms, p99.9 {sorted[(int)(ticks * 0.999)]:F3} ms, max {sorted[^1]:F3} ms");
        Console.WriteLine("Slowest ticks:");
        foreach (var (ms, tick, systems) in worst)
            Console.WriteLine($"  tick {tick,6} {ms,7:F3} ms  {systems}");
        Console.WriteLine("Peak run per system (ms):");
        foreach (var (name, ms) in sim.SystemPeaks.OrderByDescending(kv => kv.Value).Take(8))
            Console.WriteLine($"  {name,-12} {ms,7:F3}");
        Console.WriteLine($"Entities: {sim.World.AliveCount}, state hash {sim.StateHash():X16}");
        // Best of five, warm: the co-op checksum runs FullHash(withMap: false) every few turns.
        static double BestMs(Action run)
        {
            var best = double.MaxValue;
            for (var i = 0; i < 5; i++)
            {
                var t = System.Diagnostics.Stopwatch.StartNew();
                run();
                best = Math.Min(best, t.Elapsed.TotalMilliseconds);
            }
            return best;
        }
        var fullMs = BestMs(() => sim.FullHash());
        var lightMs = BestMs(() => sim.FullHash(withMap: false));
        Console.WriteLine($"Full hash {sim.FullHash():X16}: {fullMs:F1} ms, without the map {lightMs:F2} ms");
        // Saving runs on the main thread between ticks (autosave at dawn), so its cost is a frame hitch.
        Sim.Saving.SaveGame.Write(sim); // warm up
        var save = Stopwatch.StartNew();
        var image = Sim.Saving.SaveGame.Capture(sim);
        var captured = save.Elapsed.TotalMilliseconds;
        var bytes = image.Encode();
        Console.WriteLine($"Save: capture {captured:F1} ms (main thread), compress {save.Elapsed.TotalMilliseconds - captured:F1} ms (background for autosaves), {bytes.Length / 1024} KB");
    }

    /// <summary>The systems that ran on the tick just played, slowest first (SystemTimings holds each one's latest run).</summary>
    private static string RanOn(Simulation sim)
    {
        var ran = sim.Systems.Where(s => sim.Clock.Tick % s.Interval == 0)
            .Select(s => (s.Name, Ms: sim.SystemTimings.GetValueOrDefault(s.Name)))
            .OrderByDescending(s => s.Ms).Take(4)
            .Select(s => $"{s.Name} {s.Ms:F2}");
        return string.Join(", ", ran);
    }
}
