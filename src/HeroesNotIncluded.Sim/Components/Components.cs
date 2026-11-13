using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Components;

/// <summary>
/// Anything placed on (or carried off) the map that has a ThingDef: items, buildings, plants,
/// boulders, eggs, construction sites.
/// </summary>
public struct Thing
{
    public ThingDef Def;
    public int3 Cell;
    public float Hp;

    /// <summary>What it's made of (birch wood, a birch wall), or null for things without materials.</summary>
    public MaterialDef? Material;

    /// <summary>The def label, qualified by material: "birch wood", "pine wall".</summary>
    public readonly string Label => Material?.Describe(Def.Label) ?? Def.Label;

    // The def's stats as its material changes them (MaterialDef: neutral without one).

    public readonly float MaxHp => Def.MaxHp * (Material?.HpFactor ?? 1f);

    public readonly float Flammability => Def.Flammability * (Material?.Flammability ?? 1f);

    /// <summary>Weight of one unit, in kg.</summary>
    public readonly float UnitMass => Def.Mass * (Material?.MassFactor ?? 1f);

    /// <summary>A building's insulation with its material's offset, kept below 0.99 (0 for anything else).</summary>
    public readonly float Insulation => Def.Building is { } b ? Math.Min(0.99f, b.Insulation + (Material?.Insulation ?? 0f)) : 0f;

    /// <summary>False while carried by a pawn (not registered in a map layer).</summary>
    public bool Spawned;

    /// <summary>
    /// Which way a placed thing is turned, in quarter turns (0–3), chosen when placing it. Only
    /// the look depends on it (a bed's head end, see <see cref="Defs.ThingDef.Model"/>).
    /// </summary>
    public byte Facing;
}

/// <summary>Stackable item count.</summary>
public struct Stack
{
    public int Count;
}

/// <summary>A building that consumes fuel (campfire).</summary>
public struct Fueled
{
    public float Fuel;

    /// <summary>The fuel last put in (null: the building's main fuel), which sets how hot and long it burns.</summary>
    public ThingDef? Loaded;

    /// <summary>How the loaded fuel burns in <paramref name="props"/>: its option, else the main fuel's.</summary>
    public readonly FuelOption Burn(FuelProps props) => props.OptionFor(Loaded) ?? props.Options[0];
}

/// <summary>Plant/regrowable state. Ready when <see cref="RegrowTicksLeft"/> reaches zero.</summary>
public struct PlantState
{
    public float RegrowTicksLeft;
    public bool Ready => RegrowTicksLeft <= 0;
}

/// <summary>
/// A blueprint/frame. The Thing.Def of the entity is the def being built. Materials are
/// delivered (Delivered[i] matches Building.Cost[i]) then work is done.
/// </summary>
public sealed class Construction
{
    public required ThingDef Target;
    public int[] Delivered = [];
    public float WorkDone;

    /// <summary>Units delivered per material, in delivery order. The building is made of the most-delivered one.</summary>
    public readonly List<(MaterialDef Material, int Count)> Materials = [];

    public void AddMaterial(MaterialDef material, int count)
    {
        for (var i = 0; i < Materials.Count; i++)
            if (Materials[i].Material == material)
            {
                Materials[i] = (material, Materials[i].Count + count);
                return;
            }
        Materials.Add((material, count));
    }

    /// <summary>The material most delivered so far (first delivered wins ties), or null.</summary>
    public MaterialDef? MainMaterial
    {
        get
        {
            MaterialDef? best = null;
            var most = 0;
            foreach (var (m, n) in Materials)
                if (n > most)
                {
                    best = m;
                    most = n;
                }
            return best;
        }
    }

    public bool MaterialsComplete
    {
        get
        {
            var cost = Target.Building!.Cost;
            for (var i = 0; i < cost.Count; i++)
                if (Delivered[i] < cost[i].Count)
                    return false;
            return true;
        }
    }

    /// <summary>Index into Cost of the first material still needed, or -1.</summary>
    public int NextNeededMaterial(out int remaining)
    {
        var cost = Target.Building!.Cost;
        for (var i = 0; i < cost.Count; i++)
            if (Delivered[i] < cost[i].Count)
            {
                remaining = cost[i].Count - Delivered[i];
                return i;
            }
        remaining = 0;
        return -1;
    }
}

/// <summary>How long a bill keeps going.</summary>
public enum BillMode
{
    /// <summary>Make it <see cref="Bill.Target"/> times, then the bill is done and goes away.</summary>
    Count,

    /// <summary>Keep the village stocked: make more while fewer than <see cref="Bill.Target"/> of the first product are about.</summary>
    UntilYouHave,

    /// <summary>Make it whenever there are ingredients.</summary>
    Forever,
}

/// <summary>A standing order on a workbench: make this recipe, so many times.</summary>
public sealed class Bill
{
    public required RecipeDef Recipe;
    public BillMode Mode;
    public int Target = 1;

    /// <summary>Times made so far (counts toward <see cref="BillMode.Count"/>).</summary>
    public int Done;

    public bool Suspended;

    /// <summary>Only use ingredients of this material (oak planks only); null takes any.</summary>
    public MaterialDef? Material;
}

