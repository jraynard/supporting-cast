using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// A newcomer's first day in a trade: a few short tips under your portrait (the trade's "tips" in
// trades.json), and a soft ring on each of its stations (its "stations") until you've stood beside
// it. Both go when the tips are closed, or with Settings > Interface > Tips off. Nothing is saved:
// a loaded game starts them over, which is what someone coming back to it wants anyway.
public sealed partial class VillageGame
{
    private int _tipIndex;
    private bool _tipsClosed;

    /// <summary>The trade's stations in its workplace, found every few seconds (furniture is public: no secret read).</summary>
    private readonly List<Entity> _stations = [];
    private float _stationsFoundAt = float.NegativeInfinity;

    /// <summary>Stations the player has stood beside this session: their rings are done.</summary>
    private readonly HashSet<Entity> _visitedStations = [];

    private bool TipsShowing => Settings.Current.ShowTips && !_tipsClosed && HasAvatar && InRound
        && _sim.World.TryGet<Trade>(_avatar, out var t) && t.Def.Tips.Count > 0;

    private void DrawTips()
    {
        if (!TipsShowing)
            return;
        var tips = _sim.World.Get<Trade>(_avatar).Def.Tips;
        _tipIndex = Math.Clamp(_tipIndex, 0, tips.Count - 1);
        _hud.Next(Anchor.TopLeft, ScreenFit.Width(300, 0.3f));
        var open = true;
        var visible = Panel.Begin("Getting started###tips", ref open, Hud.Flags);
        _hud.Took();
        if (visible)
        {
            Wrapped(tips[_tipIndex]);
            if (tips.Count > 1)
            {
                ImGui.BeginDisabled(_tipIndex == 0);
                if (ImGui.SmallButton("<##tip-back"u8))
                    _tipIndex--;
                ImGui.EndDisabled();
                ImGui.SameLine(0, Gui.S(6));
                TextC(Theme.Dim, $"{_tipIndex + 1} of {tips.Count}");
                ImGui.SameLine(0, Gui.S(6));
                ImGui.BeginDisabled(_tipIndex == tips.Count - 1);
                if (ImGui.SmallButton(">##tip-next"u8))
                    _tipIndex++;
                ImGui.EndDisabled();
            }
        }
        Panel.End();
        if (!open)
            _tipsClosed = true;
    }

    /// <summary>The rings on the trade's stations the player hasn't been to yet, for the renderer.</summary>
    private void UpdateStationMarks()
    {
        var marks = _renderer.StationMarks;
        marks.Clear();
        if (!TipsShowing)
            return;
        var w = _sim.World;
        var trade = w.Get<Trade>(_avatar).Def;
        if (trade.StationDefs.Count == 0 || trade.WorkplaceDef is not { } shop)
            return;
        if (_time - _stationsFoundAt > 3f)
        {
            _stationsFoundAt = _time;
            _stations.Clear();
            var building = ShopSystem.BuildingOf(_sim, shop);
            if (!building.IsNull)
                foreach (var e in w.Each<Thing>())
                    if (trade.StationDefs.Contains(w.Get<Thing>(e).Def) && Buildings.Of(_sim, e) == building)
                        _stations.Add(e);
        }
        var at = w.Get<Position>(_avatar).Cell;
        foreach (var s in _stations)
        {
            if (!w.IsAlive(s) || _visitedStations.Contains(s))
                continue;
            if (AvatarSystem.InReach(_sim, at, s))
            {
                _visitedStations.Add(s);
                continue;
            }
            marks.Add(w.Get<Thing>(s).Cell);
        }
    }
}
