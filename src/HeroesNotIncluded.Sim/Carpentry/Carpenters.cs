using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Carpentry;

/// <summary>
/// The woodshop (docs/DESIGN.md, "Carpenter"): what's done there by hand, and what an AI carpenter
/// does without the games. A player in a round saws planks at the sawhorse (<see cref="SawSession"/>),
/// makes quivers (<see cref="FletchSession"/>) and tillers bows (<see cref="BowSession"/>) at the bench once the station has what it needs;
/// an AI carpenter keeps the woodshop's chest in planks and a couple of quivers on show
/// (<see cref="SawJob"/>, <see cref="FletchJob"/>), its work as good as its skill.
/// </summary>
public static class Carpenters
{
    public const string SawhorseDef = "Sawhorse";
    public const string BenchDef = "CarpenterBench";
    public const string PlanksDef = "Planks";
    public const string FeathersDef = "Feathers";
    public const string WoodDef = "Wood";

    /// <summary>The bench's plain cudgel: what a carpenter hangs on the muster rack when there aren't the planks for a bow.</summary>
    public const string CudgelRecipe = "MakeCudgel";

    /// <summary>Logs a batch of planks takes, and the planks it gives at best (as the <c>SawPlanks</c> recipe).</summary>
    public const int LogsPerBatch = 6, PlanksPerBatch = 10;

    /// <summary>Quivers an AI carpenter keeps on show at once, and the price it asks for each.</summary>
    public const int QuiversOnShow = 2, QuiverPrice = 4;

    /// <summary>Planks in the woodshop below which an AI carpenter saws more.</summary>
    public const int PlanksLow = 3;

    private static bool Is(Simulation sim, Entity e, string def) =>
        sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Name == def && !sim.World.Has<Construction>(e);

    public static bool IsSawhorse(Simulation sim, Entity e) => Is(sim, e, SawhorseDef);

    public static bool IsBench(Simulation sim, Entity e) => Is(sim, e, BenchDef);

    /// <summary>A recipe that saws planks.</summary>
    public static bool Saws(RecipeDef r) => r.StationDef?.Name == SawhorseDef && r.Products.Exists(p => p.ThingDef.Name == PlanksDef);

    /// <summary>A recipe that fletches a quiver.</summary>
    public static bool Fletches(RecipeDef r) => r.Products.Exists(p => p.ThingDef.Quiver);

    /// <summary>A recipe that makes a bow (tillered by hand, <see cref="BowSession"/>).</summary>
    public static bool MakesBow(RecipeDef r) => r.Products.Exists(p => p.ThingDef.Weapon is { Ranged: true } && p.ThingDef.Name == "Bow");

    /// <summary>Should a player's work on this recipe at this station be done by hand? In a round, sawing planks at a sawhorse, or fletching or tillering a bow at the carpenter's bench.</summary>
    public static bool Applies(Simulation sim, Entity worker, Entity station, RecipeDef recipe) =>
        sim.Round.Live && sim.World.Has<Avatar>(worker)
        && (IsSawhorse(sim, station) && Saws(recipe) || IsBench(sim, station) && (Fletches(recipe) || MakesBow(recipe)));

    /// <summary>A player works a stocked station: the hand game for the recipe (<see cref="Applies"/>).</summary>
    public static void WorkAt(Simulation sim, Entity worker, Entity station, RecipeDef recipe)
    {
        if (sim.World.Has<WorkSession>(worker))
            return;
        if (Saws(recipe))
            WorkSystem.Start(sim, worker, new SawSession { Station = station, Great = recipe.TwoHanded });
        else if (Fletches(recipe))
            WorkSystem.Start(sim, worker, new FletchSession { Station = station });
        else if (MakesBow(recipe))
            WorkSystem.Start(sim, worker, new BowSession { Station = station });
    }

    /// <summary>A bow, mended by hand at the carpenter's bench (re-tillered and restrung, <see cref="BowSession.Mending"/>).</summary>
    public static bool IsBow(ThingDef def) => def.Weapon is { Ranged: true } && def.Name == "Bow";

    /// <summary>
    /// Should a player's mend of this piece at this station be a hand game? Anything mended at the
    /// carpenter's bench is: a bow re-tillered, a club or staff re-pegged (<see cref="JoinWork.Haft"/>).
    /// </summary>
    public static bool AppliesToRepair(Simulation sim, Entity worker, Entity station, Entity item) =>
        sim.Round.Live && sim.World.Has<Avatar>(worker) && IsBench(sim, station);

