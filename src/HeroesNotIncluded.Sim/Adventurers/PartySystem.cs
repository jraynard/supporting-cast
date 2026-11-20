using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Adventurers;

/// <summary>
/// Brings adventuring parties (<see cref="PartyDef"/>) to town during a round, one at a time, and
/// sends them on their way when their stay is up. Nothing happens outside a round
/// (<see cref="RoundSystem.Live"/>), so tests, demos and the bench are untouched. None come once the
/// festival starts. Its rolls use a
/// stream of its own, so a party arriving doesn't reshuffle the rest of the sim. Lone travellers
/// (<see cref="PartyDef.Traveller"/>) come on a second schedule with a stream of its own again, of an
/// afternoon, whatever party is or isn't in town: the inn's rooms aren't all or nothing. Now and
/// then a rare company (<see cref="PartyDef.Rare"/>) comes on its day in place of the next party,
/// rolled at the round's start on a stream of its own again, so a round without one runs as before.
/// A company's size follows the round's players (<see cref="PartyDef.SizeFor"/>).
/// </summary>
public sealed class PartySystem(ulong seed) : ISimSystem, Saving.ISaveState
{
    public string Name => "Parties";
    public int Interval => SimClock.TicksPerHour / 4;

    /// <summary>Game hours from the deal to the first party.</summary>
    public const float FirstArrivalHours = 2;

    /// <summary>Game hours between one party arriving and the next being due (±30%), if the last has gone.</summary>
    public const float HoursBetweenParties = 10;

    private readonly SimRandom _rand = new(seed ^ 0xAD7E_17u);

    /// <summary>The travellers' rolls: who, when, and everything about them, apart from the parties'.</summary>
    private readonly SimRandom _travellerRand = new(seed ^ 0x7A4E_11E5u);

    /// <summary>Whether a rare company comes this round, and on what day: rolled apart from the rest.</summary>
    private readonly SimRandom _rareRand = new(seed ^ 0x0C0F_FA11u);

    private bool _rareRolled, _rareCame;
    private string? _rare;
    private int _rareDay;

    /// <summary>The rare company coming this round and the day it's due (rolled at the round's start), or null.</summary>
    public (PartyDef Company, int Day)? RareCompany(Simulation sim) =>
        _rare != null && sim.Defs.AllParties.FirstOrDefault(p => p.Name == _rare) is { } def ? (def, _rareDay) : null;

    /// <summary>Travellers come up the road between these hours, to be in by supper.</summary>
    public const float TravellerFromHour = 14, TravellerUntilHour = 19;

    /// <summary>Travellers in town at once, at most.</summary>
    public const int MaxTravellers = 2;

    /// <summary>Game hours from one traveller to the next (±30%), each then put in that day's window.</summary>
    public const float HoursBetweenTravellers = 24;

    /// <summary>The hours of the morning a traveller leaves, after their last night.</summary>
    public const float TravellerLeavesFrom = 8, TravellerLeavesBy = 10;

    /// <summary>When the next traveller is due; -1 until a round is playing.</summary>
    public long NextTravellerTick { get; private set; } = -1;

    /// <summary>When the next party is due; -1 until a round is playing.</summary>
    public long NextArrivalTick { get; private set; } = -1;

    public int PartiesSent { get; private set; }

    private int _nextId = 1;

    // The party defs sent this round, in order: the next is one sent least, so a season doesn't
    // see the same party (and the same names) twice while others haven't come.
    private readonly List<string> _sent = [];

    public void Save(Saving.SaveWriter w, Simulation sim)
    {
        var (a, b, c, d) = _rand.State;
        w.UInt(a); w.UInt(b); w.UInt(c); w.UInt(d);
        w.Long(NextArrivalTick);
        w.Int(PartiesSent);
        w.Int(_nextId);
        w.Int(_sent.Count);
        foreach (var name in _sent)
            w.String(name);
        (a, b, c, d) = _travellerRand.State;
        w.UInt(a); w.UInt(b); w.UInt(c); w.UInt(d);
        w.Long(NextTravellerTick);
        (a, b, c, d) = _rareRand.State;
        w.UInt(a); w.UInt(b); w.UInt(c); w.UInt(d);
        w.Bool(_rareRolled);
        w.NullableString(_rare);
        w.Int(_rareDay);
        w.Bool(_rareCame);
    }

