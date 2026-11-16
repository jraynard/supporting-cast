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
    // Feeling over: who it last ran its hands over, and when.
    private readonly Dictionary<Entity, long> _feltOver = [];

    private bool Heal(Simulation sim, Entity me, PerceptionView view, Entity patient, long now)
    {
        if (!Reach(sim, me, patient, now))
            return true;
        if (view.FeltWounds(patient) is not { } wounds)
            return false;
        if (wounds.Count == 0)
        {
            // Hands over them first, to find what hurts; once a while is enough.
            if (_feltOver.TryGetValue(patient, out var felt) && now - felt < SimClock.TicksPerHour)
                return false;
            _feltOver[patient] = now;
            Send(new FeelCommand(me, patient));
            return true;
        }
        Send(new LayOnHandsCommand(me, patient, wounds[0].Key));
        return true;
    }

    /// <summary>It knows the mending this wound's feel wants, and has the mana for it.</summary>
    private static bool CanWeave(Simulation sim, Entity me, Healing.FeltWound wound, Mana mana) =>
        sim.Defs.AllMendings.FirstOrDefault(m => m.Feels.ContainsValue(wound.Feels) && Weaving.Knows(sim, me, m)) is { } m && mana.Current >= m.ManaCost;

    /// <summary>The healer's own upkeep: mend what they carry, bind it if they can't, or meditate when short of mana. True if it did any.</summary>
    private bool Mend(Simulation sim, Entity me, PerceptionView view)
    {
        if (!sim.World.TryGet<Mana>(me, out var mana))
            return false;
        foreach (var wound in view.FeltWounds(me) ?? [])
        {
            // The feel of a wound tells a practised empath which mending it wants, and the book its runes.
            var mending = sim.Defs.AllMendings.FirstOrDefault(m => m.Feels.ContainsValue(wound.Feels) && Weaving.Knows(sim, me, m));
            if (wound.Absorbed && mending != null && mana.Current >= mending.ManaCost)
            {
                _weave = mending.RuneDefs.Select(r => r.Index).ToList();
                Send(new WeaveCommand(me, wound.Key));
                return true;
            }
        }
        // Short of mana with a drawn wound bleeding: bind it while the mana comes back.
        if (Bandaging.WoundsToBind(sim, me, me) > 0 && Bandaging.CantBind(sim, me, me) == null
            && (view.FeltWounds(me) ?? []).Any(f => f.Absorbed) && !(view.FeltWounds(me) ?? []).Any(f => f.Absorbed && CanWeave(sim, me, f, mana)))
        {
            Send(new BandageCommand(me, me));
            return true;
        }
        if (mana.Current < mana.Max * 0.3f && _task == 0)
        {
            // At the shrine's stone if there is one: it's calmer there.
            var stone = Nearest(sim, me, "MeditationStone");
            if (!stone.IsNull && !AvatarSystem.InReach(sim, sim.World.Get<Position>(me).Cell, stone))
                Walk(sim, me, sim.World.Get<Thing>(stone).Cell, sim.Clock.Tick);
            else
                Send(new MeditateCommand(me));
            return true;
        }
        return false;
    }

    /// <summary>The healer's further games; null for a session that isn't one.</summary>
    private (bool Press, bool Held, int Axis)? PlayShrine(WorkSession s) => null;

    /// <summary>A charm of its own it knows came out looser than this isn't sold: it's torn.</summary>
    public const float WeakCharm = 0.4f;

    /// <summary>The share of its mana it keeps for the hurt: it weaves a charm only with more than this to hand.</summary>
    public const float CharmsFrom = 0.8f;

    /// <summary>Bandages it keeps for binding: a charm takes a strip only past these.</summary>
    public const int KeepBandages = 1;

    /// <summary>
    /// The healer between patients: charms for the road. A charm it wove loose is torn (its mending
    /// lands on it, as anyone's would); good ones go on show in the shrine; and with mana to spare
    /// and linen to hand, a new one is woven at the shrine's altar, a different healing mending from
    /// what's in stock, while the stock's short. Never in the small hours. True while at it.
    /// </summary>
    private bool Shrine(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (!w.TryGet<Mana>(me, out var mana) || MyShop(sim, me) is not { IsNull: false } shop)
            return false;
        // A loose one: torn, rather than sold to someone who'd trust their life to it.
        foreach (var item in Loose(sim, me))
            if (MadeBy(sim, item, me) && view.Charm(item) is { Quality: < WeakCharm } && !w.Has<Shops.ForSale>(item))
            {
                Doing = "tearing a loose charm";
                Send(new TearCharmCommand(me, item));
                return true;
            }
        if (ShowGoods(sim, me, view, now))
            return true;
        if (!sim.Round.Live || ShopSystem.SmallHours(sim) || Stock(sim, me) >= StockWanted || mana.Current < mana.Max * CharmsFrom)
            return false;
        // Linen on it, not just in the chest beside it: it walks off to the altar.
        if (OnMe(sim, me, Bandaging.BandageDef) <= KeepBandages)
            return sim.Defs.Thing(Bandaging.BandageDef) is { } linen
                && Fetch(sim, me, view, now, new ThingCount { Def = linen.Name, Count = 2, ThingDef = linen }, "linen");
        // A healing mending in its book, not one already in stock (nobody carries two the same),
        // leaving a Calling's worth: whoever dies next mustn't wait on a charm for sale.
        var stocked = OnSale(sim, me).Concat(Loose(sim, me)).Select(view.Charm).OfType<KnownCharm>().Select(c => c.Mending).ToHashSet();
        var reserve = Math.Max(mana.Max * (CharmsFrom - 0.3f), Afterlife.Revival.CallCost(sim));
        var pick = sim.Defs.AllMendings.FirstOrDefault(m => !m.CallsBack && m.Heals.Count > 0 && m.Sears <= 0 && Weaving.Knows(sim, me, m)
            && !stocked.Contains(m) && m.CostAt(0) <= mana.Current - reserve);
        if (pick == null)
            return false;
        _weave = Weaving.Recipe(pick).Select(r => r.Index).ToList();
        Doing = $"weaving a charm of {pick.Label.ToLowerInvariant()}";
        // At the altar if the shrine has one: the heart's steadier there.
        var altar = Buildings.Parts(sim, shop).FirstOrDefault(e => w.Get<Thing>(e).Def.Building is { Weaving: > 0 }, Entity.Null);
        if (!altar.IsNull && AtStation(sim, me, altar, now, _ => Send(new WeaveCharmCommand(me))))
            return true;
        Send(new WeaveCharmCommand(me));
        return true;
    }
}
