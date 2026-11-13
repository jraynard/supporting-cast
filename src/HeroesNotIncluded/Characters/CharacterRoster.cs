using System.Text.Json;
using System.Text.Json.Nodes;
using Narsil;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Characters;

/// <summary>
/// A character the player made once and plays in every village, solo or with friends: who they
/// are and what they look like, and what they've done across rounds (<see cref="Progress"/>).
/// Only that carries over: skills and gear start fresh in every round. The sim never sees one,
/// only the name, look, traits and clothes a game sends from it.
/// </summary>
public sealed record SavedCharacter
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Arin";
    public Appearance Look { get; init; }
    public List<string> Traits { get; init; } = [];

    /// <summary>What they wear, as <c>"Def:Dye"</c> (<see cref="Sim.Systems.Clothing.Parse"/>): part of their look.</summary>
    public List<string> Clothes { get; init; } = [];

    public DateTime Created { get; init; }
    public DateTime LastUsed { get; init; }

    /// <summary>What they've done in rounds played to the end: the ground progression stands on.</summary>
    public CharacterProgress Progress { get; init; } = new();
}

/// <summary>
/// A saved character's history across rounds. Rounds per trade are kept now; titles and unlocked
/// cosmetics are slots the progression work fills (a trade mastered earns its outfit and title).
/// </summary>
public sealed record CharacterProgress
{
    /// <summary>By trade def name (<c>"Blacksmith"</c>).</summary>
    public Dictionary<string, TradeRecord> Trades { get; init; } = [];

    /// <summary>Titles earned, by id.</summary>
    public List<string> Titles { get; init; } = [];

    /// <summary>The title worn, one of <see cref="Titles"/>, or none.</summary>
    public string? Title { get; init; }

    /// <summary>Cosmetics earned, by id (outfits, dyes, hair).</summary>
    public List<string> Unlocked { get; init; } = [];

    /// <summary>All rounds played to the end, in any trade.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Rounds => Trades.Values.Sum(t => t.Rounds);

    /// <summary>This progress with one more round in <paramref name="trade"/>.</summary>
    public CharacterProgress With(string trade, int earned, bool survived)
    {
        var old = Trades.GetValueOrDefault(trade);
        var trades = new Dictionary<string, TradeRecord>(Trades)
        {
            [trade] = new(old.Rounds + 1, old.Survived + (survived ? 1 : 0), old.Earned + earned),
        };
        return this with { Trades = trades };
    }
}

/// <summary>Rounds played to the end in one trade: how many, how many lived through, silver earned in all.</summary>
public readonly record struct TradeRecord(int Rounds, int Survived, int Earned);

/// <summary>
/// The player's saved characters (<see cref="SavedCharacter"/>), in <c>characters.json</c> beside
/// their settings. Game-side only: the sim only ever sees the commands a game sends from one.
/// Nobody is dropped to make room; at <see cref="Max"/> a new one waits until one is let go.
/// </summary>
public sealed class CharacterRoster
{
    public const int Max = 16;

    /// <summary>The file's format: 1 was a bare list; 2 wraps it with the version and adds progress.</summary>
    public const int Version = 2;

