using Brutal.ImGuiApi;
using Narsil.Ui;
using SupportingCast.Sim;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Ecs;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Shops;
using SupportingCast.Ui;

namespace SupportingCast;

// Shops and coin: the orders your character knows of, and handing things over.
// Everything about other people comes through View (Perception); the sim decides what's allowed.
public sealed partial class VillageGame
{
    private bool _showClosedOrders;

    /// <summary>Prices being dragged on your sign, not yet sent (the sign itself changes when the command lands).</summary>
    private readonly Dictionary<Sim.Defs.ServiceKind, int> _priceDrafts = [];

    /// <summary>The highest price the sign's sliders reach (Ctrl+click to type one up to the sign's limit).</summary>
    private const int PriceSliderMax = 60;

    /// <summary>The orders you've heard asked or taken on, then the favours players ask of each other.</summary>
    private void DrawShop()
    {
        if (_sim.Round.Phase != RoundPhase.Playing || !HasAvatar || !_showShop)
            return;
        BeginJournal("Shop###orders", ref _showShop);
        DrawSign();
        ImGui.Checkbox("Show closed"u8, ref _showClosedOrders);
        Gui.Rule();

        var now = _sim.Clock.Tick;
        var any = false;
        foreach (var e in View.Orders().OrderBy(e => e.Status != OrderStatus.Open).ThenBy(e => e.Deadline))
        {
            var open = e.Status == OrderStatus.Open;
            if (!open && !_showClosedOrders)
                continue;
            any = true;
            ImGui.PushID(e.Id);
            var who = e.Mine ? "You" : e.PosterName;
            // A card: the kind's icon, who wants what for how much, their words, and the time left as a bar.
            var dl = ImGui.GetWindowDrawList();
            var icon = ImGui.GetTextLineHeight() * 1.6f;
            var at = ImGui.GetCursorScreenPos();
            if (Gui.TrySprite(ServiceIcon(e.Kind), out var sprite))
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
                if (e.MineToDo && e.Kind == Sim.Defs.ServiceKind.Repair)
                    TextC(Theme.Dim, $"Mended, give it back from {e.PosterName}'s inspector, in reach.");
                if (e.MineToDo && e.Kind == Sim.Defs.ServiceKind.Unlock)
                    TextC(Theme.Dim, $"From your pack: feel the box over for traps, pick it, then give it back from {e.PosterName}'s inspector.");
                if (!e.Mine && e.AssigneeName.Length == 0)
                {
                    if (ImGui.Button("I'll do it"u8, in _autoSize))
                        Issue(new TakeOrderCommand(_avatar, e.Id));
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                        Tip("Say aloud that you'll see to it. They pay whoever gets it done.");
                }
            }
            else
                TextC(Theme.Dim, e.Status switch
                {
                    OrderStatus.Done => e.AssigneeName.Length > 0 ? $"Done by {e.AssigneeName}." : "Done.",
                    OrderStatus.Expired => "They gave up waiting.",
                    _ => "Called off.",
                });
            Gui.Rule();
            ImGui.PopID();
        }
        if (!any)
            TextC(Theme.Dim, "You haven't heard anyone ask for anything.");

        DrawFavours();
        Panel.End();
    }

    /// <summary>Your shop's price sign, if your trade keeps one: what you charge, and what you're offering.</summary>
    private void DrawSign()
    {
        if (!_sim.World.TryGet<Trade>(_avatar, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return;
        var shop = ShopSystem.BuildingOf(_sim, workplace);
        var sign = shop.IsNull ? [] : View.Prices(shop);
        if (sign.Count == 0)
            return;
        TextC(Theme.Text, $"Your sign at the {workplace.Label}");
        TextC(Theme.Dim, "Adventurers who think it's too dear go without.");
        foreach (var p in sign)
        {
            ImGui.PushID((int)p.Kind);
            var offered = p.Offered;
            if (ImGui.Checkbox("##offered"u8, ref offered))
                Issue(new SetPriceCommand(_avatar, shop, p.Kind, p.Price, offered));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip(offered ? $"Offering {p.Label}. Untick to stop." : $"Not offering {p.Label}.");
            ImGui.SameLine(0, Gui.S(6));
            ImGui.BeginDisabled(!offered);
            var price = _priceDrafts.TryGetValue(p.Kind, out var draft) ? draft : p.Price;
            ImGui.SetNextItemWidth(Gui.S(150));
            ImGui.SliderInt("##price"u8, ref price, 0, Math.Max(PriceSliderMax, p.Price), "%d silver"u8, ImGuiSliderFlags.AlwaysClamp);
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                Issue(new SetPriceCommand(_avatar, shop, p.Kind, price, offered));
                _priceDrafts.Remove(p.Kind);
            }
            else if (ImGui.IsItemActive())
                _priceDrafts[p.Kind] = price;
            else
                _priceDrafts.Remove(p.Kind);
            ImGui.SameLine(0, Gui.S(8));
            TextC(offered ? Theme.Text : Theme.Dim, Capital(p.Label));
            ImGui.EndDisabled();
            ImGui.PopID();
        }
        Gui.Rule();
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string ServiceIcon(Sim.Defs.ServiceKind kind) => kind switch
    {
        Sim.Defs.ServiceKind.Meal or Sim.Defs.ServiceKind.Drink => "ui/icon_eat",
        Sim.Defs.ServiceKind.Repair => "ui/icon_build",
        Sim.Defs.ServiceKind.Healing => "ui/icon_doctor",
        Sim.Defs.ServiceKind.Quest => "ui/icon_quest",
        Sim.Defs.ServiceKind.Unlock => "ui/icon_treasure",
        _ => "ui/icon_call",
    };

    /// <summary>Inspector buttons for someone in reach: hand over what you're holding, or pay them.</summary>
    private void DrawHandOver(Entity other)
    {
        if (!HasAvatar || other == _avatar || _sim.Round.Phase != RoundPhase.Playing || !Handing.InReach(_sim, _avatar, other))
            return;
        ImGui.Separator();
        var w = _sim.World;
        // What they've asked for that you heard: take it on (a mend or a box changes hands once you do).
        foreach (var o in View.Orders().Where(o => o.Poster == other && o.Status == OrderStatus.Open))
        {
            ImGui.PushID(o.Id);
            TextC(Theme.Text, $"Wants {o.KindLabel}, {o.Price} silver.");
            if (o.AssigneeName.Length == 0)
            {
                ImGui.SameLine(0, Gui.S(8));
                if (ImGui.Button("Take order"u8, in _autoSize))
                    Issue(new TakeOrderCommand(_avatar, o.Id));
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip(ShopSystem.HandsOver(o.Kind) ? "Say you'll see to it: they hand it over, and step out of the line while you work." : "Say you'll see to it. They pay when it's done.");
            }
            else
                TextC(Theme.Dim, o.MineToDo ? "  You're seeing to it." : $"  {o.AssigneeName} is seeing to it.");
            ImGui.PopID();
        }
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
