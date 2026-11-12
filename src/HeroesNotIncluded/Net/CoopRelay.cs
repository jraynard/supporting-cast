using Narsil.Net;

namespace HeroesNotIncluded.Net;

/// <summary>
/// Which relay server online co-op uses: <c>--relay</c> for this run, else the Options setting.
/// None configured means no join codes: LAN and direct addresses only.
/// </summary>
public static class CoopRelay
{
    /// <summary>From <c>--relay host[:port]</c>; not saved.</summary>
    public static string? Override { get; set; }

    public static string Address => (Override ?? Settings.Current.RelayServer).Trim();

    public static bool TryGet(out string host, out int port)
    {
        port = RelayDefaults.Port;
        host = "";
        return Address.Length > 0 && NetAddress.TryParse(Address, RelayDefaults.Port, out host, out port);
    }
}
