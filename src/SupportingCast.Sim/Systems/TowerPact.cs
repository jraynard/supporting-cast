using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Environment;
using SupportingCast.Sim.Maps;

namespace SupportingCast.Sim.Systems;

/// <summary>
/// The Pact's debt: a tower for the sorcerer, by the ley stone he marked (<see cref="TowerDef"/>
/// says what it must be). The player builds it however they like; this checks it every hour and
/// ticks off what's done. The tower is the tallest stack of indoor rooms reaching within the
/// def's radius of the stone, each room right above the last. Once everything's met the pact is
/// settled: the sorcerer's protection lifts and the storyteller starts (if a difficulty was chosen).
/// </summary>
public static class TowerPact
{
    public const string DefaultTower = "SorcerersTower";

    /// <summary>The pact in force on this map, if any (null once there's none; settled ones stay with <see cref="PactState.Done"/>).</summary>
    public static PactState? Of(Simulation sim) => sim.Story.Pact;

    /// <summary>A pact still to settle.</summary>
    public static bool Active(Simulation sim) => sim.Story.Pact is { Done: false };

    /// <summary>Raise the ley stone near <paramref name="near"/> and put the pact in force. Returns the stone.</summary>
    public static Entity Begin(Simulation sim, int3 near, string tower = DefaultTower)
    {
        var def = sim.Defs.Tower(tower);
        var stoneDef = sim.Defs.Thing("LeyStone");
        var cell = sim.SpiralFrom(near, 10).FirstOrDefault(c => sim.Map.Passable(c) && sim.CanPlaceStructure(c, stoneDef)
            && sim.Map.Item[sim.Map.Idx(c)].IsNull, near);
        var stone = sim.SpawnStructure(stoneDef, cell);
        sim.Story.Pact = new PactState { Tower = def, Site = cell, Met = new bool[RequirementCount(def)] };
        sim.RebuildDerivedIfNeeded();
        Evaluate(sim);
        return stone;
    }

    private static int RequirementCount(TowerDef def) => 2 + def.ContainsDefs.Count;

    /// <summary>What the tower must have, in checklist order (matching <see cref="PactState.Met"/>).</summary>
    public static IReadOnlyList<string> Requirements(PactState pact)
    {
        var def = pact.Tower;
        var list = new List<string>
        {
            $"{def.Levels} storeys of enclosed rooms, one above the other, by the ley stone ({Math.Min(pact.Levels, def.Levels)}/{def.Levels})",
            $"Walls of {Join(def.WallDefs.Select(w => w.Label))} ({(int)MathF.Round(pact.WallShare * 100)}% of {(int)MathF.Round(def.WallShare * 100)}%)",
        };
        list.AddRange(def.ContainsDefs.Select(d => $"A {d.Label.ToLowerInvariant()} inside"));
        return list;
    }

    private static string Join(IEnumerable<string> labels)
    {
        var all = labels.ToList();
        return all.Count switch
        {
            0 => "anything",
            1 => all[0],
            _ => string.Join(", ", all.Take(all.Count - 1)) + " or " + all[^1],
        };
    }

    /// <summary>Check the tower against the pact, update the checklist, and settle it once everything's met.</summary>
    public static void Evaluate(Simulation sim)
    {
        if (sim.Story.Pact is not { Done: false } pact)
            return;
        sim.RebuildDerivedIfNeeded();
        var def = pact.Tower;
        var tower = TallestStack(sim, pact);
        pact.Levels = tower.Count;
        pact.WallShare = WallShare(sim, tower, def);

        var contents = new HashSet<ThingDef>();
        foreach (var room in tower)
            foreach (var i in room.Tiles)
                if (sim.Map.Structure[i] is { IsNull: false } s && !sim.World.Has<Construction>(s))
                    contents.Add(sim.World.Get<Thing>(s).Def);

        var met = pact.Met;
        if (met.Length != RequirementCount(def))
            met = pact.Met = new bool[RequirementCount(def)]; // the def changed under a save
        met[0] = pact.Levels >= def.Levels;
        met[1] = tower.Count > 0 && pact.WallShare >= def.WallShare;
        for (var k = 0; k < def.ContainsDefs.Count; k++)
            met[2 + k] = contents.Any(d => d.IsA(def.ContainsDefs[k])); // any shape of it (a long table is a table)

        if (met.All(m => m))
            Settle(sim, pact);
    }

