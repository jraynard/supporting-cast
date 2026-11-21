using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// Commissions: so many of a thing wanted, for a price, pinned to the notice board (Sim.Shops.Commissions).
// They're orders: the ones you know of are on the order cards; this is the board's own part, pinning one.
public sealed partial class VillageGame
{
    private int _commissionItem = -1;
    private int _commissionCount = 1;
    private int _commissionPrice = 10;
    private int _commissionHours = 8;

    /// <summary>Open the commissions header next frame (reading the board).</summary>
    private bool _commissionsOpen;

    /// <summary>What can be commissioned: what the town's shops make, by name. Worked out when you read the board.</summary>
    private List<Sim.Defs.ThingDef> _commissionable = [];

    /// <summary>At the notice board: pin a commission. Away from it, where to go to pin one.</summary>
    private void DrawCommissions()
    {
        if (_commissionsOpen)
        {
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            _commissionsOpen = false;
            _commissionable = Commissions.Commissionable(_sim);
        }
        if (!ImGui.CollapsingHeader("Commissions"u8, ImGuiTreeNodeFlags.None))
            return;
        if (Commissions.BoardBy(_sim, _avatar).IsNull)
        {
            TextC(Theme.Dim, Controls.Keys("To commission something, pin it to the notice board in the square (~F~ at it). Whoever makes it will hear of it, and anyone who reads the board."));
            return;
        }
        if (_commissionable.Count == 0)
            _commissionable = Commissions.Commissionable(_sim);
        TextC(Theme.Dim, "Name the thing, how many and what you'll pay. Whoever makes it brings it, and you pay when it's all come.");
        ImGui.SetNextItemWidth(Gui.S(200));
        var pick = _commissionItem >= 0 && _commissionItem < _commissionable.Count;
        if (ImGui.BeginCombo("##commission"u8, pick ? _commissionable[_commissionItem].Label : "Wanted...", ImGuiComboFlags.None))
        {
            for (var i = 0; i < _commissionable.Count; i++)
                if (ImGui.Selectable(_commissionable[i].Label, i == _commissionItem, ImGuiSelectableFlags.None, default))
                    _commissionItem = i;
            ImGui.EndCombo();
        }
        ImGui.SameLine(0, Gui.S(8));
        ImGui.SetNextItemWidth(Gui.S(110));
        ImGui.SliderInt("##commissionCount"u8, ref _commissionCount, 1, 30, "%d of them"u8, ImGuiSliderFlags.AlwaysClamp);
        ImGui.SetNextItemWidth(Gui.S(120));
        ImGui.SliderInt("silver"u8, ref _commissionPrice, 0, 100, "%d"u8, ImGuiSliderFlags.AlwaysClamp);
        ImGui.SameLine(0, Gui.S(12));
        ImGui.SetNextItemWidth(Gui.S(120));
        ImGui.SliderInt("hours"u8, ref _commissionHours, 1, Commissions.MaxHours, "%d"u8, ImGuiSliderFlags.AlwaysClamp);
        ImGui.BeginDisabled(!pick);
        if (ImGui.Button("Pin the commission"u8, in _autoSize) && pick)
            Issue(new PostCommissionCommand(_avatar, _commissionable[_commissionItem], _commissionCount, _commissionPrice, _commissionHours));
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip(pick ? $"“{Commissions.Words(_commissionable[_commissionItem], _commissionCount, _commissionPrice)}”\nYou pay when the whole lot's been handed to you."
                : "Pick what you want made first.");
    }
}
