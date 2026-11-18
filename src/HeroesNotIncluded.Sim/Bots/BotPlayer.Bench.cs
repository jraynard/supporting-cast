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
    /// <summary>Feel a strongbox over for traps, then pick it, then hand it back open.</summary>
    private bool OpenBox(Simulation sim, Entity me, PerceptionView view, Entity owner, long now)
    {
        var box = Entrusted(sim, me, owner);
        if (box.IsNull)
        {
            Reach(sim, me, owner, now);
            return true;
        }
        if (view.Strongbox(box) is not { } known)
            return false;
        Doing = known.Locked ? (known.SearchedByMe ? "picking a lock" : "feeling for traps") : "handing back a strongbox";
        if (known.Locked)
        {
            Send(known.SearchedByMe ? new PickLockCommand(me, box) : new ProbeBoxCommand(me, box));
            return true;
        }
        if (Reach(sim, me, owner, now, returning: true))
            Send(new GiveCommand(me, owner, box, 1));
        return true;
    }

    /// <summary>
    /// Out to the customer's wagon (whose is whose is plain to see), and true its wheel: the tinker's
    /// part (the woodwork is the carpenter's own order). Paid once the wheel runs true.
    /// </summary>
    private bool MendWagon(Simulation sim, Entity me, PerceptionView view, Entity owner, long now)
    {
        var w = sim.World;
        var wagon = Entity.Null;
        foreach (var e in w.Each<Wagon>())
            if (w.Get<Wagon>(e).Owner == owner)
                wagon = e;
        if (wagon.IsNull)
            return false;
        var cell = w.Get<Thing>(wagon).Cell;
        var seen = view.Wagon(wagon);
        if (!AvatarSystem.InReach(sim, w.Get<Position>(me).Cell, wagon) || seen is not { } known)
        {
            Doing = "going out to a wagon";
            Walk(sim, me, cell, now);
            return true;
        }
        if (known.Damage == WagonDamage.None)
            return false;
        // The wheel's the tinker's; the woodwork goes to the carpenter on its own order.
        if ((known.Damage & WagonDamage.Wheel) == 0)
            return false;
        Doing = "truing a wagon wheel";
        Send(new TrueWheelCommand(me, wagon));
        return true;
    }

    /// <summary>Wood from a chest at its workplace, for an axle. False if there's none to be had.</summary>
    private bool FetchWood(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(me, out var trade) || !trade.Def.HasWorkplace)
            return false;
        foreach (var e in w.Each<PartOf>())
        {
            var b = w.Get<PartOf>(e).Building;
            if (!w.TryGet<PresetBuilding>(b, out var pb) || !trade.Def.WorksAt(pb.Def) || w.Get<Thing>(e).Def.Name != "Chest")
                continue;
            var chest = e;
            if (view.InContainer(chest).FirstOrDefault(i => w.Get<Thing>(i).Def.Name == "Wood", Entity.Null) is not { IsNull: false } wood)
                continue;
            Doing = "fetching wood for an axle";
            return AtStation(sim, me, chest, now, _ => Send(new TakeFromStorageCommand(me, wood, Wagons.AxleWood)));
        }
        return false;
    }

    /// <summary>Look a curio over under the loupe, then hand it back with an honest reckoning (its middle).</summary>
    private bool Appraise(Simulation sim, Entity me, PerceptionView view, Entity owner, long now)
    {
        var curio = Entrusted(sim, me, owner);
        if (curio.IsNull)
        {
            Reach(sim, me, owner, now);
            return true;
        }
        if (view.Curio(curio) is not { } known)
            return false;
        if (known.Low == null)
        {
            Doing = "looking a curio over";
            Send(new AppraiseCommand(me, curio));
            return true;
        }
        Doing = "handing back a curio";
        if (Reach(sim, me, owner, now, returning: true) && !Haggle(sim, me, curio, known))
            Send(new GiveCommand(me, owner, curio, 1));
        return true;
    }

    /// <summary>The tinker's further games; null for a session that isn't one.</summary>
    private (bool Press, bool Held, int Axis)? PlayBench(WorkSession s) => s switch
    {
        PinSession pin => File(pin),
        _ => null,
    };

    // ---------------- Curios ----------------

    /// <summary>The least it reckons on clearing on a curio, as a share of what the collector would pay, before it'll offer.</summary>
    public const float CurioMargin = 0.15f;

    /// <summary>How far under the middle of its reading the greediest tinker puts a curio it means to buy.</summary>
    public const float TalkDown = 0.6f;

    // Curios it's made an offer for (once each).
    private readonly HashSet<Entity> _bidFor = [];

    /// <summary>
    /// With its reading of a curio in hand and the owner in reach: an offer, if it reckons the
    /// collector would pay well over it. What it'd put it at comes from its own reading only (never
    /// what it's really worth): the middle of its range, talked down the greedier it is; it says
    /// so, then offers what the owner would take on that word. An honest one doesn't buy at all.
    /// Nothing for one it thinks a fake. True if it made one (the owner's answer comes back aloud).
    /// </summary>
    private bool Haggle(Simulation sim, Entity me, Entity curio, KnownCurio known)
    {
        if (known is not { Low: { } low, High: { } high, Doubts: false } || !_bidFor.Add(curio)
            || Nearest(sim, me, CollectorSystem.CrateDef).IsNull)
            return false;
        var middle = (low + high) * 0.5f;
        var quote = (int)MathF.Round(middle * (1f - TalkDown * Temper.Greed));
        var offer = (int)MathF.Ceiling(quote * Curios.TakesOffer);
        var fetches = middle * Curios.CollectorShare;
        if (offer <= 0 || fetches - offer < Math.Max(2f, fetches * CurioMargin) || Coin.Count(sim, me) < offer)
            return false;
        Doing = $"offering {offer} for a curio";
        if (quote != (known.Quoted ?? (low + high) / 2))
            Send(new QuoteCurioCommand(me, curio, quote));
        Send(new OfferForCurioCommand(me, curio, offer));
        return true;
    }

    /// <summary>A curio of its own (bought, not left with it) into the collector's crate, and the collector's silver out of it. True while at it.</summary>
    private bool Collector(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        var crate = Nearest(sim, me, CollectorSystem.CrateDef);
        if (crate.IsNull)
            return false;
        var bought = Loose(sim, me).FirstOrDefault(i => view.Curio(i) is { Mine: true } && !w.Has<Entrusted>(i), Entity.Null);
        if (!bought.IsNull)
        {
            Doing = "taking a curio to the collector's crate";
            var held = w.Get<Carrying>(me).Item == bought;
            return AtStation(sim, me, crate, now, c =>
            {
                if (held)
                    Use(sim, me, c);
                else
                    Send(new StoreFromPackCommand(me, c, bought));
            });
        }
        if (!AvatarSystem.InReach(sim, w.Get<Position>(me).Cell, crate) && !view.CanSee(crate))
            return false;
        if (view.InContainer(crate).FirstOrDefault(i => w.Get<Thing>(i).Def.Name == Coin.Silver, Entity.Null) is not { IsNull: false } silver)
            return false;
        Doing = "taking the collector's silver";
        return AtStation(sim, me, crate, now, _ => Send(new TakeFromStorageCommand(me, silver, TakeFromStorageCommand.All)));
    }

    // ---------------- At the bench ----------------

    // The lock it's fitted to its shop's door, and whether it's put a spare key by.
    private bool _lockFitted, _spareKept;

    /// <summary>
    /// The tinker between orders: curios bought to the collector's crate; its contraptions on show;
    /// a lock for its shop's door (pinned at the bench, fitted, and a spare key filed and put in the
    /// shop's chest); then contraptions for the parties going out, while the stock's short. True while at it.
    /// </summary>
    private bool Bench(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(me, out var trade) || !trade.Def.Stations.Contains(Tinkers.BenchDef) || MyShop(sim, me) is not { IsNull: false } shop)
            return false;
        if (Collector(sim, me, view, now) || ShowGoods(sim, me, view, now))
            return true;
        var bench = Nearest(sim, me, Tinkers.BenchDef);
        if (bench.IsNull || !w.TryGet<Workbench>(bench, out var made))
            return false;
        // Something already begun at the bench is seen through first.
        if (made.Active is { } begun && made.By == me && !made.Untouched)
            return Make(sim, me, view, bench, begun, now);
        return TripBell(sim, me, view, bench, now) || Locksmith(sim, me, view, shop, bench, now) || Contraption(sim, me, view, bench, now);
    }

    /// <summary>
    /// A raid's been called (the shepherd's word is the town's) and its road has no bell: fit one at
    /// the bench, carry it out, and set it across the road a way in from the edge. True while at it.
    /// </summary>
    private bool TripBell(Simulation sim, Entity me, PerceptionView view, Entity bench, long now)
    {
        var w = sim.World;
        if (!sim.Militia.Open || sim.Militia.Mustering || sim.Director.WarnedSide is not { } side
            || TripBells.SpotFor(sim, side) is not { } spot || TripBells.Guarded(sim, spot))
            return false;
        var bell = TripBells.Carried(sim, me);
        if (bell.IsNull)
        {
            var recipe = w.Get<Thing>(bench).Def.Recipes.FirstOrDefault(r => Tinkers.MakesContraption(r) is { Use: ContraptionUse.Alarm });
            if (recipe == null || !Crafting.CanMake(sim, me, recipe) || !recipe.Ingredients.TrueForAll(x => Obtainable(sim, me, x)))
                return false;
            return Make(sim, me, view, bench, recipe, now);
        }
        Doing = "setting a trip-bell on the road";
        if (w.Get<Carrying>(me).Item != bell)
        {
            Send(new HoldCommand(me, bell));
            return true;
        }
        var here = w.Get<Position>(me).Cell;
        if (AvatarSystem.InReach(here, spot))
        {
            Send(new AvatarWorkCommand(me, true, spot));
            Send(new AvatarWorkCommand(me, false));
        }
        else
            Walk(sim, me, spot, now);
        return true;
    }

    /// <summary>The shop's door it'd put a lock on: its first without one. Null if they all have one.</summary>
    private static Entity? BareDoor(Simulation sim, Entity shop) =>
        Buildings.Parts(sim, shop).Where(e => Locksmithing.IsDoor(sim, e) && !Locksmithing.HasLock(sim, e)).OrderBy(e => e.Index).FirstOrDefault(Entity.Null) is { IsNull: false } d ? d : null;

    /// <summary>A lock for its door: fitted if it's carrying one, pinned at the bench if not; then a spare key put by. True while at it.</summary>
    private bool Locksmith(Simulation sim, Entity me, PerceptionView view, Entity shop, Entity bench, long now)
    {
        var w = sim.World;
        if (!_lockFitted && Tinkers.CarriedLock(sim, me) is { IsNull: false } lockItem)
        {
            if (BareDoor(sim, shop) is not { } door)
            {
                _lockFitted = true;
                return false;
            }
            Doing = $"fitting a lock to the {w.Get<Thing>(door).Label}";
            return AtStation(sim, me, door, now, d =>
            {
                Send(new FitLockCommand(me, d, lockItem));
                _lockFitted = true;
                if (now >= _nextTalk)
                    Say(sim, me, "lock_fitted", Volume.Say, now, thing: w.Get<Thing>(d).Label);
            });
        }
        if (_lockFitted && !_spareKept)
        {
            var door = Buildings.Parts(sim, shop).Where(e => Locksmithing.HasLock(sim, e) && view.Lock(e) is { HaveKey: true })
                .OrderBy(e => e.Index).FirstOrDefault(Entity.Null);
            if (door.IsNull)
            {
                _spareKept = true;
                return false;
            }
            var seed = w.Get<LockCut>(door).Seed;
            var keys = Locksmithing.Keys(sim, me).Where(k => w.Get<Key>(k).Seed == seed).ToList();
            if (keys.Count >= 2)
            {
                // The copy goes in the shop's chest, as the inn keeps its spares in the storeroom.
                var chest = Buildings.Parts(sim, shop).Where(e => w.Get<Thing>(e).Def.Name == "Chest").OrderBy(e => e.Index).FirstOrDefault(Entity.Null);
                if (chest.IsNull)
                {
                    _spareKept = true;
                    return false;
                }
                Doing = "putting a spare key by";
                var spare = keys[^1];
                return AtStation(sim, me, chest, now, c =>
                {
                    if (w.Get<Carrying>(me).Item == spare)
                        Send(new StowCommand(me));
                    Send(new StoreFromPackCommand(me, c, spare));
                    _spareKept = true;
                });
            }
            if (Tinkers.Bar(sim, me).IsNull)
                return LockRecipe(sim) is { } r && Fetch(sim, me, view, now, r.Ingredients[0], "an iron bar");
            Doing = "filing a spare key";
            return AtStation(sim, me, bench, now, b => Send(new CopyKeyCommand(me, b, keys[0])));
        }
        if (_lockFitted || BareDoor(sim, shop) is null || LockRecipe(sim) is not { } recipe || !Crafting.CanMake(sim, me, recipe)
            || !recipe.Ingredients.TrueForAll(x => Obtainable(sim, me, x)))
            return false;
        return Make(sim, me, view, bench, recipe, now);
    }

    private static RecipeDef? LockRecipe(Simulation sim) =>
        sim.Defs.AllRecipes.FirstOrDefault(r => Tinkers.MakesLock(r) && r.Station == Tinkers.BenchDef);

    /// <summary>
    /// A contraption for the shelf while the stock's short: one that does something none in stock
    /// does (nobody carries two the same), of those it knows and has the makings for, the least iron first.
    /// </summary>
    private bool Contraption(Simulation sim, Entity me, PerceptionView view, Entity bench, long now)
    {
        var w = sim.World;
        if (Stock(sim, me) >= StockWanted)
            return false;
        var uses = OnSale(sim, me).Concat(Loose(sim, me).Where(i => ForTheShelf(sim, me, i)))
            .Select(i => w.Get<Thing>(i).Def.Contraption?.Use).OfType<ContraptionUse>().ToHashSet();
        var pick = w.Get<Thing>(bench).Def.Recipes
            .Where(r => Tinkers.MakesContraption(r) is { ForTheRoad: true } c && !uses.Contains(c.Use) && Crafting.CanMake(sim, me, r)
                && Crafting.MissingTool(sim, me, bench, r) == null && r.Ingredients.TrueForAll(x => Obtainable(sim, me, x)))
            .OrderBy(r => r.Ingredients.Where(x => x.Def == Tinkers.BarDef).Sum(x => x.Count))
            .FirstOrDefault();
        return pick != null && Make(sim, me, view, bench, pick, now);
    }

    /// <summary>Fetch what a recipe wants from the shop's chest, then make it at the bench (its hand game starts there).</summary>
    public bool Make(Simulation sim, Entity me, PerceptionView view, Entity bench, RecipeDef recipe, long now)
    {
        var w = sim.World;
        var staged = w.Get<Workbench>(bench) is { } b && b.Active == recipe ? b.Staged : [];
        for (var i = 0; i < recipe.Ingredients.Count; i++)
        {
            var x = recipe.Ingredients[i];
            var have = OnMe(sim, me, x.Def) + (i < staged.Length ? staged[i] : 0);
            if (have < x.Count)
                return Fetch(sim, me, view, now, x, x.ThingDef.Label);
        }
        Doing = $"making a {recipe.Label} at the bench";
        return AtStation(sim, me, bench, now, s => Send(new CraftCommand(me, s, recipe)));
    }

    // ---------------- Filing pins ----------------

    // The pin it last set (so one Space sets one pin), in which session.
    private int _setPin = -1;
    private long _pinSession = -1;

    /// <summary>
    /// Filing a lock's pins (or a key's bits): long strokes while there's a way to go, short dabs as
    /// it nears the mark, and set it (Space) just shy of it. A slip: on one pin, it dabs on past the
    /// mark before it looks, and the pin sits sloppy.
    /// </summary>
    private (bool, bool, int) File(PinSession s)
    {
        if (s.StartTick != _pinSession)
            (_pinSession, _setPin) = (s.StartTick, -1);
        if (s.Pin >= s.Pins || s.Pin == _setPin)
            return (false, false, 0); // set: the next pin comes up
        var slipping = _slip && s.Pin == s.Pins / 2;
        var aim = slipping ? s.Mark - 0.03f : s.Mark + s.CrispWidth * 0.3f;
        var gap = s.Length - aim;
        if (gap <= 0f)
        {
            if (slipping)
                Slipped();
            _act = true;
            _setPin = s.Pin;
            return (false, false, 0);
        }
        if (gap > 0.06f)
            return (false, true, 0); // a long stroke
        _working = (_working + 1) % 3;
        return (false, _working == 0, 0); // a dab, then a look
    }

    /// <summary>
    /// A piece mended at its maker's bench (a bow at the carpenter's, a crossbow at the tinker's):
    /// fetched from its owner, the wood or iron it takes brought from the shop's chest, worked at the
    /// bench until it looks sound, and handed back.
    /// </summary>
    public bool BenchMend(Simulation sim, Entity me, PerceptionView view, Entity owner, long now, string benchDef)
    {
        var w = sim.World;
        var piece = Entrusted(sim, me, owner);
        if (piece.IsNull)
        {
            Reach(sim, me, owner, now); // they hand it over once we're close
            return true;
        }
        if (view.Awaiting().Any(a => a.Order == _task && a.Ready))
        {
            Doing = "handing back a mended piece";
            if (Reach(sim, me, owner, now, returning: true))
                Send(new GiveCommand(me, owner, piece, 1));
            return true;
        }
        var bench = Nearest(sim, me, benchDef);
        if (bench.IsNull)
            return false;
        if (Crafting.RepairCost(sim, w.Get<Thing>(bench).Def, piece) is { } cost && OnMe(sim, me, cost.Material.Def) < cost.Count)
            return Fetch(sim, me, view, now, cost.Material, cost.Material.ThingDef.Label);
        Doing = $"mending: {view.Condition(piece) ?? sim.LabelOf(piece)}";
        return AtStation(sim, me, bench, now, s => Send(new RepairCommand(me, s, piece)));
    }
}
