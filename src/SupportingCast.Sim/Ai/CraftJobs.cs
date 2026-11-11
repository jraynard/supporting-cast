using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Ai;

/// <summary>
/// Make a recipe at a station: fetch whatever its ingredients still lack (several stacks a trip
/// with a work pack), hand them in at the station (<see cref="Crafting.Stage"/>), then work it to
/// completion. What's been brought and the work done stay on the station's <see cref="Workbench"/>,
/// so an interrupted craft is picked up again by whoever takes the bill next. The player's avatar
/// only works (what <c>CraftCommand</c> brought); it doesn't fetch.
/// </summary>
public sealed class CraftJob(Entity station, RecipeDef recipe) : Job
{
    public override Entity Focus => station;

    public override string? ToolKind => recipe.Tools.Count > 0 ? recipe.Tools[0] : null;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = $"Making {recipe.Label}";
        if (!Crafting.IsStation(Sim, station))
        {
            Fail("the station is gone");
            yield break;
        }
        if (!Reserve(station)) yield break;
        var avatar = Sim.World.Has<Avatar>(Pawn);
        var bench = Get<Workbench>(station);
        if (bench.Active != recipe)
        {
            if (!bench.Untouched)
            {
                Fail("the station is busy with something else");
                yield break;
            }
            bench.Begin(recipe, avatar ? Pawn : Entity.Null);
        }

        while (!bench.Stocked)
        {
            if (avatar)
            {
                Fail("missing ingredients");
                yield break;
            }
            foreach (var s in Fetch(bench)) yield return s;
            if (HasFailed) yield break;
            if (!Crafting.IsStation(Sim, station) || bench.Active != recipe)
            {
                Fail("the craft was called off");
                yield break;
            }
        }

        foreach (var s in GoToBeside(station)) yield return s;
        if (HasFailed) yield break;
        while (bench.Progress < recipe.Work)
        {
            if (!Crafting.IsStation(Sim, station) || bench.Active != recipe)
            {
                Fail("the craft was called off");
                yield break;
            }
            if (Crafting.MissingTool(Sim, Pawn, station, recipe) is { } tool)
            {
                Fail($"needs {Crafting.ToolNeed(Sim, recipe, tool)} to hand");
                yield break;
            }
            if (!Crafting.CanGetHotEnough(Sim, station, recipe))
            {
                Fail($"the {Get<Thing>(station).Label} isn't lit, or hot enough");
                yield break;
            }
            var work = Work(recipe.Skill);
            if (work > 0)
                Crafting.Pump(Sim, station); // bellows beside it, if any: worked between blows
            if (!Crafting.HotEnough(Sim, station, recipe))
            {
                Report = $"Working the bellows for {recipe.Label}";
                yield return JobStatus.Running;
                continue;
            }
            Report = $"Making {recipe.Label}";
            bench.Progress += work * Crafting.ToolFactor(Sim, Pawn, station, recipe) / (bench.MainMaterial?.WorkFactor ?? 1f);
            yield return JobStatus.Running;
        }

