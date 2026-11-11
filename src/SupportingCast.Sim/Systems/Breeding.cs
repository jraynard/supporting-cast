using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;

namespace SupportingCast.Sim.Systems;

/// <summary>
/// Dragons raising clutches (docs/plans: Food &amp; dragons, phase 8). A grown female and a grown male of
/// the village, both contented (fed, in good spirits, not sulking, at least accepting of their people)
/// and with a nest free, court: each hour a hash of her and the hour gives her breed's
/// <see cref="BreedProps.MatingChancePerDay"/> (spread over the day) of success. Then she's
/// <see cref="Gravid"/> for <see cref="BreedProps.GestationDays"/> and lays
/// <see cref="BreedProps.ClutchMin"/>..<see cref="BreedProps.ClutchMax"/> eggs in a nest
/// (<see cref="LayClutchJob"/>), each of her breed or his and of her element or his, and won't court
/// again for <see cref="BreedProps.ClutchCooldownDays"/>. No <see cref="Simulation.Rand"/> anywhere,
/// so a village with one dragon (or none) plays exactly as before. The eggs are the village's: they
/// hatch to whoever <see cref="EggSystem"/> finds to keep them.
/// </summary>
public sealed class BreedingSystem : ISimSystem
{
    public string Name => "Breeding";
    public int Interval => SimClock.TicksPerHour;

    public const float MinFood = 0.5f, MinMood = 0.55f; // a nest and some attention from its keeper

    private readonly List<Entity> _females = [], _males = [];

    public void Update(Simulation sim)
    {
        var w = sim.World;
        _females.Clear();
        _males.Clear();
        foreach (var e in w.Each<Dragon>())
            if (Contented(sim, e))
                (w.Get<Dragon>(e).Sex == DragonSex.Male ? _males : _females).Add(e);
        if (_females.Count == 0 || _males.Count == 0)
            return;
        var hour = (int)(sim.Clock.Tick / SimClock.TicksPerHour);
        foreach (var she in _females)
        {
            var breed = w.Get<Creature>(she).Def.Breed ?? new BreedProps();
            if (w.Has<Gravid>(she) || (w.TryGet<LaidClutch>(she, out var last)
                && sim.Clock.Tick - last.Tick < SimClock.DaysToTicks(breed.ClutchCooldownDays)))
                continue;
            if (AiQueries.FindFreeBed(sim, she, nest: true).IsNull)
                continue; // nowhere to lay
            var he = Nearest(sim, she);
            if (he.IsNull)
                continue;
            var roll = SimRandom.Hash(she.Index, hour, (uint)sim.Seed + 0xB12Du) / (float)uint.MaxValue;
            if (roll >= breed.MatingChancePerDay / 24f)
                continue;
            Mate(sim, she, he);
        }
    }

    /// <summary>A grown dragon of the village in a fit state to raise a clutch.</summary>
    public static bool Contented(Simulation sim, Entity e)
    {
        var w = sim.World;
        var c = w.Get<Creature>(e);
        if (!c.Able || c.Faction != Faction.Village || !c.Stage(sim.Clock.Tick).CanBeRidden || w.Has<Chained>(e))
            return false;
        var food = sim.Defs.Need("Food");
        return w.Get<Needs>(e).Get(food) >= MinFood && MoodRules.MoodOf(sim, e) >= MinMood
            && !Bonding.Peek(sim, e).Sulking && Bonding.Reached(sim, e, Bonding.Accepting);
    }

    private Entity Nearest(Simulation sim, Entity she)
    {
        var w = sim.World;
        var at = w.Get<Position>(she).Cell;
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        foreach (var he in _males)
        {
            var d = Maps.Map.Distance(at, w.Get<Position>(he).Cell);
            if (d < bestDist)
            {
                best = he;
                bestDist = d;
            }
        }
        return best;
    }

    /// <summary>They court, and she carries a clutch: laid <see cref="BreedProps.GestationDays"/> from now.</summary>
    public static void Mate(Simulation sim, Entity she, Entity he)
    {
        var w = sim.World;
        var breed = w.Get<Creature>(she).Def.Breed ?? new BreedProps();
        w.Add(she, new Gravid
        {
            LayTick = sim.Clock.Tick + SimClock.DaysToTicks(breed.GestationDays),
            SireBreed = w.Get<Creature>(he).Def.Name,
            SireElement = w.Get<Dragon>(he).Element,
        });
        MoodSystem.Remember(sim, she, sim.Defs.Thoughts.CourtedMe, he);
        MoodSystem.Remember(sim, he, sim.Defs.Thoughts.CourtedMe, she);
        sim.Messages.Add(MessageKind.Good,
            $"{w.Get<Creature>(she).Name} and {w.Get<Creature>(he).Name} have been courting: she'll lay a clutch in a few days. Keep a nest free and warm.",
            w.Get<Position>(she).Cell);
    }

    /// <summary>A new dragon's sex: a hash of the seed and its entity, so spawning one never shifts anyone's rolls.</summary>
    public static DragonSex RollSex(Simulation sim, Entity e) =>
        (SimRandom.Hash(e.Index, e.Generation, (uint)sim.Seed + 0x5E7u) & 1) == 0 ? DragonSex.Female : DragonSex.Male;

    /// <summary>
    /// Lay the clutch where she is (in a nest, if she got to one): its eggs take her breed or the
    /// sire's and her element or his, each from a hash. Returns the eggs.
    /// </summary>
    public static List<Entity> Lay(Simulation sim, Entity she)
    {
        var w = sim.World;
        var eggs = new List<Entity>();
        if (!w.TryGet<Gravid>(she, out var g))
            return eggs;
        var mother = w.Get<Creature>(she).Def;
        var breed = mother.Breed ?? new BreedProps();
        var sire = Dragons.BreedOrDefault(sim.Defs, g.SireBreed);
        var dam = w.Get<Dragon>(she).Element;
        var at = w.Get<Position>(she).Cell;
        var seed = (uint)sim.Seed + 0xE66u;
        var span = Math.Max(0, breed.ClutchMax - breed.ClutchMin) + 1;
        var count = breed.ClutchMin + (int)(SimRandom.Hash(she.Index, (int)(g.LayTick % int.MaxValue), seed) % (uint)span);
        var eggDef = sim.Defs.Thing("DragonEgg");
        for (var i = 0; i < count; i++)
        {
            var h = SimRandom.Hash(she.Index * 31 + i, (int)(g.LayTick % int.MaxValue), seed + 1);
            var egg = sim.CreateItem(eggDef, 1);
            Dragons.ChooseEgg(sim, egg, (h & 1) == 0 ? mother : sire, (h & 2) == 0 ? dam : g.SireElement);
            sim.PlaceItemNear(egg, at);
            eggs.Add(egg);
        }
        w.Remove<Gravid>(she);
        w.Add(she, new LaidClutch { Tick = sim.Clock.Tick });
        sim.Messages.Add(MessageKind.Good, $"{w.Get<Creature>(she).Name} has laid {(count == 1 ? "an egg" : $"{count} eggs")}.", at);
        return eggs;
    }
}
