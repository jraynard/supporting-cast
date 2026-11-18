using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Afterlife;

/// <summary>
/// On a body an empath has called back (<see cref="Revival.Call"/>): the calling holds open until
/// <see cref="Until"/> for the dead to answer, coming to their body (<see cref="AnswerCommand"/>).
/// </summary>
public struct Calling
{
    public Entity Caster;
    public long Until;
    public float Quality;

    /// <summary>Who held the body while the empath called (the rite's for two); Entity.Null for a calling from before.</summary>
    public Entity Keeper;
}

/// <summary>Someone already brought back this round: the way back opens only once.</summary>
public struct Revived
{
    public int Count;

    /// <summary>Who called them back and who held them, for the reveal.</summary>
    public Entity CalledBy, HeldBy;
}

/// <summary>
/// Coming back (docs/DESIGN.md, "Death"): the empath's "Calling back", woven kneeling at a fresh
/// body, holds the way open; the ghost comes to its body and answers. The dead come back downed
/// and weak, the empath carries the grave's chill, and nobody comes back twice in a round. A body
/// keeps longer laid on a pallet in the shrine. The dead the AI ran, or whose player is away, answer
/// by themselves. Properties, not scripts: it works on anyone's body, villager or adventurer.
/// </summary>
public static class Revival
{
    /// <summary>
    /// How long a body can still be called back, game hours: lying anywhere, or on a shrine pallet.
    /// Eight and sixteen minutes at 1x: four hours was under three minutes, gone before a playtest's
    /// empath had found out how.
    /// </summary>
    public const float FreshHours = 12f, PalletHours = 24f;

    /// <summary>How long a calling holds for the dead to answer, ticks (half a game hour: twenty seconds).</summary>
    public const int AnswerTicks = SimClock.TicksPerHour / 2;

    /// <summary>How near its body (cells) a ghost must be to answer.</summary>
    public const float AnswerReach = 2.5f;

    /// <summary>The grave's chill the caster takes on: frost, less the better the weave.</summary>
    public const float ChillDamage = 10f;

    /// <summary>A calling held by a keeper this weak (its quality, slips and all) fails: the grave keeps them.</summary>
    public const float FailsBelow = 0.2f;

    /// <summary>Blood the returned come back with, at a poor and a perfect weave.</summary>
    public const float BloodPoor = 0.45f, BloodPerfect = 0.6f;

    /// <summary>What's left of a vital part that killed them: just enough to live on.</summary>
    public const float VitalLeft = 0.15f;

    /// <summary>Lying on a pallet (a bed) in the healer's shrine.</summary>
    public static bool OnShrinePallet(Simulation sim, Entity body)
    {
        var w = sim.World;
        var map = sim.Map;
        var cell = w.Get<Position>(body).Cell;
        if (!map.InBounds(cell) || w.Has<Carried>(body))
            return false;
        var site = map.Structure[map.Idx(cell)];
        if (site.IsNull || w.Has<Construction>(site) || w.Get<Thing>(site).Def.Building is not { IsBed: true })
            return false;
        var building = Buildings.At(sim, cell);
        return !building.IsNull && w.Get<PresetBuilding>(building).Def.Name == "Shrine";
    }

    /// <summary>Still able to be called back: died in a round, not too long ago, and not brought back already.</summary>
    public static bool Fresh(Simulation sim, Entity body)
    {
        var w = sim.World;
        if (!w.IsAlive(body) || !w.TryGet<Death>(body, out var d) || w.Has<Revived>(body))
            return false;
        return sim.Clock.Tick - d.Tick <= SimClock.HoursToTicks(OnShrinePallet(sim, body) ? PalletHours : FreshHours);
    }

    /// <summary>Game hours left to call this body back (0 once it's cold, or it's been back once).</summary>
    public static float HoursLeft(Simulation sim, Entity body)
    {
        if (!Fresh(sim, body))
            return 0f;
        var hours = OnShrinePallet(sim, body) ? PalletHours : FreshHours;
        return Math.Max(0f, hours - (sim.Clock.Tick - sim.World.Get<Death>(body).Tick) / (float)SimClock.TicksPerHour);
    }

    /// <summary>What a Calling back costs (the mending's mana).</summary>
    public static float CallCost(Simulation sim) =>
        sim.Defs.AllMendings.FirstOrDefault(m => m.CallsBack) is { } m ? m.CostAt(0) : 70f;

    /// <summary>The Calling back mending, if the defs have one.</summary>
    public static MendingDef? CallMending(Simulation sim) => sim.Defs.AllMendings.FirstOrDefault(m => m.CallsBack);

