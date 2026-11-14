using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

// Not in a namespace of its own: a namespace named Perception would hide the class from the rest
// of HeroesNotIncluded.Sim.
namespace HeroesNotIncluded.Sim.Systems;

/// <summary>What can be told about someone from looking at them. Nothing here needs an examination.</summary>
/// <summary>
/// A strongbox as someone looking it over sees it: locked or not, how many pins, whether they've
/// felt it over themselves, and a trap only once it's been drawn or gone off. Whether an armed trap
/// is there is never shown; it has to be felt for.
/// </summary>
/// <summary>A charm as someone sees it: the mending in it and how strong (anyone), how well woven (its maker only).</summary>
public readonly record struct KnownCharm(Defs.MendingDef Mending, int Extra, float? Quality);

public readonly record struct KnownStrongbox(bool Locked, int Pins, bool SearchedByMe, string? SpentTrap, int? Silver, string From);

/// <summary>
/// A tinker's lock (fitted to a door, or not yet) as someone sees it: how many pins, locked or not,
/// whether a key they carry turns it, and how tight it is to its maker alone (a picker finds out).
/// </summary>
public readonly record struct KnownLock(bool Locked, int Pins, bool HaveKey, float? Tightness);

/// <summary>
/// A curio as someone sees it: what it is, their own reading of it if they've looked it over (the
/// range they'd put it in, whether they think it a fake, what they said aloud), and what they've
/// been told it's worth. Never what it's really worth, or whether it's genuine.
/// </summary>
public readonly record struct KnownCurio(string Label, int? Low, int? High, bool Doubts, int? Quoted, int? Told, bool Mine);

/// <summary>A party's wagon as anyone near it sees it: whose it is and what's wrong with it (it shows).</summary>
public readonly record struct KnownWagon(string Owner, Tinkering.WagonDamage Damage);

/// <summary>
/// The piece in a smith's tongs as anyone in sight of it sees it: what it is, the colour it glows,
/// and how far it's been beaten into shape (and the smith's own next step, to them). Never a flaw.
/// </summary>
/// <remarks><see cref="Bloom"/>: a bloom's growth in words, as the smith sees it (null for a billet or a mend).</remarks>
public readonly record struct KnownPiece(string Label, Work.Glow Glow, float Temp, bool Hot, float Shape, bool Shaped, bool Mend, string? Next, Entity Forge, string? Bloom = null);

/// <summary>
/// A stew pot as anyone looking in sees it: what went in, the surface (the heat as it shows), how
/// far it's cooked, whether it's caught, and the bowls in it. Never whether it's turned, or what
/// seasoning it wants.
/// </summary>
public readonly record struct KnownPot(Entity Pot, string Contents, Work.Surface Surface, float Heat, float Flame, bool Lit, float Doneness,
    bool Cooked, string? Caught, int Bowls, bool Empty);

/// <summary>How a spot of mess looks: blood is just a dark stain.</summary>
public enum MessLook : byte
{
    Puddle,
    Mud,
    Crumbs,
    Stain,
}

/// <summary>A spot of mess on the floor, as anyone can see it.</summary>
public readonly record struct KnownMess(int3 Cell, MessLook Look, float Amount, string Label);

public readonly record struct PerceivedPerson(string Name, string Condition, string? Holding);

/// <summary>
/// A line as one character caught it. <see cref="Speaker"/> is null when they couldn't tell who it was;
/// <see cref="At"/> is where it was said from, which hearing it tells you (a voice has a direction).
/// </summary>
public readonly record struct HeardLine(long Id, long Tick, string Who, string Text, Speech.Volume Volume, Speech.Clarity Clarity, Entity? Speaker, int3 At = default);

/// <summary>
/// The one way the game learns what a character knows (docs/DESIGN.md, "One knowledge layer").
/// UI, rendering and bots ask a <see cref="PerceptionView"/> rather than reading the world, so
/// "you only know what your character knows" holds in one place, and a future server can filter
/// what it sends by the same rules.
/// </summary>
public static class Perception
{
    /// <summary>How far (cells) a character can see in good light.</summary>
    public const int SightRadius = 18;

    /// <summary>Levels above and below the viewer considered: past this a line of sight is rare, and costly to look for.</summary>
    public const int SightLevels = 2;

