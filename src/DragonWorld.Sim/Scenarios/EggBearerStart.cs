using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;

namespace DragonWorld.Sim.Scenarios;

/// <summary>
/// Adventure mode on a standard village start: the keeper becomes the player's avatar, holding the egg.
/// The other villagers stay autonomous companions. A stand-in until the prologue (an NPC village,
/// the story beat that drives you out, the wagon) exists.
/// </summary>
public static class EggBearerStart
{
    public static Entity Setup(Simulation sim, string? name = null, Appearance? look = null, IReadOnlyList<string>? traits = null,
        IReadOnlyList<string>? clothes = null)
    {
        var w = sim.World;
        var bearer = Entity.Null;
        foreach (var e in w.Query<Creature>())
        {
            if (w.Get<Creature>(e).Def.Race != Race.Human || w.Get<Creature>(e).Faction != Faction.Village)
                continue;
            if (bearer.IsNull || w.Has<Keeper>(e))
                bearer = e;
            if (w.Has<Keeper>(e))
                break;
        }
        if (bearer.IsNull)
            return bearer;
        w.Add(bearer, new Avatar());
        if (look is { } a)
            w.Add(bearer, a);
        Systems.Clothing.DressHero(sim, bearer, clothes);
        sim.GiveStartingPack(bearer);
        if (!string.IsNullOrWhiteSpace(name))
            w.Get<Creature>(bearer).Name = name.Trim();
        if (traits != null)
            Systems.TraitRules.Set(sim, bearer, traits);

        // The egg goes in the player's arms; everything else they brought rides in the wagon.
        var at = w.Get<Position>(bearer).Cell;
        var wagonCell = sim.SpiralFrom(at + new int3(2, 1, 0), 6)
            .FirstOrDefault(c => sim.Map.Passable(c) && sim.CanPlaceStructure(c, sim.Defs.Thing("Wagon")) && sim.Map.Item[sim.Map.Idx(c)].IsNull, at);
        var wagon = sim.SpawnStructure(sim.Defs.Thing("Wagon"), wagonCell);
        foreach (var item in sim.SpawnedItems().ToList())
        {
            if (Maps.Map.Distance(at, sim.World.Get<Thing>(item).Cell) > 8)
                continue; // only the starting kit, not something lying around the map
            sim.DespawnThing(item);
            if (w.Has<Egg>(item))
            {
                if (!w.Get<Carrying>(bearer).HasItem)
                    w.Get<Carrying>(bearer).Item = item;
                else
                    sim.PlaceItemNear(item, at);
                continue;
            }
            if (sim.Store(wagon, item) is { IsNull: false } left)
                sim.PlaceItemNear(left, wagonCell);
        }
        sim.Messages.Add(MessageKind.Info,
            "You set out alone with the dragon egg. Keep it warm through the night, and build a home others will want to share.",
            w.Get<Position>(bearer).Cell);
        return bearer;
    }
}
