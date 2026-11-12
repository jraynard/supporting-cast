using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Shops;
using SupportingCast.Sim.Speech;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Hospitality;

/// <summary>The shops an adventurer knows the way to, besides their party's haunt: seen in passing, or told.</summary>
public sealed class KnowsTheWay
{
    public readonly List<Entity> Shops = [];
}

/// <summary>Someone asked the way to a kind of building, and who heard them.</summary>
public sealed class WayAsked
{
    public int Id;
    public Entity Asker;
    public string AskerName = "";
    public required BuildingDef To;
    public long Tick;
    public readonly List<Entity> HeardBy = [];
}

/// <summary>An ask the viewer heard and could answer: who asked, for what, and where they are.</summary>
public readonly record struct KnownAsk(int Id, Entity Asker, string AskerName, string To, long Tick);

/// <summary>
/// Finding the way round a strange town. An adventurer knows their party's haunt (the inn) and
/// any shop they or their party have walked past within sight of; for the rest they have to ask. Wanting a smith
/// and knowing of none, they ask aloud where one is. Whoever answers (the innkeeper, anyone in
/// earshot) points the way, and everyone who hears it clearly knows it from then on. A villager
/// who isn't a player answers after a while if nobody else has; left unanswered for hours, the
/// asker gives up on the town's manners.
/// </summary>
public sealed class DirectionsSystem : ISimSystem, ISaveState
{
    /// <summary>Cells within which an adventurer sees a shop for what it is, walking past.</summary>
    public const int SightRange = 9;

    /// <summary>Game hours before they ask again for the same place.</summary>
    public const float AskAgainHours = 1.5f;

    /// <summary>Game hours an ask waits for a player before a villager who heard it answers.</summary>
    public const float VillagerAnswersAfterHours = 0.4f;

    /// <summary>Game hours an ask stands before the asker gives up on it.</summary>
    public const float GiveUpHours = 3;

    /// <summary>Satisfaction for being shown the way, and lost for being left to wander.</summary>
    public const float ShownMood = 0.25f, IgnoredMood = 0.5f;

    private readonly List<WayAsked> _asks = [];
    private int _nextId = 1;

    public string Name => "Directions";
    public int Interval => SimClock.TicksPerHour / 12;

    /// <summary>Asks still waiting on an answer, oldest first. Game code reads them through <see cref="PerceptionView.WaysAsked"/>.</summary>
    public IReadOnlyList<WayAsked> Open => _asks;

    /// <summary>Does this person know the way to that building? Townsfolk know their own town.</summary>
    public static bool Knows(Simulation sim, Entity who, Entity shop)
    {
        var w = sim.World;
        if (!w.TryGet<Adventurer>(who, out var a))
            return true;
        if (a.PartyDef?.HauntDef is { } haunt && w.TryGet<PresetBuilding>(shop, out var b) && b.Def == haunt)
            return true;
        return w.TryGet<KnowsTheWay>(who, out var k) && k.Shops.Contains(shop);
    }

    /// <summary>They know the way now, and so does the rest of their party: they'd tell each other.</summary>
    public static void Learn(Simulation sim, Entity who, Entity shop)
    {
        var w = sim.World;
        if (!w.TryGet<Adventurer>(who, out var a) || Knows(sim, who, shop))
            return;
        foreach (var e in w.Query<Adventurer>())
            if (e == who || w.Get<Adventurer>(e).Party == a.Party)
            {
                if (!w.TryGet<KnowsTheWay>(e, out var k))
                    w.Add(e, k = new KnowsTheWay());
                if (!k.Shops.Contains(shop))
                    k.Shops.Add(shop);
            }
    }

    /// <summary>Someone who knows the town already (a test's party, or one that's been before): every shop.</summary>
    public static void KnowsTheTown(Simulation sim, Entity who)
    {
        foreach (var e in sim.World.Each<PresetBuilding>())
            Learn(sim, who, e);
    }

    /// <summary>They don't know where to find one: they ask aloud, unless they did lately.</summary>
    public void Ask(Simulation sim, Entity who, BuildingDef to, string service)
    {
        var now = sim.Clock.Tick;
        if (_asks.Exists(x => x.Asker == who && x.To == to && now - x.Tick < SimClock.HoursToTicks(AskAgainHours)))
            return;
        _asks.RemoveAll(x => x.Asker == who && x.To == to);
        if (sim.Speech.Say(sim, who, $"Where would I find the {to.Label}? I'm after {service}.", Volume.Say) is not { } said)
            return;
        var ask = new WayAsked { Id = _nextId++, Asker = who, AskerName = sim.World.Get<Creature>(who).Name, To = to, Tick = now };
        foreach (var h in said.Listeners)
            if (h.Clarity == Clarity.Clear && h.Listener != who)
                ask.HeardBy.Add(h.Listener);
        _asks.Add(ask);
    }