    /// <summary>Anyone this close is seen whatever the light: you can tell who's beside you in the dark.</summary>
    public const float TouchRange = 2.5f;

    /// <summary>The shortest sight gets, as a share of <see cref="SightRadius"/>, on the darkest night.</summary>
    public const float DarkFactor = 0.35f;

    /// <summary>Light at or above this is good enough to see as far as anyone can.</summary>
    public const float FullLight = 0.5f;

    /// <summary>
    /// What <paramref name="viewer"/> knows. No viewer, or a village with no round, sees
    /// everything: the title screen, the demos, the overseer.
    /// </summary>
    public static PerceptionView For(Simulation sim, Entity viewer) =>
        new(sim, sim.Round.Phase == Round.RoundPhase.None || viewer.IsNull || !sim.World.IsAlive(viewer) ? Entity.Null : viewer);

    /// <summary>Someone in a round with no character yet, or no longer: the town's buildings and nothing in it.</summary>
    public static PerceptionView Blind(Simulation sim) => new(sim, Entity.Null, blind: true);
}

/// <summary>
/// One character's senses. Holds a field of visible cells, rebuilt only when it could have changed,
/// so asking about every creature and item each frame stays cheap. Strictly read-only, and never
/// to be fed back into sim state: sight depends on <see cref="Maps.Map.Light"/>, which a peer that
/// just loaded a snapshot won't have recomputed yet.
/// </summary>
public sealed class PerceptionView(Simulation sim, Entity viewer, bool blind = false)
{
    /// <summary>How often (ticks) the field is rebuilt for doors and light, if nothing else prompts it.</summary>
    private const int RefreshTicks = 6;

    private readonly HashSet<int> _visible = [];
    private readonly HashSet<int> _openDoors = [];
    private int3 _builtAt;
    private int _builtVersion = -1;
    private long _builtSlice = -1;

    public Entity Viewer => viewer;

    /// <summary>Sees everything (no viewer, or no round).</summary>
    public bool Omniscient => viewer.IsNull && !blind;

    /// <summary>Has no character to see through (<see cref="Perception.Blind"/>).</summary>
    public bool Blind => blind;

    /// <summary>This view belongs to this game and character, so a cached one is still good.</summary>
    public bool IsFor(Simulation other, Entity who) => ReferenceEquals(sim, other) && viewer == who;

    public bool CanSeeCell(int3 cell)
    {
        if (Omniscient)
            return true;
        if (!sim.Map.InBounds(cell))
            return false;
        Refresh();
        return _visible.Contains(sim.Map.Idx(cell));
    }

    /// <summary>
    /// Can this creature or thing be seen right now? Buildings and furniture always can: the town's
    /// layout is common knowledge. Something worn, packed or stored shows only to someone within reach.
    /// </summary>
    public bool CanSee(Entity e)
    {
        if (Omniscient || e == viewer)
            return true;
        var w = sim.World;
        if (!w.IsAlive(e))
            return false;
        if (w.TryGet<Position>(e, out var p))
            return CanSeeCell(p.Cell);
        if (!w.TryGet<Thing>(e, out var thing))
            return false;
        if (!thing.Spawned)
            return Within(sim.LocationOf(e), 1);
        return thing.Def.Category != ThingCategory.Item || CanSeeCell(thing.Cell);
    }

    /// <summary>The creatures in sight, the viewer included.</summary>
    public IEnumerable<Entity> Creatures()
    {
        foreach (var e in sim.World.Query<Creature>())
            if (CanSee(e))
                yield return e;
    }

    /// <summary>What the viewer can tell about someone in sight; null if they can't see them.</summary>
    public PerceivedPerson? Describe(Entity e)
    {
        var w = sim.World;
        if (!CanSee(e) || !w.TryGet<Creature>(e, out var c))
            return null;
        var drink = Hospitality.DrinkingSystem.Looks(Hospitality.DrinkingSystem.LevelOf(sim, e));
        var condition = Condition(e, c);
        return new PerceivedPerson(c.Name, drink == null ? condition : $"{condition}, {drink}", Holding(e));
    }

