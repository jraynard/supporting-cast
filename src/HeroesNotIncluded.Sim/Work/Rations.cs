using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// A bundle of rations as the cook tied it (<see cref="PackSession"/>). Its quality and flavour
/// show (the quality as <see cref="Crafted"/>); <see cref="LooseWrap"/> doesn't: a bundle tied
/// slack lets the air in, and turns twice as fast on the road. Its rot is the item's own
/// <see cref="Rot"/>, hidden like any food's.
/// </summary>
public struct Ration
{
    public float Quality;
    public Flavour Flavour;
    public Entity Maker;
    public bool LooseWrap;
}

/// <summary>Rations as they went out on a quest with their bearer (<c>Expeditions.MemberRecord</c>).</summary>
public sealed record RationRecord(float Quality, float Rot, bool LooseWrap, Entity Maker);

/// <summary>
/// Rations for the road (docs/DESIGN.md, "Cook"): the cook's work that leaves town, as the healer's
/// charms and the tinker's contraptions do. Packed at the kitchen's prep table from three portions
/// of food, wrapped and tied; set out for sale in the inn like any gear; bought, one to a person,
/// by adventurers with a quest ahead. Eaten on the way: sound, they fare a little better and come
/// home less hungry; turned, they sicken on the road and say so when they're back.
/// </summary>
public static class Rations
{
    /// <summary>The thing def rations are made as.</summary>
    public const string Def = "Rations";

    /// <summary>Portions of food that go into one bundle.</summary>
    public const int Portions = 3;

    /// <summary>The most an adventurer carries; past that they won't buy another.</summary>
    public const int MostCarried = 1;

    /// <summary>What rations are worth to a buyer against a plain piece of gear (before how well they're made).</summary>
    public const float Worth = 0.7f;

    /// <summary>How much stronger a well-fed traveller is for a quest, at best (sound rations, perfectly packed).</summary>
    public const float FedBonus = 0.06f;

    /// <summary>How much weaker, sick on the road from rations that turned.</summary>
    public const float SickFactor = 0.88f;

    /// <summary>The food need of someone back from a quest on sound rations (without: 0.35).</summary>
    public const float FedOnReturn = 0.6f;

    public static bool IsRations(Simulation sim, Entity item) => sim.World.Has<Ration>(item);

    /// <summary>Food that can go in a bundle: anything that feeds you, raw or cooked, but not a drink or another bundle.</summary>
    public static bool IsPortion(ThingDef def) => def.IsFood && !def.Drink && !def.Rations;

    /// <summary>Portions of food on someone (in hand and in the pack).</summary>
    public static int PortionsOn(Simulation sim, Entity who) => FoodOn(sim, who).Sum(e => sim.World.Get<Stack>(e).Count);

    /// <summary>The stacks of food on someone, hand first, in a fixed order.</summary>
    public static List<Entity> FoodOn(Simulation sim, Entity who)
    {
        var w = sim.World;
        var list = new List<Entity>();
        if (w.TryGet<Carrying>(who, out var c) && !c.Item.IsNull && w.IsAlive(c.Item) && IsPortion(w.Get<Thing>(c.Item).Def))
            list.Add(c.Item);
        if (sim.PackOf(who) is { IsNull: false } pack)
            foreach (var item in sim.StoredIn(pack))
                if (IsPortion(w.Get<Thing>(item).Def))
                    list.Add(item);
        return list;
    }

    /// <summary>
    /// Tie a bundle from the first <see cref="Portions"/> portions of food on the packer: its flavour
    /// the blend of theirs, its rot their average (unseen). Into the packer's hands, or pack.
    /// Entity.Null if they haven't the food.
    /// </summary>
    public static Entity Make(Simulation sim, Entity packer, float handiwork, bool loose)
    {
        var w = sim.World;
        if (PortionsOn(sim, packer) < Portions)
            return Entity.Null;
        var flavour = default(Flavour);
        var rot = 0f;
        var left = Portions;
        foreach (var food in FoodOn(sim, packer))
        {
            if (left <= 0)
                break;
            var def = w.Get<Thing>(food).Def;
            var n = Math.Min(left, w.Get<Stack>(food).Count);
            flavour += Flavour.Of(def.Flavour) * (def.Nutrition * n);
            rot += FoodPoisoning.RotOf(sim, food) * n;
            left -= n;
            var used = sim.TakeFromStack(food, n);
            ref var carrying = ref w.Get<Carrying>(packer);
            if (carrying.Item == used)
                carrying.Item = Entity.Null;
            w.Destroy(used);
        }
        // How well it's made: mostly the packing, a little the food (a bundle of one thing is dull going).
        var quality = Math.Clamp(0.8f * handiwork + 0.2f * flavour.Balance, 0f, 1f);
        var item = sim.CreateItem(sim.Defs.Thing(Def), 1);
        w.Add(item, new Ration { Quality = quality, Flavour = flavour.Shares, Maker = packer, LooseWrap = loose });
        w.Add(item, new Crafted { Quality = Crafting.QualityFrom(quality), Maker = packer });
        if (w.Has<Rot>(item))
            w.Get<Rot>(item).Progress = rot / Portions;
        ref var hands = ref w.Get<Carrying>(packer);
        if (hands.Item.IsNull)
            hands.Item = item;
        else
            Inventory.Stow(sim, packer, item);
        return item;
    }