    /// <summary>Someone points the way: the nearest such building, said aloud. Everyone who catches it clearly knows it now.</summary>
    public bool Answer(Simulation sim, Entity by, int id)
    {
        var w = sim.World;
        if (_asks.Find(x => x.Id == id) is not { } ask || !w.IsAlive(ask.Asker) || by == ask.Asker || !SpeechSystem.CanAct(sim, by))
            return false;
        var shop = Nearest(sim, ask.To, w.Get<Position>(ask.Asker).Cell);
        if (shop.IsNull)
            return false;
        var named = ShopName.Of(sim, shop) is { } name ? $" That'll be {name}." : "";
        var line = $"The {ask.To.Label}?{named} {Way(w.Get<Position>(by).Cell, w.Get<PresetBuilding>(shop).Anchor)}";
        if (sim.Speech.Say(sim, by, line, Volume.Say) is not { } said)
            return false;
        _asks.Remove(ask);
        foreach (var h in said.Listeners)
            if (h.Clarity == Clarity.Clear)
                Learn(sim, h.Listener, shop);
        if (Knows(sim, ask.Asker, shop) && w.Has<Adventurer>(ask.Asker))
        {
            w.Get<Adventurer>(ask.Asker).Satisfaction += ShownMood;
            sim.Speech.Say(sim, ask.Asker, "Much obliged.", Volume.Say);
        }
        return true;
    }

    /// <summary>The way from one cell to another, as a villager would put it.</summary>
    public static string Way(int3 from, int3 to)
    {
        int dx = to.X - from.X, dy = to.Y - from.Y;
        var far = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (far <= 4)
            return "You're standing by it.";
        // +Y is south on the map.
        var ns = Math.Abs(dy) * 2 > Math.Abs(dx) ? dy < 0 ? "north" : "south" : "";
        var ew = Math.Abs(dx) * 2 > Math.Abs(dy) ? dx < 0 ? "west" : "east" : "";
        var dir = ns + ew;
        var how = far < 15 ? "Just" : far < 35 ? "A short walk" : "A fair walk";
        return $"{how} {dir} of here.";
    }

    /// <summary>The standing building of a kind nearest a cell.</summary>
    public static Entity Nearest(Simulation sim, BuildingDef def, int3 from)
    {
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        foreach (var e in sim.World.Each<PresetBuilding>())
        {
            var b = sim.World.Get<PresetBuilding>(e);
            if (b.Def != def || b.Demolishing)
                continue;
            var d = Maps.Map.Distance(from, b.Anchor);
            if (d < bestDist)
                (best, bestDist) = (e, d);
        }
        return best;
    }

    public void Update(Simulation sim)
    {
        var w = sim.World;
        if (sim.Round.Phase != RoundPhase.Playing)
            return;
        // Walking past a shop, they see it for what it is.
        var shops = new List<(Entity Shop, int3 At)>();
        foreach (var e in w.Each<PresetBuilding>())
            if (!w.Get<PresetBuilding>(e).Demolishing)
                shops.Add((e, w.Get<PresetBuilding>(e).Anchor));
        foreach (var e in sim.Creatures())
        {
            if (!w.Has<Adventurer>(e) || w.Has<Leaving>(e))
                continue;
            var at = w.Get<Position>(e).Cell;
            foreach (var (shop, anchor) in shops)
                if (Maps.Map.Distance(at, anchor) <= SightRange)
                    Learn(sim, e, shop);
        }
        // Asks waiting: a villager who heard answers if no player has; long unanswered, they give up.
        var now = sim.Clock.Tick;
        foreach (var ask in _asks.ToList())
        {
            if (!w.IsAlive(ask.Asker) || w.Get<Creature>(ask.Asker).Dead || w.Has<Leaving>(ask.Asker))
            {
                _asks.Remove(ask);
                continue;
            }
            if (now - ask.Tick > SimClock.HoursToTicks(GiveUpHours))
            {
                _asks.Remove(ask);
                if (w.Has<Adventurer>(ask.Asker))
                    w.Get<Adventurer>(ask.Asker).Satisfaction -= IgnoredMood;
                sim.Speech.Say(sim, ask.Asker, "Nobody in this town knows their way about, it seems.", Volume.Say);
                continue;
            }
            if (now - ask.Tick < SimClock.HoursToTicks(VillagerAnswersAfterHours))
                continue;
            foreach (var by in ask.HeardBy)
                if (w.IsAlive(by) && !w.Has<Adventurer>(by) && !w.Has<PlayerCharacter>(by) && SpeechSystem.CanAct(sim, by)
                    && Answer(sim, by, ask.Id))
                    break;
        }
    }

    public void Save(SaveWriter w, Simulation sim)
    {
        w.Int(_nextId);
        w.Int(_asks.Count);
        foreach (var a in _asks)
        {
            w.Int(a.Id);
            w.Entity(a.Asker);
            w.String(a.AskerName);
            w.Def(a.To);
            w.Long(a.Tick);
            w.Int(a.HeardBy.Count);
            foreach (var h in a.HeardBy)
                w.Entity(h);
        }
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _nextId = r.Int();
        _asks.Clear();
        for (var n = r.Count(1 << 16); n > 0; n--)
        {
            var a = new WayAsked { Id = r.Int(), Asker = r.Entity(), AskerName = r.String(), To = r.Def<BuildingDef>(), Tick = r.Long() };
            for (var k = r.Count(1 << 16); k > 0; k--)
                a.HeardBy.Add(r.Entity());
            _asks.Add(a);
        }
    }
}

/// <summary>Point someone the way they asked (<see cref="DirectionsSystem.Answer"/>). Only an ask the answerer heard.</summary>
public sealed record DirectCommand(Entity Avatar, int Ask) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!sim.World.IsAlive(Avatar) || sim.Directions.Open.FirstOrDefault(a => a.Id == Ask) is not { } ask || !ask.HeardBy.Contains(Avatar))
            return;
        sim.Directions.Answer(sim, Avatar, Ask);
    }
}
