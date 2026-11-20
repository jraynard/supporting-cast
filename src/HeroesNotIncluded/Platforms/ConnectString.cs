using Narsil.Net;

namespace HeroesNotIncluded.Platforms;

/// <summary>
/// What a friend's Join game (or an accepted invite) hands the game: the same <c>--join CODE</c>
/// a player could type on the command line, with the relay's join code (<c>CoopSession.JoinCode</c>).
/// Steam appends it to the command line when it launches the game for a join, and
/// <c>Program.cs</c>'s <c>--join</c> already takes a code, so there's one way in either way.
/// </summary>
public static class ConnectString
{
    public const string Flag = "--join";

    public static string For(string code) => $"{Flag} {code}";

    /// <summary>The join code in a connect string, or null if it's anything else (a friend on an old build, or a hostile one).</summary>
    public static string? CodeOf(string? connect)
    {
        if (connect == null)
            return null;
        var parts = connect.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts is [Flag, var code] && RelayClient.IsCode(code) ? code.ToUpperInvariant() : null;
    }
}
