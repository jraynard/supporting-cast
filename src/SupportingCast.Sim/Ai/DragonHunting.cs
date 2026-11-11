using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Ai;

/// <summary>
/// A grown dragon feeds itself when nobody has left it meat: it takes wild game that won't fight
/// back (or a fresh carcass lying about) and eats on the spot. Without this a lone rider couldn't keep an adult
/// fed (it eats <c>hungerFactor</c> 2.5 times what a hatchling does). Only life stages with
/// <see cref="Defs.LifeStage.HuntsAlone"/> do it, and only after meat already on the ground:
/// the village's own stores come first (<see cref="DragonBrain"/>).
/// </summary>
public static class DragonHunting
{
    /// <summary>How far (cells) a dragon looks for game on foot, and on the wing (herds graze out by the map's edges).</summary>
    public const int SearchRadius = 48, FlyingSearchRadius = 100;

    /// <summary>A fresh carcass counts as this many cells nearer than live game: no chase needed.</summary>
    public const int CarcassBonus = 12;

    /// <summary>Carcasses further gone than this are left for the crows.</summary>
    public const float MaxCarcassRot = 0.6f;

    /// <summary>Share of an animal's meat and hide a dragon's kill yields (it's no butcher).</summary>
    public const float KillYield = 0.8f;

    /// <summary>Below this much food a dragon told never to hunt hunts anyway rather than starve.</summary>
    public const float StarvingBelow = 0.1f;

    /// <summary>A dragon's hunting orders (the default if it has none).</summary>
    public static HuntPolicy PolicyOf(Simulation sim, Entity dragon) =>
        sim.World.TryGet<HuntPolicy>(dragon, out var p) ? p : default;

    /// <summary>
    /// A meal for a dragon that feeds itself, in the order its <see cref="HuntPolicy"/> asks: what's
    /// laid out, then a hunt (the default); a hunt, then stores; or stores only, unless starving.
    /// </summary>
    public static Job? Meal(Simulation sim, Entity dragon, CreatureDef def)
    {
        var starving = sim.World.Get<Needs>(dragon).Get(sim.Defs.Need("Food")) < StarvingBelow;
        return PolicyOf(sim, dragon).Mode switch
        {
            HuntMode.HuntFirst => Hunt(sim, dragon) ?? HumanBrain.EatSomething(sim, dragon, def),
            HuntMode.Never => HumanBrain.EatSomething(sim, dragon, def) ?? (starving ? Hunt(sim, dragon, starving: true) : null),
            _ => HumanBrain.EatSomething(sim, dragon, def) ?? Hunt(sim, dragon),
        };
    }

    /// <summary>
    /// A hunt for the nearest wild animal or fresh carcass, or null. Side-effect free (no
    /// reserving), like every finder, and in a fixed order (entity order, nearest wins).
    /// Its <see cref="HuntPolicy"/> may forbid hunting (unless <paramref name="starving"/>), keep it
    /// near its nest, or let it take on game that fights back.
    /// </summary>
    public static Job? Hunt(Simulation sim, Entity dragon, bool starving = false)
    {
        var w = sim.World;
        if (w.Has<Mount>(dragon) || !w.Get<Creature>(dragon).Stage(sim.Clock.Tick).HuntsAlone
            || Combat.CombatRules.Attacks(sim, dragon).FirstOrDefault(a => !a.Ranged) == null)
            return null;
        var policy = PolicyOf(sim, dragon);
        if (policy.Mode == HuntMode.Never && !starving)
            return null;
        int3? home = null;
        if (policy.Range > 0 && !starving && AiQueries.FindFreeBed(sim, dragon, nest: true) is { IsNull: false } nest)
            home = w.Get<Thing>(nest).Cell;
        var from = w.Get<Position>(dragon).Cell;
        var radius = Navigation.FlyingStage(sim, dragon) != null ? FlyingSearchRadius : SearchRadius;
        var best = Entity.Null;
        var bestScore = int.MaxValue;
        foreach (var e in w.Each<Creature>())
        {
            var c = w.Get<Creature>(e);
            if (c.Def.Animal == null || c.Faction != Faction.Wild || w.Has<Carried>(e) || sim.Reservations.IsReservedByOther(e, dragon))
                continue;
            // Game that fights back (a boar) is left alone: its tusks leave wounds that bleed out
            // with nobody to tend them. Its carcass is fair game.
            if (!c.Dead && c.Def.Animal.Aggressive && !policy.Dangerous)
                continue;
            if (c.Dead && (!w.TryGet<Rot>(e, out var rot) || rot.Progress > MaxCarcassRot))
                continue;
            if (!c.Dead && c.Downed)
                continue; // downed, not dead: leave it to whoever brought it down
            var at = w.Get<Position>(e).Cell;
            var d = Map.Distance(from, at);
            if (d > radius || (home is { } h && Map.Distance(h, at) > policy.Range))
                continue;
            var score = c.Dead ? d - CarcassBonus : d;
            if (score < bestScore && sim.Map.Fire[sim.Map.Idx(at)] <= 0 && Navigation.CanReach(sim, dragon, from, at, PathEnd.Touch))
            {
                best = e;
                bestScore = score;
            }
        }
        return best.IsNull ? null : new DragonHuntJob(best);
    }

