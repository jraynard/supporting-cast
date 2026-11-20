using System.Text.Json.Serialization;

namespace HeroesNotIncluded.Sim.Defs;

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

    /// <summary>
    /// Which folk they always are (a company of dwarves and the small one they brought along):
    /// <see cref="Components.Kin"/>. Null: whatever their look is drawn as, like anyone else's.
    /// </summary>
    public Components.Folk? Folk { get; set; }

    /// <summary>
    /// The look they always have, part by part (the grey wanderer's long grey hair and beard; a
    /// dwarf never beardless). Anything left out is derived as anyone's is.
    /// </summary>
    public KindLook? Look { get; set; }

    /// <summary>
    /// What's fixed about how one of these looks (<see cref="Components.Kin"/>), or null when
    /// nothing is: put on them when they come to town and when they come back from a quest.
    /// </summary>
    public Components.Kin? KinOf() =>
        Folk == null && Look == null ? null
            : new Components.Kin { Folk = Folk, Body = Look?.BodyType, Hair = Look?.HairStyle, Beard = Look?.BeardStyle, HairColor = Look?.Color ?? 0 };

    /// <summary>The Melee and Shooting levels they come with (inclusive range).</summary>
    public int SkillMin { get; set; } = 5;
    public int SkillMax { get; set; } = 10;

    /// <summary>Silver coins in their purse on arrival (inclusive range).</summary>
    public int PurseMin { get; set; } = 10;
    public int PurseMax { get; set; } = 30;

    /// <summary>How free they are with coin: what a service is worth to them is its def's worth times this.</summary>
    public float Spend { get; set; } = 1f;

    /// <summary>What they like the taste of: ordering a meal, they may say they fancy one of these (<c>Hospitality.Tastes</c>).</summary>
    public List<Work.FlavourKind> Tastes { get; set; } = [];

    /// <summary>
    /// How likely each temper is in one of these (<see cref="Adventurers.Temper"/>: "Easy", "Rowdy",
    /// "Picky", "Stingy", "Boastful", "Timid"), by weight. Empty: any, evenly.
    /// </summary>
    public Dictionary<string, float> Tempers { get; set; } = [];

    /// <summary>The tempers, parsed, with their weights.</summary>
    [JsonIgnore] public List<(Adventurers.Temper Temper, float Weight)> TemperWeights { get; } = [];

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
        if (!(Spend > 0))
            throw new Exception($"Adventurer kind {Name}: spend must be above 0");
        TemperWeights.Clear();
        foreach (var (name, weight) in Tempers.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            if (!Enum.TryParse<Adventurers.Temper>(name, true, out var temper) || !(weight > 0))
                throw new Exception($"Adventurer kind {Name}: temper '{name}' isn't one of {string.Join(", ", Enum.GetNames<Adventurers.Temper>())}, or its weight isn't above 0");
            TemperWeights.Add((temper, weight));
        }
        if (TemperWeights.Count == 0)
            foreach (var t in Enum.GetValues<Adventurers.Temper>())
                TemperWeights.Add((t, 1f));
        Look?.Resolve(Name);
    }
}

/// <summary>
/// An adventurer kind's fixed look (<see cref="AdventurerKindDef.Look"/>): <c>body</c>, <c>hair</c>
/// and <c>beard</c> by their names (<see cref="Components.BodyType"/>, <see cref="Components.HairStyle"/>,
/// <see cref="Components.BeardStyle"/>), <c>hairColor</c> as RRGGBB hex. Each optional.
/// </summary>
public sealed class KindLook
{
    public string? Body { get; set; }
    public string? Hair { get; set; }
    public string? Beard { get; set; }
    public string? HairColor { get; set; }

    [JsonIgnore] public Components.BodyType? BodyType { get; private set; }
    [JsonIgnore] public Components.HairStyle? HairStyle { get; private set; }
    [JsonIgnore] public Components.BeardStyle? BeardStyle { get; private set; }

    /// <summary>The hair colour as 0xRRGGBB, or 0 when it's left to the look.</summary>
    [JsonIgnore] public uint Color { get; private set; }

