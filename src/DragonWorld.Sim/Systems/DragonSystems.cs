using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Environment;
using DragonWorld.Sim.Maps;

namespace DragonWorld.Sim.Systems;

/// <summary>
/// Incubates eggs: progress only accrues inside the ideal temperature band; outside it the egg
/// is harmed in proportion to how far off it is. Hatches into a dragon bonded to a keeper.
/// </summary>
public sealed class EggSystem : ISimSystem
{
    public string Name => "Eggs";
    public int Interval => 30;

    private const float UpdatesPerDay = SimClock.TicksPerDay / 30f;

    public static readonly string[] DragonNames = ["Ember", "Cinder", "Pyra", "Smolder", "Ashka", "Vesper"];

    public void Update(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Query<Egg>())
        {
            ref var thing = ref w.Get<Thing>(e);
            ref var egg = ref w.Get<Egg>(e);
            var props = thing.Def.Egg!;
            var tile = sim.LocationOf(e);
            var temp = sim.TemperatureAt(tile);

            if (temp >= props.IdealTempMin && temp <= props.IdealTempMax)
            {
                egg.Progress += 1f / (props.IncubationDays * UpdatesPerDay);
            }
            else
            {
                var off = temp < props.IdealTempMin ? props.IdealTempMin - temp : temp - props.IdealTempMax;
                var severity = Math.Clamp(off / 10f, 0.2f, 1.5f);
                thing.Hp -= props.DamagePerDay * thing.MaxHp * severity / UpdatesPerDay;
                sim.Messages.AddThrottled(
                    $"egg:{e.Index}",
                    SimClock.TicksPerHour * 2,
                    MessageKind.Warning,
                    temp < props.IdealTempMin
                        ? $"{EggName(sim, egg)} is getting cold ({temp:F0}°C). It needs at least {props.IdealTempMin:F0}°C."
                        : $"{EggName(sim, egg)} is too hot ({temp:F0}°C).",
                    tile
                );
            }

            if (thing.Hp <= 0)
            {
                sim.Messages.Add(MessageKind.Danger, $"{EggName(sim, egg)} has gone cold and still. It will never hatch.", tile);
                RemoveEgg(sim, e);
                continue;
            }

            if (egg.Progress >= 1f)
                Hatch(sim, e, props, tile);
        }
    }

    /// <summary>"The dragon egg", or "Bea's dragon egg" when it's a player's own.</summary>
    private static string EggName(Simulation sim, in Egg egg) =>
        sim.World.IsAlive(egg.Keeper) && sim.World.TryGet<Creature>(egg.Keeper, out var k) && sim.World.Has<PlayerCharacter>(egg.Keeper)
            ? $"{k.Name}'s dragon egg"
            : "The dragon egg";

    private static void RemoveEgg(Simulation sim, Entity egg)
    {
        foreach (var c in sim.World.Query<Carrying>())
            if (sim.World.Get<Carrying>(c).Item == egg)
                sim.World.Get<Carrying>(c).Item = Entity.Null;
        sim.World.DestroyDeferred(egg);
        sim.DespawnThing(egg);
    }

    private static void Hatch(Simulation sim, Entity egg, EggProps props, int3 tile)
    {
        var (owner, breed, element) = sim.World.Get<Egg>(egg) is var eg ? (eg.Keeper, eg.Breed, eg.Element) : default;
        RemoveEgg(sim, egg);
        var name = DragonNames[sim.Rand.NextInt(DragonNames.Length)];
        var dragon = Dragons.Spawn(sim, breed ?? props.HatchesIntoDef, element, name, tile);
        var needs = sim.World.Get<Needs>(dragon);
        needs.Values[sim.Defs.Need("Food").Index] = 0.4f; // hatchlings are born hungry

        // A player's own egg imprints on them; anyone else's on a free keeper or the nearest villager.
        var keeper = sim.World.IsAlive(owner) && sim.World.TryGet<Creature>(owner, out var oc) && !oc.Dead ? owner : FindKeeper(sim, tile);
        if (!keeper.IsNull)
            AssignKeeper(sim, dragon, keeper);
        MoodSystem.RememberVillage(sim, sim.Defs.Thoughts.EggHatched);
        if (!keeper.IsNull)
            MoodSystem.Remember(sim, keeper, sim.Defs.Thoughts.ImprintedOnMe, dragon);
        sim.Messages.Add(
            MessageKind.Good,
            keeper.IsNull
                ? $"The egg has hatched! A dragon hatchling named {name} has joined the village."
                : $"The egg has hatched! {name} imprints on {sim.World.Get<Creature>(keeper).Name}.",
            tile
        );
    }

    /// <summary>An existing keeper without a dragon, else the nearest villager.</summary>
    private static Entity FindKeeper(Simulation sim, int3 near)
    {
        var w = sim.World;
        foreach (var e in w.Query<Keeper>())
            if (!w.IsAlive(w.Get<Keeper>(e).Dragon) && !w.Get<Creature>(e).Dead && !OwnsAnEgg(sim, e))
                return e;
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        foreach (var e in sim.Creatures())
        {
            if (w.Get<Creature>(e).Def.Race != Race.Human || w.Get<Creature>(e).Faction != Faction.Village || w.Has<Keeper>(e))
                continue;
            var t = w.Get<Position>(e).Cell;
            var d = Math.Abs(t.X - near.X) + Math.Abs(t.Y - near.Y);
            if (d < bestDist)
            {
                best = e;
                bestDist = d;
            }
        }
        return best;
    }

    // A player waiting on their own egg isn't free to take someone else's hatchling.
    private static bool OwnsAnEgg(Simulation sim, Entity keeper)
    {
        foreach (var egg in sim.World.Each<Egg>())
            if (sim.World.Get<Egg>(egg).Keeper == keeper)
                return true;
        return false;
    }

    public static void AssignKeeper(Simulation sim, Entity dragon, Entity human)
    {
        var w = sim.World;
        ref var d = ref w.Get<Dragon>(dragon);
        if (w.IsAlive(d.Keeper))
            w.Remove<Keeper>(d.Keeper);
        d.Keeper = human;
        w.Add(human, new Keeper { Dragon = dragon });
    }
}

