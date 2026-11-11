using SupportingCast.Sim.Components;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Core;

// The player's own hands, pack and gear (the inventory window and hotbar). All of them act as the
// player's character and do nothing if the request has gone stale; the rules are in Inventory.

/// <summary>Swap a stack from the pack into the hands (what was held goes into the pack).</summary>
public sealed record HoldCommand(Entity Avatar, Entity Item) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && InventoryCommand.IsItem(sim, Item))
            Inventory.Hold(sim, Avatar, Item);
    }
}

/// <summary>Put what's in hand into the pack.</summary>
public sealed record StowCommand(Entity Avatar) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar))
            Inventory.StowHands(sim, Avatar);
    }
}

/// <summary>Put on or wield an item from the hands, the pack or the ground in reach.</summary>
public sealed record EquipCommand(Entity Avatar, Entity Item) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && InventoryCommand.IsItem(sim, Item))
            Inventory.Equip(sim, Avatar, Item);
    }
}

/// <summary>Take off a piece of gear (or put the weapon away): into the pack, the hands or onto the ground.</summary>
public sealed record UnequipCommand(Entity Avatar, GearSlot Slot) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar))
            Inventory.Unequip(sim, Avatar, Slot);
    }
}

/// <summary>Put a stack from the pack down at the avatar's feet.</summary>
public sealed record DropFromPackCommand(Entity Avatar, Entity Item) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && InventoryCommand.IsItem(sim, Item))
            Inventory.DropFromPack(sim, Avatar, Item);
    }
}

/// <summary>Move a stack from the pack into a container in reach (the wagon, a pack on the ground).</summary>
public sealed record StoreFromPackCommand(Entity Avatar, Entity Container, Entity Item) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && InventoryCommand.IsItem(sim, Item) && sim.IsContainer(Container))
            Inventory.StoreFromPack(sim, Avatar, Container, Item);
    }
}

internal static class InventoryCommand
{
    /// <summary>A living, conscious player character outside a fight (gear doesn't change mid-battle).</summary>
    public static bool Ready(Simulation sim, Entity avatar)
    {
        var w = sim.World;
        if (!w.IsAlive(avatar) || !w.Has<Avatar>(avatar) || sim.Combat.Involves(avatar))
            return false;
        var c = w.Get<Creature>(avatar);
        return !c.Dead && !c.Downed;
    }

    /// <summary>A stack of something (commands come off the network: the entity may be anything, or gone).</summary>
    public static bool IsItem(Simulation sim, Entity e) => sim.World.Has<Thing>(e) && sim.World.Has<Stack>(e);
}

/// <summary>
/// Hang a tool from the hands or pack on the belt (<paramref name="On"/>), or take one off it into
/// the pack (the hands if it won't fit, else at your feet).
/// </summary>
public sealed record BeltCommand(Entity Avatar, Entity Item, bool On) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!InventoryCommand.Ready(sim, Avatar) || !InventoryCommand.IsItem(sim, Item))
            return;
        var w = sim.World;
        if (On)
        {
            if (w.Get<Carrying>(Avatar).Item == Item || Inventory.InOwnPack(sim, Avatar, Item))
                Toolbelt.PutOn(sim, Avatar, Item);
            return;
        }
        if (!w.TryGet<Stored>(Item, out var s) || s.In != Avatar)
            return;
        var pack = sim.PackOf(Avatar);
        var t = w.Get<Thing>(Item);
        if (!pack.IsNull && sim.ContainerRoom(pack, t.Def, t.Material) > 0)
        {
            w.Remove<Stored>(Item);
            sim.Store(pack, Item);
        }
        else
            Toolbelt.TakeOff(sim, Avatar, Item);
    }
}
