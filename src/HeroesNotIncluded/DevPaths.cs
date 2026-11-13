using Narsil;

namespace HeroesNotIncluded;

/// <summary>
/// The source checkout's data folders when the game runs from one (dotnet run, tests), through
/// Narsil's <see cref="DevCheckout"/>. Live reloads (defs, shaders) read these rather than the
/// build output's copies, so edits show up without a rebuild. All null in a shipped build.
/// </summary>
public static class DevPaths
{
    private static readonly Lazy<DevCheckout?> Checkout = new(() => DevCheckout.Find("HeroesNotIncluded.sln"));

    public static string? Defs => Checkout.Value?.Dir("data", "defs");

    /// <summary>The quad renderer's shaders, in Narsil's voxel module (copied into the output's data/shaders).</summary>
    public static string? VoxelShaders => Checkout.Value?.VoxelShaders;

    /// <summary>Narsil's own shaders (sprites, meshes, post), which the build copies next to the voxel ones.</summary>
    public static string? EngineShaders => Checkout.Value?.EngineShaders;

    /// <summary>File watching for live reloads: Debug builds, or <c>SC_WATCH=1</c> (<c>SC_WATCH_ART=1</c> still works for art).</summary>
    public static bool Watch(string? legacyVariable = null) => DevCheckout.Watch("SC_WATCH", legacyVariable);
}
