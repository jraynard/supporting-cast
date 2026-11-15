using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Environment;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>
/// A fire to fight on demand (<c>--fire-test</c>): the inn alight, and the player at the nearest
/// well with a full bucket in hand. It's a real <see cref="FireCrisis"/>, so the bell, the town
/// coming with buckets and the outcome all run as they would in a round's beat.
/// </summary>
public static class FireDrill
{
    public const string Building = "Inn";

    /// <summary>Cells alight to begin with, round where it starts: a blaze, not a spark the AI puts out first.</summary>
    public const int Spread = 4;

    /// <summary>Set the inn alight and stand the player at the well with water. Call once the round's dealt.</summary>
    public static FireCrisis? Stage(Simulation sim, Entity player)
    {
        var w = sim.World;
        if (!sim.Director.Enabled)
            sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        var inn = w.Query<PresetBuilding>().FirstOrDefault(b => w.Get<PresetBuilding>(b).Def.Name == Building, Entity.Null);
        if (inn.IsNull)
            throw new InvalidOperationException("The fire drill needs an inn on the map.");
        var fire = sim.Director.SendFire(sim, inn) ?? throw new InvalidOperationException("Nothing in the inn would burn.");
        var lit = 1;
        foreach (var c in sim.SpiralFrom(fire.Origin, 3))
            if (lit < Spread && c != fire.Origin && Buildings.At(sim, c) == inn && sim.Fire.Ignite(sim, c, 1f))
                lit++;

        StandAtWell(sim, player, fire.Origin);
        return fire;
    }

    /// <summary>
    /// Stand the player beside the well nearest <paramref name="near"/>, a full bucket in hand
    /// (what they held goes to their pack). False if the town has no well.
    /// </summary>
    public static bool StandAtWell(Simulation sim, Entity player, int3 near)
    {
        var w = sim.World;
        var well = Buckets.Wells(sim).OrderBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, near)).FirstOrDefault(Entity.Null);
        if (well.IsNull)
            return false;
        var at = w.Get<Thing>(well).Cell;
        var stand = sim.SpiralFrom(at, 2).First(c => c != at && c.Z == at.Z && sim.Map.Passable(c) && AvatarSystem.InReach(sim, c, well));
        MoveTo(sim, player, stand);
        PutInHand(sim, player, sim.CreateItem(sim.Defs.Thing(Buckets.Full), 1));
        return true;
    }

    /// <summary>Put the player on a cell (staging only: no walk).</summary>
    public static void MoveTo(Simulation sim, Entity player, int3 cell)
    {
        sim.World.Get<Position>(player).Cell = cell;
        sim.World.Get<Position>(player).Pos = Position.CenterOf(cell);
    }

    /// <summary>Hand the player an item; what they held goes to their pack, or is set down if there's no room.</summary>
    public static void PutInHand(Simulation sim, Entity player, Entity item)
    {
        var w = sim.World;
        if (w.Get<Carrying>(player).Item is { IsNull: false } held)
        {
            w.Get<Carrying>(player).Item = Entity.Null;
            Inventory.Stow(sim, player, held);
            if (w.Get<Carrying>(player).Item is { IsNull: false } left) // no room in the pack: set down
            {
                w.Get<Carrying>(player).Item = Entity.Null;
                sim.PlaceItemNear(left, w.Get<Position>(player).Cell);
            }
        }
        w.Get<Carrying>(player).Item = item;
    }
}