    /// <summary>A player takes a worn piece to the bench: a bow re-tillered where it's set stiff and strung again, a haft re-pegged.</summary>
    public static void BeginMending(Simulation sim, Entity worker, Entity station, Entity item)
    {
        if (sim.World.Has<WorkSession>(worker))
            return;
        if (IsBow(sim.World.Get<Thing>(item).Def))
            WorkSystem.Start(sim, worker, new BowSession { Station = station, Mending = item });
        else
            WorkSystem.Start(sim, worker, new JoinSession { Station = station, Work = JoinWork.Haft, Joints = 1, Mending = item });
    }

    /// <summary>How well an AI carpenter's work comes out, by skill, before its roll.</summary>
    public static float ExpectedQuality(Simulation sim, Entity who) =>
        Math.Clamp(0.3f + 0.05f * SkillRules.LevelOf(sim, who, Skill.Crafting), 0.3f, 0.85f);

    /// <summary>The chance an AI carpenter's quiver has a warped shaft in it: less with skill.</summary>
    public static float WarpChance(Simulation sim, Entity who) =>
        Math.Clamp(0.2f - 0.02f * SkillRules.LevelOf(sim, who, Skill.Crafting), 0.02f, 0.2f);

    /// <summary>Products made the ordinary way (a bill worked, not played): a quiver is fletched as the maker's skill allows.</summary>
    public static void Made(Simulation sim, List<Entity> made, Entity maker)
    {
        var w = sim.World;
        foreach (var item in made)
            if (w.Get<Thing>(item).Def.Quiver && !w.Has<Fletched>(item))
                Fletching.Mark(sim, item, maker, Math.Clamp(ExpectedQuality(sim, maker) + sim.Rand.Range(-0.1f, 0.1f), 0.05f, 1f),
                    sim.Rand.Chance(WarpChance(sim, maker)));
    }

    // ---------------- The woodshop's stock ----------------

    /// <summary>The chest at a woodshop, Entity.Null if none.</summary>
    public static Entity ChestOf(Simulation sim, Entity woodshop)
    {
        if (woodshop.IsNull || !sim.World.IsAlive(woodshop))
            return Entity.Null;
        return Buildings.Parts(sim, woodshop).Where(e => Is(sim, e, "Chest")).OrderBy(e => e.Index).FirstOrDefault(Entity.Null);
    }

    /// <summary>The station of <paramref name="def"/> at a woodshop, Entity.Null if none.</summary>
    public static Entity StationOf(Simulation sim, Entity woodshop, string def)
    {
        if (woodshop.IsNull || !sim.World.IsAlive(woodshop))
            return Entity.Null;
        return Buildings.Parts(sim, woodshop).Where(e => Is(sim, e, def)).OrderBy(e => e.Index).FirstOrDefault(Entity.Null);
    }

    /// <summary>How many of <paramref name="def"/> are in a chest.</summary>
    public static int CountIn(Simulation sim, Entity chest, string def) =>
        chest.IsNull ? 0 : sim.StoredIn(chest).Where(e => sim.World.Get<Thing>(e).Def.Name == def).Sum(e => sim.World.Get<Stack>(e).Count);

    /// <summary>Use up <paramref name="count"/> of <paramref name="def"/> from a chest. False (nothing used) if it's short.</summary>
    public static bool TakeFrom(Simulation sim, Entity chest, string def, int count)
    {
        if (CountIn(sim, chest, def) < count)
            return false;
        foreach (var e in sim.StoredIn(chest).Where(e => sim.World.Get<Thing>(e).Def.Name == def).OrderBy(e => e.Index).ToList())
        {
            if (count <= 0)
                break;
            var n = Math.Min(count, sim.World.Get<Stack>(e).Count);
            sim.DestroyThing(sim.TakeFromStack(e, n));
            count -= n;
        }
        return true;
    }

