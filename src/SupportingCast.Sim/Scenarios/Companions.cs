using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Scenarios;

/// <summary>
/// The dragon an Adventure starts with: an adult the player has flown with for years, named and
/// coloured in the dragon creator. It's bonded already (well past what riding and commanding it
/// need), so the story can start with the two of them rather than with an egg.
/// </summary>
public static class Companions
{
    /// <summary>How far into adulthood a starting dragon is (the Hearth dragon grows up at 12, so 16 days old).</summary>
    public const float DaysGrown = 4f;

    /// <summary>The age a starting dragon of <paramref name="breed"/> is: a few days into adulthood.</summary>
    public static float StartingAgeDays(CreatureDef breed) => Dragons.AdultAgeDays(breed) + DaysGrown;

    /// <summary>
    /// A bargained-for partner starts out trusting (it lets you ride, and listens four times in
    /// five): its devotion is yours to earn (docs/plans/dragon-interactions.md).
    /// </summary>
    public const float PartnerAffinity = 0.6f;

    public const string DefaultName = "Vesper";

    /// <summary>Longest dragon name accepted (from the creator or the network).</summary>
    public const int MaxNameLength = 24;

    /// <summary>
    /// Spawn the keeper's adult dragon on a free cell near <paramref name="near"/>, fed and rested,
    /// bonded to <paramref name="keeper"/>. A null <paramref name="look"/> leaves the renderer's derived one
    /// (and a Fire dragon); a null <paramref name="breed"/> is the default breed. <paramref name="ageDays"/>
    /// defaults to a few days into the breed's adulthood.
    /// </summary>
    public static Entity SpawnBondedDragon(Simulation sim, Entity keeper, string? name, DragonLook? look, int3 near, float? ageDays = null, CreatureDef? breed = null)
    {
        var w = sim.World;
        var cell = sim.SpiralFrom(near, 8)
            .FirstOrDefault(c => sim.Map.Passable(c) && sim.Map.Structure[sim.Map.Idx(c)].IsNull, near);
        var def = Dragons.BreedOrDefault(sim.Defs, breed);
        var dragon = sim.SpawnCreature(def, CleanName(name), cell, ageDays: ageDays ?? StartingAgeDays(def));
        Array.Fill(w.Get<Needs>(dragon).Values, 0.9f);
        EggSystem.AssignKeeper(sim, dragon, keeper);
        w.Get<Dragon>(dragon).Affinity = PartnerAffinity;
        w.Get<Dragon>(dragon).Flights = 1; // flown together for years: no "first flight" announcement
        if (look is { } l)
        {
            w.Add(dragon, l.Valid());
            Dragons.SetElement(sim, dragon, l.Valid().Element);
        }
        return dragon;
    }

    /// <summary>A dragon name from the creator or the network: no control characters, trimmed, capped.</summary>
    public static string CleanName(string? name)
    {
        var s = new string((name ?? "").Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (s.Length > MaxNameLength)
            s = s[..MaxNameLength].Trim();
        return s.Length == 0 ? DefaultName : s;
    }

    /// <summary>The living dragon <paramref name="keeper"/> keeps, or Entity.Null.</summary>
    public static Entity DragonOf(Simulation sim, Entity keeper)
    {
        var w = sim.World;
        if (!w.IsAlive(keeper) || !w.TryGet<Keeper>(keeper, out var k) || !w.IsAlive(k.Dragon))
            return Entity.Null;
        return w.TryGet<Creature>(k.Dragon, out var c) && !c.Dead && w.Get<Dragon>(k.Dragon).Keeper == keeper ? k.Dragon : Entity.Null;
    }
}
