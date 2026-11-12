using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Scenarios;

/// <summary>Who a player made in the creator.</summary>
public sealed record Hero(string? Name, Appearance? Look, IReadOnlyList<string>? Traits, IReadOnlyList<string>? Clothes = null);

/// <summary>
/// The village a round is played in: a hand-made map (<see cref="MapDef"/>, Millbrook unless told
/// otherwise) with its working buildings already up and AI villagers living in it, so a trade
/// nobody took still gets done and players walk into a place that's running.
/// </summary>
public static class VillageRound
{
    /// <summary>AI villagers at the start, before any player arrives.</summary>
    public const int StartingVillagers = 6;

    /// <summary>The map a round is played on unless another is chosen (data/defs/maps/).</summary>
    public const string DefaultMap = "Millbrook";

    /// <summary>The landing point and the buildings trades will work from.</summary>
    public readonly record struct Town(int3 Center, Entity Tavern, Entity Forge, Entity Shrine, Entity Woodshop);

    /// <summary>A simulation the size of the round's map, with the standard systems: what <see cref="Setup"/> builds into.</summary>
    public static Simulation NewSimulation(DefDatabase defs, ulong seed, string map = DefaultMap) =>
        MapBuilder.CreateSimulation(defs, defs.Map(map), seed);

    /// <summary>Lay out the map and raise the town (see <see cref="NewSimulation"/>). Returns where everything is.</summary>
    public static Town Setup(Simulation sim, string map = DefaultMap)
    {
        var built = MapBuilder.Build(sim, sim.Defs.Map(map), new MapGenerator.Options { Villagers = StartingVillagers });
        Entity Role(string role) => built.Buildings.FirstOrDefault(b => b.Spec.Role == role).Building;
        var shrine = Role("Shrine");
        StockShrine(sim, shrine);
        // Beds for everyone, or the first night sends them all out to sleep in the cold.
        foreach (var (spec, home) in built.Buildings)
            if (spec.Role == "Home")
                VillageSetup.FillSpareBeds(sim, home);
        sim.RebuildDerivedIfNeeded();
        return new Town(built.Center, Role("Tavern"), Role("Forge"), shrine, Role("Woodshop"));
    }

    /// <summary>Bandages in the shrine's chest: the healer's backup for when the mana runs dry.</summary>
    public const int ShrineBandages = 12;

    private static void StockShrine(Simulation sim, Entity shrine)
    {
        if (shrine.IsNull)
            return;
        foreach (var e in sim.World.Query<PartOf>())
            if (sim.World.Get<PartOf>(e).Building == shrine && sim.World.Get<Thing>(e).Def.Building is { StorageStacks: > 0 })
            {
                sim.Store(e, sim.CreateItem(sim.Defs.Thing(Systems.Bandaging.BandageDef), ShrineBandages));
                return;
            }
    }

    /// <summary>A player's character walking into town near <paramref name="near"/>. Co-op's host claims it on joining (<see cref="Players.Join"/>).</summary>
    public static Entity SpawnHero(Simulation sim, Hero hero, int3 near)
    {
        var w = sim.World;
        var cell = sim.SpiralFrom(near + new int3(0, 2, 0), 10).FirstOrDefault(c => sim.Map.Passable(c), near);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), string.IsNullOrWhiteSpace(hero.Name) ? "Arin" : hero.Name.Trim(), cell, ageDays: 24 * 365f);
        Array.Fill(w.Get<Needs>(me).Values, 0.75f);
        w.Add(me, new Avatar());
        sim.GiveStartingPack(me);
        if (hero.Look is { } look)
            w.Add(me, look);
        if (hero.Traits != null)
            TraitRules.Set(sim, me, hero.Traits);
        Clothing.DressHero(sim, me, hero.Clothes); // last, so it shifts no other entity
        return me;
    }
}
