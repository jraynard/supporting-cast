using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Platforms;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

/// <summary>
/// The round and the store it came from (<see cref="GamePlatform"/>): what friends see you doing,
/// the join code hosting online hands to their Join game and invites, and a friend's invite
/// accepted mid-round, which asks before leaving this one.
/// </summary>
public sealed partial class VillageGame
{
    /// <summary>How often presence is rebuilt: the day and the trade change slowly.</summary>
    private const float PresenceEvery = 2f;

    private float _presenceIn;

    /// <summary>A friend's relay code from an invite accepted mid-round, waiting on the card.</summary>
    private string? _joinOffer;

    private void UpdatePlatform(float dt)
    {
        var platform = GamePlatform.Current;
        platform.Update();
        platform.SetJoinCode(_coop is { IsHost: true, JoinCode: { } code } ? code : null, _coop?.Players.Count ?? 0);
        if ((_presenceIn -= dt) <= 0)
        {
            _presenceIn = PresenceEvery;
            platform.SetPresence(CurrentPresence());
        }
        if (platform.TakeJoinRequest() is { } offer)
            _joinOffer = offer;
    }

    /// <summary>The player's own trade, the town (when it's known here) and the day: nothing they don't know themselves.</summary>
    private Presence CurrentPresence()
    {
        var round = _sim.Round;
        var trade = HasAvatar && _sim.World.TryGet<Trade>(_avatar, out var t) && t.Def.Label.Length > 0
            ? char.ToUpperInvariant(t.Def.Label[0]) + t.Def.Label[1..]
            : "";
        var town = TownLabel() ?? "the village";
        if (round.Practice)
            return new Presence("#Practice", trade, town);
        if (round.Phase == Sim.Round.RoundPhase.Lobby)
            return new Presence("#Lobby", trade, town);
        if (round.Phase == Sim.Round.RoundPhase.Festival)
            return new Presence("#Festival", trade, town);
        if (round.Phase == Sim.Round.RoundPhase.Over)
            return new Presence("#Reveal", trade, town);
        if (!round.Live)
            return new Presence("#Village", trade, town);
        if (Ghosting)
            return new Presence("#Ghost", trade, town, round.SeasonDay(_sim).Day);
        return new Presence(trade.Length > 0 ? "#Round" : "#RoundNoTrade", trade, town, round.SeasonDay(_sim).Day);
    }

    /// <summary>The town's name, known only for a village started here (as the kept replay's is).</summary>
    private string? TownLabel()
    {
        if (Loaded != null || Coop != null)
            return null;
        try
        {
            return _sim.Defs.Map(MapName).Label is { Length: > 0 } label ? label : null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The card for a friend's invite accepted mid-round: leave this game for theirs, or stay.</summary>
    private void DrawJoinOffer()
    {
        if (_joinOffer is not { } code)
            return;
        var center = ImGui.GetIO().DisplaySize * 0.5f;
        float2? middle = new float2(0.5f, 0.5f);
        ImGui.SetNextWindowPos(in center, ImGuiCond.Always, in middle);
        ScreenFit.Next();
        Narsil.Ui.Panel.Begin("Join a friend###join-offer", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
        PadWindow("###join-offer", null);
        Wrapped(_coop is { IsHost: true }
            ? "A friend's asked you over to their village. Going closes this one for everyone in it."
            : _coop != null
                ? "A friend's asked you over to their village. You'll leave this one."
                : "A friend's asked you over to their village. You'll leave this one, and anything since your last save.");
        if (ImGui.Button("Go"u8, in _autoSize))
        {
            _joinOffer = null;
            _coop?.Dispose();
            _coop = null;
            Host.SwitchTo(new TitleGame { AutoJoin = code });
        }
        ImGui.SameLine(0, Gui.S(8));
        if (ImGui.Button("Stay"u8, in _autoSize))
            _joinOffer = null;
        Narsil.Ui.Panel.End();
    }
}