    public void Load(Saving.SaveReader r, Simulation sim)
    {
        _rand.State = (r.UInt(), r.UInt(), r.UInt(), r.UInt());
        NextArrivalTick = r.Long();
        PartiesSent = r.Int();
        _nextId = r.Int();
        _sent.Clear();
        if (r.Version >= 41) // came with format 41
            for (var n = r.Count(10_000); n > 0; n--)
                _sent.Add(r.String());
        if (r.Version >= 42) // travellers came with format 42
        {
            _travellerRand.State = (r.UInt(), r.UInt(), r.UInt(), r.UInt());
            NextTravellerTick = r.Long();
        }
        if (r.Version >= 49) // the rare companies came with format 49
        {
            _rareRand.State = (r.UInt(), r.UInt(), r.UInt(), r.UInt());
            _rareRolled = r.Bool();
            _rare = r.NullableString();
            _rareDay = r.Int();
            _rareCame = r.Bool();
        }
    }

    public void Update(Simulation sim)
    {
        if (!sim.Round.Live)
            return;
        var now = sim.Clock.Tick;
        SendOff(sim, now);
        if (sim.Round.Phase == RoundPhase.Festival || sim.Round.Practice)
            return; // in the practice yard, only when sent for
        Travellers(sim, now);
        if (!_rareRolled)
            RollRare(sim);
        if (NextArrivalTick < 0)
        {
            NextArrivalTick = now + SimClock.HoursToTicks(FirstArrivalHours);
            return;
        }
        // A rare company comes on its day, whenever the town's free of the last lot.
        var rareDue = !_rareCame && RareCompany(sim) is { } rare && sim.Round.SeasonDay(sim).Day >= rare.Day;
        if (now < NextArrivalTick && !rareDue)
            return;
        if (Present(sim))
        {
            NextArrivalTick = now + SimClock.HoursToTicks(1); // one party at a time
            return;
        }
        // Nobody rides in after dark: they make camp and come in the morning.
        if (Ai.AiQueries.IsNight(sim))
            return;
        var parties = sim.Defs.AllParties.Where(p => p.Regular).ToList();
        if (rareDue)
        {
            _rareCame = true;
            Send(sim, RareCompany(sim)!.Value.Company);
        }
        else if (parties.Count > 0)
            Send(sim, NextParty(parties, _rand));
        else
            return;
        // A table that's talked of brings the next lot sooner; the talk fades with each new party.
        NextArrivalTick = now + SimClock.HoursToTicks(HoursBetweenParties * _rand.Range(0.7f, 1.3f) * (1f - sim.Shops.FareFactor));
        sim.Shops.TalkFades();
    }

    /// <summary>
    /// Whether a rare company comes this round, and on which of its days: each rare one in turn
    /// (in def order) has its chance, and the first to come is the round's.
    /// </summary>
    private void RollRare(Simulation sim)
    {
        _rareRolled = true;
        foreach (var def in sim.Defs.AllParties)
            if (def.Rare && _rareRand.Chance(def.Chance))
            {
                _rare = def.Name;
                _rareDay = Math.Min(_rareRand.Range(def.FromDay, def.ToDay + 1), Math.Max(1, sim.Defs.Season.Days));
                return;
            }
    }

    /// <summary>A dev's or a test's say: this rare company comes this round, on this day.</summary>
    public void SetRare(PartyDef def, int day)
    {
        _rareRolled = true;
        _rareCame = false;
        _rare = def.Name;
        _rareDay = day;
    }

    /// <summary>Players in the round, bots too: the size a company comes at (<see cref="PartyDef.SizeFor"/>).</summary>
    public static int Players(Simulation sim) => sim.World.Query<PlayerCharacter>().Count();

    /// <summary>One of the parties sent least this round (<see cref="Send"/> counts them), at random.</summary>
    internal PartyDef NextParty(IReadOnlyList<PartyDef> parties) => NextParty(parties, _rand);

    private PartyDef NextParty(IReadOnlyList<PartyDef> parties, SimRandom rand)
    {
        var least = parties.Min(p => _sent.Count(n => n == p.Name));
        var fewest = parties.Where(p => _sent.Count(n => n == p.Name) == least).ToList();
        return fewest[rand.NextInt(fewest.Count)];
    }

