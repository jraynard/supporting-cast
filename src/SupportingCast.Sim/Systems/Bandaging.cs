using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;

namespace SupportingCast.Sim.Systems;

/// <summary>
/// Binding wounds with bandages, for the player (village doctors bring their own dressings:
/// <see cref="TendJob"/>). One bandage per wound that needs tending: it stops the bleeding and
/// heals faster, like a doctor's dressing, a little worse done on yourself. Out of a fight it's a
/// short job (<see cref="BandageJob"/>, from the item's menu); in one it's an action for time units.
/// </summary>
public static class Bandaging
{
    public const string BandageDef = "Bandage";

    /// <summary>Ticks of work per wound, out of a fight.</summary>
    public const int TicksPerWound = 90;

    /// <summary>Binding your own wounds is awkward: this much off the dressing's quality.</summary>
    public const float SelfPenalty = 0.1f;

    /// <summary>Bandages on the binder (in hand and in the pack).</summary>
    public static int BandagesOn(Simulation sim, Entity binder)
    {
        var w = sim.World;
        var n = 0;
        if (w.Get<Carrying>(binder).Item is { IsNull: false } held && IsBandage(sim, held))
            n += w.TryGet<Stack>(held, out var s) ? s.Count : 1;
        if (sim.PackOf(binder) is { IsNull: false } pack)
            foreach (var item in sim.StoredIn(pack))
                if (IsBandage(sim, item))
                    n += sim.World.TryGet<Stack>(item, out var s) ? s.Count : 1;
        return n;
    }

    public static bool IsBandage(Simulation sim, Entity item) => sim.World.Get<Thing>(item).Def.Name == BandageDef;

    /// <summary>Wounds on the patient that want a dressing now.</summary>
    public static int WoundsToBind(Simulation sim, Entity patient) =>
        sim.World.TryGet<Health>(patient, out var h) ? h.Injuries.Count(i => i.NeedsTending(sim.Clock.Tick)) : 0;

    /// <summary>Why <paramref name="binder"/> can't bind <paramref name="patient"/>'s wounds now, or null if they can.</summary>
    public static string? CantBind(Simulation sim, Entity binder, Entity patient)
    {
        var w = sim.World;
        if (!w.IsAlive(patient) || !w.Has<Creature>(patient) || w.Get<Creature>(patient).Dead)
            return "no one to bandage";
        if (!w.Get<Creature>(binder).Able || w.Has<Captive>(binder) && binder != patient)
            return "can't do it now";
        if (w.Has<Riding>(binder))
            return "not from the saddle";
        if (WoundsToBind(sim, patient) == 0)
            return "no wounds to bandage";
        if (BandagesOn(sim, binder) == 0)
            return "no bandages";
        if (binder != patient && !Pathfinder.Touches(w.Get<Position>(binder).Cell, w.Get<Position>(patient).Cell))
            return "too far away";
        return null;
    }

    /// <summary>Bind as many of the patient's wounds as there are bandages for. Returns how many were bound.</summary>
    public static int Bind(Simulation sim, Entity binder, Entity patient)
    {
        if (CantBind(sim, binder, patient) != null)
            return 0;
        var w = sim.World;
        var health = w.Get<Health>(patient);
        var medicine = SkillRules.LevelOf(sim, binder, Skill.Medicine);
        var bound = 0;
        foreach (var inj in health.Injuries)
        {
            if (!inj.NeedsTending(sim.Clock.Tick) || !TakeBandage(sim, binder))
                continue;
            var quality = Math.Clamp(0.35f + 0.03f * medicine - (binder == patient ? SelfPenalty : 0f) + sim.Rand.Range(-0.1f, 0.1f), 0.05f, 1f);
            inj.Quality = inj.Tended ? Math.Max(inj.Quality, quality) : quality;
            inj.Tended = true;
            inj.TendedTick = sim.Clock.Tick;
            bound++;
        }
        health.Changed();
        if (bound > 0)
        {
            var who = binder == patient ? "their own" : $"{w.Get<Creature>(patient).Name}'s";
            sim.Messages.Add(MessageKind.Good, $"{w.Get<Creature>(binder).Name} bandaged {who} wounds ({bound}).", w.Get<Position>(patient).Cell);
        }
        return bound;
    }

    /// <summary>Use up one bandage: from the binder's hands first, then the pack.</summary>
    private static bool TakeBandage(Simulation sim, Entity binder)
    {
        var w = sim.World;
        ref var carrying = ref w.Get<Carrying>(binder);
        var from = carrying.Item is { IsNull: false } held && IsBandage(sim, held) ? held
            : sim.PackOf(binder) is { IsNull: false } pack ? sim.StoredIn(pack).FirstOrDefault(i => IsBandage(sim, i), Entity.Null)
            : Entity.Null;
        if (from.IsNull)
            return false;
        var one = sim.TakeFromStack(from, 1);
        if (one == carrying.Item)
            carrying.Item = Entity.Null;
        w.Destroy(one);
        return true;
    }
}

/// <summary>The player binds wounds (theirs, or someone's beside them): a short while per wound, then <see cref="Bandaging.Bind"/>.</summary>
public sealed class BandageJob(Entity patient) : Job
{
    public override Entity Focus => patient;

    protected override IEnumerable<JobStatus> Run()
    {
        if (Bandaging.CantBind(Sim, Pawn, patient) is { } why)
        {
            Fail(why);
            yield break;
        }
        var self = patient == Pawn;
        Report = self ? "Bandaging wounds" : $"Bandaging {Get<Creature>(patient).Name}";
        var wounds = Math.Min(Bandaging.WoundsToBind(Sim, patient), Bandaging.BandagesOn(Sim, Pawn));
        foreach (var s in Wait(Bandaging.TicksPerWound * Math.Max(1, wounds))) yield return s;
        if (!Alive(patient) || Bandaging.CantBind(Sim, Pawn, patient) is { } gone)
        {
            Fail("couldn't finish");
            yield break;
        }
        Bandaging.Bind(Sim, Pawn, patient);
    }
}

/// <summary>Bind wounds with bandages from the avatar's hands or pack: their own, or someone's beside them. Out of a fight.</summary>
public sealed record BandageCommand(Entity Avatar, Entity Patient) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && Bandaging.CantBind(sim, Avatar, Patient) == null)
            AiSystem.Order(sim, Avatar, new BandageJob(Patient));
    }
}
