using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Core;

/// <summary>
/// A player action, queued with <see cref="Simulation.Enqueue"/> and applied at the start of the
/// next tick. Routing input through commands instead of poking sim state from the UI keeps a run
/// reproducible from its input stream alone: the basis for replays, input-driven tests and, later,
/// lockstep multiplayer. Apply must be deterministic and tolerate a stale target (it may have died
/// since the command was sent).
/// </summary>
public interface ISimCommand
{
    void Apply(Simulation sim);
}

/// <summary>
/// Hold (or release, with a zero direction) a movement direction for an avatar. With
/// <paramref name="At"/>, the change waits until the avatar walks onto that cell: a co-op client
/// predicts its walk and names the cell where the player turned or let go, so the late-arriving
/// command lands where they saw it. Too far away, or if walking on wouldn't get there, it applies at once.
/// </summary>
public sealed record AvatarMoveCommand(Entity Avatar, int2 Direction, int3? At = null) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    /// <summary>How far ahead (cells) a turn may be scheduled: more is a stale or bogus prediction.</summary>
    public const int MaxLead = 4;

    public void Apply(Simulation sim)
    {
        if (!sim.World.IsAlive(Avatar) || !sim.World.Has<Avatar>(Avatar))
            return;
        ref var a = ref sim.World.Get<Avatar>(Avatar);
        var dir = new int2(Math.Sign(Direction.X), Math.Sign(Direction.Y));
        var here = sim.World.Get<Position>(Avatar).Cell;
        if (At is { } at && (a.MoveDir.X != 0 || a.MoveDir.Y != 0) && AvatarSystem.Distance(here, at) <= MaxLead)
        {
            a.HasPending = true;
            a.PendingDir = dir;
            a.PendingAt = at;
            return;
        }
        a.HasPending = false;
        a.MoveDir = dir;
    }
}

/// <summary>
/// Press or release a work key (<paramref name="Tool"/>: F to use, Z to dig): start the action at a
/// cell on press, and work only while held.
/// </summary>
public sealed record AvatarWorkCommand(Entity Avatar, bool Held, int3? Target = null, AvatarTool Tool = AvatarTool.Use) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!sim.World.IsAlive(Avatar) || !sim.World.Has<Avatar>(Avatar))
            return;
        ref var avatar = ref sim.World.Get<Avatar>(Avatar);
        avatar.WorkHeld = Held;
        if (!Held)
            avatar.Chain = AvatarActionKind.None; // letting go ends a run of work
        if (Held && Target is { } cell)
            AvatarSystem.Perform(sim, Avatar, AvatarSystem.ActionAt(sim, Avatar, cell, Tool));
    }
}

/// <summary>
/// Click-to-move (the left mouse button): walk to a cell, or with <paramref name="Interact"/> up to
/// what's there and then do what the work key would, the button held standing in for the key
/// (releasing it is an <see cref="AvatarWorkCommand"/> with Held false). A plain walk while one is
/// under way just moves its destination, so following the cursor stays smooth.
/// </summary>
public sealed record AvatarGoToCommand(Entity Avatar, int3 Target, bool Interact = false) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!InventoryCommand.Ready(sim, Avatar) || !sim.Map.InBounds(Target) || w.Has<Carried>(Avatar))
            return;
        ref var a = ref w.Get<Avatar>(Avatar);
        a.Chain = AvatarActionKind.None;
        a.WorkHeld = Interact;
        if (!Interact && w.Get<Mind>(Avatar).Job is AvatarWalkJob { Interact: false } walk)
            walk.Retarget(Target);
        else
            AiSystem.Order(sim, Avatar, new AvatarWalkJob(Target, Interact));
    }
}

/// <summary>
/// Eat a food stack: from the avatar's hands or pack, a container in reach or the ground beside it.
/// Picked from the item (its menu), never from the work key, so F never eats what you meant to use.
/// </summary>
public sealed record EatCommand(Entity Avatar, Entity Food) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && AvatarSystem.CanEat(sim, Avatar, Food))
            AiSystem.Order(sim, Avatar, new EatHeldJob(Food));
    }
}

