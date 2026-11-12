using Brutal.ImGuiApi;
using Narsil.Ui;
using SupportingCast.Sim;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Ecs;
using SupportingCast.Sim.Errands;
using SupportingCast.Sim.Round;
using SupportingCast.Ui;

namespace SupportingCast;

// Errands and coin: the errands your character knows of, your purse, and handing things over.
// Everything about other people comes through View (Perception); the sim decides what's allowed.
public sealed partial class VillageGame
{
    private readonly ImInputString _requestText = new(ErrandSystem.MaxTextLength + 8, ""u8);
    private int _requestPrice = 5;
    private int _requestHours = 4;
    private bool _showClosedErrands;

    /// <summary>The errands you've heard asked, taken on or asked yourself, with your purse.</summary>
    private void DrawErrands()
    {
        if (_sim.Round.Phase != RoundPhase.Playing || !HasAvatar || !_showErrands)
            return;
        BeginJournal("Errands###errands", ref _showErrands);
        ImGui.Checkbox("Show closed"u8, ref _showClosedErrands);
        Gui.Rule();

        var now = _sim.Clock.Tick;
        var any = false;
        foreach (var e in View.Errands().OrderBy(e => e.Status != ErrandStatus.Open).ThenBy(e => e.Deadline))
        {
            var open = e.Status == ErrandStatus.Open;
            if (!open && !_showClosedErrands)
                continue;
            any = true;
            ImGui.PushID(e.Id);
            var who = e.Mine ? "You" : e.PosterName;
            // A card: the kind's icon, who wants what for how much, their words, and the time left as a bar.
            var dl = ImGui.GetWindowDrawList();
            var icon = ImGui.GetTextLineHeight() * 1.6f;
            var at = ImGui.GetCursorScreenPos();
            if (Gui.TrySprite(ErrandIcon(e.Kind), out var sprite))
                Gui.Sprite(dl, sprite, at, new float2(icon, icon), open ? Gui.White : Gui.U8(Theme.Dim));
            ImGui.Dummy(new float2(icon, icon));
            ImGui.SameLine(0, Gui.S(8));
            ImGui.BeginGroup();
            TextC(open ? (e.Mine ? Theme.Good : Theme.Text) : Theme.Dim, $"{who} want{(e.Mine ? "" : "s")} {e.KindLabel}");
            TextC(open ? Theme.Warn : Theme.Dim, $"{e.Price} silver");
            ImGui.EndGroup();
            ImGui.PushTextWrapPos(0f);
            TextC(Theme.Dim, $"\u201C{e.Text}\u201D");
            ImGui.PopTextWrapPos();
            if (open)
            {
                var left = Math.Max(0, e.Deadline - now) / (float)SimClock.TicksPerHour;
                var whole = Math.Max(1, e.Deadline - e.PostedTick) / (float)SimClock.TicksPerHour;
                Gui.Bar(e.AssigneeName.Length > 0 ? (e.MineToDo ? "You're on it" : $"{e.AssigneeName} is on it") : "Nobody's taken it",
                    left / whole, left < 0.5f ? Theme.Bad : left / whole < 0.4f ? Theme.Warn : Theme.Good, $"{left:F1}h left", 170f);
                if (e.MineToDo && e.Kind == Sim.Defs.ErrandKind.Repair)
                    TextC(Theme.Dim, $"Mended, give it back from {e.PosterName}'s inspector, in reach.");
                if (e.MineToDo && e.Kind == Sim.Defs.ErrandKind.Unlock)
                    TextC(Theme.Dim, $"From your pack: feel the box over for traps, pick it, then give it back from {e.PosterName}'s inspector.");
                if (!e.Mine && e.AssigneeName.Length == 0)
                {
                    if (ImGui.Button("I'll do it"u8, in _autoSize))
                        Issue(new AcceptErrandCommand(_avatar, e.Id));
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                        Tip("Say aloud that you'll see to it. They pay whoever gets it done.");
                }
                if (e.Mine && e.Kind == Sim.Defs.ErrandKind.Request)
                {
                    if (e.AssigneeName.Length > 0)
                    {
                        if (ImGui.Button("Done: pay them"u8, in _autoSize))
                            Issue(new CompleteErrandCommand(_avatar, e.Id));
                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                            Tip($"Hand {e.AssigneeName} the {e.Price} silver. They have to be within reach.");
                        ImGui.SameLine(0, 6);
                    }
                    if (ImGui.Button("Call it off"u8, in _autoSize))
                        Issue(new CancelErrandCommand(_avatar, e.Id));
                }
            }
            else
                TextC(Theme.Dim, e.Status switch
                {
                    ErrandStatus.Done => e.AssigneeName.Length > 0 ? $"Done by {e.AssigneeName}." : "Done.",
                    ErrandStatus.Expired => "They gave up waiting.",
                    _ => "Called off.",
                });
            Gui.Rule();
            ImGui.PopID();
        }
        if (!any)
            TextC(Theme.Dim, "You haven't heard anyone ask for anything.");

        if (ImGui.CollapsingHeader("Ask for something"u8, ImGuiTreeNodeFlags.None))
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##request"u8, "What you want done"u8, _requestText, ImGuiInputTextFlags.None, null, IntPtr.Zero);
            ImGui.SetNextItemWidth(120);
            ImGui.SliderInt("silver"u8, ref _requestPrice, 0, 50, "%d"u8, ImGuiSliderFlags.AlwaysClamp);
            ImGui.SameLine(0, 12);
            ImGui.SetNextItemWidth(120);
            ImGui.SliderInt("hours"u8, ref _requestHours, 1, 24, "%d"u8, ImGuiSliderFlags.AlwaysClamp);
            if (ImGui.Button("Ask aloud"u8, in _autoSize))
            {
                Issue(new PostErrandCommand(_avatar, Narsil.Ui.ImGuiText.Value(_requestText), _requestPrice, _requestHours));
                _requestText.SetValue("".AsSpan());
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip("Only those who hear you will know. Say it where the people you need are.");
        }
        Panel.End();
    }