    /// <summary>
    /// The dragon eats from a carcass where it lies: its fill, up to what the kill holds. What's
    /// left (and the hide) stays on the ground as raw meat for whoever wants it.
    /// </summary>
    internal static void Feed(Simulation sim, Entity dragon, Entity carcass)
    {
        var w = sim.World;
        var def = w.Get<Creature>(carcass).Def;
        var meat = sim.Defs.Thing("Meat");
        var units = Math.Max(1, (int)MathF.Round(def.Animal!.MeatYield * KillYield));
        var eaten = Math.Min(units, EatJob.Wanted(sim, dragon, meat));
        var cell = sim.LocationOf(carcass);
        var rot = w.TryGet<Rot>(carcass, out var r) ? r.Progress : 0f;
        w.DestroyDeferred(carcass);
        EatJob.Nourish(sim, dragon, meat, eaten, rot);
        if (units > eaten)
        {
            sim.SpawnItem(meat, units - eaten, cell, rot: rot);
            // A devoted dragon carries a share to its keeper (Bonding; DragonBrain sends it).
            if (w.TryGet<Dragon>(dragon, out var d) && w.IsAlive(d.Keeper) && Bonding.Reached(sim, dragon, Bonding.Devoted)
                && sim.Map.Item[sim.Map.Idx(cell)] is { IsNull: false } left && w.Get<Thing>(left).Def == meat)
                Bonding.Care(sim, dragon).BringMeat = left;
        }
        if (def.Animal.HideYield > 0 && sim.Defs.TryThing("Hide", out var hide))
            sim.SpawnItem(hide, Math.Max(1, (int)MathF.Round(def.Animal.HideYield * KillYield)), cell, rot: rot);
    }
}

/// <summary>
/// A grown dragon hunts for itself: runs (or flies) down a wild animal and kills it with tooth and
/// claw (<see cref="Combat.RealtimeAttacks"/>), then eats from the carcass; or goes straight to a
/// fresh carcass. Gives up after a game hour, or if it gets badly hurt.
/// </summary>
public sealed class DragonHuntJob(Entity prey, bool mounted = false) : Job
{
    /// <summary>Sent by its rider (<see cref="RidingSystem.OrderHunt"/>): it kills and leaves the kill, rather than eating.</summary>
    public bool Mounted => mounted;

    public const int GiveUpHours = 1;

    /// <summary>Ticks spent tearing into the kill before the meal counts.</summary>
    public const int FeedTicks = 240;

    public override Entity Focus => prey;

