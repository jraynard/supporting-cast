using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Work;

namespace SupportingCast.Sim.Healing;

/// <summary>Regains an empath's mana a little at a time, slower for the pain they carry, faster asleep.</summary>
public sealed class ManaSystem : ISimSystem
{
    public string Name => "Mana";
    public int Interval => SimClock.TicksPerHour / 12;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Query<Mana>())
        {
            ref var mana = ref w.Get<Mana>(e);
            if (mana.Current >= mana.Max || (w.TryGet<Creature>(e, out var c) && c.Dead))
                continue;
            var pain = w.TryGet<Health>(e, out var h) ? Math.Clamp(h.Pain, 0f, 1f) : 0f;
            var asleep = w.TryGet<Mind>(e, out var mind) && mind.Asleep;
            mana.Current = Math.Min(mana.Max, mana.Current + mana.Max * Empathy.RegenPerHour / 12f * (1f - pain) * (asleep ? 2f : 1f));
        }
    }
}

/// <summary>Lay hands on someone in reach and draw one of their wounds into yourself.</summary>
public sealed record LayOnHandsCommand(Entity Avatar, Entity Patient, int Wound) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && !sim.World.Has<WorkSession>(Avatar))
            DrawSession.TryStart(sim, Avatar, Patient, Wound);
    }
}

/// <summary>Cast a mending on a wound you carry.</summary>
public sealed record CastMendingCommand(Entity Avatar, int Wound, MendingDef Mending) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (Mending != null && InventoryCommand.Ready(sim, Avatar) && !sim.World.Has<WorkSession>(Avatar))
            CastSession.TryStart(sim, Avatar, Wound, Mending);
    }
}

/// <summary>Sit and meditate your mana back.</summary>
public sealed record MeditateCommand(Entity Avatar) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && !sim.World.Has<WorkSession>(Avatar))
            MeditateSession.TryStart(sim, Avatar);
    }
}
