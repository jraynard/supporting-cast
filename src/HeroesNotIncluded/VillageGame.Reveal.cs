using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Characters;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

/// <summary>
/// The end-of-round reveal: how the season came out, the town's Renown and Dread over it (the only
/// place their numbers show), and everyone's trade and earnings. All of it is
/// <see cref="RoundSystem.Summary"/>, built in the sim when the round ended, so every peer shows
/// the same. M4 adds who served the Thing Below.
/// </summary>
public sealed partial class VillageGame
{
    private bool _revealHidden;

    /// <summary>The saved character a co-op guest arrived as (the in-game picker or creator, or the Join panel's).</summary>
    private SavedCharacter? _arrivedAs;

    /// <summary>The round was under way here (started in this game, or seen live after a load): a save loaded already over isn't credited again.</summary>
    private bool _sawRoundLive;
    private bool _roundRecorded;

    private void DrawReveal()
    {
        if (!RoundOver || _sim.Round.Summary is not { } s)
            return;
        if (_revealHidden)
        {
            _hud.Next(Anchor.Top);
            Panel.Begin("##reveal-again", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav);
            _hud.Took();
            if (ImGui.Button("The season's end"u8, in _autoSize))
                _revealHidden = false;
            Panel.End();
            return;
        }

        var screen = ImGui.GetIO().DisplaySize;
        var pos = new float2(screen.X * 0.5f, screen.Y * 0.5f);
        float2? middle = new float2(0.5f, 0.5f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Appearing, in middle);
        ScreenFit.Next();
        Panel.Begin("The season's end###reveal", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse);
        PadWindow("###reveal", () => _revealHidden = true);

        using (Gui.HeaderFont())
            TextC(s.Outcome == RoundOutcome.Won ? Theme.Good : Theme.Bad, s.Headline);
        Gui.Rule();

        TextC(s.Renown >= s.RenownTarget ? Theme.Good : Theme.Warn, $"Renown {s.Renown:F0} (needed {s.RenownTarget:F0})");
        TextC(s.Dread >= s.DreadLimit ? Theme.Bad : Theme.Dim, $"Dread {s.Dread:F0} of {s.DreadLimit:F0}");
        DrawStandingGraph(s);

        Gui.Rule();
        TextC(Theme.Dim, $"{s.Parties} {(s.Parties == 1 ? "party" : "parties")} came to town; {s.AdventurersServed} adventurers had their say of it.");
        TextC(Theme.Dim, $"Quests cleared {s.QuestsCleared}, failed {s.QuestsFailed}; adventurers lost on the road {s.AdventurersLost}.");

        Gui.Rule();
        if (ImGui.BeginTable("##reveal-people"u8, 4, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Who"u8);
            ImGui.TableSetupColumn("Trade"u8);
            ImGui.TableSetupColumn("Earned"u8);
            ImGui.TableSetupColumn(""u8);
            ImGui.TableHeadersRow();
            foreach (var p in s.People)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                TextC(p.Player ? Theme.Accent : Theme.Dim, p.Name);
                ImGui.TableNextColumn();
                ImGui.Text(p.Trade);
                ImGui.TableNextColumn();
                TextC(p.Earned > 0 ? Theme.Good : Theme.Dim, $"{p.Earned:+0;-0;0} silver");
                ImGui.TableNextColumn();
                if (!p.Alive)
                    TextC(Theme.Bad, "dead");
            }
            ImGui.EndTable();
        }

        Gui.Rule();
        if (ImGui.Button("Look around"u8, in _autoSize))
            _revealHidden = true;
        ImGui.SameLine(0, Gui.S(8));
        if (ImGui.Button("Back to the title screen"u8, in _autoSize))
        {
            if (_coop != null)
                LeaveCoop();
            else
                Host.SwitchTo(new TitleGame());
        }
        Panel.End();
    }

    /// <summary>Renown (gold) and Dread (red) hour by hour, with the target and the limit as faint lines.</summary>
    /// <summary>
    /// Once, when a round played here ends: one more round in their trade for the saved character
    /// this player was. Each peer writes only its own roster. A guest who took over a villager
    /// mid-round played as them, not as a saved character, so isn't credited.
    /// </summary>
    private void RecordRound()
    {
        if (_sim.Round.Live || Loaded == null)
            _sawRoundLive = true;
        if (_roundRecorded || !RoundOver || _sim.Round.Summary is not { } s)
            return;
        _roundRecorded = true;
        var credited = _coop is { IsHost: false } ? _arrivedAs : _arrivedAs ?? Character;
        if (!_sawRoundLive || credited == null)
            return;
        var w = _sim.World;
        var me = LocalPlayer is { } id ? Players.CharacterOf(_sim, id) : _avatar;
        if (me.IsNull || !w.IsAlive(me) || !w.TryGet<Trade>(me, out var trade) || !w.TryGet<Creature>(me, out var body))
            return;
        if (s.People.FirstOrDefault(p => p.Player && p.Name == body.Name) is not { } row)
            return;
        var roster = _roster ??= CharacterRoster.Load(Host, _sim.Defs);
        if (roster.Get(credited.Id) is { } saved) // not if let go of mid-round
            roster.Update(saved with { Progress = saved.Progress.With(trade.Def.Name, row.Earned, row.Alive) });
    }

    private void DrawStandingGraph(RoundSummary s)
    {
        var hours = Math.Max(s.RenownByHour.Count, s.DreadByHour.Count);
        if (hours < 2)
            return;
        var size = new float2(Gui.S(360), Gui.S(110));
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var dl = ImGui.GetWindowDrawList();
        var bottomRight = origin + size;
        dl.AddRectFilled(in origin, in bottomRight, new ImColor8(20, 16, 14, 200), Gui.S(3), ImDrawFlags.None);

        var top = Math.Max(1f, new[] { s.RenownTarget, s.DreadLimit }.Concat(s.RenownByHour).Concat(s.DreadByHour).Max());
        var bottom = Math.Min(0f, s.RenownByHour.Concat(s.DreadByHour).DefaultIfEmpty(0f).Min());
        float2 At(int hour, float v) => new(
            origin.X + size.X * hour / (hours - 1),
            origin.Y + size.Y * (1f - (v - bottom) / (top - bottom)));

        void Level(float v, float4 colour)
        {
            var a = At(0, v);
            var b = At(hours - 1, v);
            dl.AddLine(in a, in b, Theme.U8(colour with { W = 0.35f }), Gui.S(1));
        }
        void Line(List<float> values, float4 colour)
        {
            for (var i = 1; i < values.Count; i++)
            {
                var a = At(i - 1, values[i - 1]);
                var b = At(i, values[i]);
                dl.AddLine(in a, in b, Theme.U8(colour), Gui.S(2));
            }
        }
        Level(s.RenownTarget, Theme.Good);
        Level(s.DreadLimit, Theme.Bad);
        Line(s.DreadByHour, Theme.Bad);
        Line(s.RenownByHour, Theme.Good);
    }
}
