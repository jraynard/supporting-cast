using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;

namespace SupportingCast.Sim.Systems;

/// <summary>
/// The tool ladder applied to rock (docs/plans/mining-progression.md): a block's
/// <see cref="BlockDef.DigTier"/> is the lowest pick tier that breaks it, and a pick better than
/// that goes a little faster. Blocks of tier 0 (soil, rough stone) are dug as they always were.
/// </summary>
public static class Mining
{
    /// <summary>Speed bonus per tier a tool is above the block's <see cref="BlockDef.DigTier"/>.</summary>
    public const float BonusPerTier = 0.1f;

    /// <summary>At most this many tiers of <see cref="BonusPerTier"/> count.</summary>
    public const int MaxBonusTiers = 3;

    /// <summary>The tool kind a block wants: its <see cref="BlockDef.DigTool"/>, else a pick.</summary>
    public static string ToolKindFor(BlockDef block) => block.DigTool ?? "Pick";

    /// <summary>
    /// Why <paramref name="worker"/> can't break <paramref name="block"/> with what's to hand
    /// ("needs a copper pick"), or null if they can. Only people need picks: a dragon's claws,
    /// like any animal's, break whatever they're set to.
    /// </summary>
    public static string? BreakProblem(Simulation sim, Entity worker, BlockDef block)
    {
        if (block.DigTier <= 0 || sim.World.Get<Creature>(worker).Def.Race != Race.Human)
            return null;
        var kind = ToolKindFor(block);
        return Crafting.ToolTier(sim, worker, kind) >= block.DigTier ? null : $"needs {sim.Defs.ToolOfTier(block.DigTier, kind)}";
    }

    public static bool CanBreak(Simulation sim, Entity worker, BlockDef block) => BreakProblem(sim, worker, block) == null;

    /// <summary>
    /// How fast <paramref name="worker"/> digs <paramref name="block"/>: the gathering pace with its
    /// tool (<see cref="Crafting.GatherFactor"/>), plus <see cref="BonusPerTier"/> for each tier the
    /// tool is above the block's. Soil and tier-0 rock get no bonus, so they dig as they always did.
    /// </summary>
    public static float DigFactor(Simulation sim, Entity worker, BlockDef block)
    {
        if (block.DigTool is not { } kind)
            return 1f; // soil: any hands
        var factor = Crafting.GatherFactor(sim, worker, kind);
        if (block.DigTier <= 0 || sim.World.Get<Creature>(worker).Def.Race != Race.Human)
            return factor;
        var above = Math.Clamp(Crafting.ToolTier(sim, worker, kind) - block.DigTier, 0, MaxBonusTiers);
        return factor * (1f + BonusPerTier * above);
    }

    /// <summary>
    /// The pick tier a cell seems to need, as the player sees it: hidden ore passes for the rock
    /// around it, so an order's warning never gives a vein away.
    /// </summary>
    public static int ShownTier(Simulation sim, int3 cell) => ShownBlock(sim, sim.Map.Idx(cell)).DigTier;

    /// <summary>
    /// The block as the player sees it: hidden ore (not yet <see cref="Maps.Map.Revealed"/>) passes
    /// for its host rock. Everything shown on screen or in text reads blocks through this.
    /// </summary>
    public static BlockDef ShownBlock(Simulation sim, int i)
    {
        var map = sim.Map;
        var block = map.Block[i];
        if (!block.Ore || Exposed(map, i))
            return block;
        // Pass for the rock around it: the first neighbor that isn't ore, sideways before up and down.
        var c = map.CellOf(i);
        foreach (var d in Around)
        {
            var n = c + d;
            if (map.InBounds(n) && map.Block[map.Idx(n)] is { Solid: true, Ore: false, IsWater: false } rock)
                return rock;
        }
        return block.HostRockDef ?? block;
    }