/// <summary>
/// A station's bills and the craft under way at it. What's been brought for the current recipe
/// is kept as counts (<see cref="Staged"/>, one per ingredient entry), like a construction site's
/// deliveries: the items are used up as they arrive, so nobody hauls them off, and an interrupted
/// craft (or a save and load) picks up where it was. Cancelling gives them back.
/// </summary>
public sealed class Workbench
{
    public readonly List<Bill> Bills = [];

    /// <summary>The recipe being made, once someone has started on it; null when the bench is free.</summary>
    public RecipeDef? Active;

    /// <summary>
    /// The player character making <see cref="Active"/> by hand (picked at the bench), or
    /// Entity.Null for a bill's craft. Villagers leave a player's craft alone.
    /// </summary>
    public Entity By;

    /// <summary>Units brought so far, per entry of <see cref="Active"/>'s ingredients.</summary>
    public int[] Staged = [];

    /// <summary>Units brought per material, in arrival order: the products are made of the most-brought one.</summary>
    public readonly List<(MaterialDef Material, int Count)> Materials = [];

    /// <summary>How far gone the food brought so far is, on average (0 fresh), for what's cooked from it.</summary>
    public float Rot;

    /// <summary>Food units averaged into <see cref="Rot"/>.</summary>
    public int RotUnits;

    /// <summary>Work done on <see cref="Active"/>, in work-ticks.</summary>
    public float Progress;

    public bool Idle => Active == null;

    /// <summary>Nothing brought and no work done: the bench can switch to another recipe for free.</summary>
    public bool Untouched => Active == null || (Progress <= 0 && Array.TrueForAll(Staged, n => n == 0));

    public void Begin(RecipeDef recipe, Entity by)
    {
        Clear();
        Active = recipe;
        By = by;
        Staged = new int[recipe.Ingredients.Count];
    }

    public void Clear()
    {
        Active = null;
        By = Entity.Null;
        Staged = [];
        Materials.Clear();
        Rot = 0;
        RotUnits = 0;
        Progress = 0;
    }

    /// <summary>Everything for <see cref="Active"/> has been brought.</summary>
    public bool Stocked
    {
        get
        {
            if (Active == null)
                return false;
            for (var i = 0; i < Staged.Length; i++)
                if (Staged[i] < Active.Ingredients[i].Count)
                    return false;
            return true;
        }
    }

    /// <summary>Index of the first ingredient still needed (and how many), or -1.</summary>
    public int NextNeeded(out int remaining)
    {
        remaining = 0;
        if (Active == null)
            return -1;
        for (var i = 0; i < Staged.Length; i++)
            if (Staged[i] < Active.Ingredients[i].Count)
            {
                remaining = Active.Ingredients[i].Count - Staged[i];
                return i;
            }
        return -1;
    }

    public void AddMaterial(MaterialDef material, int count)
    {
        for (var i = 0; i < Materials.Count; i++)
            if (Materials[i].Material == material)
            {
                Materials[i] = (material, Materials[i].Count + count);
                return;
            }
        Materials.Add((material, count));
    }

    /// <summary>The material most brought so far (first brought wins ties), or null.</summary>
    public MaterialDef? MainMaterial
    {
        get
        {
            MaterialDef? best = null;
            var most = 0;
            foreach (var (m, n) in Materials)
                if (n > most)
                {
                    best = m;
                    most = n;
                }
            return best;
        }
    }

    /// <summary>The first unsuspended bill for a recipe, or null.</summary>
    public Bill? BillFor(RecipeDef recipe) => Bills.Find(b => b.Recipe == recipe && !b.Suspended);
}

/// <summary>How well something was made (rolled from the maker's skill when it's crafted).</summary>
public enum Quality
{
    Crude,
    Plain,
    Fine,
    Masterwork,
}

/// <summary>
/// Gear someone made (crafted weapons, apparel, packs; never stacks): how well, and by whom.
/// Quality scales armor and weapon damage; using a friend's work is a good thought about them.
/// Gear that wasn't crafted (the starting kit, a raider's) has none and counts as plain.
/// </summary>
public struct Crafted
{
    public Quality Quality;
    public Entity Maker;
}

/// <summary>
/// A station whose bellows a smith is pumping (<c>BuildingProps.Bellows</c> beside it): it runs
/// hotter until <see cref="Until"/>, a little after the pumping stops.
/// </summary>
public struct Blown
{
    public long Until;
}

/// <summary>
/// A player character's own recipe book: what they've discovered or been taught (skill unlocks
/// aren't stored, see <c>RecipeBooks</c>). It travels with them.
/// </summary>
public sealed class RecipeBook
{
    public readonly HashSet<RecipeDef> Known = [];
}

/// <summary>Food (and carcasses) going off, 0 fresh .. 1 rotted away (see <c>RotSystem</c>).</summary>
public struct Rot
{
    public float Progress;
}

public enum Faction
{
    Village,
    Wild,
    Raider,

    /// <summary>Held by the village (see <see cref="Prisoner"/>): no one's enemy while they stay put.</summary>
    Prisoner,

    /// <summary>Folk of Ashford, the prologue's village that isn't yours: they live their own lives, under nobody's orders.</summary>
    Ashford,

    /// <summary>Story characters who are no one's (the sorcerer): neither the village's nor its enemies.</summary>
    Stranger,

    /// <summary>Escape's captors before the alarm: they hold the player, but no fight starts on sight. The alarm makes them raiders.</summary>
    Captors,

