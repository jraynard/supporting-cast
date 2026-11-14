namespace HeroesNotIncluded.Sim.Defs;

/// <summary>Who a ghost's errand is for (<see cref="GhostErrandDef"/>).</summary>
public enum GhostErrandKind
{
    /// <summary>A villager mourning a death.</summary>
    Grieving,

    /// <summary>Someone asleep.</summary>
    Sleeper,

    /// <summary>An adventurer asking the way.</summary>
    Lost,
}

/// <summary>
/// A piece of unfinished business for the dead (data/defs/afterlife.json; <c>Afterlife.Errands</c>):
/// linger near someone it's for, long enough, and it's done, for a little Renown. Gives the dead
/// something to do for the town besides wait.
/// </summary>
public sealed class GhostErrandDef : Def
{
    public GhostErrandKind Kind { get; set; }

    /// <summary>How near the ghost must stay, cells.</summary>
    public float Reach { get; set; } = 2;

    /// <summary>How long it must linger, real seconds at 1x.</summary>
    public float Seconds { get; set; } = 20;

    public float Renown { get; set; } = 1;

    /// <summary>Game hours a ghost rests after any errand before the next one counts.</summary>
    public float RestHours { get; set; } = 1;

    public int Ticks => (int)(Seconds * SimClock.TicksPerSecond);

    internal override void Resolve(DefDatabase db)
    {
        if (Reach <= 0 || Seconds <= 0 || Renown < 0 || RestHours < 0)
            throw new Exception($"Ghost errand '{Name}': reach and seconds must be positive");
    }
}
