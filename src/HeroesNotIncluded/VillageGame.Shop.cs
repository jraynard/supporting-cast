using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// Shops and coin: the orders your character knows of, and handing things over.
// Everything about other people comes through View (Perception); the sim decides what's allowed.
public sealed partial class VillageGame
{
    private bool _showClosedOrders;

    /// <summary>
    /// How much of a wait is left, as a bar's fill, colour and a few words: "0.2h left" means nothing
    /// to someone who doesn't know a game hour from a real one. Shared by orders and favours.
    /// </summary>
    internal static (float Share, float4 Color, string Words) TimeLeft(long posted, long deadline, long now)
    {
        var left = Math.Max(0, deadline - now) / (float)SimClock.TicksPerHour;
        var share = left / (Math.Max(1, deadline - posted) / (float)SimClock.TicksPerHour);
        return left < 0.5f ? (share, Theme.Bad, "almost out of time")
            : share < 0.4f ? (share, Theme.Warn, "time's getting short")
            : (share, Theme.Good, "plenty of time");
    }

    /// <summary>Prices being dragged on your sign, not yet sent (the sign itself changes when the command lands).</summary>
    private readonly Dictionary<Sim.Defs.ServiceKind, int> _priceDrafts = [];

    /// <summary>The name being typed for your shop, before it's sent; null when not renaming.</summary>
    private ImInputString? _shopNameInput;

    /// <summary>The highest price the sign's sliders reach (Ctrl+click to type one up to the sign's limit).</summary>
    private const int PriceSliderMax = 60;

