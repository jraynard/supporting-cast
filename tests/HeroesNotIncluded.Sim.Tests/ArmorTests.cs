using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

public class ArmorTests
{
    private static Simulation Field()
    {
        var sim = TestWorld.Create(30, 20);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Human(Simulation sim, int3 at, Faction faction = Faction.Village)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), faction == Faction.Village ? "Villager" : "Bandit", at, faction, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        sim.SetSkills(e);
        return e;
    }

    private static int Part(Simulation sim, Entity e, string name) => sim.World.Get<Health>(e).Body.PartIndex(name);

    [Fact]
    public void ArmorOnlyProtectsWhatItCovers()
    {
        var sim = Field();
        var knight = Human(sim, C(5, 5));
        sim.Wear(knight, sim.Thing("IronHelm"));
        var head = CombatRules.ArmorAt(sim, knight, Part(sim, knight, "Head"), DamageType.Cut);
        var leg = CombatRules.ArmorAt(sim, knight, Part(sim, knight, "LeftLeg"), DamageType.Cut);
        var blunt = CombatRules.ArmorAt(sim, knight, Part(sim, knight, "Head"), DamageType.Blunt);
        Assert.Equal(0.5f, head.Armor, 3);
        Assert.False(head.Item.IsNull);
        Assert.Equal(0f, leg.Armor, 3);
        Assert.Equal(0.3f, blunt.Armor, 3);
    }

    [Fact]
    public void ArmorTakesItsShareOfEveryBlow()
    {
        var sim = Field();
        var attacker = Human(sim, C(5, 5));
        var bare = Human(sim, C(6, 5), Faction.Raider);
        var armored = Human(sim, C(6, 6), Faction.Raider);
        sim.Wear(armored, sim.Thing("ChainShirt"));
        sim.Wear(armored, sim.Thing("IronHelm"));
        sim.Equip(attacker, sim.Thing("ShortSword"));
        var sword = CombatRules.Attacks(sim, attacker)[0];
        Assert.True(CombatRules.DamageChance(sim, armored, sword) < CombatRules.DamageChance(sim, bare, sword) - 0.1f);

        float Taken(Entity target)
        {
            sim.Equip(attacker, sim.Thing("ShortSword")); // a fresh edge each time: a blade dulls as it's used
            sword = CombatRules.Attacks(sim, attacker)[0];
            var total = 0f;
            for (var i = 0; i < 60; i++)
            {
                var h = sim.World.Get<Health>(target);
                h.Injuries.Clear();
                h.Changed();
                total += CombatRules.Strike(sim, attacker, target, sword).Damage; // it reached them: armour takes its share
            }
            return total;
        }
        var (a, b) = (Taken(armored), Taken(bare));
        Assert.True(a < b * 0.85f, $"armored {a}, bare {b}");
    }

    [Fact]
    public void ArmorWearsOutAndFallsApart()
    {
        var sim = Field();
        var wearer = Human(sim, C(5, 5));
        var cap = sim.Wear(wearer, sim.Thing("LeatherCap"));
        CombatRules.WearDown(sim, wearer, cap, 40);
        Assert.True(sim.World.IsAlive(cap));
        Assert.Equal(30f, sim.World.Get<Thing>(cap).Hp, 3);
        CombatRules.WearDown(sim, wearer, cap, 70);
        Assert.False(sim.World.IsAlive(cap));
        Assert.Empty(sim.Worn(wearer));
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("leather cap falls apart"));
    }

    [Fact]
    public void VillagerPutsOnAJerkinLyingAround()
    {
        var sim = Field();
        var villager = Human(sim, C(5, 5));
        Array.Fill(sim.World.Get<Mind>(villager).WorkPriority, 0);
        sim.SpawnItem(sim.Thing("LeatherJerkin"), 1, C(10, 8));
        sim.TickMany(SimClock.TicksPerHour / 2);
        var worn = Assert.Single(sim.Worn(villager));
        Assert.Equal("LeatherJerkin", sim.World.Get<Thing>(worn).Def.Name);
        Assert.False(sim.World.Get<Thing>(worn).Spawned);
    }

    [Fact]
    public void TheDeadDropTheirGear()
    {
        var sim = Field();
        var bandit = Human(sim, C(8, 8), Faction.Raider);
        sim.Equip(bandit, sim.Thing("Club"));
        sim.Wear(bandit, sim.Thing("LeatherCap"));
        ref var c = ref sim.World.Get<Creature>(bandit);
        NeedsSystem.Kill(sim, bandit, ref c);
        Assert.Empty(sim.Worn(bandit));
        var names = sim.SpawnedItems().Select(e => sim.World.Get<Thing>(e).Def.Name).ToList();
        Assert.Contains("Club", names);
        Assert.Contains("LeatherCap", names);
    }
}
