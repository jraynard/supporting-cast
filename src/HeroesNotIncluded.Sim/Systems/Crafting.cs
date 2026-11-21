using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Systems;

/// <summary>
/// The rules of making things at a workbench, shared by villagers' <c>CraftJob</c>, the player's
/// crafting and the bill commands: what an ingredient may be, what's brought (staged) for a
/// craft, what it makes, and what a bill still wants. Stateless: a station's state is its
/// <see cref="Workbench"/> component.
/// </summary>
public static class Crafting
{
    /// <summary>Every bench's bill list is capped (<see cref="WorkbenchProps.Bills"/>); this is the fallback.</summary>
    public const int DefaultMaxBills = 8;

    /// <summary>A finished station that recipes are made at (not a blueprint of one).</summary>
    public static bool IsStation(Simulation sim, Entity e) =>
        sim.World.IsAlive(e) && sim.World.Has<Workbench>(e) && !sim.World.Has<Construction>(e);

    /// <summary>Can this stack count toward an ingredient entry (and the bill's material filter)?</summary>
    public static bool Accepts(ThingCount ingredient, Bill? bill, ThingDef def, MaterialDef? material)
    {
        if (def != ingredient.ThingDef)
            return false;
        if (ingredient.MaterialDef != null && material != ingredient.MaterialDef)
            return false;
        // A filter only binds ingredients that come in that material (oak only means oak wood, not oak and stone).
        return bill?.Material == null || !bill.Material.Fits(def) || material == bill.Material;
    }

    /// <summary>
    /// The station is fit to work: lit, if it burns fuel (a cooking fire, a kiln), and hot enough
    /// for a recipe that needs heat (a forge).
    /// </summary>
    /// <summary>The bellows beside a station (a finished one, on its level), or null.</summary>
    public static BellowsProps? BellowsOf(Simulation sim, Entity station)
    {
        var w = sim.World;
        var map = sim.Map;
        var at = w.Get<Thing>(station).Cell;
        foreach (var d in Maps.Map.Neighbors4)
        {
            var n = new int3(at.X + d.X, at.Y + d.Y, at.Z);
            if (!map.InBounds(n) || map.Structure[map.Idx(n)] is not { IsNull: false } s || w.Has<Construction>(s))
                continue;
            if (w.Get<Thing>(s).Def.Building?.Bellows is { } bellows)
                return bellows;
        }
        return null;
    }

    /// <summary>
    /// Hot enough now, or could be: lit, with bellows beside it that lift its fuel's reach past
    /// what the recipe needs. A smith may start then, and pumps till it's there.
    /// </summary>
    public static bool CanGetHotEnough(Simulation sim, Entity station, RecipeDef recipe)
    {
        if (HotEnough(sim, station, recipe))
            return true;
        var w = sim.World;
        if (recipe.MinTemp <= 0 || BellowsOf(sim, station) is not { } bellows
            || w.Get<Thing>(station).Def.Building?.Fuel is not { } props || !w.TryGet<Fueled>(station, out var fueled) || fueled.Fuel <= 0)
            return false;
        return fueled.Burn(props).Reach + bellows.Lift >= recipe.MinTemp;
    }

    /// <summary>
    /// Hot enough now, or will be once a craft is under way: a forge or kiln fires only for work
    /// (<see cref="FuelProps.OnDemand"/>), so a cold one with fuel in it is ready to start.
    /// </summary>
    public static bool CanFireFor(Simulation sim, Entity station, RecipeDef recipe) =>
        HotEnough(sim, station, recipe)
        || (sim.World.Get<Thing>(station).Def.Building?.Fuel is { OnDemand: true } && sim.World.TryGet<Fueled>(station, out var fuel) && fuel.Fuel > 0);

    /// <summary>Work the bellows beside a station (if there are any): it stays blown a little while after.</summary>
    public static void Pump(Simulation sim, Entity station)
    {
        if (BellowsOf(sim, station) == null)
            return;
        var until = sim.Clock.Tick + BlowTicks;
        if (sim.World.Has<Blown>(station))
            sim.World.Get<Blown>(station).Until = until;
        else
            sim.World.Add(station, new Blown { Until = until });
    }

    /// <summary>How long a station stays blown after the last pump.</summary>
    public const int BlowTicks = 600;

