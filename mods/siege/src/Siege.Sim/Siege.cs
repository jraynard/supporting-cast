using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;

namespace HeroesNotIncluded.Siege;

/// <summary>
/// Siege Night, a game mode as a mod (docs/MODDING.md, "A new game mode"): the round begins at dusk, with
/// no season to it; the bandits come in waves through the night, each harder; at dawn, if anyone of the
/// town still stands, it's won.
/// </summary>
public sealed class SiegeMod : ISimMod
{
    public void Register(SimRegistry registry)
    {
        registry.Mode(new SiegeMode());
        registry.System(() => new SiegeSystem(), after: "Director");
    }
}

public sealed class SiegeMode : IGameMode
{
    public const string Id = "siege";

    /// <summary>When the night begins and ends (hours of the day).</summary>
    public const float Dusk = 19f, Dawn = 6f;

    public string Name => Id;
    public string Label => "Siege Night";
    public string Description => "The bandits come at nightfall, again and again. Hold the town till dawn.";
    public bool Seasonal => false;
    public bool Crises => false;
    public float? StartHour => Dusk;

    /// <summary>Dawn of the next day, and someone's still standing: the night's held (nobody left ends it anyway).</summary>
    public RoundEnd? Check(Simulation sim) =>
        sim.Clock.Tick >= DawnTick(sim)
            ? new RoundEnd(RoundOutcome.Won, "Dawn, and the town still stands.")
            : null;

    public string? Status(Simulation sim)
    {
        var hours = (int)Math.Ceiling((DawnTick(sim) - sim.Clock.Tick) / (double)SimClock.TicksPerHour);
        return hours <= 1 ? "Siege Night: dawn within the hour" : $"Siege Night: dawn in {hours} hours";
    }

    /// <summary>The tick of the dawn after the round began.</summary>
    public static long DawnTick(Simulation sim) => sim.Round.TickAt(sim, 2, Dawn);
}

/// <summary>
/// The night's waves: each a raid by the road, sent by hand (the Director sends none of its own in this
/// mode, but plays out what's sent), its strength rising. Its own state, saved.
/// </summary>
public sealed class SiegeSystem : ISimSystem, ISaveState
{
    /// <summary>Hours after dusk each wave comes, and how strong it is.</summary>
    public static readonly (float Hours, float Strength)[] Waves = [(0.5f, 0.8f), (3.5f, 1.1f), (6.5f, 1.5f)];

    public int Sent { get; private set; }

    public string Name => "Siege";
    public int Interval => SimClock.TicksPerHour / 12;

    public void Update(Simulation sim)
    {
        if (sim.Round.Mode != SiegeMode.Id || sim.Round.Phase != RoundPhase.Playing || Sent >= Waves.Length)
            return;
        var (hours, strength) = Waves[Sent];
        if (sim.Clock.Tick < sim.Round.StartTick + SimClock.HoursToTicks(hours))
            return;
        Sent++;
        if (sim.Director.SendRaid(sim, (Sent - 1) % 4, strength) != null)
            sim.Messages.Add(MessageKind.Danger, Sent == Waves.Length ? "The last of them, and the most: they've come in strength." : "Torches on the road: they're coming.", null, "Siege Night");
    }

    public void Save(SaveWriter w, Simulation sim) => w.Int(Sent);

    public void Load(SaveReader r, Simulation sim) => Sent = Math.Clamp(r.Int(), 0, Waves.Length);
}
