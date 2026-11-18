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
        if (!_sim.Round.Live || !HasAvatar)
            return;
        // Seen while open; one taken or gone while shut lowers the mark, so the next to come raises it again.
        var untaken = UntakenOrders();
        _ordersSeen = _showShop ? untaken : Math.Min(_ordersSeen, untaken);
        if (!_showShop)
            return;
        BeginJournal("Shop###orders", ref _showShop, side: Anchor.TopLeft);
        DrawSign();
        var now = _sim.Clock.Tick;
        var any = false;
        var closed = 0;
        foreach (var e in View.Orders().OrderBy(e => e.Status != OrderStatus.Open).ThenBy(e => e.Deadline))
        {
            var open = e.Status == OrderStatus.Open;
            if (!open)
                closed++;
            if (!open && !_showClosedOrders)
                continue;
            any = true;
            ImGui.PushID(e.Id);
            DrawOrderCard(e, open, now);
            ImGui.PopID();
        }
        if (!any)
            TextC(Theme.Dim, closed > 0 ? "Nobody's waiting on anything." : "You haven't heard anyone ask for anything.");
        ClosedToggle(closed, "orders");

        DrawWaysAsked();
        DrawFavours();
        Panel.End();
    }

    /// <summary>
    /// One order as a card: who wants what and for how much on one line, the time left (and who's
    /// on it) as a bar under it with the button that's yours to press. Their words, the drink they
    /// named and any hint sit in the tooltip on the first line. A closed one is a single dim line.
    /// </summary>
    private void DrawOrderCard(KnownOrder e, bool open, long now)
    {
        var who = e.Mine ? "You" : e.PosterName;
        var title = e.Kind == Sim.Defs.ServiceKind.Goods && e.Status == OrderStatus.Done && e.KindLabel.StartsWith("the ")
            ? $"{who} bought {e.KindLabel}" : $"{who} want{(e.Mine ? "" : "s")} {e.KindLabel}";
        var tip = $"\u201C{e.Text}\u201D";
        if (open && e.Kind == Sim.Defs.ServiceKind.Goods && !e.Mine)
            tip += Controls.Keys("\nMake one, then hand it to them (~F~ beside them, holding it), or put it on your wares shelf: they'll come and buy it.");
        if (open && e.Kind == Sim.Defs.ServiceKind.MixedDrink && View.WantedDrink(e.Id) is { } named)
        {
            title += $": {named.Label}";
            tip += $"\nAsked for a {named.Label}.";
        }
        if (open && e.MineToDo && e.Kind == Sim.Defs.ServiceKind.Unlock)
            tip += "\nFrom your pack: feel the box over for traps and pick it.";
        if (!open)
            tip += "\n" + e.Status switch
            {
                OrderStatus.Done => e.AssigneeName.Length > 0 ? $"Done by {e.AssigneeName}." : "Done.",
                OrderStatus.Expired => "They gave up waiting.",
                OrderStatus.Refused => "Turned away: you were out.",
                _ => "Called off.",
            };
        CardHead(OrderIcon(e), open, open ? (e.Mine ? Theme.Good : Theme.Text) : Theme.Dim, title, e.Price, tip);
        if (open)
        {
            var (share, color, words) = TimeLeft(e.PostedTick, e.Deadline, now);
            var take = !e.Mine && e.AssigneeName.Length == 0 && View.MayTake(e.Id);
            var errand = e.MineToDo ? View.Awaiting().Cast<KnownErrand?>().FirstOrDefault(a => a!.Value.Order == e.Id) : null;
            var carry = e.MineToDo && e.Kind is Sim.Defs.ServiceKind.Drink or Sim.Defs.ServiceKind.MixedDrink;
            var refuse = View.MayTurnAway(e.Id);
            var icons = (errand != null || carry ? 1 : 0) + (refuse ? 1 : 0);
            CardBar(e.AssigneeName.Length > 0 ? (e.MineToDo ? "You're on it" : $"{e.AssigneeName} is on it") : "Nobody's taken it",
                share, color, words, (take ? TakeButtonWidth : 0f) + icons * (CardIconSize + Gui.S(8)));
            if (take)
            {
                TakeButton($"##take{e.Id}", Controls.Keys("Say aloud that you'll see to it. They pay whoever gets it done. (Or ~F~ beside them.)"),
                    () => Issue(new TakeOrderCommand(_avatar, e.Id)));
                GuideGlow(TutorialStep == "take");
            }
            if (carry)
            {
                // What they ordered, to take to them: only with a drink poured that fills it.
                var poured = !SeeToCustomerJob.DrinkFor(_sim, _avatar, e.Kind).IsNull;
                var wanted = e.Kind == Sim.Defs.ServiceKind.MixedDrink ? View.WantedDrink(e.Id) : null;
                CardIcon($"##carry{e.Id}", wanted?.Icon is { Length: > 0 } icon ? icon : ServiceIcon(e.Kind), poured,
                    poured ? $"Take it to {e.PosterName}: walks you over and serves it. Moving calls it off." : "Pour it first.",
                    () => Issue(new SeeToCustomerCommand(_avatar, e.Id)));
            }
            else if (errand is { } x)
                DrawErrandButton(x);
            if (refuse)
                CardIcon($"##out{e.Id}", "ui/icon_cancel", true, $"Sorry, we're out.\nTell {e.PosterName} you can't do it. They take it better than being left to wait, and won't ask again for a while.",
                    () => Issue(new TurnAwayCommand(_avatar, e.Id)));
        }
        CardEnd();
    }

    /// <summary>The square of an icon button at the end of a card's time bar.</summary>
    private static float CardIconSize => Gui.S(26);

    /// <summary>
    /// An icon button after a card's time bar, on the same line. Greyed out (its tooltip saying
    /// why) when it can't be pressed.
    /// </summary>
    private void CardIcon(string id, string sprite, bool enabled, string tip, Action press)
    {
        ImGui.SameLine(0, Gui.S(8));
        ImGui.BeginDisabled(!enabled);
        if (Gui.IconButton(id, sprite, CardIconSize, false) && enabled)
            press();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip(tip);
    }

    /// <summary>The green check that takes on an order or a favour ("I'll do it", in its tip).</summary>
    private void TakeButton(string id, string why, Action press) => CardIcon(id, "ui/icon_good", true, $"I'll do it.\n{why}", press);

    /// <summary>Room the take button needs at the end of a card's time bar.</summary>
    private static float TakeButtonWidth => CardIconSize + Gui.S(8);

    /// <summary>
    /// Under the orders (and the favours), how many are over with, and a link to show or hide them:
    /// nothing at all while none are.
    /// </summary>
    private void ClosedToggle(int closed, string what)
    {
        if (closed == 0)
            return;
        ImGui.PushStyleColor(ImGuiCol.Text, in Theme.Dim);
        var label = _showClosedOrders ? $"Hide the {closed} done with##closed-{what}" : $"{closed} done with: show##closed-{what}";
        if (ImGui.Selectable(label, false, ImGuiSelectableFlags.None, default))
            _showClosedOrders = !_showClosedOrders;
        ImGui.PopStyleColor(1);
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
            // The name's on the header above: here only the brush to paint a new one.
            if (!mayRename)
                return;
            if (ImGui.SmallButton("Rename the shop"u8))
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
        if (!_sim.World.TryGet<Trade>(_avatar, out var trade) || !trade.Def.HasWorkplace)
            return;
        var shop = ShopSystem.WorkplaceOf(_sim, trade.Def);
        if (shop.IsNull)
            return;
        var workplace = _sim.World.Get<PresetBuilding>(shop).Def;
        // Only your trade's lines: the cook prices the meals, the innkeeper the rest.
        var sign = shop.IsNull ? [] : View.Prices(shop).Where(p => trade.Def.Serving(p.Kind)).ToList();
        if (sign.Count == 0)
            return;
        // Folded away once set: prices are set now and then, orders watched all day. The smith's
        // tutorial chapter on prices opens it.
        if (TutorialStep == "prices")
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        var name = ShopName.Of(_sim, shop) ?? $"the {workplace.Label}";
        var onOffer = sign.Count(p => p.Offered);
        var open = ImGui.CollapsingHeader($"Your sign at {name}###sign", ImGuiTreeNodeFlags.None);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip($"{onOffer} of {sign.Count} on offer. What you charge: adventurers who think it's too dear go without.");
        if (!open)
            return;
        DrawShopName(shop, workplace, trade.Def.NamesShop);
        if (trade.Def.Serves.Contains(Sim.Defs.ServiceKind.Meal)) // named, not "serves anything": the cook's, not the tinker's
        {
            if (View.HouseDish(shop) is { } house)
                Wrapped($"House dish: the {house.Name} ({house.Tastes}). Some ask for it by name.");
            else
                TextC(Theme.Dim, "No house dish yet. Name a fine pot at the pot.");
            TextC(Theme.Dim, ShopSystem.FareWord(_sim.Shops.Fare));
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
        foreach (var p in sign)
        {
            ImGui.PushID((int)p.Kind);
            // The service's icon, as over a customer's head when they ask for it.
            var line = ImGui.GetFrameHeight();
            if (Gui.TrySprite(ServiceIcon(p.Kind), out var mark))
                DrawSprite(ImGui.GetWindowDrawList(), mark, ImGui.GetCursorScreenPos(), new float2(line, line), p.Offered ? Gui.White : Theme.U8(Theme.Dim));
            ImGui.Dummy(new float2(line, line));
            ImGui.SameLine(0, Gui.S(4));
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

    /// <summary>Prices being dragged for pieces on a wares shelf, by item.</summary>
    private readonly Dictionary<Entity, int> _salePrices = [];

    /// <summary>
    /// A piece's price, if it's for sale. Gear is sold from a shop's wares shelf (<see cref="Goods"/>),
    /// so for its keeper the price is set there (<see cref="DrawShelfPrices"/>); a piece of theirs
    /// marked for sale anywhere else can be taken off sale, and gear of theirs lying about is pointed at the shelf.
    /// </summary>
    private void DrawForSale(Entity item, Thing thing)
    {
        if (!InRound)
            return;
        var sale = View.Sale(item);
        if (sale is { } s)
            WrappedC(Theme.Warn, s.OnShow ? $"For sale: {s.Price} silver." : $"Marked for sale at {s.Price} silver, but not on show on its seller's wares shelf.");
        if (!HasAvatar || !Goods.IsGear(thing.Def))
            return;
        if (sale is { } own && own.Seller == _avatar)
        {
            if (own.OnShow)
                DrawPriceRow(item, own.Price);
            else if (ImGui.Button("Take off sale"u8, in _autoSize))
                Issue(new SetItemPriceCommand(_avatar, item, 0, false));
        }
        else if (thing.Spawned && !Goods.MyDisplay(_sim, _avatar).IsNull)
            TextC(Dim, $"To sell it, put it on your {_sim.World.Get<Thing>(Goods.MyDisplay(_sim, _avatar)).Label}.");
    }

    /// <summary>A price slider for a piece of yours on sale, with the button to set it and to take it off sale.</summary>
    private void DrawPriceRow(Entity item, int price)
    {
        ImGui.PushID(item.Index);
        var draft = _salePrices.TryGetValue(item, out var d) ? d : price;
        ImGui.SetNextItemWidth(Gui.S(140));
        if (ImGui.SliderInt("##saleprice"u8, ref draft, 0, Math.Max(PriceSliderMax * 2, draft), "%d silver"u8, ImGuiSliderFlags.AlwaysClamp))
            _salePrices[item] = draft;
        ImGui.SameLine(0, Gui.S(6));
        ImGui.BeginDisabled(draft == price);
        if (ImGui.SmallButton("Set price"u8))
        {
            Issue(new SetItemPriceCommand(_avatar, item, draft, true));
            _salePrices.Remove(item);
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip("Adventurers who'd be better off with it, and think it worth the price, line up to buy it.");
        ImGui.SameLine(0, Gui.S(6));
        if (ImGui.SmallButton("Off sale"u8))
        {
            Issue(new SetItemPriceCommand(_avatar, item, 0, false));
            _salePrices.Remove(item);
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip("Keep it on the shelf, but not for sale. Putting a price on it again puts it back up.");
        ImGui.PopID();
    }

    /// <summary>
    /// Under a wares shelf's tiles: each piece and its price. Its seller sets them here (what they put on
    /// it went up at a fair price at once); anyone else just reads them, and buys from the keeper.
    /// </summary>
    private void DrawShelfPrices(List<Entity> items)
    {
        var w = _sim.World;
        var mine = Goods.CanSellAt(_sim, _avatar, _openStorage);
        ImGui.Separator();
        if (items.Count == 0)
        {
            WrappedC(Dim, mine ? "Put your wares here to sell them: drag them in from your pack, or press F at the shelf with one in hand. They go up for sale at a fair price; change it here."
                : "Nothing for sale.");
            return;
        }
        foreach (var item in items)
        {
            if (!w.IsAlive(item))
                continue;
            ImGui.Text(Capital(w.Get<Thing>(item).Label));
            ImGui.SameLine(Gui.S(150));
            var sale = View.Sale(item);
            if (mine && sale is { Seller: var seller } && seller == _avatar)
                DrawPriceRow(item, sale.Value.Price);
            else if (sale is { } s)
                TextC(Theme.Warn, $"{s.Price} silver");
            else if (mine)
            {
                ImGui.PushID(item.Index);
                if (ImGui.SmallButton("Put up for sale"u8))
                    Issue(new SetItemPriceCommand(_avatar, item, _salePrices.GetValueOrDefault(item, 10), true));
                ImGui.PopID();
            }
            else
                TextC(Dim, "not for sale");
        }
        if (_salePrices.Count > 32)
            foreach (var gone in _salePrices.Keys.Where(e => !w.IsAlive(e)).ToList())
                _salePrices.Remove(gone);
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
            bubbles[o.Poster] = new WorldRenderer.OrderBubble(OrderIcon(o), left, forYou);
        }
    }

    /// <summary>Is this the trade's work: served at its workplace, and one of the services it sees to there?</summary>
    private bool Serves(Sim.Defs.TradeDef? trade, Sim.Defs.ServiceKind kind) =>
        trade is { HasWorkplace: true } && _sim.Defs.Service(kind).AtRole == trade.Role && trade.Serving(kind);

    /// <summary>An order's picture: the piece it's about, if it names one, else the service's own icon.</summary>
    private string OrderIcon(KnownOrder o) => o.Icon.Length > 0 && Gui.TrySprite(o.Icon, out _) ? o.Icon : ServiceIcon(o.Kind);

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
            if (o.AssigneeName.Length == 0 && View.MayTake(o.Id))
            {
                ImGui.SameLine(0, Gui.S(8));
                if (ImGui.Button("Take order"u8, in _autoSize))
                    Issue(new TakeOrderCommand(_avatar, o.Id));
                GuideGlow(TutorialStep == "take");
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
        // A piece they asked for by name, anywhere on you: hand it over for what they said they'd pay.
        foreach (var item in OnMe())
        {
            if (ShopSystem.AskedFor(_sim, other, item) is not { } asked || w.Has<Entrusted>(item))
                continue;
            ImGui.PushID(item.Index + 200000);
            if (ImGui.Button($"Hand over the {_sim.LabelOf(item)} ({asked.Price} silver)", in _autoSize))
                Issue(new GiveCommand(_avatar, other, item, 1));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip("It's what they asked for: they'll pay what they said.");
            ImGui.PopID();
        }
        var held = w.Get<Carrying>(_avatar).Item;
        if (!held.IsNull && w.Has<Stack>(held) && !w.Has<Entrusted>(held) && ShopSystem.AskedFor(_sim, other, held) == null)
        {
            var count = w.Get<Stack>(held).Count;
            if (ImGui.Button($"Hand over the {w.Get<Thing>(held).Label} ({count})", in _autoSize))
                Issue(new GiveCommand(_avatar, other, held, count));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip("If it's what they asked for, they'll pay what they promised.");
        }
        // Another player: trade, give and take at once (also F at them with empty hands).
        if (w.Has<Avatar>(other) && BarterSystem.Problem(_sim, _avatar, other) == null && _sim.Barter.Of(_avatar) == null && _sim.Barter.Of(other) == null)
        {
            if (ImGui.Button("Trade"u8, in _autoSize))
                Issue(new BarterCommand(_avatar, other));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip("Ask to trade: you each put things on the table, and they change hands once you both agree.");
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