    private static readonly int3[] Around =
        [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, -1), new(0, 0, 1)];

    /// <summary>
    /// Has a face of the block at <paramref name="i"/> been opened: is any neighbor air or water?
    /// Blocks only ever open up (digging, never filling), so once seen, ore stays seen; it needs no
    /// saved state, and every co-op peer agrees.
    /// </summary>
    public static bool Exposed(Maps.Map map, int i) => ExposedBut(map, i, -1);

    private static bool ExposedBut(Maps.Map map, int i, int ignore)
    {
        var c = map.CellOf(i);
        foreach (var d in Around)
        {
            var n = c + d;
            if (!map.InBounds(n))
                continue;
            var j = map.Idx(n);
            if (j != ignore && (!map.Block[j].Solid || map.Block[j].IsWater))
                return true;
        }
        return false;
    }

    /// <summary>
    /// After <paramref name="i"/> was dug out: ore beside it that nothing had opened before is seen
    /// now. Says so (once an hour per kind), so a vein struck while tunnelling isn't missed.
    /// </summary>
    public static void RevealAround(Simulation sim, int i, Entity digger)
    {
        var map = sim.Map;
        var c = map.CellOf(i);
        foreach (var d in Around)
        {
            var n = c + d;
            if (!map.InBounds(n))
                continue;
            var j = map.Idx(n);
            if (!map.Block[j].Ore || ExposedBut(map, j, i))
                continue;
            var who = !digger.IsNull && sim.World.TryGet<Creature>(digger, out var cr) ? cr.Name : "Someone";
            sim.Messages.AddThrottled($"ore:{map.Block[j].Name}", SimClock.TicksPerHour, MessageKind.Good,
                $"{who} uncovered {map.Block[j].Label}!", n);
        }
    }

    /// <summary>
    /// Ore from a dig, by the miner's skill: ×0.8 for a novice up to ×1.25 (Mining level 15), never
    /// below one. Only ore: stone and soil come out as they always have.
    /// </summary>
    public static int OreYield(Simulation sim, Entity digger, BlockDef block, ThingDef item, int count)
    {
        if (!block.Ore || item.Name == "Stone" || digger.IsNull || !sim.World.Has<Skills>(digger))
            return count;
        var level = SkillRules.LevelOf(sim, digger, Skill.Mining);
        return Math.Max(1, (int)MathF.Round(count * Math.Min(1.25f, 0.8f + 0.03f * level)));
    }

    /// <summary>
    /// A harvest's <see cref="HarvestProps.Bonus"/> extras (ore in a broken boulder): each rolled from
    /// a hash of the cell and the map seed, so it never draws from <c>sim.Rand</c>.
    /// </summary>
    public static void RollBonus(Simulation sim, HarvestProps harvest, int3 cell, Entity finder) =>
        RollBonus(sim, harvest.Bonus, cell, finder, 7001u);

    /// <summary>
    /// Extras that turn up now and then (<see cref="BonusYield"/>), each rolled from a hash of the
    /// cell, the map seed and <paramref name="salt"/>, so it never draws from <c>sim.Rand</c>.
    /// </summary>
    public static void RollBonus(Simulation sim, List<BonusYield> extras, int3 cell, Entity finder, uint salt)
    {
        for (var k = 0; k < extras.Count; k++)
        {
            var bonus = extras[k];
            var roll = SimRandom.Hash(cell.X + cell.Z * 7919, cell.Y, (uint)sim.Seed + salt + (uint)k * 131u) / (float)uint.MaxValue;
            if (roll >= bonus.Chance)
                continue;
            sim.SpawnItem(bonus.ThingDef, bonus.Count, cell);
            sim.Messages.Add(MessageKind.Good, $"Found {bonus.ThingDef.Label}!", cell);
            if (!finder.IsNull && sim.Defs.HandleTriggers.TryGetValue(bonus.ThingDef, out var trigger))
                RecipeBooks.Discover(sim, finder, trigger);
        }
    }

    /// <summary>
    /// "needs a copper pick (yours: stone)" for the block at <paramref name="i"/> as it looks, or
    /// null when it's soft enough for anyone. <paramref name="worker"/> null: the village's best.
    /// </summary>
    public static string? DigNote(Simulation sim, int i, Entity? worker)
    {
        var block = ShownBlock(sim, i);
        if (block.DigTier <= 0 || block.DigWork <= 0)
            return null;
        var kind = ToolKindFor(block);
        var have = worker is { IsNull: false } w ? Crafting.ToolTier(sim, w, kind) : VillageTier(sim, kind);
        var need = sim.Defs.ToolOfTier(block.DigTier, kind);
        var haveText = have <= 0 ? "none" : sim.Defs.TierLabel(have);
        return have >= block.DigTier ? $"Dig with {need} or better" : $"Needs {need} ({(worker != null ? "yours" : "the village's best")}: {haveText})";
    }

    /// <summary>
    /// The best tier of gear (tools, weapons, armor: <see cref="ThingDef.GearTier"/>) the village has:
    /// worn, wielded, carried, on belts and in packs, lying about or kept in storage. What the
    /// storyteller matches its raiders to.
    /// </summary>
    public static int VillageGearTier(Simulation sim)
    {
        var w = sim.World;
        var best = 0;
        void Consider(Entity e)
        {
            if (!e.IsNull && w.IsAlive(e) && w.TryGet<Thing>(e, out var t))
                best = Math.Max(best, t.Def.GearTier);
        }
        foreach (var e in sim.Creatures())
        {
            if (w.Get<Creature>(e) is not { Faction: Faction.Village, Dead: false })
                continue;
            if (w.TryGet<Carrying>(e, out var hands))
                Consider(hands.Item);
            if (w.TryGet<Equipment>(e, out var eq))
                Consider(eq.Weapon);
            foreach (var worn in sim.Worn(e))
                Consider(worn);
            foreach (var tool in Toolbelt.Of(sim, e))
                Consider(tool);
            if (sim.PackOf(e) is { IsNull: false } pack)
                foreach (var item in sim.StoredIn(pack))
                    Consider(item);
        }
        foreach (var e in sim.AvailableItems())
            Consider(e);
        return best;
    }

    /// <summary>
    /// The best tier of tool of this kind the village has: carried, on a belt, in a pack, lying about
    /// or kept in storage. For warnings when an order asks for more than anyone can manage.
    /// </summary>
    public static int VillageTier(Simulation sim, string kind)
    {
        var w = sim.World;
        var best = 0;
        foreach (var e in sim.Creatures())
            if (w.Get<Creature>(e).Faction == Faction.Village && !w.Get<Creature>(e).Dead)
                best = Math.Max(best, Crafting.ToolTier(sim, e, kind));
        foreach (var e in sim.AvailableItems())
            if (w.Get<Thing>(e).Def.Tool is { } t && t.Kind == kind)
                best = Math.Max(best, t.Tier);
        return best;
    }
}
