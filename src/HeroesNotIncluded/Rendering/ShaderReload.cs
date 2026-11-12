using Narsil;

namespace HeroesNotIncluded.Rendering;

/// <summary>
/// The F7 key: recompile the quad shaders (Narsil.Voxel) and Narsil's sprite and post shaders from the source
/// checkout (<see cref="DevPaths"/>) when there is one, else from the build output.
/// </summary>
public static class ShaderReload
{
    public static string QuadShaders(GameHost host) => DevPaths.VoxelShaders ?? Path.Combine(host.DataDirectory, "shaders");

    public static string EngineShaders(GameHost host) => DevPaths.EngineShaders ?? Path.Combine(host.DataDirectory, "shaders");

    /// <summary>Reload everything; null on success, else the first error in a line (all of them go to the log).</summary>
    public static string? Reload(GameHost host, QuadRenderer? quads)
    {
        var errors = new List<string>();
        if (quads?.ReloadShaders(QuadShaders(host)) is { } quadError)
            errors.Add(quadError);
        if (host.Sprites.ReloadShaders(EngineShaders(host), host.Renderer) is { } spriteError)
            errors.Add(spriteError);
        if (host.Post.ReloadShaders(EngineShaders(host)) is { } postError)
            errors.Add(postError);
        foreach (var e in errors)
            Log.Render.Warning(e);
        if (errors.Count == 0)
            Log.Render.Info($"Shaders reloaded from {QuadShaders(host)}");
        return errors.Count == 0 ? null : Summary(errors[0]);
    }

    /// <summary>
    /// The line worth showing from a compiler message: shaderc's first line is "Shader '…' failed
    /// to compile:", the next one says where and why.
    /// </summary>
    public static string Summary(string message)
    {
        var lines = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length > 1 ? lines[1] : lines.FirstOrDefault() ?? message;
    }
}
