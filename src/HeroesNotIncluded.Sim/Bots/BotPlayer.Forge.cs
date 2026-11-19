using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Expeditions;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Bots;

public sealed partial class BotPlayer
{
    private bool Repair(Simulation sim, Entity me, PerceptionView view, Entity owner, long now)
    {
        var w = sim.World;
        // A piece of its own already in the tongs is seen through first: left, it cools, and the smithy with it.
        if (view.Piece(me) is { Mend: false })
            return WorkPiece(sim, me, view, now);
        var blade = Entrusted(sim, me, owner);
        if (blade.IsNull)
        {
            Reach(sim, me, owner, now); // they hand it over once we're close
            return true;
        }
        var condition = view.Condition(blade) ?? "";
        Doing = $"repair: {condition}";
        // In the tongs: to the fire while it's dark, to the anvil while it glows. A forge gone out is fed first.
        if (view.Piece(me) is { } piece)
        {
            if (!piece.Hot && Nearest(sim, me, "Forge") is { IsNull: false } forge && view.FuelIn(forge) is <= 0f)
                return FeedForge(sim, me, view, forge, now);
            Doing = $"forging: {piece.Label} {(piece.Hot ? "to the anvil" : "to the fire")}";
            return AtStation(sim, me, Nearest(sim, me, piece.Hot ? "Anvil" : "Forge"), now, s => Send(new SmithCommand(me, s)));
        }
        if (condition.Contains("chip") || condition.Contains("bent") || condition.Contains("breaking"))
        {
            var forge = Nearest(sim, me, "Forge");
            if (!forge.IsNull && TooHotToForge(sim, me, forge, WarmsPerWork * MendWork))
            {
                Doing = "letting the smithy cool before the mend";
                return true; // they'll wait: a mend begun hot can't be seen through
            }
            return Stoke(sim, me, view, now) || AtStation(sim, me, forge, now, s => Send(new RepairCommand(me, s, blade)));
        }
        if (condition.Contains("dull") || condition.Contains("blunt"))
        {
            // The grindstone if the smithy has one, else the whetstone in the kit.
            var wheel = Nearest(sim, me, "Grindstone");
            if (wheel.IsNull)
                Send(new SharpenCommand(me, blade));
            else
                AtStation(sim, me, wheel, now, s => Send(new SmithCommand(me, s)));
            return true;
        }
        if (Reach(sim, me, owner, now, returning: true))
            Send(new GiveCommand(me, owner, blade, 1));
        return true;
    }

    /// <summary>Feed a forge that's gone out: charcoal from the smithy's fuel bin, then into the fire.</summary>
    private bool FeedForge(Simulation sim, Entity me, PerceptionView view, Entity forge, long now)
    {
        var w = sim.World;
        Doing = "feeding the forge";
        bool IsFuel(Entity e) => w.IsAlive(e) && w.Get<Thing>(e).Def.Name is "Charcoal" or "Coal";
        var pack = sim.PackOf(me);
        if (IsFuel(w.Get<Carrying>(me).Item) || (!pack.IsNull && sim.StoredIn(pack).Any(IsFuel)))
            return AtStation(sim, me, forge, now, f =>
            {
                // In hand: a forge still alight is fed only from the hands, not the pack.
                if (!IsFuel(w.Get<Carrying>(me).Item))
                    Send(new HoldCommand(me, sim.StoredIn(pack).First(IsFuel)));
                Send(new AvatarWorkCommand(me, true, w.Get<Thing>(f).Cell));
                Send(new AvatarWorkCommand(me, false));
            });
        return AtStation(sim, me, Nearest(sim, me, "FuelBin"), now, bin =>
        {
            if (view.InContainer(bin).FirstOrDefault(IsFuel, Entity.Null) is { IsNull: false } coal)
                Send(new TakeFromStorageCommand(me, coal, 10)); // a few shovelfuls
        });
    }

    /// <summary>Charcoal in the forge below which it feeds it before taking up a piece.</summary>
    public const float StokeBelow = 8f;

