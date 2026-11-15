using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>On something hung on a smoking rack (<see cref="Larder"/>): hours it's had in the smoke.</summary>
public struct Smoking
{
    public float Hours;
}

/// <summary>
/// Keeping the larder (docs/DESIGN.md, "Cook"). Food turns faster the warmer it's kept, and a
/// kitchen by its fires is warm: the larder keeps cool (<see cref="BuildingProps.KeepsAt"/>), so
/// what's left out turns first. Raw meat and fish rubbed with salt at the prep table keep for weeks,
/// and season a pot by themselves (<see cref="ThingDef.Salty"/>). Hung on the smoking rack while a
/// fire burns close by, they cure in the smoke (<see cref="SmokeSystem"/>) and keep for weeks too.
/// </summary>
public static class Larder
{
    /// <summary>The salt def, and how many portions a scoop of it salts.</summary>
    public const string Salt = "CoarseSalt";
    public const int PerScoop = 4;

    /// <summary>Hours in the smoke to cure.</summary>
    public const float SmokeHours = 6f;

    /// <summary>How near a burning fire a rack has to be to have smoke on it.</summary>
    public const int SmokeReach = 2;

    /// <summary>Something on the cook that salting would keep (raw meat, fish), or Entity.Null.</summary>
    public static Entity Saltable(Simulation sim, Entity cook) => AvatarSystem.Supply(sim, cook, d => d.SaltsInto != null);

    public static Entity SaltOn(Simulation sim, Entity cook) => AvatarSystem.Supply(sim, cook, d => d.Name == Salt);

    public static bool CanSalt(Simulation sim, Entity cook) => !Saltable(sim, cook).IsNull && !SaltOn(sim, cook).IsNull;

    /// <summary>
    /// Rub salt into what's saltable on the cook, a scoop to <see cref="PerScoop"/> portions, as far as
    /// the salt goes: each stack salted whole back where it was. How many portions were salted.
    /// </summary>
    public static int SaltAll(Simulation sim, Entity cook, int3 at)
    {
        var w = sim.World;
        var salted = 0;
        while (Saltable(sim, cook) is { IsNull: false } food && SaltOn(sim, cook) is { IsNull: false } salt)
        {
            var def = w.Get<Thing>(food).Def;
            if (!sim.Defs.TryThing(def.SaltsInto!, out var into))
                break;
            var count = Math.Min(w.Get<Stack>(food).Count, w.Get<Stack>(salt).Count * PerScoop);
            var scoops = (count + PerScoop - 1) / PerScoop;
            Use(sim, cook, salt, scoops);
            Replace(sim, cook, food, into, count, 1f);
            salted += count;
        }
        if (salted > 0)
            sim.Messages.Tell(cook, MessageKind.Info, $"You rub salt into {salted} portion{(salted == 1 ? "" : "s")}: it'll keep for weeks now, and season a pot by itself.", at);
        return salted;
    }

    /// <summary>So many of a stack used up, out of whoever's hands or pack.</summary>
    private static void Use(Simulation sim, Entity cook, Entity stack, int count)
    {
        var w = sim.World;
        ref var hands = ref w.Get<Carrying>(cook);
        var used = sim.TakeFromStack(stack, count);
        if (hands.Item == used)
            hands.Item = Entity.Null;
        w.Destroy(used);
    }

    /// <summary>So many of <paramref name="food"/> turned into <paramref name="into"/>, as far gone (times <paramref name="rotKept"/>), back where they were.</summary>
    private static void Replace(Simulation sim, Entity cook, Entity food, ThingDef into, int count, float rotKept)
    {
        var w = sim.World;
        var rot = FoodPoisoning.RotOf(sim, food);
        var inHand = w.Get<Carrying>(cook).Item == food && w.Get<Stack>(food).Count <= count;
        Use(sim, cook, food, count);
        var made = sim.CreateItem(into, count);
        if (w.Has<Rot>(made))
            w.Get<Rot>(made).Progress = rot * rotKept;
        if (inHand && w.Get<Carrying>(cook).Item.IsNull)
            w.Get<Carrying>(cook).Item = made;
        else
            Inventory.Stow(sim, cook, made);
    }

    public static bool IsRack(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Building is { SmokingRack: true } && !sim.World.Has<Construction>(e);

    /// <summary>Whether a fire's burning close enough to a rack to smoke what's on it.</summary>
    public static bool Smoky(Simulation sim, Entity rack)
    {
        var at = sim.World.Get<Thing>(rack).Cell;
        for (var dy = -SmokeReach; dy <= SmokeReach; dy++)
        for (var dx = -SmokeReach; dx <= SmokeReach; dx++)
        {
            var c = new int3(at.X + dx, at.Y + dy, at.Z);
            if (sim.Map.InBounds(c) && sim.Map.Structure[sim.Map.Idx(c)] is { IsNull: false } s && sim.Burning(s))
                return true;
        }
        return false;
    }
}

/// <summary>What's hung on a smoking rack with a fire burning close by cures, hour by hour, and keeps for weeks once it has.</summary>
public sealed class SmokeSystem : ISimSystem
{
    public string Name => "Smoke cure";
    public int Interval => 250;

    private readonly List<Entity> _racks = [];

    public void Update(Simulation sim)
    {
        var w = sim.World;
        _racks.Clear();
        foreach (var e in w.Each<Thing>())
            if (w.Get<Thing>(e).Def.Building is { SmokingRack: true })
                _racks.Add(e);
        var hours = Interval / (float)SimClock.TicksPerHour;
        foreach (var rack in _racks)
        {
            if (!Larder.IsRack(sim, rack) || !Larder.Smoky(sim, rack))
                continue;
            foreach (var item in sim.StoredIn(rack).ToList())
            {
                var def = w.Get<Thing>(item).Def;
                if (def.SmokesInto is not { } name || !sim.Defs.TryThing(name, out var into))
                    continue;
                if (!w.Has<Smoking>(item))
                    w.Add(item, new Smoking());
                ref var s = ref w.Get<Smoking>(item);
                s.Hours += hours;
                if (s.Hours < Larder.SmokeHours)
                    continue;
                // Cured: the smoked stack takes its place on the rack.
                var count = w.Get<Stack>(item).Count;
                var rot = FoodPoisoning.RotOf(sim, item);
                w.Remove<Stored>(item);
                sim.DestroyThing(item);
                var made = sim.CreateItem(into, count);
                if (w.Has<Rot>(made))
                    w.Get<Rot>(made).Progress = rot * 0.5f;
                var left = sim.Store(rack, made);
                if (!left.IsNull)
                    sim.PlaceItemNear(left, w.Get<Thing>(rack).Cell);
                sim.Messages.Add(MessageKind.Info, $"The {def.Label} on the smoking rack has cured: {count} {into.Label}.", w.Get<Thing>(rack).Cell);
            }
        }
    }
}
