using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>No steel in the inn (<see cref="HouseRules"/>): fists only there, unless you're a raider or a raid's on.</summary>
public class HouseRulesTests
{
    private static void Put(Simulation sim, Entity e, int3 cell)
    {
        sim.World.Get<Position>(e).Cell = cell;
        sim.World.Get<Position>(e).Pos = Position.CenterOf(cell);
    }

    [Theory]
    [InlineData("millbrook")]
    [InlineData("reedmere")]
    public void FistsOnlyInTheInnUnlessARaidIsOn(string map)
    {
        var (sim, _) = TestWorld.OnMap(map);
        var inn = ShopSystem.BuildingFor(sim, TownRole.Tavern);
        Assert.False(inn.IsNull);
        var anchor = sim.World.Get<PresetBuilding>(inn).Anchor;
        var inside = sim.SpiralFrom(anchor, 12).First(c => sim.Map.Passable(c) && HouseRules.RuledBuilding(sim, c) == inn);
        var outside = sim.SpiralFrom(anchor, 30).First(c => c.Z == anchor.Z && sim.Map.Passable(c) && Buildings.At(sim, c).IsNull);

        var villager = sim.Creatures().First(e => sim.World.Get<Creature>(e).Faction == Faction.Village && sim.World.Get<Creature>(e).Def.Name == "Human");
        sim.Equip(villager, sim.Thing("ShortSword"));
        Put(sim, villager, outside);
        Assert.False(CombatRules.Attacks(sim, villager)[0].Name == "Fists", "steel outside");
        Put(sim, villager, inside);
        Assert.True(HouseRules.NoSteel(sim, villager));
        Assert.All(CombatRules.Attacks(sim, villager), a => Assert.True(a.Name == "Fists", $"{a.Name} drawn in the inn"));

        // A raider keeps no house rules.
        sim.World.Get<Creature>(villager).Faction = Faction.Raider;
        Assert.False(HouseRules.NoSteel(sim, villager));
        sim.World.Get<Creature>(villager).Faction = Faction.Village;

        // A raid lifts it: the town may defend the inn with steel.
        sim.Militia.Muster(sim);
        Assert.False(HouseRules.NoSteel(sim, villager));
        Assert.Equal("ShortSword", sim.World.Get<Thing>(sim.World.Get<Equipment>(villager).Weapon).Def.Name);
        Assert.NotEqual("Fists", CombatRules.Attacks(sim, villager)[0].Name);
    }
}