    /// <summary>Wolves and bears the storyteller sends (<c>Systems.WildEvents</c>): the village's enemy, fought turn by turn like raiders.</summary>
    Predator,

    /// <summary>Adventurers passing through (<see cref="Adventurer"/>): customers, not enemies, and nobody's to command.</summary>
    Adventurer,
}

public enum PrisonerMode
{
    /// <summary>Kept, fed and tended.</summary>
    Hold,

    /// <summary>Wardens talk them round, a visit a day, until they join.</summary>
    Recruit,

    /// <summary>A warden lets them go and they walk off the map.</summary>
    Release,
}

/// <summary>A captured raider held in a prisoner bed (their <see cref="BedOwner"/>).</summary>
public struct Prisoner
{
    /// <summary>Who they were with before.</summary>
    public Faction Was;
    public PrisonerMode Mode;

    /// <summary>How much talking it takes before recruiting can work at all.</summary>
    public float Resistance;

    public long LastVisit;
}

/// <summary>A prisoner who broke out: a raider again, grabbing a weapon and running for the map edge.</summary>
public struct Escaping;

/// <summary>The player wants this downed enemy taken prisoner (Doctor work carries them to a prisoner bed).</summary>
public struct CaptureOrder;

/// <summary>The player wants this animal hunted (Hunt work: someone with a bow).</summary>
public struct HuntOrder;

/// <summary>Who last hurt this creature, and when: animals run from (or turn on) them.</summary>
public struct Provoked
{
    public Entity By;
    public long Tick;
}

/// <summary>A wild animal's herd, and when the herd moves on (see <c>WildlifeSystem</c>).</summary>
public struct Herd
{
    public int Id;
    public long LeaveTick;
}

/// <summary>A bed set aside for prisoners: villagers don't use it, prisoners only use these.</summary>
public struct ForPrisoners;

public static class Factions
{
    /// <summary>Will these two fight? Raiders and the village do; wild animals keep to themselves unless hunted.</summary>
    public static bool Hostile(Faction a, Faction b) =>
        (Attacker(a) && Defends(b)) || (Defends(a) && Attacker(b));

    /// <summary>Who raiders and predators come for, and who fights them: the village, and adventurers in town.</summary>
    public static bool Defends(Faction f) => f is Faction.Village or Faction.Adventurer;

    /// <summary>The village's enemies' side in a fight: raiders, and predators.</summary>
    public static bool Attacker(Faction f) => f is Faction.Raider or Faction.Predator;
}

/// <summary>
/// How keen a bladed weapon's edge is (<see cref="Combat.WeaponWear"/>), 0 keen to 1 blunt. Chips
/// and bends are its HP. Added the first time it dulls; a weapon without one is keen.
/// </summary>
public struct Edge
{
    public float Dullness;
}

/// <summary>
/// Handed over to be seen to (a blade to mend, <c>Shops.ShopSystem</c>): whoever holds it keeps
/// it safe between jobs until it goes back to its owner.
/// </summary>
public struct Entrusted
{
    public Entity Owner;
}

/// <summary>What a strongbox's trap does to the hand that springs it (<c>Tinkering/Lockwork</c>).</summary>
public enum TrapKind : byte
{
    None,

    /// <summary>A poisoned needle in the hasp.</summary>
    Needle,

    /// <summary>A spring-loaded blade under the lid.</summary>
    Blade,

    /// <summary>A charge of burning powder.</summary>
    Powder,
}

/// <summary>
/// A locked box brought back from a quest (<c>Tinkering/</c>). The lock's pins are set from
/// <see cref="LockSeed"/>; the trap, where it sits on the seam and whether it's armed are hidden
/// item state, shown to players only through Perception. The silver inside is taken out by its
/// owner when it comes back open.
/// </summary>
public struct Strongbox
{
    public int Pins;
    public uint LockSeed;
    public bool Locked;
    public TrapKind Trap;

    /// <summary>Where along the lid's seam (0..1) the trap's trigger sits.</summary>
    public float TrapAt;
    public bool Armed;
    public int Silver;

    /// <summary>The last one to run a probe along the seam (they know what they felt, or didn't).</summary>
    public Entity SearchedBy;

    /// <summary>Where it came from, for its description.</summary>
    public string From;
}

/// <summary>How many drinks have been drawn from a cask (<see cref="Defs.CaskProps"/>); none until the first.</summary>
public struct CaskDrawn
{
    public int Drawn;
}

/// <summary>
/// The gift an empath heals with (<c>Healing/</c>): spent casting mendings, regained slowly on its
/// own and quickly by meditating.
/// </summary>
public struct Mana
{
    public float Current;
    public float Max;
}

/// <summary>
/// The mendings an empath knows how to weave (<c>Healing.Weaving</c>): those every empath starts
/// with, and any they've found by weaving a new mix of runes. Only a book entry shows its runes.
/// </summary>
public sealed class Spellbook
{
    public readonly HashSet<Defs.MendingDef> Known = [];
}

/// <summary>
/// A mending woven into a charm (<c>Healing.Charms</c>): torn, it lands on whoever tears it, as if
/// they'd cast it; carried on a quest, it's a ward. What mending it holds anyone can tell (the
/// knot's token); how well it was woven only its maker knows (<c>PerceptionView.Charm</c>).
/// </summary>
public struct Charm
{
    public Defs.MendingDef Mending;
    public int Extra;
    public float Quality;
    public Entity Maker;
}

