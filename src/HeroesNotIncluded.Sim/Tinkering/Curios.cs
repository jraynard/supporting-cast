using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tinkering;

/// <summary>
/// Something odd brought back from a quest. All of it is hidden: what it would really fetch
/// (<see cref="Worth"/>, next to nothing for a fake), what it looks like it would (<see cref="Face"/>),
/// and whether it's genuine at all. Its tells (marks, tooling, patina) are at places along it fixed
/// by <see cref="Seed"/>; on a fake, one of them gives it away. Only an appraisal says anything about it
/// (<see cref="Appraisal"/>, through <c>PerceptionView.Curio</c>).
/// </summary>
public struct Curio
{
    public int Worth;
    public int Face;
    public bool Genuine;
    public uint Seed;
}

/// <summary>
/// One person's reading of a curio under the loupe: the range they'd put it in, and whether they
/// think it's a fake. Known to them alone, unless they say it (<see cref="Quoted"/>, -1 until they do).
/// </summary>
public struct Appraisal
{
    public Entity By;
    public int Low, High;
    public bool Doubts;
    public int Quoted;
}

/// <summary>What a curio's owner believes it's worth, from what they've been told (or how it looks, untold).</summary>
public struct Valued
{
    public int Worth;
    public Entity ToldBy;
}

/// <summary>
/// Curios and appraising them (docs/DESIGN.md, "Tinker"). A party back from a cleared quest
/// sometimes carries a curio home (on a sound wagon), and asks at the tinker's what it's worth.
/// The tinker reads its tells under the loupe (<see cref="LoupeSession"/>), says what they think
/// it's worth, and can offer to buy it: the owner takes an offer near what they believe. Curios put
/// in the collector's crate go off with the morning carrier (<see cref="CollectorSystem"/>), and
/// the collector's silver comes back the morning after: what it was really worth, less a cut, and
/// nearly nothing for a fake. The gamble is how well the tinker read it.
/// </summary>
public static class Curios
{
    /// <summary>The chance a cleared quest sends a curio home with the party, on a sound wagon or none.</summary>
    public const float FoundChance = 0.55f;

    /// <summary>The chance a curio's genuine.</summary>
    public const float GenuineChance = 0.7f;

    /// <summary>What a fake fetches from the collector.</summary>
    public const int FakeWorth = 2;

    /// <summary>The share of a curio's worth the collector pays (the carrier and the collector take the rest).</summary>
    public const float CollectorShare = 0.8f;

    /// <summary>An owner takes an offer this near what they believe it's worth: coin now beats coin later.</summary>
    public const float TakesOffer = 0.9f;

    public static bool IsCurio(Simulation sim, Entity e) => sim.World.IsAlive(e) && sim.World.Has<Curio>(e);

    /// <summary>A new curio (unspawned) of a random kind, rolled from <paramref name="rand"/>; worth more from harder places.</summary>
    public static Entity Make(Simulation sim, SimRandom rand, float danger)
    {
        var kinds = sim.Defs.AllThings.Where(t => t.Curio != null).ToList();
        if (kinds.Count == 0)
            return Entity.Null;
        var def = kinds[rand.NextInt(kinds.Count)];
        var props = def.Curio!;
        var scale = Math.Clamp(0.7f + 0.15f * danger, 0.8f, 1.6f);
        var face = (int)MathF.Round(rand.Range(props.WorthMin, props.WorthMax + 1) * scale);
        var genuine = rand.Chance(GenuineChance);
        var item = sim.CreateItem(def, 1);
        sim.World.Add(item, new Curio { Worth = genuine ? face : FakeWorth, Face = face, Genuine = genuine, Seed = rand.NextUInt() | 1u });
        sim.World.Add(item, new Valued { Worth = face, ToldBy = Entity.Null });
        return item;
    }

    /// <summary>Where a curio's tells sit along it (0..1), from its seed: three to five, spread out.</summary>
    public static float[] Tells(uint seed)
    {
        var rand = new SimRandom(seed);
        var n = 3 + rand.NextInt(3);
        var at = new float[n];
        for (var i = 0; i < n; i++)
            at[i] = (i + rand.Range(0.25f, 0.75f)) / n * 0.84f + 0.08f;
        return at;
    }

    /// <summary>On a fake, the tell that gives it away.</summary>
    public static int Giveaway(uint seed) => (int)(seed >> 8) % Tells(seed).Length;

    /// <summary>A curio someone carries that they've not yet been told the worth of (Entity.Null if none).</summary>
    public static Entity UnvaluedOf(Simulation sim, Entity who)
    {
        var w = sim.World;
        bool Wants(Entity e) => IsCurio(sim, e) && !w.Has<Entrusted>(e) && w.TryGet<Valued>(e, out var v) && v.ToldBy.IsNull;
        var held = w.Get<Carrying>(who).Item;
        if (Wants(held))
            return held;
        return sim.PackOf(who) is { IsNull: false } pack ? sim.StoredIn(pack).FirstOrDefault(Wants, Entity.Null) : Entity.Null;
    }

