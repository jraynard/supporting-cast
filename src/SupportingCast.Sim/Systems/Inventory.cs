using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Components;

namespace SupportingCast.Sim.Systems;

/// <summary>
/// A creature's hands, pack and gear, moved between by the player (<c>Core/InventoryCommands.cs</c>)
/// and by Adventure-mode actions. Items in hand stay a single stack (<see cref="Carrying"/>); the pack
/// is worn on the back and holds stacks by weight (<see cref="Simulation.ContainerRoom"/>); gear is
/// <see cref="Apparel"/> and <see cref="Equipment"/>. Everything here takes unspawned items or
/// returns them, and never throws on a stale request: callers check first (<see cref="InOwnPack"/>).
/// </summary>
public static class Inventory
{
    private static readonly ApparelSlot[] Slots = Enum.GetValues<ApparelSlot>();

    /// <summary>
    /// The pack a companion hauls with: theirs, unless they're a player's character or an
    /// adventurer (whose packs are their own, kept between jobs and never emptied by the AI). A
    /// work pack holds nothing but the load of the job at hand, its owner's coin and anything
    /// entrusted to them: ending the job sets the load down (<c>AiSystem.ReleasePawn</c>).
    /// </summary>
    public static Entity WorkPack(Simulation sim, Entity creature) =>
        sim.World.Has<Avatar>(creature) || sim.World.Has<PlayerCharacter>(creature) || sim.World.Has<Adventurer>(creature)
            ? Entity.Null : sim.PackOf(creature);

    /// <summary>Kept in a work pack when a job ends: coin, and what someone handed over to be seen to.</summary>
    public static bool KeptBetweenJobs(Simulation sim, Entity item) =>
        Shops.Coin.IsSilver(sim, item) || sim.World.Has<Entrusted>(item);

    /// <summary>Is this item a stack in the creature's own pack?</summary>
    public static bool InOwnPack(Simulation sim, Entity creature, Entity item) =>
        sim.World.IsAlive(item) && sim.World.TryGet<Stored>(item, out var s) && !s.In.IsNull && s.In == sim.PackOf(creature);

    /// <summary>Is this item something the creature has on it: in hand, in its pack, worn or wielded?</summary>
    public static bool OnPerson(Simulation sim, Entity creature, Entity item)
    {
        var w = sim.World;
        if (!w.IsAlive(item))
            return false;
        if (w.Get<Carrying>(creature).Item == item || InOwnPack(sim, creature, item))
            return true;
        if (w.TryGet<Equipment>(creature, out var eq) && eq.Weapon == item)
            return true;
        if (!w.TryGet<Apparel>(creature, out var worn))
            return false;
        foreach (var slot in Slots)
            if (worn[slot] == item)
                return true;
        return false;
    }

    /// <summary>
    /// Pick up a stack: as much as fits into the pack, the rest (up to a stack) into empty hands.
    /// Returns what's in hand afterwards.
    /// </summary>
    public static Entity PickUp(Simulation sim, Entity avatar, Entity item, Entity held)
    {
        var w = sim.World;
        var pack = sim.PackOf(avatar);
        if (!pack.IsNull)
        {
            var t = w.Get<Thing>(item);
            var room = Math.Min(sim.ContainerRoom(pack, t.Def, t.Material), w.Get<Stack>(item).Count);
            if (room > 0)
            {
                var whole = room >= w.Get<Stack>(item).Count;
                sim.Store(pack, sim.TakeFromStack(item, room));
                if (whole)
                    return held;
            }
        }
        if (!held.IsNull)
            return held;
        var count = Math.Min(w.Get<Stack>(item).Count, w.Get<Thing>(item).Def.StackLimit);
        return sim.TakeFromStack(item, count);
    }

    /// <summary>
    /// Put on or wield an unspawned gear item. What it replaces goes into the pack, or to the ground
    /// when it won't fit (a pack you take off keeps its contents, and lands in empty hands if it can).
    /// </summary>
    public static void PutOn(Simulation sim, Entity avatar, Entity item)
    {
        var w = sim.World;
        var def = w.Get<Thing>(item).Def;
        var old = def.Apparel is { } a
            ? (w.TryGet<Apparel>(avatar, out var worn) ? worn[a.Slot] : Entity.Null)
            : (w.TryGet<Equipment>(avatar, out var eq) ? eq.Weapon : Entity.Null);
        if (!old.IsNull && w.IsAlive(old))
        {
            // Unhook the old piece first, so the new pack (if it's one) doesn't take it in.
            if (def.Apparel is { } slot)
                w.Get<Apparel>(avatar)[slot.Slot] = Entity.Null;
            else
                w.Remove<Equipment>(avatar);
        }
        if (def.Apparel != null)
            sim.WearItem(avatar, item);
        else
            sim.EquipItem(avatar, item);
        if (!old.IsNull && w.IsAlive(old))
        {
            // Trading up packs: what fits moves across, the rest stays in the old one.
            if (def.Container != null && sim.IsPack(old))
                sim.TransferContents(old, item);
            Stow(sim, avatar, old);
        }
    }

    /// <summary>
    /// <paramref name="count"/> units of a stack a creature has in hand or in its pack, as an
    /// unspawned item of its own (the whole stack itself if that's all of it).
    /// </summary>
    public static Entity TakeFromPerson(Simulation sim, Entity who, Entity stack, int count)
    {
        var w = sim.World;
        ref var carrying = ref w.Get<Carrying>(who);
        if (carrying.Item == stack && count >= w.Get<Stack>(stack).Count)
        {
            carrying.Item = Entity.Null;
            return stack;
        }
        return sim.TakeFromStack(stack, count);
    }

