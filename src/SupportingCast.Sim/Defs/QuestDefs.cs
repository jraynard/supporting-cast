using System.Text.Json.Serialization;

namespace SupportingCast.Sim.Defs;

/// <summary>
/// Somewhere a party can be sent (<c>Expeditions.ExpeditionSystem</c>). A round rolls its true
/// danger and reward from these ranges and tells nobody: the Quest Broker works from estimates,
/// rumour and what parties come back saying. Danger is measured against a party's strength (an
/// ordinary, well-kept adventurer is about 1).
/// </summary>
public sealed class QuestDef : Def
{
    public float DangerMin { get; set; } = 2;
    public float DangerMax { get; set; } = 4;

    /// <summary>Silver the whole party brings back if it succeeds.</summary>
    public int RewardMin { get; set; } = 40;
    public int RewardMax { get; set; } = 80;

    /// <summary>Game hours away, there and back.</summary>
    public float Hours { get; set; } = 8;

    /// <summary>What it wounds with.</summary>
    public List<DamageType> Threats { get; set; } = [DamageType.Cut];

    /// <summary>What's said about it over a cup ("{0}": how dangerous the teller thinks it is).</summary>
    public List<string> Rumours { get; set; } = [];

    internal override void Resolve(DefDatabase db)
    {
        if (DangerMin <= 0 || DangerMin > DangerMax || RewardMin < 0 || RewardMin > RewardMax || Hours <= 0 || Threats.Count == 0)
            throw new Exception($"Quest '{Name}': bad ranges or no threats");
    }
}
