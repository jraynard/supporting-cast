using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>
/// The practice ring (<c>--arena</c>): a small fenced ring (data/defs/maps/arena.json, from
/// tools/arena_map.py), the player with a weapon, and one foe at a time. Each bout
/// (<see cref="Bout"/>) starts them both fresh, so a fight can be tried again and again without a
/// whole raid around it. No round and no villagers: it's for learning and tuning the fight.
/// </summary>
public static class Arena
{
    public const string Map = "Arena";

    /// <summary>The foe a bout brings unless another is asked for.</summary>
    public const string DefaultFoe = "Swordsman";

    /// <summary>What the player fights with unless another is asked for.</summary>
    public const string DefaultWeapon = "ShortSword";

    /// <summary>How far across the ring a bout's foe starts from the player.</summary>
    public const int Apart = 6;

    public static Simulation NewSimulation(DefDatabase defs, ulong seed) => MapBuilder.CreateSimulation(defs, defs.Map(Map), seed);

    /// <summary>Lay out the ring. Returns its middle.</summary>
    public static int3 Setup(Simulation sim) =>
        MapBuilder.Build(sim, sim.Defs.Map(Map), new MapGenerator.Options { Villagers = 0, SpawnStartingKit = false, Wildlife = false }).Center;

    /// <summary>The one foe that isn't a raider: a dragon, its jaws and its fire.</summary>
    public const string Dragon = "Dragon";

    /// <summary>The foes a bout can bring: the raiders' kinds, by name, and a dragon.</summary>
    public static IEnumerable<string> Foes(DefDatabase defs) => defs.AllRaiderKinds.Select(k => k.Name).Append(Dragon);

    /// <summary>
    /// A new bout: the last foe gone, the player healed, rested and armed (with <paramref name="weapon"/>
    /// if it's a weapon, else what they hold), and a <paramref name="foe"/> (a raider kind, or
    /// <see cref="Dragon"/>) across the ring.
    /// Returns the foe, or Entity.Null if there's no such kind or no player.
    /// </summary>
    public static Entity Bout(Simulation sim, Entity player, string foe, string? weapon = null)
    {
        var w = sim.World;
        var kind = sim.Defs.AllRaiderKinds.FirstOrDefault(k => k.Name == foe);
        var dragonDef = foe == Dragon ? sim.Defs.AllCreatures.FirstOrDefault(c => c.Race == Race.Dragon) : null;
        if (!w.IsAlive(player) || !w.Has<Avatar>(player) || (kind == null && dragonDef == null))
            return Entity.Null;
        // The last foe leaves the ring, gear and all, before the next steps in.
        foreach (var e in sim.Creatures().Where(e => w.Get<Creature>(e).Faction is Faction.Raider or Faction.Predator).ToList())
            sim.Depart(e);
        w.FlushDeferred();
        Freshen(sim, player);
        if (w.Has<FreeMover>(player))
            w.Get<FreeMover>(player).Drawn = true; // in the ring to fight
        if (weapon != null && sim.Defs.AllThings.FirstOrDefault(t => t.Name == weapon) is { Weapon: not null } arms)
            sim.Equip(player, arms);
        else if (!w.TryGet<Equipment>(player, out var eq) || !w.IsAlive(eq.Weapon))
            sim.Equip(player, sim.Defs.Thing(DefaultWeapon));

        var here = w.Get<Position>(player).Cell;
        var center = new int3(sim.Map.Width / 2, sim.Map.Height / 2, here.Z);
        // Across the middle from the player, or anywhere free if that's no good.
        var dir = new int2(Math.Sign(center.X - here.X), Math.Sign(center.Y - here.Y));
        if (dir == int2.Zero)
            dir = new int2(0, 1);
        var want = new int3(here.X + dir.X * Apart, here.Y + dir.Y * Apart, here.Z);
        var at = sim.SpiralFrom(want, 6).FirstOrDefault(c => sim.Map.Passable(c) && c != here && sim.Regions.CanReach(c, here, PathEnd.Touch), want);
        if (dragonDef != null)
        {
            var adult = dragonDef.LifeStages.Count > 0 ? dragonDef.LifeStages[^1].MinAgeDays + 8 : 30;
            var dragon = sim.SpawnCreature(dragonDef, "Old Ashjaw", at, Faction.Predator, ageDays: adult);
            Array.Fill(w.Get<Needs>(dragon).Values, 1f);
            sim.Messages.Add(MessageKind.Danger, "A dragon comes down in the ring.", at);
            return dragon;
        }
        var raider = Raids.SpawnRaider(sim, kind!, Raids.Names[sim.Rand.NextInt(Raids.Names.Length)], at, sim.Rand);
        sim.Messages.Add(MessageKind.Danger, $"A {kind!.Label} steps into the ring.", at);
        return raider;
    }

    /// <summary>Whole again: wounds gone, blood back, on their feet, fed, rested and with their wind.</summary>
    public static void Freshen(Simulation sim, Entity e)
    {
        var w = sim.World;
        if (w.TryGet<Health>(e, out var h))
        {
            h.Injuries.Clear();
            h.Blood = 1f;
            h.Changed();
        }
        ref var c = ref w.Get<Creature>(e);
        c.Dead = false;
        HealthSystem.SetDowned(sim, e, ref c, false);
        if (w.TryGet<Needs>(e, out var needs))
            Array.Fill(needs.Values, 1f);
        if (w.Has<Guard>(e))
            w.Get<Guard>(e) = Guard.Fresh;
        Blows.Cancel(sim, e);
    }
}

/// <summary>A new bout in the practice ring (<see cref="Arena.Bout"/>): a developer's tool, so the host's.</summary>
public sealed record ArenaBoutCommand(Entity Player, string Foe, string? Weapon = null) : IDevCommand
{
    public void Apply(Simulation sim)
    {
        if (Foe != null)
            Arena.Bout(sim, Player, Foe, Weapon);
    }
}