    public static bool HotEnough(Simulation sim, Entity station, RecipeDef recipe)
    {
        var w = sim.World;
        if (w.Get<Thing>(station).Def.Building?.Fuel != null && (!w.TryGet<Fueled>(station, out var fuel) || fuel.Fuel <= 0))
            return false;
        return recipe.MinTemp <= 0 || sim.TemperatureAt(w.Get<Thing>(station).Cell) >= recipe.MinTemp;
    }

    /// <summary>
    /// Does this bill want another one made? Count bills until they're done; "until you have" ones
    /// while fewer of the first product are lying about or stored (packs don't count).
    /// </summary>
    public static bool WantsMore(Simulation sim, Bill bill)
    {
        if (bill.Suspended)
            return false;
        switch (bill.Mode)
        {
            case BillMode.Count:
                return bill.Done < bill.Target;
            case BillMode.UntilYouHave:
                var product = bill.Recipe.Products[0].ThingDef;
                var have = 0;
                foreach (var e in sim.AvailableItems())
                    if (sim.World.Get<Thing>(e).Def == product && (have += sim.World.Get<Stack>(e).Count) >= bill.Target)
                        return false;
                return true;
            default:
                return true;
        }
    }

    /// <summary>
    /// Put units of a stack toward the station's current recipe: as many as the first entries it
    /// fits still need. Used-up units are gone (the stack is destroyed when empty). Returns the
    /// number taken.
    /// </summary>
    public static int Stage(Simulation sim, Entity station, Entity stack) => Stage(sim, sim.World.Get<Workbench>(station), stack);

    private static int Stage(Simulation sim, Workbench bench, Entity stack)
    {
        var w = sim.World;
        if (bench.Active is not { } recipe || !w.IsAlive(stack))
            return 0;
        var t = w.Get<Thing>(stack);
        var bill = bench.By.IsNull ? bench.BillFor(recipe) : null;
        ref var units = ref w.Get<Stack>(stack);
        var taken = 0;
        for (var i = 0; i < recipe.Ingredients.Count && units.Count > 0; i++)
        {
            if (!Accepts(recipe.Ingredients[i], bill, t.Def, t.Material))
                continue;
            var n = Math.Min(units.Count, recipe.Ingredients[i].Count - bench.Staged[i]);
            if (n <= 0)
                continue;
            bench.Staged[i] += n;
            units.Count -= n;
            taken += n;
            if (t.Material is { } m)
                bench.AddMaterial(m, n);
            if (w.TryGet<Rot>(stack, out var rot))
            {
                bench.Rot = (bench.Rot * bench.RotUnits + rot.Progress * n) / (bench.RotUnits + n);
                bench.RotUnits += n;
            }
        }
        if (units.Count <= 0)
            sim.DestroyThing(stack);
        return taken;
    }

    /// <summary>Give back what was brought for the current craft (at the station) and free it.</summary>
    public static void Cancel(Simulation sim, Entity station)
    {
        var w = sim.World;
        var bench = w.Get<Workbench>(station);
        if (bench.Active is { } recipe)
        {
            var cell = w.Get<Thing>(station).Cell;
            var material = bench.MainMaterial;
            for (var i = 0; i < bench.Staged.Length; i++)
                if (bench.Staged[i] > 0)
                {
                    var c = recipe.Ingredients[i];
                    sim.SpawnItem(c.ThingDef, bench.Staged[i], cell, c.MaterialDef ?? Simulation.MaterialFor(c.ThingDef, material), bench.Rot);
                }
        }
        bench.Clear();
    }

    /// <summary>
    /// The craft is done: make the products (in the ingredients' main material where they come in
    /// one, cooked food a little fresher than what went in), count it on the bill and free the
    /// station. Returns the products, unspawned, for the caller to put down or stow.
    /// </summary>
    /// <param name="quality">How it came out, when the crafter's hands decided (a mini-game); null rolls for it.</param>
    public static List<Entity> Finish(Simulation sim, Entity station, Entity crafter, Quality? quality = null)
    {
        var w = sim.World;
        var bench = w.Get<Workbench>(station);
        var recipe = bench.Active ?? throw new InvalidOperationException("nothing is being made");
        var made = MakeProducts(sim, recipe, bench.MainMaterial, bench.RotUnits > 0 ? bench.Rot : null, crafter, w.Get<Thing>(station).Cell, quality);
        // Made the plain way (not played by hand): a lock gets cut and keyed, a contraption put together.
        if (quality == null)
        {
            Tinkering.Tinkers.Made(sim, made, crafter);
            Modding.Trades.Made(sim, made, crafter);
        }
        if (bench.By.IsNull && bench.BillFor(recipe) is { } bill)
        {
            bill.Done++;
            if (bill.Mode == BillMode.Count && bill.Done >= bill.Target)
                bench.Bills.Remove(bill);
        }
        bench.Clear();
        return made;
    }

