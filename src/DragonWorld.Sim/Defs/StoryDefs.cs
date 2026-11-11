using System.Text.Json.Serialization;

namespace DragonWorld.Sim.Defs;

/// <summary>
/// Something a story character says, page by page (the game shows it as a dialogue window). The
/// last page's button reads <see cref="Accept"/>; pressing it is the story beat (taking the egg,
/// striking the bargain). Kept in defs so the words can change without touching code.
/// </summary>
public sealed class DialogueDef : Def
{
    /// <summary>Who's speaking (the window title); the creature's own name when empty.</summary>
    public string Speaker { get; set; } = "";

    public List<string> Lines { get; set; } = [];

    /// <summary>The last page's button.</summary>
    public string Accept { get; set; } = "Continue";

    internal override void Resolve(DefDatabase db)
    {
        if (Lines.Count == 0)
            throw new Exception($"Dialogue '{Name}' has no lines");
    }
}

/// <summary>
/// What a tower must be to settle a pact (<c>Systems.TowerPact</c>): enclosed rooms stacked
/// <see cref="Levels"/> high within <see cref="Radius"/> of its site, mostly walled with
/// <see cref="Walls"/>, holding each of <see cref="Contains"/> somewhere inside.
/// </summary>
public sealed class TowerDef : Def
{
    /// <summary>Cells from the site (the ley stone) the tower's rooms must reach into.</summary>
    public int Radius { get; set; } = 10;

    /// <summary>Indoor rooms stacked one above the other, each overlapping the one below.</summary>
    public int Levels { get; set; } = 3;

    /// <summary>Wall defs that count as the tower's kind of wall (stone, say).</summary>
    public List<string> Walls { get; set; } = [];

    /// <summary>Share of the tower's walls (and doors) that must be of <see cref="Walls"/>.</summary>
    public float WallShare { get; set; } = 0.75f;

    /// <summary>Buildings that must stand in the tower's rooms, one of each.</summary>
    public List<string> Contains { get; set; } = [];

    [JsonIgnore] public List<ThingDef> WallDefs { get; } = [];
    [JsonIgnore] public List<ThingDef> ContainsDefs { get; } = [];

    internal override void Resolve(DefDatabase db)
    {
        if (Levels < 1 || Radius < 1)
            throw new Exception($"Tower '{Name}' needs at least one level and a positive radius");
        WallDefs.Clear();
        ContainsDefs.Clear();
        foreach (var w in Walls)
            WallDefs.Add(db.Thing(w));
        foreach (var c in Contains)
            ContainsDefs.Add(db.Thing(c));
    }
}