    protected override IEnumerable<JobStatus> Run()
    {
        var label = Get<Creature>(prey).Def.Label;
        Report = $"Hunting {label}";
        if (!Reserve(prey)) yield break;
        if (Combat.CombatRules.Attacks(Sim, Pawn).FirstOrDefault(a => !a.Ranged) is not { } attack)
        {
            Fail("nothing to hunt with");
            yield break;
        }

        var giveUp = Sim.Clock.Tick + SimClock.HoursToTicks(GiveUpHours);
        var mover = Get<Mover>(Pawn);
        long nextAttack = 0, nextPlan = 0;
        while (true)
        {
            if (!Alive(prey) || Sim.World.Has<Carried>(prey))
            {
                Fail("prey is gone");
                yield break;
            }
            if (Get<Creature>(prey).Dead)
                break;
            if (Sim.Clock.Tick > giveUp)
            {
                Fail("the prey got away");
                yield break;
            }
            if (Sim.World.TryGet<Health>(Pawn, out var h) && h.Pain > 0.5f)
            {
                Fail("too hurt to hunt");
                yield break;
            }
            var here = PawnCell;
            var at = Sim.LocationOf(prey);
            // Strike only when the blow can land (a bite can't reach the ground from the air: a dive
            // goes on down beside the prey first), and spend the cooldown only on a real attack.
            if ((Pathfinder.Touches(here, at) || Navigation.CanStrikeFromAir(Sim.Map, here, at))
                && Combat.CombatRules.Chance(Sim, Pawn, prey, attack).Possible)
            {
                mover.Stop();
                if (Sim.Clock.Tick >= nextAttack && Combat.RealtimeAttacks.TryAttack(Sim, Pawn, prey, attack) != null)
                    nextAttack = Sim.Clock.Tick + attack.CooldownTicks;
            }
            else if (!mover.Moving || Sim.Clock.Tick >= nextPlan)
            {
                if (Chase(here, at) is not { } path)
                {
                    Fail($"no way to reach the {label}");
                    yield break;
                }
                mover.Path = path;
                mover.PathIndex = 0;
                mover.StepProgress = 0;
                mover.StepFrom = here;
                nextPlan = Sim.Clock.Tick + 30;
            }
            yield return JobStatus.Running;
        }

        // Hunting for its rider: the kill is theirs to butcher.
        if (mounted)
        {
            mover.Stop();
            yield break;
        }

        // Dead (just now, or it was a carcass all along): settle down beside it and eat.
        foreach (var s in GoToEntity(prey, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;

        // Told to bring kills home: carry it to the butcher spot and eat there, so what's left
        // lands by the larder (ButcherSpots).
        if (DragonHunting.PolicyOf(Sim, Pawn).BringHome && !Sim.World.Has<Carried>(prey) && ButcherSpots.CanCarry(Sim, Pawn, prey)
            && ButcherSpots.SpotAt(Sim, Sim.LocationOf(prey)).IsNull
            && ButcherSpots.Nearest(Sim, Pawn, PawnCell) is { IsNull: false } spot)
        {
            Sim.World.Add(prey, new Carried { By = Pawn });
            Report = $"Carrying the {label} home";
            foreach (var s in GoToBeside(spot)) yield return s;
            if (HasFailed) yield break; // ReleasePawn sets it down where we are
            Sim.World.Remove<Carried>(prey);
            Sim.World.Get<Position>(prey) = Position.At(PawnCell);
        }
        Report = $"Feeding on a {label}";
        foreach (var s in Wait(FeedTicks)) yield return s;
        if (!Alive(prey) || Sim.World.Has<Carried>(prey))
        {
            Fail("the carcass is gone");
            yield break;
        }
        DragonHunting.Feed(Sim, Pawn, prey);
    }

    /// <summary>Cells (horizontally) within which a dragon in the air dives at its prey.</summary>
    public const int StoopRange = 16;

    /// <summary>Cells within which a dragon on the ground takes off after its prey rather than run.</summary>
    public const int HopRange = 24;

    /// <summary>
    /// The next leg of the chase. Game outruns a dragon on foot, so one that can fly hunts from the
    /// air: a low hop up when it's on the ground, then a dive (<see cref="Navigation.StoopRoute"/>)
    /// at where the prey is running to, not where it is.
    /// </summary>
    private List<int3>? Chase(int3 here, int3 at)
    {
        if (Navigation.FlyingStage(Sim, Pawn) is { } stage)
        {
            var d = Math.Max(Math.Abs(at.X - here.X), Math.Abs(at.Y - here.Y));
            var aim = Lead(at, d, stage);
            if (Navigation.Airborne(Sim.Map, here))
            {
                if (d <= StoopRange && Navigation.StoopRoute(Sim, Pawn, here, aim) is { } dive)
                    return dive;
            }
            else if (d > 1 && d <= HopRange && Navigation.HopUp(Sim, here, HopHeight) is { } hop
                && Navigation.StoopRoute(Sim, Pawn, hop[^1], aim) is { } pounce)
            {
                hop.AddRange(pounce);
                return hop;
            }
        }
        return Navigation.PlanRoute(Sim, Pawn, here, at, PathEnd.Touch);
    }

    /// <summary>Levels a dragon on the ground jumps up before diving at prey close by.</summary>
    public const int HopHeight = 2;

    /// <summary>
    /// Where fleeing prey will be by the time the dragon gets there: along its own path, as many
    /// steps ahead as it covers while the dragon flies the <paramref name="distance"/>.
    /// </summary>
    private int3 Lead(int3 at, int distance, Defs.LifeStage stage)
    {
        var w = Sim.World;
        if (!w.TryGet<Mover>(prey, out var run) || !run.Moving || run.Path is not { } path)
            return at;
        var mine = Get<Creature>(Pawn).Def.MoveSpeed * stage.SpeedFactor * stage.FlySpeedFactor;
        var theirs = w.Get<Creature>(prey).Def.MoveSpeed * Math.Max(0.1f, w.Get<Creature>(prey).Stage(Sim.Clock.Tick).SpeedFactor);
        var ahead = (int)MathF.Ceiling(distance * theirs / Math.Max(1f, mine)) + 1;
        var index = Math.Min(run.PathIndex + ahead, path.Count - 1);
        return index >= run.PathIndex ? path[index] : at;
    }
}