/// <summary>
/// Dragon life: announces growth into new life stages and makes young dragons sneeze fire —
/// more often when neglected. Sneezes ignite flammable ground and plants next to the dragon, but
/// not buildings.
/// </summary>
public sealed class DragonSystem : ISimSystem, Saving.ISaveState
{
    public string Name => "Dragons";
    public int Interval => 60;

    private const float UpdatesPerHour = SimClock.TicksPerHour / 60f;
    private readonly Dictionary<Entity, string> _lastStage = [];

    /// <summary>Percent chance a day that a grown dragon sheds a scale or two (hashed, so it never shifts sim.Rand).</summary>
    public const int ShedChancePerDay = 30;

    /// <summary>
    /// Dragons shed scales as they grow (a handful at each new stage) and now and then after:
    /// <c>DragonScale</c> items for the tanning rack. Hatchlings' scales are too soft to keep.
    /// </summary>
    private static void Shed(Simulation sim, Entity dragon, Creature c, LifeStage stage, int3 tile, bool growing)
    {
        // Only on the ground: a scale shed in flight would have nowhere to land.
        if (stage.Armor < 0.2f || !sim.Map.Standable(sim.Map.Idx(tile)) || !sim.Defs.TryThing("DragonScale", out var scale))
            return;
        // Its scales itch for a day after: grooming then brings more away (Bonding).
        Bonding.Care(sim, dragon).ItchyUntil = sim.Clock.Tick + Bonding.ItchyTicks;
        var count = growing ? (int)MathF.Round(stage.Size * 3) : 1 + (int)(SimRandom.Hash(dragon.Index, (int)sim.Clock.Tick, 31) % 2);
        sim.SpawnItem(scale, count, tile);
        if (growing)
            sim.Messages.Add(MessageKind.Info, $"{c.Name} shed some of its old scales.", tile);
    }


