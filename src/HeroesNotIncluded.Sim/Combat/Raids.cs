using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;

namespace HeroesNotIncluded.Sim.Combat;

/// <summary>
/// Putting raiders on the map, kitted out by <see cref="RaiderKindDef"/>. Used by the storyteller
/// and the scripted raid demo. They walk in on their own (<see cref="RaiderBrain"/>), and the
/// fight starts once they come in sight of the village.
/// </summary>
public static class Raids
{
    public static readonly string[] Names =
    [
        "Grimsby", "Mott", "Sable", "Wendel", "Kragg", "Orla", "Brannoc", "Tilda", "Vask", "Hroth", "Ysolde", "Cobb",
    ];

    /// <summary>One raider of a kind at a cell, armed, dressed, rested and fed.</summary>
    public static Entity SpawnRaider(Simulation sim, RaiderKindDef kind, string name, int3 cell, SimRandom rand)
    {
        var r = sim.SpawnCreature(sim.Defs.Creature("Human"), name, cell, Faction.Raider, ageDays: rand.Range(20, 46) * 365f);
        if (kind.WeaponDef != null)
            sim.Equip(r, kind.WeaponDef);
        if (kind.OutfitDef != null) // clothes first, then armor over them
            Systems.Clothing.Dress(sim, r, kind.OutfitDef, Systems.Clothing.StreamFor(sim, r));
        foreach (var a in kind.ApparelDefs)
            sim.Wear(r, a);
        var skills = sim.World.Get<Skills>(r);
        skills.Level[(int)Skill.Melee] = rand.Range(kind.SkillMin, kind.SkillMax + 1);
        skills.Level[(int)Skill.Shooting] = rand.Range(kind.SkillMin, kind.SkillMax + 1);
        Array.Fill(sim.World.Get<Needs>(r).Values, 1f);
        return r;
    }

    /// <summary>A band of raiders gathered around <paramref name="entry"/>, each with a way to <paramref name="target"/>.</summary>
    public static List<Entity> Spawn(Simulation sim, IReadOnlyList<RaiderKindDef> kinds, int3 entry, int3 target, SimRandom rand)
    {
        var raiders = new List<Entity>();
        var first = rand.NextInt(Names.Length);
        var spots = GatherSpots(sim, entry, target, kinds.Count);
        for (var i = 0; i < spots.Count; i++)
            raiders.Add(SpawnRaider(sim, kinds[i], Names[(first + i) % Names.Length], spots[i], rand));
        return raiders;
    }

    /// <summary>Up to <paramref name="count"/> distinct passable cells near <paramref name="entry"/> that can reach <paramref name="target"/>.</summary>
    /// <summary>
    /// How big a raid the village has earned: it grows with the days survived, the people (armed
    /// ones count extra). Before any difficulty's factor.
    /// </summary>
    public static float Points(Simulation sim)
    {
        var w = sim.World;
        var points = 40f + 10f * (sim.Clock.Day - 1);
        foreach (var e in sim.Creatures())
        {
            var c = w.Get<Creature>(e);
            if (c.Faction != Faction.Village)
                continue;
            if (c.Def.Race == Race.Human)
            {
                points += 20f;
                // The town's own arms (Militia) are handed out after the band is sized: they don't count.
                if (w.TryGet<Equipment>(e, out var eq) && w.IsAlive(eq.Weapon) && !w.Has<Militia.TownArms>(eq.Weapon))
                    points += 15f;
            }
        }
        return points;
    }

    /// <summary>
    /// Pick raiders worth up to <paramref name="points"/>: random affordable kinds, at least the
    /// cheapest one. Only kinds whose gear is at most one tier past the village's best
    /// (<see cref="Systems.Mining.VillageGearTier"/>): raiders climb the ladder with you.
    /// </summary>
    public static List<RaiderKindDef> Choose(Simulation sim, SimRandom rand, float points, int maxRaiders)
    {
        var villageTier = Systems.Mining.VillageGearTier(sim);
        var kinds = sim.Defs.AllRaiderKinds.Where(k => k.Tier <= villageTier + 1).ToList();
        if (kinds.Count == 0)
            kinds = [sim.Defs.AllRaiderKinds.MinBy(k => (int)k.Tier)!];
        var chosen = new List<RaiderKindDef>();
        var left = points;
        while (chosen.Count < maxRaiders)
        {
            var affordable = kinds.Where(k => k.Cost <= left).ToList();
            if (affordable.Count == 0)
                break;
            var kind = rand.Pick(affordable);
            chosen.Add(kind);
            left -= kind.Cost;
        }
        if (chosen.Count == 0 && kinds.Count > 0)
            chosen.Add(kinds.MinBy(k => k.Cost)!);
        return chosen;
    }

    /// <summary>
    /// A raid of up to <paramref name="points"/> comes from a map edge, heading for whoever is nearest
    /// the middle of the village. Returns the raiders, none if there was nowhere to come from.
    /// </summary>
    public static List<Entity> Send(Simulation sim, SimRandom rand, float points, int maxRaiders)
    {
        if (VillageTarget(sim) is not { } target)
            return [];
        var first = rand.NextInt(4);
        int3? entry = null;
        for (var i = 0; i < 4 && entry == null; i++)
            entry = EdgeEntry(sim, (first + i) % 4, target);
        if (entry is not { } at)
            return [];

        var raiders = Spawn(sim, Choose(sim, rand, points, maxRaiders), at, target, rand);
        if (raiders.Count == 0)
            return raiders;
        var who = raiders.Count == 1 ? "A bandit is" : $"{raiders.Count} bandits are";
        sim.Messages.Add(MessageKind.Danger, $"{who} coming from the {Direction(sim.Map, at)}!", at);
        return raiders;
    }

