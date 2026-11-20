using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Hospitality;

/// <summary>In the brawl at the inn (<see cref="BrawlCrisis"/>): swinging at whoever's nearest, till they're settled.</summary>
public struct Brawler
{
    public long Since;
}

/// <summary>
/// A brawl at the inn, the Director's crisis (docs/DESIGN.md, "Bar fights"). A rowdy crowd
/// (<see cref="PartyDef.Crowd"/>) comes up the road loud with drink, and once they're in the inn
/// one of them, the ringleader, throws the first punch. It's fists (the inn's house rule,
/// <see cref="HouseRules"/>), but it spreads: a guest struck by a brawler who's rowdy or drunk
/// enough joins in. Its heat climbs with how many are still at it, and at the top it boils over
/// into the street: not met. Each brawler is settled by being knocked down by someone not in it
/// (they sleep it off where they fall; one dropped by a mate gets back up and back at it); settle the
/// ringleader and those who aren't rowdy lose heart. Met when every brawler's settled, nobody's died, and it never boiled over.
/// Nobody's told who the ringleader is: it's the one who started it, and who shouts the others on.
/// </summary>
public sealed class BrawlCrisis : ICrisis
{
    public CrisisKind Kind => CrisisKind.Brawl;
    public long Started { get; private init; }

    private long _deadline;
    private long _lastCheck;
    private int _deadBefore;
    private Entity _inn;
    private readonly List<Entity> _crowd = [];
    private readonly List<Entity> _brawlers = [];

    /// <summary>Who started it. Shown to nobody; the reveal may name them.</summary>
    public Entity Ringleader { get; private set; }

    /// <summary>The first punch has been thrown.</summary>
    public bool Begun { get; private set; }

    /// <summary>How near it is to boiling over (0..1).</summary>
    public float Heat { get; private set; }

    /// <summary>Everyone who's been in it, the crowd and whoever joined.</summary>
    public IReadOnlyList<Entity> Brawlers => _brawlers;

    /// <summary>The crowd, as it came.</summary>
    public IReadOnlyList<Entity> Crowd => _crowd;

    public Entity Inn => _inn;

    /// <summary>A brawler's blow this recent counts as starting something with whoever it struck (game ticks).</summary>
    public static readonly long StruckWithin = SimClock.TicksPerHour / 4;