    /// <summary>
    /// The forge fed before a piece goes in the tongs: once one's there, F at a cold forge takes up
    /// the work rather than feeding the fire, so a forge let go out mid-piece can't be lit again.
    /// True while at it.
    /// </summary>
    private bool Stoke(Simulation sim, Entity me, PerceptionView view, long now) =>
        Nearest(sim, me, "Forge") is { IsNull: false } forge && view.Piece(me) == null && view.FuelIn(forge) is { } fuel && fuel < StokeBelow
        && FeedForge(sim, me, view, forge, now);

    /// <summary>About how far the smithy warms, in degrees, for each unit of a recipe's work done at the forge: a long piece heats it more.</summary>
    public const float WarmsPerWork = 0.028f;

    /// <summary>About the work in a mend at the forge, for how far it warms the smithy.</summary>
    public const float MendWork = 1000f;

    /// <summary>How far a bloom warms it: white-hot, and heat after heat at the forge's mouth. Smelted only in a cool smithy.</summary>
    public const float BloomWarms = 38f;

    /// <summary>
    /// Too hot at the forge's mouth to start on a piece that can wait: working it would warm the
    /// smithy past what drives it back from the heat, mid-piece, and a forge let go out with a piece
    /// in the tongs can't be fed (<see cref="Stoke"/>). By what it feels standing there, and how long
    /// the piece takes, as a smith would judge it.
    /// </summary>
    private static bool TooHotToForge(Simulation sim, Entity me, Entity forge, RecipeDef recipe) =>
        TooHotToForge(sim, me, forge, recipe.Bloom ? BloomWarms : WarmsPerWork * recipe.Work);

    /// <summary>As <see cref="TooHotToForge(Simulation, Entity, Entity, RecipeDef)"/>, for work that warms the smithy by <paramref name="warms"/> degrees.</summary>
    private static bool TooHotToForge(Simulation sim, Entity me, Entity forge, float warms)
    {
        var w = sim.World;
        var at = w.Get<Thing>(forge).Cell;
        var limit = w.Get<Creature>(me).Tolerance(sim.Clock.Tick).SafeMax - 8 - warms;
        var coolest = float.MaxValue;
        foreach (var c in sim.SpiralFrom(at, 1))
            if (c != at && sim.Map.Passable(c) && Ai.Pathfinder.Touches(c, at))
                coolest = Math.Min(coolest, Environment.Warmth.FeltAt(sim, c));
        return coolest > limit && coolest < float.MaxValue;
    }

    /// <summary>Bars it likes to have to hand before it'll smelt more.</summary>
    public const int SmeltBelow = 2;

    private long _noOreUntil;