    /// <summary>
    /// A raid warned of: these raiders, by the road on <paramref name="side"/> (or the next side
    /// round if that's no way in now), for whoever is nearest the middle of the village.
    /// </summary>
    public static List<Entity> SendPlanned(Simulation sim, SimRandom rand, IReadOnlyList<RaiderKindDef> kinds, int side, bool byRoad = false)
    {
        if (kinds.Count == 0 || VillageTarget(sim) is not { } target)
            return [];
        var entry = byRoad ? RoadEntry(sim, side, target) : null;
        for (var i = 0; i < 4 && entry == null; i++)
            entry = EdgeEntry(sim, (side + i) % 4, target);
        if (entry is not { } at)
            return [];
        var raiders = Spawn(sim, kinds, at, target, rand);
        if (raiders.Count == 0)
            return raiders;
        var who = raiders.Count == 1 ? "A bandit is" : $"{raiders.Count} bandits are";
        sim.Messages.Add(MessageKind.Danger, $"{who} coming from the {Direction(sim.Map, at)}!", at);
        return raiders;
    }

    /// <summary>The village human nearest the middle of them all; null if there's none.</summary>
    private static int3? VillageTarget(Simulation sim)
    {
        var w = sim.World;
        var villagers = sim.Creatures().Where(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human }).ToList();
        if (villagers.Count == 0)
            return null;
        var sum = new float3();
        foreach (var e in villagers)
            sum += Position.CenterOf(w.Get<Position>(e).Cell);
        var mid = sum * (1f / villagers.Count);
        return villagers
            .Select(e => w.Get<Position>(e).Cell)
            .MinBy(c => (Position.CenterOf(c) - mid) is var d ? d.X * d.X + d.Y * d.Y : 0);
    }

    /// <summary>The road a map side (as <see cref="EdgeEntry"/> numbers them) is.</summary>
    public static string SideName(int side) => side switch
    {
        0 => "west",
        1 => "east",
        2 => "north",
        _ => "south",
    };

    public static List<int3> GatherSpots(Simulation sim, int3 entry, int3 target, int count)
    {
        var spots = new List<int3>();
        foreach (var c in sim.SpiralFrom(entry, 8))
        {
            if (spots.Count == count)
                break;
            if (!spots.Contains(c) && sim.Map.Passable(c) && sim.Regions.CanReach(c, target, PathEnd.Touch))
                spots.Add(c);
        }
        return spots;
    }

    /// <summary>Which way a cell on the map edge lies: +x is east, -y north (as the map's rows run, <see cref="Defs.MapDef"/>).</summary>
    public static string Direction(Map map, int3 edge) =>
        edge.X == 0 ? "west" : edge.X == map.Width - 1 ? "east" : edge.Y == 0 ? "north" : "south";

    /// <summary>
    /// Where a road leaves the map on one side (as <see cref="EdgeEntry"/> numbers them): the road
    /// cell on that edge nearest across from <paramref name="toward"/> that can reach it. Null if no
    /// road runs off that side.
    /// </summary>
    public static int3? RoadEntry(Simulation sim, int side, int3 toward)
    {
        var map = sim.Map;
        var length = side < 2 ? map.Height : map.Width;
        var along = side < 2 ? toward.Y : toward.X;
        int3? best = null;
        var bestOff = int.MaxValue;
        for (var a = 0; a < length; a++)
        {
            var (x, y) = side switch
            {
                0 => (0, a),
                1 => (map.Width - 1, a),
                2 => (a, 0),
                _ => (a, map.Height - 1),
            };
            if (Math.Abs(a - along) >= bestOff || map.NearestStandable(x, y, toward.Z, map.Depth) is not { } c
                || map.GroundUnder(map.Idx(c)) is not { Name: "Road" } || !map.Passable(c) || !sim.Regions.CanReach(c, toward, PathEnd.Touch))
                continue;
            best = c;
            bestOff = Math.Abs(a - along);
        }
        return best;
    }

    /// <summary>
    /// A standable cell on one side of the map (0 west, 1 east, 2 north, 3 south), level with and
    /// across from <paramref name="toward"/>, sliding along the edge until one can reach it.
    /// </summary>
    public static int3? EdgeEntry(Simulation sim, int side, int3 toward)
    {
        var map = sim.Map;
        var length = side < 2 ? map.Height : map.Width;
        var along = side < 2 ? toward.Y : toward.X;
        for (var slide = 0; slide < length; slide += 3)
            foreach (var sign in new[] { 1, -1 })
            {
                var a = along + sign * slide;
                if (a < 0 || a >= length)
                    continue;
                var (x, y) = side switch
                {
                    0 => (0, a),
                    1 => (map.Width - 1, a),
                    2 => (a, 0),
                    _ => (a, map.Height - 1),
                };
                if (map.NearestStandable(x, y, toward.Z, map.Depth) is { } c && map.Passable(c) && sim.Regions.CanReach(c, toward, PathEnd.Touch))
                    return c;
            }
        return null;
    }
}

/// <summary>Beaten and making for the map edge: raiders who've given up, herds moving on. Nobody attacks them.</summary>
public struct Leaving;