    /// <summary>An unspawned item into the pack, else empty hands, else the ground at the avatar's feet.</summary>
    public static void Stow(Simulation sim, Entity avatar, Entity item)
    {
        var w = sim.World;
        var pack = sim.PackOf(avatar);
        var left = pack.IsNull ? item : sim.Store(pack, item);
        if (left.IsNull)
            return;
        ref var carrying = ref w.Get<Carrying>(avatar);
        if (carrying.Item.IsNull)
            carrying.Item = left;
        else
            sim.PlaceItemNear(left, w.Get<Position>(avatar).Cell);
    }


    /// <summary>
    /// Swap a pack stack into the hands; what was held goes into the pack in its place. Refused
    /// (false) when what's held can't go in: bulky, or too heavy once the stack is out.
    /// </summary>
    public static bool Hold(Simulation sim, Entity creature, Entity item)
    {
        var w = sim.World;
        if (!InOwnPack(sim, creature, item))
            return false;
        var pack = sim.PackOf(creature);
        ref var carrying = ref w.Get<Carrying>(creature);
        var held = carrying.Item;
        if (!held.IsNull)
        {
            var h = w.Get<Thing>(held);
            var spare = w.Get<Thing>(pack).Def.Container!.Capacity - (sim.LoadOf(pack) - sim.MassOf(item));
            if (h.Def.Bulky || h.Def.Container != null || sim.MassOf(held) > spare + 1e-4f)
                return false;
        }
        var count = Math.Min(w.Get<Stack>(item).Count, w.Get<Thing>(item).Def.StackLimit);
        var taken = sim.TakeFromStack(item, count);
        if (!held.IsNull && !sim.Store(pack, held).IsNull)
            throw new InvalidOperationException("the swap was weighed, so it must fit");
        w.Get<Carrying>(creature).Item = taken;
        return true;
    }

    /// <summary>Put what's in hand into the pack (as much as fits; the rest stays in hand).</summary>
    public static void StowHands(Simulation sim, Entity creature)
    {
        var pack = sim.PackOf(creature);
        ref var carrying = ref sim.World.Get<Carrying>(creature);
        if (!pack.IsNull && !carrying.Item.IsNull)
            carrying.Item = sim.Store(pack, carrying.Item);
    }

    /// <summary>
    /// Put on or wield an item the creature has (in hand or pack) or one lying in reach. Takes one
    /// unit; what it replaces is stowed (<see cref="PutOn"/>).
    /// </summary>
    public static bool Equip(Simulation sim, Entity creature, Entity item)
    {
        var w = sim.World;
        if (!w.IsAlive(item) || w.Get<Thing>(item).Def is not { } def || (def.Apparel == null && def.Weapon == null))
            return false;
        ref var carrying = ref w.Get<Carrying>(creature);
        var fromHands = carrying.Item == item;
        var lying = w.Get<Thing>(item).Spawned && AvatarSystem.InReach(w.Get<Position>(creature).Cell, w.Get<Thing>(item).Cell);
        if (!fromHands && !lying && !InOwnPack(sim, creature, item))
            return false;
        if (sim.Reservations.IsReservedByOther(item, creature))
            return false;
        var one = sim.TakeFromStack(item, 1);
        if (fromHands && one == item)
            carrying.Item = Entity.Null;
        PutOn(sim, creature, one);
        return true;
    }

    /// <summary>Take off a piece of gear (or put the weapon away) into the pack, else the hands, else the ground.</summary>
    public static void Unequip(Simulation sim, Entity creature, GearSlot slot)
    {
        var w = sim.World;
        Entity item;
        if (Outfit.ApparelOf(slot) is { } a)
        {
            if (!w.TryGet<Apparel>(creature, out var worn) || !w.IsAlive(worn[a]))
                return;
            item = worn[a];
            w.Get<Apparel>(creature)[a] = Entity.Null;
        }
        else
        {
            if (!w.TryGet<Equipment>(creature, out var eq) || !w.IsAlive(eq.Weapon))
                return;
            item = eq.Weapon;
            w.Remove<Equipment>(creature);
        }
        Stow(sim, creature, item);
    }

    /// <summary>Put a stack from the pack down at the creature's feet.</summary>
    public static void DropFromPack(Simulation sim, Entity creature, Entity item)
    {
        if (!InOwnPack(sim, creature, item))
            return;
        sim.PlaceItemNear(sim.TakeFromStack(item, sim.World.Get<Stack>(item).Count), sim.World.Get<Position>(creature).Cell);
    }

    /// <summary>Move a pack stack into a container in reach (as much as fits).</summary>
    public static void StoreFromPack(Simulation sim, Entity creature, Entity container, Entity item)
    {
        var w = sim.World;
        if (!InOwnPack(sim, creature, item) || !CanReachContainer(sim, creature, container))
            return;
        var t = w.Get<Thing>(item);
        var room = Math.Min(sim.ContainerRoom(container, t.Def, t.Material), w.Get<Stack>(item).Count);
        if (room > 0 && !sim.Store(container, sim.TakeFromStack(item, room)).IsNull)
            throw new InvalidOperationException("room was counted, so it must fit");
    }

    /// <summary>
    /// A container the creature may reach into: a storage building or a pack lying on the ground,
    /// within arm's reach (never a pack someone's wearing).
    /// </summary>
    public static bool CanReachContainer(Simulation sim, Entity creature, Entity container)
    {
        var w = sim.World;
        return w.IsAlive(container) && sim.IsContainer(container) && w.Get<Thing>(container).Spawned
            && !w.Has<Construction>(container)
            && AvatarSystem.InReach(sim, w.Get<Position>(creature).Cell, container);
    }
}
