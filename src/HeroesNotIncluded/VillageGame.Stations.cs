using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

/// <summary>
/// The station card: what's at the station beside you, between times (the pot's contents, the
/// oven's smell, what the prep table could make). One card at the bottom for whichever station F
/// would work, with a tab for each other one in reach. In a cramped kitchen the pot, spit, oven,
/// prep table and block each opened a panel of their own, stacked up over the room you were
/// working in (playtest).
/// </summary>
public sealed partial class VillageGame
{
    /// <summary>A station in reach and how to show it: its name, and what goes on its card.</summary>
    private readonly record struct StationCard(string Name, Entity Station, Action Body);

    private readonly List<StationCard> _stationCards = [];

    /// <summary>The station whose tab you picked: shown while it's in reach, till you turn to another.</summary>
    private Entity _stationPicked = Entity.Null;

    /// <summary>The station F worked when you picked: turning to face another one follows it again.</summary>
    private Entity _stationFaced = Entity.Null;

    private void DrawStationCard()
    {
        var cards = _stationCards;
        cards.Clear();
        if (!HasAvatar)
            return;
        OfferPot(cards);
        OfferSpit(cards);
        OfferOven(cards);
        OfferPrep(cards);
        OfferBlock(cards);
        OfferBar(cards);
        if (cards.Count == 0)
        {
            _stationPicked = Entity.Null;
            return;
        }

        var faced = FacedStation(cards);
        if (faced != _stationFaced)
            (_stationFaced, _stationPicked) = (faced, Entity.Null);
        var shown = cards.FindIndex(c => c.Station == _stationPicked);
        if (shown < 0)
            shown = Math.Max(0, cards.FindIndex(c => c.Station == faced));

        _hud.Next(Anchor.Bottom);
        Panel.Begin($"{cards[shown].Name}###station-card", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav);
        _hud.Took();
        if (cards.Count > 1)
        {
            for (var i = 0; i < cards.Count; i++)
            {
                if (i > 0)
                    ImGui.SameLine(0, Gui.S(4));
                var on = i == shown;
                if (on)
                {
                    var c = Theme.AccentSoft;
                    ImGui.PushStyleColor(ImGuiCol.Button, in c);
                }
                if (ImGui.SmallButton($"{TabName(cards[i].Name)}##station-tab{i}"))
                    _stationPicked = cards[i].Station;
                if (on)
                    ImGui.PopStyleColor(1);
            }
            Gui.Rule();
        }
        ImGui.PushID("station-card"u8);
        cards[shown].Body();
        ImGui.PopID();
        Panel.End();
    }

    /// <summary>The station in reach that F would work just now, or Entity.Null.</summary>
    private Entity FacedStation(List<StationCard> cards)
    {
        var target = WorkTarget();
        if (target.Kind == Sim.Systems.AvatarActionKind.None)
            return Entity.Null;
        foreach (var c in cards)
            if (c.Station == target.Target || _sim.World.TryGet<Thing>(c.Station, out var t) && t.Cell == target.Cell)
                return c.Station;
        return Entity.Null;
    }

    /// <summary>A card's name for its tab: "The stew pot" as "Stew pot".</summary>
    private static string TabName(string name) => name.StartsWith("The ", StringComparison.Ordinal) ? Cap(name[4..]) : name;
}
