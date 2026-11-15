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

        // The nearest well, and the player beside it with a full bucket.
        var well = Buckets.Wells(sim).OrderBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, fire.Origin)).FirstOrDefault(Entity.Null);
        if (well.IsNull)
            return fire;
        var at = w.Get<Thing>(well).Cell;
        var stand = sim.SpiralFrom(at, 2).First(c => c != at && c.Z == at.Z && sim.Map.Passable(c) && AvatarSystem.InReach(sim, c, well));
        w.Get<Position>(player).Cell = stand;
        w.Get<Position>(player).Pos = Position.CenterOf(stand);
        if (w.Get<Carrying>(player).Item is { IsNull: false } held)
        {
            w.Get<Carrying>(player).Item = Entity.Null;
            Inventory.Stow(sim, player, held);
            if (w.Get<Carrying>(player).Item is { IsNull: false } left) // no room in the pack: set down
            {
                w.Get<Carrying>(player).Item = Entity.Null;
                sim.PlaceItemNear(left, stand);
            }
        }
        w.Get<Carrying>(player).Item = sim.CreateItem(sim.Defs.Thing(Buckets.Full), 1);
        return fire;
    }
}
