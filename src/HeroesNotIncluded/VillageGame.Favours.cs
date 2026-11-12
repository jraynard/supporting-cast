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

    /// <summary>The favours you've heard asked, taken on or asked yourself, and a box to ask one.</summary>
    private void DrawFavours()
    {
        if (!ImGui.CollapsingHeader("Favours"u8, ImGuiTreeNodeFlags.None))
            return;
        var now = _sim.Clock.Tick;
        var any = false;
        foreach (var f in View.Favours().OrderBy(f => f.Status != FavourStatus.Open).ThenBy(f => f.Deadline))
        {
            var open = f.Status == FavourStatus.Open;
            if (!open && !_showClosedOrders)
                continue;
            any = true;
            ImGui.PushID(f.Id);
            TextC(open ? (f.Mine ? Theme.Good : Theme.Text) : Theme.Dim, $"{(f.Mine ? "You ask" : f.PosterName + " asks")}, {f.Price} silver");
            ImGui.PushTextWrapPos(0f);
            TextC(Theme.Dim, $"“{f.Text}”");
            ImGui.PopTextWrapPos();
            if (open)
            {
                var left = Math.Max(0, f.Deadline - now) / (float)SimClock.TicksPerHour;
                var whole = Math.Max(1, f.Deadline - f.PostedTick) / (float)SimClock.TicksPerHour;
                Gui.Bar(f.AssigneeName.Length > 0 ? (f.MineToDo ? "You're on it" : $"{f.AssigneeName} is on it") : "Nobody's taken it",
                    left / whole, left < 0.5f ? Theme.Bad : left / whole < 0.4f ? Theme.Warn : Theme.Good, $"{left:F1}h left", 170f);
                if (!f.Mine && f.AssigneeName.Length == 0)
                {
                    if (ImGui.Button("I'll do it"u8, in _autoSize))
                        Issue(new AcceptFavourCommand(_avatar, f.Id));
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                        Tip("Say aloud that you'll see to it. They pay you when they say it's done.");
                }
                if (f.Mine)
                {
                    if (f.AssigneeName.Length > 0)
                    {
                        if (ImGui.Button("Done: pay them"u8, in _autoSize))
                            Issue(new CompleteFavourCommand(_avatar, f.Id));
                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                            Tip($"Hand {f.AssigneeName} the {f.Price} silver. They have to be within reach.");
                        ImGui.SameLine(0, 6);
                    }
                    if (ImGui.Button("Call it off"u8, in _autoSize))
                        Issue(new CancelFavourCommand(_avatar, f.Id));
                }
            }
            else
                TextC(Theme.Dim, f.Status switch
                {
                    FavourStatus.Done => f.AssigneeName.Length > 0 ? $"Done by {f.AssigneeName}." : "Done.",
                    FavourStatus.Expired => "Nobody got to it in time.",
                    _ => "Called off.",
                });
            Gui.Rule();
            ImGui.PopID();
        }
        if (!any)
            TextC(Theme.Dim, "Nobody you've heard has asked a favour.");

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##favour"u8, "What you want done"u8, _favourText, ImGuiInputTextFlags.None, null, IntPtr.Zero);
        ImGui.SetNextItemWidth(120);
        ImGui.SliderInt("silver"u8, ref _favourPrice, 0, 50, "%d"u8, ImGuiSliderFlags.AlwaysClamp);
        ImGui.SameLine(0, 12);
        ImGui.SetNextItemWidth(120);
        ImGui.SliderInt("hours"u8, ref _favourHours, 1, FavourBoard.MaxHours, "%d"u8, ImGuiSliderFlags.AlwaysClamp);
        if (ImGui.Button("Ask aloud"u8, in _autoSize))
        {
            Issue(new PostFavourCommand(_avatar, Narsil.Ui.ImGuiText.Value(_favourText), _favourPrice, _favourHours));
            _favourText.SetValue("".AsSpan());
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip("Only those who hear you will know. Say it where the people you need are.");
    }
}
