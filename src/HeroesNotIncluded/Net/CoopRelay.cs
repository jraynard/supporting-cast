using Narsil.Net;

namespace HeroesNotIncluded.Net;

/// <summary>
/// Which relay server online co-op uses: <c>--relay</c> for this run, else the Options setting,
/// else the game's own (<see cref="Default"/>). Hosting through it is the Options' "Host online".
/// </summary>
public static class CoopRelay
{
    /// <summary>
    /// The game's own relay (Narsil.Relay on an Oracle Cloud instance; see
    /// external/narsil/docs/NETWORKING.md, "Running a relay"). Change it here if it ever moves.
    /// </summary>
    public const string Default = "129.153.122.49";

    /// <summary>From <c>--relay host[:port]</c>; not saved.</summary>
    public static string? Override { get; set; }

    public static string Address => (Override ?? (Settings.Current.RelayServer is { Length: > 0 } own ? own : Default)).Trim();

    public static bool TryGet(out string host, out int port)
    {
        port = RelayDefaults.Port;
        host = "";
        return Address.Length > 0 && NetAddress.TryParse(Address, RelayDefaults.Port, out host, out port);
    }
}