/// <summary>
/// "Sleep here": lie down where the avatar stands, in the bed there if there is one, else on the
/// ground (a worse night: the "slept on the ground" thought). Walking off wakes them.
/// </summary>
public sealed record SleepCommand(Entity Avatar) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Avatar) || !w.Has<Avatar>(Avatar) || !w.Get<Creature>(Avatar).Able)
            return;
        var here = w.Get<Position>(Avatar).Cell;
        if (!sim.Map.Standable(sim.Map.Idx(here)))
            return;
        var site = sim.Map.Structure[sim.Map.Idx(here)];
        var bed = !site.IsNull && w.Get<Thing>(site).Def.Building is { IsBed: true } && !w.Has<Construction>(site) ? site : Entity.Null;
        w.Get<Avatar>(Avatar).Chain = AvatarActionKind.None;
        AiSystem.Order(sim, Avatar, new Ai.SleepJob(bed));
    }
}

/// <summary>X: put down what the avatar holds at a cell in reach (a press, not held: work carries on as it was).</summary>
public sealed record AvatarDropCommand(Entity Avatar, int3 Target) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!sim.World.IsAlive(Avatar) || !sim.World.Has<Avatar>(Avatar))
            return;
        if (AvatarSystem.ActionAt(sim, Avatar, Target, AvatarTool.Drop) is { Kind: AvatarActionKind.PutDown } drop)
            AvatarSystem.Perform(sim, Avatar, drop);
    }
}

/// <summary>
/// Take some of a stack (one, by default; <see cref="All"/> for up to a full stack) out of a
/// container in reach (the wagon, a shelf, a pack on the ground): into empty hands, else as much
/// as fits into the avatar's pack.
/// </summary>
public sealed record TakeFromStorageCommand(Entity Avatar, Entity Item, int Count = 1) : ISimCommand, IActorCommand
{
    /// <summary>The whole stack (as much of it as fits).</summary>
    public const int All = int.MaxValue;

    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Avatar) || !w.Has<Avatar>(Avatar) || !w.IsAlive(Item) || !w.TryGet<Stored>(Item, out var stored))
            return;
        if (sim.Reservations.IsReservedByOther(Item, Avatar) || !Inventory.CanReachContainer(sim, Avatar, stored.In))
            return;
        // One at a time unless more is asked for: a stack's not grabbed whole by accident.
        var count = Math.Clamp(Count, 1, Math.Min(w.Get<Stack>(Item).Count, w.Get<Thing>(Item).Def.StackLimit));
        if (!w.Get<Carrying>(Avatar).HasItem)
        {
            w.Get<Carrying>(Avatar).Item = sim.TakeFromStack(Item, count);
            return;
        }
        var pack = sim.PackOf(Avatar);
        if (pack.IsNull)
            return;
        var t = w.Get<Thing>(Item);
        var room = Math.Min(sim.ContainerRoom(pack, t.Def, t.Material), count);
        if (room > 0)
            sim.Store(pack, sim.TakeFromStack(Item, room));
    }
}

/// <summary>
/// Take off what a person wears in a slot (or drop their weapon) and keep it off, or lift that
/// order (<paramref name="KeepBare"/> false) so they pick gear up again.
/// </summary>
public sealed record GearOrderCommand(Entity Creature, GearSlot Slot, bool KeepBare = true) : ISimCommand, ISubjectCommand
{
    public Entity Subject => Creature;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Creature) || !w.Has<Creature>(Creature) || w.Get<Creature>(Creature).Dead)
            return;
        if (!w.Has<Outfit>(Creature))
            w.Add(Creature, new Outfit());
        ref var outfit = ref w.Get<Outfit>(Creature);
        outfit.SetBare(Slot, KeepBare);
        if (!KeepBare)
            return;
        if (Slot == GearSlot.Weapon)
            sim.DropWeapon(Creature);
        else
            sim.TakeOff(Creature, Outfit.ApparelOf(Slot)!.Value);
    }
}

/// <summary>Go and put on (or wield) a particular item, lifting any bare order on its slot.</summary>
public sealed record WearCommand(Entity Creature, Entity Item) : ISimCommand, ISubjectCommand
{
    public Entity Subject => Creature;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Creature) || !w.Has<Creature>(Creature) || w.Get<Creature>(Creature).Dead || !w.IsAlive(Item) || !w.Has<Thing>(Item))
            return;
        var def = w.Get<Thing>(Item).Def;
        GearSlot slot;
        if (def.Apparel is { } a)
            slot = Outfit.Of(a.Slot);
        else if (def.Weapon != null)
            slot = GearSlot.Weapon;
        else
            return;
        if (!w.Has<Outfit>(Creature))
            w.Add(Creature, new Outfit());
        ref var outfit = ref w.Get<Outfit>(Creature);
        outfit.SetBare(slot, false);
        outfit.Wanted = Item;
    }
}
