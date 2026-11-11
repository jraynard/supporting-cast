using Brutal.ImGuiApi;
using SupportingCast.Net;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Ecs;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Systems;
using SupportingCast.Ui;

namespace SupportingCast;

/// <summary>
/// The round's lobby: seats, trade preferences and the host's Start. The state is all in
/// <see cref="RoundSystem"/>; this only draws it and sends commands. The session stands still
/// until the deal, which is why a lobby can sit open as long as it likes.
/// </summary>
public sealed partial class VillageGame
{
    /// <summary><c>--trade</c>: the trade a solo round deals you; otherwise one is picked for you.</summary>
    public string? HeroTrade { get; init; }

    private bool _tradeRequested;

    private bool InLobby => _sim.Round.Phase == RoundPhase.Lobby;

    private PerceptionView? _view;

    /// <summary>What the local player knows. Every read of other characters and items goes through this.</summary>
    private PerceptionView View
    {
        get
        {
            var phase = _sim.Round.Phase;
            var blind = phase == RoundPhase.Playing && !HasAvatar;
            var who = phase != RoundPhase.None && HasAvatar ? _avatar : Entity.Null;
            if (_view == null || !_view.IsFor(_sim, who) || _view.Blind != blind)
                _view = blind ? Perception.Blind(_sim) : Perception.For(_sim, who);
            return _view;
        }
    }

    /// <summary>A round for one: take a seat and deal at once. Returns where the character is.</summary>
    private int3 StartSoloRound(int3 center)
    {
        var (look, clothes) = HeroAppearance();
        var round = _sim.Round;
        round.Sit(Guid.Empty, string.IsNullOrWhiteSpace(HeroName) ? "Arin" : HeroName.Trim(), look, HeroTraits, clothes);
        if (HeroTrade != null)
            round.SetPreferences(Guid.Empty, [_sim.Defs.Trade(HeroTrade)], []);
        // Bots ask for the trades that have a game to play, the hero's aside, so a solo round has its cast.
        var wanted = new[] { "Innkeeper", "Blacksmith", "Healer", "QuestBroker" }.Where(t => t != HeroTrade).ToList();
        for (var i = 0; i < Bots; i++)
        {
            var id = new Guid(i + 1, 0, 0, new byte[8]);
            round.Sit(id, $"Bot {i + 1}", null, null, null);
            if (wanted.Count > 0)
                round.SetPreferences(id, wanted.Skip(i % wanted.Count).Concat(wanted.Take(i % wanted.Count)).Select(_sim.Defs.Trade).ToList(), []);
            _bots.Add(new Sim.Bots.BotPlayer(() => _sim, id, Issue, seed + (ulong)i));
        }
        round.Start(_sim);
        foreach (var e in _sim.World.Query<Avatar>())
            if (!_sim.World.Has<PlayerCharacter>(e))
                return _sim.World.Get<Position>(e).Cell;
        return center;
    }

    /// <summary>The host made their character before the lobby existed: seat them now.</summary>
    private void SeatHost()
    {
        if (_coop is not { IsHost: true } session || !InLobby || !Adventure)
            return;
        var (look, clothes) = HeroAppearance();
        session.Send(new JoinLobbyCommand(session.Me.Id, string.IsNullOrWhiteSpace(HeroName) ? session.Me.Name : HeroName.Trim(), look, HeroTraits ?? [], clothes));
    }

    /// <summary>Tell the player who they've become.</summary>
    private void AnnounceTrade()
    {
        if (!HasAvatar || !_sim.World.TryGet<Trade>(_avatar, out var trade))
            return;
        _tradeRequested = false;
        Notify(MessageKind.Good, $"You are {_sim.World.Get<Creature>(_avatar).Name}, the {trade.Def.Label}.");
    }