    /// <summary>
    /// A recipe's products, unspawned: in <paramref name="material"/> where they come in it, cooked
    /// food half as far gone as what went in, gear with a quality rolled from the maker's skill.
    /// </summary>
    public static List<Entity> MakeProducts(Simulation sim, RecipeDef recipe, MaterialDef? material, float? rot, Entity crafter, int3 at, Quality? decided = null)
    {
        var w = sim.World;
        var made = new List<Entity>();
        foreach (var p in recipe.Products)
        {
            var item = sim.CreateItem(p.ThingDef, p.Count, p.MaterialDef ?? Simulation.MaterialFor(p.ThingDef, material));
            if (rot is { } r && w.Has<Rot>(item))
                w.Get<Rot>(item).Progress = r * 0.5f;
            if (IsGear(p.ThingDef))
            {
                var quality = decided ?? RollQuality(sim, crafter, recipe);
                w.Add(item, new Crafted { Quality = quality, Maker = crafter });
                if (quality == Quality.Masterwork && w.TryGet<Creature>(crafter, out var maker))
                    sim.Messages.Add(MessageKind.Good, $"{maker.Name} made a masterwork {w.Get<Thing>(item).Label}!", at);
            }
            made.Add(item);
        }
        return made;
    }

    /// <summary>
    /// Use up a recipe's ingredients from these stacks (in order), all or nothing: false, and
    /// nothing taken, if they don't add up. Gives back the main material and rot of what was used.
    /// </summary>
    public static bool Consume(Simulation sim, RecipeDef recipe, List<Entity> from, Entity holder, out MaterialDef? material, out float? rot)
    {
        var w = sim.World;
        material = null;
        rot = null;
        foreach (var x in recipe.Ingredients)
            if (from.Where(e => w.IsAlive(e) && Accepts(x, null, w.Get<Thing>(e).Def, w.Get<Thing>(e).Material)).Sum(e => w.Get<Stack>(e).Count) < x.Count)
                return false;
        var tally = new Workbench();
        tally.Begin(recipe, holder);
        foreach (var e in from)
            if (w.IsAlive(e) && !tally.Stocked)
            {
                var held = w.Get<Carrying>(holder).Item == e;
                Stage(sim, tally, e);
                if (held && !w.IsAlive(e))
                    w.Get<Carrying>(holder).Item = Entity.Null;
            }
        material = tally.MainMaterial;
        rot = tally.RotUnits > 0 ? tally.Rot : null;
        return true;
    }

    /// <summary>
    /// The bill a villager should work at this station next, or null: the craft under way if a
    /// bill still wants it, else the first unsuspended bill that wants more and that the worker
    /// is skilled enough for. A player's craft in progress keeps villagers off the bench.
    /// </summary>
    public static RecipeDef? NextForVillager(Simulation sim, Entity station, Entity worker, WorkType type)
    {
        var w = sim.World;
        var bench = w.Get<Workbench>(station);
        if (!bench.By.IsNull && w.IsAlive(bench.By) && !bench.Untouched)
            return null;
        if (bench.Active is { } active && !bench.Untouched)
        {
            if (active.WorkType != type || active.TwoHanded || !CanMake(sim, worker, active))
                return null;
            var bill = bench.BillFor(active);
            if (bill != null)
                return WantsMore(sim, bill) ? active : null;
            // Something kept in, begun without a bill: whoever keeps it finishes it, short or not.
            return KeepOf(sim, worker, station, active) != null ? active : null;
        }
        // Work for two (a great blade) is a player's, with a striker: no villager makes it alone.
        foreach (var bill in bench.Bills)
            if (bill.Recipe.WorkType == type && !bill.Recipe.TwoHanded && WantsMore(sim, bill) && CanMake(sim, worker, bill.Recipe) && Fitted(sim, station, bill.Recipe))
                return bill.Recipe;
        if (w.TryGet<Trade>(worker, out var trade))
            foreach (var keep in trade.Def.Keeps)
                if (keep.RecipeDef.WorkType == type && !keep.RecipeDef.TwoHanded && KeepOf(sim, worker, station, keep.RecipeDef) != null
                    && Short(sim, station, keep) && CanMake(sim, worker, keep.RecipeDef) && Fitted(sim, station, keep.RecipeDef))
                    return keep.RecipeDef;
        return null;
    }

