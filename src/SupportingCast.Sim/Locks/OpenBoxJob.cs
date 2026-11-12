using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Shops;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Locks;

/// <summary>
/// An AI carpenter opens a strongbox for its owner: takes the order on, collects the box, feels
/// it over for a trap, picks the lock and brings it back for the pay. A basic hand at it, rolled
/// rather than played: skill makes finding and drawing a trap likelier, and a missed trap goes
/// off when the lid comes up. Players do it by hand (<see cref="ProbeSession"/>, <see cref="PickSession"/>).
/// </summary>
public sealed class OpenBoxJob(int order) : Job
{
    /// <summary>Ticks of searching and of picking.</summary>
    public const int ProbeTicks = 420, PickTicks = 540;

    /// <summary>An unlock order this villager could see to now: their trade's, known to them, theirs or nobody's.</summary>
    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef is not { } workplace || Lockwork.Picks(sim, pawn).IsNull)
            return null;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var e in sim.Shops.FrontFirst(sim))
        {
            if (!e.IsOpen || e.Kind.Kind != ServiceKind.Unlock || e.Kind.AtDef != workplace || !e.KnownBy.Contains(pawn)
                || (!e.Assignee.IsNull && e.Assignee != pawn) || !w.IsAlive(e.Subject) || !w.IsAlive(e.Poster))
                continue;
            if (Navigation.CanReach(sim, pawn, from, w.Get<Position>(e.Poster).Cell, PathEnd.Touch))
                return new OpenBoxJob(e.Id);
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
        var box = e.Subject;
        Report = $"Fetching {e.PosterName}'s strongbox";
        for (var tries = 0; tries < 3 && !Inventory.OnPerson(Sim, Pawn, box); tries++)
        {
            foreach (var s in GoToEntity(e.Poster, PathEnd.Touch, 1500)) yield return s;
            if (HasFailed) yield break;
            if (!orders.TryHandOver(Sim, e))
                foreach (var s in Wait(30)) yield return s;
        }
        if (!Inventory.OnPerson(Sim, Pawn, box))
        {
            Fail("couldn't get the box");
            yield break;
        }

        var level = SkillRules.LevelOf(Sim, Pawn, Skill.Crafting);
        if (Get<Strongbox>(box).Locked && Get<Strongbox>(box).SearchedBy != Pawn)
        {
            Report = "Feeling a strongbox over for traps";
            for (var i = 0; i < ProbeTicks; i++)
            {
                if (!Lockwork.IsLocked(Sim, box) || !Inventory.OnPerson(Sim, Pawn, box))
                {
                    Fail("the search was called off");
                    yield break;
                }
                SkillRules.Learn(Sim, Pawn, Skill.Crafting, SkillRules.XpPerWorkTick);
                yield return JobStatus.Running;
            }
            ref var b = ref Get<Strongbox>(box);
            b.SearchedBy = Pawn;
            // Found it? Then drawn, or set off drawing it.
            if (b.Armed && Sim.Rand.Chance(Math.Clamp(0.45f + 0.05f * level, 0f, 0.95f)))
            {
                if (Sim.Rand.Chance(Math.Clamp(0.6f + 0.04f * level, 0f, 0.95f)))
                    b.Armed = false;
                else
                    Lockwork.Spring(Sim, Pawn, box);
            }
        }
        if (Get<Strongbox>(box).Locked)
        {
            Report = "Picking a strongbox's lock";
            for (var i = 0; i < PickTicks; i++)
            {
                if (!Lockwork.IsLocked(Sim, box) || !Inventory.OnPerson(Sim, Pawn, box) || Lockwork.Picks(Sim, Pawn).IsNull)
                {
                    Fail("the lock work was called off");
                    yield break;
                }
                SkillRules.Learn(Sim, Pawn, Skill.Crafting, SkillRules.XpPerWorkTick);
                yield return JobStatus.Running;
            }
            Lockwork.Open(Sim, Pawn, box);
        }

        Report = $"Bringing back {e.PosterName}'s strongbox";
        foreach (var s in GoToEntity(e.Poster, PathEnd.Touch, 1500)) yield return s;
        if (HasFailed) yield break;
        if (!Handing.InReach(Sim, Pawn, e.Poster) || !Inventory.OnPerson(Sim, Pawn, box))
        {
            Fail("couldn't hand it back");
            yield break;
        }
        orders.Received(Sim, Pawn, e.Poster, Inventory.TakeFromPerson(Sim, Pawn, box, 1));
    }
}
