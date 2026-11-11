using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Core;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Systems;
using Xunit.Abstractions;
using static DragonWorld.Sim.Tests.TestWorld;

namespace DragonWorld.Sim.Tests;

/// <summary>
/// Raising a dragon from the egg, headless (docs/plans/dragon-interactions.md): a keeper who looks
/// after it (village care work: answering its wants, playing, hand-feeding, lessons) earns its trust
/// by the time it's grown, but not its devotion so soon; one who leaves it to itself ends up with a
/// sulking dragon that never trusts them.
/// </summary>
public class KeeperCareScenarioTests(ITestOutputHelper output)
{
    /// <summary>When a dragon is grown (the adult stage): an attentive keeper should be trusted by then.</summary>
    private const float AdultDays = 12f;

    private (Simulation Sim, Entity Keeper, Entity Dragon) Raise(ulong seed, bool tends, float days, Dictionary<string, float> reached)
    {
        var sim = Create(40, 40, seed, depth: 4);
        sim.Climate.BaseTemperature = 28;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 28f);
        sim.SpawnItem(sim.Thing("Meat"), 30, C(14, 14));
        sim.SpawnItem(sim.Thing("Meat"), 30, C(14, 15));
        sim.SpawnItem(sim.Thing("Meat"), 30, C(14, 16));
        var home = C(10, 10);
        if (!tends)
        {
            // Away: shut in under rock across the map, where the dragon can't reach (nor fly to, nor be
            // kept company by) her.
            home = C(31, 31);
            sim.FillBlocks(new int3(28, 28, 1), new int3(34, 34, 2), "Limestone");
            sim.FillBlocks(new int3(30, 30, 1), new int3(32, 32, 1), "Air");
            sim.Map.MarkStructureChanged();
            sim.RebuildDerivedIfNeeded();
        }
        var keeper = sim.SpawnCreature(sim.Defs.Creature("Human"), "Brenna", home, ageDays: 25 * 365);
        var work = sim.World.Get<Mind>(keeper).WorkPriority;
        Array.Fill(work, 0);
        work[(int)WorkType.Tend] = tends ? 1 : 0;
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Smolder", C(12, 10), ageDays: 0);
        EggSystem.AssignKeeper(sim, dragon, keeper);
        var w = sim.World;
        var sulked = false;
        for (var t = 0; t < SimClock.DaysToTicks(days); t += SimClock.TicksPerHour)
        {
            // The keeper's own food: this is about the dragon.
            w.Get<Needs>(keeper).Add(sim.Defs.Need("Food"), 1f);
            sim.TickMany(SimClock.TicksPerHour);
            if (!w.Has<Keeper>(keeper))
                reached.TryAdd("(left)", sim.Clock.Tick / (float)SimClock.TicksPerDay); // gone from her (maybe off the map already)
            if (!w.IsAlive(dragon) || w.Get<Creature>(dragon).Dead)
                break;
            var level = Bonding.Level(sim, dragon)!;
            reached.TryAdd(level.Name, sim.Clock.Tick / (float)SimClock.TicksPerDay);
            sulked |= Bonding.Peek(sim, dragon).Sulking;
            // More meat when it runs low: the larder isn't the question.
            if (AiQueries.FindNearestItem(sim, keeper, (_, d) => d.IsMeat).IsNull)
                sim.SpawnItem(sim.Thing("Meat"), 30, C(14, 14));
        }
        if (sulked)
            reached.TryAdd("(sulked)", 0);
        foreach (var m in sim.Messages.Messages.Where(m => m.Kind != MessageKind.Info))
            output.WriteLine($" [{m.Tick / (float)SimClock.TicksPerDay:F2}d] {m.Kind}: {m.Text}");
        output.WriteLine($"{(tends ? "Attentive" : "Neglectful")} keeper, seed {seed}: affinity {(w.IsAlive(dragon) ? w.Get<Dragon>(dragon).Affinity : 0):F2}, "
            + string.Join(", ", reached.Select(kv => $"{kv.Key} {kv.Value:F1}d")));
        return (sim, keeper, dragon);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    public void AnAttentiveKeeperIsTrustedByTheTimeTheDragonIsGrown(ulong seed)
    {
        var reached = new Dictionary<string, float>();
        var (sim, _, dragon) = Raise(seed, tends: true, AdultDays + 2, reached);
        Assert.True(reached.TryGetValue("Trusting", out var trusted) && trusted <= AdultDays, $"trusted by day {AdultDays}");
        Assert.False(reached.TryGetValue("Devoted", out var devoted) && devoted < 10f, "devotion takes longer than a hatchling's first days");
        Assert.False(reached.ContainsKey("(sulked)"), "never sulked");
        Assert.NotEmpty(Bonding.Peek(sim, dragon).Training); // lessons once it's a juvenile
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    public void ALeftAloneDragonSulksAndNeverTrusts(ulong seed)
    {
        // A keeper who's never there: the hatchling sulks within a few days and never trusts her,
        // and once it's old enough to fly it leaves for good.
        var reached = new Dictionary<string, float>();
        var (sim, keeper, dragon) = Raise(seed, tends: false, 6f, reached);
        Assert.True(reached.ContainsKey("(sulked)"), "it sulked");
        Assert.False(reached.ContainsKey("Accepting"));
        Assert.True(reached.TryGetValue("(left)", out var left) && left >= 4f, "it left once it could fly");
        Assert.False(sim.World.Has<Keeper>(keeper));
        Assert.Contains(sim.World.Get<Mood>(keeper).Memories, m => m.Def.Name == "MyDragonLeft");
    }
}
