using SupportingCast.Sim.Components;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Scenarios;

/// <summary>
/// The combat slice: an early-game raid. Of the three starting villagers one has a hunting bow,
/// one a woodcutter's axe, and the third won't fight but keeps a young dragon that can (when it
/// listens). Two bandits with a club and a short sword walk in from the east; turn-based
/// combat starts when they come within sight. Shared by the scenario test and --raid-demo.
/// </summary>
public static class RaidDemo
{
    public static readonly string[] BanditNames = ["Grimsby", "Mott", "Sable", "Wendel", "Kragg"];

    public sealed record Cast(Entity Archer, Entity Axeman, Entity Keeper, Entity Dragon, IReadOnlyList<Entity> Raiders);

    public static Cast Setup(Simulation sim, int3 center, int raidDistance = 30)
    {
        var w = sim.World;
        var villagers = sim.Creatures().Where(e => w.Get<Creature>(e) is { Def.Race: Race.Human, Faction: Faction.Village }).ToList();
        if (villagers.Count < 3)
            throw new InvalidOperationException("The raid needs the three starting villagers");
        var (archer, axeman, keeper) = (villagers[0], villagers[1], villagers[2]);
        sim.Equip(archer, sim.Defs.Thing("Bow"));
        sim.Equip(axeman, sim.Defs.Thing("Axe"));
        w.Add(keeper, new NonViolent());
        foreach (var k in w.Query<Keeper>())
            w.Remove<Keeper>(k);

        var spot = sim.SpiralFrom(center + new int3(-2, 3, 0), 5).First(c => sim.Map.Passable(c));
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Ember", spot, ageDays: 5);
        EggSystem.AssignKeeper(sim, dragon, keeper);
        w.Get<Dragon>(dragon).Affinity = 0.6f; // listens four times in five

        var raiders = new List<Entity>();
        var kinds = new[] { "Brawler", "Swordsman" };
        for (var i = 0; i < 2; i++)
        {
            var near = center + new int3(raidDistance, 4 * i - 2, 0);
            var at = sim.SpiralFrom(near, 8).First(c => sim.Map.Passable(c) && sim.Regions.CanReach(c, center, PathEnd.Touch));
            var name = BanditNames[(i + (int)(sim.Seed % 3)) % BanditNames.Length];
            raiders.Add(Raids.SpawnRaider(sim, sim.Defs.RaiderKind(kinds[i]), name, at, sim.Rand));
        }

        foreach (var e in villagers.Append(dragon).Concat(raiders))
            Array.Fill(w.Get<Needs>(e).Values, 1f);
        sim.Messages.Add(MessageKind.Danger, "Bandits are coming from the east!", raiders[0].IsNull ? center : w.Get<Position>(raiders[0]).Cell);
        return new Cast(archer, axeman, keeper, dragon, raiders);
    }
}
