using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// What was wrong with a bowl, as whoever eats it can taste: the one thing that most let it down
/// (<see cref="Cooking.FaultOf"/>). Set when it's ladled; said aloud when it's a poor one. Appended
/// only: saves keep it by number.
/// </summary>
public enum DishFault : byte
{
    None,
    Salty,
    Bland,
    Caught,
    Underdone,

    /// <summary>Tastes of one thing only.</summary>
    Flat,
}

/// <summary>
/// The inn's house dish (docs/DESIGN.md, "Cook"): a pot good enough to put a name to, named by the
/// cook, on the building. The recipe is its flavour: any later pot that tastes near enough the same
/// is the house dish again, so the cook has to hit it from the larder each time. It's posted with
/// the inn's prices, so it's no secret; adventurers ask for it by name, and know it when they get it.
/// </summary>
public sealed class HouseDish
{
    /// <summary>How far a pot's flavour (summed difference of shares) can be from the named one and still be it.</summary>
    public const float Near = 0.35f;

    /// <summary>The chance an adventurer ordering a meal asks for the house dish by name, when there is one.</summary>
    public const float AskChance = 0.35f;

    /// <summary>Satisfaction for the house dish when they asked for it, and off for something else.</summary>
    public const float RightMood = 0.6f, WrongMood = 0.3f;

    public string Name = "";
    public Flavour Flavour;
    public Entity Maker;

    /// <summary>The house dish at a building (the inn), or null.</summary>
    public static HouseDish? Of(Simulation sim, Entity building) =>
        sim.World.IsAlive(building) && sim.World.TryGet<HouseDish>(building, out var h) && h.Name.Length > 0 ? h : null;

    /// <summary>Does a pot (or bowl) of this flavour taste like the house dish?</summary>
    public static bool Like(Flavour flavour, HouseDish house)
    {
        var a = flavour.Shares;
        var b = house.Flavour;
        var off = 0f;
        foreach (var k in Flavour.Kinds)
            off += MathF.Abs(a[k] - b[k]);
        return a.Total > 0f && off <= Near;
    }

    /// <summary>The house dish a pot would serve, if what's in it tastes like it.</summary>
    public static HouseDish? Served(Simulation sim, Entity pot, StewPot p) =>
        Of(sim, Buildings.Of(sim, pot)) is { } house && !p.Empty && Like(p.Flavour, house) ? house : null;

    /// <summary>
    /// The cook names the pot by them the house dish: why not, or null if it's done. It must be
    /// cooked, good enough to put a name to (Fine or better), and at a shop whose meals they see to.
    /// </summary>
    public static string? Christen(Simulation sim, Entity cook, Entity pot, string? name)
    {
        var w = sim.World;
        if (!Cooking.IsPot(sim, pot) || !AvatarSystem.InReach(sim, w.Get<Position>(cook).Cell, pot))
            return "the pot's out of reach";
        var shop = Buildings.Of(sim, pot);
        if (!ShopPrices.Keeps(sim, cook, shop) || !w.Get<Trade>(cook).Def.Serving(ServiceKind.Meal))
            return "it isn't your kitchen";
        var clean = ShopName.Clean(name);
        if (clean.Length == 0)
            return "it wants a name";
        var p = Cooking.PotOf(sim, pot);
        if (p.Empty || !p.Cooked)
            return "it isn't cooked yet";
        if (Crafting.QualityFrom(Cooking.QualityOf(p)) < Quality.Fine)
            return "it isn't good enough to put a name to";
        if (!w.TryGet<HouseDish>(shop, out var h))
            w.Add(shop, h = new HouseDish());
        h.Name = clean;
        h.Flavour = p.Flavour.Shares;
        h.Maker = cook;
        return null;
    }
}

/// <summary>Name the pot in front of you the house dish (<see cref="HouseDish.Christen"/>).</summary>
public sealed record NameDishCommand(Entity Avatar, Entity Pot, string? Name) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!InventoryCommand.Ready(sim, Avatar) || !sim.World.Has<Trade>(Avatar) || !Cooking.IsPot(sim, Pot))
            return;
        var at = sim.World.Get<Thing>(Pot).Cell;
        if (HouseDish.Christen(sim, Avatar, Pot, Name) is { } why)
            sim.Messages.Add(MessageKind.Info, $"Can't name it: {why}.", at);
        else
            sim.Messages.Add(MessageKind.Good, $"The {HouseDish.Of(sim, Buildings.Of(sim, Pot))!.Name} is the house dish now. It's on the sign: make it like this again and it's the same dish.", at);
    }
}