    internal static BrawlCrisis? Begin(Simulation sim, SimRandom rand, float strength)
    {
        var inn = ShopSystem.BuildingFor(sim, TownRole.Tavern);
        var crowds = sim.Defs.AllParties.Where(p => p.Crowd).ToList();
        if (inn.IsNull || crowds.Count == 0)
            return null;
        var def = crowds[rand.NextInt(crowds.Count)];
        var brawl = sim.Defs.Season.Brawl;
        var members = sim.Parties.Send(sim, def, Math.Max(3, (int)MathF.Round(brawl.Crowd * strength)));
        if (members.Count == 0)
            return null;
        var crisis = new BrawlCrisis
        {
            Started = sim.Clock.Tick,
            _deadline = sim.Clock.Tick + SimClock.HoursToTicks(sim.Defs.Season.CrisisHours),
            _lastCheck = sim.Clock.Tick,
            _deadBefore = Dead(sim),
            _inn = inn,
            Ringleader = members[rand.NextInt(members.Count)],
        };
        crisis._crowd.AddRange(members);
        foreach (var e in members)
            Drink(sim, e, brawl.ArriveTipsy);
        sim.Messages.Add(MessageKind.Danger, $"{Cap(def.Label)} come up the road, loud and already singing. Trouble, by the look of them.",
            sim.World.Get<Position>(members[0]).Cell);
        Barks.Say(sim, crisis.Ringleader, "brawl_crowd", Volume.Shout, "Landlord! Ale, and plenty of it!");
        return crisis;
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static void Drink(Simulation sim, Entity e, float level)
    {
        var w = sim.World;
        if (w.Has<Tipsy>(e))
            w.Get<Tipsy>(e).Level = Math.Max(w.Get<Tipsy>(e).Level, level);
        else
            w.Add(e, new Tipsy { Level = level });
    }

    /// <summary>The town's and its guests' dead (human): nobody's meant to die in a brawl.</summary>
    private static int Dead(Simulation sim)
    {
        var n = 0;
        foreach (var e in sim.World.Query<Creature>())
            if (sim.World.Get<Creature>(e) is { Dead: true, Def.Race: Race.Human, Faction: Faction.Village or Faction.Adventurer })
                n++;
        return n;
    }

    /// <summary>In the inn (the building, either storey).</summary>
    public bool InInn(Simulation sim, Entity e) =>
        sim.World.TryGet<Position>(e, out var p) && Buildings.At(sim, p.Cell) == _inn;

    /// <summary>Still fighting: in it, on their feet, not on their way.</summary>
    public static bool Fighting(Simulation sim, Entity e) =>
        sim.World.IsAlive(e) && sim.World.Has<Brawler>(e) && sim.World.Get<Creature>(e).Able && !sim.World.Has<Leaving>(e);

    public bool? Check(Simulation sim)
    {
        var w = sim.World;
        var brawl = sim.Defs.Season.Brawl;
        var now = sim.Clock.Tick;
        var hours = (now - _lastCheck) / (float)SimClock.TicksPerHour;
        var since = _lastCheck;
        _lastCheck = now;
        if (!w.IsAlive(_inn))
            return Over(sim, met: Dead(sim) == _deadBefore);
        if (!Begun)
        {
            if (_crowd.FirstOrDefault(e => w.IsAlive(e) && w.Get<Creature>(e).Able && !w.Has<Leaving>(e) && InInn(sim, e), Entity.Null) is { IsNull: false } first)
                Start(sim, first);
            else if (now >= _deadline || _crowd.All(e => !w.IsAlive(e) || !w.Get<Creature>(e).Able || w.Has<Leaving>(e)))
                return Over(sim, met: true); // they never got as far as the inn
            return null;
        }
        Join(sim, since);
        Settle(sim);
        var fighting = _brawlers.Count(e => Fighting(sim, e));
        Heat = Math.Clamp(Heat + (brawl.RisePerHour * fighting / Math.Max(1f, brawl.PerBrawlers) - brawl.CoolPerHour) * hours, 0f, 1f);
        if (Heat >= 1f)
        {
            sim.Messages.Add(MessageKind.Danger, "The brawl boils over and spills out into the street!", w.Get<PresetBuilding>(_inn).Anchor);
            if (_brawlers.FirstOrDefault(e => Fighting(sim, e), Entity.Null) is { IsNull: false } shouter)
                Barks.Say(sim, shouter, "brawl_spill", Volume.Shout, "Outside! The lot of you!");
            // A lamp goes over in the scuffle, now and then: the inn's alight as well.
            if (sim.Rand.NextFloat() < brawl.FireOnBoil && sim.Director.SendFire(sim, _inn) != null)
                sim.Messages.Add(MessageKind.Danger, "A lamp goes over in the scuffle, and the inn's alight!", w.Get<PresetBuilding>(_inn).Anchor);
            return Over(sim, met: false);
        }
        if (fighting == 0)
            return Over(sim, met: Dead(sim) == _deadBefore);
        if (now >= _deadline)
            return Over(sim, met: false);
        return null;
    }

    /// <summary>The first punch: the ringleader's if they're in, else whoever is.</summary>
    private void Start(Simulation sim, Entity first)
    {
        var w = sim.World;
        Begun = true;
        var thrower = InInn(sim, Ringleader) && w.Get<Creature>(Ringleader).Able ? Ringleader : first;
        foreach (var e in _crowd)
            if (w.IsAlive(e) && w.Get<Creature>(e).Able && !w.Has<Leaving>(e))
                Enlist(sim, e);
        var foe = FoeFor(sim, thrower);
        Barks.Say(sim, thrower, "brawl_start", Volume.Shout, "You looking at me?", who: foe.IsNull ? "" : w.Get<Creature>(foe).Name);
        sim.Messages.Add(MessageKind.Danger, $"A brawl breaks out in {Buildings.Called(sim, _inn)}!", w.Get<Position>(thrower).Cell);
        if (thrower == Ringleader)
            return;
        Barks.Say(sim, Ringleader, "brawl_rally", Volume.Shout, "Go on! Hit him!");
    }

    private void Enlist(Simulation sim, Entity e)
    {
        var w = sim.World;
        if (!w.Has<Brawler>(e))
            w.Add(e, new Brawler { Since = sim.Clock.Tick });
        if (!_brawlers.Contains(e))
            _brawlers.Add(e);
        AiSystem.Order(sim, e, new BrawlerJob());
    }

    /// <summary>Guests in the inn struck by a brawler since the last look: the rowdy and the drunk join in.</summary>
    private void Join(Simulation sim, long since)
    {
        var w = sim.World;
        var join = sim.Defs.Season.Brawl.JoinTipsy;
        foreach (var e in w.Query<Adventurer>())
        {
            if (w.Has<Brawler>(e) || _brawlers.Contains(e) || !w.Get<Creature>(e).Able || w.Has<Leaving>(e) || Players.Steered(sim, e)
                || !w.TryGet<Provoked>(e, out var p) || p.Tick < since - StruckWithin || !w.Has<Brawler>(p.By) || !InInn(sim, e))
                continue;
            if (Adventurers.Tempers.Of(sim, e) != Adventurers.Temper.Rowdy && DrinkingSystem.LevelOf(sim, e) < join)
                continue;
            Enlist(sim, e);
            Barks.Say(sim, e, "brawl_join", Volume.Shout, "Right, that's it!", who: w.Get<Creature>(p.By).Name);
        }
    }

    /// <summary>Who's done: knocked down by someone not in it. Anyone else still in it is set back to it.</summary>
    private void Settle(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in _brawlers)
        {
            if (!w.IsAlive(e) || !w.Has<Brawler>(e))
                continue;
            var c = w.Get<Creature>(e);
            // Knocked down by one of their own: they'll be back up and at it when they come to.
            if (c.Downed && !c.Dead && w.TryGet<Provoked>(e, out var p) && w.IsAlive(p.By) && w.Has<Brawler>(p.By))
                continue;
            if (c.Dead || c.Downed)
            {
                // Out cold: they'll sleep it off where they fall once they come to.
                Drink(sim, e, DrinkingSystem.OutAt + 0.3f);
                Settled(sim, e);
            }
            else if (w.TryGet<Mind>(e, out var m) && m.Job is not BrawlerJob && c.Able)
                AiSystem.Order(sim, e, new BrawlerJob()); // back up and back at it
        }
    }

