using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>What the practice yard can set up (<see cref="PracticeCommand"/>).</summary>
public enum PracticeAction : byte
{
    /// <summary>A party rides in now, with what the player's trade works on (<see cref="PracticeCommand.Arg"/>, a <see cref="CustomerKind"/>).</summary>
    SendCustomers,

    /// <summary>The player's workplace stocked again.</summary>
    Restock,

    /// <summary>The player whole, fed, rested, with full mana.</summary>
    Freshen,

    /// <summary>The healer's: someone on a shrine pallet with a wound of a kind (<see cref="PracticeCommand.Arg"/>, a <see cref="DamageType"/>).</summary>
    Patient,

    /// <summary>A lesson's setup (<see cref="LessonDef"/>, by index in the player's trade's lessons): stock, supplies, customers.</summary>
    Lesson,

    /// <summary>The healer's: a patient with more to them than one wound (<see cref="PracticeCommand.Arg"/>, a <see cref="PatientCase"/>).</summary>
    PatientCase,
}

/// <summary>A practice patient beyond one plain wound.</summary>
public enum PatientCase : byte
{
    /// <summary>A cut, a bruise and a burn: one sitting, wound after wound.</summary>
    Many,

    /// <summary>A cut bound up, festering under the dressing: only a long press finds it.</summary>
    Festering,
}

/// <summary>
/// The practice yard (from the title menu): Millbrook, a round in the trade you pick, and no
/// season to it (<see cref="Round.RoundSystem.Practice"/>): no festival, no Renown or Dread, no
/// crises, and adventurers only when sent for. A panel sets up what each of the trade's games
/// works on, so they can be tried one by one before a round asks for them under pressure.
/// </summary>
public static class Practice
{
    /// <summary>The wounds a practice patient can be brought in with.</summary>
    public static readonly DamageType[] PatientWounds = [DamageType.Cut, DamageType.Blunt, DamageType.Burn, DamageType.Fever];

    /// <summary>How bad a practice wound is: enough to want mending, not enough to kill.</summary>
    public const float WoundDamage = 12f;

