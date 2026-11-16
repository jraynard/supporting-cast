using HeroesNotIncluded.Sim.Adventurers;
using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>Blows thrown by one side in a trial, by how each met its mark.</summary>
public sealed class BlowTally
{
    public readonly int[] ByOutcome = new int[Enum.GetValues<BlowOutcome>().Length];

    public int this[BlowOutcome o] => ByOutcome[(int)o];
    public int Total => ByOutcome.Sum();

    internal void Add(BlowOutcome o) => ByOutcome[(int)o]++;

    public override string ToString() =>
        $"{Total} thrown: {this[BlowOutcome.Hit] + this[BlowOutcome.Riposte]} hit, {this[BlowOutcome.Dodged]} dodged, "
        + $"{this[BlowOutcome.Blocked]} blocked, {this[BlowOutcome.Parried]} parried, {this[BlowOutcome.GuardBroken]} broke a guard, {this[BlowOutcome.Miss]} missed";
}

/// <summary>How one raid on the town went (<see cref="CombatSweep.RaidTrial"/>).</summary>
public sealed record RaidResult(
    ulong Seed, bool Party, bool? Met, int Raiders, string Band,
    int RaidersDown, int RaidersFled, int TownDead, int TownDowned, int HeroesDead, int HeroesDowned,
    float Hours, BlowTally RaiderBlows, BlowTally TownBlows, bool RoundLost = false)
{
    /// <summary>Who stood where at the start, and who went down doing what: for reading one trial closely.</summary>
    public List<string> Log { get; init; } = [];
}

/// <summary>How one dragon's visit went (<see cref="CombatSweep.DragonTrial"/>).</summary>
public sealed record DragonResult(
    ulong Seed, bool Party, bool? Met, bool Downed, bool DrivenOff, float DamageTaken,
    int TownDead, int TownDowned, int HeroesDead, int HeroesDowned, float Hours, BlowTally TownBlows);

/// <summary>Who won one fight in the ring (<see cref="CombatSweep.Duel"/>). Winner null: neither, in the time.</summary>
public sealed record DuelResult(ulong Seed, string Raider, string Villager, bool? VillagerWon, float Seconds);

/// <summary>
/// Combat balance, measured headlessly: a raid (or the dragon) on the town, a seed at a time, and
/// one-on-one fights in the practice ring between AI fighters. Nothing here is played: it's the
/// yardstick for tuning (<c>--combat-sweep</c>, <c>CombatBalanceTests</c>), so it sets up the same
/// town a round does, sends the crisis, and counts.
/// </summary>
public static class CombatSweep
{
    /// <summary>The day a trial's crisis comes on, and the hour: the season's main raid.</summary>
    public const int Day = 2;
    public const float Hour = 11;