    private static string ErrandIcon(Sim.Defs.ErrandKind kind) => kind switch
    {
        Sim.Defs.ErrandKind.Meal or Sim.Defs.ErrandKind.Drink => "ui/icon_eat",
        Sim.Defs.ErrandKind.Repair => "ui/icon_build",
        Sim.Defs.ErrandKind.Healing => "ui/icon_doctor",
        Sim.Defs.ErrandKind.Quest => "ui/icon_quest",
        Sim.Defs.ErrandKind.Unlock => "ui/icon_treasure",
        _ => "ui/icon_call",
    };

    /// <summary>Inspector buttons for someone in reach: hand over what you're holding, or pay them.</summary>
    private void DrawHandOver(Entity other)
    {
        if (!HasAvatar || other == _avatar || _sim.Round.Phase != RoundPhase.Playing || !Handing.InReach(_sim, _avatar, other))
            return;
        ImGui.Separator();
        var w = _sim.World;
        // What they handed you to see to, wherever you've put it: give it back.
        foreach (var item in OnMe())
        {
            if (!w.TryGet<Entrusted>(item, out var trust) || trust.Owner != other)
                continue;
            ImGui.PushID(item.Index);
            if (ImGui.Button($"Give back their {_sim.LabelOf(item)}", in _autoSize))
                Issue(new GiveCommand(_avatar, other, item, 1));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip(View.Strongbox(item) is { } box
                    ? (box.Locked ? "It's still locked: they won't pay for that." : "Open: they'll take out what's inside and pay.")
                    : View.Condition(item) is { } c ? $"It's {c}. They'll look it over: mended, they pay; if not, they'll say what's wrong." : "They'll look it over before they pay.");
            ImGui.PopID();
        }
        var held = w.Get<Carrying>(_avatar).Item;
        if (!held.IsNull && w.Has<Stack>(held) && !w.Has<Entrusted>(held))
        {
            var count = w.Get<Stack>(held).Count;
            if (ImGui.Button($"Hand over the {w.Get<Thing>(held).Label} ({count})", in _autoSize))
                Issue(new GiveCommand(_avatar, other, held, count));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip("If it's what they asked for, they'll pay what they promised.");
        }
        var purse = Coin.Count(_sim, _avatar);
        if (purse > 0)
            foreach (var amount in new[] { 1, 5 })
            {
                if (amount > purse)
                    break;
                if (ImGui.Button($"Pay {amount} silver", in _autoSize))
                    PaySilver(other, amount);
                ImGui.SameLine(0, 6);
            }
        ImGui.NewLine();
    }

    /// <summary>What's loose on your person: in hand, then in the pack.</summary>
    private List<Entity> OnMe()
    {
        var list = new List<Entity>();
        var held = _sim.World.Get<Carrying>(_avatar).Item;
        if (!held.IsNull && _sim.World.IsAlive(held))
            list.Add(held);
        var pack = _sim.PackOf(_avatar);
        if (!pack.IsNull)
            list.AddRange(_sim.StoredIn(pack));
        return list;
    }

    /// <summary>Hand coin over from the first silver stacks found (hands, then pack).</summary>
    private void PaySilver(Entity to, int amount)
    {
        var w = _sim.World;
        var stacks = new List<Entity>();
        var held = w.Get<Carrying>(_avatar).Item;
        if (Coin.IsSilver(_sim, held))
            stacks.Add(held);
        var pack = _sim.PackOf(_avatar);
        if (!pack.IsNull)
            stacks.AddRange(_sim.StoredIn(pack).Where(i => Coin.IsSilver(_sim, i)));
        foreach (var stack in stacks)
        {
            if (amount <= 0)
                break;
            var n = Math.Min(amount, w.Get<Stack>(stack).Count);
            Issue(new GiveCommand(_avatar, to, stack, n));
            amount -= n;
        }
    }
}