    private void Settled(Simulation sim, Entity e)
    {
        var w = sim.World;
        w.Remove<Brawler>(e);
        if (w.TryGet<Mind>(e, out var m) && m.Job is BrawlerJob)
            AiSystem.EndJob(sim, e, m, false);
        if (e != Ringleader)
            return;
        // The one who started it is done: those who aren't spoiling for it lose heart.
        Heat = Math.Max(0f, Heat - sim.Defs.Season.Brawl.RingleaderCools);
        foreach (var other in _brawlers)
            if (other != e && Fighting(sim, other) && Adventurers.Tempers.Of(sim, other) != Adventurers.Temper.Rowdy)
            {
                w.Remove<Brawler>(other);
                if (w.TryGet<Mind>(other, out var om) && om.Job is BrawlerJob)
                    AiSystem.EndJob(sim, other, om, false);
                Barks.Say(sim, other, "brawl_settled", Volume.Say, "Ah, what's the use.");
            }
    }

    /// <summary>It's over: whoever's still at it is put out, and a crowd that wasn't settled leaves sour.</summary>
    private bool Over(Simulation sim, bool met)
    {
        var w = sim.World;
        foreach (var e in _brawlers)
            if (w.IsAlive(e) && w.Has<Brawler>(e))
            {
                w.Remove<Brawler>(e);
                if (!met && !w.Has<Leaving>(e))
                    w.Add(e, new Leaving());
            }
        if (!met)
            foreach (var e in _crowd)
                if (w.IsAlive(e) && w.Has<Adventurer>(e))
                {
                    ref var a = ref w.Get<Adventurer>(e);
                    a.Satisfaction = Math.Min(a.Satisfaction, sim.Defs.Season.Brawl.SourSatisfaction);
                    if (!w.Has<Leaving>(e))
                        w.Add(e, new Leaving());
                }
        return met;
    }