    /// <summary>
    /// A round's town on <paramref name="seed"/>, nobody playing (the one seat left at once, so their
    /// character is a villager like the rest), no beats of its own and no party unless asked for:
    /// on the clock at <see cref="Day"/>, <see cref="Hour"/>.
    /// </summary>
    public static Simulation Town(DefDatabase defs, ulong seed, bool party, string difficulty = "Normal")
    {
        var sim = VillageRound.NewSimulation(defs, seed);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Director.Enable(defs.Difficulty(difficulty));
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Guid.Empty, "Arin", default, []));
        sim.ApplyNow(new StartRoundCommand());
        Players.Leave(sim, Guid.Empty);
        sim.Director.PassOverBeats(sim);
        sim.Parties.HoldArrivals();
        sim.Clock.Tick = sim.Round.TickAt(sim, Day, Hour - (party ? 1 : 0));
        var parties = defs.AllParties.Where(p => !p.Traveller && !p.Rare).ToList();
        if (party && parties.Count > 0)
        {
            sim.Parties.Send(sim, parties[(int)(seed % (ulong)parties.Count)]);
            sim.TickMany(SimClock.TicksPerHour); // they've walked in
        }
        return sim;
    }

    /// <summary>A raid on the town as the season's day-two beat would send it, to its end.</summary>
    public static RaidResult RaidTrial(DefDatabase defs, ulong seed, bool party, float strength = 1f)
    {
        var sim = Town(defs, seed, party);
        var w = sim.World;
        var before = Counts(sim);
        var raid = (RaidCrisis?)sim.Director.Send(sim, CrisisKind.Raid, strength);
        if (raid == null)
            return new RaidResult(seed, party, null, 0, "", 0, 0, 0, 0, 0, 0, 0, new(), new());
        var band = string.Join(" ", raid.Raiders.Select(e => w.TryGet<Equipment>(e, out var eq) && w.IsAlive(eq.Weapon) ? w.Get<Thing>(eq.Weapon).Def.Name : "fists"));
        var start = sim.Clock.Tick;
        var raiders = new BlowTally();
        var town = new BlowTally();
        var downed = new Dictionary<Entity, Faction>();
        var log = new List<string>();
        foreach (var e in sim.Creatures().Where(e => w.Get<Creature>(e) is { Faction: Faction.Village or Faction.Adventurer, Def.Race: Race.Human, Dead: false }))
            log.Add($"  {w.Get<Creature>(e).Name} ({w.Get<Creature>(e).Faction}): {Arms(sim, e)}");
        var met = Run(sim, start, SimClock.HoursToTicks(sim.Defs.Season.CrisisHours + 1), downed, b =>
            (raid.Raiders.Contains(b.Attacker) ? raiders : town).Add(b.Outcome), log: log);
        var after = Counts(sim);
        var down = raid.Raiders.Count(e => w.IsAlive(e) && w.Get<Creature>(e) is { Dead: true } or { Downed: true });
        var fled = raid.Raiders.Count(e => !w.IsAlive(e) || (w.Get<Creature>(e).Able && w.Has<Leaving>(e))); // off the map, or on their way
        return new RaidResult(seed, party, met, raid.Raiders.Count, band, down, fled,
            after.TownDead - before.TownDead, downed.Values.Count(f => f == Faction.Village),
            after.HeroesDead - before.HeroesDead, downed.Values.Count(f => f == Faction.Adventurer),
            (sim.Clock.Tick - start) / (float)SimClock.TicksPerHour, raiders, town, sim.Round.Phase == RoundPhase.Over) { Log = log };
    }

    /// <summary>The dragon comes to town, to its end.</summary>
    public static DragonResult DragonTrial(DefDatabase defs, ulong seed, bool party)
    {
        var sim = Town(defs, seed, party);
        var w = sim.World;
        var before = Counts(sim);
        var crisis = (DragonCrisis?)sim.Director.Send(sim, CrisisKind.Dragon);
        if (crisis == null)
            return new DragonResult(seed, party, null, false, false, 0, 0, 0, 0, 0, 0, new());
        var dragon = crisis.Dragon;
        var start = sim.Clock.Tick;
        var town = new BlowTally();
        var downed = new Dictionary<Entity, Faction>();
        var drivenOff = false;
        var damage = 0f;
        var met = Run(sim, start, SimClock.HoursToTicks(DragonCrisis.Hours + 1), downed, b =>
        {
            if (b.Target == dragon)
                town.Add(b.Outcome);
        }, () =>
        {
            if (!w.IsAlive(dragon))
                return;
            drivenOff |= DragonBrain.DrivenOff(sim, dragon);
            damage = Math.Max(damage, w.Get<Health>(dragon).Injuries.Sum(i => i.Damage));
        });
        var after = Counts(sim);
        return new DragonResult(seed, party, met, w.IsAlive(dragon) && w.Get<Creature>(dragon) is { Dead: true } or { Downed: true },
            drivenOff, damage, after.TownDead - before.TownDead, downed.Values.Count(f => f == Faction.Village),
            after.HeroesDead - before.HeroesDead, downed.Values.Count(f => f == Faction.Adventurer),
            (sim.Clock.Tick - start) / (float)SimClock.TicksPerHour, town);
    }

    /// <summary>
    /// One raider of <paramref name="raider"/>'s kind against one villager, both AI, in the
    /// practice ring: the villager with every skill at <paramref name="skill"/>, holding
    /// <paramref name="weapon"/> (none: bare hands) and wearing <paramref name="apparel"/>. The
    /// fight's over when one is down, or wants out (too hurt to go on).
    /// </summary>
    public static DuelResult Duel(DefDatabase defs, ulong seed, string raider, string? weapon, int skill = Skills.Average, params string[] apparel)
    {
        var sim = Arena.NewSimulation(defs, seed);
        var center = Arena.Setup(sim);
        var w = sim.World;
        var villager = sim.SpawnCreature(defs.Creature("Human"), "Corin", center, Faction.Village, ageDays: 25 * 365);
        Array.Fill(w.Get<Needs>(villager).Values, 1f);
        var skills = w.Get<Skills>(villager);
        Array.Fill(skills.Level, skill);
        if (weapon != null)
            sim.Equip(villager, defs.Thing(weapon));
        foreach (var a in apparel)
            sim.Wear(villager, defs.Thing(a));
        var kind = defs.AllRaiderKinds.First(k => k.Name == raider);
        var at = sim.SpiralFrom(center + new int3(Arena.Apart, 0, 0), 6)
            .First(c => sim.Map.Passable(c) && c != center && sim.Regions.CanReach(c, center, PathEnd.Touch));
        var foe = Raids.SpawnRaider(sim, kind, "Mott", at, sim.Rand);
        var label = (weapon ?? "fists") + (apparel.Length > 0 ? "+" + string.Join("+", apparel) : "") + $"@{skill}";
        var limit = SimClock.TicksPerSecond * 120;
        for (var t = 0; t < limit; t++)
        {
            sim.Tick();
            var vOut = Out(sim, villager);
            var fOut = Out(sim, foe);
            if (vOut || fOut)
                return new DuelResult(seed, raider, label, vOut == fOut ? null : fOut, t / (float)SimClock.TicksPerSecond);
        }
        return new DuelResult(seed, raider, label, null, limit / (float)SimClock.TicksPerSecond);
    }

    /// <summary>Out of the fight: gone, dead, down, leaving, or hurt enough to want to.</summary>
    private static bool Out(Simulation sim, Entity e) =>
        !sim.World.IsAlive(e) || !sim.World.Get<Creature>(e).Able || sim.World.Has<Leaving>(e) || RaiderBrain.WantsToFlee(sim, e);

    /// <summary>Tick until the crisis resolves (or <paramref name="limit"/>), noting every blow and everyone who went down.</summary>
    private static bool? Run(Simulation sim, long start, long limit, Dictionary<Entity, Faction> downed, Action<BlowEvent> blow, Action? each = null, List<string>? log = null)
    {
        var doing = new Dictionary<Entity, string>();
        var hitBy = new Dictionary<Entity, Entity>();
        var w = sim.World;
        var met = sim.Director.Met;
        var unmet = sim.Director.Unmet;
        var seen = long.MinValue;
        while (sim.Director.Active.Count > 0 && sim.Clock.Tick - start < limit)
        {
            sim.Tick();
            foreach (var b in sim.RecentBlows)
                if (b.Tick > seen)
                {
                    blow(b);
                    if (b.Outcome is BlowOutcome.Hit or BlowOutcome.Riposte or BlowOutcome.GuardBroken or BlowOutcome.Blocked)
                        hitBy[b.Target] = b.Attacker;
                }
            if (sim.RecentBlows.Count > 0)
                seen = Math.Max(seen, sim.RecentBlows[^1].Tick);
            if (sim.Clock.Tick % 30 != 0)
                continue;
            each?.Invoke();
            foreach (var e in sim.Creatures())
            {
                if (w.Get<Creature>(e) is not { Faction: Faction.Village or Faction.Adventurer, Def.Race: Race.Human } c)
                    continue;
                if (c.Able)
                {
                    if (w.Get<Mind>(e).Job is { } job)
                        doing[e] = job.Report ?? job.GetType().Name;
                    continue;
                }
                if (downed.TryAdd(e, c.Faction) && log != null)
                {
                    var by = hitBy.TryGetValue(e, out var a) && w.IsAlive(a) ? $"{w.Get<Creature>(a).Name} ({Arms(sim, a)})" : "?";
                    log.Add($"+{(sim.Clock.Tick - start) / (float)SimClock.TicksPerHour * 60,3:F0}m {c.Name} ({c.Faction}, {Arms(sim, e)}) {(c.Dead ? "DIED" : "down")}, last hit by {by}, was: {doing.GetValueOrDefault(e, "-")}");
                }
            }
        }
        if (log != null)
            log.AddRange(sim.Messages.Messages.Where(m => m.Tick >= start && m.Kind is MessageKind.Danger or MessageKind.Good or MessageKind.Warning).Select(m => $"  [{(m.Tick - start) / (float)SimClock.TicksPerHour * 60,3:F0}m] {m.Text}"));
        return sim.Director.Met > met ? true : sim.Director.Unmet > unmet ? false : null;
    }

    /// <summary>What they hold, or "unarmed".</summary>
    private static string Arms(Simulation sim, Entity e) =>
        sim.World.TryGet<Equipment>(e, out var eq) && sim.World.IsAlive(eq.Weapon) ? sim.World.Get<Thing>(eq.Weapon).Def.Name : "unarmed";

    private static (int TownDead, int HeroesDead) Counts(Simulation sim)
    {
        var w = sim.World;
        int town = 0, heroes = 0;
        foreach (var e in w.Query<Creature>())
            if (w.Get<Creature>(e) is { Def.Race: Race.Human, Dead: true } c)
            {
                if (c.Faction == Faction.Adventurer)
                    heroes++;
                else if (c.Faction == Faction.Village)
                    town++;
            }
        return (town, heroes);
    }
}
