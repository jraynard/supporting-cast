using DragonWorld.Sim.Combat;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Maps;

namespace DragonWorld.Sim.Systems;

/// <summary>
/// Adventure mode: the settlement grows by making room for people. A finished bed in an enclosed room
/// that nobody owns gets marked <see cref="AwaitingSettler"/>; some hours later a newcomer walks in
/// from the map edge, joins the village and claims it. Off unless there's an avatar, so the overseer
/// game is untouched.
/// </summary>
public sealed class SettlerSystem : ISimSystem
{
    public string Name => "Settlers";
    public int Interval => SimClock.TicksPerHour / 4;

    /// <summary>Hours between a bed becoming ready and its newcomer arriving.</summary>
    public const int MinDelayHours = 6, MaxDelayHours = 18;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        if ((w.Count<Avatar>() == 0 && w.Count<PlayerCharacter>() == 0) || w.Count<Quest>() > 0)
            return; // no players (overseer mode), or still in the prologue's village (its beds aren't yours to fill)
        var owned = new HashSet<Entity>();
        foreach (var e in w.Each<BedOwner>())
            owned.Add(w.Get<BedOwner>(e).Bed);

        foreach (var bed in w.Query<Thing>())
        {
            var ready = IsHomeBed(sim, bed) && !owned.Contains(bed);
            var waiting = w.TryGet<AwaitingSettler>(bed, out var wait);
            if (!ready)
            {
                if (waiting)
                    w.Remove<AwaitingSettler>(bed);
                continue;
            }
            if (!waiting)
            {
                var hours = sim.Rand.Range(MinDelayHours, MaxDelayHours + 1);
                w.Add(bed, new AwaitingSettler { ArriveTick = sim.Clock.Tick + SimClock.HoursToTicks(hours) });
                sim.Messages.Add(MessageKind.Info, "A bed stands ready under your roof. Word of it will spread.", w.Get<Thing>(bed).Cell);
                continue;
            }
            if (sim.Clock.Tick < wait.ArriveTick)
                continue;
            w.Remove<AwaitingSettler>(bed);
            Arrive(sim, bed);
        }
    }

    /// <summary>A finished, ordinary bed (not a nest or prisoner bed) indoors.</summary>
    public static bool IsHomeBed(Simulation sim, Entity e)
    {
        var w = sim.World;
        var thing = w.Get<Thing>(e);
        if (!thing.Spawned || thing.Def.Building is not { IsBed: true, IsNest: false } || w.Has<Construction>(e) || w.Has<ForPrisoners>(e))
            return false;
        return sim.Rooms.RoomAt(thing.Cell) is { Indoors: true };
    }

    /// <summary>A newcomer walks in from the nearest reachable map edge and takes the bed.</summary>
    public static Entity Arrive(Simulation sim, Entity bed)
    {
        var w = sim.World;
        var home = w.Get<Thing>(bed).Cell;
        var side = sim.Rand.Range(0, 4);
        int3? entry = null;
        for (var k = 0; k < 4 && entry == null; k++)
            entry = Raids.EdgeEntry(sim, (side + k) % 4, home);
        var at = entry ?? home;

        var taken = new HashSet<string>();
        foreach (var e in w.Each<Creature>())
            taken.Add(w.Get<Creature>(e).Name);
        var names = MapGenerator.VillagerNames;
        var first = sim.Rand.Range(0, names.Length);
        var name = names[first];
        for (var k = 0; k < names.Length; k++)
            if (!taken.Contains(names[(first + k) % names.Length]))
            {
                name = names[(first + k) % names.Length];
                break;
            }

        var person = sim.SpawnCreature(sim.Defs.Creature("Human"), name, at, ageDays: sim.Rand.Range(18, 46) * 365f);
        Array.Fill(w.Get<Needs>(person).Values, 0.8f);
        w.Add(person, new BedOwner { Bed = bed });
        sim.GiveStartingPack(person);
        Clothing.DressVillager(sim, person);
        sim.Messages.Add(MessageKind.Good,
            entry == null ? $"{name} has come to live with you." : $"{name} has heard of your home and is coming from the {Raids.Direction(sim.Map, at)} to live with you.",
            at);
        return person;
    }
}
