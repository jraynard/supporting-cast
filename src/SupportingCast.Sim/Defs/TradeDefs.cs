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

    /// <summary>
    /// Stations (thing def names) whose every recipe the holder knows. A trade is its know-how: the
    /// blacksmith shouldn't have to rediscover iron. Skill still decides which they can make yet.
    /// </summary>
    public List<string> Knows { get; set; } = [];

    /// <summary>The least the holder has in a skill: a smith can smith.</summary>
    public Dictionary<Components.Skill, int> Skills { get; set; } = [];

    /// <summary>
    /// The holder's mana, for a trade with the gift (the Healer's empathy, <c>Healing/</c>); 0 for
    /// none. Having it is what lets them lay on hands and cast.
    /// </summary>
    public float Mana { get; set; }

    /// <summary>What the holder starts the round with: the tools of the trade and a little stock.</summary>
    public List<ThingCount> Kit { get; set; } = [];

    [JsonIgnore]
    public List<ThingDef> KnownStations { get; } = [];

    [JsonIgnore]
    public BuildingDef? WorkplaceDef { get; private set; }

    internal override void Resolve(DefDatabase db)
    {
        if (Workplace.Length > 0)
            WorkplaceDef = db.Building(Workplace);
        KnownStations.Clear();
        foreach (var station in Knows)
            KnownStations.Add(db.Thing(station));
        foreach (var item in Kit)
            item.ThingDef = db.Thing(item.Def);
        foreach (var (skill, level) in Skills)
            if (level is < 0 or > Components.Skills.Max)
                throw new Exception($"Trade '{Name}': {skill} {level} is out of range");
        foreach (var (work, priority) in WorkPriorities)
            if (priority is < 0 or > 4)
                throw new Exception($"Trade '{Name}': priority {priority} for {work} isn't between 0 and 4");
    }
}
