using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Carpentry;

/// <summary>
/// The carpenter, registered the way a mod registers a trade (docs/MODDING.md): its systems, its
/// components' codecs and its <see cref="CarpenterModule"/>. Nothing in the core names the carpenter;
/// this is how the carpenter reaches the core.
/// </summary>
public sealed class CarpenterMod : ISimMod
{
    public void Register(SimRegistry registry)
    {
        registry.Trade(new CarpenterModule());
        registry.Lesson([.. CarpenterLessons.All]);
        registry.Bot(bot => new CarpenterBot(bot));
        registry.System(() => new Refurnishing(), after: "Repairs");
        registry.System(() => new Felling(), after: "Baking");
        // Felling by hand (format 62): a tree coming down, and the trunk it leaves.
        registry.Component<Falling>("Falling", (w, f) =>
        {
            w.Float(f.Dir);
            w.Long(f.At);
            w.Entity(f.By);
            w.Bool(f.Through);
        }, r => new Falling { Dir = FellSession.Wrap360(r.Float()), At = r.Long(), By = r.Entity(), Through = r.Bool() });
        registry.Component<Trunk>("Trunk", (w, t) =>
        {
            w.Float(t.Dir);
            w.Int(t.Lengths);
            w.Bool(t.GreatButt);
            w.Def(t.Material);
        }, r => new Trunk { Dir = FellSession.Wrap360(r.Float()), Lengths = Math.Clamp(r.Int(), 0, 16), GreatButt = r.Bool(), Material = r.DefOrNull<MaterialDef>() });
        registry.Component<Furnishing>("Furnishing", (w, f) =>
        {
            w.Def(f.Def);
            w.Float(f.Quality);
            w.Entity(f.Maker);
        }, r => new Furnishing { Def = r.Def<ThingDef>(), Quality = Math.Clamp(r.Float(), 0f, 1f), Maker = r.Entity() });
    }
}

/// <summary>The carpenter's hooks: what the core asks a trade, answered for the woodshop and the woods.</summary>
public sealed class CarpenterModule : ITradeModule
{
    public const string TradeName = "Carpenter";

    public string? Trade => TradeName;

    public bool AppliesAt(Simulation sim, Entity worker, Entity station, RecipeDef recipe) => Carpenters.Applies(sim, worker, station, recipe);

    public void WorkAt(Simulation sim, Entity worker, Entity station, RecipeDef recipe) => Carpenters.WorkAt(sim, worker, station, recipe);

    public bool AppliesToRepair(Simulation sim, Entity worker, Entity station, Entity item) => Carpenters.AppliesToRepair(sim, worker, station, item);

    public void BeginMending(Simulation sim, Entity worker, Entity station, Entity item) => Carpenters.BeginMending(sim, worker, station, item);

    public void Made(Simulation sim, List<Entity> made, Entity maker) => Carpenters.Made(sim, made, maker);

    public Job? JobFor(Simulation sim, Entity pawn, WorkType type) => type == WorkType.Craft
        ? Carpenters.Arrows(sim, pawn) ?? Refurnishing.Furnish(sim, pawn) ?? Carpenters.Split(sim, pawn) ?? Carpenters.Saw(sim, pawn) ?? Carpenters.Fletch(sim, pawn)
        : null;

    public AvatarAction? EarlyActionAt(Simulation sim, Entity avatar, int3 cell, int3 here) =>
        // Where a piece of furniture burned, with a new one in your arms: set it there.
        Refurnishing.Of(sim).At(cell) is { } loss && AvatarSystem.InReach(here, cell) && !Refurnishing.Held(sim, avatar).IsNull
            ? new AvatarAction(AvatarActionKind.SetFurniture, $"Set the new {loss.Label} here", cell, Entity.Null)
            : null;

    public AvatarAction? SiteActionAt(Simulation sim, Entity avatar, Entity site, int3 cell)
    {
        var thing = sim.World.Get<Thing>(site);
        if (Felling.IsTrunk(sim, site) && Round.RoundSystem.MayFell(sim, avatar))
            return new AvatarAction(AvatarActionKind.Buck, $"Buck the {thing.Label} into logs", cell, site);
        if (thing.Def.Felling != null && !sim.World.Has<Falling>(site) && Felling.ByHand(sim, avatar, site))
            return new AvatarAction(AvatarActionKind.Fell, $"Fell {thing.Label}", cell, site);
        return null;
    }

