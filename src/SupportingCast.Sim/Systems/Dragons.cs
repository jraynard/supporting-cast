using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;

namespace SupportingCast.Sim.Systems;

/// <summary>
/// The one place that knows what dragon breeds there are (creature defs with <see cref="BreedProps"/>,
/// docs/plans/dragon-types.md). Spawns, eggs, the creator and the network go through here, so a
/// breed from a save, a command or a roster file is always a real one.
/// </summary>
public static class Dragons
{
    /// <summary>The breeds in creator order.</summary>
    public static IEnumerable<CreatureDef> Breeds(DefDatabase defs) =>
        defs.AllCreatures.Where(c => c.Breed != null).OrderBy(c => c.Breed!.Order).ThenBy(c => c.Name, StringComparer.Ordinal);

    /// <summary>The breed eggs and demos hatch into when nothing says otherwise (the Hearth dragon).</summary>
    public static CreatureDef DefaultBreed(DefDatabase defs) => Breeds(defs).First();

    /// <summary>A breed, or the default one for anything that isn't (null, a human sent by a hostile peer).</summary>
    public static CreatureDef BreedOrDefault(DefDatabase defs, CreatureDef? breed) =>
        breed is { Breed: not null } ? breed : DefaultBreed(defs);

    /// <summary>A breed by def name (roster files, travel parties), or the default.</summary>
    public static CreatureDef BreedOrDefault(DefDatabase defs, string? name) =>
        BreedOrDefault(defs, name != null && defs.AllCreatures.FirstOrDefault(c => c.Name == name) is { } d ? d : null);

    /// <summary>The age a breed grows up at (its last life stage's start).</summary>
    public static float AdultAgeDays(CreatureDef breed) => breed.LifeStages.Count > 0 ? breed.LifeStages[^1].MinAgeDays : 0f;

    /// <summary>A dragon of a breed (the default for null or a non-breed) breathing <paramref name="element"/>.</summary>
    public static Entity Spawn(Simulation sim, CreatureDef? breed, DragonElement element, string name, int3 cell,
        Faction faction = Faction.Village, float ageDays = 0)
    {
        var dragon = sim.SpawnCreature(BreedOrDefault(sim.Defs, breed), name, cell, faction, ageDays);
        SetElement(sim, dragon, element);
        return dragon;
    }

    /// <summary>What a dragon breathes (<see cref="Dragon.Element"/>; a look it wears follows).</summary>
    public static void SetElement(Simulation sim, Entity dragon, DragonElement element)
    {
        var w = sim.World;
        if (!Enum.IsDefined(element) || !w.Has<Dragon>(dragon))
            return;
        w.Get<Dragon>(dragon).Element = element;
        if (w.Has<DragonLook>(dragon))
            w.Get<DragonLook>(dragon).Element = element;
        Refresh(sim, dragon);
        // Its own breath work comes first, as burning always has for fire (SpawnCreature).
        if (ElementDefOf(sim, dragon) is { Work.Count: > 0 } el && w.TryGet<Mind>(dragon, out var mind))
            mind.WorkPriority[(int)el.Work[0]] = 2;
    }

    /// <summary>What a dragon's element changes about it that isn't saved (its cold tolerance): on spawning, a change of element and loading.</summary>
    public static void Refresh(Simulation sim, Entity dragon)
    {
        if (ElementDefOf(sim, dragon) is { } el && sim.World.Has<Creature>(dragon))
            sim.World.Get<Creature>(dragon).ColdTolerance = el.ColdTolerance;
    }

    /// <summary>
    /// What a chosen egg hatches into (the creator's egg step, a co-op joiner's): a null breed keeps
    /// the egg def's own. Nothing is rolled, so an egg nobody chose stays a Fire egg of the default breed.
    /// </summary>
    public static void ChooseEgg(Simulation sim, Entity egg, CreatureDef? breed, DragonElement element)
    {
        if (egg.IsNull || !sim.World.Has<Egg>(egg))
            return;
        ref var e = ref sim.World.Get<Egg>(egg);
        if (breed != null)
            e.Breed = BreedOrDefault(sim.Defs, breed);
        e.Element = Enum.IsDefined(element) ? element : DragonElement.Fire;
    }

    /// <summary>A dragon's element (Fire for anything that isn't one).</summary>
    public static DragonElement ElementOf(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Dragon>(e, out var d) ? d.Element : DragonElement.Fire;

    /// <summary>A dragon's element def, or null for anything that isn't a dragon (or defs without elements).</summary>
    public static ElementDef? ElementDefOf(Simulation sim, Entity e) =>
        sim.Defs.AllElements.Count > 0 && !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Dragon>(e, out var d) ? sim.Defs.Element(d.Element) : null;

    /// <summary>The element def a dragon breathes with, Fire's when it has none.</summary>
    public static ElementDef BreathOf(Simulation sim, Entity e) =>
        ElementDefOf(sim, e) ?? sim.Defs.Element(DragonElement.Fire);

    /// <summary>
    /// Whether a creature shrugs off a kind of damage from breath. Burns: anything fireproof, as
    /// always (<c>SafeTempMax</c> of 300 or more: every dragon). Frost, lightning and venom: a
    /// dragon of that element. Everything else hurts everyone.
    /// </summary>
    public static bool Resists(Simulation sim, Entity e, DamageType type)
    {
        if (type == DamageType.Burn)
            return sim.World.Get<Creature>(e).Def.SafeTempMax >= 300;
        return type is DamageType.Frost or DamageType.Shock or DamageType.Venom && ElementDefOf(sim, e) is { } el && el.DamageType == type;
    }

    /// <summary>Whether a breath of <paramref name="element"/> leaves nothing on this creature: its own element's dragons.</summary>
    public static bool ImmuneTo(Simulation sim, Entity e, ElementDef element) =>
        ElementDefOf(sim, e) is { } own && own.Element == element.Element;

    /// <summary>The creator's name for a dragon's breed ("Norse dragon"), or the def's label for anything else.</summary>
    public static string BreedLabel(CreatureDef def) => def.Breed?.Label ?? def.Label;
}