/// <summary>Settled by a steadying mending: drawing a wound shakes the empath less until <see cref="Until"/>.</summary>
public struct Steadied
{
    /// <summary>How much wider the calm band is, as a share.</summary>
    public float Band;
    public long Until;
}

/// <summary>
/// Looks as the person did under another entity id (an adventurer back from a quest is a new
/// entity): the derived look and clothes come from <see cref="Id"/> rather than their own index.
/// </summary>
public struct LookedAs
{
    public int Id;
}

/// <summary>Back from a quest with something to tell, said once they reach the tavern (<c>Expeditions/</c>).</summary>
public struct ReportPending
{
    public int Quest;
    public float Danger;
    public string Text;
}

/// <summary>A forge kept burning for a mend in hand (<c>Work.HammerSession</c>) until this tick, with no recipe on it.</summary>
public struct FiredForMending
{
    public long Until;
}

/// <summary>What a creature fights with. The weapon is an unspawned Thing entity, like a carried item.</summary>
public struct Equipment
{
    public Entity Weapon;
}

/// <summary>What a creature wears, one unspawned Thing per <see cref="ApparelSlot"/> (Entity.Null if bare).</summary>
public struct Apparel
{
    public Apparel() => Head = Body = Back = Neck = Tunic = Legs = Feet = Cloak = Entity.Null;

    public Entity Head;
    public Entity Body;

    /// <summary>A pack (<see cref="Defs.ThingDef.Container"/>), whose contents are <see cref="Stored"/> in it.</summary>
    public Entity Back;

    /// <summary>Jewelry.</summary>
    public Entity Neck;

    /// <summary>Clothes (format 12).</summary>
    public Entity Tunic, Legs, Feet, Cloak;

    public Entity this[ApparelSlot slot]
    {
        readonly get => slot switch
        {
            ApparelSlot.Head => Head,
            ApparelSlot.Body => Body,
            ApparelSlot.Neck => Neck,
            ApparelSlot.Tunic => Tunic,
            ApparelSlot.Legs => Legs,
            ApparelSlot.Feet => Feet,
            ApparelSlot.Cloak => Cloak,
            _ => Back,
        };
        set
        {
            switch (slot)
            {
                case ApparelSlot.Head: Head = value; break;
                case ApparelSlot.Body: Body = value; break;
                case ApparelSlot.Neck: Neck = value; break;
                case ApparelSlot.Tunic: Tunic = value; break;
                case ApparelSlot.Legs: Legs = value; break;
                case ApparelSlot.Feet: Feet = value; break;
                case ApparelSlot.Cloak: Cloak = value; break;
                default: Back = value; break;
            }
        }
    }
}

/// <summary>A gear slot the player can order bare: the apparel slots and the weapon hand.</summary>
public enum GearSlot
{
    Head,
    Body,
    Weapon,
    Back,
    Neck,
    Tunic,
    Legs,
    Feet,
    Cloak,
}

/// <summary>
/// The player's standing gear orders for a person: slots kept bare (a "Take off" that sticks, so
/// <c>HumanBrain.FindGear</c> doesn't put the item straight back on), and one item they were told
/// to put on. Only exists once an order was given.
/// </summary>
public struct Outfit
{
    public Outfit() => Wanted = Entity.Null;

    public bool BareHead;
    public bool BareBody;
    public bool Unarmed;
    public bool BareBack;
    public bool BareNeck;
    public bool BareTunic, BareLegs, BareFeet, BareCloak;

    /// <summary>An item to go and put on (<see cref="Core.WearCommand"/>), Entity.Null if none.</summary>
    public Entity Wanted;

    public bool KeepBare(GearSlot slot) => slot switch
    {
        GearSlot.Head => BareHead,
        GearSlot.Body => BareBody,
        GearSlot.Back => BareBack,
        GearSlot.Neck => BareNeck,
        GearSlot.Tunic => BareTunic,
        GearSlot.Legs => BareLegs,
        GearSlot.Feet => BareFeet,
        GearSlot.Cloak => BareCloak,
        _ => Unarmed,
    };

    public void SetBare(GearSlot slot, bool bare)
    {
        switch (slot)
        {
            case GearSlot.Head: BareHead = bare; break;
            case GearSlot.Body: BareBody = bare; break;
            case GearSlot.Back: BareBack = bare; break;
            case GearSlot.Neck: BareNeck = bare; break;
            case GearSlot.Tunic: BareTunic = bare; break;
            case GearSlot.Legs: BareLegs = bare; break;
            case GearSlot.Feet: BareFeet = bare; break;
            case GearSlot.Cloak: BareCloak = bare; break;
            default: Unarmed = bare; break;
        }
    }

    public static GearSlot Of(Defs.ApparelSlot slot) => slot switch
    {
        Defs.ApparelSlot.Head => GearSlot.Head,
        Defs.ApparelSlot.Body => GearSlot.Body,
        Defs.ApparelSlot.Neck => GearSlot.Neck,
        Defs.ApparelSlot.Tunic => GearSlot.Tunic,
        Defs.ApparelSlot.Legs => GearSlot.Legs,
        Defs.ApparelSlot.Feet => GearSlot.Feet,
        Defs.ApparelSlot.Cloak => GearSlot.Cloak,
        _ => GearSlot.Back,
    };

