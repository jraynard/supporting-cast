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
/// stream of its own, so a party arriving doesn't reshuffle the rest of the sim.
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

    /// <summary>When the next party is due; -1 until a round is playing.</summary>
    public long NextArrivalTick { get; private set; } = -1;

    public int PartiesSent { get; private set; }

    private int _nextId = 1;

    public void Save(Saving.SaveWriter w, Simulation sim)
    {
        var (a, b, c, d) = _rand.State;
        w.UInt(a); w.UInt(b); w.UInt(c); w.UInt(d);
        w.Long(NextArrivalTick);
        w.Int(PartiesSent);
        w.Int(_nextId);
    }

    public void Load(Saving.SaveReader r, Simulation sim)
    {
        _rand.State = (r.UInt(), r.UInt(), r.UInt(), r.UInt());
        NextArrivalTick = r.Long();
        PartiesSent = r.Int();
        _nextId = r.Int();
    }

    public void Update(Simulation sim)
    {
        if (!sim.Round.Live)
            return;
        var now = sim.Clock.Tick;
        SendOff(sim, now);
        if (sim.Round.Phase == RoundPhase.Festival)
            return;
        if (NextArrivalTick < 0)
        {
            NextArrivalTick = now + SimClock.HoursToTicks(FirstArrivalHours);
            return;
        }
        if (now < NextArrivalTick)
            return;
        if (Present(sim))
        {
            NextArrivalTick = now + SimClock.HoursToTicks(1); // one party at a time
            return;
        }
        var parties = sim.Defs.AllParties;
        if (parties.Count == 0)
            return;
        Send(sim, parties[_rand.NextInt(parties.Count)]);
        NextArrivalTick = now + SimClock.HoursToTicks(HoursBetweenParties * _rand.Range(0.7f, 1.3f));
    }

    /// <summary>Is any adventurer still in town (alive and not on their way out)?</summary>
    public static bool Present(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in sim.Creatures())
            if (w.Has<Adventurer>(e) && !w.Get<Creature>(e).Dead)
                return true;
        return false;
    }

    /// <summary>Everyone whose stay is up makes for the edge, with one word for each party.</summary>
    /// <summary>Game hours past their leaving time an adventurer waits to have a locked strongbox opened.</summary>
    public const float BoxWaitHours = 8;

    private static void SendOff(Simulation sim, long now)
    {
        var w = sim.World;
        List<int>? announced = null;
        foreach (var e in w.Query<Adventurer>())
        {
            var a = w.Get<Adventurer>(e);
            if (now < a.LeaveTick || w.Has<Leaving>(e) || w.Get<Creature>(e).Dead)
                continue;
            // Nobody leaves town with their sword still at the smith's, if they can help it.
            if (sim.Shops.GearOut(sim, e) && now < a.LeaveTick + SimClock.HoursToTicks(Shops.ShopSystem.HandedOverHours))
                continue;
            // Nor with the reward still locked in its box: back late, they'll want it opened in the morning.
            if (!Tinkering.Lockwork.LockedBoxOf(sim, e).IsNull && now < a.LeaveTick + SimClock.HoursToTicks(BoxWaitHours))
                continue;
            w.Add(e, new Leaving());
            sim.Reckoning.AdventurerLeft(sim, a.Satisfaction);
            AiSystem.EndJob(sim, e, w.Get<Mind>(e), false);
            announced ??= [];
            if (announced.Contains(a.Party))
                continue;
            announced.Add(a.Party);
            Tinkering.Wagons.Leave(sim, a.Party);
            sim.Messages.Add(MessageKind.Info, $"{Capital(a.PartyDef.Label)} are moving on.", w.Get<Position>(e).Cell);
        }
    }

    /// <summary>
    /// A party walks in from a map edge toward its haunt, armed, dressed, fed and carrying coin.
    /// Empty if there's no way in. Also the debug panel's and tests' way to send one now.
    /// </summary>
    public List<Entity> Send(Simulation sim, PartyDef def)
    {
        var members = new List<Entity>();
        var toward = HauntOf(sim, def) is { } haunt ? haunt.Anchor : TownCenter(sim);
        if (toward is not { } target)
            return members;
        int3? entry = null;
        var first = _rand.NextInt(4);
        for (var i = 0; i < 4 && entry == null; i++)
            entry = Raids.EdgeEntry(sim, (first + i) % 4, target);
        if (entry is not { } from)
            return members;

        var id = _nextId++;
        var stay = SimClock.HoursToTicks(def.StayHours * _rand.Range(0.75f, 1.25f));
        var names = def.Names.ToList();
        Shuffle(names);
        var spots = Raids.GatherSpots(sim, from, target, def.MemberDefs.Count);
        for (var i = 0; i < spots.Count; i++)
        {
            var kind = def.MemberDefs[i];
            var e = Spawn(sim, kind, names[i], spots[i]);
            sim.World.Add(e, new Adventurer
            {
                Party = id, Kind = kind, PartyDef = def, LeaveTick = sim.Clock.Tick + stay, Arrived = sim.Clock.Tick,
                NextDrink = sim.Clock.Tick + SimClock.HoursToTicks(_rand.Range(0.3f, 1.2f)), // the road's thirsty work
            });
            members.Add(e);
        }
        if (members.Count > 0)
        {
            PartiesSent++;
            // The wagon's whoever's best able to pay for its mending.
            Tinkering.Wagons.Bring(sim, seed, def, id, members.OrderByDescending(e => Shops.Coin.Count(sim, e)).First(), target);
            sim.Messages.Add(MessageKind.Good,
                $"{Capital(def.Label)} ride in from the {Raids.Direction(sim.Map, from)}, wanting everything at once.", from);
        }
        return members;
    }

    private Entity Spawn(Simulation sim, AdventurerKindDef kind, string name, int3 cell)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), name, cell, Faction.Adventurer, ageDays: _rand.Range(19, 45) * 365f, rand: _rand);
        if (kind.WeaponDef != null)
            sim.Equip(e, kind.WeaponDef);
        if (kind.OutfitDef != null) // clothes first, then armor over them
            Clothing.Dress(sim, e, kind.OutfitDef, Clothing.StreamFor(sim, e));
        foreach (var a in kind.ApparelDefs)
            sim.Wear(e, a);
        if (sim.Defs.TryThing(Simulation.StartingPack, out var pack))
            sim.Wear(e, pack);
        var skills = sim.World.Get<Skills>(e);
        skills.Level[(int)Skill.Melee] = _rand.Range(kind.SkillMin, kind.SkillMax + 1);
        skills.Level[(int)Skill.Shooting] = _rand.Range(kind.SkillMin, kind.SkillMax + 1);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        RoadWorn(sim, e);
        var coins = _rand.Range(kind.PurseMin, kind.PurseMax + 1);
        if (coins > 0)
            Inventory.Stow(sim, e, sim.CreateItem(sim.Defs.Thing("SilverCoin"), coins));
        return e;
    }

    /// <summary>
    /// They've come a long way: blades dulled and nicked, armor scuffed, and now and then a scrape
    /// that wants seeing to. Work for the smith and the healer from the first hour.
    /// </summary>
    private void RoadWorn(Simulation sim, Entity e)
    {
        var w = sim.World;
        if (w.TryGet<Equipment>(e, out var eq) && w.IsAlive(eq.Weapon))
        {
            ref var t = ref w.Get<Thing>(eq.Weapon);
            t.Hp = t.MaxHp * _rand.Range(0.55f, 1f);
            if (WeaponWear.Edged(t.Def))
                WeaponWear.SetDullness(sim, eq.Weapon, _rand.Range(0.15f, 0.7f));
        }
        foreach (var item in sim.Worn(e))
            if (w.Get<Thing>(item).Def.Apparel is { } props && (props.Sharp > 0 || props.Blunt > 0))
            {
                ref var t = ref w.Get<Thing>(item);
                t.Hp = t.MaxHp * _rand.Range(0.6f, 1f);
            }
        if (_rand.Chance(RoadWoundChance) && w.TryGet<Health>(e, out var h))
        {
            var limbs = Enumerable.Range(0, h.Body.Parts.Count).Where(p => !h.Body.Parts[p].Vital).ToList();
            if (limbs.Count > 0)
                HealthSystem.Damage(sim, e, _rand.Range(3f, 8f), _rand.Chance(0.5f) ? DamageType.Cut : DamageType.Blunt, limbs[_rand.NextInt(limbs.Count)]);
        }
    }

    /// <summary>The chance an adventurer arrives with a scrape that wants tending.</summary>
    public const float RoadWoundChance = 0.4f;

    /// <summary>The first standing building of the party's haunt, if the town has one.</summary>
    public static PresetBuilding? HauntOf(Simulation sim, PartyDef def)
    {
        if (def.HauntDef == null)
            return null;
        foreach (var e in sim.World.Each<PresetBuilding>())
        {
            var b = sim.World.Get<PresetBuilding>(e);
            if (b.Def == def.HauntDef && !b.Demolishing)
                return b;
        }
        return null;
    }

    /// <summary>Where the villagers are, for a town with no haunt to walk to.</summary>
    private static int3? TownCenter(Simulation sim)
    {
        foreach (var e in sim.Creatures())
            if (sim.World.Get<Creature>(e).Faction == Faction.Village)
                return sim.World.Get<Position>(e).Cell;
        return null;
    }

    private void Shuffle<T>(List<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _rand.NextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