    /// <summary>The woodshop an AI carpenter keeps, or Entity.Null (not a carpenter, a player, outside a round).</summary>
    public static Entity WoodshopOf(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!sim.Round.Live || w.Has<Avatar>(pawn) || !w.TryGet<Trade>(pawn, out var trade) || !trade.Def.HasWorkplace
            || trade.Def.Role != TownRole.Woodshop)
            return Entity.Null;
        return Shops.ShopSystem.WorkplaceOf(sim, trade.Def);
    }

    /// <summary>Quivers on show at a woodshop now.</summary>
    public static int OnShow(Simulation sim, Entity woodshop) =>
        Shops.Goods.OnShow(sim).Count(g => g.Shop == woodshop && sim.World.Get<Thing>(g.Item).Def.Quiver);

    /// <summary>Planks short at the woodshop and logs in its chest to saw: an AI carpenter saws a batch.</summary>
    public static Job? Saw(Simulation sim, Entity pawn)
    {
        var shop = WoodshopOf(sim, pawn);
        var chest = ChestOf(sim, shop);
        var horse = StationOf(sim, shop, SawhorseDef);
        if (chest.IsNull || horse.IsNull || CountIn(sim, chest, PlanksDef) >= PlanksLow || CountIn(sim, chest, WoodDef) < LogsPerBatch
            || sim.Reservations.IsReservedByOther(horse, pawn)
            || !Navigation.CanReach(sim, pawn, sim.World.Get<Position>(pawn).Cell, sim.World.Get<Thing>(horse).Cell, PathEnd.Touch))
            return null;
        return new SawJob(horse, chest);
    }

    /// <summary>
    /// An archer asking for arrows at the woodshop (<see cref="ServiceKind.Arrows"/>), known to this AI
    /// carpenter and nobody else's: it fletches a quiver at the bench (or takes one it has on it) and
    /// hands it over. Before the shelf's stock: an archer asking is a sale for certain.
    /// </summary>
    public static Job? Arrows(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var shop = WoodshopOf(sim, pawn);
        var chest = ChestOf(sim, shop);
        var bench = StationOf(sim, shop, BenchDef);
        if (bench.IsNull || chest.IsNull)
            return null;
        var role = w.Get<Trade>(pawn).Def.Role;
        var stocked = CountIn(sim, chest, PlanksDef) >= 1 && CountIn(sim, chest, FeathersDef) >= 1;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var e in sim.Shops.FrontFirst(sim))
        {
            if (!e.IsOpen || e.Kind.Kind != ServiceKind.Arrows || e.Kind.AtRole != role || !e.KnownBy.Contains(pawn)
                || (!e.Assignee.IsNull && e.Assignee != pawn) || !w.IsAlive(e.Poster))
                continue;
            if ((stocked || Fletching.Carried(sim, pawn).Count > 0) && !sim.Reservations.IsReservedByOther(bench, pawn)
                && Navigation.CanReach(sim, pawn, from, w.Get<Position>(e.Poster).Cell, PathEnd.Touch))
                return new ArrowsJob(e.Id, bench, chest);
        }
        return null;
    }

    /// <summary>
    /// A waking hour with fewer than <see cref="QuiversOnShow"/> quivers out and a plank and feathers
    /// in the chest: an AI carpenter fletches one and puts it on show.
    /// </summary>
    public static Job? Fletch(Simulation sim, Entity pawn)
    {
        if (Shops.ShopSystem.SmallHours(sim))
            return null;
        var shop = WoodshopOf(sim, pawn);
        var chest = ChestOf(sim, shop);
        var bench = StationOf(sim, shop, BenchDef);
        if (chest.IsNull || bench.IsNull || OnShow(sim, shop) >= QuiversOnShow
            || CountIn(sim, chest, PlanksDef) < 1 || CountIn(sim, chest, FeathersDef) < 1 || sim.Reservations.IsReservedByOther(bench, pawn)
            || !Navigation.CanReach(sim, pawn, sim.World.Get<Position>(pawn).Cell, sim.World.Get<Thing>(bench).Cell, PathEnd.Touch))
            return null;
        return new FletchJob(bench, chest);
    }
}

/// <summary>An AI carpenter saws a batch of logs from the woodshop's chest into planks, as many as its skill gets from them.</summary>
public sealed class SawJob(Entity horse, Entity chest) : Job
{
    public const int Ticks = 900;

    public override Entity Focus => horse;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Sawing planks";
        if (!Reserve(horse)) yield break;
        foreach (var s in GoTo(Get<Thing>(horse).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        foreach (var s in Wait(Ticks)) yield return s;
        if (!Alive(horse) || !Alive(chest) || !Carpenters.TakeFrom(Sim, chest, Carpenters.WoodDef, Carpenters.LogsPerBatch))
            yield break;
        SkillRules.Learn(Sim, Pawn, Skill.Crafting, SkillRules.XpPerWorkTick * Ticks);
        var quality = Math.Clamp(Carpenters.ExpectedQuality(Sim, Pawn) + Sim.Rand.Range(-0.1f, 0.1f), 0f, 1f);
        var planks = Sim.CreateItem(Sim.Defs.Thing(Carpenters.PlanksDef), SawSession.Yield(Carpenters.PlanksPerBatch, quality));
        var rest = Sim.Store(chest, planks);
        if (!rest.IsNull)
            Sim.PlaceItemNear(rest, Get<Thing>(chest).Cell);
    }
}

/// <summary>An AI carpenter sees an archer's ask for arrows through: a quiver fletched at the bench (or one it has on it), carried over and handed to them.</summary>
public sealed class ArrowsJob(int order, Entity bench, Entity chest) : Job
{
    public override Entity Focus => bench;