    private static void Settle(Simulation sim, PactState pact)
    {
        pact.Done = true;
        sim.Messages.Add(MessageKind.Good,
            $"The tower stands. A voice carries on the wind, dry as old paper: \"It will do. We're square, rider.\" "
            + "The pact is settled: the land is yours to do with as you please.",
            pact.Site);
        if (sim.Story.PendingDifficulty is { } difficulty)
        {
            sim.Storyteller.SetDifficulty(sim, sim.Defs.Difficulty(difficulty));
            sim.Story.PendingDifficulty = null;
            sim.Messages.Add(MessageKind.Info, "Without the sorcerer's protection, word of you will travel. Raiders may come.", pact.Site);
        }
    }

    /// <summary>The tallest chain of indoor rooms near the site, each standing right on top of the one before (bottom first).</summary>
    public static List<Room> TallestStack(Simulation sim, PactState pact)
    {
        var map = sim.Map;
        var radius = pact.Tower.Radius;
        bool Near(Room r)
        {
            foreach (var i in r.Tiles)
            {
                var c = map.CellOf(i);
                if (Math.Max(Math.Abs(c.X - pact.Site.X), Math.Abs(c.Y - pact.Site.Y)) <= radius)
                    return true;
            }
            return false;
        }
        var candidates = sim.Rooms.All.Where(r => r.Indoors && r.Tiles.Count > 0 && Near(r)).ToList();
        var best = new List<Room>();
        foreach (var start in candidates)
        {
            var chain = new List<Room> { start };
            var current = start;
            while (candidates.FirstOrDefault(r => Rests(map, r, current)) is { } above && !chain.Contains(above))
            {
                chain.Add(above);
                current = above;
            }
            if (chain.Count > best.Count)
                best = chain;
        }
        return best;
    }

    // Some of the upper room's floor is right over the lower room.
    private static bool Rests(Map map, Room upper, Room lower)
    {
        foreach (var i in upper.Tiles)
        {
            var below = i - map.LayerSize;
            if (below >= 0 && map.RoomId[below] == lower.Id)
                return true;
        }
        return false;
    }

    /// <summary>Share of the walls bounding the tower's rooms that are of the pact's kind.</summary>
    private static float WallShare(Simulation sim, List<Room> tower, TowerDef def)
    {
        var map = sim.Map;
        var seen = new HashSet<int>();
        int walls = 0, good = 0;
        foreach (var room in tower)
            foreach (var i in room.Tiles)
            {
                var c = map.CellOf(i);
                foreach (var d in Map.Neighbors4)
                {
                    var n = Map.Offset(c, d);
                    if (!map.InBounds(n))
                        continue;
                    var ni = map.Idx(n);
                    if (!seen.Add(ni) || map.Structure[ni] is not { IsNull: false } s || sim.World.Has<Construction>(s))
                        continue;
                    var wall = sim.World.Get<Thing>(s).Def;
                    if (wall.Building is not { IsWall: true })
                        continue;
                    walls++;
                    if (def.WallDefs.Contains(wall))
                        good++;
                }
            }
        return walls == 0 ? 0f : good / (float)walls;
    }
}

/// <summary>A tower owed under a pact, and how far along it is (see <see cref="TowerPact"/>).</summary>
public sealed class PactState
{
    public required TowerDef Tower;

    /// <summary>The ley stone's cell: the tower's rooms must reach within the tower's radius of it.</summary>
    public int3 Site;

    public bool Done;

    /// <summary>Which requirements are met, in <see cref="TowerPact.Requirements"/> order (as of the last check).</summary>
    public bool[] Met = [];

    /// <summary>Storeys stacked so far, and the share of their walls of the right kind (as of the last check).</summary>
    public int Levels;
    public float WallShare;
}
