using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// Pausing. Alone, the clock holds while the game menu (or a window it opened) is up, and the
// pause key holds it till it's pressed again: there's nobody else for the town to keep going for.
// In co-op the town never waits for one player, but it does for everyone: the pause key asks,
// and time holds once all agree (Sim.Round.PauseVote), then runs again once all are ready.
public sealed partial class VillageGame
{
    /// <summary>Single player: the pause key's hold, till it's pressed again.</summary>
    private bool _soloHold;

    /// <summary>Single player: the game menu, or one of the windows it opens, is up.</summary>
    private bool MenuHold => _coop == null && (_gameMenu || _settings.IsOpen || _saveWindow != SaveWindow.None || _showCredits);

    /// <summary>
    /// Time is held for the players: alone, by the pause key or the menu; in co-op, by everyone's
    /// agreement. Not the god-mode speed buttons' pause (<see cref="_speed"/> 0), which gives orders.
    /// </summary>
    private bool PauseHeld => _coop == null ? _soloHold || MenuHold || TutorialHold : _sim.PauseVote.Held;

    /// <summary>
    /// Single player: the tutorial's card is open, so time holds while it's read ("Got it" lets it run).
    /// Never at hands-on work, which has the pad to itself and needs the clock.
    /// </summary>
    private bool TutorialHold => _coop == null && Settings.Current.TutorialHold && TutorialCardOpen && !Working;

    /// <summary>The pause key: holds or releases alone, and asks, agrees or says ready in co-op.</summary>
    private void HandlePauseKey()
    {
        if (!Controls.Map.Pressed(Act.Pause) || ImGui.GetIO().WantTextInput || _cameraMode)
            return;
        if (_coop == null)
            _soloHold = !_soloHold;
        else
            AnswerPause();
    }

    /// <summary>Co-op: whatever the pause key means now, from where the vote stands.</summary>
    private void AnswerPause()
    {
        if (_coop is not { } session || InLobby || RoundOver)
            return;
        var me = session.Me.Id;
        var vote = _sim.PauseVote;
        switch (vote.Phase)
        {
            case PausePhase.None:
                Issue(new CallPauseCommand(me));
                break;
            case PausePhase.Calling when !vote.HasAgreed(me):
                Issue(new PauseAnswerCommand(me, true));
                break;
            case PausePhase.Held:
                Issue(new PauseAnswerCommand(me, !vote.HasAgreed(me)));
                break;
        }
    }

    /// <summary>The game menu's pause line in co-op: what pressing it would do, or null when nothing.</summary>
    private string? PauseMenuLabel()
    {
        if (_coop is not { } session || InLobby || RoundOver)
            return null;
        var vote = _sim.PauseVote;
        return vote.Phase switch
        {
            PausePhase.None => "Ask everyone to pause",
            PausePhase.Calling when !vote.HasAgreed(session.Me.Id) => "Agree to pause",
            PausePhase.Held => vote.HasAgreed(session.Me.Id) ? "Not ready after all" : "I'm ready",
            _ => null,
        };
    }

    /// <summary>Who's still to agree, or to be ready, by name.</summary>
    private string WaitingOn() => string.Join(", ", _sim.PauseVote.Waiting.Select(p => PauseVote.NameOf(_sim, p)));

    /// <summary>
    /// A call to pause, at the top while it stands; while time is held, a panel in the middle
    /// saying so (alone, unless the menu is the one holding it).
    /// </summary>
    private void DrawPause()
    {
        if (_coop is { } session)
        {
            var vote = _sim.PauseVote;
            var me = session.Me.Id;
            if (vote.Phase == PausePhase.Calling)
            {
                _hud.Next(Anchor.Top);
                Panel.Begin("##pause-call", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDecoration);
                _hud.Took();
                TextC(Theme.Accent, $"{PauseVote.NameOf(_sim, vote.Caller)} asks to pause.");
                ImGui.SameLine(0, Gui.S(10));
                TextC(Theme.Dim, $"{vote.Agreed.Count} of {vote.Present.Count} agree");
                if (!vote.HasAgreed(me))
                {
                    if (ImGui.Button($"Agree [{Controls.Glyph(Act.Pause)}]"))
                        Issue(new PauseAnswerCommand(me, true));
                    ImGui.SameLine();
                    if (ImGui.Button("Carry on"u8))
                        Issue(new PauseAnswerCommand(me, false));
                }
                else
                {
                    TextC(Theme.Dim, $"Waiting on {WaitingOn()}.");
                    ImGui.SameLine();
                    if (vote.Caller == me && ImGui.Button("Never mind"u8))
                        Issue(new PauseAnswerCommand(me, false));
                }
                Panel.End();
            }
            else if (vote.Held)
            {
                PausedPanel(() =>
                {
                    var ready = vote.HasAgreed(me);
                    TextC(Theme.Dim, vote.Waiting.Any() ? $"Waiting on {WaitingOn()}." : "Everyone's ready.");
                    ImGui.Spacing();
                    if (ImGui.Button(ready ? $"Not ready after all [{Controls.Glyph(Act.Pause)}]" : $"I'm ready [{Controls.Glyph(Act.Pause)}]", new float2(Gui.S(240), 0)))
                        Issue(new PauseAnswerCommand(me, !ready));
                });
            }
            return;
        }
        if (_soloHold && !MenuHold)
            PausedPanel(() =>
            {
                TextC(Theme.Dim, $"Time holds till you're back. {Controls.Glyph(Act.Pause)} to carry on.");
                ImGui.Spacing();
                if (ImGui.Button("Carry on"u8, new float2(Gui.S(240), 0)))
                    _soloHold = false;
            });
    }

    private void PausedPanel(Action body)
    {
        var center = ImGui.GetIO().DisplaySize * 0.5f;
        float2? middle = new float2(0.5f, 0.5f);
        ImGui.SetNextWindowPos(in center, ImGuiCond.Always, in middle);
        Panel.Begin("##paused", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDecoration);
        using (Gui.DisplayFont(28f))
            TextC(Theme.Accent, "Paused");
        body();
        Panel.End();
    }
}