    /// <summary>The rations someone has on them (in hand or in the pack), in a fixed order.</summary>
    public static List<Entity> Carried(Simulation sim, Entity who)
    {
        var list = new List<Entity>();
        var w = sim.World;
        if (w.TryGet<Carrying>(who, out var c) && !c.Item.IsNull && w.Has<Ration>(c.Item))
            list.Add(c.Item);
        if (sim.PackOf(who) is { IsNull: false } pack)
            foreach (var item in sim.StoredIn(pack))
                if (w.Has<Ration>(item))
                    list.Add(item);
        return list;
    }

    // ---------------- On a quest ----------------

    /// <summary>The rations someone carries, as records to go out with them.</summary>
    public static List<RationRecord> Records(Simulation sim, Entity who) =>
        Carried(sim, who).Select(e =>
        {
            var r = sim.World.Get<Ration>(e);
            return new RationRecord(r.Quality, FoodPoisoning.RotOf(sim, e), r.LooseWrap, r.Maker);
        }).ToList();

    /// <summary>
    /// How far gone a bundle is when it's eaten, halfway out: what it left with, and the road's
    /// days on top (as fast as at a warm room's, twice that tied slack).
    /// </summary>
    public static float RotWhenEaten(Simulation sim, RationRecord r, float hoursOut)
    {
        var days = sim.Defs.Thing(Def).RotDays;
        var gained = days <= 0 ? 0f : hoursOut * 0.5f / 24f / days * (r.LooseWrap ? 2f : 1f);
        return Math.Min(1f, r.Rot + gained);
    }

    /// <summary>Did this bundle turn before it was eaten?</summary>
    public static bool Turned(Simulation sim, RationRecord r, float hoursOut) => FoodPoisoning.Spoiled(RotWhenEaten(sim, r, hoursOut));

    /// <summary>
    /// How much a bearer's rations add to (or, turned, take from) their strength for a quest
    /// <paramref name="hoursOut"/> long. 1 with none.
    /// </summary>
    public static float StrengthFactor(Simulation sim, List<RationRecord> rations, float hoursOut)
    {
        if (rations.Count == 0)
            return 1f;
        var r = rations[0];
        return Turned(sim, r, hoursOut) ? SickFactor : 1f + FedBonus * (0.5f + 0.5f * r.Quality);
    }

    /// <summary>Rations not eaten (someone carrying more than they needed): back in the pack, a little further gone.</summary>
    public static void Restore(Simulation sim, Entity who, IEnumerable<RationRecord> rations)
    {
        foreach (var r in rations)
        {
            var item = sim.CreateItem(sim.Defs.Thing(Def), 1);
            sim.World.Add(item, new Ration { Quality = r.Quality, Maker = r.Maker, LooseWrap = r.LooseWrap });
            sim.World.Add(item, new Crafted { Quality = Crafting.QualityFrom(r.Quality), Maker = r.Maker });
            if (sim.World.Has<Rot>(item))
                sim.World.Get<Rot>(item).Progress = r.Rot;
            Inventory.Stow(sim, who, item);
        }
    }

    /// <summary>The prep table at a building (the inn's kitchen), Entity.Null if none.</summary>
    public static bool IsPrepTable(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Building is { PrepTable: true } && !sim.World.Has<Construction>(e);

    /// <summary>Raw food in hand that the prep table's knife can chop, or Entity.Null.</summary>
    public static Entity Choppable(Simulation sim, Entity cook)
    {
        var held = sim.World.Get<Carrying>(cook).Item;
        return !held.IsNull && sim.World.IsAlive(held) && sim.World.Get<Thing>(held).Def.ChopsInto != null ? held : Entity.Null;
    }

    /// <summary>
    /// What the work key does at a prep table: chop the raw food in your hand, else pack rations if
    /// you have the food. Null if nothing. (To pack with something choppable in hand, stow it first.)
    /// </summary>
    public static string? StepAt(Simulation sim, Entity cook, Entity station)
    {
        if (!IsPrepTable(sim, station))
            return null;
        if (Choppable(sim, cook) is { IsNull: false } food)
            return $"Chop the {sim.World.Get<Thing>(food).Def.Label}";
        return PortionsOn(sim, cook) >= Portions ? "Pack rations for the road" : null;
    }

    /// <summary>Start chopping or packing at the table, or say why not.</summary>
    public static void Start(Simulation sim, Entity cook, Entity table)
    {
        if (!IsPrepTable(sim, table) || !AvatarSystem.InReach(sim, sim.World.Get<Position>(cook).Cell, table))
            return;
        if (Choppable(sim, cook) is { IsNull: false } food)
        {
            WorkSystem.Start(sim, cook, ChopSession.For(sim, table, food));
            return;
        }
        if (PortionsOn(sim, cook) < Portions)
        {
            sim.Messages.Add(MessageKind.Info, $"A bundle takes {Portions} portions of food: bring them from the larder.", sim.World.Get<Thing>(table).Cell);
            return;
        }
        WorkSystem.Start(sim, cook, new PackSession { Table = table });
    }
}
