using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Carpentry;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

using HeroesNotIncluded.Sim.Bots;

namespace HeroesNotIncluded.Sim.Carpentry;

/// <summary>The carpenter: quivers fletched for the shelf, and planks sawn when there are none to fletch from.</summary>
public sealed class CarpenterBot(BotPlayer bot) : IBotTrade
{
    public bool Between(Simulation sim, Entity me, PerceptionView view, long now) => Woodshop(sim, me, view, now);

    public bool? Order(Simulation sim, Entity me, PerceptionView view, ServiceKind kind, Entity poster, long now) => kind switch
    {
        ServiceKind.WoodRepair => bot.BenchMend(sim, me, view, poster, now, Carpenters.BenchDef),
        ServiceKind.Arrows => QuiverFor(sim, me, view, poster, now),
        ServiceKind.WagonJoinery => JoinWagon(sim, me, view, poster, now),
        _ => null,
    };

    public (bool Press, bool Held, int Axis)? Play(WorkSession s)
    {
        switch (s)
        {
            case PitSession pit:
                // Pull with each stroke the top sawyer starts, and let go between.
                return (false, pit.CallOpen || pit.Down > 0f && pit.Down < 1f, 0);
            case CrosscutSession crosscut:
                // Pull back with each stroke the lead starts, and let go between.
                return (false, crosscut.CallOpen || crosscut.Drawn > 0f && crosscut.Drawn < 1f, 0);
            case BuckSession { GreatButt: true } great when great.Partner.IsNull && !great.Done && great.StepsDone % (SimClock.TicksPerSecond * 4) == 1:
                bot.Send(new AskForHandCommand(great.Worker));
                return (false, false, 0);
            case LiftSession lift:
                // Keep it up, and lean against the way it's tipping; set it down to rest once the arms are nearly spent.
                return (false, lift.Strain < 0.9f || lift.Up && lift.Strain < 0.97f, lift.Tilt + lift.Sway * 8f > 0.05f ? -1 : lift.Tilt + lift.Sway * 8f < -0.05f ? 1 : 0);
            case JoinSession { Work: JoinWork.Axle, Cutting: false } axle when axle.Partner.IsNull && axle.StepsDone % (SimClock.TicksPerSecond * 4) == 1:
                bot.Send(new AskForHandCommand(axle.Worker));
                return (false, false, 0);
            case SawSession { Great: true, Hewing: false } great when great.Partner.IsNull && great.StepsDone % (SimClock.TicksPerSecond * 4) == 1:
                bot.Send(new AskForHandCommand(great.Worker));
                return (false, false, 0);
        }
        return PlayWoodshop(s);
    }