    /// <summary>The workplace of the player's trade, Entity.Null if it has none in town.</summary>
    public static Entity Workplace(Simulation sim, Entity player)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(player, out var trade) || !trade.Def.HasWorkplace)
            return Entity.Null;
        foreach (var b in w.Query<PresetBuilding>())
            if (trade.Def.WorksAt(w.Get<PresetBuilding>(b).Def))
                return b;
        return Entity.Null;
    }

    public static void Apply(Simulation sim, Entity player, PracticeAction action, int arg)
    {
        var w = sim.World;
        if (!sim.Round.Practice || !w.IsAlive(player) || !w.Has<Avatar>(player))
            return;
        switch (action)
        {
            case PracticeAction.SendCustomers:
                if (Enum.IsDefined((CustomerKind)arg))
                    SendCustomers(sim, player, (CustomerKind)arg);
                break;
            case PracticeAction.Restock:
                VillageRound.Restock(sim, Workplace(sim, player));
                sim.Messages.Add(MessageKind.Good, "Your workplace is stocked again.", null);
                break;
            case PracticeAction.Freshen:
                Arena.Freshen(sim, player);
                if (w.Has<Mana>(player))
                    w.Get<Mana>(player).Current = w.Get<Mana>(player).Max;
                break;
            case PracticeAction.Patient:
                if (Enum.IsDefined((DamageType)arg) && PatientWounds.Contains((DamageType)arg))
                    BringPatient(sim, player, (DamageType)arg);
                break;
            case PracticeAction.PatientCase:
                if (Enum.IsDefined((PatientCase)arg))
                    BringPatient(sim, player, (PatientCase)arg);
                break;
            case PracticeAction.Lesson:
                if (w.TryGet<Trade>(player, out var trade) && arg >= 0 && arg < trade.Def.Lessons.Count)
                    SetUp(sim, player, trade.Def.Lessons[arg]);
                break;
        }
    }

    /// <summary>What a lesson needs to be there when it begins: the workplace stocked, materials in the pack, customers on their way.</summary>
    public static void SetUp(Simulation sim, Entity player, LessonDef lesson)
    {
        if (lesson.Restock)
            VillageRound.Restock(sim, Workplace(sim, player));
        foreach (var item in lesson.Supply)
            Inventory.Stow(sim, player, sim.CreateItem(item.ThingDef, item.Count, Simulation.MaterialFor(item.ThingDef, null)));
        if (lesson.Customers is { } wear)
            SendCustomers(sim, player, wear);
        if (lesson.PatientCase is { } kind)
            BringPatient(sim, player, kind);
        else if (lesson.Patient is { } wound && PatientWounds.Contains(wound))
            BringPatient(sim, player, wound);
        if (lesson.Spent && sim.World.Has<Mana>(player))
            sim.World.Get<Mana>(player).Current = 0f;
    }

    /// <summary>
    /// A party now, the way the player's trade wants it: hurt for a healer, their blades worn for a
    /// smith (chipped, or only dull), a locked strongbox and a curio for a tinker, archers for a carpenter; hungry, for the inn.
    /// </summary>
    /// <summary>Silver each practice customer brings beyond their party's purse, for what they came for.</summary>
    public const int CustomerPurse = 15;

    public static List<Entity> SendCustomers(Simulation sim, Entity player, CustomerKind wear = CustomerKind.Trade)
    {
        var w = sim.World;
        var parties = sim.Defs.AllParties.Where(p => !p.Traveller && !p.Rare).ToList();
        if (parties.Count == 0)
            return [];
        // A customer or two, not a whole party through the door: one for a drink or a quest, two where
        // a chapter wants a pair (the tinker's box and curio) or a spare (a smith's blade it can't mend).
        var most = wear is CustomerKind.Thirsty or CustomerKind.Questing ? 1 : 2;
        var party = sim.Parties.Send(sim, parties[sim.Rand.NextInt(parties.Count)], most);
        var trade = w.TryGet<Trade>(player, out var t) ? t.Def.Name : "";
        for (var k = 0; k < party.Count; k++)
        {
            var e = party[k];
            Hospitality.DirectionsSystem.KnowsTheTown(sim, e); // they've been here before: no asking the way
            // Silver to spare for what they came for: a meal and a round on the way mustn't leave the
            // lesson's customer unable to pay for it (a tinker's curio went unasked for, its bearer broke).
            Inventory.Stow(sim, e, sim.CreateItem(sim.Defs.Thing(Shops.Coin.Silver), CustomerPurse));
            switch (trade)
            {
                case "Empath":
                    if (w.TryGet<Health>(e, out var h))
                    {
                        var kind = PatientWounds[k % (PatientWounds.Length - 1)]; // fever's its own button
                        HealthSystem.Damage(sim, e, WoundDamage, kind, PartFor(h));
                    }
                    break;
                case "Blacksmith":
                    if (w.TryGet<Equipment>(e, out var eq) && w.IsAlive(eq.Weapon) && !SmithyMends(sim, player, eq.Weapon))
                    {
                        // Sound and keen, so they don't ask for what the smithy can't do.
                        ref var sound = ref w.Get<Thing>(eq.Weapon);
                        sound.Hp = sound.MaxHp;
                        Combat.WeaponWear.SetDullness(sim, eq.Weapon, 0f);
                    }
                    else if (w.TryGet<Equipment>(e, out eq) && w.IsAlive(eq.Weapon))
                    {
                        if (wear == CustomerKind.Dull)
                        {
                            // Sound but dull: a grindstone's work alone. An edged blade only (a mace has no edge to lose).
                            if (Combat.WeaponWear.Edged(w.Get<Thing>(eq.Weapon).Def))
                            {
                                ref var whole = ref w.Get<Thing>(eq.Weapon);
                                whole.Hp = whole.MaxHp;
                                Combat.WeaponWear.SetDullness(sim, eq.Weapon, DullBlade);
                            }
                        }
                        else
                        {
                            ref var weapon = ref w.Get<Thing>(eq.Weapon);
                            weapon.Hp = weapon.MaxHp * ChippedBlade;
                        }
                    }
                    break;
                case "Tinker":
                    if (k == 0)
                        Inventory.Stow(sim, e, Tinkering.Lockwork.Make(sim, sim.Rand, 2f, 20, "the practice yard"));
                    else if (k == 1 && Tinkering.Curios.Make(sim, sim.Rand, 2f) is { IsNull: false } curio)
                        Inventory.Stow(sim, e, curio);
                    break;
                case "Carpenter":
                    // Archers, every one: the woodshop's customers are those who'll want arrows.
                    if (!Carpentry.Fletching.Shoots(sim, e))
                    {
                        if (w.TryGet<Equipment>(e, out var held) && w.IsAlive(held.Weapon))
                        {
                            var old = held.Weapon;
                            w.Remove<Equipment>(e);
                            Inventory.Stow(sim, e, old);
                        }
                        sim.EquipItem(e, sim.CreateItem(sim.Defs.Thing("Bow"), 1));
                    }
                    break;
                default:
                    if (wear == CustomerKind.Thirsty)
                        w.Get<Adventurer>(e).NextDrink = sim.Clock.Tick; // the road's dust in their throats
                    else if (wear == CustomerKind.Questing)
                        w.Get<Adventurer>(e).Arrived = sim.Clock.Tick - SimClock.HoursToTicks(Shops.ShopSystem.QuestAfterHours); // settled in, and wanting work
                    else if (w.TryGet<Needs>(e, out var needs))
                        needs.Set(sim.Defs.Need("Food"), 0.3f);
                    break;
            }
        }
        sim.Messages.Add(MessageKind.Info, party.Count == 1 ? "An adventurer rides into town." : "Adventurers ride into town.", party.Count > 0 ? w.Get<Position>(party[0]).Cell : null);
        return party;
    }

    /// <summary>How worn a practice customer's blade is: this much of its HP left, chipped; or this dull, only dull.</summary>
    public const float ChippedBlade = 0.4f, DullBlade = 0.7f;

    /// <summary>
    /// Can the smith mend this weapon with what the trade can make? A bronze spear needs a bronze
    /// bar the smithy never has, so a practice customer with one would wait for nothing.
    /// </summary>
    public static bool SmithyMends(Simulation sim, Entity player, Entity weapon)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(player, out var trade))
            return false;
        foreach (var station in trade.Def.KnownStations)
        {
            if (Crafting.RepairRecipe(station, w.Get<Thing>(weapon).Def) is not { } recipe)
                continue;
            var material = recipe.Ingredients[0].ThingDef;
            return trade.Def.KnownStations.Any(s => s.Recipes.Any(r => r.Products.Exists(p => p.ThingDef == material)));
        }
        return false;
    }

    /// <summary>A limb, not something vital: a practice wound shouldn't kill.</summary>
    private static int PartFor(Health h)
    {
        var arm = h.Body.Parts.FindIndex(p => p.Label.Contains("arm") && !p.Vital);
        return arm >= 0 ? arm : h.Body.Parts.FindIndex(p => !p.Vital);
    }

    /// <summary>Who the practice patient is, every time: the town's most accident-prone.</summary>
    public const string PatientName = "Torvi";

    /// <summary>The practice patient: someone lying on a shrine pallet with a wound (or a fever), keeping still for the healer.</summary>
    public static Entity BringPatient(Simulation sim, Entity player, DamageType wound)
    {
        var w = sim.World;
        var patient = PatientOnAPallet(sim, player);
        var at = w.Get<Position>(patient).Cell;
        if (wound == DamageType.Fever)
            FeverSystem.Catch(sim, patient);
        else
            HealthSystem.Damage(sim, patient, WoundDamage, wound, PartFor(w.Get<Health>(patient)));
        sim.Messages.Add(MessageKind.Info, $"{PatientName} is brought in, {DamageTypes.Wound(wound)} and all.", at);
        return patient;
    }

    /// <summary>The practice patient with more to them: several wounds at once, or one festering under its dressing.</summary>
    public static Entity BringPatient(Simulation sim, Entity player, PatientCase kind)
    {
        var w = sim.World;
        var patient = PatientOnAPallet(sim, player);
        var h = w.Get<Health>(patient);
        int Part(string label) => h.Body.Parts.FindIndex(p => p.Label.Contains(label) && !p.Vital) is var i and >= 0 ? i : PartFor(h);
        if (kind == PatientCase.Many)
        {
            // Three kinds that feel different under the hands (a cut is the first lesson's), the fever among them.
            HealthSystem.Damage(sim, patient, WoundDamage * 0.7f, DamageType.Blunt, Part("leg"));
            HealthSystem.Damage(sim, patient, WoundDamage * 0.5f, DamageType.Burn, Part("hand"));
            FeverSystem.Catch(sim, patient);
            sim.Messages.Add(MessageKind.Info, $"{PatientName} is brought in, bruised, burned and feverish: a bad day.", w.Get<Position>(patient).Cell);
            return patient;
        }
        HealthSystem.Damage(sim, patient, WoundDamage, DamageType.Cut, Part("arm"));
        var cut = h.Injuries.Last();
        Bandaging.Dress(sim, cut, 0.4f);
        cut.Infection = new Infection { Severity = 0.25f };
        cut.InfectionRolled = true;
        h.Changed();
        sim.Messages.Add(MessageKind.Info, $"{PatientName} is brought in, a cut bound up days ago. It smells wrong.", w.Get<Position>(patient).Cell);
        return patient;
    }

    /// <summary>
    /// The practice patient, lying on a free shrine pallet (the one nearest the healer's corner: the
    /// wash basin and the herbs), whole for now and keeping still for the healer.
    /// </summary>
    private static Entity PatientOnAPallet(Simulation sim, Entity player)
    {
        var w = sim.World;
        var shrine = Workplace(sim, player);
        var pallets = shrine.IsNull ? [] : Shops.ShopSystem.BedsOf(sim, shrine);
        var basin = shrine.IsNull ? Entity.Null : Buildings.Parts(sim, shrine).FirstOrDefault(e => w.Get<Thing>(e).Def.Building is { Washes: true }, Entity.Null);
        var free = pallets.Select(p => w.Get<Thing>(p).Cell).Where(c => !sim.Creatures().Any(e => w.Get<Position>(e).Cell == c)).ToList();
        if (!basin.IsNull)
            free = free.OrderBy(c => Maps.Map.Distance(c, w.Get<Thing>(basin).Cell)).ToList();
        var at = free.Count > 0 ? free[0] : w.Get<Position>(player).Cell;
        var patient = sim.SpawnCreature(sim.Defs.Creature("Human"), PatientName, at, ageDays: 30 * 365);
        Array.Fill(w.Get<Needs>(patient).Values, 1f);
        w.Get<Mind>(patient).Drafted = true; // lying still for it, as in a bed
        return patient;
    }
}

/// <summary>Set something up in the practice yard (<see cref="Practice"/>): nothing at all outside it.</summary>
public sealed record PracticeCommand(Entity Player, PracticeAction Action, int Arg = 0) : IHostCommand, ISimCommand
{
    public void Apply(Simulation sim)
    {
        if (Enum.IsDefined(Action))
            Practice.Apply(sim, Player, Action, Arg);
    }
}
