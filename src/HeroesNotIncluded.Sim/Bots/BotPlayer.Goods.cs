using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Bots;

/// <summary>Gear made for sale and put on show in the shop, and sold to whoever asks.</summary>
public sealed partial class BotPlayer
{
    /// <summary>One step of selling a piece on show to who asked for it. False when it's over.</summary>
    private bool Sell(Simulation sim, Entity me, PerceptionView view, Entity buyer, long now) => false;
}