    /// <summary>Whom this brawler swings at: whoever last hit them, else the nearest other brawler, else any guest in the inn.</summary>
    public Entity FoeFor(Simulation sim, Entity e)
    {
        var w = sim.World;
        if (!w.TryGet<Position>(e, out var here))
            return Entity.Null;
        var at = here.Cell;
        if (w.TryGet<Provoked>(e, out var p) && p.By != e && sim.Clock.Tick - p.Tick < StruckWithin && w.IsAlive(p.By)
            && w.TryGet<Creature>(p.By, out var by) && by.Able && w.TryGet<Position>(p.By, out var byAt) && Maps.Map.Distance(byAt.Cell, at) <= 8
            && (!w.Has<Brawler>(p.By) || InInn(sim, p.By))) // a mate is fought where the fight is; anyone else, wherever they ran
            return p.By;
        var best = Entity.Null;
        var bestD = int.MaxValue;
        foreach (var o in _brawlers)
            if (o != e && Fighting(sim, o) && InInn(sim, o) && w.TryGet<Position>(o, out var op) && op.Cell is var c && c.Z == at.Z && Maps.Map.Distance(c, at) is var d && d < bestD)
                (best, bestD) = (o, d);
        if (!best.IsNull)
            return best;
        foreach (var o in w.Query<Adventurer>())
            if (o != e && w.Get<Creature>(o).Able && !w.Has<Leaving>(o) && InInn(sim, o)
                && w.Get<Position>(o).Cell is var c && c.Z == at.Z && Maps.Map.Distance(c, at) is var d && d < bestD)
                (best, bestD) = (o, d);
        return best;
    }

    public string Outcome(bool met) => met
        ? "The brawl's done with: the inn's quiet again, if not tidy."
        : "The brawl got the better of the town, and the crowd's gone off cursing it.";

    public void Save(SaveWriter w)
    {
        w.Long(Started);
        w.Long(_deadline);
        w.Long(_lastCheck);
        w.Int(_deadBefore);
        w.Entity(_inn);
        w.Entity(Ringleader);
        w.Bool(Begun);
        w.Float(Heat);
        w.Int(Smashed);
        w.Int(_crowd.Count);
        foreach (var e in _crowd)
            w.Entity(e);
        w.Int(_brawlers.Count);
        foreach (var e in _brawlers)
            w.Entity(e);
    }

    internal static BrawlCrisis Read(SaveReader r)
    {
        var crisis = new BrawlCrisis
        {
            Started = r.Long(), _deadline = r.Long(), _lastCheck = r.Long(), _deadBefore = r.Int(), _inn = r.Entity(),
            Ringleader = r.Entity(), Begun = r.Bool(), Heat = r.Float(), Smashed = r.Int(),
        };
        for (var n = r.Count(100); n > 0; n--)
            crisis._crowd.Add(r.Entity());
        for (var n = r.Count(1000); n > 0; n--)
            crisis._brawlers.Add(r.Entity());
        return crisis;
    }