    /// <summary>
    /// The cells a body can be knelt at from: its own, and, laid in a bed, every cell of the bed (a
    /// body half under the covers is reached from any side of it).
    /// </summary>
    public static IEnumerable<int3> LiesAcross(Simulation sim, Entity body)
    {
        var w = sim.World;
        var cell = w.Get<Position>(body).Cell;
        yield return cell;
        if (!sim.Map.InBounds(cell))
            yield break;
        var site = sim.Map.Structure[sim.Map.Idx(cell)];
        if (site.IsNull || !w.TryGet<Thing>(site, out var t) || t.Def.Building is not { IsBed: true })
            yield break;
        foreach (var c in sim.FootprintCells(site))
            if (c != cell)
                yield return c;
    }

    /// <summary>The freshest dead person lying at (or in a bed across) a cell, or Entity.Null.</summary>
    public static Entity BodyAt(Simulation sim, int3 cell)
    {
        var w = sim.World;
        var best = Entity.Null;
        var bestTick = long.MinValue;
        foreach (var e in w.Each<Death>())
        {
            if (w.Has<Carried>(e) || !w.TryGet<Creature>(e, out var c) || !c.Dead || w.Get<Death>(e).Tick <= bestTick)
                continue;
            foreach (var at in LiesAcross(sim, e))
                if (at == cell)
                {
                    (best, bestTick) = (e, w.Get<Death>(e).Tick);
                    break;
                }
        }
        return best;
    }

    /// <summary>
    /// Why <paramref name="caster"/> can't call <paramref name="body"/> back now, in words for them
    /// (null if they can): the gift, the body's freshness, being beside it, and the mana.
    /// </summary>
    public static string? CallProblem(Simulation sim, Entity caster, Entity body)
    {
        var w = sim.World;
        if (!w.TryGet<Mana>(caster, out var mana))
            return "only an empath can call the dead back";
        if (!w.IsAlive(body) || !w.Has<Death>(body) || !w.Has<Creature>(body) || !w.Has<Position>(body))
            return "there's nobody here to call back";
        var name = w.Get<Creature>(body).Name;
        if (w.Has<Revived>(body))
            return $"the way back has opened for {name} once already; it won't again";
        if (!Fresh(sim, body))
            return $"{name} is too long gone";
        if (BodyBeside(sim, caster) != body)
            return $"kneel right beside {name} first";
        var cost = CallCost(sim);
        if (mana.Current < cost - 0.01f)
            return $"a calling takes {cost:F0} mana and you have {mana.Current:F0}; meditate first (it'll keep {HoursLeft(sim, body):F0} more hours{(OnShrinePallet(sim, body) ? "" : ", longer on a shrine pallet")})";
        return null;
    }

    /// <summary>
    /// The empath starts weaving a Calling back for the body beside them (F at the body): the weave's
    /// the same as any, keyed from the book, but the scene shows the runes it wants. Says why not if it can't.
    /// </summary>
    public static void BeginCalling(Simulation sim, Entity caster, Entity body)
    {
        if (!sim.World.IsAlive(caster) || !sim.World.Has<Creature>(caster) || sim.World.Has<Work.WorkSession>(caster))
            return;
        if (CallProblem(sim, caster, body) is { } why)
        {
            sim.Messages.Tell(caster, MessageKind.Info, $"You can't: {why}.", sim.LocationOf(caster));
            return;
        }
        Healing.CastSession.TryStart(sim, caster, 0, callingFor: sim.World.Get<Creature>(body).Name, body: body);
    }

    /// <summary>
    /// Someone's died who could be called back: each empath alive in the town hears how, and how
    /// long they have, so the way back isn't something to stumble on.
    /// </summary>
    internal static void TellEmpaths(Simulation sim, Entity body)
    {
        var w = sim.World;
        if (!w.Has<Death>(body) || CallMending(sim) is null)
            return;
        var name = w.Get<Creature>(body).Name;
        foreach (var e in w.Query<Mana>())
            if (e != body && w.TryGet<Creature>(e, out var c) && !c.Dead && (w.Has<Avatar>(e) || w.Has<PlayerCharacter>(e)))
                sim.Messages.Tell(e, MessageKind.Warning,
                    $"{name} has died. You can still call them back: go to the body within {FreshHours:F0} hours ({PalletHours:F0} on a shrine pallet), "
                    + $"with {CallCost(sim):F0} mana, and kneel at it: it'll offer you the Calling.", w.Get<Position>(body).Cell);
    }