    internal void Resolve(string kind)
    {
        BodyType = Parse<Components.BodyType>(kind, "body", Body);
        HairStyle = Parse<Components.HairStyle>(kind, "hair", Hair);
        BeardStyle = Parse<Components.BeardStyle>(kind, "beard", Beard);
        Color = 0;
        if (!string.IsNullOrEmpty(HairColor))
        {
            if (HairColor.Length != 6 || !uint.TryParse(HairColor, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
                throw new Exception($"Adventurer kind {kind}: hairColor '{HairColor}' isn't RRGGBB hex");
            Color = rgb == 0 ? 0x010101u : rgb; // 0 means "derived": pure black is as near as makes no odds
        }
    }

    private static T? Parse<T>(string kind, string field, string? text) where T : struct, Enum
    {
        if (string.IsNullOrEmpty(text))
            return null;
        if (!Enum.TryParse<T>(text, true, out var v) || !Enum.IsDefined(v))
            throw new Exception($"Adventurer kind {kind}: {field} '{text}' isn't one of {string.Join(", ", Enum.GetNames<T>())}");
        return v;
    }
}

/// <summary>
/// A party that can come to town: who's in it, where they hang about and how long they stay before
/// moving on. The round's <c>PartySystem</c> picks one at a time.
/// </summary>
public sealed class PartyDef : Def
{
    /// <summary>Adventurer kinds by name, one member each: those who always come.</summary>
    public List<string> Members { get; set; } = [];

    /// <summary>
    /// More of the company, in the order they join it as the town grows (<see cref="SizeFor"/>):
    /// none at <see cref="CoreAt"/> players or fewer, all of them at <see cref="FullAt"/>. A full
    /// company of fourteen would swamp a town of five; a town of twelve can take them all.
    /// </summary>
    public List<string> ExtraMembers { get; set; } = [];

    /// <summary>Players in the round (bots too) at or under which only <see cref="Members"/> come.</summary>
    public int CoreAt { get; set; } = 4;

    /// <summary>Players in the round from which the whole company comes, <see cref="ExtraMembers"/> and all.</summary>
    public int FullAt { get; set; } = 12;

    /// <summary>
    /// Each member always has the name in the same place in <see cref="Names"/> (members, then the
    /// extras): a company everyone knows by name. Otherwise names are shuffled out to them.
    /// </summary>
    public bool FixedNames { get; set; }

    /// <summary>
    /// A company that comes now and then, not in the usual run (<see cref="Adventurers.PartySystem"/>):
    /// at most one such a round, with <see cref="Chance"/>, on a day from <see cref="FromDay"/> to
    /// <see cref="ToDay"/> of the season, in place of the party that would have come next.
    /// </summary>
    public bool Rare { get; set; }

    /// <summary>A rare company's chance of coming in a round (each rare one rolled in turn; the first to come is the round's).</summary>
    public float Chance { get; set; } = 0.25f;

    /// <summary>The days of the season (1: the deal's) a rare company may come on.</summary>
    public int FromDay { get; set; } = 1;
    public int ToDay { get; set; } = 3;

    /// <summary>The town role (<see cref="TownRole"/>) of the building they make for and loiter in: the tavern.</summary>
    public string Haunt { get; set; } = "Tavern";

    /// <summary>Game hours in town before they move on (±25%).</summary>
    public float StayHours { get; set; } = 12;

    /// <summary>Names members are given, in a rolled order.</summary>
    public List<string> Names { get; set; } = [];

    /// <summary>The chance they come with a wagon, and so the chance it's come off the road the worse for it (<c>Tinkering.Wagons</c>).</summary>
    public float WagonChance { get; set; } = 0.5f;

    /// <summary>
    /// A lone traveller (a pedlar, a pilgrim), not an adventuring party: they come of an afternoon on a
    /// schedule of their own, alongside whatever party's in town, eat, drink and take a room for a
    /// night or two, and move on. They never quest, and their word of the town counts for less.
    /// </summary>
    public bool Traveller { get; set; }

    /// <summary>A traveller's nights in town (inclusive range): they leave the morning after the last.</summary>
    public int NightsMin { get; set; } = 1;
    public int NightsMax { get; set; } = 1;

    [JsonIgnore] public List<AdventurerKindDef> MemberDefs { get; } = [];
    [JsonIgnore] public List<AdventurerKindDef> ExtraMemberDefs { get; } = [];
    [JsonIgnore] public TownRole HauntRole { get; private set; }

    /// <summary>
    /// How many of the company come to a round of so many players: the core, and a share of the
    /// extras growing from none at <see cref="CoreAt"/> to all at <see cref="FullAt"/>.
    /// </summary>
    public int SizeFor(int players)
    {
        if (ExtraMemberDefs.Count == 0)
            return MemberDefs.Count;
        var share = Math.Clamp((players - CoreAt) / (float)Math.Max(1, FullAt - CoreAt), 0f, 1f);
        return MemberDefs.Count + (int)MathF.Round(ExtraMemberDefs.Count * share);
    }

    /// <summary>Who comes, in order, for a round of so many players (<see cref="SizeFor"/>).</summary>
    public IEnumerable<AdventurerKindDef> MembersFor(int players) => MemberDefs.Concat(ExtraMemberDefs).Take(SizeFor(players));

    internal override void Resolve(DefDatabase db)
    {
        MemberDefs.Clear();
        foreach (var m in Members)
            MemberDefs.Add(db.AdventurerKind(m));
        ExtraMemberDefs.Clear();
        foreach (var m in ExtraMembers)
            ExtraMemberDefs.Add(db.AdventurerKind(m));
        if (MemberDefs.Count == 0)
            throw new Exception($"Party {Name} has no members");
        if (Names.Count < MemberDefs.Count + ExtraMemberDefs.Count)
            throw new Exception($"Party {Name} needs at least as many names as members");
        if (ExtraMemberDefs.Count > 0 && CoreAt >= FullAt)
            throw new Exception($"Party {Name}: coreAt must be below fullAt");
        if (Rare && (!(Chance > 0) || Chance > 1 || FromDay < 1 || FromDay > ToDay))
            throw new Exception($"Party {Name}: a rare company needs a chance in (0, 1] and days from no later than to");
        if (Rare && Traveller)
            throw new Exception($"Party {Name}: a traveller can't be a rare company");
        HauntRole = TownRoles.Parse(Haunt, $"Party '{Name}' haunt");
        if (NightsMin < 1 || NightsMin > NightsMax)
            throw new Exception($"Party {Name}: nights must be at least 1, min no more than max");
    }
}