    /// <summary>The apparel slot behind a gear slot (null for the weapon hand).</summary>
    public static Defs.ApparelSlot? ApparelOf(GearSlot slot) => slot switch
    {
        GearSlot.Head => Defs.ApparelSlot.Head,
        GearSlot.Body => Defs.ApparelSlot.Body,
        GearSlot.Back => Defs.ApparelSlot.Back,
        GearSlot.Neck => Defs.ApparelSlot.Neck,
        GearSlot.Tunic => Defs.ApparelSlot.Tunic,
        GearSlot.Legs => Defs.ApparelSlot.Legs,
        GearSlot.Feet => Defs.ApparelSlot.Feet,
        GearSlot.Cloak => Defs.ApparelSlot.Cloak,
        _ => null,
    };
}

/// <summary>Won't attack anyone (stands in for a traits system): can still move in combat.</summary>
public struct NonViolent;

public struct Creature
{
    public CreatureDef Def;
    public string Name;
    public Faction Faction;
    public long BornTick;
    public float Hp;
    public bool Dead;

    /// <summary>Collapsed from blood loss, pain or a head wound: can't move or act until it recovers.</summary>
    public bool Downed;

    /// <summary>Alive and on its feet.</summary>
    public readonly bool Able => !Dead && !Downed;

    public readonly float AgeDays(long now) => SimClock.TicksToDays(now - BornTick);
    public readonly LifeStage Stage(long now) => Def.StageForAge(AgeDays(now));

    /// <summary>
    /// Degrees lower its cold limits go, from a dragon's element (<c>ElementDef.ColdTolerance</c>:
    /// an ice dragon is at home in the cold). Derived, not saved: <c>Systems.Dragons.SetElement</c>
    /// sets it, and a load sets it again from <see cref="Dragon.Element"/>.
    /// </summary>
    public float ColdTolerance;

    /// <summary>The temperatures it's comfortable in and survives, at its age.</summary>
    public readonly TempTolerance Tolerance(long now)
    {
        var t = Def.Tolerance(Stage(now));
        return ColdTolerance == 0 ? t : t with { ComfortMin = t.ComfortMin - ColdTolerance, SafeMin = t.SafeMin - ColdTolerance };
    }
}

/// <summary>One wound on one body part. Several hits of the same kind on a part merge into one.</summary>
public sealed class Injury
{
    public int Part;
    public Defs.DamageType Type;
    public float Damage;

    /// <summary>The worst the wound got: deep ones may leave a scar.</summary>
    public float PeakDamage;

    /// <summary>Bandaged: stops bleeding and heals faster.</summary>
    public bool Tended;

    /// <summary>How well it was tended (0..1, from the doctor's Medicine): faster healing, less infection.</summary>
    public float Quality;

    /// <summary>When it was last tended; infected wounds want tending again daily.</summary>
    public long TendedTick;

    public long Tick;

    /// <summary>Drawn out of someone else by an empath (<c>Healing.Empathy</c>): they feel it rather than see it.</summary>
    public bool Absorbed;

    /// <summary>
    /// The empath who's felt this wound with their hands (<c>Healing.FeelSession</c>), Entity.Null if
    /// none: only they know it's there, and can draw it.
    /// </summary>
    public Entity FeltBy = Entity.Null;

    /// <summary>Festering found under the dressing by a pressed pass (an untended one shows anyway).</summary>
    public bool FeltFestering;

    /// <summary>Wounds get one chance to fester, half a day after they're made.</summary>
    public bool InfectionRolled;

    public Infection? Infection;

    /// <summary>Untended, or infected and due a fresh dressing.</summary>
    public bool NeedsTending(long now) => !Tended || (Infection != null && now - TendedTick >= SimClock.TicksPerDay);
}

/// <summary>
/// A festering wound: severity creeps up (slower if well tended) while the body builds immunity
/// (faster in bed). Immunity first and it clears; severity first and it kills.
/// </summary>
public sealed class Infection
{
    public float Severity;
    public float Immunity;

    /// <summary>This body's knack for fighting it off (rolled at onset).</summary>
    public float ImmunityRate = 1f;
}

/// <summary>What a bad wound leaves behind once healed: a little lasting damage and pain.</summary>
public readonly record struct Scar(int Part, Defs.DamageType Type, float Damage);

/// <summary>
/// Injuries on body parts plus blood. Pain and capacities are derived from them (and cached until
/// the injuries change). <see cref="Creature.Hp"/> stays the general-condition pool that hunger
/// and cold drain; this tracks wounds.
/// </summary>
public sealed class Health
{
    public required Defs.BodyDef Body;

    /// <summary>Part hit points scale with the creature's max HP (body defs are written for 100).</summary>
    public required float Scale;

    public readonly List<Injury> Injuries = [];
    public readonly List<Scar> Scars = [];

    /// <summary>0..1; collapse below ~0.45, death at 0.</summary>
    public float Blood = 1f;

    private float[]? _capacities;
    private float _pain = -1;

    public void Changed()
    {
        _capacities = null;
        _pain = -1;
    }

    /// <summary>
    /// The cached pain and capacities, for save games: between <c>HealthSystem</c> updates the sim
    /// acts on these (possibly a little stale) values, so a load must restore them, not recompute.
    /// </summary>
    internal (float[]? Capacities, float Pain) Cache
    {
        get => (_capacities, _pain);
        set => (_capacities, _pain) = value;
    }

