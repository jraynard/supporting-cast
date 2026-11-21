using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// A fire that goes out when it's left (<see cref="Defs.FuelProps.GoesCold"/>: the forge). Unworked for
/// <see cref="Kindling.ColdAfterHours"/> it's cold by morning, and gives no heat, light or glow
/// (<see cref="Simulation.Burning"/>) till it's lit again by hand (<see cref="KindleSession"/>). Banked at
/// close (ash over the coals, <see cref="Kindling.Bank"/>) it keeps through the night. Plain to see: a cold
/// forge is dark.
/// </summary>
public struct Hearth
{
    public bool Cold;

    /// <summary>When it last burned (worked, or wanting to be).</summary>
    public long LastBurned;

    /// <summary>Banked till then: it won't go cold before.</summary>
    public long BankedUntil;
}

/// <summary>
/// Opening the shop: the smith's cold start. A fire that goes cold (<see cref="Hearth"/>) is lit with flint,
/// steel and breath (<see cref="KindleSession"/>), F at it once it's cold and has fuel in; banked at close it
/// keeps till morning. Rounds open with these fires cold, so the first morning starts with lighting them.
/// An AI keeper lights its own plainly (<see cref="KindleJob"/>).
/// </summary>
public static class Kindling
{
    /// <summary>Hours a fire that goes cold keeps alight with nobody working it.</summary>
    public const float ColdAfterHours = 6f;

    /// <summary>Hours a banked fire keeps: the night, and into the morning.</summary>
    public const float BankedHours = 16f;

    /// <summary>The dead of night, when a fire nobody banked dies down to nothing: hours from and to.</summary>
    public const int NightFrom = 2, NightTo = 5;

    public static bool GoesCold(Simulation sim, Entity e) =>
        sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Building?.Fuel is { } fuel
        && (fuel.GoesCold || (fuel.GoesColdAtWork && AtWork(sim, e)));

    /// <summary>In a building some trade works from (the inn, the shrine), not a cottage.</summary>
    public static bool AtWork(Simulation sim, Entity e)
    {
        var building = Buildings.Of(sim, e);
        return !building.IsNull && sim.World.TryGet<PresetBuilding>(building, out var shop) && sim.Defs.AllTrades.Exists(t => t.WorksAt(shop.Def));
    }

    /// <summary>The hour from which a keeper banks the fire for the night (an inn's, so its lodgers sleep warm).</summary>
    public const int BankFrom = 20;

    /// <summary>Evening into the night, before the dead of it: when fires are banked.</summary>
    public static bool BankingTime(Simulation sim) => sim.Clock.HourOfDay >= BankFrom || sim.Clock.HourOfDay < NightFrom;

    /// <summary>Alight and not yet banked, in the evening: one to bank before the night.</summary>
    public static bool WantsBanking(Simulation sim, Entity e) =>
        BankingTime(sim) && sim.World.TryGet<Hearth>(e, out var h) && !h.Cold && h.BankedUntil <= sim.Clock.Tick;

    /// <summary>The dead of night (<see cref="NightFrom"/> to <see cref="NightTo"/>): unbanked fires go out, and nobody lights one.</summary>
    public static bool DeadOfNight(Simulation sim) => sim.Clock.HourOfDay >= NightFrom && sim.Clock.HourOfDay < NightTo;

    public static bool IsCold(Simulation sim, Entity e) => sim.World.TryGet<Hearth>(e, out var h) && h.Cold;

    public static bool IsBanked(Simulation sim, Entity e) =>
        sim.World.TryGet<Hearth>(e, out var h) && !h.Cold && h.BankedUntil > sim.Clock.Tick;

    /// <summary>The fire's out and it has fuel in: F at it lights it.</summary>
    public static bool CanLight(Simulation sim, Entity e) =>
        IsCold(sim, e) && sim.World.TryGet<Fueled>(e, out var f) && f.Fuel > 0;

    /// <summary>What F at a cold fire does, for the prompt: null when it isn't one to light.</summary>
    public static string? StepAt(Simulation sim, Entity avatar, Entity site) =>
        !site.IsNull && CanLight(sim, site) && !sim.World.Has<WorkSession>(avatar) ? $"Light the {sim.World.Get<Thing>(site).Label}" : null;

    /// <summary>Begin lighting it, if it's cold, fuelled and in reach.</summary>
    public static void Start(Simulation sim, Entity avatar, Entity site)
    {
        if (!CanLight(sim, site) || sim.World.Has<WorkSession>(avatar)
            || !AvatarSystem.InReach(sim, sim.World.Get<Position>(avatar).Cell, site))
            return;
        WorkSystem.Start(sim, avatar, new KindleSession { Station = site, Label = sim.World.Get<Thing>(site).Label });
    }