    /// <summary>The orders you've heard asked or taken on, then the favours players ask of each other.</summary>
    private void DrawShop()
    {
        if (!_sim.Round.Live || !HasAvatar || !_showShop)
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
            if (open && e.Kind == Sim.Defs.ServiceKind.MixedDrink && View.WantedDrink(e.Id) is { } named)
                TextC(Theme.Accent, $"Asked for a {named.Label}");
            ImGui.EndGroup();
            ImGui.PushTextWrapPos(0f);
            TextC(Theme.Dim, $"\u201C{e.Text}\u201D");
            ImGui.PopTextWrapPos();
            if (open)
            {
                var (share, color, words) = TimeLeft(e.PostedTick, e.Deadline, now);
                Gui.Bar(e.AssigneeName.Length > 0 ? (e.MineToDo ? "You're on it" : $"{e.AssigneeName} is on it") : "Nobody's taken it",
                    share, color, words, 170f);
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

        DrawWaysAsked();
        DrawFavours();
        Panel.End();
    }

    /// <summary>Strangers who've asked the way within your hearing: point them to it, and they'll thank you for it.</summary>
    private void DrawWaysAsked()
    {
        var asks = View.WaysAsked().ToList();
        if (asks.Count == 0)
            return;
        TextC(Theme.Accent, "Asking the way");
        foreach (var a in asks)
        {
            ImGui.PushID(a.Id + 100000);
            ImGui.AlignTextToFramePadding();
            ImGui.Text($"{a.AskerName}: where's the {a.To}?");
            ImGui.SameLine(0, 8);
            if (ImGui.SmallButton("Point the way"u8))
                Issue(new Sim.Hospitality.DirectCommand(_avatar, a.Id));
            ImGui.PopID();
        }
        Gui.Rule();
    }

    /// <summary>What your shop's called, over the door: click Rename to paint a new name (empty puts the old one back).</summary>
    private void DrawShopName(Entity shop, Sim.Defs.BuildingDef workplace, bool mayRename)
    {
        var name = ShopName.Of(_sim, shop);
        if (_shopNameInput == null || !mayRename)
        {
            ImGui.AlignTextToFramePadding();
            TextC(Theme.Text, name != null ? $"Your sign at {name}" : $"Your sign at the {workplace.Label}");
            if (!mayRename)
                return;
            ImGui.SameLine(0, 8);
            if (ImGui.SmallButton("Rename"u8))
            {
                _shopNameInput = new ImInputString(ShopName.MaxLength + 8, ""u8);
                _shopNameInput.SetValue((name ?? "").AsSpan());
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip("Paint a new name over the door. Everyone in town will know it by that.");
            return;
        }
        ImGui.SetNextItemWidth(Gui.S(200));
        var done = ImGui.InputTextWithHint("##shopname"u8, "What's it called?"u8, _shopNameInput, ImGuiInputTextFlags.EnterReturnsTrue, null, IntPtr.Zero);
        OnScreenKeyboard.AfterTextField();
        ImGui.SameLine(0, 6);
        done |= ImGui.SmallButton("Paint it"u8);
        ImGui.SameLine(0, 6);
        if (ImGui.SmallButton("Cancel"u8))
            _shopNameInput = null;
        else if (done)
        {
            Issue(new SetShopNameCommand(_avatar, shop, Narsil.Ui.ImGuiText.Value(_shopNameInput)));
            _shopNameInput = null;
        }
    }

    /// <summary>Your shop's price sign, if your trade keeps one: what you charge, and what you're offering.</summary>
    private void DrawSign()
    {
        if (!_sim.World.TryGet<Trade>(_avatar, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return;
        var shop = ShopSystem.BuildingOf(_sim, workplace);
        // Only your trade's lines: the cook prices the meals, the innkeeper the rest.
        var sign = shop.IsNull ? [] : View.Prices(shop).Where(p => trade.Def.Serving(p.Kind)).ToList();
        if (sign.Count == 0)
            return;
        DrawShopName(shop, workplace, trade.Def.NamesShop);
        if (trade.Def.Serving(Sim.Defs.ServiceKind.Meal))
        {
            if (View.HouseDish(shop) is { } house)
                Wrapped($"House dish: the {house.Name} ({house.Tastes}). Some ask for it by name.");
            else
                TextC(Theme.Dim, "No house dish yet. Name a fine pot at the pot.");
            // Dishes beyond the plain meal: asked for by name, at their own price, once offered.
            foreach (var d in View.Menu(shop))
            {
                ImGui.PushID(d.Dish.Name);
                var offered = d.Offered;
                if (ImGui.Checkbox("##dish"u8, ref offered))
                    Issue(new SetDishPriceCommand(_avatar, shop, d.Dish, d.Price, offered));
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip(offered ? $"Some will ask for {d.Dish.Label} by name, at this price. Untick to stop." : $"Not offering {d.Dish.Label}. Tick it once you can make it.");
                ImGui.SameLine(0, Gui.S(6));
                ImGui.BeginDisabled(!offered);
                var price = _dishPriceDrafts.TryGetValue(d.Dish.Name, out var draft) ? draft : d.Price;
                ImGui.SetNextItemWidth(Gui.S(150));
                ImGui.SliderInt("##dishprice"u8, ref price, 0, Math.Max(PriceSliderMax, d.Price), "%d silver"u8, ImGuiSliderFlags.AlwaysClamp);
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    Issue(new SetDishPriceCommand(_avatar, shop, d.Dish, price, offered));
                    _dishPriceDrafts.Remove(d.Dish.Name);
                }
                else if (ImGui.IsItemActive())
                    _dishPriceDrafts[d.Dish.Name] = price;
                else
                    _dishPriceDrafts.Remove(d.Dish.Name);
                ImGui.SameLine(0, Gui.S(8));
                TextC(offered ? Theme.Text : Theme.Dim, Capital(d.Dish.Label));
                ImGui.EndDisabled();
                ImGui.PopID();
            }
        }
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

    /// <summary>Dish prices being dragged on the sign, by dish def name.</summary>
    private readonly Dictionary<string, int> _dishPriceDrafts = [];

    /// <summary>The price being set for a piece in the inspector, and which piece it's for.</summary>
    private int _salePrice = 10;
    private Entity _salePriceFor;

    /// <summary>
    /// A piece's price, if it's for sale; and for its keeper, putting gear lying in their shop up
    /// for sale, changing its price or taking it off sale.
    /// </summary>
    private void DrawForSale(Entity item, Thing thing)
    {
        if (!InRound)
            return;
        var sale = View.Sale(item);
        if (sale is { } s)
            TextC(Theme.Warn, s.OnShow ? $"For sale: {s.Price} silver." : $"Marked for sale at {s.Price} silver, but not on show in its seller's shop.");
        if (!HasAvatar || !Goods.IsGear(thing.Def))
            return;
        var inMyShop = thing.Spawned && Sim.Systems.Buildings.At(_sim, thing.Cell) is { IsNull: false } shop && ShopPrices.Keeps(_sim, _avatar, shop);
        var mine = sale is { } own && own.Seller == _avatar;
        if (!inMyShop && !mine)
            return;
        if (_salePriceFor != item)
        {
            _salePriceFor = item;
            _salePrice = sale?.Price ?? 10;
        }
        ImGui.SetNextItemWidth(Gui.S(150));
        ImGui.SliderInt("##saleprice"u8, ref _salePrice, 0, Math.Max(PriceSliderMax * 2, _salePrice), "%d silver"u8, ImGuiSliderFlags.AlwaysClamp);
        ImGui.SameLine(0, Gui.S(6));
        if (inMyShop && ImGui.Button(sale == null ? "Put up for sale"u8 : "Change price"u8, in _autoSize))
            Issue(new SetItemPriceCommand(_avatar, item, _salePrice, true));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip("On show while it lies in your shop. Adventurers who'd be better off with it, and think it worth the price, line up to buy it.");
        if (mine)
        {
            ImGui.SameLine(0, Gui.S(6));
            if (ImGui.Button("Take off sale"u8, in _autoSize))
                Issue(new SetItemPriceCommand(_avatar, item, 0, false));
        }
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>
    /// What each waiting customer the player heard wants, for the bubbles over their heads. Only
    /// through <see cref="PerceptionView.Orders"/>: an order nobody heard shows nothing. Work for
    /// the player's trade (or taken on by them) gets the quest marker.
    /// </summary>
    private void UpdateOrderBubbles()
    {
        var bubbles = _renderer.OrderBubbles;
        bubbles.Clear();
        if (!HasAvatar || !_sim.Round.Live)
            return;
        var trade = _sim.World.TryGet<Trade>(_avatar, out var t) ? t.Def : null;
        var now = _sim.Clock.Tick;
        foreach (var o in View.Orders())
        {
            if (o.Status != OrderStatus.Open || o.Mine || o.Poster.IsNull || (o.AssigneeName.Length > 0 && !o.MineToDo))
                continue; // served, given up, someone else's to do, or the player's own ask
            var forYou = o.MineToDo || (o.AssigneeName.Length == 0 && Serves(trade, o.Kind));
            var left = TimeLeft(o.PostedTick, o.Deadline, now).Share;
            // The newest want wins if one customer has two.
            bubbles[o.Poster] = new WorldRenderer.OrderBubble(ServiceIcon(o.Kind), left, forYou);
        }
    }

    /// <summary>Is this the trade's work: served at its workplace, and one of the services it sees to there?</summary>
    private bool Serves(Sim.Defs.TradeDef? trade, Sim.Defs.ServiceKind kind) =>
        trade?.WorkplaceDef is { } shop && _sim.Defs.Service(kind).AtDef == shop && trade.Serving(kind);

    /// <summary>The service's own icon (services.json), or a speech mark for one without.</summary>
    private string ServiceIcon(Sim.Defs.ServiceKind kind) =>
        _sim.Defs.Service(kind).Icon is { Length: > 0 } icon ? icon : "ui/icon_call";

    /// <summary>Inspector buttons for someone in reach: hand over what you're holding, or pay them.</summary>
    private void DrawHandOver(Entity other)
    {
        if (!HasAvatar || other == _avatar || !_sim.Round.Live || !Handing.InReach(_sim, _avatar, other))
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
