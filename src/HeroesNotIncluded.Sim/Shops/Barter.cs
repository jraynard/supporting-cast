using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Shops;

/// <summary>One side of a <see cref="BarterDeal"/>: what they've put on the table, and whether they'll shake on it.</summary>
public sealed class BarterSide(Entity who)
{
    public Entity Who { get; } = who;

    /// <summary>Stacks from their hands or pack, and how many of each.</summary>
    public List<(Entity Item, int Count)> Items { get; } = [];

    public int Silver { get; set; }
    public bool Agreed { get; set; }

    public bool IsEmpty => Items.Count == 0 && Silver == 0;
}

/// <summary>
/// Two players trading face to face. The one who asked waits until the other says yes
/// (<see cref="Open"/>); then each puts things on the table, and it changes hands only once both
/// agree to the same table, all at once (<see cref="BarterSystem"/>).
/// </summary>
public sealed class BarterDeal(int id, Entity asker, Entity asked, long since)
{
    public int Id { get; } = id;
    public BarterSide Asker { get; } = new(asker);
    public BarterSide Asked { get; } = new(asked);

    /// <summary>The other one said yes: the table's open.</summary>
    public bool Open { get; set; }

    /// <summary>When it was asked for (an unanswered ask lapses).</summary>
    public long Since { get; } = since;

    public bool Involves(Entity e) => Asker.Who == e || Asked.Who == e;
    public BarterSide SideOf(Entity e) => Asker.Who == e ? Asker : Asked;
    public BarterSide OtherSide(Entity e) => Asker.Who == e ? Asked : Asker;
}

/// <summary>
/// Trading between players: give and take at once, so neither has to trust the other to pay up.
/// (Handing something over with nothing back is <see cref="GiveCommand"/>.) Only between characters
/// players are steering now, standing within reach; a deal is called off when either walks away,
/// falls, drops out, or opens another. Anything changed on the table undoes both agreements, so
/// nobody shakes on a table that's moved under them. What's on the other side is read through
/// <see cref="PerceptionView.Barter"/>.
/// </summary>
public sealed class BarterSystem : ISimSystem, ISaveState
{
    public string Name => "Barter";
    public int Interval => 15;

    /// <summary>How long an ask stands unanswered.</summary>
    public const long AskTicks = 30 * SimClock.TicksPerSecond;

    /// <summary>A step or two further than handing reach before it's called off: shuffling about mustn't end it.</summary>
    public const int Slack = 2;

    private readonly List<BarterDeal> _deals = [];
    private int _nextId = 1;

    public IReadOnlyList<BarterDeal> Deals => _deals;

    /// <summary>The deal someone is in (asking, asked or trading), if any.</summary>
    public BarterDeal? Of(Entity who) => _deals.Find(d => d.Involves(who));

    /// <summary>Why <paramref name="from"/> can't trade with <paramref name="with"/> right now, or null if they can.</summary>
    public static string? Problem(Simulation sim, Entity from, Entity with)
    {
        var w = sim.World;
        if (from == with || !InventoryCommand.Ready(sim, from) || !w.IsAlive(with) || !w.Has<Avatar>(with) || !w.Has<Creature>(with))
            return "not someone to trade with";
        var c = w.Get<Creature>(with);
        if (c.Dead || c.Downed)
            return $"{c.Name} is in no state to trade";
        if (w.TryGet<Mind>(with, out var mind) && mind.Asleep)
            return $"{c.Name} is asleep";
        return Handing.InReach(sim, from, with) ? null : "too far away";
    }

    /// <summary>
    /// Ask to trade, or say yes to someone who asked you. Asking anew leaves any other deal you were in.
    /// </summary>
    public void Ask(Simulation sim, Entity from, Entity with)
    {
        if (Problem(sim, from, with) != null)
            return;
        if (Of(from) is { } mine)
        {
            if (mine.Involves(with))
            {
                if (!mine.Open && mine.Asked.Who == from)
                    mine.Open = true; // yes
                return;
            }
            End(sim, mine, from, $"{NameOf(sim, from)} has called off the trade.");
        }
        // Trading with someone else, or asking or asked: busy.
        if (Of(with) != null)
            return;
        _deals.Add(new BarterDeal(_nextId++, from, with, sim.Clock.Tick));
    }

    /// <summary>Walk away from the table (or turn down, or take back, an ask).</summary>
    public void Leave(Simulation sim, Entity who)
    {
        if (Of(who) is { } deal)
            End(sim, deal, who, deal.Open ? $"{NameOf(sim, who)} has called off the trade."
                : deal.Asker.Who == who ? $"{NameOf(sim, who)} doesn't want to trade after all." : $"{NameOf(sim, who)} would rather not trade.");
    }

