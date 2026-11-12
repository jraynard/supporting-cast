using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Core;

// Bills are village orders (anyone may set them); making something yourself is an actor command.
// Bills are addressed by index plus their recipe, so a stale request (the list changed under it)
// does nothing rather than editing the wrong bill.

/// <summary>Add a bill to a station.</summary>
public sealed record AddBillCommand(Entity Station, RecipeDef Recipe, BillMode Mode, int Target) : ISimCommand
{
    public void Apply(Simulation sim)
    {
        if (!Crafting.IsStation(sim, Station) || !sim.World.Get<Thing>(Station).Def.Recipes.Contains(Recipe) || !Enum.IsDefined(Mode)
            || !Crafting.Fitted(sim, Station, Recipe))
            return;
        var bench = sim.World.Get<Workbench>(Station);
        if (bench.Bills.Count >= Crafting.MaxBills(sim.World.Get<Thing>(Station).Def))
            return;
        bench.Bills.Add(new Bill { Recipe = Recipe, Mode = Mode, Target = Math.Max(1, Target) });
    }
}

/// <summary>Change a bill: how long it runs, paused or not, which material it uses.</summary>
public sealed record EditBillCommand(Entity Station, int Index, RecipeDef Recipe, BillMode Mode, int Target, bool Suspended, MaterialDef? Material)
    : ISimCommand
{
    public void Apply(Simulation sim)
    {
        if (CraftCommandRules.BillAt(sim, Station, Index, Recipe) is not { } bill || !Enum.IsDefined(Mode))
            return;
        bill.Mode = Mode;
        bill.Target = Math.Max(1, Target);
        bill.Suspended = Suspended;
        // Only a material some ingredient comes in.
        bill.Material = Material != null && Recipe.Ingredients.Exists(i => Material.Fits(i.ThingDef)) ? Material : null;
    }
}

/// <summary>Remove a bill. If it was being made and no other bill wants it, what was brought is given back.</summary>
public sealed record RemoveBillCommand(Entity Station, int Index, RecipeDef Recipe) : ISimCommand
{
    public void Apply(Simulation sim)
    {
        if (CraftCommandRules.BillAt(sim, Station, Index, Recipe) is not { } bill)
            return;
        var bench = sim.World.Get<Workbench>(Station);
        bench.Bills.Remove(bill);
        if (bench.Active == Recipe && bench.By.IsNull && bench.BillFor(Recipe) == null)
            Crafting.Cancel(sim, Station);
    }
}

/// <summary>Move a bill up (-1) or down (+1) the list: villagers work the first one that wants making.</summary>
public sealed record MoveBillCommand(Entity Station, int Index, RecipeDef Recipe, int By) : ISimCommand
{
    public void Apply(Simulation sim)
    {
        if (CraftCommandRules.BillAt(sim, Station, Index, Recipe) is not { } bill)
            return;
        var bills = sim.World.Get<Workbench>(Station).Bills;
        var to = Math.Clamp(Index + Math.Sign(By), 0, bills.Count - 1);
        bills.RemoveAt(Index);
        bills.Insert(to, bill);
    }
}

/// <summary>Call off the craft under way at a station, giving back what was brought for it.</summary>
public sealed record CancelCraftCommand(Entity Station) : ISimCommand
{
    public void Apply(Simulation sim)
    {
        if (Crafting.IsStation(sim, Station))
            Crafting.Cancel(sim, Station);
    }
}

/// <summary>
/// The player makes a recipe at a station in reach: brings what they have of its ingredients
/// (hands, pack, and containers in reach such as the wagon), and once it's all there starts the
/// work, which goes on while the work key is held (<see cref="CraftJob"/>). What's brought stays
/// at the station, so the rest can be fetched later.
/// </summary>
public sealed record CraftCommand(Entity Avatar, Entity Station, RecipeDef Recipe) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!InventoryCommand.Ready(sim, Avatar) || !Crafting.IsStation(sim, Station) || !w.Get<Thing>(Station).Def.Recipes.Contains(Recipe)
            || !Crafting.Fitted(sim, Station, Recipe))
            return;
        var cell = w.Get<Thing>(Station).Cell;
        if (!AvatarSystem.InReach(sim, w.Get<Position>(Avatar).Cell, Station) || sim.Reservations.IsReservedByOther(Station, Avatar))
            return;
        if (!Crafting.CanMake(sim, Avatar, Recipe))
        {
            sim.Messages.Add(MessageKind.Info, $"You don't know how to make {Recipe.Label} yet.", cell);
            return;
        }
        if (Crafting.MissingTool(sim, Avatar, Station, Recipe) is { } tool)
        {
            sim.Messages.Add(MessageKind.Info, $"You'll need {Crafting.ToolNeed(sim, Recipe, tool)} to make {Recipe.Label}.", cell);
            return;
        }
        var bench = w.Get<Workbench>(Station);
        if (bench.Active != Recipe)
        {
            if (!bench.Untouched)
            {
                sim.Messages.Add(MessageKind.Info, $"The {w.Get<Thing>(Station).Label} is busy with {bench.Active!.Label}.", cell);
                return;
            }
            bench.Begin(Recipe, Avatar);
        }

        foreach (var source in CraftCommandRules.Sources(sim, Avatar))
        {
            if (bench.Stocked)
                break;
            if (w.IsAlive(source))
                Crafting.Stage(sim, Station, source);
        }
        ref var carrying = ref w.Get<Carrying>(Avatar);
        if (!carrying.Item.IsNull && !w.IsAlive(carrying.Item))
            carrying.Item = Entity.Null;

        if (bench.Stocked)
            Crafting.WorkAt(sim, Avatar, Station, Recipe);
        else if (bench.NextNeeded(out var remaining) is var i and >= 0)
            sim.Messages.Add(MessageKind.Info,
                $"The {Recipe.Label} still needs {remaining} {Recipe.Ingredients[i].ThingDef.Label}.", cell);
    }
}