    /// <summary>Can this person look this curio over now? Null if so, else why not.</summary>
    public static string? Problem(Simulation sim, Entity worker, Entity curio)
    {
        if (!IsCurio(sim, curio) || !Inventory.OnPerson(sim, worker, curio))
            return "you need it in hand or in your pack";
        return null;
    }

    /// <summary>
    /// What a look under the loupe comes to: <paramref name="read"/> of its tells studied, and
    /// whether the one that gives a fake away was among them. The more read and the better the
    /// eye, the narrower the range; spotting a fake brings it down to nothing.
    /// </summary>
    public static Appraisal Read(Simulation sim, Entity by, Entity curio, int read, bool sawGiveaway)
    {
        var c = sim.World.Get<Curio>(curio);
        var total = Tells(c.Seed).Length;
        var level = SkillRules.LevelOf(sim, by, Skill.Crafting);
        var doubts = !c.Genuine && sawGiveaway;
        var center = doubts ? c.Worth : c.Face;
        var eye = Math.Clamp(1.3f - 0.06f * level, 0.6f, 1.3f);
        var width = Math.Clamp((1f - 0.8f * read / Math.Max(1, total)) * eye * 0.6f, 0.05f, 0.9f);
        var guess = center * (1f + sim.Rand.Range(-0.3f, 0.3f) * width);
        return new Appraisal
        {
            By = by, Low = Math.Max(0, (int)MathF.Round(guess * (1f - width))), High = Math.Max(1, (int)MathF.Round(guess * (1f + width))),
            Doubts = doubts, Quoted = -1,
        };
    }

    public static void Appraised(Simulation sim, Entity by, Entity curio, Appraisal a)
    {
        var w = sim.World;
        if (w.Has<Appraisal>(curio))
            w.Get<Appraisal>(curio) = a;
        else
            w.Add(curio, a);
        var label = sim.LabelOf(curio);
        sim.Messages.Add(MessageKind.Info, a.Doubts
            ? $"Under the loupe, the {label} gives itself away: a fake. Worth {a.Low} to {a.High} silver, as a curiosity."
            : $"You'd put the {label} at {a.Low} to {a.High} silver.", sim.LocationOf(by));
    }

    /// <summary>The appraiser says what they'd put it at: aloud, so whoever's by hears it. Its owner will believe it when it's handed back.</summary>
    public static bool Quote(Simulation sim, Entity who, Entity curio, int worth)
    {
        var w = sim.World;
        if (!IsCurio(sim, curio) || !Inventory.OnPerson(sim, who, curio) || !w.TryGet<Appraisal>(curio, out var a) || a.By != who)
            return false;
        worth = Math.Clamp(worth, 0, ShopPrices.MaxPrice * 10);
        w.Get<Appraisal>(curio).Quoted = worth;
        sim.Speech.Say(sim, who, $"I'd put this {sim.LabelOf(curio)} at about {worth} silver.", Volume.Say);
        return true;
    }

    /// <summary>What the appraiser tells its owner: their quote, or the middle of their range if they've not said.</summary>
    public static int Told(Appraisal a) => a.Quoted >= 0 ? a.Quoted : (a.Low + a.High) / 2;

    /// <summary>
    /// The appraiser offers to buy a curio left with them for appraisal: its owner takes it if it's
    /// near what they believe it's worth (what they've been told, or how it looks), and the coin
    /// changes hands. The appraisal is done and paid for either way it goes, once it's theirs.
    /// </summary>
    public static bool Offer(Simulation sim, Entity buyer, Entity curio, int price)
    {
        var w = sim.World;
        if (!IsCurio(sim, curio) || !Inventory.OnPerson(sim, buyer, curio) || !w.TryGet<Entrusted>(curio, out var held)
            || !w.IsAlive(held.Owner) || !Handing.InReach(sim, buyer, held.Owner) || !SpeechSystem.CanAct(sim, held.Owner))
            return false;
        var owner = held.Owner;
        price = Math.Max(0, price);
        var label = sim.LabelOf(curio);
        sim.Speech.Say(sim, buyer, $"I'll give you {price} silver for the {label}.", Volume.Say);
        if (Coin.Count(sim, buyer) < price)
        {
            sim.Speech.Say(sim, owner, "With what? You haven't the coin.", Volume.Say);
            return false;
        }
        var believe = w.TryGet<Appraisal>(curio, out var a) && a.By == buyer ? Told(a) : w.TryGet<Valued>(curio, out var v) ? v.Worth : 0;
        if (price < believe * TakesOffer)
        {
            sim.Speech.Say(sim, owner, $"{price}? It's worth more than that.", Volume.Say);
            return false;
        }
        Coin.Pay(sim, buyer, owner, price);
        w.Remove<Entrusted>(curio);
        w.Get<Valued>(curio) = new Valued { Worth = price, ToldBy = buyer };
        sim.Speech.Say(sim, owner, $"Done. It's yours.", Volume.Say);
        if (sim.Shops.All.FirstOrDefault(e => e.IsOpen && e.Kind.Kind == ServiceKind.Appraise && e.Subject == curio) is { } order)
            sim.Shops.Complete(sim, order, buyer);
        return true;
    }
}

