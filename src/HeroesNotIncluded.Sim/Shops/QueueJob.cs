using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ai;

namespace HeroesNotIncluded.Sim.Shops;

/// <summary>
/// A customer takes their place in a shop's line (<see cref="ShopSystem.PlaceOf"/>) and stands
/// there a moment. It ends often, so the brain sends them one place forward as the line moves.
/// <paramref name="shop"/> is what it's called (<see cref="Systems.Buildings.Called"/>).
/// </summary>
/// <summary>
/// Lying on a bed at a shop that lines its customers up on its beds (the shrine's pallets), awake,
/// until seen to: the front of that line.
/// </summary>
public sealed class LieAbedJob(Entity bed, string shop) : Job
{
    public override bool Interruptible => true;
    public override Entity Focus => bed;
    public override bool LiesDown => _down;

    private bool _down;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = $"Going to lie down at {shop}";
        if (!Alive(bed)) yield break;
        foreach (var s in GoTo(Get<Thing>(bed).Cell, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        Report = $"Lying on a pallet at {shop}, waiting to be seen to";
        _down = true;
        foreach (var s in Wait(QueueJob.StandTicks * 4)) yield return s;
        _down = false;
    }
}

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