internal static class CraftCommandRules
{
    public static Bill? BillAt(Simulation sim, Entity station, int index, RecipeDef recipe)
    {
        if (!Crafting.IsStation(sim, station))
            return null;
        var bills = sim.World.Get<Workbench>(station).Bills;
        return index >= 0 && index < bills.Count && bills[index].Recipe == recipe ? bills[index] : null;
    }

    /// <summary>Where the player's ingredients come from, in order: hands, pack, then containers in reach.</summary>
    public static List<Entity> Sources(Simulation sim, Entity avatar)
    {
        var w = sim.World;
        var items = new List<Entity>();
        if (w.Get<Carrying>(avatar).Item is { IsNull: false } held)
            items.Add(held);
        if (sim.PackOf(avatar) is { IsNull: false } pack)
            items.AddRange(sim.StoredIn(pack));
        foreach (var c in ContainersInReach(sim, avatar))
            items.AddRange(sim.StoredIn(c));
        return items;
    }

    /// <summary>Storage buildings (the wagon, chests) within arm's reach, in cell order.</summary>
    public static List<Entity> ContainersInReach(Simulation sim, Entity avatar)
    {
        var w = sim.World;
        var here = w.Get<Position>(avatar).Cell;
        var found = new List<Entity>();
        for (var dz = -1; dz <= 1; dz++)
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var c = here + new int3(dx, dy, dz);
                    if (!sim.Map.InBounds(c))
                        continue;
                    var s = sim.Map.Structure[sim.Map.Idx(c)];
                    if (!s.IsNull && w.Get<Thing>(s).Def.Building is { StorageStacks: > 0 } && !w.Has<Construction>(s)
                        && AvatarSystem.InReach(here, c) && !found.Contains(s))
                        found.Add(s);
                }
        return found;
    }
}

/// <summary>
/// The player mends worn gear they have on them (worn, wielded, in hand or pack) at a station in
/// reach that makes that kind of thing; the work goes on while the key is held (<see cref="RepairJob"/>).
/// </summary>
public sealed record RepairCommand(Entity Avatar, Entity Station, Entity Item) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!InventoryCommand.Ready(sim, Avatar) || !Crafting.IsStation(sim, Station) || !Inventory.OnPerson(sim, Avatar, Item)
            || !AvatarSystem.InReach(sim, w.Get<Position>(Avatar).Cell, Station)
            || Crafting.RepairCost(sim, w.Get<Thing>(Station).Def, Item) == null)
            return;
        if (Work.Smithing.AppliesToRepair(sim, Avatar, Station))
            Work.Smithing.BeginMending(sim, Avatar, Station, Item);
        else
            AiSystem.Order(sim, Avatar, new RepairJob(Station, Item));
    }
}

/// <summary>The player makes a hand recipe (one with no station) where they stand (<see cref="HandCraftJob"/>).</summary>
public sealed record HandCraftCommand(Entity Avatar, RecipeDef Recipe) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!InventoryCommand.Ready(sim, Avatar) || !Recipe.IsHandRecipe || !Crafting.CanMake(sim, Avatar, Recipe))
            return;
        var have = CraftCommandRules.Sources(sim, Avatar);
        foreach (var x in Recipe.Ingredients)
            if (have.Where(e => sim.World.IsAlive(e) && Crafting.Accepts(x, null, sim.World.Get<Thing>(e).Def, sim.World.Get<Thing>(e).Material))
                    .Sum(e => sim.World.Get<Stack>(e).Count) < x.Count)
            {
                sim.Messages.Add(MessageKind.Info, $"Making {Recipe.Label} takes {string.Join(", ", Recipe.Ingredients.Select(i => $"{i.Count} {i.ThingDef.Label}"))}.",
                    sim.World.Get<Position>(Avatar).Cell);
                return;
            }
        AiSystem.Order(sim, Avatar, new HandCraftJob(Recipe));
    }
}