    /// <summary>
    /// The smith between customers: a piece in the tongs is seen through (to the fire while it's
    /// dark, the anvil while it glows, the tub once it's shaped); in a call to arms, spears for the
    /// muster rack, and each one hung there; short of bars, ore from the smithy chest and charcoal
    /// from the bin go in the forge as a bloom. True while at it.
    /// </summary>
    private bool Smith(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(me, out var trade) || trade.Def.Name != "Blacksmith")
            return false;
        if (view.Piece(me) != null)
            return WorkPiece(sim, me, view, now);
        if (sim.Militia.Paying && ArmTheTown(sim, me, view, now))
            return true;
        if (ShowGoods(sim, me, view, now) || ForgeForSale(sim, me, view, now))
            return true;
        if (now < _noOreUntil || sim.Defs.AllRecipes.FirstOrDefault(r => r.Bloom) is not { } recipe || OnMe(sim, me, "IronBar") >= SmeltBelow)
            return false;
        var forge = Nearest(sim, me, "Forge");
        if (forge.IsNull || TooHotToForge(sim, me, forge, recipe))
            return false;
        var ore = recipe.Ingredients[0];
        var fuel = recipe.Ingredients.Count > 1 ? recipe.Ingredients[1] : null;
        if (!w.Get<Carrying>(me).Item.IsNull && w.Get<Thing>(w.Get<Carrying>(me).Item).Def != ore.ThingDef)
        {
            Send(new StowCommand(me));
            return true;
        }
        if (OnMe(sim, me, ore.Def) < ore.Count)
            return Fetch(sim, me, view, now, ore, "ore");
        if (fuel != null && OnMe(sim, me, fuel.Def) < fuel.Count)
            return Fetch(sim, me, view, now, fuel, "charcoal");
        if (Stoke(sim, me, view, now))
            return true;
        Doing = "smelting a bloom";
        return AtStation(sim, me, forge, now, f => Send(new CraftCommand(me, f, recipe)));
    }

    /// <summary>The piece in the tongs, on to its next station: the fire while it's dark, the anvil while it glows, the tub once it's shaped.</summary>
    private bool WorkPiece(Simulation sim, Entity me, PerceptionView view, long now)
    {
        if (view.Piece(me) is not { } piece)
            return false;
        if (!piece.Hot && Nearest(sim, me, "Forge") is { IsNull: false } lit && view.FuelIn(lit) is <= 0f)
            return FeedForge(sim, me, view, lit, now);
        // Work for two (a greatsword): heated only once there's someone at the anvil to strike, so it's not back and forth
        // to the fire with nobody there (the smithy too hot to stand in); meanwhile it calls for a hand.
        if (!piece.Hot && !piece.Shaped && Smithing.PieceOf(sim, me) is { Recipe.TwoHanded: true }
            && Nearest(sim, me, "Anvil") is { IsNull: false } anvil && !StrikerAt(sim, me, anvil))
        {
            Doing = "waiting for a striker at the anvil";
            if (now - _askedForHand > SimClock.TicksPerSecond * 20)
            {
                _askedForHand = now;
                Send(new AskForHandCommand(me)); // dropped when it waits for a player's hand (WaitsForHand)
            }
            return true;
        }
        var next = !piece.Hot ? "Forge" : piece.Shaped ? "QuenchTub" : "Anvil";
        Doing = $"forging: {piece.Label} to the {next.ToLowerInvariant()}";
        return AtStation(sim, me, Nearest(sim, me, next), now, st => Send(new SmithCommand(me, st)));
    }

    private long _askedForHand = -1_000_000;

    /// <summary>Someone besides the smith standing where they could take the sledge at this anvil (or on their way, called).</summary>
    private static bool StrikerAt(Simulation sim, Entity me, Entity anvil)
    {
        var w = sim.World;
        foreach (var e in sim.Creatures())
            if (e != me && w.Get<Creature>(e) is { Dead: false, Downed: false } && !w.Has<Components.Adventurer>(e)
                && (AvatarSystem.InReach(sim, w.Get<Position>(e).Cell, anvil) || w.TryGet<Helping>(e, out var h) && h.Lead == me))
                return true;
        return false;
    }

    /// <summary>
    /// A weapon of its own making (not on the rack yet, not a customer's) taken to the muster rack:
    /// true while at it, false if there's a rack but it can't get there, null if there's nothing to
    /// hang (or no rack): then it makes one.
    /// </summary>
    public bool? RackSpare(Simulation sim, Entity me, long now)
    {
        var w = sim.World;
        var rack = Nearest(sim, me, Militia.MilitiaSystem.RackDef);
        if (rack.IsNull)
            return false;
        bool Spare(Entity e) => Militia.MilitiaSystem.IsWeapon(sim, e) && w.TryGet<Crafted>(e, out var c) && c.Maker == me
            && !w.Has<Militia.TownArms>(e) && !w.Has<Entrusted>(e) && !w.Has<ForSale>(e);
        var pack = sim.PackOf(me);
        if (Spare(w.Get<Carrying>(me).Item))
        {
            Doing = $"taking a {sim.LabelOf(w.Get<Carrying>(me).Item)} to the muster rack";
            return AtStation(sim, me, rack, now, r =>
            {
                Send(new AvatarWorkCommand(me, true, w.Get<Thing>(r).Cell));
                Send(new AvatarWorkCommand(me, false));
            });
        }
        if (!pack.IsNull && sim.StoredIn(pack).FirstOrDefault(Spare, Entity.Null) is { IsNull: false } spare)
        {
            Doing = $"taking a {sim.LabelOf(spare)} to the muster rack";
            return AtStation(sim, me, rack, now, r => Send(new StoreFromPackCommand(me, r, spare)));
        }
        return null;
    }

    /// <summary>The spear the call to arms is answered with: cheap (a bar and two hafts), and quick at the anvil.</summary>
    public const string MilitiaWeapon = "ForgeIronSpear";

    /// <summary>A weapon of its own making to hang on the muster rack, or one to make. False if it can't do either.</summary>
    private bool ArmTheTown(Simulation sim, Entity me, PerceptionView view, long now)
    {
        if (RackSpare(sim, me, now) is { } racking)
            return racking;
        if (sim.Defs.AllRecipes.FirstOrDefault(r => r.Name == MilitiaWeapon) is not { } recipe || !RecipeBooks.Knows(sim, me, recipe))
            return false;
        var bar = recipe.Ingredients[0];
        if (OnMe(sim, me, bar.Def) < bar.Count)
            return false; // smelt first
        foreach (var x in recipe.Ingredients.Skip(1))
            if (OnMe(sim, me, x.Def) < x.Count)
                return Fetch(sim, me, view, now, x, x.ThingDef.Label);
        var forge = Nearest(sim, me, "Forge");
        if (forge.IsNull)
            return false;
        if (Stoke(sim, me, view, now))
            return true;
        Doing = "forging a spear for the muster";
        return AtStation(sim, me, forge, now, f => Send(new CraftCommand(me, f, recipe)));
    }

    /// <summary>Take what the forge wants from the containers at its workplace (the chest, the fuel bin). False, and gives up a while, if there's none.</summary>
    public bool Fetch(Simulation sim, Entity me, PerceptionView view, long now, ThingCount want, string what)
    {
        var w = sim.World;
        if (MyShop(sim, me) is not { IsNull: false } shop)
            return false;
        var store = Buildings.Parts(sim, shop).Where(e => w.Get<Thing>(e).Def.Building?.StorageStacks > 0)
            .OrderBy(e => e.Index).ToList();
        // Its own workplace's chests are common knowledge to it, as a keeper knows their shelves.
        var holding = store.FirstOrDefault(e => sim.StoredIn(e).Any(i => w.Get<Thing>(i).Def == want.ThingDef), Entity.Null);
        if (holding.IsNull)
        {
            _noOreUntil = now + SimClock.TicksPerHour;
            Need(sim, me, want.ThingDef.Label, now);
            return false;
        }
        Doing = $"fetching {what}";
        return AtStation(sim, me, holding, now, s =>
        {
            if (view.InContainer(s).FirstOrDefault(e => w.Get<Thing>(e).Def == want.ThingDef, Entity.Null) is { IsNull: false } item)
                Send(new TakeFromStorageCommand(me, item, want.Count * 2));
        });
    }

    /// <summary>The smith's further games; null for a session that isn't one.</summary>
    private (bool Press, bool Held, int Axis)? PlayForge(WorkSession s) => null;

    /// <summary>
    /// While the town's quiet (<see cref="Quiet"/>), a blade for the shelf while its stock's short: of the pieces it knows how to
    /// forge from what it has (bars on it, the rest in the smithy's chests), the one most of the
    /// adventurers it's seen would be the better for (judged by the weapon in their hand and what
    /// they wore), the quickest first; never a kind already in stock. Not while the smithy's too
    /// hot to see a piece through (<see cref="TooHotToForge"/>). False if it's nothing to make, or
    /// not the iron for it (it smelts first).
    /// </summary>
    private bool ForgeForSale(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        var forge = Nearest(sim, me, "Forge");
        if (!Quiet(sim, me, view, now) || forge.IsNull || Stock(sim, me) >= StockWanted || !w.TryGet<Workbench>(forge, out var bench))
            return false;
        var stocked = OnSale(sim, me).Concat(Loose(sim, me).Where(i => ForTheShelf(sim, me, i))).Select(i => w.Get<Thing>(i).Def).ToHashSet();
        RecipeDef? pick = null;
        var best = (Wanted: -1, Work: int.MaxValue);
        foreach (var r in w.Get<Thing>(forge).Def.Recipes)
        {
            if (r.Bloom || r.Products.Count != 1 || r.Products[0].ThingDef is not { } made || !Goods.IsGear(made) || stocked.Contains(made)
                || !Crafting.CanMake(sim, me, r) || Crafting.MissingTool(sim, me, forge, r) != null || !r.Ingredients.TrueForAll(x => Obtainable(sim, me, x)))
                continue;
            // A piece it's already begun at the forge comes first; then what's wanted, the quicker the better.
            var begun = bench.Active == r && bench.By == me && !bench.Untouched;
            var score = (Wanted: begun ? int.MaxValue : WantedBy(made), Work: (int)r.Work);
            if (score.Wanted > best.Wanted || (score.Wanted == best.Wanted && score.Work < best.Work))
                (pick, best) = (r, score);
        }
        if (pick == null)
            return false;
        if (TooHotToForge(sim, me, forge, pick))
        {
            Doing = "letting the smithy cool";
            return false;
        }
        if (!w.Get<Carrying>(me).Item.IsNull && !pick.Ingredients.Exists(x => x.ThingDef == w.Get<Thing>(w.Get<Carrying>(me).Item).Def))
        {
            Send(new StowCommand(me));
            return true;
        }
        foreach (var x in pick.Ingredients)
            if (OnMe(sim, me, x.Def) < x.Count)
                return Fetch(sim, me, view, now, x, x.ThingDef.Label);
        if (Stoke(sim, me, view, now))
            return true;
        Doing = $"forging a {pick.Label} for the shelf";
        return AtStation(sim, me, forge, now, f => Send(new CraftCommand(me, f, pick)));
    }

    /// <summary>Game hours since it last saw an adventurer before it reckons the town quiet enough to forge for the shelf.</summary>
    public const float QuietHours = 2;

    private long _sawAdventurer = long.MinValue / 2;

    /// <summary>What an adventurer it's seen carries and wears (head and body), as it looked then: what the shelf should offer.</summary>
    private readonly record struct Kit(ThingDef? Weapon, ThingDef? Head, ThingDef? Body);

    private readonly Dictionary<Entity, Kit> _kits = [];

    /// <summary>
    /// No adventurers about (none seen for a while, no order of theirs waiting): the time to forge
    /// for the shelf, so the smithy's cooled again by the time someone brings a blade to mend.
    /// What the ones it sees carry is remembered, for what to make.
    /// </summary>
    private bool Quiet(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        ThingDef? DefOf(Entity e) => w.IsAlive(e) ? w.Get<Thing>(e).Def : null;
        foreach (var e in view.Creatures())
            if (w.Has<Adventurer>(e))
            {
                _sawAdventurer = now;
                var worn = w.TryGet<Apparel>(e, out var a) ? a : new Apparel();
                _kits[e] = new Kit(w.TryGet<Equipment>(e, out var eq) ? DefOf(eq.Weapon) : null, DefOf(worn.Head), DefOf(worn.Body));
            }
        return now - _sawAdventurer >= SimClock.HoursToTicks(QuietHours)
            && !view.Orders().Any(o => o.Status == OrderStatus.Open && o.Kind == ServiceKind.Repair);
    }

    /// <summary>Of a recipe's ingredients: enough on it, or in its workplace's chests (common knowledge to it).</summary>
    public static bool Obtainable(Simulation sim, Entity me, ThingCount x)
    {
        if (OnMe(sim, me, x.Def) >= x.Count)
            return true;
        if (MyShop(sim, me) is not { IsNull: false } shop)
            return false;
        var w = sim.World;
        var n = 0;
        foreach (var store in Buildings.Parts(sim, shop).Where(e => w.Get<Thing>(e).Def.Building?.StorageStacks > 0))
            foreach (var i in sim.StoredIn(store))
                if (w.Get<Thing>(i).Def == x.ThingDef)
                    n += w.Get<Stack>(i).Count;
        return n + OnMe(sim, me, x.Def) >= x.Count;
    }

    /// <summary>
    /// How many of the adventurers it's seen would be better off with a piece like this, by what
    /// they carried and wore when it saw them: a weapon of the kind they fight with (in hand or at
    /// range) a tier above theirs; a helm or a body piece where they had none, or a tier above it.
    /// </summary>
    private int WantedBy(ThingDef made)
    {
        var n = 0;
        foreach (var kit in _kits.Values)
        {
            if (made.Weapon is { } attack)
            {
                if (kit.Weapon == null || ((kit.Weapon.Weapon?.Ranged ?? false) == attack.Ranged && made.GearTier > kit.Weapon.GearTier))
                    n++;
            }
            else if (made.Apparel is { } apparel && apparel.Slot is ApparelSlot.Head or ApparelSlot.Body)
            {
                var worn = apparel.Slot == ApparelSlot.Head ? kit.Head : kit.Body;
                if (worn == null || made.GearTier > worn.GearTier)
                    n++;
            }
        }
        return n;
    }
}
