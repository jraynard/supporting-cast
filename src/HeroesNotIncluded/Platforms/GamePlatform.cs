namespace HeroesNotIncluded.Platforms;

/// <summary>
/// What a friend sees you doing in their friends list. <see cref="Token"/> names a line in
/// <c>packaging/steam/rich_presence.vdf</c> (uploaded to Steamworks), whose <c>%trade%</c>,
/// <c>%town%</c> and <c>%day%</c> are filled from the rest. Only what the player themselves knows
/// goes in: their own trade, the town, the day.
/// </summary>
public readonly record struct Presence(string Token, string Trade = "", string Town = "", int Day = 0)
{
    public static readonly Presence Menu = new("#Menu");
}

/// <summary>
/// The store the game was started from (Steam, or none), behind one door so nothing else names
/// it: the game's UI asks <see cref="GamePlatform.Current"/> and never the Steam API. Game side
/// only; the sim never sees it. Every call is safe to make on <see cref="NoPlatform"/>.
/// </summary>
public interface IGamePlatform
{
    string Name { get; }

    /// <summary>Friends can be invited from the game (the Steam overlay's invite dialog).</summary>
    bool CanInvite { get; }

    /// <summary>There's a store page to wishlist (<see cref="OpenStore"/>): the demo's link to the full game.</summary>
    bool HasStore { get; }

    /// <summary>The player's name on the platform, for a new character's default name; null if none.</summary>
    string? PersonaName { get; }

    /// <summary>The platform's overlay is up over the game.</summary>
    bool OverlayOpen { get; }

    /// <summary>Once a frame: the platform's callbacks.</summary>
    void Update();

    /// <summary>What friends see you doing; null clears it. Cheap to call every frame: only changes go out.</summary>
    void SetPresence(Presence? presence);

    /// <summary>Hosting online with this relay code (or null for not): friends' Join game and invites carry it.</summary>
    void SetJoinCode(string? code, int players);

    /// <summary>The platform's invite dialog for the code given to <see cref="SetJoinCode"/>.</summary>
    void InviteFriends();

    /// <summary>The store page, to wishlist the game.</summary>
    void OpenStore();

    /// <summary>A friend's Join game or an accepted invite since the last call: their relay code, once.</summary>
    string? TakeJoinRequest();

    void Shutdown();
}

/// <summary>The platform in use: Steam when the game was started through it (<see cref="SteamPlatform"/>), else <see cref="NoPlatform"/>.</summary>
public static class GamePlatform
{
    public static IGamePlatform Current { get; private set; } = NoPlatform.Instance;

    public static void Use(IGamePlatform platform) => Current = platform;

    public static void Shutdown()
    {
        Current.Shutdown();
        Current = NoPlatform.Instance;
    }
}

/// <summary>No store: itch builds, runs from a checkout, tests and headless runs.</summary>
public sealed class NoPlatform : IGamePlatform
{
    public static readonly NoPlatform Instance = new();

    public string Name => "none";
    public bool CanInvite => false;
    public bool HasStore => false;
    public string? PersonaName => null;
    public bool OverlayOpen => false;
    public void Update() { }
    public void SetPresence(Presence? presence) { }
    public void SetJoinCode(string? code, int players) { }
    public void InviteFriends() { }
    public void OpenStore() { }
    public string? TakeJoinRequest() => null;
    public void Shutdown() { }
}
