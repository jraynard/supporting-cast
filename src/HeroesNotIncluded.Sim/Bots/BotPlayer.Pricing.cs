using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Expeditions;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Bots;

public sealed partial class BotPlayer
{
    // ---------------- Pricing ----------------

    /// <summary>Game hours between changes to the same price.</summary>
    public const float RepriceHours = 1;

    /// <summary>The most it will ask, as a multiple of the default price.</summary>
    public const int MaxMarkup = 3;

    /// <summary>People in line it can see before it reckons business is good enough to charge more.</summary>
    public const int BusyLine = 3;

    /// <summary>
    /// A simple market, played from its side of the counter: hearing an adventurer call a price
    /// too dear, it drops that one; seeing a line of <see cref="BusyLine"/> or more, it puts up the
    /// price of what they've asked for. Its sign starts at the defaults. Only what it heard and saw.
    /// </summary>
    private void Price(Simulation sim, Entity me, PerceptionView view, long now)
    {
        if (MyShop(sim, me) is not { IsNull: false } shop || view.Prices(shop) is not { Count: > 0 } sign)
        {
            _pricedUpTo = view.Heard().Select(l => l.Id).DefaultIfEmpty(_pricedUpTo).Max();
            return;
        }
        var changed = new HashSet<ServiceKind>();
        void Set(PostedPrice p, int price)
        {
            var max = sim.Defs.Service(p.Kind).DefaultPrice * MaxMarkup;
            price = Math.Clamp(price, 1, Math.Max(1, max));
            if (price == p.Price || changed.Contains(p.Kind)
                || (_repriced.TryGetValue(p.Kind, out var at) && now - at < SimClock.HoursToTicks(RepriceHours)))
                return;
            Send(new SetPriceCommand(me, shop, p.Kind, price, true));
            _repriced[p.Kind] = now;
            changed.Add(p.Kind);
            Doing = $"pricing {p.Label} at {price}";
        }
        foreach (var line in view.Heard())
        {
            if (line.Id <= _pricedUpTo)
                continue;
            _pricedUpTo = line.Id;
            if (line.Speaker is not { } who || who == me || line.Clarity != Clarity.Clear || !sim.World.Has<Adventurer>(who))
                continue;
            foreach (var p in sign)
                if (p.Offered && TooDear(sim.Defs.Service(p.Kind), p.Price, line.Text))
                    Set(p, p.Price - 1);
        }
        var cells = ShopSystem.LineCells(sim, shop);
        var waiting = view.Creatures().Where(e => e != me && sim.World.Has<Adventurer>(e) && cells.Contains(sim.World.Get<Position>(e).Cell)).ToHashSet();
        if (waiting.Count < BusyLine)
            return;
        foreach (var kind in view.Orders().Where(o => o.Status == OrderStatus.Open && waiting.Contains(o.Poster)).Select(o => o.Kind).Distinct())
            if (sign.FirstOrDefault(p => p.Kind == kind) is { Offered: true } p)
                Set(p, p.Price + 1);
    }

    /// <summary>Is this what an adventurer says when a price is more than they'll pay (the def's lines, filled in)?</summary>
    private static bool TooDear(ServiceDef def, int price, string text)
    {
        foreach (var template in def.TooDearLines.Append($"{{0}} silver for {def.Label}? I'll do without."))
        {
            var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(template).Replace(@"\{0}", price.ToString()).Replace(@"\{1}", ".+") + "$";
            if (System.Text.RegularExpressions.Regex.IsMatch(text, pattern))
                return true;
        }
        return false;
    }
}
