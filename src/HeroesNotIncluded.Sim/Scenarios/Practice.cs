using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>What the practice yard can set up (<see cref="PracticeCommand"/>).</summary>
public enum PracticeAction : byte
{
    /// <summary>A party rides in now, with what the player's trade works on (<see cref="PracticeCommand.Arg"/>, a <see cref="CustomerWear"/>).</summary>
    SendCustomers,

    /// <summary>The player's workplace stocked again.</summary>
    Restock,

    /// <summary>The player whole, fed, rested, with full mana.</summary>
    Freshen,

    /// <summary>The healer's: someone on a shrine pallet with a wound of a kind (<see cref="PracticeCommand.Arg"/>, a <see cref="DamageType"/>).</summary>
    Patient,

    /// <summary>A lesson's setup (<see cref="LessonDef"/>, by index in the player's trade's lessons): stock, supplies, customers.</summary>
    Lesson,
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
        if (!w.TryGet<Trade>(player, out var trade) || trade.Def.WorkplaceDef is not { } def)
            return Entity.Null;
        foreach (var b in w.Query<PresetBuilding>())
            if (w.Get<PresetBuilding>(b).Def == def)
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
                if (Enum.IsDefined((CustomerWear)arg))
                    SendCustomers(sim, player, (CustomerWear)arg);
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
    }

    /// <summary>
    /// A party now, the way the player's trade wants it: hurt for a healer, their blades worn for a
    /// smith (chipped, or only dull), a locked strongbox and a curio for a tinker; hungry, for the inn.
    /// </summary>
    public static List<Entity> SendCustomers(Simulation sim, Entity player, CustomerWear wear = CustomerWear.Trade)
    {
        var w = sim.World;
        var parties = sim.Defs.AllParties;
        if (parties.Count == 0)
            return [];
        var party = sim.Parties.Send(sim, parties[sim.Rand.NextInt(parties.Count)]);
        var trade = w.TryGet<Trade>(player, out var t) ? t.Def.Name : "";
        for (var k = 0; k < party.Count; k++)
        {
            var e = party[k];
            Hospitality.DirectionsSystem.KnowsTheTown(sim, e); // they've been here before: no asking the way
            switch (trade)
            {
                case "Healer":
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
                        if (wear == CustomerWear.Dull)
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
                default:
                    if (w.TryGet<Needs>(e, out var needs))
                        needs.Set(sim.Defs.Need("Food"), 0.3f);
                    break;
            }
        }
        sim.Messages.Add(MessageKind.Info, "Adventurers ride into town.", party.Count > 0 ? w.Get<Position>(party[0]).Cell : null);
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

    /// <summary>The practice patient: someone lying on a shrine pallet with a wound (or a fever), keeping still for the healer.</summary>
    public static Entity BringPatient(Simulation sim, Entity player, DamageType wound)
    {
        var w = sim.World;
        var shrine = Workplace(sim, player);
        var pallets = shrine.IsNull ? [] : Shops.ShopSystem.BedsOf(sim, shrine);
        var at = pallets.Select(p => w.Get<Thing>(p).Cell).FirstOrDefault(c => !sim.Creatures().Any(e => w.Get<Position>(e).Cell == c),
            w.Get<Position>(player).Cell);
        var patient = sim.SpawnCreature(sim.Defs.Creature("Human"), "Torvi", at, ageDays: 30 * 365);
        Array.Fill(w.Get<Needs>(patient).Values, 1f);
        w.Get<Mind>(patient).Drafted = true; // lying still for it, as in a bed
        if (wound == DamageType.Fever)
            FeverSystem.Catch(sim, patient);
        else
            HealthSystem.Damage(sim, patient, WoundDamage, wound, PartFor(w.Get<Health>(patient)));
        sim.Messages.Add(MessageKind.Info, $"Torvi is brought in, {DamageTypes.Wound(wound)} and all.", at);
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
