using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;

namespace HeroesNotIncluded.Sim.Expeditions;

/// <summary>What some of the town's work did for a party on the road (<see cref="Deed"/>).</summary>
public enum DeedKind : byte
{
    /// <summary>The weapon they fought with.</summary>
    Armed,

    /// <summary>Armour they wore.</summary>
    Armoured,

    /// <summary>Rations that kept them going.</summary>
    Fed,

    /// <summary>Rations that had turned.</summary>
    Sickened,

    /// <summary>A charm that warded a wound.</summary>
    Warded,

    /// <summary>A charm or a contraption that turned a death aside.</summary>
    SparedALife,

    /// <summary>A contraption that bound and did nothing (its <c>Binding</c> was hidden).</summary>
    Jammed,

    /// <summary>A quiver shot away true.</summary>
    Shot,

    /// <summary>A quiver whose arrows flew wide (a hidden warp).</summary>
    ShotWide,

    /// <summary>Saw to their wounds before they left.</summary>
    Healed,

    /// <summary>Brokered the quest.</summary>
    Sent,

    /// <summary>Struck at the sledge for gear they carried (<c>Crafted.Helper</c>, <c>Work.Pairs</c>).</summary>
    Struck,
}

/// <summary>
/// Someone's work that went out with a party, and what it did there: "Bea's longsword", times how
/// many of them carried it. Names are kept as words, so a tale outlives the people in it.
/// </summary>
public sealed record Deed(DeedKind Kind, string Who, string What, int Count = 1)
{
    /// <summary>
    /// Truth the town wasn't told in the round: a warped quiver, a binding contraption. Shown only at
    /// the reveal, never at the festival (CLAUDE.md, "Secrets").
    /// </summary>
    public bool Hidden => Kind is DeedKind.Jammed or DeedKind.ShotWide;

    /// <summary>Work that served them well: what a party toasts, and what counts toward the festival's honour.</summary>
    public bool Good => Kind is DeedKind.Armed or DeedKind.Armoured or DeedKind.Fed or DeedKind.Warded or DeedKind.SparedALife
        or DeedKind.Shot or DeedKind.Healed or DeedKind.Struck;

    /// <summary>
    /// Good work everyone could know of: what a party carried and who saw to them, and what they
    /// told of on the way home. Leaves out the quiver, since a warped one is never toasted and the gap
    /// would give it away.
    /// </summary>
    public bool Public => Good && Kind != DeedKind.Shot;

    /// <summary>The deed in a few words, for the reveal: "fought with Bea's longsword (3 of them)".</summary>
    public string Phrase
    {
        get
        {
            var times = Count > 1 ? $" ({Count} of them)" : "";
            return Kind switch
            {
                DeedKind.Armed => $"fought with {Who}'s {What}{times}",
                DeedKind.Armoured => $"wore {Who}'s {What}{times}",
                DeedKind.Fed => $"ate {Who}'s rations{times}",
                DeedKind.Sickened => $"were sickened by {Who}'s rations: they had turned{times}",
                DeedKind.Warded => Count > 1 ? $"{Who}'s charms of {What.ToLowerInvariant()} took {Count} wounds" : $"{Who}'s charm of {What.ToLowerInvariant()} took a wound",
                DeedKind.SparedALife => Count > 1 ? $"{Who}'s {What} saved {Count} lives" : $"{Who}'s {What} saved a life",
                DeedKind.Jammed => $"{Who}'s {What} jammed: it was binding",
                DeedKind.Shot => $"shot {Who}'s arrows{times}",
                DeedKind.ShotWide => $"{Who}'s arrows flew wide: the shafts were warped{times}",
                DeedKind.Healed => Count > 1 ? $"{Who} saw to {Count} of them" : $"{Who} saw to their wounds",
                DeedKind.Sent => $"{Who} sent them",
                DeedKind.Struck => $"{Who} struck at the sledge for the {What}{times}",
                _ => "",
            };
        }
    }
}

/// <summary>
/// A quest a party went on, and the hands behind them (docs/DESIGN.md, "Behind every hero"). The
/// heroes take the glory; this is the town's own count of whose work went with them, kept from the
/// moment the quest is resolved to the reveal.
/// </summary>
public sealed class QuestTale
{
    public string Quest = "";
    public string Party = "";

    /// <summary>The party's number in the round (<c>Adventurer.Party</c>), so its toast can find its own tale.</summary>
    public int PartyId;
    public bool Cleared;
    public int Went;
    public int Back;
    public List<Deed> Deeds = [];

