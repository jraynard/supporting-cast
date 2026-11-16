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
            return AtStation(sim, me, Nearest(sim, me, "Forge"), now, s => Send(new RepairCommand(me, s, blade)));
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
                Send(new AvatarWorkCommand(me, true, w.Get<Thing>(f).Cell));
                Send(new AvatarWorkCommand(me, false));
            });
        return AtStation(sim, me, Nearest(sim, me, "FuelBin"), now, bin =>
        {
            if (view.InContainer(bin).FirstOrDefault(IsFuel, Entity.Null) is { IsNull: false } coal)
                Send(new TakeFromStorageCommand(me, coal, 10)); // a few shovelfuls
        });
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
        if (view.Piece(me) is { } piece)
        {
            if (!piece.Hot && Nearest(sim, me, "Forge") is { IsNull: false } lit && view.FuelIn(lit) is <= 0f)
                return FeedForge(sim, me, view, lit, now);
            var next = !piece.Hot ? "Forge" : piece.Shaped ? "QuenchTub" : "Anvil";
            Doing = $"forging: {piece.Label} to the {next.ToLowerInvariant()}";
            return AtStation(sim, me, Nearest(sim, me, next), now, st => Send(new SmithCommand(me, st)));
        }
        if (sim.Militia.Paying && ArmTheTown(sim, me, view, now))
            return true;
        if (now < _noOreUntil || sim.Defs.AllRecipes.FirstOrDefault(r => r.Bloom) is not { } recipe || OnMe(sim, me, "IronBar") >= SmeltBelow)
            return false;
        var forge = Nearest(sim, me, "Forge");
        if (forge.IsNull)
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
        Doing = "smelting a bloom";
        return AtStation(sim, me, forge, now, f => Send(new CraftCommand(me, f, recipe)));
    }

    /// <summary>The spear the call to arms is answered with: cheap (a bar and two hafts), and quick at the anvil.</summary>
    public const string MilitiaWeapon = "ForgeIronSpear";

    /// <summary>A weapon of its own making to hang on the muster rack, or one to make. False if it can't do either.</summary>
    private bool ArmTheTown(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        var rack = Nearest(sim, me, Militia.MilitiaSystem.RackDef);
        if (rack.IsNull)
            return false;
        bool Spare(Entity e) => Militia.MilitiaSystem.IsWeapon(sim, e) && w.TryGet<Crafted>(e, out var c) && c.Maker == me
            && !w.Has<Militia.TownArms>(e) && !w.Has<Entrusted>(e);
        var pack = sim.PackOf(me);
        if (Spare(w.Get<Carrying>(me).Item))
        {
            Doing = "taking a spear to the muster rack";
            return AtStation(sim, me, rack, now, r =>
            {
                Send(new AvatarWorkCommand(me, true, w.Get<Thing>(r).Cell));
                Send(new AvatarWorkCommand(me, false));
            });
        }
        if (!pack.IsNull && sim.StoredIn(pack).FirstOrDefault(Spare, Entity.Null) is { IsNull: false } spare)
        {
            Doing = "taking a spear to the muster rack";
            return AtStation(sim, me, rack, now, r => Send(new StoreFromPackCommand(me, r, spare)));
        }
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
        Doing = "forging a spear for the muster";
        return AtStation(sim, me, forge, now, f => Send(new CraftCommand(me, f, recipe)));
    }

    /// <summary>Take what the forge wants from the containers at its workplace (the chest, the fuel bin). False, and gives up a while, if there's none.</summary>
    private bool Fetch(Simulation sim, Entity me, PerceptionView view, long now, ThingCount want, string what)
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
}
