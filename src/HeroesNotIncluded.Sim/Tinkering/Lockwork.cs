using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tinkering;

/// <summary>
/// Strongboxes and the Tinker's lockwork (docs/DESIGN.md, "Tinker"). A party
/// that clears a quest brings back a locked box with part of the reward inside, and often a trap
/// tied to the lid. Feeling along the seam for the trap (<see cref="ProbeSession"/>) comes first;
/// picking the lock (<see cref="PickSession"/>) with the trap still armed sets it off as the lid
/// comes up, if a slip doesn't first.
/// </summary>
public static class Lockwork
{
    public const string StrongboxDef = "Strongbox";
    public const string PicksKind = "Lockpicks";
    public const int MinPins = 3, MaxPins = 6;

    /// <summary>Lowest and highest pin heights (0..1 of the lift), so no pin sits at the stop.</summary>
    public const float PinLow = 0.15f, PinHigh = 0.85f;

    /// <summary>A new strongbox from a quest of this danger, holding <paramref name="silver"/>. Harder places lock harder and trap more often.</summary>
    public static Entity Make(Simulation sim, SimRandom rand, float danger, int silver, string from)
    {
        var box = sim.CreateItem(sim.Defs.Thing(StrongboxDef), 1);
        var trapped = rand.Chance(Math.Clamp(0.3f + 0.1f * danger, 0.3f, 0.85f));
        sim.World.Add(box, new Strongbox
        {
            Pins = Math.Clamp(MinPins + (danger > 3f ? 1 : 0) + (danger > 4.5f ? 1 : 0) + rand.NextInt(2), MinPins, MaxPins),
            LockSeed = rand.NextUInt() | 1u,
            Locked = true,
            Trap = trapped ? (TrapKind)(1 + rand.NextInt(3)) : TrapKind.None,
            TrapAt = rand.Range(0.15f, 0.85f),
            Armed = trapped,
            Silver = silver,
            SearchedBy = Entity.Null,
            From = from,
        });
        return box;
    }

    public static bool IsBox(Simulation sim, Entity item) => sim.World.IsAlive(item) && sim.World.Has<Strongbox>(item);

    public static bool IsLocked(Simulation sim, Entity item) => IsBox(sim, item) && sim.World.Get<Strongbox>(item).Locked;

    /// <summary>The pin heights of a lock (0..1 of the lift), the same every time for the same box.</summary>
    public static float[] PinHeights(uint seed, int pins)
    {
        var rand = new SimRandom(seed);
        var heights = new float[Math.Clamp(pins, 1, MaxPins)];
        for (var i = 0; i < heights.Length; i++)
            heights[i] = rand.Range(PinLow, PinHigh);
        return heights;
    }

    /// <summary>Lockpicks on the worker's belt, in their hands or in their pack.</summary>
    public static Entity Picks(Simulation sim, Entity worker)
    {
        var belt = Toolbelt.Best(sim, worker, PicksKind);
        if (!belt.IsNull)
            return belt;
        var held = sim.World.Get<Carrying>(worker).Item;
        if (IsPicks(sim, held))
            return held;
        var pack = sim.PackOf(worker);
        return pack.IsNull ? Entity.Null : sim.StoredIn(pack).FirstOrDefault(i => IsPicks(sim, i), Entity.Null);
    }

    private static bool IsPicks(Simulation sim, Entity e) =>
        sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Tool is { Kind: PicksKind };

    /// <summary>Can this worker work at this box's lock now? Null if so, else why not.</summary>
    public static string? Problem(Simulation sim, Entity worker, Entity box)
    {
        if (!IsBox(sim, box) || !Inventory.OnPerson(sim, worker, box))
            return "you need the box in hand or in your pack";
        if (!sim.World.Get<Strongbox>(box).Locked)
            return "it's already open";
        if (Picks(sim, worker).IsNull)
            return "you need lockpicks";
        return null;
    }

    public static string TrapLabel(TrapKind trap) => trap switch
    {
        TrapKind.Needle => "a poisoned needle",
        TrapKind.Blade => "a spring blade",
        TrapKind.Powder => "a charge of burning powder",
        _ => "nothing",
    };

    /// <summary>
    /// The trap goes off in the worker's hands: a wound to an arm, of the trap's kind (one for the
    /// healer). Disarms it either way: a trap only goes off once.
    /// </summary>
    public static void Spring(Simulation sim, Entity worker, Entity box)
    {
        var w = sim.World;
        ref var b = ref w.Get<Strongbox>(box);
        if (!b.Armed)
            return;
        b.Armed = false;
        var (type, damage, what) = b.Trap switch
        {
            TrapKind.Needle => (DamageType.Venom, 7f, "A needle jumps from the hasp into"),
            TrapKind.Blade => (DamageType.Cut, 9f, "A blade snaps out of the lid across"),
            _ => (DamageType.Burn, 8f, "Powder flares in the lock and sears"),
        };
        var arm = -1;
        if (w.TryGet<Health>(worker, out var h))
            arm = h.Body.Parts.FindIndex(p => p.Name == (sim.Rand.Chance(0.5f) ? "LeftArm" : "RightArm"));
        HealthSystem.Damage(sim, worker, damage, type, arm);
        var name = w.Get<Creature>(worker).Name;
        sim.Messages.Add(MessageKind.Warning, $"{what} {name}'s {(arm >= 0 && h != null ? h.Body.Parts[arm].Label : "hand")}: the strongbox was trapped.", sim.LocationOf(worker));
    }

    /// <summary>The lid comes up: an armed trap goes off now.</summary>
    public static void Open(Simulation sim, Entity worker, Entity box)
    {
        sim.World.Get<Strongbox>(box).Locked = false;
        Spring(sim, worker, box);
        sim.Messages.Add(MessageKind.Good, $"{sim.World.Get<Creature>(worker).Name} gets the strongbox open.", sim.LocationOf(worker));
    }

    /// <summary>A slip with the picks wears them; worn through, they snap.</summary>
    public static void WearPicks(Simulation sim, Entity worker, float fraction)
    {
        var picks = Picks(sim, worker);
        if (picks.IsNull)
            return;
        ref var t = ref sim.World.Get<Thing>(picks);
        t.Hp -= t.MaxHp * fraction;
        if (t.Hp > 0)
            return;
        sim.Messages.Add(MessageKind.Warning, $"{sim.World.Get<Creature>(worker).Name}'s last pick snaps.", sim.LocationOf(worker));
        sim.DestroyThing(picks);
    }

    /// <summary>A locked strongbox an adventurer has with them, to be opened (Entity.Null if none).</summary>
    public static Entity LockedBoxOf(Simulation sim, Entity who)
    {
        var w = sim.World;
        var held = w.Get<Carrying>(who).Item;
        if (IsLocked(sim, held))
            return held;
        var pack = sim.PackOf(who);
        return pack.IsNull ? Entity.Null : sim.StoredIn(pack).FirstOrDefault(i => IsLocked(sim, i), Entity.Null);
    }
}
