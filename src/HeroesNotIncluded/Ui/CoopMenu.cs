using Brutal.ImGuiApi;
using Narsil;
using Narsil.Net;
using HeroesNotIncluded.Net;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Ui;

/// <summary>
/// The title screen's Join panel: your name, villages found on the local network, a box for a
/// join code (through the relay, <see cref="CoopRelay"/>) or an address, and the wait while the
/// host sends the village. Hands back a ready
/// <see cref="CoopSession"/>; the title screen switches to it.
/// </summary>
public sealed class CoopMenu(GameHost host, DefDatabase defs) : IDisposable
{
    public enum Result { None, Back, Joined }

    /// <summary>Optional size for ImGui overloads; null means "auto".</summary>
    private static readonly float2? _auto = null;

    private readonly ImInputString _name = new(32, ""u8);
    private readonly ImInputString _address = new(128, "127.0.0.1"u8);
    private PlayerIdentity _me = CoopIdentity.Load(host);
    private LanBrowser? _browser;
    private Task<INetConnection>? _connecting;
    private string? _connectingTo;
    private CoopSession? _session;
    private string? _error;
    private bool _nameSet;

    public PlayerIdentity Identity => _me;

    /// <summary>The joined session, once <see cref="Draw"/> returns <see cref="Result.Joined"/>; the caller owns it.</summary>
    public CoopSession? Joined { get; private set; }

    /// <summary>Start joining <paramref name="address"/> straight away (<c>--join</c>).</summary>
    public void Connect(string address)
    {
        _address.SetValue(address.AsSpan());
        Begin(address);
    }

    /// <summary>Network work for the frame: discovery, the connection attempt, and the session's first turns.</summary>
    public void Update(float dt)
    {
        _browser ??= new LanBrowser(CoopSession.GameId, CoopSession.DiscoveryPort);
        _browser.Update();
        if (_connecting is { IsCompleted: true } task)
        {
            _connecting = null;
            if (task.IsCompletedSuccessfully)
                _session = CoopSession.Join(defs, _me, task.Result, _reconnect);
            else
                _error = $"Couldn't reach {_connectingTo}: {task.Exception?.GetBaseException().Message}";
        }
        if (_session is { } s)
        {
            s.Update(dt, 0);
            if (s.Ended is { } why)
            {
                _error = $"{_connectingTo}: {why}";
                s.Dispose();
                _session = null;
            }
        }
    }

    public Result Draw()
    {
        if (!_nameSet)
        {
            _name.SetValue(_me.Name.AsSpan());
            _nameSet = true;
        }
        if (_session is { Ready: true } ready)
        {
            Joined = ready;
            _session = null;
            return Result.Joined;
        }

        var busy = _connecting != null || _session != null;
        if (busy)
            ImGui.BeginDisabled(true);
        ImGui.SetNextItemWidth(220);
        ImGui.InputText("Your name"u8, _name, ImGuiInputTextFlags.None, null, IntPtr.Zero);
        if (ImGui.IsItemDeactivatedAfterEdit())
            _me = CoopIdentity.Rename(host, _me, Narsil.Ui.ImGuiText.Value(_name));

        ImGui.Spacing();
        ImGui.TextColored(Theme.Dim, "On your network"u8);
        var sessions = _browser?.Sessions ?? [];
        if (_browser?.Error is { } lanError)
            ImGui.TextColored(Theme.Warn, Narsil.Ui.ImGuiText.Escape($"Can't look for games here: {lanError}"));
        else if (sessions.Count == 0)
            ImGui.TextColored(Theme.Dim, "  Looking for games..."u8);
        foreach (var s in sessions)
        {
            var mismatch = s.BuildTag != CoopSession.BuildTag(defs);
            var label = $"{s.Name}   {s.Players}/{s.MaxPlayers}   {s.Endpoint}{(mismatch ? "   (different version)" : "")}";
            if (ImGui.Selectable(label, false, ImGuiSelectableFlags.None, in _auto) && !mismatch)
                Begin($"{s.Endpoint.Address}:{s.Endpoint.Port}");
        }

        ImGui.Spacing();
        ImGui.TextColored(Theme.Dim, CoopRelay.TryGet(out _, out _) ? "By join code or address"u8 : "By address"u8);
        ImGui.SetNextItemWidth(220);
        var enter = ImGui.InputText("##address"u8, _address, ImGuiInputTextFlags.EnterReturnsTrue, null, IntPtr.Zero);
        ImGui.SameLine(0, 8);
        if (ImGui.Button("Join"u8, in _auto) || enter)
            Begin(Narsil.Ui.ImGuiText.Value(_address));
        if (busy)
            ImGui.EndDisabled();

        if (busy)
        {
            var phase = _connecting != null ? "Connecting to"
                : _session?.Client?.State == LockstepClient.Phase.Loading ? "Receiving the village from"
                : "Saying hello to";
            ImGui.TextColored(Theme.Accent, Narsil.Ui.ImGuiText.Escape($"{phase} {_connectingTo}..."));
            if (ImGui.Button("Cancel"u8, in _auto))
                Cancel();
        }
        if (_error != null)
        {
            ImGui.PushTextWrapPos(380);
            ImGui.TextColored(Theme.Bad, Narsil.Ui.ImGuiText.Escape(_error));
            ImGui.PopTextWrapPos();
        }

        ImGui.Spacing();
        if (ImGui.Button("Back"u8, in _auto))
        {
            Cancel();
            return Result.Back;
        }
        return Result.None;
    }

    private void Begin(string address)
    {
        _error = null;
        // With a relay set up, six letters like "KX7-QPM" are a join code (no I, L, O, 0 or 1,
        // so they're rarely a machine name; add a port to force an address).
        if (RelayClient.IsCode(address) && CoopRelay.TryGet(out var relay, out var relayPort))
        {
            _me = CoopIdentity.Rename(host, _me, Narsil.Ui.ImGuiText.Value(_name));
            var code = address.Trim().ToUpperInvariant();
            _connectingTo = $"game {code}";
            _connecting = RelayClient.JoinAsync(relay, relayPort, CoopSession.GameId, code);
            _reconnect = ct => RelayClient.JoinAsync(relay, relayPort, CoopSession.GameId, code, ct);
            return;
        }
        if (!NetAddress.TryParse(address, CoopSession.DefaultPort, out var hostName, out var port))
        {
            _error = $"\"{address}\" isn't an address (try 192.168.1.20 or 192.168.1.20:{CoopSession.DefaultPort}).";
            return;
        }
        _me = CoopIdentity.Rename(host, _me, Narsil.Ui.ImGuiText.Value(_name));
        _connectingTo = $"{hostName}:{port}";
        _connecting = TcpNetConnection.ConnectAsync(hostName, port);
        _reconnect = ct => TcpNetConnection.ConnectAsync(hostName, port, ct);
    }

    private Func<CancellationToken, Task<INetConnection>>? _reconnect;

    private void Cancel()
    {
        _session?.Dispose();
        _session = null;
        if (_connecting is { } pending)
            _ = pending.ContinueWith(t => { if (t.IsCompletedSuccessfully) t.Result.Dispose(); }, TaskScheduler.Default);
        _connecting = null;
    }

    public void Dispose()
    {
        Cancel();
        _browser?.Dispose();
    }
}