    // The stage each dragon was last seen in, so a load doesn't announce growth that already happened.
    public void Save(Saving.SaveWriter w, Simulation sim)
    {
        w.Int(_lastStage.Count);
        foreach (var (e, stage) in _lastStage.OrderBy(kv => kv.Key.Index))
        {
            w.Entity(e);
            w.String(stage);
        }
    }

    public void Load(Saving.SaveReader r, Simulation sim)
    {
        _lastStage.Clear();
        var n = r.Count();
        for (var i = 0; i < n; i++)
            _lastStage[r.Entity()] = r.String();
    }

    public void Update(Simulation sim)
    {
        var w = sim.World;
        var bond = sim.Defs.Need("Bond");
        foreach (var e in w.Query<Dragon>())
        {
            var c = w.Get<Creature>(e);
            if (c.Dead)
                continue;
            var stage = c.Stage(sim.Clock.Tick);
            var tile = w.Get<Position>(e).Cell;

            if (_lastStage.TryGetValue(e, out var last) && last != stage.Name)
            {
                sim.Messages.Add(MessageKind.Good, $"{c.Name} has grown into a {stage.Label}!", tile);
                Shed(sim, e, c, stage, tile, growing: true);
            }
            else if (sim.Clock.Tick % SimClock.TicksPerDay < Interval
                     && SimRandom.Hash(e.Index, (int)(sim.Clock.Tick / SimClock.TicksPerDay), (uint)sim.Seed + 7717) % 100 < ShedChancePerDay)
                Shed(sim, e, c, stage, tile, growing: false);
            _lastStage[e] = stage.Name;
            Neglect(sim, e, c, stage, w.Get<Needs>(e), bond);

            Breath.Prune(sim);
            if (stage.FireSneezePerHour <= 0 || w.Get<Mind>(e).Asleep)
                continue;

            // Lonely or hungry dragons are twitchier.
            var needs = w.Get<Needs>(e);
            var stress = 1f + 1.5f * (1f - needs.Get(bond)) + (1f - needs.Get(sim.Defs.Need("Food")));
            var held = Bonding.Knows(sim, e, TrickEffect.HoldFire) ? 0.25f : 1f; // the "hold fire" trick
            if (!sim.Rand.Chance(stage.FireSneezePerHour * stress * held / UpdatesPerHour))
                continue;

            // A sneeze is a tiny, unaimed breath that scorches but spares buildings.
            var d = Map.Neighbors8[sim.Rand.NextInt(8)];
            var target = Map.Offset(tile, d);
            var mouth = w.Get<Position>(e).Pos + new float3(0, 0, stage.Size * 0.4f);
            var breath = Breath.Breathe(sim, mouth, Position.CenterOf(target), new BreathShape(1.8f, 40f, 3, 0.4f, 0.8f, SparesBuildings: true), e);
            var ignited = breath.Ignited > 0;
            var puff = breath.Element == DragonElement.Fire ? "flame" : Dragons.BreathOf(sim, e).Noun; // a sneeze of its own element
            sim.Messages.Add(
                ignited ? MessageKind.Warning : MessageKind.Info,
                ignited ? $"{c.Name} sneezed {(breath.Element == DragonElement.Fire ? "fire" : puff)} and set something alight!" : $"{c.Name} sneezed a puff of {puff}.",
                target
            );
        }
    }

    /// <summary>Affinity lost per day while the dragon's bond, or its food, is at the urgent level.</summary>
    public const float LonelyAffinityPerDay = 0.15f, HungryAffinityPerDay = 0.1f;