        var cell = Get<Thing>(station).Cell;
        var made = Crafting.Finish(Sim, station, Pawn);
        foreach (var item in made)
        {
            var label = Get<Stack>(item).Count > 1 ? $"{Get<Stack>(item).Count} {Get<Thing>(item).Label}" : $"a {Sim.LabelOf(item)}";
            if (avatar)
            {
                Inventory.Stow(Sim, Pawn, item);
                Sim.Messages.Add(MessageKind.Info, $"Made {label}.", cell);
            }
            else
                Sim.PlaceItemNear(item, cell);
        }
    }

    /// <summary>One trip: the nearest stack of the first missing ingredient, more of it near that one into the pack, then hand it all in.</summary>
    private IEnumerable<JobStatus> Fetch(Workbench bench)
    {
        var need = bench.NextNeeded(out var remaining);
        var ingredient = recipe.Ingredients[need];
        var bill = bench.BillFor(recipe);
        bool Fits(Entity e, Thing t) => Crafting.Accepts(ingredient, bill, t.Def, t.Material);
        var item = AiQueries.FindNearestItem(Sim, Pawn, (e, _) => Fits(e, Get<Thing>(e)));
        if (item.IsNull)
        {
            Fail($"no {ingredient.ThingDef.Label} to be had");
            yield break;
        }
        Report = $"Fetching {Get<Thing>(item).Label} for {recipe.Label}";
        if (!Reserve(item)) yield break;
        foreach (var s in GoTo(Get<Thing>(item).Cell, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        if (!Sim.IsAvailable(item))
        {
            Fail("the ingredient disappeared");
            yield break;
        }
        var def = ingredient.ThingDef;
        var near = Get<Thing>(item).Cell;
        var carried = Sim.TakeFromStack(item, Math.Min(Math.Min(remaining, def.StackLimit), Get<Stack>(item).Count));
        Get<Carrying>(Pawn).Item = carried;
        var pack = Inventory.WorkPack(Sim, Pawn);
        foreach (var s in GatherNear(pack, def, Fits, near, remaining, new Tally { Count = Get<Stack>(carried).Count })) yield return s;
        if (HasFailed) yield break;

        if (!Crafting.IsStation(Sim, station))
        {
            Fail("the station is gone");
            yield break;
        }
        foreach (var s in GoToBeside(station)) yield return s;
        if (HasFailed) yield break;
        if (!Crafting.IsStation(Sim, station) || bench.Active != recipe)
        {
            Fail("the craft was called off");
            yield break;
        }

        Report = $"Making {recipe.Label}";
        Crafting.Stage(Sim, station, carried);
        Get<Carrying>(Pawn).Item = Entity.Null;
        if (Alive(carried))
            Sim.PlaceItemNear(carried, PawnCell); // more than it needed
        if (!pack.IsNull)
            foreach (var e in Sim.StoredIn(pack))
                if (Get<Thing>(e).Def == def)
                {
                    Crafting.Stage(Sim, station, e);
                    if (Alive(e))
                        Sim.PlaceItemNear(Sim.TakeFromStack(e, Get<Stack>(e).Count), PawnCell);
                }
    }
}

/// <summary>
/// The player mends a worn piece of gear at the station that makes it (<see cref="Crafting.RepairCost"/>),
/// working while the key is held. The material is used up only when it's done, so stopping costs nothing.
/// </summary>
public sealed class RepairJob(Entity station, Entity item) : Job
{
    public override Entity Focus => station;

    protected override IEnumerable<JobStatus> Run()
    {
        if (!Crafting.IsStation(Sim, station) || !Alive(item)
            || Crafting.RepairCost(Sim, Get<Thing>(station).Def, item) is not { } cost)
        {
            Fail("nothing to mend");
            yield break;
        }
        if (!Reserve(station)) yield break;
        Report = $"Mending {Sim.LabelOf(item)}";
        var recipe = Crafting.RepairRecipe(Get<Thing>(station).Def, Get<Thing>(item).Def)!;
        for (var work = 0f; work < cost.Work; work += Work(recipe.Skill) * Crafting.ToolFactor(Sim, Pawn, station, recipe))
        {
            if (!Crafting.IsStation(Sim, station) || !Alive(item) || !Systems.Inventory.OnPerson(Sim, Pawn, item))
            {
                Fail("the mending was called off");
                yield break;
            }
            yield return JobStatus.Running;
        }
        // Pay for it now, from what's to hand; short of it, nothing is mended.
        if (!Crafting.PayForRepair(Sim, Pawn, cost.Material, cost.Count))
        {
            Fail($"needs {cost.Count} {cost.Material.ThingDef.Label}");
            yield break;
        }
        // Done as a job, the mend is a whole one: chips out and the edge put back.
        ref var t = ref Get<Thing>(item);
        t.Hp = t.MaxHp;
        Combat.WeaponWear.SetDullness(Sim, item, 0f);
        Sim.Messages.Add(MessageKind.Info, $"Mended the {Sim.LabelOf(item)}.", Get<Thing>(station).Cell);
    }
}

/// <summary>
/// The player makes a hand recipe (no station) where they stand, from what they carry and what's in
/// reach. It goes on by itself while they stand still (walking off stops it, losing the work, not
/// the ingredients: those are used up only when it's done).
/// </summary>
public sealed class HandCraftJob(RecipeDef recipe) : Job
{
    public override string? ToolKind => recipe.Tools.Count > 0 ? recipe.Tools[0] : null;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = $"Making {recipe.Label}";
        var skill = recipe.Skill;
        for (var work = 0f; work < recipe.Work; work += SkillRules.WorkFactor(Sim, Pawn, skill))
        {
            SkillRules.Learn(Sim, Pawn, skill, SkillRules.XpPerWorkTick);
            yield return JobStatus.Running;
        }
        if (!Crafting.Consume(Sim, recipe, Core.CraftCommandRules.Sources(Sim, Pawn), Pawn, out var material, out var rot))
        {
            Fail("missing ingredients");
            yield break;
        }
        foreach (var item in Crafting.MakeProducts(Sim, recipe, material, rot, Pawn, PawnCell))
        {
            var label = Get<Stack>(item).Count > 1 ? $"{Get<Stack>(item).Count} {Get<Thing>(item).Label}" : $"a {Sim.LabelOf(item)}";
            Inventory.Stow(Sim, Pawn, item);
            Sim.Messages.Add(MessageKind.Info, $"Made {label}.", PawnCell);
        }
    }
}
