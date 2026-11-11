using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Combat;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;

namespace DragonWorld.Sim.Systems;

/// <summary>The storyteller's wild events (<see cref="Storyteller"/>), besides raids.</summary>
public enum WildEventKind
{
    /// <summary>A wolf pack or a bear comes for the village: a turn-based fight like a raid.</summary>
    Predators,
    /// <summary>A big herd crosses the valley from edge to edge: a few hours' hunting.</summary>
    Migration,
    /// <summary>A grown wild dragon stays a day or two; fed from the village's stores, it may stay for good.</summary>
    DragonVisitor,
}

/// <summary>
/// Animals in the storyteller: predators (defs with <see cref="AnimalProps.Predator"/>, of
/// <see cref="Faction.Predator"/>: they think like raiders and fight in an <see cref="Encounter"/>),
/// migrating herds (<see cref="Migrating"/>) and wild dragon visitors (<see cref="Visitor"/>). Every
/// roll comes from the stream it's given (the storyteller's own), never <see cref="Simulation.Rand"/>.
/// </summary>
public static class WildEvents
{
    /// <summary>Animals in a migrating herd.</summary>
    public const int MigrationMin = 10, MigrationMax = 16;

    /// <summary>Game hours a visiting dragon stays unless it's fed enough to settle.</summary>
    public const int VisitHoursMin = 24, VisitHoursMax = 48;

    /// <summary>Predator kinds the village can be sent today, for the points it has earned.</summary>
    public static List<CreatureDef> PredatorKinds(Simulation sim, float points) =>
        sim.Defs.AllCreatures.Where(c => c.Animal is { Predator: true } a && sim.Clock.Day >= a.MinDay && a.Points * a.PackMin <= points).ToList();

    /// <summary>
    /// A pack of the kind (or one picked from what the points afford) from the map edge, heading for
    /// the village. Returns the animals (none if there was no kind, or nowhere to come from).
    /// </summary>
    public static List<Entity> SendPredators(Simulation sim, SimRandom rand, float points, CreatureDef? kind = null)
    {
        // Nothing affordable yet (only the debug panel asks then): the cheapest pack.
        kind ??= PredatorKinds(sim, points) is { Count: > 0 } kinds ? rand.Pick(kinds)
            : sim.Defs.AllCreatures.Where(c => c.Animal is { Predator: true }).MinBy(c => c.Animal!.Points * c.Animal.PackMin);
        if (kind?.Animal is not { } a || VillageTarget(sim) is not { } target || Entry(sim, rand, target) is not { } entry)
            return [];
        var count = Math.Clamp((int)(points / Math.Max(1f, a.Points)), a.PackMin, a.PackMax);
        var spots = Raids.GatherSpots(sim, entry, target, count);
        var pack = new List<Entity>();
        foreach (var at in spots)
        {
            var e = sim.SpawnCreature(kind, Capital(kind.Label), at, Faction.Predator, ageDays: 3 * 365, rand: rand);
            Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
            pack.Add(e);
        }
        if (pack.Count > 0)
        {
            var who = pack.Count == 1 ? $"A {kind.Label} is" : $"A pack of {pack.Count} {a.Plural} is";
            sim.Messages.Add(MessageKind.Danger, $"{who} coming from the {Raids.Direction(sim.Map, entry)}!", entry);
        }
        return pack;
    }

    /// <summary>A big herd of the valley's starting kind (deer) entering on one edge, bound for the one across.</summary>
    public static List<Entity> SendMigration(Simulation sim, SimRandom rand, CreatureDef? kind = null)
    {
        kind ??= sim.Defs.Wildlife.StartingKindDef ?? sim.Defs.AllCreatures.FirstOrDefault(c => c.Animal is { Commonness: > 0 });
        if (kind == null || VillageTarget(sim) is not { } middle)
            return [];
        var side = rand.NextInt(4);
        if (Raids.EdgeEntry(sim, side, middle) is not { } entry || Raids.EdgeEntry(sim, side ^ 1, middle) is not { } exit)
            return [];
        var count = rand.Range(MigrationMin, MigrationMax + 1);
        var herdId = sim.Wildlife.NewHerdId();
        var herd = new List<Entity>();
        foreach (var at in Raids.GatherSpots(sim, entry, exit, count))
        {
            var e = sim.SpawnCreature(kind, kind.Label, at, Faction.Wild, ageDays: 3 * 365, rand: rand);
            sim.World.Add(e, new Herd { Id = herdId, LeaveTick = long.MaxValue });
            sim.World.Add(e, new Migrating { To = exit });
            herd.Add(e);
        }
        if (herd.Count > 0)
            sim.Messages.Add(MessageKind.Good,
                $"A great herd of {herd.Count} {kind.Animal?.Plural ?? kind.Label} is crossing the valley from the {Raids.Direction(sim.Map, entry)}: a few hours' good hunting.", entry);
        return herd;
    }