    /// <summary>From <c>--character</c>: use the saved character of this name without asking (scripted runs).</summary>
    public static string? PickOverride { get; set; }

    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };

    private sealed record RosterFile(int Version, List<SavedCharacter> Characters);

    public List<SavedCharacter> Characters { get; private set; } = [];

    public string? Path { get; private set; }

    /// <summary>Written by a newer build: kept as it is, never overwritten with what this one understands of it.</summary>
    public bool ReadOnly { get; private set; }

    public bool Full => Characters.Count >= Max;

    public static string PathFor(GameHost host) => System.IO.Path.Combine(host.ConfigDirectory, "characters.json");

    public static CharacterRoster Load(GameHost host, DefDatabase defs) => Load(PathFor(host), defs);

    /// <summary>Read the roster, dropping what this build doesn't know (traits since removed, bad colours). Missing or broken: empty.</summary>
    public static CharacterRoster Load(string path, DefDatabase defs)
    {
        var roster = new CharacterRoster { Path = path };
        try
        {
            if (!File.Exists(path))
                return roster;
            var node = JsonNode.Parse(File.ReadAllText(path));
            List<SavedCharacter>? list;
            if (node is JsonArray)
                list = node.Deserialize<List<SavedCharacter>>(Json); // version 1
            else
            {
                var file = node.Deserialize<RosterFile>(Json);
                list = file?.Characters;
                roster.ReadOnly = file?.Version > Version;
            }
            if (list != null)
                roster.Characters = list.Select(c => Clean(c, defs)).OrderByDescending(c => c.LastUsed).ToList();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Engine.Warning($"characters: couldn't read {path}: {e.Message}");
        }
        if (roster.ReadOnly)
            Log.Engine.Warning($"characters: {path} is from a newer build; changes won't be saved");
        return roster;
    }

    private static SavedCharacter Clean(SavedCharacter c, DefDatabase defs)
    {
        var known = defs.AllTraits.Select(t => t.Name).ToHashSet();
        var trades = defs.AllTrades.Select(t => t.Name).ToHashSet();
        var look = c.Look;
        look.HairColor &= 0xFFFFFF;
        look.EyeColor &= 0xFFFFFF;
        look.SkinColor &= 0xFFFFFF;
        if (!Enum.IsDefined(look.Body))
            look.Body = BodyType.Male;
        if (!Enum.IsDefined(look.Hair))
            look.Hair = HairStyle.Short;
        if (!Enum.IsDefined(look.Beard))
            look.Beard = BeardStyle.None;
        if (!Enum.IsDefined(look.Eyes))
            look.Eyes = EyeStyle.Round;
        if (!Enum.IsDefined(look.Stance))
            look.Stance = Stance.Relaxed;
        if (!Enum.IsDefined(look.Folk))
            look.Folk = Folk.Human; // older rosters have none: human, as they were
        var progress = c.Progress ?? new CharacterProgress();
        var titles = (progress.Titles ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
        return c with
        {
            Name = string.IsNullOrWhiteSpace(c.Name) ? "Arin" : c.Name.Trim(),
            Look = look,
            Traits = (c.Traits ?? []).Where(known.Contains).Distinct().Take(Sim.Systems.TraitRules.MaxTraits).ToList(),
            Clothes = (c.Clothes ?? []).Where(p => Sim.Systems.Clothing.Parse(defs, p) != null).Take(Sim.Systems.Clothing.Slots.Length).ToList(),
            Created = c.Created == default ? c.LastUsed : c.Created,
            Progress = progress with
            {
                // A trade since removed keeps no record: there's nothing left to master.
                Trades = (progress.Trades ?? []).Where(t => trades.Contains(t.Key) && t.Value.Rounds > 0).ToDictionary(),
                Titles = titles,
                Title = progress.Title != null && titles.Contains(progress.Title) ? progress.Title : null,
                Unlocked = (progress.Unlocked ?? []).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList(),
            },
        };
    }

    public void Save()
    {
        if (Path == null || ReadOnly)
            return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new RosterFile(Version, Characters), Json));
            File.Move(temp, Path, overwrite: true); // a crash mid-write never loses the roster
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Engine.Warning($"characters: couldn't write {Path}: {e.Message}");
        }
    }

    /// <summary>
    /// Add or replace (by id) a character, as the most recently used, and save. Returns null for a
    /// new one when the roster is <see cref="Full"/>: nobody is dropped to make room.
    /// </summary>
    public SavedCharacter? Put(SavedCharacter c)
    {
        if (Full && !Characters.Exists(x => x.Id == c.Id))
            return null;
        var now = DateTime.UtcNow;
        c = c with { LastUsed = now, Created = c.Created == default ? now : c.Created };
        Characters.RemoveAll(x => x.Id == c.Id);
        Characters.Insert(0, c);
        Save();
        return c;
    }

    /// <summary>Change a character without making them the most recently used (round records, edits from the roster).</summary>
    public SavedCharacter? Update(SavedCharacter c)
    {
        var i = Characters.FindIndex(x => x.Id == c.Id);
        if (i < 0)
            return Put(c);
        Characters[i] = c;
        Save();
        return c;
    }

    public void Remove(Guid id)
    {
        if (Characters.RemoveAll(c => c.Id == id) > 0)
            Save();
    }

    /// <summary>The one picked last (the creators start from it), if any.</summary>
    public SavedCharacter? Last => Characters.Count > 0 ? Characters[0] : null;

    public SavedCharacter? Get(Guid id) => Characters.Find(c => c.Id == id);

    public SavedCharacter? Find(string name) =>
        Characters.FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}