    public bool Busy(Simulation sim, Entity site) => sim.World.Has<Falling>(site);

    public void Perform(Simulation sim, Entity avatar, AvatarAction action)
    {
        switch (action.Kind)
        {
            case AvatarActionKind.SetFurniture:
                Refurnishing.Set(sim, avatar, Refurnishing.Of(sim).At(action.Cell));
                break;
            case AvatarActionKind.Fell:
                Felling.StartFelling(sim, avatar, action.Target);
                break;
            case AvatarActionKind.Buck:
                Felling.StartBucking(sim, avatar, action.Target);
                break;
        }
    }

    public void Burned(Simulation sim, ThingDef def, int3 cell, byte facing) =>
        Refurnishing.Burned(sim, def, cell, facing); // a joined piece in a town building: the carpenter's to set back

    /// <summary>
    /// Logs, timber, planks and feathers in the woodshop's chest at the start: three logs to saw (four
    /// planks each), a little timber for mending a bridge or an axle, and a day's quivers. More logs come from felling.
    /// </summary>
    public const int WoodshopLogs = 3, WoodshopTimber = 4, WoodshopPlanks = 6, WoodshopFeathers = 6;

    /// <summary>Stock in the carpenter's chest: logs to saw, planks to fletch from, and feathers for the fletching.</summary>
    public void Stock(Simulation sim, Entity building)
    {
        if (!sim.World.TryGet<PresetBuilding>(building, out var b) || !b.Def.Has(TownRole.Woodshop))
            return;
        var chest = Systems.Stock.ChestOf(sim, building);
        if (chest.IsNull)
            return;
        for (var i = 0; i < WoodshopLogs; i++) // a log's a stack of one
            Put(Carpenters.LogDef, 1, sim.Defs.Material("Pine"));
        foreach (var (def, count) in new[] { ("Wood", WoodshopTimber), ("Planks", WoodshopPlanks), ("Feathers", WoodshopFeathers) })
            Put(def, count, null);

        void Put(string def, int count, MaterialDef? material)
        {
            var rest = sim.Store(chest, sim.CreateItem(sim.Defs.Thing(def), count, material));
            if (!rest.IsNull)
                sim.PlaceItemNear(rest, sim.World.Get<Thing>(chest).Cell);
        }
    }

    public void PracticeCustomer(Simulation sim, Entity player, Entity customer, int index)
    {
        // Archers, every one: the woodshop's customers are those who'll want arrows.
        var w = sim.World;
        if (Combat.Fletching.Shoots(sim, customer))
            return;
        if (w.TryGet<Equipment>(customer, out var held) && w.IsAlive(held.Weapon))
        {
            var old = held.Weapon;
            w.Remove<Equipment>(customer);
            Inventory.Stow(sim, customer, old);
        }
        sim.EquipItem(customer, sim.CreateItem(sim.Defs.Thing("Bow"), 1));
    }

    public IEnumerable<string> PairsDrill(Simulation sim)
    {
        // The pitman: a great log by the sawhorse.
        var woodshop = ShopSystem.BuildingFor(sim, TownRole.Woodshop);
        if (Systems.Stock.StationOf(sim, woodshop, Carpenters.SawhorseDef) is { IsNull: false } horse)
        {
            Scenarios.PairsDrill.Lay(sim, horse, Felling.GreatLogDef, 1);
            yield return "a great log by the sawhorse (the carpenter saws it; someone gets in the pit)";
        }
        // The lifter: a party's wagon with a cracked axle by the woodshop, and a log for the axle.
        if (!woodshop.IsNull && Scenarios.PairsDrill.Wagon(sim, woodshop) is { IsNull: false } wagon)
        {
            Scenarios.PairsDrill.Lay(sim, wagon, "Wood", Tinkering.Wagons.AxleWood + 1);
            yield return "a wagon with a cracked axle by the woodshop (the carpenter fits it; someone lifts)";
        }
    }
}
