namespace HeroesNotIncluded;

/// <summary>
/// What a demo copy (<see cref="BuildInfo.IsDemo"/>) may be started with. The demo is one season in
/// Millbrook, the tutorial and co-op (docs/NEXT-FEST.md), so its launch arguments are filtered to those
/// before anything reads them: the drills, the arena, the demos, other towns and modes, <c>--mods</c> and
/// <c>--dev</c> are dropped, with any value that followed them, and the game opens as if they weren't given.
/// An allow-list, not a deny-list, so a flag added later stays out of the demo until it's let in.
/// </summary>
public static class DemoGate
{
    /// <summary>Flags a demo keeps: playing, joining (Steam's invites carry <c>--join CODE</c>), the screen, and support.</summary>
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        "--demo", "--steam", "--version", "--check-gpu",
        "--new-game", "--trade", "--name", "--character", "--folk", "--bots", "--friendly-fire",
        "--host", "--port", "--relay", "--join", "--player-id",
        "--practice", "--lesson",
        "--deck", "--size", "--ui-scale", "--mute", "--no-splash", "--input-log", "--voice-clip",
        "--screenshot", "--screenshot-at", "--screenshot-no-ui", "--quit",
    };

    /// <summary><paramref name="args"/> with every flag the demo doesn't allow left out, its value with it (in <paramref name="dropped"/>).</summary>
    public static string[] Arguments(string[] args, out List<string> dropped)
    {
        var kept = new List<string>(args.Length);
        dropped = [];
        var keeping = true;
        foreach (var a in args)
        {
            if (a.StartsWith("--", StringComparison.Ordinal))
                keeping = Allowed.Contains(a);
            if (keeping)
                kept.Add(a);
            else
                dropped.Add(a);
        }
        return [.. kept];
    }
}
