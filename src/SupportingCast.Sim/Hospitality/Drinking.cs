using SupportingCast.Sim.Components;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Shops;
using SupportingCast.Sim.Speech;
using SupportingCast.Sim.Systems;
using SupportingCast.Sim.Work;

namespace SupportingCast.Sim.Hospitality;

/// <summary>How far the drink has gone to someone's head, 0 sober to 1 and past it under the table. Wears off over hours.</summary>
public struct Tipsy
{
    public float Level;
}

/// <summary>
/// Drink taking hold, and what's asked for at the bar. Each drink a customer is served goes to
/// their head by its strength (<see cref="DrinkEffectProps"/>, or a mix's <see cref="DrinkMix"/>)
/// and does what else it does: warms, rouses, numbs. Merry, they talk: rumours come unbidden,
/// and truer, so a busy bar is the keeper's best intel. Loud, their voices carry. Past it, they
/// spill what they're given, and at last they're asleep where they sit. Now and then a customer
/// wants a named drink rather than an ale, and is pleased (or not) by what they get.
/// </summary>
public sealed class DrinkingSystem : ISimSystem, ISaveState
{
    /// <summary>Tipsiness lost each game hour.</summary>
    public const float SoberPerHour = 0.12f;

    /// <summary>Merry: rumours come freely and truer. Loud: their voice carries further. Clumsy: they spill. Out: they're asleep.</summary>
    public const float MerryAt = 0.3f, LoudAt = 0.5f, ClumsyAt = 0.65f, OutAt = 1f;

    /// <summary>How much further a loud drinker's speech carries.</summary>
    public const float LoudReach = 1.5f;

    /// <summary>The chance a customer, wanting a drink, wants a named one (a <see cref="ServiceKind.MixedDrink"/>) rather than an ale.</summary>
    public const float NamedDrinkChance = 0.35f;

    /// <summary>Satisfaction for the drink they asked for, or lost for something else.</summary>
    public const float RightDrinkMood = 0.5f, WrongDrinkMood = 0.25f;

    private readonly SimRandom _rand = new(0xD7A4Bu);

    /// <summary>The named drink an open drink order is for, by order id.</summary>
    private readonly Dictionary<int, string> _wanted = [];

    private long _lastHour;

    public string Name => "Drinking";
    public int Interval => SimClock.TicksPerHour / 12;

    public static float LevelOf(Simulation sim, Entity who) => sim.World.TryGet<Tipsy>(who, out var t) ? t.Level : 0f;

    /// <summary>The named drink an order is for, if it's for one.</summary>
    public string? Wanted(int order) => _wanted.GetValueOrDefault(order);

    /// <summary>How someone looks with drink in them, for whoever's looking; null sober.</summary>
    public static string? Looks(float level) => level switch
    {
        >= OutAt => "out cold",
        >= ClumsyAt => "drunk",
        >= LoudAt => "loud with drink",
        >= MerryAt => "merry",
        _ => null,
    };

    /// <summary>A mixed drink's been ordered: which one they have in mind, said aloud.</summary>
    public void Ordered(Simulation sim, Order order)
    {
        var named = sim.Defs.AllThings.Where(d => d.Mix is { House: false }).ToList();
        if (named.Count == 0)
            return;
        var pick = named[_rand.NextInt(named.Count)];
        _wanted[order.Id] = pick.Name;
        if (sim.Speech.Say(sim, order.Poster, $"Make it a {pick.Label}, if you know how.", Volume.Say) is { } said)
            foreach (var h in said.Listeners)
                if (h.Clarity == Clarity.Clear)
                    ShopSystem.Learn(order, h.Listener);
    }

