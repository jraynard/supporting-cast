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
}

/// <summary>Someone already brought back this round: the way back opens only once.</summary>
public struct Revived
{
    public int Count;
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
    /// <summary>How long a body can still be called back, game hours: lying anywhere, or on a shrine pallet.</summary>
    public const float FreshHours = 4f, PalletHours = 12f;

    /// <summary>How long a calling holds for the dead to answer, ticks (half a game hour: twenty seconds).</summary>
    public const int AnswerTicks = SimClock.TicksPerHour / 2;

    /// <summary>How near its body (cells) a ghost must be to answer.</summary>
    public const float AnswerReach = 2.5f;

    /// <summary>The grave's chill the caster takes on: frost, less the better the weave.</summary>
    public const float ChillDamage = 10f;

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
        var hours = OnShrinePallet(sim, body) ? PalletHours : FreshHours;
        return sim.Clock.Tick - d.Tick <= SimClock.HoursToTicks(hours);
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
            var at = w.Get<Position>(e).Cell;
            if (at != here && !Ai.Pathfinder.Touches(here, at))
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
    public static bool Call(Simulation sim, Entity caster, float quality)
    {
        var w = sim.World;
        var at = sim.LocationOf(caster);
        var body = BodyBeside(sim, caster);
        if (body.IsNull)
        {
            sim.Messages.Add(MessageKind.Warning, "The calling reaches out and finds no one beside you to call back. The mana's spent.", at);
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
        if (w.Has<Calling>(body))
            w.Remove<Calling>(body);
        w.Add(body, new Calling { Caster = caster, Until = sim.Clock.Tick + AnswerTicks, Quality = Math.Clamp(quality, 0f, 1f) });

        // The grave's cold comes back with the caster: a wound of their own, known by its feel.
        var chill = HealthSystem.Damage(sim, caster, ChillDamage * (1.5f - Math.Clamp(quality, 0f, 1f)), DamageType.Frost, bias: PartHeight.High);
        if (chill != null)
            chill.Absorbed = true;
        sim.Messages.Add(MessageKind.Good, $"The calling holds the way open for {name}, and the grave's cold creeps into your hands.", at);

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
        w.Add(body, new Revived { Count = 1 });
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

/// <summary>A ghost answers the calling at its body (<see cref="Revival.Answer"/>).</summary>
public sealed record AnswerCommand(Entity Actor) : ISimCommand, IActorCommand
{
    public void Apply(Simulation sim) => Revival.Answer(sim, Actor);
}

/// <summary>The dead's business each second: callings that close unanswered, presence gathering, signs fading.</summary>
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
        if (sim.World.Count<Death>() > 0 && sim.Round.Live)
            Vacancies.Update(sim);
    }
}
