using System.Runtime.InteropServices;
using System.Text;

namespace HeroesNotIncluded.Platforms;

/// <summary>
/// The few functions of Valve's flat C API (<c>steam_api_flat.h</c>, SDK 1.65) the game uses, bound
/// by hand rather than through a wrapper library, so they always match the libraries committed in
/// <c>external/steamworks/</c>. Loaded with <see cref="NativeLibrary.TryLoad(string, out nint)"/>:
/// a missing library or entry point means no Steam, never a crash. Interface accessors carry
/// their version in the name (<c>SteamFriends_v018</c>); check them when the SDK is updated.
/// </summary>
internal sealed unsafe class SteamNative
{
    /// <summary>The library's file name on this system.</summary>
    public static string FileName =>
        OperatingSystem.IsWindows() ? "steam_api64.dll" : OperatingSystem.IsMacOS() ? "libsteam_api.dylib" : "libsteam_api.so";

    public delegate* unmanaged<byte*, int> InitFlat;
    public delegate* unmanaged<void> Shutdown;
    public delegate* unmanaged<uint, byte> RestartAppIfNecessary;
    public delegate* unmanaged<int> GetHSteamPipe;
    public delegate* unmanaged<void> ManualDispatchInit;
    public delegate* unmanaged<int, void> ManualDispatchRunFrame;
    public delegate* unmanaged<int, CallbackMsg*, byte> ManualDispatchGetNextCallback;
    public delegate* unmanaged<int, void> ManualDispatchFreeLastCallback;
    public delegate* unmanaged<nint> SteamFriends;
    public delegate* unmanaged<nint> SteamUtils;
    public delegate* unmanaged<nint, byte*> GetPersonaName;
    public delegate* unmanaged<nint, byte*, byte*, byte> SetRichPresence;
    public delegate* unmanaged<nint, void> ClearRichPresence;
    public delegate* unmanaged<nint, uint, int, void> ActivateOverlayToStore;
    public delegate* unmanaged<nint, byte*, void> ActivateOverlayInviteConnectString;
    public delegate* unmanaged<nint, byte> IsOverlayEnabled;

    private SteamNative() { }

    /// <summary>The library beside the game (or on the system's path), with every function found; else null and why.</summary>
    public static SteamNative? TryLoad(out string? why)
    {
        why = null;
        if (!TryOpen(out var lib))
        {
            why = $"{FileName} not found";
            return null;
        }
        var n = new SteamNative();
        var missing = new List<string>();
        nint Get(string name)
        {
            if (NativeLibrary.TryGetExport(lib, name, out var f))
                return f;
            missing.Add(name);
            return 0;
        }
        n.InitFlat = (delegate* unmanaged<byte*, int>)Get("SteamAPI_InitFlat");
        n.Shutdown = (delegate* unmanaged<void>)Get("SteamAPI_Shutdown");
        n.RestartAppIfNecessary = (delegate* unmanaged<uint, byte>)Get("SteamAPI_RestartAppIfNecessary");
        n.GetHSteamPipe = (delegate* unmanaged<int>)Get("SteamAPI_GetHSteamPipe");
        n.ManualDispatchInit = (delegate* unmanaged<void>)Get("SteamAPI_ManualDispatch_Init");
        n.ManualDispatchRunFrame = (delegate* unmanaged<int, void>)Get("SteamAPI_ManualDispatch_RunFrame");
        n.ManualDispatchGetNextCallback = (delegate* unmanaged<int, CallbackMsg*, byte>)Get("SteamAPI_ManualDispatch_GetNextCallback");
        n.ManualDispatchFreeLastCallback = (delegate* unmanaged<int, void>)Get("SteamAPI_ManualDispatch_FreeLastCallback");
        n.SteamFriends = (delegate* unmanaged<nint>)Get("SteamAPI_SteamFriends_v018");
        n.SteamUtils = (delegate* unmanaged<nint>)Get("SteamAPI_SteamUtils_v011");
        n.GetPersonaName = (delegate* unmanaged<nint, byte*>)Get("SteamAPI_ISteamFriends_GetPersonaName");
        n.SetRichPresence = (delegate* unmanaged<nint, byte*, byte*, byte>)Get("SteamAPI_ISteamFriends_SetRichPresence");
        n.ClearRichPresence = (delegate* unmanaged<nint, void>)Get("SteamAPI_ISteamFriends_ClearRichPresence");
        n.ActivateOverlayToStore = (delegate* unmanaged<nint, uint, int, void>)Get("SteamAPI_ISteamFriends_ActivateGameOverlayToStore");
        n.ActivateOverlayInviteConnectString = (delegate* unmanaged<nint, byte*, void>)Get("SteamAPI_ISteamFriends_ActivateGameOverlayInviteDialogConnectString");
        n.IsOverlayEnabled = (delegate* unmanaged<nint, byte>)Get("SteamAPI_ISteamUtils_IsOverlayEnabled");
        if (missing.Count > 0)
        {
            why = $"{FileName} lacks {string.Join(", ", missing)} (a different SDK version?)";
            return null;
        }
        return n;
    }

    /// <summary>Beside the game's assemblies, beside the executable (a Mac app keeps them apart), then wherever the system looks.</summary>
    private static bool TryOpen(out nint lib)
    {
        foreach (var dir in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(Environment.ProcessPath) })
            if (dir != null && NativeLibrary.TryLoad(Path.Combine(dir, FileName), out lib))
                return true;
        return NativeLibrary.TryLoad(FileName, out lib);
    }

    /// <summary>A C string for a call: UTF-8, null-terminated.</summary>
    public static byte[] Utf8(string text)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(text) + 1];
        Encoding.UTF8.GetBytes(text, bytes);
        return bytes;
    }

    /// <summary>A C string from Steam (UTF-8), or "" for none.</summary>
    public static string FromUtf8(byte* text) => text == null ? "" : Marshal.PtrToStringUTF8((nint)text) ?? "";
}

/// <summary>
/// <c>CallbackMsg_t</c>: who, which callback, and its bytes. The SDK packs it to 4 on Linux and the
/// Mac and to 8 on Windows, which moves no field on a 64-bit build, so one layout fits all.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct CallbackMsg
{
    [FieldOffset(0)] public int SteamUser;
    [FieldOffset(4)] public int Callback;
    [FieldOffset(8)] public nint Param;
    [FieldOffset(16)] public int ParamSize;
}

/// <summary>The callbacks the game reads, and how (pure, for tests).</summary>
internal static class SteamCallbacks
{
    /// <summary><c>GameOverlayActivated_t</c> (<c>k_iSteamFriendsCallbacks + 31</c>): its first byte is whether the overlay opened.</summary>
    public const int OverlayActivated = 331;

    /// <summary>
    /// <c>GameRichPresenceJoinRequested_t</c> (<c>k_iSteamFriendsCallbacks + 37</c>): the player chose
    /// Join game on a friend, or accepted an invite, while the game was running. A <c>CSteamID</c>,
    /// then the friend's <c>connect</c> string (at most 256 bytes, null-terminated).
    /// </summary>
    public const int JoinRequested = 337;

    /// <summary>The connect string in a join request's bytes, or null if there's none.</summary>
    public static string? ConnectOf(ReadOnlySpan<byte> param)
    {
        if (param.Length <= 8)
            return null;
        var text = param[8..Math.Min(param.Length, 8 + 256)];
        var end = text.IndexOf((byte)0);
        if (end >= 0)
            text = text[..end];
        return text.Length == 0 ? null : Encoding.UTF8.GetString(text);
    }
}
