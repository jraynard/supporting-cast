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

    /// <summary>Which way a cell on the map edge lies: +x is east, +y north.</summary>
    public static string Direction(Map map, int3 edge) =>
        edge.X == 0 ? "west" : edge.X == map.Width - 1 ? "east" : edge.Y == 0 ? "south" : "north";

    /// <summary>
    /// A standable cell on one side of the map (0 west, 1 east, 2 south, 3 north), level with and
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