    /// <summary>A deal ends without a trade; the other one hears why (the one who ended it knows).</summary>
    private void End(Simulation sim, BarterDeal d, Entity by, string toOther)
    {
        _deals.Remove(d);
        var other = d.Asker.Who == by ? d.Asked.Who : d.Asker.Who;
        if (sim.World.IsAlive(other))
            sim.Messages.Tell(other, MessageKind.Info, toOther);
    }

    private static string NameOf(Simulation sim, Entity e) =>
        sim.World.TryGet<Creature>(e, out var c) ? c.Name : "Someone";

    /// <summary>Put so many of a stack on the table (0 takes it off). Only what's loose in your hands or pack, and your own.</summary>
    public void Offer(Simulation sim, Entity who, Entity item, int count)
    {
        if (Of(who) is not { Open: true } deal || !Offerable(sim, who, item))
            return;
        var side = deal.SideOf(who);
        var at = side.Items.FindIndex(o => o.Item == item);
        count = Math.Clamp(count, 0, sim.World.Get<Stack>(item).Count);
        var was = at >= 0 ? side.Items[at].Count : 0;
        if (count == was)
            return;
        if (count == 0)
            side.Items.RemoveAt(at);
        else if (at >= 0)
            side.Items[at] = (item, count);
        else if (side.Items.Count < MaxItems)
            side.Items.Add((item, count));
        else
            return;
        Unsettle(deal);
    }

    /// <summary>Most different stacks one side can put on the table.</summary>
    public const int MaxItems = 12;

    /// <summary>Put silver on the table (as much as you have, at most).</summary>
    public void OfferSilver(Simulation sim, Entity who, int amount)
    {
        if (Of(who) is not { Open: true } deal)
            return;
        var side = deal.SideOf(who);
        amount = Math.Clamp(amount, 0, Coin.Count(sim, who));
        if (amount == side.Silver)
            return;
        side.Silver = amount;
        Unsettle(deal);
    }

    /// <summary>Shake on the table as it is (or take that back). Once both have, it changes hands.</summary>
    public void Agree(Simulation sim, Entity who, bool yes)
    {
        if (Of(who) is not { Open: true } deal)
            return;
        Prune(sim, deal);
        if (yes && deal.Asker.IsEmpty && deal.Asked.IsEmpty)
            return; // nothing to shake on
        deal.SideOf(who).Agreed = yes;
        if (deal.Asker.Agreed && deal.Asked.Agreed)
            Settle(sim, deal);
    }

    /// <summary>Can this go on the table: a stack loose in your hands or pack, not something you're minding for a customer?</summary>
    public static bool Offerable(Simulation sim, Entity who, Entity item)
    {
        var w = sim.World;
        if (!InventoryCommand.IsItem(sim, item) || w.Has<Entrusted>(item) || Coin.IsSilver(sim, item))
            return false;
        return w.Get<Carrying>(who).Item == item || Inventory.InOwnPack(sim, who, item);
    }

    public void Update(Simulation sim)
    {
        for (var i = _deals.Count - 1; i >= 0; i--)
        {
            var d = _deals[i];
            if (Lapsed(sim, d))
            {
                _deals.RemoveAt(i);
                foreach (var (a, b) in new[] { (d.Asker.Who, d.Asked.Who), (d.Asked.Who, d.Asker.Who) })
                    if (sim.World.IsAlive(a) && sim.World.Has<Avatar>(a))
                        sim.Messages.Tell(a, MessageKind.Info, d.Open ? $"The trade with {NameOf(sim, b)} is off." : a == d.Asker.Who ? $"{NameOf(sim, b)} didn't answer." : $"{NameOf(sim, b)}'s ask to trade has lapsed.");
            }
            else if (d.Open)
                Prune(sim, d);
        }
    }

    private static bool Lapsed(Simulation sim, BarterDeal d)
    {
        if (!d.Open && sim.Clock.Tick - d.Since >= AskTicks)
            return true;
        foreach (var (a, b) in new[] { (d.Asker.Who, d.Asked.Who), (d.Asked.Who, d.Asker.Who) })
        {
            if (!InventoryCommand.Ready(sim, a) || (sim.World.TryGet<Mind>(a, out var mind) && mind.Asleep))
                return true;
            var w = sim.World;
            if (!w.IsAlive(b) || !w.TryGet<Position>(a, out var pa) || !w.TryGet<Position>(b, out var pb)
                || pa.Cell.Z != pb.Cell.Z || Map.Distance(pa.Cell, pb.Cell) > Handing.Reach + Slack)
                return true;
        }
        return false;
    }

    /// <summary>Take off the table what's no longer there to give (moved, used, dropped); any change undoes the agreements.</summary>
    private static void Prune(Simulation sim, BarterDeal d)
    {
        foreach (var side in new[] { d.Asker, d.Asked })
        {
            var changed = false;
            for (var i = side.Items.Count - 1; i >= 0; i--)
            {
                var (item, count) = side.Items[i];
                if (!Offerable(sim, side.Who, item))
                {
                    side.Items.RemoveAt(i);
                    changed = true;
                }
                else if (sim.World.Get<Stack>(item).Count < count)
                {
                    side.Items[i] = (item, sim.World.Get<Stack>(item).Count);
                    changed = true;
                }
            }
            var purse = Coin.Count(sim, side.Who);
            if (side.Silver > purse)
            {
                side.Silver = purse;
                changed = true;
            }
            if (changed)
                Unsettle(d);
        }
    }

