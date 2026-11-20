using System.Runtime.InteropServices;
using Narsil;

namespace HeroesNotIncluded.Platforms;

/// <summary>
/// Steam, through <see cref="SteamNative"/>: started before the window (the overlay hooks Vulkan
/// as it's made), callbacks pumped once a frame by manual dispatch, and torn down on exit. It does
/// what the demo needs and no more: rich presence, Join game and invites carrying the relay's join
/// code (<see cref="ConnectString"/>), and the store overlay. Lobbies, Steam's own relay and the
/// cloud wait for Early Access (docs/NEXT-FEST.md).
/// </summary>
public sealed unsafe class SteamPlatform : IGamePlatform
{
    private readonly SteamNative _api;
    private readonly SteamConfig _config;
    private readonly int _pipe;
    private readonly nint _friends;
    private readonly nint _utils;
    private readonly Dictionary<string, string> _sent = [];
    private string? _joinCode;
    private string? _pendingJoin;
    private bool _down;

    private SteamPlatform(SteamNative api, SteamConfig config)
    {
        _api = api;
        _config = config;
        _api.ManualDispatchInit();
        _pipe = _api.GetHSteamPipe();
        _friends = _api.SteamFriends();
        _utils = _api.SteamUtils();
        PersonaName = SteamNative.FromUtf8(_api.GetPersonaName(_friends)) is { Length: > 0 } name ? name : null;
    }

    public string Name => "Steam";
    public bool CanInvite => _joinCode != null;
    /// <summary>The demo (or another app) pointing at the full game's page; the full game itself has nothing to wishlist.</summary>
    public bool HasStore => _config.StoreAppId > 0 && _config.StoreAppId != _config.AppId;
    public string? PersonaName { get; }
    public bool OverlayOpen { get; private set; }

    /// <summary>
    /// A Steam copy started outside Steam (the executable opened directly): true when Steam is
    /// starting it again properly, and this process should quit. False for a dev run, or when the
    /// library isn't there (that's logged by <see cref="TryStart"/>).
    /// </summary>
    public static bool RestartThroughSteam(SteamConfig config)
    {
        if (config.Dev || SteamNative.TryLoad(out _) is not { } api)
            return false;
        return api.RestartAppIfNecessary(config.AppId) != 0;
    }

    /// <summary>Steam, started for <paramref name="config"/>'s app; null (and a line in the log) if it can't be.</summary>
    public static SteamPlatform? TryStart(SteamConfig config)
    {
        if (SteamNative.TryLoad(out var why) is not { } api)
        {
            Log.Engine.Warning($"Steam: not started: {why}");
            return null;
        }
        if (config.Dev)
            DevAppId(config.AppId);
        const int ErrMsgSize = 1024;
        var err = stackalloc byte[ErrMsgSize];
        err[0] = 0;
        var result = api.InitFlat(err);
        if (result != 0)
        {
            var reason = result switch
            {
                2 => "Steam isn't running",
                3 => "the Steam client is older than this game's Steam library",
                _ => "init failed",
            };
            Log.Engine.Warning($"Steam: not started ({reason}): {SteamNative.FromUtf8(err)}");
            return null;
        }
        var steam = new SteamPlatform(api, config);
        Log.Engine.Info($"Steam: started for app {config.AppId}{(config.Dev ? " (dev)" : "")}, as {steam.PersonaName ?? "(no name)"}");
        return steam;
    }

    /// <summary>
    /// A dev run names its app the way Steam does when it launches a game, through the
    /// <c>SteamAppId</c> environment variable. On Linux and the Mac .NET keeps its own copy of the
    /// environment, so it also goes to libc's, which the Steam library reads.
    /// </summary>
    private static void DevAppId(uint appId)
    {
        var id = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Environment.SetEnvironmentVariable("SteamAppId", id);
        Environment.SetEnvironmentVariable("SteamGameId", id);
        if (!OperatingSystem.IsWindows())
        {
            setenv("SteamAppId", id, 1);
            setenv("SteamGameId", id, 1);
        }
    }