    private void DrawLobby(CoopSession session)
    {
        var screen = ImGui.GetIO().DisplaySize;
        var pos = new float2(screen.X * 0.5f, screen.Y * 0.5f);
        float2? middle = new float2(0.5f, 0.5f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.FirstUseEver, in middle);
        ImGui.Begin("The round is about to start###lobby"u8, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse);
        var round = _sim.Round;

        foreach (var p in session.Players)
        {
            var seat = round.SeatOf(p.Id);
            var state = seat == null ? "choosing a character" : seat.Ready ? "ready" : "not ready";
            TextC(seat is { Ready: true } ? Theme.Good : Theme.Dim, $"{p.Name}{(p.IsHost ? " (host)" : "")}: {state}");
        }

        if (round.SeatOf(session.Me.Id) is { } mine)
        {
            ImGui.Separator();
            TextC(Theme.Dim, "Which trade do you want? Choices are ranked in the order you pick them.");
            DrawPreferences(session, mine);
            ImGui.Separator();
            if (mine.Ready)
            {
                if (ImGui.Button("Not ready"u8, in _autoSize))
                    session.Send(new SetReadyCommand(session.Me.Id, false));
            }
            else if (ImGui.Button("Ready"u8, in _autoSize))
            {
                session.Send(new SetReadyCommand(session.Me.Id, true));
            }
        }

        if (session.IsHost)
        {
            ImGui.SameLine(0, 16);
            var seated = round.Seats.Count;
            if (seated == 0)
                ImGui.BeginDisabled(true);
            if (ImGui.Button("Deal the trades and start"u8, in _autoSize))
                session.Send(new StartRoundCommand());
            if (seated == 0)
                ImGui.EndDisabled();
            TextC(Theme.Dim, $"{round.Seats.Count(s => s.Ready)} of {session.Players.Count} ready. Anyone still choosing can take a trade once it's under way.");
        }
        DrawChat(session);
        ImGui.End();
    }

    private void DrawPreferences(CoopSession session, LobbySeat seat)
    {
        foreach (var trade in _sim.Defs.AllTrades)
        {
            if (trade.Unlimited)
                continue;
            ImGui.PushID(trade.Index);
            var rank = seat.Wanted.IndexOf(trade);
            var never = seat.Never.Contains(trade);
            ImGui.Text(Capitalized(trade.Label));
            Tip(trade.Description);
            ImGui.SameLine(160, -1);
            if (ImGui.SmallButton(rank >= 0 ? $"Choice {rank + 1}" : "Want"))
            {
                var wanted = seat.Wanted.ToList();
                if (!wanted.Remove(trade))
                    wanted.Add(trade);
                session.Send(new SetTradePreferencesCommand(session.Me.Id, wanted, seat.Never.Where(t => t != trade).ToList()));
            }
            ImGui.SameLine(0, 8);
            if (ImGui.SmallButton(never ? "Refused" : "Never"))
            {
                var refused = seat.Never.ToList();
                if (!refused.Remove(trade))
                    refused.Add(trade);
                session.Send(new SetTradePreferencesCommand(session.Me.Id, seat.Wanted.Where(t => t != trade).ToList(), refused));
            }
            ImGui.PopID();
        }
    }

    /// <summary>
    /// Arriving once the round is under way. Every trade is offered whether or not a person holds
    /// it: showing which are open would tell this player who the other players are.
    /// </summary>
    private void DrawTakeTrade(CoopSession session, float2 center, float2? middle)
    {
        ImGui.SetNextWindowPos(in center, ImGuiCond.Always, in middle);
        ImGui.Begin("Who are you?###take-trade"u8, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
        if (_tradeRequested)
        {
            TextC(Theme.Dim, "Arriving...");
            ImGui.End();
            return;
        }
        Wrapped("The round is under way. Whose work would you like to take up? You'll get the closest thing going.");
        foreach (var trade in _sim.Defs.AllTrades)
        {
            if (trade.Unlimited)
                continue;
            if (ImGui.Button(Capitalized(trade.Label), in _autoSize))
                Take(session, [trade]);
            Tip(trade.Description);
        }
        if (ImGui.Button("Anything going"u8, in _autoSize))
            Take(session, []);
        ImGui.End();
    }

    private void Take(CoopSession session, IReadOnlyList<TradeDef> wanted)
    {
        session.Send(new TakeTradeCommand(session.Me.Id, wanted, []));
        _tradeRequested = true;
    }
}
