using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Healing;

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

/// <summary>Lay hands on someone in reach and feel them over, to find what hurts (<see cref="FeelSession"/>).</summary>
public sealed record FeelCommand(Entity Avatar, Entity Patient) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && !sim.World.Has<WorkSession>(Avatar))
            FeelSession.TryStart(sim, Avatar, Patient);
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

/// <summary>
/// Begin weaving a mending, meant for a wound you carry (0 for none). The runes come as the
/// weave's own input (<see cref="CastSession"/>), so what it makes is decided as it's woven.
/// </summary>
public sealed record WeaveCommand(Entity Avatar, int Wound) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && !sim.World.Has<WorkSession>(Avatar))
            CastSession.TryStart(sim, Avatar, Wound);
    }
}

/// <summary>Begin weaving a mending into a charm, to keep or sell (<see cref="Charms"/>).</summary>
public sealed record WeaveCharmCommand(Entity Avatar) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && !sim.World.Has<WorkSession>(Avatar))
            CastSession.TryStart(sim, Avatar, 0, intoCharm: true);
    }
}

/// <summary>Tear a charm you have on you: its mending lands on you.</summary>
public sealed record TearCharmCommand(Entity Avatar, Entity Item) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && !sim.World.Has<WorkSession>(Avatar))
            Charms.Tear(sim, Avatar, Item);
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
