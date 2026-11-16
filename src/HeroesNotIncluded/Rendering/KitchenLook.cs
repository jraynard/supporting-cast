using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Rendering;

/// <summary>
/// How the kitchen's food looks, shared by its mini-game scenes and the world (as
/// <see cref="ForgeGlow"/> is for the smithy), so a crust reads the same at the spit as in the roast's window.
/// </summary>
public static class KitchenLook
{
    /// <summary>The colour of a joint's crust, pale to charred.</summary>
    public static float4 Crust(Crust c) => c switch
    {
        Sim.Work.Crust.Pale => new(0.86f, 0.70f, 0.66f, 1f),
        Sim.Work.Crust.Colouring => new(0.80f, 0.55f, 0.36f, 1f),
        Sim.Work.Crust.Golden => new(0.74f, 0.47f, 0.18f, 1f),
        Sim.Work.Crust.Dark => new(0.45f, 0.26f, 0.12f, 1f),
        _ => new(0.16f, 0.12f, 0.10f, 1f),
    };
}
