using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Hospitality;

/// <summary>A party back in triumph, owed a feast once they're in the inn (<see cref="FeastSystem"/>). On the one who'll call for it.</summary>
public struct FeastDue
{
    public int Party;
}

/// <summary>
/// Feasts (docs/DESIGN.md, "Cook"): a party back from a quest cleared with nobody lost calls for one
/// as they come into the inn. Each of them orders a dish at the hatch on the spot, a different one
/// off the menu where there's a choice, and they pay over the odds. Every dish served before the
/// clock runs out and the whole room's merrier for it, the party sings the town's praises and its
/// Renown rises; the table left bare, the party grumbles.
/// </summary>
public sealed class FeastSystem : ISimSystem, ISaveState
{
    public string Name => "Feasts";
    public int Interval => SimClock.TicksPerHour / 12;

    /// <summary>How long the kitchen has to put every dish up: at 2 hours (80 real seconds) none of a playtest's three were.</summary>
    public const float Hours = 6f;

    /// <summary>What each of them pays over the price, for the occasion.</summary>
    public const int Over = 2;

    /// <summary>Renown for a feast laid on, and how much happier the party is for it (or the less, left wanting).</summary>
    public const float Renown = 3f, Cheer = 2f, Letdown = 1f;

    public sealed class Feast
    {
        public int Party;
        public Entity Inn;
        public List<int> Orders = [];
        public long Deadline;
    }

    private readonly List<Feast> _feasts = [];

    public IReadOnlyList<Feast> Active => _feasts;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        // Those owed one call for it as they come in.
        if (w.Count<FeastDue>() > 0)
            foreach (var e in w.Query<FeastDue>())
            {
                if (!w.TryGet<Creature>(e, out var c) || c.Dead || !w.Has<Adventurer>(e))
                {
                    w.Remove<FeastDue>(e);
                    continue;
                }
                var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
                if (inn.IsNull || Buildings.At(sim, w.Get<Position>(e).Cell) != inn || !SpeechSystem.CanAct(sim, e))
                    continue;
                var party = w.Get<FeastDue>(e).Party;
                w.Remove<FeastDue>(e);
                Call(sim, e, party, inn);
            }
        for (var i = _feasts.Count - 1; i >= 0; i--)
        {
            var f = _feasts[i];
            var orders = sim.Shops.All.Where(o => f.Orders.Contains(o.Id)).ToList();
            if (orders.Count > 0 && orders.All(o => o.Status == OrderStatus.Done))
            {
                Laid(sim, f);
                _feasts.RemoveAt(i);
            }
            else if (sim.Clock.Tick >= f.Deadline || orders.Count == 0 || orders.All(o => !o.IsOpen))
            {
                Bare(sim, f);
                _feasts.RemoveAt(i);
            }
        }
    }

    /// <summary>The party calls for a feast: a dish each, ordered at the hatch now.</summary>
    public Feast? Call(Simulation sim, Entity caller, int party, Entity inn)
    {
        var w = sim.World;
        var meal = sim.Defs.Service(ServiceKind.Meal);
        if (ShopPrices.Posted(sim, inn, ServiceKind.Meal) is not { Offered: true } plain)
            return null;
        var members = sim.Creatures().Where(e => w.TryGet<Adventurer>(e, out var a) && a.Party == party && !w.Get<Creature>(e).Dead)
            .OrderBy(e => e.Index).ToList();
        if (members.Count == 0)
            return null;
        var menu = Menu.Offered(sim, inn).ToList();
        var feast = new Feast { Party = party, Inn = inn, Deadline = sim.Clock.Tick + SimClock.HoursToTicks(Hours) };
        for (var i = 0; i < members.Count; i++)
        {
            var dish = menu.Count > 0 && i % (menu.Count + 1) < menu.Count ? menu[i % (menu.Count + 1)] : (PostedDish?)null;
            var price = (dish?.Price ?? plain.Price) + Over;
            var text = dish is { } d ? $"{Capital(d.Dish.Label)} for me, and keep it coming!" : "A bowl of whatever's best, and plenty of it!";
            var order = sim.Shops.Post(sim, members[i], meal, text, price, feast.Deadline, where: inn);
            order.Dish = dish?.Dish;
            feast.Orders.Add(order.Id);
        }
        sim.Speech.Say(sim, caller, "A feast! We're back, all of us, and the job's done. Food for the whole table, on us!", Volume.Shout);
        sim.Messages.Add(MessageKind.Good, $"{w.Get<Creature>(caller).Name}'s party calls for a feast: {members.Count} dishes at the hatch, quick as the kitchen can.",
            w.Get<Position>(caller).Cell);
        _feasts.Add(feast);
        return feast;
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Every dish served: the room's merry, the party's pleased, the town's talked of.</summary>
    private static void Laid(Simulation sim, Feast f)
    {
        var w = sim.World;
        foreach (var e in sim.Creatures().ToList())
        {
            if (w.Get<Creature>(e).Dead)
                continue;
            // The party, wherever they've got to by now, and everyone in the inn.
            var theirs = w.TryGet<Adventurer>(e, out var a) && a.Party == f.Party;
            if (!theirs && Buildings.At(sim, w.Get<Position>(e).Cell) != f.Inn)
                continue;
            MoodSystem.Remember(sim, e, sim.Defs.Thoughts.Feasted);
            if (theirs)
                w.Get<Adventurer>(e).Satisfaction += Cheer;
        }
        if (sim.Round.Live)
            sim.Reckoning.Add(sim, Renown, 0f);
        var at = w.TryGet<PresetBuilding>(f.Inn, out var b) ? b.Anchor : default;
        sim.Messages.Add(MessageKind.Good, "The feast's laid: every dish up and eaten, and the whole room merry. They'll talk of it on the road.", at);
    }

    /// <summary>The clock ran out with dishes owed: the party grumbles.</summary>
    private static void Bare(Simulation sim, Feast f)
    {
        var w = sim.World;
        foreach (var e in sim.Creatures().ToList())
            if (w.TryGet<Adventurer>(e, out var a) && a.Party == f.Party)
                w.Get<Adventurer>(e).Satisfaction -= Letdown;
        var at = w.TryGet<PresetBuilding>(f.Inn, out var b) ? b.Anchor : default;
        sim.Messages.Add(MessageKind.Warning, "The feast fell flat: the kitchen couldn't keep up.", at);
    }

    public void Save(SaveWriter w, Simulation sim)
    {
        w.Int(_feasts.Count);
        foreach (var f in _feasts)
        {
            w.Int(f.Party);
            w.Entity(f.Inn);
            w.Long(f.Deadline);
            w.Int(f.Orders.Count);
            foreach (var id in f.Orders)
                w.Int(id);
        }
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _feasts.Clear();
        for (var n = r.Count(100); n > 0; n--)
        {
            var f = new Feast { Party = r.Int(), Inn = r.Entity(), Deadline = r.Long() };
            for (var k = r.Count(100); k > 0; k--)
                f.Orders.Add(r.Int());
            _feasts.Add(f);
        }
    }
}