    // ---------------- Kept in (TradeDef.Keeps) ----------------

    /// <summary>
    /// The keep of <paramref name="recipe"/> that <paramref name="worker"/>'s trade has at this station: theirs to make
    /// without a bill, at their own workplace, while they're the AI's (a player keeps their own). Null if none.
    /// </summary>
    public static TradeKeep? KeepOf(Simulation sim, Entity worker, Entity station, RecipeDef recipe)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(worker, out var trade) || trade.Def.Keeps.Count == 0 || Players.Steered(sim, worker)
            || KeptAt(sim, station) is not { IsNull: false } building || !trade.Def.WorksAt(w.Get<PresetBuilding>(building).Def))
            return null;
        foreach (var keep in trade.Def.Keeps)
            if (keep.RecipeDef == recipe && keep.RecipeDef.StationDef == w.Get<Thing>(station).Def)
                return keep;
        return null;
    }

    /// <summary>How far out of doors a station may stand from a building and still be its yard's (the smithy's kiln).</summary>
    public const int YardReach = 3;

    /// <summary>
    /// The building a station is worked for: the one it's part of, or for one standing on its own out of doors (a kiln,
    /// whose fire would roast a room), the nearest building within <see cref="YardReach"/> of it. Entity.Null if neither.
    /// </summary>
    public static Entity KeptAt(Simulation sim, Entity station)
    {
        var w = sim.World;
        if (w.TryGet<PartOf>(station, out var part))
            return w.Has<PresetBuilding>(part.Building) ? part.Building : Entity.Null;
        if (!w.TryGet<Thing>(station, out var t) || !t.Spawned)
            return Entity.Null;
        var best = Entity.Null;
        var bestD = YardReach + 1;
        for (var dy = -YardReach; dy <= YardReach; dy++)
        for (var dx = -YardReach; dx <= YardReach; dx++)
        {
            var c = t.Cell + new int3(dx, dy, 0);
            var d = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (d == 0 || d >= bestD || !sim.Map.InBounds(c))
                continue;
            var at = Buildings.At(sim, c);
            if (!at.IsNull)
                (best, bestD) = (at, d);
        }
        return best;
    }

    /// <summary>
    /// The keep wants making: fewer of its product than its <see cref="TradeKeep.Below"/> kept at the station's building
    /// (in its chests and bins, or lying in it) or lying free about the town (what's made is set down by the station,
    /// and may be hauled off).
    /// </summary>
    public static bool Short(Simulation sim, Entity station, TradeKeep keep)
    {
        var w = sim.World;
        var building = KeptAt(sim, station);
        var product = keep.RecipeDef.Products[0].ThingDef;
        var have = building.IsNull ? 0 : Shops.Commissions.Stock(sim, building, product);
        if (have >= keep.Below)
            return false;
        foreach (var item in sim.AvailableItems())
            if (w.Get<Thing>(item).Def == product && (building.IsNull || !w.Get<Thing>(item).Spawned || Buildings.At(sim, w.Get<Thing>(item).Cell) != building)
                && (have += w.Get<Stack>(item).Count) >= keep.Below)
                return false;
        return true;
    }

    /// <summary>
    /// What's kept in at this station that wants making now (<see cref="TradeDef.Keeps"/>), by any AI holder of a trade
    /// that works here and is fit to: what lights an on-demand kiln, as a bill would.
    /// </summary>
    public static IEnumerable<RecipeDef> Kept(Simulation sim, Entity station)
    {
        var w = sim.World;
        var def = w.Get<Thing>(station).Def;
        if (!sim.Defs.AllTrades.Any(t => t.Keeps.Any(k => k.RecipeDef.StationDef == def)))
            yield break;
        foreach (var e in w.Query<Trade>())
        {
            if (w.Get<Creature>(e) is not { Dead: false, Downed: false } || w.Has<Components.Adventurer>(e))
                continue;
            foreach (var keep in w.Get<Trade>(e).Def.Keeps)
                if (KeepOf(sim, e, station, keep.RecipeDef) != null && Short(sim, station, keep))
                    yield return keep.RecipeDef;
        }
    }

    /// <summary>The station has what the recipe needs fitted to it (<see cref="RecipeDef.Upgrade"/>: a hearth's cooking pot).</summary>
    public static bool Fitted(Simulation sim, Entity station, RecipeDef recipe) =>
        recipe.Upgrade == null || Buildings.HasUpgrade(sim, station, recipe.Upgrade);

    /// <summary>
    /// Is there enough of every ingredient the craft still lacks lying about (or stored) that the
    /// worker can reach and nobody else has claimed? Checked before starting, so nobody hands in
    /// half a recipe and leaves it stuck on the bench.
    /// </summary>
    public static bool HasIngredients(Simulation sim, Entity worker, Entity station, RecipeDef recipe)
    {
        var w = sim.World;
        var bench = w.Get<Workbench>(station);
        var bill = bench.By.IsNull ? bench.BillFor(recipe) : null;
        var from = w.Get<Position>(worker).Cell;
        for (var i = 0; i < recipe.Ingredients.Count; i++)
        {
            var ingredient = recipe.Ingredients[i];
            var need = ingredient.Count - (bench.Active == recipe ? bench.Staged[i] : 0);
            if (need <= 0)
                continue;
            foreach (var e in sim.AvailableItems(worker))
            {
                var t = w.Get<Thing>(e);
                if (!Accepts(ingredient, bill, t.Def, t.Material) || sim.Reservations.IsReservedByOther(e, worker)
                    || sim.Map.Fire[sim.Map.Idx(t.Cell)] > 0 || !Ai.Navigation.CanReach(sim, worker, from, t.Cell, PathEnd.OnTile))
                    continue;
                if ((need -= w.Get<Stack>(e).Count) <= 0)
                    break;
            }
            if (need > 0)
                return false;
        }
        return true;
    }

    /// <summary>Skilled enough, and the recipe is known (the village's book, or a player's own).</summary>
    public static bool CanMake(Simulation sim, Entity worker, RecipeDef recipe) =>
        SkillRules.LevelOf(sim, worker, recipe.Skill) >= recipe.MinSkill && RecipeBooks.Knows(sim, worker, recipe);

    // ---------------- Quality ----------------

    /// <summary>Things that get a quality when made: gear that doesn't stack (weapons, apparel, packs, tools).</summary>
    public static bool IsGear(ThingDef def) =>
        def.StackLimit <= 1 && (def.Weapon != null || def.Apparel != null || def.Container != null || def.Tool != null || def.Contraption != null);

    /// <summary>
    /// How well it comes out: 0.25 + 0.035 per skill level, ± 0.15 (from sim.Rand), so a novice
    /// mostly makes crude or plain things and a master fine ones, with the odd masterwork.
    /// </summary>
    public static Quality RollQuality(Simulation sim, Entity crafter, RecipeDef recipe)
    {
        return QualityFrom(0.25f + 0.035f * SkillRules.LevelOf(sim, crafter, recipe.Skill) + sim.Rand.Range(-0.15f, 0.15f));
    }

    /// <summary>The grade of a piece from how well it was made, 0..1.</summary>
    public static Quality QualityFrom(float q) => q switch
    {
        < 0.3f => Quality.Crude,
        < 0.62f => Quality.Plain,
        < 0.88f => Quality.Fine,
        _ => Quality.Masterwork,
    };

    /// <summary>A player works a stocked station: by hand where the station wants it, otherwise as an ordinary job.</summary>
    public static void WorkAt(Simulation sim, Entity avatar, Entity station, RecipeDef recipe)
    {
        if (Work.Smithing.Applies(sim, avatar, station, recipe) && recipe.Bloom)
            Work.Smithing.BeginBloom(sim, avatar, station, recipe);
        else if (Work.Smithing.Applies(sim, avatar, station, recipe))
            Work.Smithing.BeginMaking(sim, avatar, station, recipe);
        else if (Tinkering.Tinkers.Applies(sim, avatar, station, recipe))
            Tinkering.Tinkers.WorkAt(sim, avatar, station, recipe);
        else if (Modding.Trades.WorkingAt(sim, avatar, station, recipe) is { } trade)
            trade.WorkAt(sim, avatar, station, recipe);
        else
            AiSystem.Order(sim, avatar, new Ai.CraftJob(station, recipe));
    }

    /// <summary>What quality does to armor and weapon damage.</summary>
    public static float QualityFactor(Quality q) => q switch
    {
        Quality.Crude => 0.8f,
        Quality.Fine => 1.15f,
        Quality.Masterwork => 1.3f,
        _ => 1f,
    };

    public static float QualityFactorOf(Simulation sim, Entity item) =>
        sim.World.TryGet<Crafted>(item, out var c) ? QualityFactor(c.Quality) : 1f;

    public static string QualityLabel(Quality q) => q switch
    {
        Quality.Crude => "crude",
        Quality.Fine => "fine",
        Quality.Masterwork => "masterwork",
        _ => "plain",
    };

    /// <summary>Someone puts on (or takes up) gear another person made: a good thought about the maker.</summary>
    internal static void OnPutOn(Simulation sim, Entity wearer, Entity item)
    {
        var w = sim.World;
        if (!w.TryGet<Crafted>(item, out var c) || c.Maker == wearer || !w.IsAlive(c.Maker) || !w.Has<Mood>(wearer))
            return;
        MoodSystem.Remember(sim, wearer, sim.Defs.Thoughts.UsesTheirWork, c.Maker);
    }

    // ---------------- Repair ----------------

    /// <summary>What of a repair's material the worker has to hand: in their hands, their pack, or a chest in reach.</summary>
    private static List<Entity> RepairStock(Simulation sim, Entity worker, ThingCount material)
    {
        var w = sim.World;
        return Core.CraftCommandRules.Sources(sim, worker)
            .Where(e => w.IsAlive(e) && Accepts(material, null, w.Get<Thing>(e).Def, w.Get<Thing>(e).Material)).ToList();
    }

    /// <summary>How much of a repair's material the worker has to hand (<see cref="PayForRepair"/> pays from the same).</summary>
    public static int ToHandForRepair(Simulation sim, Entity worker, ThingCount material) =>
        RepairStock(sim, worker, material).Sum(e => sim.World.Get<Stack>(e).Count);

    /// <summary>
    /// Why a mend of <paramref name="item"/> at <paramref name="station"/> can't start: the material
    /// isn't to hand. Said before any work's done, so a smith isn't left heating and hammering a piece
    /// for nothing: what it takes, what they have, and where it's made if their trade makes it. Null if
    /// there's enough (or nothing to mend).
    /// </summary>
    public static string? ShortForRepair(Simulation sim, Entity worker, Entity station, Entity item)
    {
        var w = sim.World;
        if (!w.IsAlive(station) || RepairCost(sim, w.Get<Thing>(station).Def, item) is not { } cost)
            return null;
        var have = ToHandForRepair(sim, worker, cost.Material);
        if (have >= cost.Count)
            return null;
        var label = cost.Material.ThingDef.Label;
        var text = $"Mending the {sim.LabelOf(item)} takes {cost.Count} {label}, and you have {(have == 0 ? "none" : have.ToString())} to hand (your hands, your pack, or a chest in reach).";
        if (MadeAt(sim, worker, cost.Material.ThingDef) is { } made)
            text += $" The {made.Station.Label.ToLowerInvariant()} makes {made.Recipe.Label}.";
        return text;
    }

    /// <summary>A station and its recipe that make this (the forge's iron bars): one the worker's trade knows first, else any; null if none.</summary>
    public static (ThingDef Station, RecipeDef Recipe)? MadeAt(Simulation sim, Entity worker, ThingDef thing)
    {
        static RecipeDef? Makes(ThingDef station, ThingDef thing) => station.Recipes.Find(r => r.Products.Exists(p => p.ThingDef == thing));
        if (sim.World.TryGet<Trade>(worker, out var trade))
            foreach (var known in trade.Def.KnownStations)
                if (Makes(known, thing) is { } recipe)
                    return (known, recipe);
        foreach (var station in sim.Defs.AllThings)
            if (station.Building?.Workbench != null && Makes(station, thing) is { } recipe)
                return (station, recipe);
        return null;
    }

    /// <summary>Use up a repair's material from what the worker has to hand. False (nothing used) if they're short.</summary>
    public static bool PayForRepair(Simulation sim, Entity worker, ThingCount material, int count)
    {
        var w = sim.World;
        var payWith = RepairStock(sim, worker, material);
        if (payWith.Sum(e => w.Get<Stack>(e).Count) < count)
            return false;
        foreach (var e in payWith)
        {
            var take = Math.Min(count, w.Get<Stack>(e).Count);
            count -= take;
            if ((w.Get<Stack>(e).Count -= take) <= 0)
            {
                if (w.Get<Carrying>(worker).Item == e)
                    w.Get<Carrying>(worker).Item = Entity.Null;
                sim.DestroyThing(e);
            }
            if (count <= 0)
                break;
        }
        return true;
    }

    /// <summary>The recipe at a station that makes this kind of item (what mending it there is based on), or null.</summary>
    public static RecipeDef? RepairRecipe(ThingDef station, ThingDef item) =>
        station.Recipes.Find(r => r.Products.Exists(p => p.ThingDef == item));

    /// <summary>
    /// What mending an item costs: half its recipe's first ingredient (rounded up, at least one)
    /// scaled by how worn it is, and half the recipe's work likewise. Null if it isn't worn or
    /// isn't made at this station.
    /// </summary>
    public static (ThingCount Material, int Count, float Work)? RepairCost(Simulation sim, ThingDef station, Entity item)
    {
        var t = sim.World.Get<Thing>(item);
        var worn = 1f - t.Hp / t.MaxHp;
        if (worn < 0.05f || RepairRecipe(station, t.Def) is not { } recipe)
            return null;
        var material = recipe.Ingredients[0];
        return (material, Math.Max(1, (int)MathF.Ceiling(material.Count * worn * 0.5f)), recipe.Work * worn * 0.5f);
    }

    // ---------------- Tools ----------------

    /// <summary>How far from a station (cells) a tool lying about, or kept in a chest, still counts as to hand.</summary>
    public const int ToolReach = 2;

    /// <summary>
    /// The best speed of a tool of this kind the worker has to hand (in hand, in the pack or
    /// wielded), or at the station if one is given; 0 if there's none.
    /// </summary>
    public static float ToolSpeed(Simulation sim, Entity worker, string kind, Entity? station = null) =>
        BestTool(sim, worker, kind, station).Speed;

    /// <summary>The highest tier of a tool of this kind to hand (as for <see cref="ToolSpeed"/>); 0 if there's none.</summary>
    public static int ToolTier(Simulation sim, Entity worker, string kind, Entity? station = null) =>
        BestTool(sim, worker, kind, station).Tier;

    /// <summary>
    /// Gathering pace with what's to hand for <paramref name="kind"/> of work (an "Axe" for felling):
    /// the tool's speed, or <see cref="BareHands"/> with none. Only people need tools; a dragon's
    /// claws (and any animal) work at 1.
    /// </summary>
    public static float GatherFactor(Simulation sim, Entity worker, string kind)
    {
        if (sim.World.Get<Creature>(worker).Def.Race != Defs.Race.Human)
            return 1f;
        var speed = ToolSpeed(sim, worker, kind);
        return speed > 0 ? speed : BareHands;
    }

    /// <summary>How fast people chop, mine, dig rock and fish with no tool for it.</summary>
    public const float BareHands = 0.35f;

    /// <summary>
    /// The tool of <paramref name="kind"/> a worker has to hand (in their hands, as their weapon, on
    /// their belt, in their pack), the fastest first; Entity.Null with none. For the renderer, which
    /// draws it in their hand while they work; the sim's own choice is <see cref="ToolSpeed"/>.
    /// </summary>
    public static Entity ToolInHand(Simulation sim, Entity worker, string kind)
    {
        var w = sim.World;
        var best = Entity.Null;
        var speed = 0f;
        void Consider(Entity item)
        {
            if (item.IsNull || !w.IsAlive(item) || w.Get<Thing>(item).Def.Tool is not { } t || t.Kind != kind || t.Speed <= speed)
                return;
            best = item;
            speed = t.Speed;
        }
        if (w.TryGet<Carrying>(worker, out var hands))
            Consider(hands.Item);
        if (w.TryGet<Equipment>(worker, out var eq))
            Consider(eq.Weapon);
        foreach (var e in Toolbelt.Of(sim, worker))
            Consider(e);
        if (sim.PackOf(worker) is { IsNull: false } pack)
            foreach (var e in sim.StoredIn(pack))
                Consider(e);
        return best;
    }

    private static (float Speed, int Tier) BestTool(Simulation sim, Entity worker, string kind, Entity? station)
    {
        var w = sim.World;
        var best = 0f;
        var tier = 0;
        void Consider(Entity item)
        {
            if (item.IsNull || !w.IsAlive(item) || w.Get<Thing>(item).Def.Tool is not { } t || t.Kind != kind)
                return;
            best = Math.Max(best, t.Speed);
            tier = Math.Max(tier, t.Tier);
        }
        if (w.TryGet<Carrying>(worker, out var hands))
            Consider(hands.Item);
        if (w.TryGet<Equipment>(worker, out var eq))
            Consider(eq.Weapon);
        foreach (var e in Toolbelt.Of(sim, worker))
            Consider(e);
        if (sim.PackOf(worker) is { IsNull: false } pack)
            foreach (var e in sim.StoredIn(pack))
                Consider(e);
        if (station is { IsNull: false } bench && w.IsAlive(bench))
        {
            var at = w.Get<Thing>(bench).Cell;
            foreach (var e in sim.AvailableItems(worker))
                if (w.Get<Thing>(e).Def.Tool?.Kind == kind && Maps.Map.Distance(w.Get<Thing>(e).Cell, at) <= ToolReach
                    && Math.Abs(w.Get<Thing>(e).Cell.Z - at.Z) <= 1)
                    Consider(e);
        }
        return (best, tier);
    }

    /// <summary>A missing tool for messages: "an iron hammer" when the recipe wants better than stone, else "a hammer".</summary>
    public static string ToolNeed(Simulation sim, Defs.RecipeDef recipe, string kind) =>
        sim.Defs.ToolOfTier(recipe.ToolTier >= 2 ? recipe.ToolTier : 0, kind);

    /// <summary>The first tool kind a recipe needs that isn't to hand (of its <see cref="Defs.RecipeDef.ToolTier"/>), or null if it has them all.</summary>
    public static string? MissingTool(Simulation sim, Entity worker, Entity station, RecipeDef recipe)
    {
        foreach (var kind in recipe.Tools)
            if (ToolTier(sim, worker, kind, station) < Math.Max(1, recipe.ToolTier))
                return kind;
        return null;
    }

    /// <summary>How much faster the tools to hand make a recipe (the best of each kind it names, multiplied).</summary>
    public static float ToolFactor(Simulation sim, Entity worker, Entity station, RecipeDef recipe)
    {
        var f = 1f;
        foreach (var kind in recipe.Tools.Concat(recipe.HelpedBy))
            f *= Math.Max(1f, ToolSpeed(sim, worker, kind, station));
        return f;
    }

    /// <summary>An on-demand fire (a kiln) is wanted: something's being made, or a bill wants making.</summary>
    public static bool WantsFiring(Simulation sim, Entity station)
    {
        if (!sim.World.TryGet<Workbench>(station, out var bench) || sim.World.Has<Construction>(station))
            return false;
        if (bench.Active != null)
            return true;
        if (sim.World.TryGet<FiredForMending>(station, out var mend) && mend.Until > sim.Clock.Tick)
            return true;
        foreach (var bill in bench.Bills)
            if (WantsMore(sim, bill))
                return true;
        return Kept(sim, station).Any();
    }

    /// <summary>The heat the next thing to make here needs (0 if none): what a dragon stokes it for.</summary>
    public static float HeatWanted(Simulation sim, Entity station)
    {
        var bench = sim.World.Get<Workbench>(station);
        if (bench.Active is { } active)
            return active.MinTemp;
        foreach (var bill in bench.Bills)
            if (WantsMore(sim, bill))
                return bill.Recipe.MinTemp;
        foreach (var recipe in Kept(sim, station))
            return recipe.MinTemp;
        return 0;
    }

    /// <summary>
    /// The heat the station needs for what can actually be made next: the craft under way, else the
    /// first bill wanting more whose ingredients <paramref name="worker"/> could fetch. What fuel is
    /// chosen by: a bronze bill with no tin about doesn't send anyone for charcoal.
    /// </summary>
    public static float HeatToMake(Simulation sim, Entity station, Entity worker)
    {
        var bench = sim.World.Get<Workbench>(station);
        if (bench.Active is { } active)
            return active.MinTemp;
        foreach (var bill in bench.Bills)
            if (WantsMore(sim, bill) && HasIngredients(sim, worker, station, bill.Recipe))
                return bill.Recipe.MinTemp;
        foreach (var recipe in Kept(sim, station))
            if (HasIngredients(sim, worker, station, recipe))
                return recipe.MinTemp;
        return 0;
    }

    /// <summary>Give a newly built (or loaded) station its bench state.</summary>
    internal static void EnsureBench(Simulation sim, Entity e)
    {
        if (sim.World.Get<Thing>(e).Def.Building?.Workbench != null && !sim.World.Has<Workbench>(e))
            sim.World.Add(e, new Workbench());
    }

    public static int MaxBills(ThingDef station) => station.Building?.Workbench?.Bills ?? DefaultMaxBills;
}
