using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Errands;
using SupportingCast.Sim.Maps;

namespace SupportingCast.Sim.Expeditions;

/// <summary>Put a quest you know of to a party member in reach (<see cref="ExpeditionSystem.Offer"/>).</summary>
public sealed record OfferQuestCommand(Entity Avatar, Entity Member, int Quest) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar))
            sim.Expeditions.Offer(sim, Avatar, Member, Quest);
    }
}

/// <summary>
/// An AI broker answers a party's call for work: picks the quest they believe best fits the
/// party's strength (a little under it, to bring them back), walks over and puts it to them.
/// </summary>
public sealed class BrokerJob(Entity member, int quest) : Job
{
    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || !trade.Def.Brokers)
            return null;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var e in sim.Errands.All)
        {
            if (!e.IsOpen || e.Kind.Kind != Defs.ErrandKind.Quest || !e.KnownBy.Contains(pawn) || (!e.Assignee.IsNull && e.Assignee != pawn)
                || !w.TryGet<Adventurer>(e.Poster, out var a) || w.Has<Leaving>(e.Poster))
                continue;
            var strength = ExpeditionSystem.PartyStrength(sim, a.Party);
            var best = -1;
            var bestOff = float.MaxValue;
            foreach (var q in sim.Expeditions.Quests)
            {
                if (q.Status != QuestStatus.Open || sim.Expeditions.IntelOf(pawn, q.Id) is not { } intel
                    || intel.Danger < ExpeditionSystem.TooEasy * strength || intel.Danger > ExpeditionSystem.TooHard * strength)
                    continue;
                var off = MathF.Abs(intel.Danger - 0.85f * strength);
                if (off < bestOff)
                {
                    best = q.Id;
                    bestOff = off;
                }
            }
            if (best > 0 && Navigation.CanReach(sim, pawn, from, w.Get<Position>(e.Poster).Cell, PathEnd.Touch))
                return new BrokerJob(e.Poster, best);
        }
        return null;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Finding work for a party";
        foreach (var s in GoToEntity(member, PathEnd.Touch, 1500)) yield return s;
        if (HasFailed) yield break;
        if (!Handing.InReach(Sim, Pawn, member) || !Sim.Expeditions.Offer(Sim, Pawn, member, quest))
            Fail("they wouldn't have it");
    }
}