    public float PartMaxHp(int part) => Body.Parts[part].Hp * Scale;

    public float PartDamage(int part)
    {
        var d = 0f;
        foreach (var inj in Injuries)
            if (inj.Part == part)
                d += inj.Damage;
        foreach (var scar in Scars)
            if (scar.Part == part)
                d += scar.Damage;
        return d;
    }

    /// <summary>The worst infection's severity, 0 if none.</summary>
    public float InfectionSeverity
    {
        get
        {
            var s = 0f;
            foreach (var inj in Injuries)
                if (inj.Infection != null)
                    s = Math.Max(s, inj.Infection.Severity);
            return s;
        }
    }

    /// <summary>Remaining fraction of the part, 0 = destroyed.</summary>
    public float PartHealth(int part) => Math.Clamp(1f - PartDamage(part) / PartMaxHp(part), 0f, 1f);

    /// <summary>Fraction of blood lost per game hour from untended wounds.</summary>
    public float BleedPerHour
    {
        get
        {
            var b = 0f;
            foreach (var inj in Injuries)
                if (!inj.Tended)
                    b += BleedOf(inj);
            return b;
        }
    }

    public float BleedOf(Injury inj) =>
        0.2f * inj.Damage / PartMaxHp(inj.Part) * Body.Parts[inj.Part].BleedFactor * Defs.DamageTypes.Bleed(inj.Type);

    /// <summary>0..1. Wounds to vital parts hurt more; tended ones less; infections and old scars ache.</summary>
    public float Pain
    {
        get
        {
            if (_pain >= 0)
                return _pain;
            var p = 0f;
            foreach (var inj in Injuries)
                p += 0.4f * inj.Damage / PartMaxHp(inj.Part) * (Body.Parts[inj.Part].Vital ? 1.5f : 1f) * (inj.Tended ? 0.6f : 1f)
                    + 0.3f * (inj.Infection?.Severity ?? 0f);
            foreach (var scar in Scars)
                p += 0.1f * scar.Damage / PartMaxHp(scar.Part);
            return _pain = Math.Min(1f, p);
        }
    }

    /// <summary>Overall bodily condition 0..1, for health bars.</summary>
    public float Condition
    {
        get
        {
            var total = 0f;
            var damage = 0f;
            for (var i = 0; i < Body.Parts.Count; i++)
            {
                total += PartMaxHp(i);
                damage += Math.Min(PartDamage(i), PartMaxHp(i));
            }
            return Math.Min(1f - damage / total, Blood);
        }
    }

    /// <summary>
    /// 0..1. Each capacity averages the health of the parts that provide it (1 if none do);
    /// consciousness also suffers from blood loss and pain, and everything else from consciousness.
    /// </summary>
    public float Capacity(Defs.Capacity kind)
    {
        if (_capacities == null)
        {
            var n = Enum.GetValues<Defs.Capacity>().Length;
            var sum = new float[n];
            var count = new int[n];
            for (var i = 0; i < Body.Parts.Count; i++)
                foreach (var c in Body.Parts[i].Capacities)
                {
                    sum[(int)c] += PartHealth(i);
                    count[(int)c]++;
                }
            _capacities = new float[n];
            for (var c = 0; c < n; c++)
                _capacities[c] = count[c] > 0 ? sum[c] / count[c] : 1f;
            var blood = Math.Clamp((Blood - 0.2f) / 0.6f, 0f, 1f);
            var fever = 0.6f * Math.Max(0f, InfectionSeverity - 0.5f);
            var conscious = Math.Clamp(_capacities[0] * blood - 0.5f * Math.Max(0f, Pain - 0.3f) - fever, 0f, 1f);
            _capacities[0] = conscious;
            var alert = Math.Min(1f, conscious + 0.2f);
            for (var c = 1; c < n; c++)
                _capacities[c] *= alert;
        }
        // Blood changes without an injury change, so consciousness (and what depends on it) is
        // recomputed each update by HealthSystem calling Changed().
        return _capacities[(int)kind];
    }
}

/// <summary>
/// The player's own character (Adventure mode). Its brain never runs: it only moves and works from
/// player commands (<see cref="Systems.AvatarSystem"/>), so needs and health still apply but the
/// AI never picks a job for it. What it holds stays in its hands between jobs.
/// </summary>
public struct Avatar
{
    /// <summary>Held movement direction on the map (each component -1, 0 or 1); zero when standing still.</summary>
    public int2 MoveDir;

    /// <summary>The work key is held: jobs make progress only while this is true.</summary>
    public bool WorkHeld;

    /// <summary>Last direction moved, for "the cell in front of me".</summary>
    public int2 Facing;

    /// <summary>
    /// Work that carries on to the next target in reach while the key stays held (building a run
    /// of walls, beating out a spreading fire); None otherwise. Cleared on letting go or walking.
    /// </summary>
    public Systems.AvatarActionKind Chain;

    /// <summary>
    /// A direction change (or stop, when zero) waiting for the avatar to reach <see cref="PendingAt"/>.
    /// In co-op a client shows its own walk straight away and says where it turned or stopped, so
    /// the sim, which gets the command a little later, ends up on the same cell instead of overshooting.
    /// </summary>
    public bool HasPending;
    public int2 PendingDir;
    public int3 PendingAt;
}