    /// <summary>
    /// What state a weapon or piece of armor is in (<see cref="Combat.WeaponWear.Describe"/>): an
    /// edge and its chips are seen only up close, in hand or within reach. Null if out of reach.
    /// </summary>
    public string? Condition(Entity item)
    {
        var w = sim.World;
        if (!w.IsAlive(item) || !w.TryGet<Thing>(item, out var t) || (t.Def.Weapon == null && t.Def.Apparel == null))
            return null;
        // On your own person you can always look it over; anything else has to be within reach.
        var onMe = !viewer.IsNull && Inventory.OnPerson(sim, viewer, item);
        if (!Omniscient && !onMe && !Within(t.Spawned ? t.Cell : sim.LocationOf(item), 1))
            return null;
        return Combat.WeaponWear.Describe(sim, item);
    }

    /// <summary>How much fuel is in a fire or forge the viewer can see; null if they can't, or it takes none.</summary>
    public float? FuelIn(Entity station)
    {
        var w = sim.World;
        if (blind || !CanSee(station) || !w.TryGet<Fueled>(station, out var f))
            return null;
        return f.Fuel;
    }

    /// <summary>What's in a chest or bin within the viewer's reach (nothing if it's further off).</summary>
    public IEnumerable<Entity> InContainer(Entity container)
    {
        var w = sim.World;
        if (blind || !w.IsAlive(container) || !w.TryGet<Thing>(container, out var t) || (!Omniscient && !Within(t.Cell, 1)))
            return [];
        return sim.StoredIn(container);
    }

    /// <summary>The piece in someone's tongs, if they have one and the viewer can see them (<see cref="KnownPiece"/>).</summary>
    public KnownPiece? Piece(Entity smith)
    {
        if (blind || !CanSee(smith) || Work.Smithing.PieceOf(sim, smith) is not { } p)
            return null;
        // Temp is what the colour shows; the UI draws the colour from it and never prints it.
        var next = smith == viewer || Omniscient ? Work.Smithing.NextStep(sim, smith, p) : null;
        var hot = p.Temp >= Work.Smithing.Window(p.Recipe).Lo;
        return new KnownPiece(Work.Smithing.PieceLabel(sim, p), Work.Smithing.GlowOf(p.Temp), p.Temp, hot, Math.Clamp(p.Shape, 0f, 1f), p.Shaped, p.IsMend, next, p.Forge,
            p.IsBloom ? Work.Smithing.BloomWords(p.Reduced) : null);
    }

    /// <summary>Who asked the way (and where to) within the viewer's hearing, still waiting on an answer.</summary>
    public IEnumerable<Hospitality.KnownAsk> WaysAsked()
    {
        if (blind)
            yield break;
        foreach (var a in sim.Directions.Open)
            if (Omniscient || a.HeardBy.Contains(viewer))
                yield return new Hospitality.KnownAsk(a.Id, a.Asker, a.AskerName, a.To.Label, a.Tick);
    }

    /// <summary>How far gone with drink someone looks (null sober, or out of sight).</summary>
    public string? Drunkenness(Entity e) =>
        blind || !CanSee(e) ? null : Hospitality.DrinkingSystem.Looks(Hospitality.DrinkingSystem.LevelOf(sim, e));

    /// <summary>The mess the viewer can see (all of it, for an omniscient view), as it looks.</summary>
    public IEnumerable<KnownMess> Messes()
    {
        if (blind)
            yield break;
        foreach (var (i, s) in sim.Mess.All)
        {
            var c = sim.Map.CellOf(i);
            if (!Omniscient && !CanSeeCell(c))
                continue;
            var look = s.Kind switch
            {
                Hospitality.MessKind.SpilledAle => MessLook.Puddle,
                Hospitality.MessKind.Mud => MessLook.Mud,
                Hospitality.MessKind.Crumbs => MessLook.Crumbs,
                _ => MessLook.Stain,
            };
            yield return new KnownMess(c, look, s.Amount, Hospitality.MessSystem.Label(s.Kind));
        }
    }