/// <summary>Look a curio over under the loupe (<see cref="LoupeSession"/>).</summary>
public sealed record AppraiseCommand(Entity Avatar, Entity Curio) : ISimCommand, Core.IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (Core.InventoryCommand.Ready(sim, Avatar) && Core.InventoryCommand.IsItem(sim, Curio) && !sim.World.Has<Work.WorkSession>(Avatar))
            LoupeSession.TryStart(sim, Avatar, Curio);
    }
}

/// <summary>Say what you'd put a curio you've appraised at (<see cref="Curios.Quote"/>).</summary>
public sealed record QuoteCurioCommand(Entity Avatar, Entity Curio, int Worth) : ISimCommand, Core.IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (Core.InventoryCommand.Ready(sim, Avatar) && Core.InventoryCommand.IsItem(sim, Curio))
            Curios.Quote(sim, Avatar, Curio, Worth);
    }
}

/// <summary>Offer to buy a curio left with you for appraisal (<see cref="Curios.Offer"/>).</summary>
public sealed record OfferForCurioCommand(Entity Avatar, Entity Curio, int Price) : ISimCommand, Core.IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (Core.InventoryCommand.Ready(sim, Avatar) && Core.InventoryCommand.IsItem(sim, Curio))
            Curios.Offer(sim, Avatar, Curio, Price);
    }
}

/// <summary>
/// The collector's carrier: every morning at <see cref="CollectHour"/> takes the curios lying in
/// collector's crates away, and brings the silver for the last lot (into the crate it came from).
/// What a curio fetches is what it was really worth, less the collector's cut: the tinker's
/// appraisal doesn't come into it. Nothing happens outside a round.
/// </summary>
public sealed class CollectorSystem : ISimSystem, ISaveState
{
    public const string CrateDef = "CollectorsCrate";
    public const int CollectHour = 6;

    public string Name => "Collector";
    public int Interval => SimClock.TicksPerHour / 4;

    private sealed record Sale(Entity Crate, int Silver, int Count);

    private readonly List<Sale> _owed = [];
    private int _lastDay = -1;

    public void Update(Simulation sim)
    {
        if (sim.Round.Phase != Round.RoundPhase.Playing || sim.Clock.HourOfDay < CollectHour || sim.Clock.Day == _lastDay)
            return;
        _lastDay = sim.Clock.Day;
        var w = sim.World;
        // Yesterday's lot is paid for first: the carrier brings the silver when they come for today's.
        foreach (var sale in _owed)
        {
            if (!w.IsAlive(sale.Crate))
                continue;
            if (sale.Silver > 0)
                sim.Store(sale.Crate, sim.CreateItem(sim.Defs.Thing(Coin.Silver), sale.Silver));
            sim.Messages.Add(MessageKind.Good, $"The carrier brings {sale.Silver} silver from the collector for {sale.Count} {(sale.Count == 1 ? "curio" : "curios")}.",
                w.Get<Thing>(sale.Crate).Cell);
        }
        _owed.Clear();
        foreach (var crate in w.Query<Thing>().Where(e => w.Get<Thing>(e).Def.Name == CrateDef && !w.Has<Construction>(e)).OrderBy(e => e.Index))
        {
            var curios = sim.StoredIn(crate).Where(e => Curios.IsCurio(sim, e)).ToList();
            if (curios.Count == 0)
                continue;
            var silver = 0;
            foreach (var c in curios)
            {
                silver += (int)MathF.Round(w.Get<Curio>(c).Worth * Curios.CollectorShare);
                sim.DestroyThing(c);
            }
            _owed.Add(new Sale(crate, silver, curios.Count));
            sim.Messages.Add(MessageKind.Info, $"The carrier takes {curios.Count} {(curios.Count == 1 ? "curio" : "curios")} off to the collector. Silver tomorrow.",
                w.Get<Thing>(crate).Cell);
        }
    }

    public void Save(SaveWriter w, Simulation sim)
    {
        w.Int(_lastDay);
        w.Int(_owed.Count);
        foreach (var s in _owed)
        {
            w.Entity(s.Crate);
            w.Int(s.Silver);
            w.Int(s.Count);
        }
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _lastDay = r.Int();
        _owed.Clear();
        for (var n = r.Count(1000); n > 0; n--)
            _owed.Add(new Sale(r.Entity(), Math.Max(0, r.Int()), Math.Max(0, r.Int())));
    }
}