    [DllImport("libc", CharSet = CharSet.Ansi)]
    private static extern int setenv(string name, string value, int overwrite);

    /// <summary>Frames until the overlay's state is logged: it hooks the game's first frames, not Steam's start.</summary>
    private int _overlayLogIn = 300;

    public void Update()
    {
        if (_down)
            return;
        if (_overlayLogIn > 0 && --_overlayLogIn == 0)
            Log.Engine.Info(_api.IsOverlayEnabled(_utils) != 0
                ? "Steam: the overlay is on (Shift+Tab)"
                : "Steam: no overlay (Steam draws it only over games it launched; see docs/alpha/PUBLISHING.md, Steam)");
        _api.ManualDispatchRunFrame(_pipe);
        CallbackMsg msg;
        while (_api.ManualDispatchGetNextCallback(_pipe, &msg) != 0)
        {
            try
            {
                var param = new ReadOnlySpan<byte>((void*)msg.Param, msg.ParamSize);
                switch (msg.Callback)
                {
                    case SteamCallbacks.OverlayActivated:
                        OverlayOpen = param.Length > 0 && param[0] != 0;
                        break;
                    case SteamCallbacks.JoinRequested:
                        var connect = SteamCallbacks.ConnectOf(param);
                        if (ConnectString.CodeOf(connect) is { } code)
                            _pendingJoin = code;
                        else
                            Log.Engine.Warning($"Steam: a join request that isn't ours: \"{connect}\"");
                        break;
                    // Anything else (including call results, which the game never asks for) is let go.
                }
            }
            finally
            {
                _api.ManualDispatchFreeLastCallback(_pipe);
            }
        }
    }

    public string? TakeJoinRequest()
    {
        var code = _pendingJoin;
        _pendingJoin = null;
        return code;
    }

    public void SetPresence(Presence? presence)
    {
        if (presence is not { } p)
        {
            Set("steam_display", "");
            Set("trade", "");
            Set("town", "");
            Set("day", "");
            return;
        }
        // The fields first: Steam shows the line as soon as it's set.
        Set("trade", p.Trade);
        Set("town", p.Town);
        Set("day", p.Day > 0 ? p.Day.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
        Set("steam_display", p.Token);
    }

    public void SetJoinCode(string? code, int players)
    {
        _joinCode = code;
        Set("connect", code != null ? ConnectString.For(code) : "");
        // Friends see who's playing together.
        Set("steam_player_group", code ?? "");
        Set("steam_player_group_size", code != null ? Math.Max(1, players).ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
    }

    public void InviteFriends()
    {
        if (_down || _joinCode == null)
            return;
        fixed (byte* connect = SteamNative.Utf8(ConnectString.For(_joinCode)))
            _api.ActivateOverlayInviteConnectString(_friends, connect);
    }

    public void OpenStore()
    {
        if (_down || !HasStore)
            return;
        if (_api.IsOverlayEnabled(_utils) != 0)
        {
            _api.ActivateOverlayToStore(_friends, _config.StoreAppId, 0);
            return;
        }
        // The overlay's off (the player's choice, or it couldn't hook the game): the browser instead.
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_config.StoreUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Engine.Warning($"Steam: couldn't open {_config.StoreUrl}: {ex.Message}");
        }
    }

    /// <summary>A rich presence key, sent only when it changes (an empty value removes it).</summary>
    private void Set(string key, string value)
    {
        if (_down || (_sent.TryGetValue(key, out var was) ? was == value : value.Length == 0))
            return;
        _sent[key] = value;
        fixed (byte* k = SteamNative.Utf8(key))
        fixed (byte* v = SteamNative.Utf8(value))
            _api.SetRichPresence(_friends, k, v);
    }

    public void Shutdown()
    {
        if (_down)
            return;
        _api.ClearRichPresence(_friends);
        _api.Shutdown();
        _down = true;
        Log.Engine.Info("Steam: shut down");
    }
}
