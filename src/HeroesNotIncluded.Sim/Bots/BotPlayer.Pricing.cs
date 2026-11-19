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

    /// <summary>The most it will ask, as a multiple of the default price: a greedy bot asks up to this, a modest one two thirds of it.</summary>
    public const int MaxMarkup = 3;

    /// <summary>The most this bot will ask, as a multiple of the default: its greed.</summary>
    private float Markup => MaxMarkup * (0.67f + 0.33f * Temper.Greed);

    /// <summary>People in line it can see before it reckons business is good enough to charge more.</summary>
    public const int BusyLine = 3;

    /// <summary>
    /// A simple market, played from its side of the counter: hearing an adventurer call a price
    /// too dear, it drops that one; knowing of <see cref="BusyLine"/> or more waiting on what it sells, it puts up the
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
            var max = (int)MathF.Round(sim.Defs.Service(p.Kind).DefaultPrice * Markup);
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
                {
                    // A greedy one holds out against some of the grumbling, and says so.
                    if (_slipRand.Chance(Temper.Greed * 0.6f))
                        _toAnswer ??= ("too_dear_reply", line.Who);
                    else
                        Set(p, p.Price - 1);
                }
        }
        // Busy: as many waiting on what its sign sells as it knows of, standing in the line or about the room (a long
        // line spills off the line's cells: counted by bodies on them alone, seven waiting read as two).
        var waiting = view.Orders().Where(o => o.Status == OrderStatus.Open && sign.Any(p => p.Kind == o.Kind && p.Offered)).ToList();
        if (waiting.Count < BusyLine)
            return;
        foreach (var kind in waiting.Select(o => o.Kind).Distinct())
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