    /// <summary>
    /// A traveller up the road when one's due, if it's the afternoon and there's room for another;
    /// then the next, about a day on, at some hour of that day's window.
    /// </summary>
    private void Travellers(Simulation sim, long now)
    {
        if (NextTravellerTick < 0)
        {
            NextTravellerTick = InWindow(sim, now);
            return;
        }
        if (now < NextTravellerTick)
            return;
        var hour = sim.Clock.HourOfDay;
        if (hour is < TravellerFromHour or >= TravellerUntilHour)
        {
            NextTravellerTick = InWindow(sim, now); // missed it (a save, a long festival): the next afternoon
            return;
        }
        var kinds = sim.Defs.AllParties.Where(p => p.Traveller).ToList();
        if (kinds.Count == 0)
        {
            NextTravellerTick = long.MaxValue;
            return;
        }
        if (TravellersPresent(sim) >= MaxTravellers)
        {
            NextTravellerTick = now + SimClock.HoursToTicks(1);
            return;
        }
        Send(sim, NextParty(kinds, _travellerRand));
        NextTravellerTick = InWindow(sim, now + SimClock.HoursToTicks(HoursBetweenTravellers * _travellerRand.Range(0.7f, 1.3f)));
    }

    /// <summary>
    /// A tick at some hour of the traveller window (<see cref="TravellerFromHour"/> to
    /// <see cref="TravellerUntilHour"/>) no earlier than <paramref name="from"/>: that day's, or the next if it's past.
    /// </summary>
    private long InWindow(Simulation sim, long from)
    {
        var offset = sim.Clock.StartHour * (long)SimClock.TicksPerHour; // ticks count from the start hour, days from midnight
        var total = from + offset;
        var midnight = total - total % SimClock.TicksPerDay;
        long open = midnight + SimClock.HoursToTicks(TravellerFromHour), close = midnight + SimClock.HoursToTicks(TravellerUntilHour);
        if (total >= close)
        {
            open += SimClock.TicksPerDay;
            close += SimClock.TicksPerDay;
        }
        var start = Math.Max(total, open);
        return start + (long)((close - start) * _travellerRand.NextFloat()) - offset;
    }

    /// <summary>Travellers in town, alive and not on their way out.</summary>
    public static int TravellersPresent(Simulation sim)
    {
        var w = sim.World;
        var n = 0;
        foreach (var e in w.Query<Adventurer>())
            if (w.Get<Adventurer>(e).PartyDef.Traveller && !w.Get<Creature>(e).Dead && !w.Has<Leaving>(e))
                n++;
        return n;
    }

    /// <summary>No party (nor traveller) comes unless one is sent (<see cref="Send"/>): the combat sweep, with or without heroes as it asks.</summary>
    public void HoldArrivals()
    {
        NextArrivalTick = NextTravellerTick = long.MaxValue;
        (_rareRolled, _rare) = (true, null); // and no rare company either
    }