    /// <summary>Hot enough that they're going for the furniture.</summary>
    public bool Smashing(Simulation sim) => Begun && Heat >= sim.Defs.Season.Brawl.BreakAt;

    /// <summary>A piece the inn's brawl can smash, standing in the inn.</summary>
    public bool Breakable(Simulation sim, Entity thing) =>
        sim.World.TryGet<Thing>(thing, out var t) && t.Def.BreaksInBrawls && Buildings.At(sim, t.Cell) == _inn;

    /// <summary>The nearest piece to smash (or, <paramref name="toThrow"/>, to hurl) within <paramref name="reach"/> cells of a brawler, or Entity.Null.</summary>
    public Entity FurnitureNear(Simulation sim, Entity e, int reach, bool toThrow = false)
    {
        var w = sim.World;
        if (!w.TryGet<Position>(e, out var p))
            return Entity.Null;
        foreach (var c in sim.SpiralFrom(p.Cell, reach))
            if (c.Z == p.Cell.Z && sim.Map.InBounds(c) && sim.Map.Structure[sim.Map.Idx(c)] is { IsNull: false } s && Breakable(sim, s)
                && (!toThrow || w.Get<Thing>(s).Def.ThrownInBrawls))
                return s;
        return Entity.Null;
    }

    /// <summary>
    /// A brawler's blow that found nobody (<see cref="Blows"/>): once it's hot enough, the table or
    /// stool in its sweep takes it, and a few such and it's matchwood: gone, and the carpenter's to
    /// make again (<see cref="Modding.Trades.Lost"/>). The piece struck, or Entity.Null.
    /// </summary>
    public static Entity Smash(Simulation sim, Entity attacker, AttackDef attack, float aim)
    {
        var w = sim.World;
        if (!w.Has<Brawler>(attacker) || Of(sim) is not { } brawl || !brawl.Smashing(sim))
            return Entity.Null;
        var piece = Reach.ThingInSweep(sim, attacker, attack, aim, d => d.BreaksInBrawls);
        if (piece.IsNull || !brawl.Breakable(sim, piece))
            return Entity.Null;
        brawl.Batter(sim, piece, 1, $"{w.Get<Creature>(attacker).Name} smashes a {{0}} to matchwood!");
        return piece;
    }

    /// <summary>
    /// A brawler's punch landed on <paramref name="target"/> (<see cref="Blows"/>): once it's hot, now
    /// and then it sends them into the table or stool just behind them, which takes it twice over.
    /// </summary>
    public static void Knocked(Simulation sim, Entity attacker, Entity target, float aim)
    {
        var w = sim.World;
        if (!w.Has<Brawler>(attacker) || Of(sim) is not { } brawl || !brawl.Smashing(sim) || !w.TryGet<Position>(target, out var at)
            || sim.Rand.NextFloat() >= sim.Defs.Season.Brawl.ShoveChance)
            return;
        var dir = new float2(DetMath.Cos(aim), DetMath.Sin(aim));
        foreach (var reach in (ReadOnlySpan<float>)[0.7f, 1.3f])
        {
            var c = new int3((int)MathF.Floor(at.Pos.X + dir.X * reach), (int)MathF.Floor(at.Pos.Y + dir.Y * reach), at.Cell.Z);
            if (!sim.Map.InBounds(c) || sim.Map.Structure[sim.Map.Idx(c)] is not { IsNull: false } piece || !brawl.Breakable(sim, piece))
                continue;
            brawl.Batter(sim, piece, 2, $"{w.Get<Creature>(target).Name} goes over backwards into a {{0}}, and it gives way!");
            return;
        }
    }

    /// <summary>How far (cells) a stool's hurled.</summary>
    public const int ThrowRange = 6;