    /// <summary>A stew pot close enough to look into (<see cref="KnownPot"/>); null if it isn't one, or is too far.</summary>
    public KnownPot? Pot(Entity pot)
    {
        if (blind || !Work.Cooking.IsPot(sim, pot))
            return null;
        var t = sim.World.Get<Thing>(pot);
        if (!Omniscient && !Within(t.Cell, 2))
            return null;
        var p = sim.World.TryGet<Work.StewPot>(pot, out var sp) ? sp : new Work.StewPot();
        // A session in hand has the pot's newest state.
        foreach (var e in sim.World.Each<Work.WorkSession>())
            if (sim.World.Get<Work.WorkSession>(e) is Work.StewSession s && s.Pot == pot)
                p = new Work.StewPot { In = p.In, Nutrition = p.Nutrition, Heat = s.Heat, Flame = s.Flame, Doneness = s.Doneness, Scorch = s.Scorch, Ladled = p.Ladled };
        var contents = string.Join(", ", p.In.Select(i => $"{i.Count} {i.Def.Label}"));
        var caught = p.Scorch > 0.35f ? "it's caught on the bottom" : p.Scorch > 0.12f ? "a little caught" : null;
        return new KnownPot(pot, contents, Work.Cooking.SurfaceOf(p.Heat), p.Heat, p.Flame, Work.Cooking.Lit(sim, pot), Math.Clamp(p.Doneness, 0f, 1f),
            p.Cooked, caught, Work.Cooking.ServingsLeft(sim, pot), p.Empty);
    }

    /// <summary>A charm, as this character can tell it (<see cref="KnownCharm"/>). Null if it isn't one.</summary>
    public KnownCharm? Charm(Entity item)
    {
        if (!sim.World.IsAlive(item) || !sim.World.TryGet<Components.Charm>(item, out var c))
            return null;
        return new KnownCharm(c.Mending, c.Extra, Omniscient || (!viewer.IsNull && c.Maker == viewer) ? c.Quality : null);
    }

    /// <summary>A strongbox within reach or on your person, as you can see it (<see cref="KnownStrongbox"/>). Null if it isn't one, or is out of reach.</summary>
    public KnownStrongbox? Strongbox(Entity item)
    {
        var w = sim.World;
        if (!w.IsAlive(item) || !w.TryGet<Components.Strongbox>(item, out var b) || !w.TryGet<Thing>(item, out var t))
            return null;
        var onMe = !viewer.IsNull && Inventory.OnPerson(sim, viewer, item);
        if (!Omniscient && !onMe && !Within(t.Spawned ? t.Cell : sim.LocationOf(item), 1))
            return null;
        var spent = b.Trap != TrapKind.None && !b.Armed ? Tinkering.Lockwork.TrapLabel(b.Trap) : null;
        return new KnownStrongbox(b.Locked, b.Pins, !viewer.IsNull && b.SearchedBy == viewer, spent, b.Locked ? null : b.Silver, b.From ?? "");
    }

    /// <summary>A lock on a door in sight, or a lock item, as you can tell it (<see cref="KnownLock"/>). Null if there's none.</summary>
    public KnownLock? Lock(Entity doorOrLock)
    {
        var w = sim.World;
        if (!w.IsAlive(doorOrLock) || !w.TryGet<Tinkering.LockCut>(doorOrLock, out var c))
            return null;
        var haveKey = !viewer.IsNull && !Tinkering.Locksmithing.KeyFor(sim, viewer, c.Seed).IsNull;
        return new KnownLock(sim.IsLocked(doorOrLock), c.Pins, haveKey, Omniscient || (!viewer.IsNull && c.Maker == viewer) ? c.Tightness : null);
    }

    /// <summary>A curio as you can tell it (<see cref="KnownCurio"/>): within reach or on your person. Null if it isn't one, or is out of reach.</summary>
    public KnownCurio? Curio(Entity item)
    {
        var w = sim.World;
        if (!Tinkering.Curios.IsCurio(sim, item) || !w.TryGet<Thing>(item, out var t))
            return null;
        var onMe = !viewer.IsNull && Inventory.OnPerson(sim, viewer, item);
        if (!Omniscient && !onMe && !Within(t.Spawned ? t.Cell : sim.LocationOf(item), 1))
            return null;
        var mine = w.TryGet<Tinkering.Appraisal>(item, out var a) && (Omniscient || (!viewer.IsNull && a.By == viewer));
        var owned = onMe && !w.Has<Entrusted>(item);
        int? told = owned && w.TryGet<Tinkering.Valued>(item, out var v) && !v.ToldBy.IsNull ? v.Worth : null;
        return new KnownCurio(t.Label, mine ? a.Low : null, mine ? a.High : null, mine && a.Doubts, mine && a.Quoted >= 0 ? a.Quoted : null, told, mine);
    }