    /// <summary>A customer drinks what they were served: it goes to their head, does what it does, and they say what they think.</summary>
    public void Drank(Simulation sim, Entity who, Entity drink, Order? order)
    {
        var w = sim.World;
        var def = w.Get<Thing>(drink).Def;
        var mix = w.TryGet<DrinkMix>(drink, out var m) ? m : FromDef(def);
        if (!w.Has<Tipsy>(who))
            w.Add(who, new Tipsy());
        ref var tipsy = ref w.Get<Tipsy>(who);
        var was = tipsy.Level;
        tipsy.Level = Math.Min(1.5f, tipsy.Level + mix.Strength * (1f + mix.Loosening));
        var a = w.Has<Adventurer>(who);
        // What else it does.
        if (mix.Warming > 0.1f && sim.TemperatureAt(w.Get<Position>(who).Cell) < 12f)
        {
            if (a) w.Get<Adventurer>(who).Satisfaction += 0.5f * mix.Warming;
            sim.Speech.Say(sim, who, "That takes the chill off.", Volume.Say);
        }
        if (mix.Rousing > 0.1f && w.TryGet<Needs>(who, out var needs))
        {
            var rest = sim.Defs.Need("Rest");
            needs.Values[rest.Index] = Math.Min(1f, needs.Values[rest.Index] + 0.3f * mix.Rousing);
        }
        if (mix.Numbing > 0.1f && w.TryGet<Health>(who, out var h) && h.Injuries.Count > 0)
        {
            if (a) w.Get<Adventurer>(who).Satisfaction += 0.6f * mix.Numbing;
            sim.Speech.Say(sim, who, "Dulls the ache, that does.", Volume.Say);
        }
        // Was it what they asked for?
        if (order != null && _wanted.Remove(order.Id, out var wanted) && a)
        {
            var right = def.Name == wanted;
            w.Get<Adventurer>(who).Satisfaction += right ? RightDrinkMood : -WrongDrinkMood;
            var label = sim.Defs.Thing(wanted).Label;
            sim.Speech.Say(sim, who, right ? $"A proper {label}. Good." : $"I asked for a {label}.", Volume.Say);
        }
        // Too far gone to hold a tankard straight.
        if (was >= ClumsyAt && _rand.NextFloat() < 0.35f)
        {
            sim.Mess.Spill(sim, MessKind.SpilledAle, w.Get<Position>(who).Cell, 0.6f);
            sim.Speech.Say(sim, who, "Whoops.", Volume.Say);
        }
    }

    /// <summary>A plain drink's properties, from its def.</summary>
    public static DrinkMix FromDef(ThingDef def) => def.DrinkEffect is { } e
        ? new DrinkMix { Strength = e.Strength, Warming = e.Warming, Rousing = e.Rousing, Numbing = e.Numbing, Loosening = e.Loosening, Adds = "" }
        : new DrinkMix { Adds = "" };

    public void Update(Simulation sim)
    {
        var w = sim.World;
        // Orders gone: what they wanted goes with them.
        if (_wanted.Count > 0)
            foreach (var id in _wanted.Keys.Where(id => sim.Shops.Find(id) is not { IsOpen: true }).ToList())
                _wanted.Remove(id);
        if (w.Count<Tipsy>() == 0)
            return;
        var hourly = sim.Clock.Tick - _lastHour >= SimClock.TicksPerHour;
        if (hourly)
            _lastHour = sim.Clock.Tick;
        foreach (var e in w.Query<Tipsy>())
        {
            ref var t = ref w.Get<Tipsy>(e);
            t.Level -= SoberPerHour / 12f;
            if (t.Level <= 0f)
            {
                w.Remove<Tipsy>(e);
                continue;
            }
            if (sim.Round.Phase != RoundPhase.Playing || !SpeechSystem.CanAct(sim, e) || w.Has<Leaving>(e))
                continue;
            // Under the table: they sleep it off where they are.
            if (t.Level >= OutAt && w.TryGet<Needs>(e, out var needs))
            {
                needs.Values[sim.Defs.Need("Rest").Index] = 0f;
                continue;
            }
            // Merry, they talk: a rumour now and then, and truer the more they've had.
            if (hourly && t.Level >= MerryAt && w.Has<Adventurer>(e) && _rand.NextFloat() < 0.6f * t.Level)
                sim.Expeditions.Gossip(sim, e, spread: Math.Max(0.05f, 0.25f - 0.2f * t.Level));
        }
    }

    public void Save(SaveWriter w, Simulation sim)
    {
        var (a, b, c, d) = _rand.State;
        w.UInt(a); w.UInt(b); w.UInt(c); w.UInt(d);
        w.Long(_lastHour);
        w.Int(_wanted.Count);
        foreach (var (id, def) in _wanted.OrderBy(kv => kv.Key))
        {
            w.Int(id);
            w.String(def);
        }
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _rand.State = (r.UInt(), r.UInt(), r.UInt(), r.UInt());
        _lastHour = r.Long();
        _wanted.Clear();
        for (var n = r.Count(1 << 16); n > 0; n--)
            _wanted[r.Int()] = r.String();
    }
}
