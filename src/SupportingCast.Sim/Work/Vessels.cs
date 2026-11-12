using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Work;

/// <summary>
/// The inn's crockery. A drink is poured into a clean tankard and a bowl of stew ladled into a
/// clean bowl; the customer leaves it dirty where they finished (<see cref="ThingDef.Leaves"/>),
/// and it's washed at the tub (<see cref="WashSession"/>) before it's used again. Run out of clean
/// ones and the bar stops, however full the cask.
/// </summary>
public static class Vessels
{
    public static bool IsDirty(ThingDef def) => def.WashesInto != null;

    /// <summary>A clean vessel of a kind on someone: in hand first, then in the pack. Entity.Null if none.</summary>
    public static Entity CleanOn(Simulation sim, Entity who, string def) =>
        AvatarSystem.Supply(sim, who, d => d.Name == def);

    /// <summary>
    /// Ready to pour or ladle: a clean vessel of the kind on them, and their hand either empty or
    /// holding that kind (so the drink or bowl can go in it).
    /// </summary>
    public static bool Ready(Simulation sim, Entity who, string def)
    {
        var held = sim.World.Get<Carrying>(who).Item;
        return !CleanOn(sim, who, def).IsNull && (held.IsNull || sim.World.Get<Thing>(held).Def.Name == def);
    }

    /// <summary>Use up one clean vessel of a kind from someone (hand first, then pack), and free their hand for what goes in it. False if they had none.</summary>
    public static bool Use(Simulation sim, Entity who, string def)
    {
        var w = sim.World;
        var vessel = CleanOn(sim, who, def);
        if (vessel.IsNull)
            return false;
        w.Destroy(Inventory.TakeFromPerson(sim, who, vessel, 1));
        ref var carrying = ref w.Get<Carrying>(who);
        // The rest of the stack goes to the pack, so the drink lands in hand.
        if (!carrying.Item.IsNull && w.Get<Thing>(carrying.Item).Def.Name == def && !sim.PackOf(who).IsNull)
        {
            var rest = carrying.Item;
            carrying.Item = Entity.Null;
            Inventory.Stow(sim, who, rest);
        }
        return true;
    }

    /// <summary>What's left once someone has drunk or eaten this: set down where they are.</summary>
    public static void LeaveAfter(Simulation sim, Entity who, ThingDef consumed)
    {
        if (consumed.Leaves is not { } leaves || !sim.World.TryGet<Position>(who, out var at))
            return;
        sim.SpawnItem(sim.Defs.Thing(leaves), 1, at.Cell);
    }
}