    /// <summary>Lit: it burns again from now.</summary>
    public static void Light(Simulation sim, Entity e)
    {
        if (!sim.World.Has<Hearth>(e))
            return;
        ref var h = ref sim.World.Get<Hearth>(e);
        h.Cold = false;
        h.LastBurned = sim.Clock.Tick;
    }

    /// <summary>Lit: every fire that goes cold (the practice yard keeps its fires going for you; a drill stages work at a lit one).</summary>
    public static void LightAll(Simulation sim)
    {
        foreach (var e in sim.World.Query<Fueled>())
            if (GoesCold(sim, e))
            {
                if (!sim.World.Has<Hearth>(e))
                    sim.World.Add(e, new Hearth());
                Light(sim, e);
            }
    }

    /// <summary>Put out: every fire that goes cold, at a round's opening (the first morning starts by lighting them).</summary>
    public static void PutOutAll(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Query<Fueled>())
        {
            if (!GoesCold(sim, e))
                continue;
            if (!w.Has<Hearth>(e))
                w.Add(e, new Hearth());
            ref var h = ref w.Get<Hearth>(e);
            h.Cold = true;
            h.BankedUntil = 0;
        }
    }

    /// <summary>Ash raked over the coals: it keeps till morning. Only a lit fire, by someone at it.</summary>
    public static bool Bank(Simulation sim, Entity who, Entity e)
    {
        var w = sim.World;
        if (!w.Has<Hearth>(e) || IsCold(sim, e) || !AvatarSystem.InReach(sim, w.Get<Position>(who).Cell, e))
            return false;
        ref var h = ref w.Get<Hearth>(e);
        h.BankedUntil = sim.Clock.Tick + SimClock.HoursToTicks(BankedHours);
        h.LastBurned = sim.Clock.Tick;
        sim.Messages.Tell(who, MessageKind.Info, $"You rake ash over the coals: the {w.Get<Thing>(e).Label} will keep till morning.", w.Get<Thing>(e).Cell);
        return true;
    }
}

/// <summary>
/// Keeps each <see cref="Hearth"/>: one is added to a fire that goes cold the first time it's seen. A fire goes out,
/// unless it's banked, when it's gone unworked past <see cref="Kindling.ColdAfterHours"/> (a forge left), and in the
/// dead of night (a hearth that burns all day): every workplace's morning opens with lighting its fires.
/// </summary>
public sealed class KindlingSystem : ISimSystem
{
    public string Name => "Kindling";
    public int Interval => SimClock.TicksPerHour / 12;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        var now = sim.Clock.Tick;
        var after = SimClock.HoursToTicks(Kindling.ColdAfterHours);
        foreach (var e in w.Query<Fueled>())
        {
            if (!Kindling.GoesCold(sim, e))
                continue;
            if (!w.Has<Hearth>(e))
            {
                w.Add(e, new Hearth { LastBurned = now });
                continue;
            }
            ref var h = ref w.Get<Hearth>(e);
            if (h.Cold)
                continue;
            var banked = now <= h.BankedUntil;
            if (sim.Burning(e))
                h.LastBurned = now;
            if (!banked && (now - h.LastBurned > after || Kindling.DeadOfNight(sim)))
                h.Cold = true;
        }
    }
}

/// <summary>
/// Lighting a cold fire. First flint on steel: the steel swings, and F as it comes down through the strike
/// throws a spark, which the tinder may catch (the truer the strike, the likelier). Then breath: hold F to
/// blow on the ember. A short breath is gentle and brings the glow up; held, it grows to a gust that blows it
/// out; left alone, it smoulders down. And breath runs out, so it's a rhythm of puffs. When the glow's up,
/// it's flame, and the fire is lit.
/// </summary>
public sealed class KindleSession : WorkSession
{
    /// <summary>Seconds a swing of the steel takes.</summary>
    public const float SwingSeconds = 1.2f;

    /// <summary>Where on its swing the steel strikes the flint (0 raised, 1 down), and how far either side still throws a spark.</summary>
    public const float StrikeAt = 0.85f, StrikeHalfWidth = 0.13f;

    /// <summary>How the breath builds while held (to a gust) and falls away once let go, per step.</summary>
    public const float DraftRise = 0.022f, DraftFall = 0.06f;

    /// <summary>The draft that feeds an ember, and the one that blows it out.</summary>
    public const float GentleLo = 0.15f, GentleHi = 0.6f, GustAt = 0.82f;

    /// <summary>The glow a caught spark starts at, what a gentle breath adds a step, what a gust takes, what idling loses.</summary>
    public const float CaughtGlow = 0.22f, Feed = 0.006f, Gust = 0.02f, Smoulder = 0.0016f;

    /// <summary>Lungs: what a step of blowing spends and a step of breathing in gets back.</summary>
    public const float BreathSpent = 0.012f, BreathBack = 0.02f;