    protected override IEnumerable<JobStatus> Run()
    {
        var orders = Sim.Shops;
        if (orders.Find(order) is not { IsOpen: true } e || (e.Assignee != Pawn && !orders.Accept(Sim, Pawn, order)))
        {
            Fail("someone else has it");
            yield break;
        }
        var quiver = Fletching.Carried(Sim, Pawn).FirstOrDefault(Entity.Null);
        if (quiver.IsNull)
        {
            Report = $"Fletching arrows for {e.PosterName}";
            if (!Reserve(bench)) yield break;
            foreach (var s in GoTo(Get<Thing>(bench).Cell, PathEnd.Touch)) yield return s;
            if (HasFailed) yield break;
            foreach (var s in Wait(FletchJob.Ticks)) yield return s;
            if (!Alive(bench) || !Alive(chest) || !e.IsOpen || Carpenters.CountIn(Sim, chest, Carpenters.PlanksDef) < 1
                || !Carpenters.TakeFrom(Sim, chest, Carpenters.FeathersDef, 1))
            {
                Fail("no plank or feathers");
                yield break;
            }
            Carpenters.TakeFrom(Sim, chest, Carpenters.PlanksDef, 1);
            SkillRules.Learn(Sim, Pawn, Skill.Crafting, SkillRules.XpPerWorkTick * FletchJob.Ticks);
            quiver = Sim.CreateItem(Sim.Defs.Thing(Fletching.Def), 1);
            Carpenters.Made(Sim, [quiver], Pawn);
            Inventory.Stow(Sim, Pawn, quiver);
        }
        Report = $"Bringing {e.PosterName} a quiver";
        foreach (var s in GoToEntity(e.Poster, PathEnd.Touch, 1500)) yield return s;
        if (HasFailed) yield break;
        if (!e.IsOpen || !Alive(quiver) || !Shops.Handing.InReach(Sim, Pawn, e.Poster) || !Inventory.OnPerson(Sim, Pawn, quiver))
        {
            Fail("couldn't hand it over");
            yield break;
        }
        orders.Received(Sim, Pawn, e.Poster, Inventory.TakeFromPerson(Sim, Pawn, quiver, 1));
    }
}

/// <summary>An AI carpenter fletches a quiver at the bench from the woodshop's chest and puts it on show there.</summary>
public sealed class FletchJob(Entity bench, Entity chest) : Job
{
    public const int Ticks = 900;

    public override Entity Focus => bench;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Fletching arrows";
        if (!Reserve(bench)) yield break;
        foreach (var s in GoTo(Get<Thing>(bench).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        foreach (var s in Wait(Ticks)) yield return s;
        if (!Alive(bench) || !Alive(chest) || Carpenters.CountIn(Sim, chest, Carpenters.PlanksDef) < 1
            || !Carpenters.TakeFrom(Sim, chest, Carpenters.FeathersDef, 1))
            yield break;
        Carpenters.TakeFrom(Sim, chest, Carpenters.PlanksDef, 1);
        SkillRules.Learn(Sim, Pawn, Skill.Crafting, SkillRules.XpPerWorkTick * Ticks);
        var quiver = Sim.CreateItem(Sim.Defs.Thing(Fletching.Def), 1);
        Carpenters.Made(Sim, [quiver], Pawn);
        // Onto the woodshop's wares shelf where the archers will see it, at the price the carpenter asks
        // (set down by the bench if the shelf's full or gone).
        var shelf = Shops.Goods.MyDisplay(Sim, Pawn);
        if (shelf.IsNull || Sim.ContainerRoom(shelf, Get<Thing>(quiver).Def, null) <= 0)
        {
            Sim.PlaceItemNear(quiver, PawnCell);
            yield break;
        }
        Get<Carrying>(Pawn).Item = Inventory.PickUp(Sim, Pawn, quiver, Get<Carrying>(Pawn).Item);
        foreach (var s in GoTo(Get<Thing>(shelf).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed || !Alive(shelf) || !Alive(quiver))
            yield break;
        if (Get<Carrying>(Pawn).Item == quiver)
        {
            Get<Carrying>(Pawn).Item = Entity.Null;
            var left = Sim.Store(shelf, quiver);
            if (!left.IsNull)
                Sim.PlaceItemNear(left, PawnCell);
        }
        else if (Inventory.InOwnPack(Sim, Pawn, quiver))
            Inventory.StoreFromPack(Sim, Pawn, shelf, quiver);
        if (Alive(quiver) && Sim.World.Has<Stored>(quiver) && Sim.World.Get<Stored>(quiver).In == shelf)
            Shops.Goods.Set(Sim, Pawn, quiver, Carpenters.QuiverPrice, true);
    }
}
