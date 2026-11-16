using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Bots;

/// <summary>The labourer's work: errands off the board, gathering, hauling, the festival's sites.</summary>
public sealed partial class BotPlayer
{
    /// <summary>The labourer between errands. True while at it.</summary>
    private bool Labour(Simulation sim, Entity me, PerceptionView view, long now) => false;
}
