using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;

namespace SupportingCast.Sim.Systems;

/// <summary>
/// Saddles: dragon apparel for the one slot a dragon has. A rideable dragon (<see cref="LifeStage.CanBeRidden"/>)
/// wears one as <see cref="Saddled"/>; the item is unspawned while it's on, like worn apparel. A saddle
/// steadies its rider's aim (<see cref="SaddleProps.MountedAim"/>), adds a little to the scales under it,
/// and carries saddlebags (<see cref="ThingDef.Container"/>, its contents <see cref="Stored"/> in it,
/// which <see cref="Simulation.IsAvailable"/> treats like a pack's). Riding bareback is still allowed and
/// plays as it always has.
/// </summary>
public static class Saddles
{
    /// <summary>The saddle a dragon has on, or Entity.Null.</summary>
    public static Entity Of(Simulation sim, Entity dragon) =>
        sim.World.TryGet<Saddled>(dragon, out var s) && sim.World.IsAlive(s.Saddle) ? s.Saddle : Entity.Null;

    public static SaddleProps? PropsOf(Simulation sim, Entity dragon) =>
        Of(sim, dragon) is { IsNull: false } s ? sim.World.Get<Thing>(s).Def.Saddle : null;

    /// <summary>Why <paramref name="dragon"/> can't wear <paramref name="item"/>, or null if it can.</summary>
    public static string? CantWear(Simulation sim, Entity dragon, Entity item)
    {
        var w = sim.World;
        if (!w.IsAlive(dragon) || !w.TryGet<Creature>(dragon, out var c) || c.Def.Race != Race.Dragon)
            return "only a dragon wears a saddle";
        if (!w.IsAlive(item) || !w.TryGet<Thing>(item, out var t) || t.Def.Saddle == null)
            return "that isn't a saddle";
        if (!c.Stage(sim.Clock.Tick).CanBeRidden)
            return $"{c.Name} is too small for a saddle";
        if (c.Dead)
            return $"{c.Name} is dead";
        if (w.Has<Mount>(dragon))
            return "not while someone's riding";
        return null;
    }

    /// <summary>
    /// Put a saddle on (taking off any it had, which lands at its feet). The item comes out of
    /// wherever it was (hands, the ground) by the caller; here it must already be unspawned and free.
    /// </summary>
    public static void PutOn(Simulation sim, Entity dragon, Entity item)
    {
        if (CantWear(sim, dragon, item) != null)
            return;
        TakeOff(sim, dragon);
        sim.World.Add(dragon, new Saddled { Saddle = item });
    }

    /// <summary>Take a dragon's saddle off, saddlebags and all, and set it down by the dragon. Returns it (or Entity.Null).</summary>
    public static Entity TakeOff(Simulation sim, Entity dragon, bool placeNear = true)
    {
        var w = sim.World;
        if (!w.TryGet<Saddled>(dragon, out var s))
            return Entity.Null;
        w.Remove<Saddled>(dragon);
        if (!w.IsAlive(s.Saddle))
            return Entity.Null;
        if (placeNear)
            sim.PlaceItemNear(s.Saddle, w.Get<Position>(dragon).Cell);
        return s.Saddle;
    }

    /// <summary>The steadier seat a saddle gives its rider: added to the rider's chance to hit.</summary>
    public static float AimBonus(Simulation sim, Entity rider) =>
        sim.World.TryGet<Riding>(rider, out var r) && sim.World.IsAlive(r.Mount) && PropsOf(sim, r.Mount) is { } p
            ? p.MountedAim * Crafting.QualityFactorOf(sim, Of(sim, r.Mount))
            : 0f;

    /// <summary>What a saddle adds to the dragon's armor over a body part (by name).</summary>
    public static float ArmorOver(Simulation sim, Entity dragon, string part) =>
        PropsOf(sim, dragon) is { } p && p.Covers.Contains(part) ? p.Armor * Crafting.QualityFactorOf(sim, Of(sim, dragon)) : 0f;
}
