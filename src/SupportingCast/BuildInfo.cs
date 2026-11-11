using System.Reflection;
using System.Runtime.InteropServices;
using SupportingCast.Net;
using SupportingCast.Sim.Defs;

namespace SupportingCast;

/// <summary>
/// Which build this is, for testers' bug reports and the co-op handshake: the version (set by
/// <c>Directory.Build.props</c>, or the release tag in CI), the commit it was built from, and the
/// co-op build tag, which only matches between identical code and defs.
/// </summary>
public static class BuildInfo
{
    private static readonly string Informational =
        typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";

    /// <summary>"0.1.0-alpha.1".</summary>
    public static string Version => Informational.Split('+')[0];

    /// <summary>The commit (short), when the build was given one (<c>-p:SourceRevisionId</c>), else null.</summary>
    public static string? Commit => Informational.Split('+') is [_, var sha, ..] ? sha[..Math.Min(8, sha.Length)] : null;

    /// <summary>The co-op build tag (see <see cref="CoopSession.BuildTag"/>), computed once.</summary>
    public static string Tag(DefDatabase defs) => CoopSession.BuildTag(defs);

    /// <summary>"v0.1.0-alpha.1 (abc12345)".</summary>
    public static string Label => Commit is { } c ? $"v{Version} ({c})" : $"v{Version}";

    /// <summary>A few lines for logs and crash reports.</summary>
    public static string Describe(string? tag) =>
        $"SupportingCast {Label}, build tag {tag ?? "?"}, {RuntimeInformation.OSDescription} {RuntimeInformation.RuntimeIdentifier}, .NET {Environment.Version}";

    /// <summary>Where logs, crash reports and desync dumps go (the per-user config folder's <c>logs</c>).</summary>
    public static string LogDirectory => Path.Combine(Narsil.GameHost.DefaultConfigDirectory(TitleGame.DataFolder), "logs");
}
