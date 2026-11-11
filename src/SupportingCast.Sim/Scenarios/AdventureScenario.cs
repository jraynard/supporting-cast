namespace SupportingCast.Sim.Scenarios;

/// <summary>Which Adventure opening a game began with (none: the sandbox village, demos, tests).</summary>
public enum StartKind : byte
{
    None,

    /// <summary>The Ashford prologue: Orla's egg, her wagon, the long road (<see cref="Prologue"/>).</summary>
    EggBearer,

    /// <summary>Combat on: a captured dragon rider breaks out with their dragon and survives in the wild.</summary>
    Escape,

    /// <summary>Peaceful: a bargain with a sorcerer, paid by building him a tower.</summary>
    Pact,
}

/// <summary>
/// The Adventure's story state, kept by the sim rather than on the player's character so every
/// co-op peer shares it and it carries across a load: which opening this is, and the difficulty
/// waiting to be switched on once the story allows raids. Saved as a system (<see cref="Saving.ISaveState"/>);
/// it only goes into the state hash once an Adventure has set it, so sandbox games, demos and
/// tests hash as they always have.
/// </summary>
public sealed class StoryState : ISimSystem, Saving.ISaveState
{
    public string Name => "Story";

    /// <summary>Hourly: checks the tower against a pact in force. The openings' quicker beats run in <see cref="PrologueSystem"/>.</summary>
    public int Interval => SimClock.TicksPerHour;

    public StartKind Start { get; set; }

    /// <summary>The storyteller difficulty (def name) to switch on when the story lets raids begin; null for none.</summary>
    public string? PendingDifficulty { get; set; }

    /// <summary>The tower owed to the sorcerer on this map (<see cref="Systems.TowerPact"/>), if any.</summary>
    public Systems.PactState? Pact { get; set; }

    /// <summary>Escape's camp on this map (<see cref="EscapeCamp"/>), if any.</summary>
    public EscapeState? Escape { get; set; }

    public bool IsAdventure => Start != StartKind.None;

    public void Update(Simulation sim) => Systems.TowerPact.Evaluate(sim);

    public void Save(Saving.SaveWriter w, Simulation sim)
    {
        w.Int((int)Start);
        w.String(PendingDifficulty ?? "");
        w.Bool(Escape != null);
        if (Escape is { } e)
        {
            w.Int3(e.Gate);
            w.Int3(e.CageMin);
            w.Int3(e.CageMax);
            w.Int3(e.DragonSpot);
            w.Entity(e.Chest);
            w.Bool(e.Reinforced);
            w.Entity(e.KeyHolder);
            w.Long(e.NoSupperBefore);
        }
        w.Bool(Pact != null);
        if (Pact is not { } p)
            return;
        w.String(p.Tower.Name);
        w.Int3(p.Site);
        w.Bool(p.Done);
        w.Int(p.Met.Length);
        foreach (var m in p.Met)
            w.Bool(m);
        w.Int(p.Levels);
        w.Float(p.WallShare);
    }

    public void Load(Saving.SaveReader r, Simulation sim)
    {
        Start = (StartKind)r.Int();
        PendingDifficulty = r.String() is { Length: > 0 } d ? d : null;
        Escape = r.Bool()
            ? new EscapeState
            {
                Gate = r.Int3(), CageMin = r.Int3(), CageMax = r.Int3(), DragonSpot = r.Int3(), Chest = r.Entity(), Reinforced = r.Bool(),
                KeyHolder = r.Entity(), NoSupperBefore = r.Long(),
            }
            : null;
        Pact = null;
        if (!r.Bool())
            return;
        var tower = sim.Defs.Tower(r.String());
        var site = r.Int3();
        var done = r.Bool();
        var met = new bool[r.Count(64)];
        for (var i = 0; i < met.Length; i++)
            met[i] = r.Bool();
        Pact = new Systems.PactState { Tower = tower, Site = site, Done = done, Met = met, Levels = r.Int(), WallShare = r.Float() };
    }

    /// <summary>Mixed into <see cref="Simulation.StateHash"/> (0 while unset).</summary>
    public long Fingerprint
    {
        get
        {
            if (Start == StartKind.None)
                return 0;
            var h = (long)Start << 32 | (uint)(PendingDifficulty?.Length ?? 0);
            if (Escape is { } e)
                h ^= (e.Reinforced ? 1L << 41 : 0) ^ ((long)e.Gate.X << 20) ^ e.Gate.Y;
            if (Pact is { } p)
                h ^= (p.Done ? 1L << 40 : 0) ^ ((long)p.Met.Count(m => m) << 44) ^ ((long)p.Levels << 50) ^ p.Site.X * 31L + p.Site.Y;
            return h;
        }
    }
}