    /// <summary>Is anyone with the gift alive to call the dead back?</summary>
    public static bool AnyEmpath(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Each<Mana>())
            if (w.TryGet<Creature>(e, out var c) && !c.Dead)
                return true;
        return false;
    }

    /// <summary>The dead person the caster is kneeling at (touching), if any, nearest first and the freshest among those.</summary>
    public static Entity BodyBeside(Simulation sim, Entity caster)
    {
        var w = sim.World;
        var here = w.Get<Position>(caster).Cell;
        var best = Entity.Null;
        var bestTick = long.MinValue;
        foreach (var e in w.Each<Death>())
        {
            if (w.Has<Carried>(e) || !w.TryGet<Creature>(e, out var c) || !c.Dead)
                continue;
            var touching = false;
            foreach (var at in LiesAcross(sim, e))
                if (at == here || Ai.Pathfinder.Touches(here, at))
                {
                    touching = true;
                    break;
                }
            if (!touching)
                continue;
            var tick = w.Get<Death>(e).Tick;
            if (tick > bestTick)
                (best, bestTick) = (e, tick);
        }
        return best;
    }

    /// <summary>The ghost a body left, if its player's dead and watching.</summary>
    public static Entity GhostOf(Simulation sim, Entity body)
    {
        var w = sim.World;
        foreach (var e in w.Each<Ghost>())
            if (w.Get<Ghost>(e).Body == body)
                return e;
        return Entity.Null;
    }

    /// <summary>
    /// The woven Calling back is let go (<see cref="Healing.CastSession"/>): it reaches for the body
    /// the caster kneels at. True if it took hold. The mana's spent either way.
    /// </summary>
    public static bool Call(Simulation sim, Entity caster, float quality, Entity? keptBy = null)
    {
        var keeper = keptBy ?? Entity.Null;
        var w = sim.World;
        var at = sim.LocationOf(caster);
        var body = BodyBeside(sim, caster);
        if (body.IsNull)
        {
            sim.Messages.Tell(caster, MessageKind.Warning, "The calling reaches out and finds no one beside you to call back. The mana's spent.", at);
            return false;
        }
        var name = w.Get<Creature>(body).Name;
        if (w.Has<Revived>(body))
        {
            sim.Messages.Add(MessageKind.Warning, $"The way back has opened for {name} once already. It won't again.", at);
            return false;
        }
        if (!Fresh(sim, body))
        {
            sim.Messages.Add(MessageKind.Warning, $"{name} is too long gone. The calling finds nothing to hold.", at);
            return false;
        }
        if (!keeper.IsNull && quality < FailsBelow)
        {
            // Too many holds slipped: the grave keeps them, and the cold comes back all the same.
            var cold = HealthSystem.Damage(sim, caster, ChillDamage, DamageType.Frost, bias: PartHeight.High);
            if (cold != null)
                cold.Absorbed = true;
            sim.Messages.Tell(caster, MessageKind.Warning, $"The grave pulls {name} out of your keeper's hands, and the calling fails. The mana's spent.", at);
            return false;
        }
        if (w.Has<Calling>(body))
            w.Remove<Calling>(body);
        w.Add(body, new Calling { Caster = caster, Until = sim.Clock.Tick + AnswerTicks, Quality = Math.Clamp(quality, 0f, 1f), Keeper = keeper });

        // The grave's cold comes back with the caster: a wound of their own, known by its feel.
        var chill = HealthSystem.Damage(sim, caster, ChillDamage * (1.5f - Math.Clamp(quality, 0f, 1f)), DamageType.Frost, bias: PartHeight.High);
        if (chill != null)
            chill.Absorbed = true;
        sim.Messages.Tell(caster, MessageKind.Good, $"The calling holds the way open for {name}, and the grave's cold creeps into your hands.", at);

        var ghost = GhostOf(sim, body);
        if (ghost.IsNull || w.Get<Ghost>(ghost).Away)
            Revive(sim, body); // nobody there to answer: they come as they're called
        return true;
    }

    /// <summary>Why this ghost can't answer now, or null if it can.</summary>
    public static string? AnswerProblem(Simulation sim, Entity ghost)
    {
        var w = sim.World;
        if (!w.IsAlive(ghost) || !w.TryGet<Ghost>(ghost, out var g) || !w.IsAlive(g.Body))
            return "there's nothing to answer";
        if (!w.TryGet<Calling>(g.Body, out var calling) || calling.Until < sim.Clock.Tick)
            return "nobody is calling you back";
        var a = w.Get<Position>(ghost).Pos;
        var b = w.Get<Position>(g.Body).Pos;
        if (MathF.Abs(a.Z - b.Z) > 0.5f || (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) > AnswerReach * AnswerReach)
            return "you must be at your body to answer";
        return null;
    }

    /// <summary>A ghost answers the calling at its body: it comes back.</summary>
    public static bool Answer(Simulation sim, Entity ghost)
    {
        if (AnswerProblem(sim, ghost) != null)
            return false;
        Revive(sim, sim.World.Get<Ghost>(ghost).Body);
        return true;
    }

    /// <summary>Back, barely: breathing, downed, the fatal wound eased to just survivable, once a round.</summary>
    public static void Revive(Simulation sim, Entity body)
    {
        var w = sim.World;
        ref var c = ref w.Get<Creature>(body);
        if (!c.Dead)
            return;
        var quality = w.TryGet<Calling>(body, out var calling) ? calling.Quality : 0.5f;
        var (calledBy, heldBy) = w.Has<Calling>(body) ? (calling.Caster, calling.Keeper) : (Entity.Null, Entity.Null);
        if (w.TryGet<Health>(body, out var h))
        {
            h.Blood = Math.Max(h.Blood, BloodPoor + (BloodPerfect - BloodPoor) * quality);
            for (var part = 0; part < h.Body.Parts.Count; part++)
            {
                var over = h.PartDamage(part) - (1f - VitalLeft) * h.PartMaxHp(part);
                if (!h.Body.Parts[part].Vital || over <= 0)
                    continue;
                foreach (var inj in h.Injuries)
                {
                    if (inj.Part != part || over <= 0)
                        continue;
                    var eased = Math.Min(inj.Damage, over);
                    inj.Damage -= eased;
                    over -= eased;
                }
                h.Injuries.RemoveAll(i => i.Damage <= 0);
            }
            foreach (var inj in h.Injuries)
                if (inj.Infection != null)
                    inj.Infection.Severity = Math.Min(inj.Infection.Severity, 0.4f);
            h.Changed();
        }
        c.Dead = false;
        c.Hp = Math.Max(c.Hp, c.Def.MaxHp * 0.3f);
        c.Downed = true; // up when the body says so (HealthSystem)
        w.Remove<Death>(body);
        w.Remove<Calling>(body);
        w.Add(body, new Revived { Count = 1, CalledBy = calledBy, HeldBy = heldBy });
        if (c.Faction == Faction.Village)
            sim.Reckoning.Add(sim, 0f, sim.Defs.Season.DreadPerRevival);
        w.Get<Mover>(body).Stop();

        var ghost = GhostOf(sim, body);
        if (!ghost.IsNull)
        {
            var g = w.Get<Ghost>(ghost);
            // Their player steers them again; one who's away finds them waiting when they're back.
            if (!g.Away && !w.Has<Avatar>(body))
            {
                AiSystem.EndJob(sim, body, w.Get<Mind>(body), false);
                w.Add(body, new Avatar());
            }
            w.Destroy(ghost);
        }
        sim.Messages.Add(MessageKind.Good, $"{c.Name} draws a ragged breath. They've come back.", w.Get<Position>(body).Cell);
    }

    /// <summary>Callings nobody answered in time close again.</summary>
    internal static void Update(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Query<Calling>())
        {
            if (w.Get<Calling>(e).Until >= sim.Clock.Tick)
                continue;
            w.Remove<Calling>(e);
            sim.Messages.Add(MessageKind.Warning, $"The way back for {w.Get<Creature>(e).Name} closes, unanswered.", w.Get<Position>(e).Cell);
        }
    }
}

