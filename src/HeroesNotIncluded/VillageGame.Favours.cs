using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Favours;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// Favours: what players ask of each other, for silver they name. Known through View (Perception).
public sealed partial class VillageGame
{
    private readonly ImInputString _favourText = new(FavourBoard.MaxTextLength + 8, ""u8);
    private int _favourPrice = 5;
    private int _favourHours = 4;
    private int _errandItem = -1;
    private int _errandCount = 5;

    /// <summary>Open the favours header next frame (reading the board).</summary>
    private bool _favoursOpen;

    /// <summary>What an errand can ask for: things that come in stacks, by name.</summary>
    private List<Sim.Defs.ThingDef>? _errandItems;

    /// <summary>The favours you've read on the board, taken on or pinned yourself; at the board, a note to pin.</summary>
    private void DrawFavours()
    {
        if (_favoursOpen)
        {
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            _favoursOpen = false;
        }
        if (!ImGui.CollapsingHeader("Favours"u8, ImGuiTreeNodeFlags.None))
            return;
        var now = _sim.Clock.Tick;
        var any = false;
        var closed = 0;
        foreach (var f in View.Favours().OrderBy(f => f.Status != FavourStatus.Open).ThenBy(f => f.Deadline))
        {
            var open = f.Status == FavourStatus.Open;
            if (!open)
                closed++;
            if (!open && !_showClosedOrders)
                continue;
            any = true;
            ImGui.PushID(f.Id);
            DrawFavourCard(f, open, now);
            ImGui.PopID();
        }
        if (!any)
            TextC(Theme.Dim, "You haven't read of any favours. They're pinned to the notice board in the square.");
        ClosedToggle(closed, "favours");

        if (FavourBoard.BoardBy(_sim, _avatar).IsNull)
        {
            TextC(Theme.Dim, Controls.Keys("To ask a favour, pin a note to the notice board in the square (~F~ at it)."));
            return;
        }
        Gui.Rule();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##favour"u8, "What you want done"u8, _favourText, ImGuiInputTextFlags.None, null, IntPtr.Zero);
        OnScreenKeyboard.AfterTextField();
        ImGui.SetNextItemWidth(Gui.S(120));
        ImGui.SliderInt("silver"u8, ref _favourPrice, 0, 50, "%d"u8, ImGuiSliderFlags.AlwaysClamp);
        ImGui.SameLine(0, 12);
        ImGui.SetNextItemWidth(Gui.S(120));
        ImGui.SliderInt("hours"u8, ref _favourHours, 1, FavourBoard.MaxHours, "%d"u8, ImGuiSliderFlags.AlwaysClamp);
        if (ImGui.Button("Pin it to the board"u8, in _autoSize))
        {
            Issue(new PostFavourCommand(_avatar, Narsil.Ui.ImGuiText.Value(_favourText), _favourPrice, _favourHours));
            _favourText.SetValue("".AsSpan());
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip("Whoever reads the board will know. Say what you want done in your own words.");

        // An errand: so many of something, brought to you. A labourer will take one on.
        _errandItems ??= _sim.Defs.AllThings.Where(t => t.Category == Sim.Defs.ThingCategory.Item && t.StackLimit > 1)
            .OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase).ToList();
        ImGui.SetNextItemWidth(160);
        var label = _errandItem >= 0 && _errandItem < _errandItems.Count ? _errandItems[_errandItem].Label : "an errand: bring me...";
        if (ImGui.BeginCombo("##errand"u8, label, ImGuiComboFlags.None))
        {
            for (var i = 0; i < _errandItems.Count; i++)
                if (ImGui.Selectable(_errandItems[i].Label, i == _errandItem, ImGuiSelectableFlags.None, default))
                    _errandItem = i;
            ImGui.EndCombo();
        }
        ImGui.SameLine(0, 8);
        ImGui.SetNextItemWidth(90);
        ImGui.SliderInt("##errandCount"u8, ref _errandCount, 1, 30, "%d of them"u8, ImGuiSliderFlags.AlwaysClamp);
        ImGui.SameLine(0, 8);
        var pick = _errandItem >= 0 && _errandItem < _errandItems.Count;
        if (!pick)
            ImGui.BeginDisabled(true);
        if (ImGui.Button("Pin the errand"u8, in _autoSize))
            Issue(new PostErrandCommand(_avatar, _errandItems[_errandItem], _errandCount, _favourPrice, _favourHours));
        if (!pick)
            ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip("Ask for so many of a thing, brought to you, for the silver above. Bringing it is doing it: they're paid when they hand it over. The town's labourer takes these on.");
    }
    /// <summary>
    /// A favour as a card, like an order's: who asks and for how much, the time left as a bar,
    /// and the buttons that are yours to press. Their words are in the first line's tooltip.
    /// </summary>
    private void DrawFavourCard(KnownFavour f, bool open, long now)
    {
        var tip = $"\u201C{f.Text}\u201D";
        if (!open)
            tip += "\n" + f.Status switch
            {
                FavourStatus.Done => f.AssigneeName.Length > 0 ? $"Done by {f.AssigneeName}." : "Done.",
                FavourStatus.Expired => "Nobody got to it in time.",
                _ => "Called off.",
            };
        var title = $"{(f.Mine ? "You ask" : f.PosterName + " asks")}: {f.Text}";
        CardHead(f.Wants != null ? "ui/icon_haul" : "ui/icon_call", open, open ? (f.Mine ? Theme.Good : Theme.Text) : Theme.Dim, title, f.Price, tip);
        if (open)
        {
            var (share, color, words) = TimeLeft(f.PostedTick, f.Deadline, now);
            var take = !f.Mine && f.AssigneeName.Length == 0;
            var hand = f.MineToDo && f.Wants != null;
            var pay = f.Mine && f.AssigneeName.Length > 0;
            var keep = take ? SmallButtonWidth("I'll do it") : hand ? SmallButtonWidth("Hand it over") : 0f;
            CardBar(f.AssigneeName.Length > 0 ? (f.MineToDo ? "You're on it" : $"{f.AssigneeName} is on it") : "Nobody's taken it",
                share, color, words, keep);
            if (take)
            {
                ImGui.SameLine(0, Gui.S(8));
                if (ImGui.SmallButton("I'll do it"u8))
                    Issue(new AcceptFavourCommand(_avatar, f.Id));
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip(f.Wants != null ? "Take it on. Bring them what they want and they'll pay you on the spot."
                        : "Say aloud that you'll see to it. They pay you when they say it's done.");
            }
            if (hand && f.Wants is { } wants)
            {
                ImGui.SameLine(0, Gui.S(8));
                if (ImGui.SmallButton("Hand it over"u8))
                    Issue(new DeliverFavourCommand(_avatar, f.Id));
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip($"Give {f.PosterName} the {f.Count} {wants.Label} from your hands and pack. They have to be within reach.");
            }
            if (f.Mine)
            {
                if (pay)
                {
                    if (ImGui.SmallButton("Done: pay them"u8))
                        Issue(new CompleteFavourCommand(_avatar, f.Id));
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                        Tip($"Hand {f.AssigneeName} the {f.Price} silver. They have to be within reach.");
                    ImGui.SameLine(0, Gui.S(6));
                }
                if (ImGui.SmallButton("Call it off"u8))
                    Issue(new CancelFavourCommand(_avatar, f.Id));
            }
        }
        CardEnd();
    }
}