    /// <summary>
    /// Affinity only grows with care (feeding, bonding, riding): a dragon left lonely or hungry
    /// loses it, and with it obedience, its willingness to work and to be ridden.
    /// </summary>
    private static void Neglect(Simulation sim, Entity e, Creature c, LifeStage stage, Needs needs, NeedDef bond)
    {
        var food = sim.Defs.Need("Food");
        var loss = (needs.Get(bond) < bond.UrgentBelow ? LonelyAffinityPerDay : 0f) + (needs.Get(food) < food.UrgentBelow ? HungryAffinityPerDay : 0f);
        ref var dragon = ref sim.World.Get<Dragon>(e);
        if (loss <= 0 || dragon.Affinity <= 0)
            return;
        var before = dragon.Affinity;
        dragon.Affinity = Math.Max(0f, dragon.Affinity - loss / (UpdatesPerHour * 24f));
        var at = sim.World.Get<Position>(e).Cell;
        if (stage.CanBeRidden && before >= stage.MinRideAffinity && dragon.Affinity < stage.MinRideAffinity)
            sim.Messages.Add(MessageKind.Warning, $"{c.Name} seems distant: it won't be ridden until it trusts its keeper again.", at);
        else
            sim.Messages.AddThrottled($"neglect:{e.Index}", SimClock.TicksPerHour * 12, MessageKind.Warning,
                $"{c.Name} feels neglected: its affinity is slipping.", at);
    }

    /// <summary>
    /// The player orders a dragon to breathe fire at a cell. Only stages with breath can; whether
    /// it obeys depends on its affinity with the village. Returns true if it set off.
    /// </summary>
    public static bool OrderBreath(Simulation sim, Entity dragon, int3 target, int puffs = 4)
    {
        var w = sim.World;
        var c = w.Get<Creature>(dragon);
        var stage = c.Stage(sim.Clock.Tick);
        if (c.Dead || stage.BreathRange <= 0)
        {
            sim.Messages.Add(MessageKind.Info, $"{c.Name} is too young to breathe fire on command.", target);
            return false;
        }
        if (!sim.Rand.Chance(Obedience(sim, dragon, WorkType.Burn)))
        {
            sim.Messages.Add(MessageKind.Info, Bonding.Peek(sim, dragon).Sulking ? $"{c.Name} is sulking, and ignores the order." : $"{c.Name} ignores the order.", target);
            return false;
        }
        AiSystem.Order(sim, dragon, new BreatheFireJob(target, puffs));
        return true;
    }

    /// <summary>
    /// Chance the dragon does what it's told: from a coin flip up to always, with affinity, and
    /// better with its keeper on its back (<see cref="RidingSystem.MountedObedience"/>), nudged by
    /// mood (<see cref="MoodRules.ObedienceShift"/>) and the Steady trick when mounted. A sulking
    /// dragon does nothing it's asked.
    /// </summary>
    public static float Obedience(Simulation sim, Entity dragon)
    {
        if (Bonding.Peek(sim, dragon).Sulking)
            return 0f;
        var mounted = sim.World.Has<Mount>(dragon) ? RidingSystem.MountedObedience + Bonding.SteadyObedience(sim, dragon) : 0f;
        return Math.Clamp(0.5f + 0.5f * sim.World.Get<Dragon>(dragon).Affinity + mounted
            + MoodRules.ObedienceShift(MoodRules.MoodOf(sim, dragon)), 0.5f, 1f);
    }

    /// <summary><see cref="Obedience(Simulation, Entity)"/> for a kind of work: tricks it's been taught for it add on.</summary>
    public static float Obedience(Simulation sim, Entity dragon, WorkType work) =>
        Bonding.Peek(sim, dragon).Sulking ? 0f : Math.Min(1f, Obedience(sim, dragon) + Bonding.TrickObedience(sim, dragon, work));

    /// <summary>
    /// Mark cells for dragons to burn (see <see cref="Designation.Burn"/>). Only cells with
    /// something flammable are marked; returns how many.
    /// </summary>
    public static int DesignateBurn(Simulation sim, IEnumerable<int3> cells)
    {
        var marked = 0;
        foreach (var c in cells)
        {
            var i = sim.Map.Idx(c);
            if (FireSystem.Fuel(sim, i) <= 0)
                continue;
            sim.Map.Designate(i, Designation.Burn);
            marked++;
        }
        return marked;
    }
}