/// <summary>An empath beside a fresh body starts weaving its Calling back (<see cref="Revival.BeginCalling"/>).</summary>
public sealed record CallBackCommand(Entity Actor, Entity Body) : ISimCommand, IActorCommand
{
    public void Apply(Simulation sim)
    {
        if (Core.InventoryCommand.Ready(sim, Actor, downed: false) && !sim.World.Has<Carried>(Actor) && !sim.World.Has<Work.WorkSession>(Actor))
            Revival.BeginCalling(sim, Actor, Body);
    }
}

/// <summary>A ghost answers the calling at its body (<see cref="Revival.Answer"/>).</summary>
public sealed record AnswerCommand(Entity Actor) : ISimCommand, IActorCommand
{
    public void Apply(Simulation sim) => Revival.Answer(sim, Actor);
}

/// <summary>The dead's business each second: callings that close unanswered, presence gathering and spent in beasts, signs fading, dreams remembered.</summary>
public sealed class AfterlifeSystem : ISimSystem
{
    public string Name => "Afterlife";
    public int Interval => SimClock.TicksPerSecond;

    public void Update(Simulation sim)
    {
        if (sim.World.Count<Calling>() > 0)
            Revival.Update(sim);
        if (sim.World.Count<Ghost>() > 0 || sim.World.Count<Haunting>() > 0)
            Hauntings.Update(sim, Interval);
        if (sim.World.Count<Ghost>() > 0)
            Errands.Update(sim, Interval);
        if (sim.World.Count<Possessed>() > 0 || sim.World.Count<Ghost>() > 0)
            Possession.Update(sim, Interval);
        if (sim.World.Count<Dream>() > 0)
            Dreams.Update(sim);
        if (sim.World.Count<Death>() > 0 && sim.Round.Live)
            Vacancies.Update(sim);
    }
}
