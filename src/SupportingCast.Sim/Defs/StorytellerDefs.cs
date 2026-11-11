using System.Text.Json.Serialization;

namespace SupportingCast.Sim.Defs;

/// <summary>How hard the storyteller pushes: when raids start, how often they come and how big they get.</summary>
public sealed class DifficultyDef : Def
{
    /// <summary>Never sends raids.</summary>
    public bool Peaceful { get; set; }

    public float GraceDays { get; set; } = 5;
    public float MeanDaysBetween { get; set; } = 4;
    public float PointsFactor { get; set; } = 1;
    public int MaxRaiders { get; set; } = 5;

    /// <summary>Days between wild events (predators, migrations, dragon visitors), ±40%.</summary>
    public float EventsMeanDays { get; set; } = 3;
}

/// <summary>A sort of raider a raid can be made of, and what it costs in raid points.</summary>
public sealed class RaiderKindDef : Def
{
    public float Cost { get; set; } = 40;
    public string Weapon { get; set; } = "";
    public List<string> Apparel { get; set; } = [];

    /// <summary>The clothes under their armor (<see cref="OutfitDef"/>), dyes rolled per raider.</summary>
    public string? Outfit { get; set; }

    [JsonIgnore] public OutfitDef? OutfitDef { get; private set; }

    /// <summary>The Melee and Shooting levels they come with (inclusive range).</summary>
    public int SkillMin { get; set; } = 3;
    public int SkillMax { get; set; } = 8;

    /// <summary>
    /// The tier of their gear (<see cref="TierDef"/>): the storyteller sends kinds at most one tier
    /// past the village's best gear, so a bronze village meets bronze raiders, not knights.
    /// </summary>
    public TierRef Tier { get; set; }

    [JsonIgnore] public ThingDef? WeaponDef { get; private set; }
    [JsonIgnore] public List<ThingDef> ApparelDefs { get; } = [];

    internal override void Resolve(DefDatabase db)
    {
        Tier = db.ResolveTier(Tier, $"Raider kind '{Name}'");
        OutfitDef = string.IsNullOrEmpty(Outfit) ? null : db.Outfit(Outfit);
        if (!string.IsNullOrEmpty(Weapon))
        {
            WeaponDef = db.Thing(Weapon);
            if (WeaponDef.Weapon == null)
                throw new Exception($"Raider kind {Name}: {Weapon} isn't a weapon");
        }
        foreach (var a in Apparel)
        {
            var def = db.Thing(a);
            if (def.Apparel == null)
                throw new Exception($"Raider kind {Name}: {a} isn't apparel");
            ApparelDefs.Add(def);
        }
    }
}