    private static void Unsettle(BarterDeal d)
    {
        d.Asker.Agreed = false;
        d.Asked.Agreed = false;
    }

    /// <summary>Both agreed: everything on the table goes across at once.</summary>
    private void Settle(Simulation sim, BarterDeal d)
    {
        _deals.Remove(d);
        // Take everything off both people first, so nothing given can be given on in the same swap.
        var toAsked = TakeAll(sim, d.Asker);
        var toAsker = TakeAll(sim, d.Asked);
        foreach (var item in toAsked)
            Inventory.Stow(sim, d.Asked.Who, item);
        foreach (var item in toAsker)
            Inventory.Stow(sim, d.Asker.Who, item);
        Coin.Pay(sim, d.Asker.Who, d.Asked.Who, d.Asker.Silver);
        Coin.Pay(sim, d.Asked.Who, d.Asker.Who, d.Asked.Silver);
        sim.Messages.Tell(d.Asker.Who, MessageKind.Good, $"You traded with {NameOf(sim, d.Asked.Who)}.");
        sim.Messages.Tell(d.Asked.Who, MessageKind.Good, $"You traded with {NameOf(sim, d.Asker.Who)}.");
    }

    private static List<Entity> TakeAll(Simulation sim, BarterSide side)
    {
        var taken = new List<Entity>();
        foreach (var (item, count) in side.Items)
            taken.Add(Inventory.TakeFromPerson(sim, side.Who, item, count));
        return taken;
    }

    public void Save(SaveWriter w, Simulation sim)
    {
        w.Int(_nextId);
        w.Int(_deals.Count);
        foreach (var d in _deals)
        {
            w.Int(d.Id);
            w.Long(d.Since);
            w.Bool(d.Open);
            WriteSide(w, d.Asker);
            WriteSide(w, d.Asked);
        }
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _deals.Clear();
        _nextId = r.Int();
        var n = r.Count(Round.RoundSystem.MaxSeats * 2);
        for (var i = 0; i < n; i++)
        {
            var id = r.Int();
            var since = r.Long();
            var open = r.Bool();
            var asker = ReadSide(r);
            var asked = ReadSide(r);
            var d = new BarterDeal(id, asker.Who, asked.Who, since) { Open = open };
            Copy(asker, d.Asker);
            Copy(asked, d.Asked);
            _deals.Add(d);
        }
    }

    private static void WriteSide(SaveWriter w, BarterSide s)
    {
        w.Entity(s.Who);
        w.Int(s.Silver);
        w.Bool(s.Agreed);
        w.Int(s.Items.Count);
        foreach (var (item, count) in s.Items)
        {
            w.Entity(item);
            w.Int(count);
        }
    }

    private static BarterSide ReadSide(SaveReader r)
    {
        var s = new BarterSide(r.Entity()) { Silver = r.Int(), Agreed = r.Bool() };
        var n = r.Count(MaxItems);
        for (var i = 0; i < n; i++)
            s.Items.Add((r.Entity(), r.Int()));
        return s;
    }

    private static void Copy(BarterSide from, BarterSide to)
    {
        to.Silver = from.Silver;
        to.Agreed = from.Agreed;
        to.Items.AddRange(from.Items);
    }
}

/// <summary>Ask someone in reach to trade, or say yes to their ask (<see cref="BarterSystem.Ask"/>).</summary>
public sealed record BarterCommand(Entity Avatar, Entity With) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;
    public void Apply(Simulation sim) => sim.Barter.Ask(sim, Avatar, With);
}

/// <summary>Put so many of a stack on your side of the table (0 takes it back).</summary>
public sealed record BarterOfferCommand(Entity Avatar, Entity Item, int Count) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar))
            sim.Barter.Offer(sim, Avatar, Item, Count);
    }
}

/// <summary>Put silver on your side of the table.</summary>
public sealed record BarterSilverCommand(Entity Avatar, int Amount) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar))
            sim.Barter.OfferSilver(sim, Avatar, Amount);
    }
}

/// <summary>Shake on the table as it stands, or take that back.</summary>
public sealed record BarterAgreeCommand(Entity Avatar, bool Yes) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar))
            sim.Barter.Agree(sim, Avatar, Yes);
    }
}

/// <summary>Walk away from the table, turn down an ask, or take yours back.</summary>
public sealed record BarterLeaveCommand(Entity Avatar) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;
    public void Apply(Simulation sim) => sim.Barter.Leave(sim, Avatar);
}