    /// <summary>A party's wagon in sight (<see cref="KnownWagon"/>). Null if it isn't one, or can't be seen.</summary>
    public KnownWagon? Wagon(Entity wagon)
    {
        var w = sim.World;
        if (!Tinkering.Wagons.IsWagon(sim, wagon) || !w.TryGet<Thing>(wagon, out var t) || (!Omniscient && !Within(t.Cell, 3)))
            return null;
        var g = w.Get<Tinkering.Wagon>(wagon);
        return new KnownWagon(w.IsAlive(g.Owner) && w.TryGet<Creature>(g.Owner, out var c) ? c.Name : "someone", g.Damage);
    }

    /// <summary>
    /// The wounds this character can feel on someone: their own always (the ones they drew into
    /// themselves only as sensations), and a patient's while an empath has hands on them: those
    /// they've found by feeling them over (<see cref="Injury.FeltBy"/>), as an empath feels them.
    /// Festering under a dressing shows only once a pressed pass has found it. Null if they can't
    /// tell. Never the kind of a felt wound by name.
    /// </summary>
    public List<Healing.FeltWound>? FeltWounds(Entity who)
    {
        var w = sim.World;
        if (blind || !w.IsAlive(who) || !w.TryGet<Health>(who, out var h))
            return null;
        var mine = who == viewer;
        var empath = !viewer.IsNull && Healing.Empathy.Gifted(sim, viewer);
        if (!Omniscient && !mine && !(empath && w.TryGet<Position>(who, out var p) && Ai.Pathfinder.Touches(w.Get<Position>(viewer).Cell, p.Cell)))
            return null;
        var felt = new List<Healing.FeltWound>();
        foreach (var inj in h.Injuries)
        {
            if (!Omniscient && !mine && inj.FeltBy != viewer)
                continue;
            var bySense = !Omniscient && (!mine || inj.Absorbed);
            var feels = bySense ? Healing.Empathy.Feel(sim, inj.Type) : Defs.DamageTypes.Wound(inj.Type);
            var festering = inj.Infection != null && (Omniscient || mine || !inj.Tended || inj.FeltFestering);
            felt.Add(new Healing.FeltWound(Healing.Empathy.KeyOf(inj), h.Body.Parts[inj.Part].Label, feels,
                Healing.Empathy.Severity(h, inj), festering, inj.Absorbed));
        }
        return felt;
    }

    /// <summary>The quests this character has heard of, as they believe them to be (everything, truly, when omniscient).</summary>
    public IEnumerable<Expeditions.KnownQuest> Quests()
    {
        if (blind)
            yield break;
        foreach (var q in sim.Expeditions.Quests)
        {
            if (Omniscient)
                yield return new Expeditions.KnownQuest(q.Id, q.Def.Label, q.Def.Description, q.Danger, q.Reward, 1f, q.Status);
            else if (sim.Expeditions.IntelOf(viewer, q.Id) is { } i)
                yield return new Expeditions.KnownQuest(q.Id, q.Def.Label, q.Def.Description, i.Danger, i.Reward, i.Confidence, q.Status);
        }
    }

    /// <summary>
    /// How strong an adventurer's party looks to this character: their true strength, off by up to
    /// a fifth either way (a fixed misjudgement per viewer and party, so it doesn't flicker).
    /// Null if they can't see the adventurer.
    /// </summary>
    public float? PartyLooks(Entity member)
    {
        var w = sim.World;
        if (!CanSee(member) || !w.TryGet<Adventurer>(member, out var a))
            return null;
        var strength = Expeditions.ExpeditionSystem.PartyStrength(sim, a.Party);
        if (Omniscient)
            return strength;
        var h = (uint)(viewer.Index * 7919 + a.Party * 104729) * 2654435761u;
        h ^= h >> 15;
        return strength * (0.8f + 0.4f * (h % 1000) / 1000f);
    }

    /// <summary>
    /// The orders this character knows of, oldest first: those they heard asked, took on or asked
    /// for themselves. Open ones and those closed within the last day.
    /// </summary>
    public IEnumerable<Shops.KnownOrder> Orders()
    {
        if (blind)
            yield break;
        foreach (var e in sim.Shops.All)
            if (Omniscient || e.KnownBy.Contains(viewer))
                yield return new Shops.KnownOrder(e.Id, e.Kind.Kind, e.Kind.Label, e.PosterName, e.Text, e.Price,
                    e.PostedTick, e.Deadline, e.AssigneeName, e.Status, e.Poster == viewer, e.Assignee == viewer, e.Poster);
    }