    /// <summary>Is any adventuring party's member still in town (alive)? Travellers don't count: they hold no party back.</summary>
    public static bool Present(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in sim.Creatures())
            if (w.TryGet<Adventurer>(e, out var a) && !a.PartyDef.Traveller && !w.Get<Creature>(e).Dead)
                return true;
        return false;
    }

    /// <summary>Everyone whose stay is up makes for the edge, with one word for each party.</summary>
    /// <summary>Game hours past their leaving time an adventurer waits to have a locked strongbox opened.</summary>
    public const float BoxWaitHours = 8;

    /// <summary>Satisfaction at or above which they leave pleased with the town, and at or below which, sour.</summary>
    public const float PleasedFrom = 3f, SourFrom = -1f;

    private static void SendOff(Simulation sim, long now)
    {
        var w = sim.World;
        List<int>? announced = null;
        foreach (var e in w.Query<Adventurer>())
        {
            var a = w.Get<Adventurer>(e);
            if (now < a.LeaveTick || w.Has<Leaving>(e) || w.Get<Creature>(e).Dead)
                continue;
            // Taken a quest and waiting on their gear to set out: the quest sends them (Expeditions).
            if (a.Quest != 0)
                continue;
            // The practice yard's customers wait as long as a learner takes: they came for the lesson.
            if (sim.Round.Practice && Scenarios.Practice.StillWanted(sim, e))
                continue;
            // Nobody leaves town with their sword still at the smith's, if they can help it.
            if (sim.Shops.GearOut(sim, e) && now < a.LeaveTick + SimClock.HoursToTicks(Shops.ShopSystem.HandedOverHours))
                continue;
            // Nor with the reward still locked in its box: back late, they'll want it opened in the morning.
            if (!Tinkering.Lockwork.LockedBoxOf(sim, e).IsNull && now < a.LeaveTick + SimClock.HoursToTicks(BoxWaitHours))
                continue;
            w.Add(e, new Leaving());
            sim.Reckoning.AdventurerLeft(sim, a.Satisfaction, Director.Reckoning.WordOf(a));
            AiSystem.EndJob(sim, e, w.Get<Mind>(e), false);
            announced ??= [];
            if (announced.Contains(a.Party))
                continue;
            announced.Add(a.Party);
            Tinkering.Wagons.Leave(sim, a.Party);
            // A parting word for the town, as it found it.
            if (Speech.SpeechSystem.CanAct(sim, e))
                Speech.Barks.Say(sim, e, "adv_leaving", Speech.Volume.Say, null, extra: [a.Satisfaction >= PleasedFrom ? "pleased" : a.Satisfaction <= SourFrom ? "sour" : "middling"]);
            sim.Messages.Add(MessageKind.Info, a.PartyDef.Traveller
                ? $"{w.Get<Creature>(e).Name} the {a.Kind.Label} moves on."
                : $"{Capital(a.PartyDef.Label)} are moving on.", w.Get<Position>(e).Cell);
        }
    }

    /// <summary>
    /// A party walks in from a map edge toward its haunt, armed, dressed, fed and carrying coin.
    /// Empty if there's no way in. Also the debug panel's and tests' way to send one now.
    /// </summary>
    public List<Entity> Send(Simulation sim, PartyDef def, int most = int.MaxValue)
    {
        var members = new List<Entity>();
        var toward = HauntOf(sim, def) is { } haunt ? haunt.Anchor : TownCenter(sim);
        if (toward is not { } target)
            return members;
        // A traveller's every roll is from their own stream: one coming doesn't change the parties.
        var rand = def.Traveller ? _travellerRand : _rand;
        int3? entry = null;
        var first = rand.NextInt(4);
        for (var i = 0; i < 4 && entry == null; i++)
            entry = Raids.EdgeEntry(sim, (first + i) % 4, target);
        if (entry is not { } from)
            return members;

        var id = _nextId++;
        _sent.Add(def.Name);
        var stay = def.Traveller ? TravellerStay(sim, rand.Range(def.NightsMin, def.NightsMax + 1), rand)
            : SimClock.HoursToTicks(def.StayHours * rand.Range(0.75f, 1.25f));
        var names = def.Names.ToList();
        if (!def.FixedNames)
            Shuffle(names, rand);
        var kinds = def.MembersFor(Players(sim)).Take(most).ToList();
        var spots = Raids.GatherSpots(sim, from, target, kinds.Count);
        for (var i = 0; i < spots.Count; i++)
        {
            var kind = kinds[i];
            var e = Spawn(sim, kind, names[i], spots[i], rand);
            sim.World.Add(e, new Adventurer
            {
                Party = id, Kind = kind, PartyDef = def, LeaveTick = sim.Clock.Tick + stay, Arrived = sim.Clock.Tick,
                NextDrink = sim.Clock.Tick + SimClock.HoursToTicks(rand.Range(0.3f, 1.2f)), // the road's thirsty work
            });
            // And hungry work: they come in wanting feeding. Not from _rand, which rolls who they are.
            if (sim.World.TryGet<Needs>(e, out var needs))
                needs.Values[sim.Defs.Need("Food").Index] = 0.45f + 0.1f * (i % 3);
            members.Add(e);
        }
        if (members.Count > 0 && def.Traveller)
            sim.Messages.Add(MessageKind.Info, $"{Capital(def.Label)} comes up the {Raids.Direction(sim.Map, from)} road.", from);
        else if (members.Count > 0)
        {
            PartiesSent++;
            sim.Expeditions.PostOwn(sim, def);
            // The wagon's whoever's best able to pay for its mending.
            Tinkering.Wagons.Bring(sim, seed, def, id, members.OrderByDescending(e => Shops.Coin.Count(sim, e)).First(), target);
            // Who they are, as the town sees them coming: the party's own description, if it has one.
            sim.Messages.Add(MessageKind.Good, string.IsNullOrWhiteSpace(def.Description)
                ? $"{Capital(def.Label)} ride in from the {Raids.Direction(sim.Map, from)}, wanting everything at once."
                : $"{Capital(def.Label)} ride in from the {Raids.Direction(sim.Map, from)}. {def.Description}", from);
        }
        return members;
    }

    /// <summary>Ticks from now to a traveller's leaving: the morning after their last night.</summary>
    private static long TravellerStay(Simulation sim, int nights, SimRandom rand)
    {
        var offset = sim.Clock.StartHour * (long)SimClock.TicksPerHour;
        var total = sim.Clock.Tick + offset;
        var midnight = total - total % SimClock.TicksPerDay;
        var leave = midnight + (long)nights * SimClock.TicksPerDay + SimClock.HoursToTicks(rand.Range(TravellerLeavesFrom, TravellerLeavesBy));
        return leave - total;
    }

    private Entity Spawn(Simulation sim, AdventurerKindDef kind, string name, int3 cell, SimRandom rand)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), name, cell, Faction.Adventurer, ageDays: rand.Range(19, 45) * 365f, rand: rand);
        if (kind.KinOf() is { } kin)
            sim.World.Add(e, kin);
        if (kind.WeaponDef != null)
            sim.Equip(e, kind.WeaponDef);
        if (kind.OutfitDef != null) // clothes first, then armor over them
            Clothing.Dress(sim, e, kind.OutfitDef, Clothing.StreamFor(sim, e));
        foreach (var a in kind.ApparelDefs)
            sim.Wear(e, a);
        if (sim.Defs.TryThing(Simulation.StartingPack, out var pack))
            sim.Wear(e, pack);
        var skills = sim.World.Get<Skills>(e);
        skills.Level[(int)Skill.Melee] = rand.Range(kind.SkillMin, kind.SkillMax + 1);
        skills.Level[(int)Skill.Shooting] = rand.Range(kind.SkillMin, kind.SkillMax + 1);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        RoadWorn(sim, e, rand);
        var coins = rand.Range(kind.PurseMin, kind.PurseMax + 1);
        if (coins > 0)
            Inventory.Stow(sim, e, sim.CreateItem(sim.Defs.Thing("SilverCoin"), coins));
        return e;
    }

    /// <summary>
    /// They've come a long way: blades dulled and nicked, armor scuffed, and now and then a scrape
    /// that wants seeing to. Work for the smith and the healer from the first hour.
    /// </summary>
    private static void RoadWorn(Simulation sim, Entity e, SimRandom rand)
    {
        var w = sim.World;
        if (w.TryGet<Equipment>(e, out var eq) && w.IsAlive(eq.Weapon))
        {
            ref var t = ref w.Get<Thing>(eq.Weapon);
            t.Hp = t.MaxHp * rand.Range(0.55f, 1f);
            if (WeaponWear.Edged(t.Def))
                WeaponWear.SetDullness(sim, eq.Weapon, rand.Range(0.15f, 0.7f));
        }
        foreach (var item in sim.Worn(e))
            if (w.Get<Thing>(item).Def.Apparel is { } props && (props.Sharp > 0 || props.Blunt > 0))
            {
                ref var t = ref w.Get<Thing>(item);
                t.Hp = t.MaxHp * rand.Range(0.6f, 1f);
            }
        if (rand.Chance(RoadWoundChance) && w.TryGet<Health>(e, out var h))
        {
            var limbs = Enumerable.Range(0, h.Body.Parts.Count).Where(p => !h.Body.Parts[p].Vital).ToList();
            if (limbs.Count > 0)
                HealthSystem.Damage(sim, e, rand.Range(3f, 8f), rand.Chance(0.5f) ? DamageType.Cut : DamageType.Blunt, limbs[rand.NextInt(limbs.Count)]);
        }
    }

    /// <summary>The chance an adventurer arrives with a scrape that wants tending.</summary>
    public const float RoadWoundChance = 0.4f;

    /// <summary>The first standing building of the party's haunt, if the town has one.</summary>
    public static PresetBuilding? HauntOf(Simulation sim, PartyDef def)
    {
        var e = Shops.ShopSystem.BuildingFor(sim, def.HauntRole);
        return e.IsNull ? null : sim.World.Get<PresetBuilding>(e);
    }

    /// <summary>Where the villagers are, for a town with no haunt to walk to.</summary>
    private static int3? TownCenter(Simulation sim)
    {
        foreach (var e in sim.Creatures())
            if (sim.World.Get<Creature>(e).Faction == Faction.Village)
                return sim.World.Get<Position>(e).Cell;
        return null;
    }

    private static void Shuffle<T>(List<T> list, SimRandom rand)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rand.NextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