    /// <summary>
    /// <paramref name="thrower"/> picks up <paramref name="stool"/> and hurls it at <paramref name="at"/>:
    /// it's gone from where it stood (the carpenter's to make again), flies, and breaks over them.
    /// False if it can't be thrown at them from here (it's put down again, as it was).
    /// </summary>
    public bool Hurl(Simulation sim, Entity thrower, Entity stool, Entity at)
    {
        var w = sim.World;
        if (!Breakable(sim, stool) || !w.IsAlive(at) || !w.Get<Creature>(at).Able || !w.TryGet<Position>(at, out var them))
            return false;
        var me = w.Get<Position>(thrower);
        if (Maps.Map.Distance(me.Cell, them.Cell) > ThrowRange || me.Cell.Z != them.Cell.Z
            || !CombatRules.LineOfFire(sim, me.Pos + new float3(0, 0, 0.5f), them.Pos + new float3(0, 0, 0.5f)))
            return false;
        var def = w.Get<Thing>(stool).Def;
        Gone(sim, stool);
        var d = new float2(them.Pos.X - me.Pos.X, them.Pos.Y - me.Pos.Y);
        Hurling.Fly(sim, thrower, def, DetMath.Atan2(d.Y, d.X), MathF.Sqrt(d.X * d.X + d.Y * d.Y) + 0.5f, 1f);
        return true;
    }

    /// <summary>A piece takes <paramref name="hits"/> blows' worth; broken, it's gone (<paramref name="told"/>, its label at {0}).</summary>
    private void Batter(Simulation sim, Entity piece, int hits, string told)
    {
        ref var t = ref sim.World.Get<Thing>(piece);
        t.Hp -= hits * t.MaxHp / Math.Max(1, sim.Defs.Season.Brawl.BlowsToBreak) + 0.01f;
        if (t.Hp > 0)
            return;
        sim.Messages.Add(MessageKind.Warning, string.Format(System.Globalization.CultureInfo.InvariantCulture, told, t.Def.Label), t.Cell);
        Gone(sim, piece);
    }

    /// <summary>A piece from <paramref name="cell"/> broken by someone not in it (a player's thrown stool): counted with the rest if it was the inn's.</summary>
    public void Broke(Simulation sim, int3 cell)
    {
        if (Buildings.At(sim, cell) == _inn)
            Smashed++;
    }

    /// <summary>A piece gone from the inn: the carpenter's to set back (<see cref="Modding.Trades.Lost"/>).</summary>
    private void Gone(Simulation sim, Entity piece)
    {
        var t = sim.World.Get<Thing>(piece);
        sim.DestroyThing(piece);
        sim.Map.MarkStructureChanged();
        Modding.Trades.Lost(sim, t.Def, t.Cell, t.Facing);
        Smashed++;
    }

    /// <summary>Pieces smashed so far.</summary>
    public int Smashed { get; private set; }

    /// <summary>The brawl under way, if any.</summary>
    public static BrawlCrisis? Of(Simulation sim) => sim.Director.Active.OfType<BrawlCrisis>().FirstOrDefault();
}

/// <summary>
/// A brawler in the inn's brawl (<see cref="BrawlCrisis"/>): at whoever's to be hit
/// (<see cref="BrawlCrisis.FoeFor"/>) with fists, stepping in and out and circling as any fight goes
/// (<see cref="Job.FightFree"/>), a few seconds at a time before looking round again. Once it's hot,
/// now and then they grab a stool and hurl it, or take it out on a table.
/// </summary>
public sealed class BrawlerJob : Job
{
    /// <summary>Ticks between swings at the furniture.</summary>
    public const int Between = 80;

    /// <summary>How far (cells) a brawler will go for a table to smash or a stool to throw.</summary>
    public const int SmashReach = 8;

    /// <summary>How long (ticks) they go at one foe before looking round again.</summary>
    public const int Bout = SimClock.TicksPerSecond * 5;

