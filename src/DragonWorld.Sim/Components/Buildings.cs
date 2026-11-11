using DragonWorld.Sim.Defs;

namespace DragonWorld.Sim.Components;

/// <summary>
/// A preset building (<see cref="BuildingDef"/>) as a whole: an entity of its own, off the map
/// layers, that its parts point to (<see cref="PartOf"/>). It holds the plan's place and which
/// phase it's at; the walls, floors and furniture themselves are ordinary structures, so a save
/// or a fire treats them like any other. <c>Systems.Buildings</c> lays each phase in turn.
/// </summary>
public sealed class PresetBuilding
{
    public required BuildingDef Def;

    /// <summary>Index into <see cref="BuildingDef.Variants"/>.</summary>
    public int Variant;

    /// <summary>The plan's middle on the ground level (<see cref="BuildingDef.Local"/>).</summary>
    public int3 Anchor;

    public byte Facing;

    /// <summary>The phase being built; <see cref="BuildPhase.Done"/> once it's finished.</summary>
    public BuildPhase Phase;

    /// <summary>Ordered taken down: nothing more is laid, and it goes once its last part does.</summary>
    public bool Demolishing;

    /// <summary>Upgrades fitted (<see cref="BuildingDef.Upgrades"/> names), in the order they went in.</summary>
    public List<string> Upgrades = [];

    public BuildingVariant VariantDef => Def.Variant(Variant);
}

/// <summary>A structure (or its construction site) that belongs to a preset building.</summary>
public struct PartOf
{
    public Entity Building;
}