    public Entity Station { get; set; } = Entity.Null;
    public string Label { get; set; } = "forge";

    /// <summary>An ember's caught: blowing now, not striking.</summary>
    public bool Ember { get; private set; }

    /// <summary>How bright the ember is, 0 (dead) to 1 (flame).</summary>
    public float Glow { get; private set; }

    /// <summary>How hard the breath is coming now, 0 to 1 (past <see cref="GustAt"/> it blows the ember out).</summary>
    public float Draft { get; private set; }

    /// <summary>Breath left in the lungs, 0 to 1.</summary>
    public float Breath { get; private set; } = 1f;

    public int Strikes { get; private set; }
    public int Sparks { get; private set; }

    /// <summary>Embers lost: blown out or let die.</summary>
    public int Lost { get; private set; }

    /// <summary>Lit: flame.</summary>
    public bool Caught { get; private set; }

    /// <summary>How the last strike landed: 0 not yet, 1 a spark caught, 2 a spark that didn't catch, 3 missed the flint.</summary>
    public int LastStrike { get; private set; }

    private int _swingFrom;

    /// <summary>The steel's swing, 0 (raised) to 1 (down past the flint), while striking.</summary>
    public float Steel => Triangle(StepsDone - _swingFrom, SwingSeconds);

    /// <summary>The steel's coming down through the strike: F now throws a spark.</summary>
    public bool OnTheStrike => MathF.Abs(Steel - StrikeAt) <= StrikeHalfWidth;

    /// <summary>The breath's a gentle one: the ember takes it.</summary>
    public bool Gentle => Held && Draft >= GentleLo && Draft <= GentleHi;

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Station) && sim.World.TryGet<Thing>(Station, out var t) ? t.Cell : null;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => 10f;
    public override string Title => Ember ? $"Blowing the {Label}'s ember to life" : $"Striking a spark for the {Label}";
    public override bool HeardByOthers => true;

    public override bool StillValid(Simulation sim) =>
        sim.World.IsAlive(Station) && Kindling.IsCold(sim, Station)
        && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Station);

    protected override void Press()
    {
        if (Ember)
            return; // blowing is the hold, not a press
        Strikes++;
        var off = MathF.Abs(Steel - StrikeAt);
        if (off > StrikeHalfWidth)
        {
            LastStrike = 3;
            return;
        }
        Sparks++;
        // The truer the strike, the fatter the spark and the likelier the tinder takes it.
        var truth = 1f - off / StrikeHalfWidth;
        if (Rand.NextFloat() < 0.25f + 0.55f * truth + 0.15f * Hands.Precision)
        {
            (Ember, Glow, LastStrike) = (true, CaughtGlow, 1);
            Draft = 0f;
        }
        else
            LastStrike = 2;
    }

    protected override void Step()
    {
        if (!Ember)
            return;
        var blowing = Held && Breath > 0f;
        Draft = blowing ? Math.Min(1f, Draft + DraftRise) : Math.Max(0f, Draft - DraftFall);
        Breath = blowing ? Math.Max(0f, Breath - BreathSpent) : Math.Min(1f, Breath + BreathBack);
        if (Draft > GustAt)
            Glow -= Gust; // too hard: it scatters
        else if (blowing && Draft >= GentleLo)
            Glow += Feed * (Draft <= GentleHi ? 1f : 0.3f);
        else
            Glow -= Smoulder;
        if (Glow >= 1f)
        {
            (Glow, Caught, Done) = (1f, true, true);
            Sample(Math.Clamp(1f - Lost * 0.2f - Math.Max(0, Strikes - 1) * 0.05f, 0.2f, 1f));
        }
        else if (Glow <= 0f)
        {
            // Out: back to the flint.
            (Glow, Ember, Draft) = (0f, false, 0f);
            Lost++;
            _swingFrom = StepsDone;
        }
    }

    public override void Finish(Simulation sim)
    {
        if (!Caught || !sim.World.IsAlive(Station))
            return;
        Kindling.Light(sim, Station);
        sim.Messages.Tell(Worker, MessageKind.Info, $"The {Label} catches, and the fire's up.", sim.World.Get<Thing>(Station).Cell);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Station);
        w.String(Label);
        w.Bool(Ember);
        w.Float(Glow);
        w.Float(Draft);
        w.Float(Breath);
        w.Int(Strikes);
        w.Int(Sparks);
        w.Int(Lost);
        w.Bool(Caught);
        w.Int(LastStrike);
        w.Int(_swingFrom);
    }

    protected override void ReadState(SaveReader r)
    {
        Station = r.Entity();
        Label = r.String();
        Ember = r.Bool();
        Glow = Math.Clamp(r.Float(), 0f, 1f);
        Draft = Math.Clamp(r.Float(), 0f, 1f);
        Breath = Math.Clamp(r.Float(), 0f, 1f);
        Strikes = Math.Max(0, r.Int());
        Sparks = Math.Max(0, r.Int());
        Lost = Math.Max(0, r.Int());
        Caught = r.Bool();
        LastStrike = Math.Clamp(r.Int(), 0, 3);
        _swingFrom = Math.Max(0, r.Int());
    }
}