/// <summary>
/// On a free bed in a house (Adventure mode): word has got out, and a newcomer will arrive to claim
/// it at <see cref="ArriveTick"/> (see <see cref="Systems.SettlerSystem"/>).
/// </summary>
public struct AwaitingSettler
{
    public long ArriveTick;
}

public enum BodyType : byte
{
    Male,
    Female,
}

/// <summary>
/// Cosmetic hair styles. Saved by value, so new ones go at the end (never reorder).
/// </summary>
public enum HairStyle : byte
{
    Short,
    Long,
    Ponytail,
    Pigtails,
    CurlyShort,
    CurlyLong,
    None,
    SideBraid,
    Topknot,
    Bun,
    CrownBraid,
    Shaggy,
    Swept,
    /// <summary>A high, wavy ponytail, a braid tight along one side of the head, loose waves over the other shoulder.</summary>
    BraidedPonytail,
}

/// <summary>
/// How someone stands about (cosmetic: their idle animation): relaxed, arms crossed, a hand on the
/// hip, or flipping a coin. Saved by value: new ones go at the end.
/// </summary>
public enum Stance : byte
{
    Relaxed,
    Crossed,
    Hip,
    Coin,
}

/// <summary>Cosmetic beards, drawn in the hair colour. Saved by value: new ones go at the end.</summary>
public enum BeardStyle : byte
{
    None,
    Stubble,
    Short,
    Full,
    Braided,
}

/// <summary>Cosmetic eye shapes. Saved by value: new ones go at the end.</summary>
public enum EyeStyle : byte
{
    Round,
    Narrow,
    Sleepy,
    Bright,
}

/// <summary>
/// How a person looks, chosen in the character creator (purely cosmetic: no system reads it, and
/// it's left out of the state hash). Colours are 0xRRGGBB. People without one get a look derived
/// from their entity by the renderer.
/// </summary>
public struct Appearance
{
    public BodyType Body;
    public HairStyle Hair;
    public uint HairColor;
    public uint EyeColor;
    public uint SkinColor;
    public BeardStyle Beard;
    public EyeStyle Eyes;
    public Stance Stance;

    /// <summary>
    /// A character modelled in 3D and rendered to sprites (docs/ART.md "3D to sprites"), named by
    /// its sheets (<c>wanderer3d</c>: <c>creatures/wanderer3d</c>, <c>apparel/vest_wanderer3d</c>);
    /// null for the generated look of <see cref="Body"/> and <see cref="Hair"/>. Its hair and build
    /// are the model's, still tinted with these colours, and it shows the garments rendered for it.
    /// </summary>
    public string? Model;

    /// <summary>The longest <see cref="Model"/> name, which is only letters, digits and underscores.</summary>
    public const int MaxModelLength = 32;

    /// <summary>A model name as it may be used to build sheet names, or null.</summary>
    public static string? ValidModel(string? model) =>
        model is { Length: > 0 and <= MaxModelLength } && model.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_') ? model : null;
}

public enum RequestKind : byte
{
    Follow,
    Guard,
}

/// <summary>
/// Something the player's avatar asked a companion to do until told otherwise (Adventure mode).
/// The brain honours it after emergencies and urgent needs, ahead of ordinary work.
/// <see cref="Target"/> is who to follow or what to guard;
/// <see cref="From"/> is who asked.
/// </summary>
public struct Request
{
    public RequestKind Kind;
    public Entity Target;
    public Entity From;
}

/// <summary>An item kept inside a container building (<see cref="Defs.BuildingProps.StorageStacks"/>).</summary>
public struct Stored
{
    public Entity In;
}

/// <summary>
/// A lockable door, locked: nobody passes. Change it only through <see cref="Simulation.SetLocked"/>,
/// which also tells the map (paths and regions rebuild).
/// </summary>
public struct Locked;

/// <summary>
/// A player up on a piece of solid furniture (<see cref="Defs.ThingDef.Climbable"/>): the one
/// creature allowed in its cell. Set when they climb (the step onto it is let through by
/// <c>MovementSystem</c>) and taken off when they step down to anywhere else.
/// </summary>
public struct Perched
{
    public int3 Cell;
}

/// <summary>A downed creature being carried; its position follows <see cref="By"/>.</summary>
public struct Carried
{
    public Entity By;
}

/// <summary>Need values (0..1) indexed by NeedDef.Index. Only needs the creature has are tracked.</summary>
public sealed class Needs
{
    public float[] Values = [];
    public bool[] Has = [];

    public float Get(Defs.NeedDef def) => Has[def.Index] ? Values[def.Index] : 1f;

    public void Add(Defs.NeedDef def, float delta)
    {
        if (Has[def.Index])
            Values[def.Index] = Math.Clamp(Values[def.Index] + delta, 0f, 1f);
    }

    /// <summary>Set a need outright (tests, scenario setup); ignored if the creature doesn't have it.</summary>
    public void Set(Defs.NeedDef def, float value)
    {
        if (Has[def.Index])
            Values[def.Index] = Math.Clamp(value, 0f, 1f);
    }
}

/// <summary>
/// Continuous position for things that move, in cells. X/Y are horizontal; Z is the height of the
/// creature's feet, so a creature standing in cell z has Pos.Z == z. <see cref="Cell"/> is the cell
/// currently occupied. It's stored rather than derived from Pos because halfway up a ramp the
/// creature is above the next column while its feet are still a level below.
/// </summary>
public struct Position
{
    public float3 Pos;
    public int3 Cell;