    /// <summary>The named drink a drink order the viewer knows of asked for, if it asked for one.</summary>
    public Defs.ThingDef? WantedDrink(int order) =>
        !blind && sim.Shops.Find(order) is { } e && (Omniscient || e.KnownBy.Contains(viewer)) && sim.Drinking.Wanted(order) is { } name
            ? sim.Defs.Thing(name) : null;

    /// <summary>
    /// A shop's price sign: every service the building serves, what it costs and whether it's
    /// offered. Posted for anyone to read, so it isn't gated by sight. Empty if it's no shop.
    /// </summary>
    public List<Shops.PostedPrice> Prices(Entity building) => Shops.ShopPrices.Board(sim, building);

    /// <summary>
    /// What a piece is for sale at and who's selling it, if it's for sale (the price is posted
    /// with it, like a sign), and whether it's on show in their shop. Null if it isn't for sale.
    /// </summary>
    public (int Price, Entity Seller, bool OnShow)? Sale(Entity item) =>
        sim.World.IsAlive(item) && sim.World.TryGet<Shops.ForSale>(item, out var s)
            ? (s.Price, s.Seller, !Shops.Goods.ShownIn(sim, item).IsNull)
            : null;

    /// <summary>
    /// The town's standing as anyone in it feels it: what travellers say of it, and how the dungeon
    /// weighs on the valley. Words, never the numbers (those are for the reveal). Null outside a round.
    /// </summary>
    public (string Renown, string Dread)? Standing()
    {
        if (!sim.Round.Live)
            return null;
        var season = sim.Defs.Season;
        var r = sim.Reckoning;
        return (Director.Reckoning.RenownWord(r.Renown, season.RenownTarget), Director.Reckoning.DreadWord(r.Dread, season.DreadLimit));
    }

    /// <summary>
    /// The favours players asked this character knows of, oldest first: those they heard asked, took
    /// on or asked for themselves. Open ones and those closed within the last day.
    /// </summary>
    public IEnumerable<Favours.KnownFavour> Favours()
    {
        if (blind)
            yield break;
        foreach (var r in sim.Favours.All)
            if (Omniscient || r.KnownBy.Contains(viewer))
                yield return new Favours.KnownFavour(r.Id, r.PosterName, r.Text, r.Price, r.PostedTick, r.Deadline,
                    r.AssigneeName, r.Status, r.Poster == viewer, r.Assignee == viewer, r.Poster, r.Wants, r.Count);
    }

    /// <summary>Everything this character has heard that's still remembered, oldest first.</summary>
    public IEnumerable<HeardLine> Heard()
    {
        if (blind)
            yield break;
        foreach (var u in sim.Speech.Kept)
        {
            if (Omniscient)
            {
                yield return new HeardLine(u.Id, u.Tick, u.SpeakerName, u.Text, u.Volume, Speech.Clarity.Clear, u.Speaker, u.At);
                continue;
            }
            foreach (var h in u.Listeners)
                if (h.Listener == viewer)
                    yield return new HeardLine(u.Id, u.Tick, h.Identified ? u.SpeakerName : "Someone",
                        Speech.SpeechSystem.TextFor(u, h), u.Volume, h.Clarity, h.Identified ? u.Speaker : null, u.At);
        }
    }

    private string Condition(Entity e, Creature c)
    {
        var w = sim.World;
        if (c.Dead)
            return "Not moving.";
        if (c.Downed)
            return "Collapsed.";
        if (w.TryGet<Mind>(e, out var mind) && mind.Asleep)
            return "Asleep.";
        if (w.TryGet<Health>(e, out var h))
        {
            if (h.Capacity(Capacity.Moving) < 0.7f)
                return "Limping badly.";
            if (h.Injuries.Exists(i => !i.Tended || i.Stanched))
                return "Hurt.";
        }
        return "Looks well enough.";
    }