/// <summary>
/// An AI keeper lights its own cold fire, flint, steel and breath, done plainly: a forge or kiln when there's work
/// wanting it, a hearth or oven whenever it's out (but not in the dead of night, when it would only go out again).
/// </summary>
public sealed class KindleJob(Entity fire) : Job
{
    /// <summary>Seconds it takes a practised hand.</summary>
    public const float Seconds = 12f;

    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (Kindling.DeadOfNight(sim))
            return null;
        foreach (var e in w.Query<Hearth>())
        {
            if (!Kindling.CanLight(sim, e) || sim.Reservations.IsReservedByOther(e, pawn) || !WorkGivers.MayWorkAt(sim, pawn, e))
                continue;
            var thing = w.Get<Thing>(e);
            if (thing.Def.Building?.Fuel is { OnDemand: true } && !Crafting.WantsFiring(sim, e))
                continue; // a forge with nothing to make stays cold
            if (!WorkGivers.FeedsHere(sim, pawn, e, thing))
                continue; // the kitchen's fires are its cook's
            if (!sim.Regions.CanReach(w.Get<Position>(pawn).Cell, w.Get<Thing>(e).Cell, PathEnd.Touch))
                continue;
            return new KindleJob(e);
        }
        return null;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        Report = $"Lighting the {Get<Thing>(fire).Def.Label}";
        if (!Reserve(fire)) yield break;
        foreach (var s in GoToBeside(fire)) yield return s;
        if (HasFailed || !Alive(fire)) yield break;
        foreach (var s in Wait((int)(Seconds * SimClock.TicksPerSecond))) yield return s;
        if (!Alive(fire)) yield break;
        Kindling.Light(Sim, fire);
    }
}

/// <summary>An AI keeper banks its workplace's fires of an evening, so they keep (and an inn's lodgers sleep warm).</summary>
public sealed class BankFireJob(Entity fire) : Job
{
    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!Kindling.BankingTime(sim))
            return null;
        foreach (var e in w.Query<Hearth>())
        {
            if (!Kindling.WantsBanking(sim, e) || sim.Reservations.IsReservedByOther(e, pawn) || !WorkGivers.MayWorkAt(sim, pawn, e)
                || !WorkGivers.FeedsHere(sim, pawn, e, w.Get<Thing>(e)))
                continue;
            if (!sim.Regions.CanReach(w.Get<Position>(pawn).Cell, w.Get<Thing>(e).Cell, PathEnd.Touch))
                continue;
            return new BankFireJob(e);
        }
        return null;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        Report = $"Banking the {Get<Thing>(fire).Def.Label}";
        if (!Reserve(fire)) yield break;
        foreach (var s in GoToBeside(fire)) yield return s;
        if (HasFailed || !Alive(fire)) yield break;
        foreach (var s in Wait(SimClock.TicksPerSecond * 2)) yield return s;
        if (Alive(fire))
            Kindling.Bank(Sim, Pawn, fire);
    }
}

/// <summary>Bank a lit fire for the night (<see cref="Kindling.Bank"/>): from the forge's window.</summary>
public sealed record BankFireCommand(Entity Avatar, Entity Fire) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!sim.World.IsAlive(Fire) || !sim.World.Has<Thing>(Fire) || !Kindling.GoesCold(sim, Fire))
            return;
        Kindling.Bank(sim, Avatar, Fire);
    }
}

/// <summary>Registers the fire's keeping (its system and its saved state) and the drill, as a mod would.</summary>
internal sealed class KindlingContent : ISimMod
{
    public void Register(SimRegistry registry)
    {
        registry.System(() => new KindlingSystem());
        registry.Component<Hearth>("Hearth", (w, h) =>
        {
            w.Bool(h.Cold);
            w.Long(h.LastBurned);
            w.Long(h.BankedUntil);
        }, r => new Hearth { Cold = r.Bool(), LastBurned = r.Long(), BankedUntil = r.Long() });
        // --drill kindle: the smith at a cold forge with fuel in, F to light it.
        registry.Drill("kindle", Scenarios.KindleDrill.Stage);
        // --drill kindling: the same, the flint already in hand.
        registry.Drill("kindling", (sim, me) =>
        {
            var fire = Scenarios.KindleDrill.Stage(sim, me);
            if (!fire.IsNull)
                Kindling.Start(sim, me, fire);
            return fire;
        });
    }
}
