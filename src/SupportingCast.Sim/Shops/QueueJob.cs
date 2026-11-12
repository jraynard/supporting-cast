using SupportingCast.Sim.Ai;

namespace SupportingCast.Sim.Shops;

/// <summary>
/// A customer takes their place in a shop's line (<see cref="ShopSystem.PlaceOf"/>) and stands
/// there a moment. It ends often, so the brain sends them one place forward as the line moves.
/// <paramref name="shop"/> is what it's called (<see cref="Systems.Buildings.Called"/>).
/// </summary>
public sealed class QueueJob(int3 cell, string shop, bool front) : Job
{
    /// <summary>Ticks they stand before looking again where their place is.</summary>
    public const int StandTicks = 60;

    public override bool Interruptible => true;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = $"Lining up at {shop}";
        foreach (var s in GoTo(cell, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        Report = front ? $"Waiting to be served at {shop}" : $"Waiting in line at {shop}";
        foreach (var s in Wait(StandTicks)) yield return s;
    }
}
