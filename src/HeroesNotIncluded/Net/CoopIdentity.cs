using System.Security.Cryptography;
using System.Text;
using Narsil;
using Narsil.Net;

namespace HeroesNotIncluded.Net;

/// <summary>
/// This player's co-op identity, kept in the config directory so hosts recognise them next time.
/// Its name is only a fallback: in a game you go by your saved character's
/// (<see cref="Characters.SavedCharacter"/>). <c>--player-id</c>/<c>--name</c> override it for the
/// session (two copies on one machine).
/// </summary>
public static class CoopIdentity
{
    /// <summary>From <c>--player-id</c>: a GUID, or any word (hashed into one, so "bea" is always the same player).</summary>
    public static string? IdOverride { get; set; }

    /// <summary>From <c>--name</c>.</summary>
    public static string? NameOverride { get; set; }

    public static string PathFor(GameHost host) => Path.Combine(host.ConfigDirectory, "player.txt");

    public static PlayerIdentity Load(GameHost host)
    {
        var saved = PlayerIdentity.LoadOrCreate(PathFor(host), DefaultName());
        var id = IdOverride is { } text ? Parse(text) : saved.Id;
        return new PlayerIdentity(id, NameOverride ?? saved.Name);
    }

    public static Guid Parse(string text) =>
        Guid.TryParse(text, out var g) ? g : new Guid(MD5.HashData(Encoding.UTF8.GetBytes(text.Trim().ToLowerInvariant())));

    private static string DefaultName()
    {
        var user = Environment.UserName;
        return string.IsNullOrWhiteSpace(user) ? "Player" : char.ToUpperInvariant(user[0]) + user[1..];
    }
}
