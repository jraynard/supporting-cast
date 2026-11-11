using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Scenarios;

/// <summary>Who a player made in the creator.</summary>
public sealed record Hero(string? Name, Appearance? Look, IReadOnlyList<string>? Traits, IReadOnlyList<string>? Clothes = null);

/// <summary>
/// The village a round is played in: a generated valley with its working buildings already up and
/// AI villagers living in it, so a trade nobody took still gets done and players walk into a
/// place that's running. M1's lobby deals players into it; for now they simply arrive.
/// </summary>
public static class VillageRound
{
    /// <summary>AI villagers at the start, before any player arrives.</summary>
    public const int StartingVillagers = 6;

    /// <summary>The landing point and the buildings trades will work from.</summary>
    public readonly record struct Town(int3 Center, Entity Tavern, Entity Forge, Entity Shrine);

    /// <summary>Generate the valley and raise the town. Returns where everything is.</summary>
    public static Town Setup(Simulation sim)
    {
        var center = MapGenerator.Generate(sim, new MapGenerator.Options { Villagers = StartingVillagers });
        sim.RebuildDerivedIfNeeded();
        VillageSetup.ApplyVillageDefaults(sim, center);

        var tavern = Raise(sim, "Longhouse", center + new int3(-14, -12, 0), 0);
        var forge = Raise(sim, "Smithy", center + new int3(12, -12, 0), 0);
        var shrine = Raise(sim, "Shrine", center + new int3(14, 6, 0), 0);
        StockShrine(sim, shrine);
        // Beds for everyone, or the first night sends them all out to sleep in the cold.
        for (var i = 0; i < 3; i++)
        {
            var home = Raise(sim, "Dwelling", center + new int3(-16 + i * 11, 12, 0), 2);
            if (!home.IsNull)
                VillageSetup.FillSpareBeds(sim, home);
        }
        sim.RebuildDerivedIfNeeded();
        return new Town(center, tavern, forge, shrine);
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

    /// <summary>A finished building at the nearest spot its plot fits, on the landing's level; Entity.Null if none is in reach.</summary>
    private static Entity Raise(Simulation sim, string name, int3 preferred, int facing)
    {
        var map = sim.Map;
        var def = sim.Defs.Building(name);
        for (var r = 0; r <= 14; r++)
        for (var dy = -r; dy <= r; dy++)
        for (var dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r || !map.InBounds(preferred.X + dx, preferred.Y + dy, 0))
                continue;
            var ground = map.VisibleCell(preferred.X + dx, preferred.Y + dy, map.Depth - 1);
            if (map.IsSolid(map.Idx(ground)))
                ground = new int3(ground.X, ground.Y, ground.Z + 1);
            if (ground.Z != preferred.Z || Buildings.PlacementProblem(sim, def, ground, facing) != null)
                continue;
            var built = Buildings.SpawnFinished(sim, def, ground, facing);
            sim.RebuildDerivedIfNeeded(); // the next plot must see this one
            return built;
        }
        return Entity.Null;
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