    /// <summary>The share of their swings that are haymakers.</summary>
    public const float Haymakers = 0.15f;

    private Entity _foe;

    public override Entity Focus => _foe;

    protected override IEnumerable<JobStatus> Run()
    {
        var fists = Get<Creature>(Pawn).Def.Attacks.FirstOrDefault(a => !a.Ranged);
        if (fists == null)
        {
            Fail("no fists");
            yield break;
        }
        var tuning = Sim.Defs.Season.Brawl;
        while (BrawlCrisis.Fighting(Sim, Pawn) && BrawlCrisis.Of(Sim) is { } brawl)
        {
            // Hot enough, now and then they grab a stool to hurl, or take it out on a table.
            var roll = Sim.Rand.NextFloat();
            if (brawl.Smashing(Sim) && roll < tuning.ThrowChance && brawl.FurnitureNear(Sim, Pawn, SmashReach, toThrow: true) is { IsNull: false } stool)
            {
                Combat.FreeFight.Leave(Sim, Pawn);
                Report = $"Grabbing a {Get<Thing>(stool).Def.Label}";
                foreach (var s in GoTo(Get<Thing>(stool).Cell, PathEnd.Touch)) yield return s;
                if (HasFailed)
                    yield break;
                if (brawl.FoeFor(Sim, Pawn) is { IsNull: false } target && Sim.World.IsAlive(stool))
                {
                    _foe = target;
                    Report = $"Hurling a {Get<Thing>(stool).Def.Label} at {Get<Creature>(target).Name}";
                    foreach (var s in Wait(SimClock.TicksPerSecond / 2)) yield return s; // heaved up
                    if (Sim.World.IsAlive(stool))
                        brawl.Hurl(Sim, Pawn, stool, target);
                }
                continue;
            }
            if (brawl.Smashing(Sim) && roll < tuning.ThrowChance + tuning.SmashChance
                && brawl.FurnitureNear(Sim, Pawn, SmashReach) is { IsNull: false } piece)
            {
                Combat.FreeFight.Leave(Sim, Pawn);
                var at = Get<Thing>(piece).Cell;
                Report = $"Smashing a {Get<Thing>(piece).Def.Label}";
                foreach (var s in GoTo(at, PathEnd.Touch)) yield return s;
                if (HasFailed)
                    yield break;
                if (Sim.World.IsAlive(piece))
                {
                    var me = Get<Position>(Pawn).Pos;
                    Blows.Begin(Sim, Pawn, Entity.Null, fists, aim: DetMath.Atan2(at.Y + 0.5f - me.Y, at.X + 0.5f - me.X));
                    foreach (var s in Wait(Between)) yield return s;
                }
                continue;
            }
            _foe = brawl.FoeFor(Sim, Pawn);
            if (_foe.IsNull)
            {
                Report = "Spoiling for a fight";
                foreach (var s in Wait(SimClock.TicksPerSecond * 2)) yield return s;
                yield break; // think again
            }
            Report = $"Brawling with {Get<Creature>(_foe).Name}";
            // A few seconds at them, footwork and all (among themselves the punches are pulled:
            // HouseRules.Pulled), then a look round for who's next.
            var until = Sim.Clock.Tick + Bout;
            var foe = _foe;
            foreach (var s in FightFree(foe, fists, () => BrawlCrisis.Fighting(Sim, Pawn) && Sim.Clock.Tick < until && BrawlCrisis.Of(Sim) == brawl,
                Haymakers, giveUp: SmashReach + 4))
                yield return s;
            if (HasFailed)
                yield break;
            if (Sim.Rand.NextFloat() < 0.15f)
                Sim.Mess.Spill(Sim, MessKind.SpilledAle, PawnCell, 0.3f); // a tankard goes over
            if (Sim.Clock.Tick < until)
                foreach (var s in Wait(SimClock.TicksPerSecond / 2)) yield return s; // they broke off: a breath
        }
    }
}