    private string? Holding(Entity e)
    {
        var w = sim.World;
        if (w.TryGet<Carrying>(e, out var carrying) && w.IsAlive(carrying.Item))
            return w.Get<Thing>(carrying.Item).Def.Label;
        if (w.TryGet<Equipment>(e, out var gear) && w.IsAlive(gear.Weapon))
            return w.Get<Thing>(gear.Weapon).Def.Label;
        return null;
    }

    private bool Within(int3 cell, int cells)
    {
        if (!sim.World.IsAlive(viewer))
            return false;
        var here = sim.World.Get<Position>(viewer).Cell;
        return cell.Z == here.Z && Math.Abs(cell.X - here.X) <= cells && Math.Abs(cell.Y - here.Y) <= cells;
    }

    private void Refresh()
    {
        if (!sim.World.IsAlive(viewer))
        {
            _visible.Clear();
            return;
        }
        var map = sim.Map;
        var here = sim.World.Get<Position>(viewer).Cell;
        var slice = sim.Clock.Tick / RefreshTicks;
        if (_builtVersion == map.StructureVersion && _builtSlice == slice && here == _builtAt)
            return;
        (_builtVersion, _builtSlice, _builtAt) = (map.StructureVersion, slice, here);
        Build(here);
    }

    private void Build(int3 here)
    {
        var map = sim.Map;
        var w = sim.World;
        _visible.Clear();
        if (!map.InBounds(here))
            return;

        // A door has no open state in the sim: it stands open while someone is in the doorway,
        // which is also how it's drawn.
        _openDoors.Clear();
        foreach (var e in w.Each<Creature>())
        {
            var cell = w.Get<Position>(e).Cell;
            if (map.InBounds(cell))
                _openDoors.Add(map.Idx(cell));
        }

        const int r = Perception.SightRadius;
        var z0 = Math.Max(0, here.Z - Perception.SightLevels);
        var z1 = Math.Min(map.Depth - 1, here.Z + Perception.SightLevels);
        for (var z = z0; z <= z1; z++)
        for (var y = Math.Max(0, here.Y - r); y <= Math.Min(map.Height - 1, here.Y + r); y++)
        for (var x = Math.Max(0, here.X - r); x <= Math.Min(map.Width - 1, here.X + r); x++)
        {
            var i = map.Idx(x, y, z);
            if (map.IsSolid(i))
                continue;
            var target = new int3(x, y, z);
            if (InRange(here, target, i) && Clear(here, target, i))
                _visible.Add(i);
        }
    }

    // Dark cells are seen from less far: what counts is the light where the thing is.
    private bool InRange(int3 here, int3 target, int i)
    {
        float dx = target.X - here.X, dy = target.Y - here.Y, dz = target.Z - here.Z;
        var distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (distance <= Perception.TouchRange)
            return true;
        var light = Math.Clamp(sim.Map.Light[i] / Perception.FullLight, Perception.DarkFactor, 1f);
        return distance <= Perception.SightRadius * light;
    }

    private bool Clear(int3 here, int3 target, int targetIdx)
    {
        if (here == target)
            return true;
        var map = sim.Map;
        var hereIdx = map.Idx(here);
        var lowest = Math.Min(here.Z, target.Z);
        var crossesLevels = here.Z != target.Z;
        // A roof you can't see through, seen from another level: the room under it stays private.
        if (crossesLevels && (map.Roofed[hereIdx] || map.Roofed[targetIdx]))
            return false;

        var from = new float3(here.X + 0.5f, here.Y + 0.5f, here.Z + 0.5f);
        var d = new float3(target.X - here.X, target.Y - here.Y, target.Z - here.Z);
        var length = MathF.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
        return !map.Raycast(from, d * (1f / length), length, i =>
        {
            // Stepping between levels goes through the floor of the upper cell, at either end of the line.
            if (crossesLevels && map.CellOf(i).Z > lowest && map.Ceiling(i))
                return true;
            return i != hereIdx && i != targetIdx && Blocks(i);
        }, out _, out _);
    }

    private bool Blocks(int i)
    {
        var map = sim.Map;
        if (map.IsSolid(i))
            return true;
        var s = map.Structure[i];
        if (s.IsNull || sim.World.Has<Construction>(s))
            return false;
        var building = sim.World.Get<Thing>(s).Def.Building;
        if (building == null || building.SeeThrough)
            return false;
        return building.IsWall || (building.IsDoor && !_openDoors.Contains(i));
    }
}
