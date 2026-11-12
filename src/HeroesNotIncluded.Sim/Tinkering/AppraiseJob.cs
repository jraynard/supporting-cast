using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tinkering;

/// <summary>
/// An AI tinker appraises a curio for its owner: takes the order on, collects the piece, looks it
/// over and hands it back with an honest reckoning. Rolled rather than played: skill decides how
/// many of its tells they read. They don't haggle or buy. Players do it by hand (<see cref="LoupeSession"/>).
/// </summary>
public sealed class AppraiseJob(int order) : Job
{
    /// <summary>Ticks spent looking it over.</summary>
    public const int LookTicks = 480;

    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return null;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var e in sim.Shops.FrontFirst(sim))
        {
            if (!e.IsOpen || e.Kind.Kind != ServiceKind.Appraise || e.Kind.AtDef != workplace || !trade.Def.Serving(e.Kind.Kind)
                || !e.KnownBy.Contains(pawn) || (!e.Assignee.IsNull && e.Assignee != pawn) || !w.IsAlive(e.Subject) || !w.IsAlive(e.Poster))
                continue;
            if (Navigation.CanReach(sim, pawn, from, w.Get<Position>(e.Poster).Cell, PathEnd.Touch))
                return new AppraiseJob(e.Id);
        }
        return null;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        var orders = Sim.Shops;
        if (orders.Find(order) is not { IsOpen: true } e || (e.Assignee != Pawn && !orders.Accept(Sim, Pawn, order)))
        {
            Fail("someone else has it");
            yield break;
        }
        var curio = e.Subject;
        Report = $"Fetching {e.PosterName}'s curio";
        for (var tries = 0; tries < 3 && !Inventory.OnPerson(Sim, Pawn, curio); tries++)
        {
            foreach (var s in GoToEntity(e.Poster, PathEnd.Touch, 1500)) yield return s;
            if (HasFailed) yield break;
            if (!orders.TryHandOver(Sim, e))
                foreach (var s in Wait(30)) yield return s;
        }
        if (!Inventory.OnPerson(Sim, Pawn, curio))
        {
            Fail("couldn't get the curio");
            yield break;
        }

        Report = "Looking a curio over";
        for (var i = 0; i < LookTicks; i++)
        {
            if (!Alive(curio) || !Inventory.OnPerson(Sim, Pawn, curio))
            {
                Fail("the curio's gone");
                yield break;
            }
            SkillRules.Learn(Sim, Pawn, Skill.Crafting, SkillRules.XpPerWorkTick);
            yield return JobStatus.Running;
        }
        var seed = Get<Curio>(curio).Seed;
        var tells = Curios.Tells(seed).Length;
        var level = SkillRules.LevelOf(Sim, Pawn, Skill.Crafting);
        var read = 0;
        var saw = false;
        for (var t = 0; t < tells; t++)
            if (Sim.Rand.Chance(Math.Clamp(0.35f + 0.06f * level, 0.3f, 0.9f)))
            {
                read++;
                saw |= t == Curios.Giveaway(seed);
            }
        Curios.Appraised(Sim, Pawn, curio, Curios.Read(Sim, Pawn, curio, read, saw));

        Report = $"Bringing back {e.PosterName}'s curio";
        foreach (var s in GoToEntity(e.Poster, PathEnd.Touch, 1500)) yield return s;
        if (HasFailed) yield break;
        if (!Handing.InReach(Sim, Pawn, e.Poster) || !Inventory.OnPerson(Sim, Pawn, curio))
        {
            Fail("couldn't hand it back");
            yield break;
        }
        orders.Received(Sim, Pawn, e.Poster, Inventory.TakeFromPerson(Sim, Pawn, curio, 1));
    }
}