    /// <summary>How it went, in a line: "The Greyhorn company cleared the barrow: 3 of 4 came home."</summary>
    public string Headline => Cleared
        ? $"{Cap(Party)} cleared {Quest}: {(Back == Went ? $"all {Went} came home" : $"{Back} of {Went} came home")}."
        : Back == 0 ? $"{Cap(Party)} came to grief at {Quest}: none of the {Went} came home." : $"{Cap(Party)} came to grief at {Quest}: {Back} of {Went} came home.";

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Adds a deed, or counts it again if that person's already credited with that kind of work on that thing.</summary>
    public void Credit(DeedKind kind, string? who, string what)
    {
        if (string.IsNullOrEmpty(who))
            return;
        var i = Deeds.FindIndex(d => d.Kind == kind && d.Who == who && d.What == what);
        if (i >= 0)
            Deeds[i] = Deeds[i] with { Count = Deeds[i].Count + 1 };
        else
            Deeds.Add(new Deed(kind, who, what));
    }

    /// <summary>Credits someone's work by who made it, if they're a person.</summary>
    public void Credit(Simulation sim, DeedKind kind, Entity maker, string what) => Credit(kind, NameOf(sim, maker), what);

    /// <summary>A maker's name, if they're someone (alive or lying dead); null for gear nobody made.</summary>
    public static string? NameOf(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Creature>(e, out var c) ? c.Name : null;

    public void Save(SaveWriter w)
    {
        w.String(Quest);
        w.String(Party);
        w.Int(PartyId);
        w.Bool(Cleared);
        w.Int(Went);
        w.Int(Back);
        w.Int(Deeds.Count);
        foreach (var d in Deeds)
        {
            w.Byte((byte)d.Kind);
            w.String(d.Who);
            w.String(d.What);
            w.Int(d.Count);
        }
    }

    public static QuestTale Read(SaveReader r)
    {
        var t = new QuestTale { Quest = r.String(), Party = r.String(), PartyId = r.Int(), Cleared = r.Bool(), Went = r.Int(), Back = r.Int() };
        for (var n = r.Count(1024); n > 0; n--)
        {
            var kind = r.Byte();
            if (!Enum.IsDefined((DeedKind)kind))
                throw new SaveException($"Corrupt save: deed {kind}");
            t.Deeds.Add(new Deed((DeedKind)kind, r.String(), r.String(), Math.Max(1, r.Int())));
        }
        return t;
    }

    /// <summary>
    /// The deed a party toasts, from the last quest they came back from in triumph: good work everyone
    /// knows of, a life saved first, then what most of them carried. Null if there's none.
    /// </summary>
    public static Deed? Toasted(IEnumerable<QuestTale> tales, int party) =>
        tales.LastOrDefault(t => t.PartyId == party && t.Cleared)?.Deeds.Where(d => d.Public)
            .Select((d, i) => (d, i)).OrderBy(x => Rank(x.d.Kind)).ThenByDescending(x => x.d.Count).ThenBy(x => x.i)
            .Select(x => x.d).FirstOrDefault();

    private static int Rank(DeedKind kind) => kind switch
    {
        DeedKind.SparedALife => 0,
        DeedKind.Warded => 1,
        DeedKind.Healed => 2,
        DeedKind.Armed or DeedKind.Struck => 3,
        DeedKind.Fed => 4,
        _ => 5,
    };

    /// <summary>
    /// The people most credited with good work everyone knows of, across the quests that came good:
    /// the festival's "hands behind the heroes". Each person counts once per quest.
    /// </summary>
    public static List<(string Who, int Quests)> Hands(IEnumerable<QuestTale> tales)
    {
        var counts = new List<(string Who, int Quests)>();
        foreach (var t in tales)
        {
            if (!t.Cleared)
                continue;
            foreach (var who in t.Deeds.Where(d => d.Public).Select(d => d.Who).Distinct())
            {
                var i = counts.FindIndex(c => c.Who == who);
                if (i >= 0)
                    counts[i] = (who, counts[i].Quests + 1);
                else
                    counts.Add((who, 1));
            }
        }
        // Stable: ties keep the order they were first credited in.
        return counts.Select((c, i) => (c, i)).OrderByDescending(x => x.c.Quests).ThenBy(x => x.i).Select(x => x.c).ToList();
    }
}
