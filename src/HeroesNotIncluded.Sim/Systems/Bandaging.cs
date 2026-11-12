using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;

namespace HeroesNotIncluded.Sim.Systems;

/// <summary>
/// Binding wounds with bandages, for the player (village doctors bring their own dressings:
/// <see cref="TendJob"/>). One bandage per wound that needs tending: it stops the bleeding and
/// heals faster, like a doctor's dressing, a little worse done on yourself. It's done by hand
/// (<see cref="Healing.BandageSession"/>), from the item's menu or a healer's hands. Bandages come
/// from the binder's hands, their pack, or a chest within reach (the shrine's supplies).
/// </summary>
public static class Bandaging
{
    public const string BandageDef = "Bandage";

    /// <summary>Ticks of work per wound, out of a fight.</summary>
    public const int TicksPerWound = 90;

    /// <summary>Binding your own wounds is awkward: this much off the dressing's quality.</summary>
    public const float SelfPenalty = 0.1f;

    /// <summary>Bandages to hand: held, in the pack, and in chests within reach.</summary>
    public static int BandagesOn(Simulation sim, Entity binder)
    {
        var w = sim.World;
        var n = 0;
        if (w.Get<Carrying>(binder).Item is { IsNull: false } held && IsBandage(sim, held))
            n += w.TryGet<Stack>(held, out var s) ? s.Count : 1;
        foreach (var source in Sources(sim, binder))
            foreach (var item in sim.StoredIn(source))
                if (IsBandage(sim, item))
                    n += sim.World.TryGet<Stack>(item, out var s) ? s.Count : 1;
        return n;
    }

    /// <summary>Where bandages can be taken from besides the hands: the pack, then chests within reach (in cell order).</summary>
    private static IEnumerable<Entity> Sources(Simulation sim, Entity binder)
    {
        if (sim.PackOf(binder) is { IsNull: false } pack)
            yield return pack;
        var at = sim.World.Get<Position>(binder).Cell;
        var seen = new HashSet<Entity>();
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var c = at + new Brutal.Numerics.int3(dx, dy, 0);
                if (!sim.Map.InBounds(c))
                    continue;
                var s = sim.Map.Structure[sim.Map.Idx(c)];
                if (!s.IsNull && seen.Add(s) && Inventory.CanReachContainer(sim, binder, s) && !sim.IsPack(s))
                    yield return s;
            }
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
        if (!w.Get<Creature>(binder).Able)
            return "can't do it now";
        if (WoundsToBind(sim, patient) == 0)
            return "no wounds to bandage";
        if (BandagesOn(sim, binder) == 0)
            return "no bandages";
        if (binder != patient && !Pathfinder.Touches(w.Get<Position>(binder).Cell, w.Get<Position>(patient).Cell))
            return "too far away";
        return null;
    }

    /// <summary>
    /// Bind as many of the patient's wounds as there are bandages for, at a dressing quality (by
    /// the binder's Medicine if not given). Returns how many were bound.
    /// </summary>
    public static int Bind(Simulation sim, Entity binder, Entity patient, float? dressing = null)
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
            var quality = Math.Clamp((dressing ?? 0.35f + 0.03f * medicine + sim.Rand.Range(-0.1f, 0.1f)) - (binder == patient ? SelfPenalty : 0f), 0.05f, 1f);
            inj.Quality = inj.Tended ? Math.Max(inj.Quality, quality) : quality;
            inj.Tended = true;
            inj.TendedTick = sim.Clock.Tick;
            bound++;
        }
        health.Changed();
        if (bound > 0 && binder != patient)
            sim.Shops.Treated(sim, patient, binder);
        if (bound > 0)
        {
            var who = binder == patient ? "their own" : $"{w.Get<Creature>(patient).Name}'s";
            sim.Messages.Add(MessageKind.Good, $"{w.Get<Creature>(binder).Name} bandaged {who} wounds ({bound}).", w.Get<Position>(patient).Cell);
        }
        return bound;
    }

    /// <summary>Use up one bandage: from the binder's hands first, then the pack, then a chest within reach.</summary>
    internal static bool TakeBandage(Simulation sim, Entity binder)
    {
        var w = sim.World;
        ref var carrying = ref w.Get<Carrying>(binder);
        var from = carrying.Item is { IsNull: false } held && IsBandage(sim, held) ? held
            : Sources(sim, binder).Select(s => sim.StoredIn(s).FirstOrDefault(i => IsBandage(sim, i), Entity.Null)).FirstOrDefault(i => !i.IsNull, Entity.Null);
        if (from.IsNull)
            return false;
        var one = sim.TakeFromStack(from, 1);
        if (one == carrying.Item)
            carrying.Item = Entity.Null;
        w.Destroy(one);
        return true;
    }
}

/// <summary>Bind wounds with bandages to hand: your own, or someone's beside you, by hand (<see cref="Healing.BandageSession"/>).</summary>
public sealed record BandageCommand(Entity Avatar, Entity Patient) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && !sim.World.Has<Work.WorkSession>(Avatar) && Bandaging.CantBind(sim, Avatar, Patient) == null)
            Healing.BandageSession.TryStart(sim, Avatar, Patient);
    }
}