    /// <summary>
    /// A grown wild dragon landing at the edge of the village for a day or two: of the other sex to the
    /// village's first grown dragon (so a pair can come of it), breed and element rolled.
    /// </summary>
    public static Entity SendDragonVisitor(Simulation sim, SimRandom rand)
    {
        var w = sim.World;
        if (VillageTarget(sim) is not { } target || Entry(sim, rand, target) is not { } entry)
            return Entity.Null;
        var breeds = Dragons.Breeds(sim.Defs).ToList();
        var breed = rand.Pick(breeds);
        var element = rand.Pick(Enum.GetValues<DragonElement>());
        var cell = sim.SpiralFrom(entry, 6).FirstOrDefault(c => sim.Map.Standable(sim.Map.Idx(c)) && sim.Map.Passable(c), entry);
        var name = Names[rand.NextInt(Names.Length)];
        var dragon = Dragons.Spawn(sim, breed, element, name, cell, Faction.Wild, Dragons.AdultAgeDays(breed) + rand.Range(2f, 30f));
        var mate = w.Query<Dragon>().FirstOrDefault(d => d != dragon && w.Get<Creature>(d) is { Dead: false, Faction: Faction.Village } c
            && c.Stage(sim.Clock.Tick).CanBeRidden, Entity.Null);
        if (!mate.IsNull)
            w.Get<Dragon>(dragon).Sex = w.Get<Dragon>(mate).Sex == DragonSex.Male ? DragonSex.Female : DragonSex.Male;
        w.Get<Needs>(dragon).Values[sim.Defs.Need("Food").Index] = 0.35f; // hungry from the road
        w.Add(dragon, new Visitor { LeaveTick = sim.Clock.Tick + SimClock.HoursToTicks(rand.Range(VisitHoursMin, VisitHoursMax + 1)) });
        var sex = w.Get<Dragon>(dragon).Sex == DragonSex.Male ? "male" : "female";
        sim.Messages.Add(MessageKind.Good,
            $"A wild {Dragons.BreedLabel(breed).ToLowerInvariant()}, {name} ({sex}), has come down by the {Raids.Direction(sim.Map, entry)} edge of the village. Leave food out: fed well, it may stay.",
            cell);
        return dragon;
    }

    private static readonly string[] Names = ["Stormcrest", "Brackenwing", "Halla", "Skarn", "Myrdin", "Thessaly", "Grimsa", "Wyll"];

    /// <summary>The middle of the village: its first person, as wildlife reckons it.</summary>
    private static int3? VillageTarget(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Each<Creature>())
            if (w.Get<Creature>(e) is { Faction: Faction.Village, Dead: false } c && c.Def.Race == Race.Human)
                return w.Get<Position>(e).Cell;
        return null;
    }

    private static int3? Entry(Simulation sim, SimRandom rand, int3 target)
    {
        var first = rand.NextInt(4);
        for (var i = 0; i < 4; i++)
            if (Raids.EdgeEntry(sim, (first + i) % 4, target) is { } at)
                return at;
        return null;
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

/// <summary>
/// Wild dragons visiting (<see cref="Visitor"/>): one fed <see cref="MealsToStay"/> meals from the
/// village's stores (or a keeper's hand) before its time is up joins the village; otherwise it flies off.
/// </summary>
public sealed class VisitorSystem : ISimSystem
{
    public const int MealsToStay = 3;

    public string Name => "Visitors";
    public int Interval => SimClock.TicksPerHour / 4;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Query<Visitor>())
        {
            var v = w.Get<Visitor>(e);
            ref var c = ref w.Get<Creature>(e);
            if (c.Dead)
            {
                w.Remove<Visitor>(e);
                continue;
            }
            if (v.Meals >= MealsToStay)
            {
                w.Remove<Visitor>(e);
                c.Faction = Faction.Village;
                sim.Messages.Add(MessageKind.Good, $"{c.Name} has settled in: the wild dragon is one of the village's now. Make someone its keeper.", w.Get<Position>(e).Cell);
            }
            else if (sim.Clock.Tick >= v.LeaveTick)
            {
                w.Remove<Visitor>(e);
                w.Add(e, new Leaving());
                AiSystem.EndJob(sim, e, w.Get<Mind>(e), false);
                sim.Messages.Add(MessageKind.Info, $"{c.Name}, the wild dragon, is moving on.", w.Get<Position>(e).Cell);
            }
        }
    }

    /// <summary>A visiting dragon ate the village's food (not its own kill): one meal nearer staying.</summary>
    public static void AteVillageFood(Simulation sim, Entity eater)
    {
        if (sim.World.Has<Visitor>(eater))
            sim.World.Get<Visitor>(eater).Meals++;
    }
}
