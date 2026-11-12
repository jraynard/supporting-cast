using System.Text.Json.Serialization;

namespace SupportingCast.Sim.Defs;

/// <summary>
/// A sort of adventurer a party can be made of: their gear, how good they are with it and how much
/// coin they bring. Gear matters beyond looks: what the town sells and mends them is what they take
/// into the dungeon.
/// </summary>
public sealed class AdventurerKindDef : Def
{
    public string Weapon { get; set; } = "";
    public List<string> Apparel { get; set; } = [];

    /// <summary>The clothes under their armor (<see cref="OutfitDef"/>), dyes rolled per adventurer.</summary>
    public string? Outfit { get; set; }

    /// <summary>The Melee and Shooting levels they come with (inclusive range).</summary>
    public int SkillMin { get; set; } = 5;
    public int SkillMax { get; set; } = 10;

    /// <summary>Silver coins in their purse on arrival (inclusive range).</summary>
    public int PurseMin { get; set; } = 10;
    public int PurseMax { get; set; } = 30;

    [JsonIgnore] public OutfitDef? OutfitDef { get; private set; }
    [JsonIgnore] public ThingDef? WeaponDef { get; private set; }
    [JsonIgnore] public List<ThingDef> ApparelDefs { get; } = [];

    internal override void Resolve(DefDatabase db)
    {
        OutfitDef = string.IsNullOrEmpty(Outfit) ? null : db.Outfit(Outfit);
        ApparelDefs.Clear();
        if (!string.IsNullOrEmpty(Weapon))
        {
            WeaponDef = db.Thing(Weapon);
            if (WeaponDef.Weapon == null)
                throw new Exception($"Adventurer kind {Name}: {Weapon} isn't a weapon");
        }
        foreach (var a in Apparel)
        {
            var def = db.Thing(a);
            if (def.Apparel == null)
                throw new Exception($"Adventurer kind {Name}: {a} isn't apparel");
            ApparelDefs.Add(def);
        }
        if (SkillMin > SkillMax || PurseMin > PurseMax || PurseMin < 0)
            throw new Exception($"Adventurer kind {Name}: a min is above its max");
    }
}

/// <summary>
/// A party that can come to town: who's in it, where they hang about and how long they stay before
/// moving on. The round's <c>PartySystem</c> picks one at a time.
/// </summary>
public sealed class PartyDef : Def
{
    /// <summary>Adventurer kinds by name, one member each.</summary>
    public List<string> Members { get; set; } = [];

    /// <summary>The building they make for and loiter in (a building def name): the tavern.</summary>
    public string Haunt { get; set; } = "Inn";

    /// <summary>Game hours in town before they move on (±25%).</summary>
    public float StayHours { get; set; } = 12;

    /// <summary>Names members are given, in a rolled order.</summary>
    public List<string> Names { get; set; } = [];

    [JsonIgnore] public List<AdventurerKindDef> MemberDefs { get; } = [];
    [JsonIgnore] public BuildingDef? HauntDef { get; private set; }

    internal override void Resolve(DefDatabase db)
    {
        MemberDefs.Clear();
        foreach (var m in Members)
            MemberDefs.Add(db.AdventurerKind(m));
        if (MemberDefs.Count == 0)
            throw new Exception($"Party {Name} has no members");
        if (Names.Count < MemberDefs.Count)
            throw new Exception($"Party {Name} needs at least as many names as members");
        HauntDef = Haunt.Length > 0 ? db.Building(Haunt) : null;
    }
}