    /// <summary>
    /// The carpenter between orders: its quivers on show; a piece someone asked for by name; a quiver fletched while the stock's short;
    /// planks sawn first if there are none to split shafts from. True while at it.
    /// </summary>
    private bool Woodshop(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(me, out var trade) || !trade.Def.Stations.Contains(Carpenters.BenchDef) || BotPlayer.MyShop(sim, me) is not { IsNull: false })
            return false;
        // In a call to arms, a bow of its own goes to the muster rack before it goes on the shelf.
        if (sim.Militia.Paying && bot.RackSpare(sim, me, now) == true)
            return true;
        if (bot.ShowGoods(sim, me, view, now))
            return true;
        var bench = BotPlayer.Nearest(sim, me, Carpenters.BenchDef);
        var horse = BotPlayer.Nearest(sim, me, Carpenters.SawhorseDef);
        // Something already begun at a station is seen through first.
        foreach (var station in new[] { bench, horse })
            if (!station.IsNull && w.TryGet<Workbench>(station, out var b) && b.Active is { } begun && b.By == me && !b.Untouched)
                return bot.Make(sim, me, view, station, begun, now);
        if (sim.Militia.Paying && !bench.IsNull && BowsForTheMuster(sim, me, view, bench, now))
            return true;
        if (!bench.IsNull && Refurnish(sim, me, view, bench, now))
            return true;
        if (FirewoodOrdered(sim, me, view, now) || Firewood(sim, me, view, now))
            return true;
        // A piece asked for by name (a bow, a cudgel), or a commission off the board (planks, firewood): made at
        // whichever of its stations makes it, whatever's in stock.
        if (BotPlayer.Asked(sim, me, view) is { } asked)
            foreach (var station in new[] { bench, horse, BotPlayer.Nearest(sim, me, Carpenters.BlockDef) })
                if (BotPlayer.AskedRecipe(sim, me, station, asked) is { } wanted)
                    return bot.Make(sim, me, view, station, wanted, now);
        if (bot.Stock(sim, me) >= bot.StockWanted || bench.IsNull)
            return false;
        var fletch = Recipe(sim, bench, Carpenters.Fletches);
        if (fletch == null || !Crafting.CanMake(sim, me, fletch))
            return false;
        if (fletch.Ingredients.TrueForAll(x => BotPlayer.Obtainable(sim, me, x)))
            return bot.Make(sim, me, view, bench, fletch, now);
        // Short of planks: saw a log (the chest's, or one it fetches, bucks or fells), or timber at a pinch.
        var planks = fletch.Ingredients.Find(x => x.Def == Carpenters.PlanksDef);
        if (horse.IsNull || planks == null || BotPlayer.Obtainable(sim, me, planks))
            return false;
        if (SawRecipe(sim, me, horse) is { } saw)
            return bot.Make(sim, me, view, horse, saw, now);
        return GetALog(sim, me, view, now, "for planks", great: false);
    }

    /// <summary>
    /// An archer's ask for arrows: a quiver it has on it (not one on the shelf) handed over; else one
    /// fletched at the bench (planks sawn first if there are none). False if it can do neither.
    /// </summary>
    private bool QuiverFor(Simulation sim, Entity me, PerceptionView view, Entity archer, long now)
    {
        var w = sim.World;
        var mine = Fletching.Carried(sim, me).FirstOrDefault(q => !w.Has<ForSale>(q), Entity.Null);
        if (!mine.IsNull)
        {
            bot.Doing = "bringing a quiver";
            if (bot.Reach(sim, me, archer, now, returning: true))
                bot.Send(new GiveCommand(me, archer, mine, 1));
            return true;
        }
        var bench = BotPlayer.Nearest(sim, me, Carpenters.BenchDef);
        if (bench.IsNull)
            return false;
        if (w.TryGet<Workbench>(bench, out var b) && b.Active is { } begun && b.By == me && !b.Untouched)
            return bot.Make(sim, me, view, bench, begun, now);
        var fletch = Recipe(sim, bench, Carpenters.Fletches);
        if (fletch == null || !Crafting.CanMake(sim, me, fletch))
            return false;
        if (fletch.Ingredients.TrueForAll(x => BotPlayer.Obtainable(sim, me, x)))
            return bot.Make(sim, me, view, bench, fletch, now);
        var horse = BotPlayer.Nearest(sim, me, Carpenters.SawhorseDef);
        if (horse.IsNull)
            return false;
        return SawRecipe(sim, me, horse) is { } saw ? bot.Make(sim, me, view, horse, saw, now) : GetALog(sim, me, view, now, "for planks", great: false);
    }

    /// <summary>
    /// The call to arms, answered from the woodshop: a bow of its own tillering hung on the muster
    /// rack (the town shoots as well as stabs), or a cudgel when there aren't the planks for a bow.
    /// False if it can do neither.
    /// </summary>
    private bool BowsForTheMuster(Simulation sim, Entity me, PerceptionView view, Entity bench, long now)
    {
        if (bot.RackSpare(sim, me, now) is { } racking)
            return racking;
        foreach (var which in new Func<RecipeDef, bool>[] { Carpenters.MakesBow, r => r.Name == Carpenters.CudgelRecipe })
            if (Recipe(sim, bench, which) is { } recipe && RecipeBooks.Knows(sim, me, recipe) && Crafting.CanMake(sim, me, recipe)
                && recipe.Ingredients.TrueForAll(x => BotPlayer.Obtainable(sim, me, x)))
            {
                bot.Doing = $"making a {recipe.Label} for the muster";
                return bot.Make(sim, me, view, bench, recipe, now);
            }
        return false;
    }

    /// <summary>
    /// The town's firewood running short (<see cref="Carpenters.FirewoodLow"/>): a log split at the
    /// chopping block if there's one to be had; else the nearest tree felled and its trunk bucked
    /// into logs, to split. True while at it.
    /// </summary>
    /// <summary>
    /// Firewood a workplace sent for (<see cref="FirewoodOrders"/>): a bundle or more on it, carried to that workplace's store and
    /// put in (F there); short of a bundle, more split at the block first, whatever the town's stock. True while at it.
    /// </summary>
    private bool FirewoodOrdered(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var orders = FirewoodOrders.Of(sim);
        if (orders.All.OrderBy(o => o.Id).FirstOrDefault() is not { } order || FirewoodOrders.StoreFor(sim, order.Building) is not { IsNull: false } store)
            return false;
        if (FirewoodOrders.Carried(sim, me) < FirewoodOrders.Bundle)
            return Firewood(sim, me, view, now, ordered: true);
        bot.Doing = "carrying firewood that was sent for";
        return bot.AtStation(sim, me, store, now, s =>
        {
            bot.Send(new AvatarWorkCommand(me, true, sim.World.Get<Thing>(s).Cell));
            bot.Send(new AvatarWorkCommand(me, false));
        });
    }

    private bool Firewood(Simulation sim, Entity me, PerceptionView view, long now, bool ordered = false)
    {
        var w = sim.World;
        var block = BotPlayer.Nearest(sim, me, Carpenters.BlockDef);
        if (block.IsNull || (!ordered && Carpenters.TownFirewood(sim) >= Carpenters.FirewoodLow))
            return false;
        if (w.TryGet<Workbench>(block, out var b) && b.Active is { } begun && b.By == me && !b.Untouched)
            return bot.Make(sim, me, view, block, begun, now);
        foreach (var split in w.Get<Thing>(block).Def.Recipes.Where(Carpenters.Splits))
            if (split.Ingredients.TrueForAll(x => BotPlayer.Obtainable(sim, me, x)))
            {
                bot.Doing = "splitting firewood";
                return bot.Make(sim, me, view, block, split, now);
            }
        return GetALog(sim, me, view, now, "for firewood", great: true);
    }

    /// <summary>
    /// A log to hand: one lying about picked up (one it bucked, or left by anyone); else a trunk down
    /// bucked into logs; else the nearest tree felled. False if there's no way to one (no axe, no tree).
    /// </summary>
    private bool GetALog(Simulation sim, Entity me, PerceptionView view, long now, string what, bool great)
    {
        var w = sim.World;
        foreach (var def in great ? new[] { Felling.LogDef, Felling.GreatLogDef } : [Felling.LogDef])
            if (BotPlayer.Known(sim, me, view, sim.Defs.Thing(def)) is { IsNull: false } lying)
            {
                bot.Doing = $"fetching a log {what}";
                return bot.Fetch(sim, me, view, lying, 1, now);
            }
        if (Crafting.ToolSpeed(sim, me, Felling.AxeKind) <= 0f)
            return false;
        var from = w.Get<Position>(me).Cell;
        // A trunk down already: saw it into logs. Else the nearest tree to fell.
        Entity Near(Func<Entity, bool> what) => sim.SpiralFrom(from, 40)
            .Where(c => sim.Map.InBounds(c))
            .Select(c => sim.Map.Structure[sim.Map.Idx(c)])
            .FirstOrDefault(e => !e.IsNull && what(e) && sim.Regions.CanReach(from, w.Get<Thing>(e).Cell, PathEnd.Touch), Entity.Null);
        var trunk = Near(e => Felling.IsTrunk(sim, e));
        if (!trunk.IsNull)
        {
            bot.Doing = "bucking a trunk into logs";
            return bot.AtStation(sim, me, trunk, now, t => bot.Send(new AvatarWorkCommand(me, true, w.Get<Thing>(t).Cell)));
        }
        var tree = Near(e => w.Get<Thing>(e).Def.Felling != null && !w.Has<Falling>(e));
        if (tree.IsNull)
            return false;
        bot.Doing = $"felling a tree {what}";
        return bot.AtStation(sim, me, tree, now, t => bot.Send(new AvatarWorkCommand(me, true, w.Get<Thing>(t).Cell)));
    }

    /// <summary>Felling: the aim left as it starts (away from the bot), the notch to the mark, round the back, the hinge thinned and not cut through.</summary>
    private (bool, bool, int) Fell(FellSession s)
    {
        var per = FellSession.DeepestBlow / Math.Max(0.3f, s.Girth);
        if (!s.Backing)
        {
            if (s.Notch >= FellSession.NotchMark - 0.02f && s.Raised <= 0f)
            {
                bot.Act = true;
                return (false, false, 0);
            }
            return (false, s.Raised * per < Math.Min(per, Math.Max(0.02f, FellSession.NotchMark - s.Notch)), 0);
        }
        return (false, s.Raised * per < Math.Min(per * 0.7f, Math.Max(0.015f, s.Hinge - FellSession.HingeFalls * 0.5f)), 0);
    }

    /// <summary>Bucking: steady strokes on the keys, a wedge in once the kerf starts to close.</summary>
    private (bool, bool, int) Buck(BuckSession s)
    {
        if (s.Wedging > 0f)
            return (false, false, 0);
        if (!s.Wedged && (s.Progress >= BuckSession.PinchFrom - 0.05f || s.Bound))
        {
            bot.Act = true;
            return (false, false, 0);
        }
        if (s.Stroke >= 0.97f)
            _sawWay = -1;
        else if (s.Stroke <= 0.03f)
            _sawWay = 1;
        bot.Axis2 = _sawWay;
        return (false, false, 0);
    }

    /// <summary>Nailing: the head kept over the nail, a light tap to set it, then full blows; a bent one drawn.</summary>
    private (bool, bool, int) Nail(NailSession s)
    {
        if (s.Bent)
        {
            bot.Act = true;
            return (false, false, 0);
        }
        if (s.Settling > 0)
            return (false, false, 0);
        var steer = MathF.Abs(s.Offset) > s.OnHeadWidth * 0.4f ? -Math.Sign(s.Offset) : 0;
        var want = !s.Set ? NailSession.SetMax * 0.6f : Math.Min(1f, (1f - s.Depth) / NailSession.DeepestDrive + 0.05f);
        // Hold off striking while it's off the head: keep it raised and bring it back.
        var holding = s.Raised < want || (s.Raised > 0f && steer != 0 && s.Raised < 1f);
        return (false, holding, steer);
    }

    /// <summary>Splitting: the edge over the check, raised past what it needs, let go; a stuck axe rocked free.</summary>
    private (bool, bool, int) Split(SplitSession s)
    {
        if (s.Stuck > 0)
        {
            bot.Act = true;
            return (false, false, 0);
        }
        if (s.Raised <= 0f && MathF.Abs(s.Aim - s.Check) > s.CheckWidthNow * 0.4f)
            return (false, false, Math.Sign(s.Check - s.Aim));
        return (false, s.Raised < s.Need + 0.1f, 0);
    }

    /// <summary>The plank recipe at the sawhorse it has the makings of (a log first, then timber), or null. Not a great log's: that wants a pitman.</summary>
    private static RecipeDef? SawRecipe(Simulation sim, Entity me, Entity horse) =>
        sim.World.Get<Thing>(horse).Def.Recipes.FirstOrDefault(r => Carpenters.Saws(r) && !r.TwoHanded && r.Ingredients.TrueForAll(x => BotPlayer.Obtainable(sim, me, x)));

    private static RecipeDef? Recipe(Simulation sim, Entity station, Func<RecipeDef, bool> which) =>
        sim.World.Get<Thing>(station).Def.Recipes.FirstOrDefault(which);

    /// <summary>The carpenter's games; null for a session that isn't one.</summary>
    private (bool Press, bool Held, int Axis)? PlayWoodshop(WorkSession s) => s switch
    {
        SawSession saw => Saw(saw),
        FletchSession fletch => Fletch(fletch),
        BowSession bow => Tiller(bow),
        JoinSession join => Join(join),
        FellSession fell => Fell(fell),
        BuckSession buck => Buck(buck),
        SplitSession split => Split(split),
        NailSession nail => Nail(nail),
        _ => null,
    };

    /// <summary>
    /// Joinery: chisel blows raised just enough to take the mortise to the mark, and called deep
    /// enough just short of it; then the tenon driven with blows well under what the joint will take.
    /// </summary>
    private (bool, bool, int) Join(JoinSession s)
    {
        if (s.Settling > 0)
            return (false, false, 0);
        if (s.Cutting)
        {
            if (s.Depth >= 1f - s.SnugWidth * 0.7f)
            {
                if (s.Raised <= 0f)
                    bot.Act = true;
                return (false, false, 0);
            }
            var want = Math.Clamp((1f - s.SnugWidth * 0.4f - s.Depth) / JoinSession.DeepestCut, 0.1f, 1f);
            return (false, s.Raised < want, 0);
        }
        return (false, s.Raised < JoinSession.SafeAt(s.Seat) * 0.7f, 0);
    }

    /// <summary>A planks count as a fetchable want.</summary>
    private static ThingCount Planks(Simulation sim, int count)
    {
        var want = new ThingCount { Def = Carpenters.PlanksDef, Count = count };
        want.ThingDef = sim.Defs.Thing(Carpenters.PlanksDef);
        return want;
    }

    /// <summary>
    /// Furniture burned in town: a new piece in its arms carried over and set where the old one
    /// stood; else one made at the bench (planks fetched from the chest first). False if neither.
    /// </summary>
    private bool Refurnish(Simulation sim, Entity me, PerceptionView view, Entity bench, long now)
    {
        var w = sim.World;
        if (!Refurnishing.Settled(sim))
            return false; // the town's still burning, or a crisis is on: rebuild after
        var held = Refurnishing.Held(sim, me);
        if (!held.IsNull)
        {
            var piece = w.Get<Furnishing>(held).Def.Name; // its own make, in its own arms
            if (view.Losses().Where(l => l.Piece == piece).Select(l => (KnownLoss?)l).FirstOrDefault() is not { } loss)
                return false;
            bot.Doing = $"setting a new {loss.Label} in the {loss.Where}";
            if (!AvatarSystem.InReach(w.Get<Position>(me).Cell, loss.Cell))
            {
                bot.Walk(sim, me, loss.Cell, now);
                return true;
            }
            bot.Send(new AvatarWorkCommand(me, true, loss.Cell));
            bot.Send(new AvatarWorkCommand(me, false));
            return true;
        }
        if (view.Losses().Select(l => (KnownLoss?)l).FirstOrDefault() is not { } lost)
            return false;
        if (BotPlayer.OnMe(sim, me, Carpenters.PlanksDef) < lost.Planks)
            return bot.Fetch(sim, me, view, now, Planks(sim, lost.Planks), "planks");
        bot.Doing = $"making a {lost.Label} to replace the one that burned";
        return bot.AtStation(sim, me, bench, now, b => bot.Send(new MakeFurnitureCommand(me, b, lost.Piece)));
    }

    /// <summary>
    /// A wagon's woodwork (the carpenter's order): out to it with a log for an axle or a plank for a
    /// board (fetched from the woodshop's chest first), and hewn or pegged there. False once it's sound.
    /// </summary>
    private bool JoinWagon(Simulation sim, Entity me, PerceptionView view, Entity owner, long now)
    {
        var w = sim.World;
        var wagon = Entity.Null;
        foreach (var e in w.Each<Tinkering.Wagon>())
            if (w.Get<Tinkering.Wagon>(e).Owner == owner)
                wagon = e;
        if (wagon.IsNull)
            return false;
        var seen = view.Wagon(wagon);
        var damage = seen?.Damage ?? (Tinkering.WagonDamage.Axle | Tinkering.WagonDamage.Board);
        if (seen is { } known && (known.Damage & (Tinkering.WagonDamage.Axle | Tinkering.WagonDamage.Board)) == 0)
            return false;
        var axle = (damage & Tinkering.WagonDamage.Axle) != 0;
        var board = (damage & Tinkering.WagonDamage.Board) != 0;
        // What it takes, from the shop on the way.
        if (axle && Tinkering.Wagons.WoodOn(sim, me) < Tinkering.Wagons.AxleWood)
        {
            var logs = new ThingCount { Def = Carpenters.WoodDef, Count = Tinkering.Wagons.AxleWood };
            logs.ThingDef = sim.Defs.Thing(Carpenters.WoodDef);
            return bot.Fetch(sim, me, view, now, logs, "a log");
        }
        if (board && !axle && Carpentry.WagonJoinery.PlanksOn(sim, me) < Tinkering.Wagons.BoardPlanks)
            return bot.Fetch(sim, me, view, now, Planks(sim, Tinkering.Wagons.BoardPlanks), "a plank");
        if (!AvatarSystem.InReach(sim, w.Get<Position>(me).Cell, wagon))
        {
            bot.Doing = "going out to a wagon";
            bot.Walk(sim, me, w.Get<Thing>(wagon).Cell, now);
            return true;
        }
        bot.Doing = axle ? "hewing a wagon a new axle" : "pegging a wagon's sprung board";
        bot.Send(axle ? new Carpentry.FitAxleCommand(me, wagon) : new Carpentry.PegBoardCommand(me, wagon));
        return true;
    }

    // Which way the saw's going (+1 pushing, −1 pulling).
    private int _sawWay = 1;

    /// <summary>
    /// Hewing: raise the axe for a blow that takes the side down near the line (a big one while
    /// there's a lot to come off, a light one to finish), then let go. Sawing: full steady strokes
    /// down and up on the keys, the blade steered back whenever it's off the line.
    /// </summary>
    private (bool, bool, int) Saw(SawSession s)
    {
        if (s.Hewing)
        {
            if (s.Turning > 0f)
                return (false, false, 0);
            // BotPlayer.Flat enough (or bitten past the line, nothing more to gain): turn the log.
            if (s.Proud[s.Side] <= s.FlatWidth && s.Raised <= 0f)
            {
                bot.Act = true;
                return (false, false, 0);
            }
            var want = Math.Min(SawSession.DeepestBlow, s.Proud[s.Side] - s.FlatWidth * 0.4f);
            return (false, SawSession.BlowDepth(s.Raised) < want, 0);
        }
        if (s.Stroke >= 0.97f)
            _sawWay = -1;
        else if (s.Stroke <= 0.03f)
            _sawWay = 1;
        bot.Axis2 = _sawWay;
        return (false, false, MathF.Abs(s.Offset) > s.OnLineWidth * 0.5f ? -Math.Sign(s.Offset) : 0);
    }

    // In which session: whether it's said the shaft's true (so one Space does it), the feather it last
    // set (so one F sets one), and its passes along the shaft with the knife.
    private long _fletchSession = -1;
    private bool _sighted;
    private int _feathered = -1;
    private int _passes, _knifeWay = 1;

    /// <summary>
    /// An arrow, stage by stage, by what the carpenter sees: bend the shaft till the needle reads true
    /// (long holds while it's well off, dabs near); shave in passes along the shaft with the edge just
    /// over its line, and call it done after a few; push the head on, keeping it in line; set the
    /// feathers a third of a turn apart.
    /// </summary>
    private (bool, bool, int) Fletch(FletchSession s)
    {
        if (s.StartTick != _fletchSession)
            (_fletchSession, _sighted, _feathered, _passes, _knifeWay) = (s.StartTick, false, -1, 0, 1);
        switch (s.Stage)
        {
            case ArrowStage.Sight:
            {
                if (_sighted)
                    return (false, false, 0);
                var seen = s.SeenWarp * s.BendDirection; // positive: still warped the way it came
                if (seen <= s.TrueWidth * 0.5f)
                {
                    if (bot.Slip)
                        bot.Slipped();
                    bot.Act = true;
                    _sighted = true;
                    return (false, false, 0);
                }
                if (seen > 0.15f)
                    return (false, true, 0);
                bot.Working = (bot.Working + 1) % 3;
                return (false, bot.Working == 0, 0);
            }
            case ArrowStage.Shave:
            {
                if (_passes >= 4)
                {
                    bot.Act = true;
                    return (false, false, 0);
                }
                // The edge a hair over the line, then along the shaft and back.
                var wantY = FletchSession.LineV - 0.03f * FletchSession.HeightV;
                bot.Axis2 = MathF.Abs(s.KnifeY - wantY) <= FletchSession.KnifeSpeed ? 0 : Math.Sign(wantY - s.KnifeY);
                if (bot.Axis2 != 0)
                    return (false, true, 0);
                if (s.KnifeX >= 0.99f && _knifeWay > 0 || s.KnifeX <= 0.01f && _knifeWay < 0)
                {
                    _knifeWay = -_knifeWay;
                    _passes++;
                }
                return (false, true, _knifeWay);
            }
            case ArrowStage.Head:
                return (false, true, MathF.Abs(s.Tilt) > s.InLineWidth * 0.4f ? -Math.Sign(s.Tilt) : 0);
            case ArrowStage.Fletch:
            {
                if (_feathered == s.Set || s.Set >= FletchSession.Feathers)
                    return (false, false, 0);
                if (s.Set == 0)
                {
                    _feathered = 0;
                    return (true, false, 0);
                }
                var want = s.FeatherAt[0] + s.Set / (float)FletchSession.Feathers;
                want -= MathF.Floor(want);
                var d = want - s.Aim;
                d -= MathF.Round(d); // the short way round
                if (MathF.Abs(d) <= FletchSession.AimPerStep * 0.6f)
                {
                    _feathered = s.Set;
                    return (true, false, 0);
                }
                return (false, false, Math.Sign(d));
            }
        }
        return (false, false, 0);
    }

    /// <summary>Tiller the stiffest section first, a short stroke at a time, draw it to full once every section's near even, then string it.</summary>
    private (bool, bool, int) Tiller(BowSession s)
    {
        if (s.Stringing)
        {
            // Bend it a little past the catch, slip the loop on, and never into the strain.
            if (s.Flex >= s.CatchLow + 0.05f)
            {
                bot.Act = true;
                return (false, false, 0);
            }
            return (false, true, 0);
        }
        var stiffest = 0;
        for (var i = 1; i < BowSession.Sections; i++)
            if (s.Stiffness[i] > s.Stiffness[stiffest])
                stiffest = i;
        if (s.Stiffness[stiffest] <= BowSession.Even + s.RightWidth * 0.7f)
        {
            bot.Act = true;
            return (false, false, 0);
        }
        if (s.Selected != stiffest)
            return (false, false, bot.Working++ % 2 == 0 ? Math.Sign(BowSession.ColumnOf(stiffest) - BowSession.ColumnOf(s.Selected)) : 0); // a press a section
        bot.Working = (bot.Working + 1) % 20;
        return (false, bot.Working < 12, 0);
    }
}
