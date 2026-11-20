using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Director;

/// <summary>
/// A decision for the Director made outside the sim: by the host's dev panel, or later by a mind
/// that can't run inside a deterministic sim (a host-side model reading the round,
/// docs/DIRECTOR-PLAN.md). It goes through the command stream like anything a player does, so
/// every peer applies it alike and a replay has it. It can only pick among what the season
/// already allows: the next unsettled beat's <paramref name="Kind"/> (one of that beat's kinds),
/// and an <paramref name="Incident"/> by name, now, if it could happen. Anything else is ignored.
/// </summary>
public sealed record DirectorChoiceCommand(string? Kind, string? Incident) : ISimCommand, IHostCommand
{
    public void Apply(Simulation sim)
    {
        if (Kind is { Length: > 0 and <= 64 } name0 && CrisisTypes.Find(name0) is { } type)
            sim.Director.Choose(sim, type.Kind);
        if (Incident is { Length: > 0 and <= 64 } name && !sim.Director.CallIncident(sim, name))
            sim.Messages.Add(MessageKind.Info, $"No {name} could come now.");
    }
}
