using System.Text.Json.Serialization;

namespace SupportingCast.Sim.Defs;

/// <summary>
/// A role in the village, dealt to a player or held by an AI villager. Called a trade because
/// "job" already means an AI task. A trade decides where its holder works and, while the AI is
/// running them, what they put first, so a town with empty seats still gets its work done.
/// </summary>
public sealed class TradeDef : Def
{
    /// <summary>The building this trade works from, by def name; empty for none.</summary>
    public string Workplace { get; set; } = "";

    /// <summary>Any number may hold it, so there's always something left to deal.</summary>
    public bool Unlimited { get; set; }

    /// <summary>Changes from the default work priorities (1 highest, 4 lowest, 0 disabled).</summary>
    public Dictionary<WorkType, int> WorkPriorities { get; set; } = [];

    [JsonIgnore]
    public BuildingDef? WorkplaceDef { get; private set; }

    internal override void Resolve(DefDatabase db)
    {
        if (Workplace.Length > 0)
            WorkplaceDef = db.Building(Workplace);
        foreach (var (work, priority) in WorkPriorities)
            if (priority is < 0 or > 4)
                throw new Exception($"Trade '{Name}': priority {priority} for {work} isn't between 0 and 4");
    }
}