    public static Position At(int3 c) => new() { Pos = CenterOf(c), Cell = c };

    /// <summary>Where a creature standing in <paramref name="c"/> is placed: centered, feet on the floor.</summary>
    public static float3 CenterOf(int3 c) => new(c.X + 0.5f, c.Y + 0.5f, c.Z);
}

/// <summary>Path following state.</summary>
public sealed class Mover
{
    public List<int3>? Path;
    public int PathIndex;

    /// <summary>Progress from the current tile to Path[PathIndex], 0..1.</summary>
    public float StepProgress;

    public int3 StepFrom;
    public bool Moving => Path != null && PathIndex < Path.Count;

    public void Stop()
    {
        Path = null;
        PathIndex = 0;
        StepProgress = 0;
    }
}

/// <summary>What a creature is doing and wants to do.</summary>
public sealed class Mind
{
    public Job? Job;

    /// <summary>Don't look for new work until this tick (back-off after failures / idle).</summary>
    public long NextThinkTick;

    public bool Asleep;
    public bool Drafted;

    /// <summary>Work priorities: 0 = disabled, 1 = highest … 4 = lowest. Indexed by WorkType.</summary>
    public int[] WorkPriority = DefaultPriorities();

    public static int[] DefaultPriorities()
    {
        var p = new int[Enum.GetValues<Defs.WorkType>().Length];
        Array.Fill(p, 3);
        p[(int)Defs.WorkType.Firefight] = 1;
        p[(int)Defs.WorkType.Doctor] = 1;
        p[(int)Defs.WorkType.Warden] = 2;
        p[(int)Defs.WorkType.Refuel] = 2;
        p[(int)Defs.WorkType.Construct] = 2;
        return p;
    }

    public string Status => Asleep ? "Sleeping" : Job?.Report ?? "Idle";
}

/// <summary>What a creature is holding. The carried item is an unspawned Thing entity.</summary>
public struct Carrying
{
    public Entity Item;
    public readonly bool HasItem => !Item.IsNull;
}

/// <summary>
/// How a human or dragon feels (0–1, 0.5 neutral): eases toward 0.5 plus its thoughts' offsets
/// (<c>MoodSystem</c>). Only <see cref="Memories"/> and <see cref="Value"/> are state; the
/// situational list is rebuilt every update for the inspector.
/// </summary>
public sealed class Mood
{
    public float Value = 0.5f;
    public List<Memory> Memories = [];

    /// <summary>Where <see cref="Value"/> is heading, as of the last update (derived).</summary>
    public float Target = 0.5f;

    /// <summary>Situational thoughts that held at the last update (derived, for the UI).</summary>
    public readonly List<Defs.ThoughtDef> Situational = [];
}

/// <summary>Someone's lasting nature (<see cref="Defs.TraitDef"/>): rolled at spawn for humans, picked for the avatar.</summary>
public sealed class Traits
{
    public List<Defs.TraitDef> List = [];

    public bool Has(Defs.TraitDef def) => List.Contains(def);
}

/// <summary>A memory thought counting down; <see cref="Other"/> is who it's about, if anyone.</summary>
public sealed class Memory
{
    public required Defs.ThoughtDef Def;
    public int TicksLeft;
    public Entity Other = Entity.Null;
}

/// <summary>
/// A person who belongs to a player (co-op), whether or not that player is connected. It outlives
/// the session, so a returning player gets the same character back; while they're away the
/// <see cref="Avatar"/> is removed and the character lives as an ordinary villager.
/// </summary>
public struct PlayerCharacter
{
    public Guid PlayerId;
}

/// <summary>The role a villager holds in this round, whether a player or the AI is running them.</summary>
public struct Trade
{
    public Defs.TradeDef Def;
}

/// <summary>A creature that owns a bed/nest.</summary>
public struct BedOwner
{
    public Entity Bed;
}

/// <summary>A migrating herd crossing the valley (<c>Systems.WildEvents</c>): it heads for <see cref="To"/>, an edge, and leaves there.</summary>
public struct Migrating
{
    public int3 To;
}

/// <summary>
/// A member of an adventuring party in town (<c>Adventurers.PartySystem</c>). Their party is just an
/// id shared with the others: they arrive, stay and leave together.
/// </summary>
public struct Adventurer
{
    public int Party;
    public Defs.AdventurerKindDef Kind;
    public Defs.PartyDef PartyDef;

    /// <summary>When they move on.</summary>
    public long LeaveTick;

    /// <summary>When they next want a drink.</summary>
    public long NextDrink;

    /// <summary>The quest they've taken and are setting out on (<c>Expeditions/</c>); 0 for none.</summary>
    public int Quest;

    /// <summary>When they came to town (or back to it).</summary>
    public long Arrived;

    /// <summary>
    /// What they think of the town so far: up for each order done (more if done quickly), down
    /// for each they gave up on. M3's Renown is built from it.
    /// </summary>
    public float Satisfaction;
}

/// <summary>
/// Ill from spoiled food (<c>Systems.FoodPoisoning</c>): slower, working less, keeping little down,
/// until <see cref="Until"/> (sooner in bed). Removed once it's passed.
/// </summary>
public struct FoodPoisoned
{
    public long Until;

    /// <summary>How bad, 0..1.</summary>
    public float Severity;
}

