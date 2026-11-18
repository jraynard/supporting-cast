using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Carpentry;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Bots;

/// <summary>The carpenter: quivers fletched for the shelf, and planks sawn when there are none to fletch from.</summary>
public sealed partial class BotPlayer
{
    /// <summary>
    /// The carpenter between orders: its quivers on show; a quiver fletched while the stock's short;
    /// planks sawn first if there are none to split shafts from. True while at it.
    /// </summary>
    private bool Woodshop(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(me, out var trade) || !trade.Def.Stations.Contains(Carpenters.BenchDef) || MyShop(sim, me) is not { IsNull: false })
            return false;
        // In a call to arms, a bow of its own goes to the muster rack before it goes on the shelf.
        if (sim.Militia.Paying && RackSpare(sim, me, now) == true)
            return true;
        if (ShowGoods(sim, me, view, now))
            return true;
        var bench = Nearest(sim, me, Carpenters.BenchDef);
        var horse = Nearest(sim, me, Carpenters.SawhorseDef);
        // Something already begun at a station is seen through first.
        foreach (var station in new[] { bench, horse })
            if (!station.IsNull && w.TryGet<Workbench>(station, out var b) && b.Active is { } begun && b.By == me && !b.Untouched)
                return Make(sim, me, view, station, begun, now);
        if (sim.Militia.Paying && !bench.IsNull && BowsForTheMuster(sim, me, view, bench, now))
            return true;
        if (!bench.IsNull && Refurnish(sim, me, view, bench, now))
            return true;
        if (Firewood(sim, me, view, now))
            return true;
        if (Stock(sim, me) >= StockWanted || bench.IsNull)
            return false;
        var fletch = Recipe(sim, bench, Carpenters.Fletches);
        if (fletch == null || !Crafting.CanMake(sim, me, fletch))
            return false;
        if (fletch.Ingredients.TrueForAll(x => Obtainable(sim, me, x)))
            return Make(sim, me, view, bench, fletch, now);
        // Short of planks: saw a log (the chest's, or one it fetches, bucks or fells), or timber at a pinch.
        var planks = fletch.Ingredients.Find(x => x.Def == Carpenters.PlanksDef);
        if (horse.IsNull || planks == null || Obtainable(sim, me, planks))
            return false;
        if (SawRecipe(sim, me, horse) is { } saw)
            return Make(sim, me, view, horse, saw, now);
        return GetALog(sim, me, view, now, "for planks", great: false);
    }

    /// <summary>
    /// A piece mended at its maker's bench (a bow at the carpenter's, a crossbow at the tinker's):
    /// fetched from its owner, the wood or iron it takes brought from the shop's chest, worked at the
    /// bench until it looks sound, and handed back.
    /// </summary>
    private bool BenchMend(Simulation sim, Entity me, PerceptionView view, Entity owner, long now, string benchDef)
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
            Doing = "bringing a quiver";
            if (Reach(sim, me, archer, now, returning: true))
                Send(new GiveCommand(me, archer, mine, 1));
            return true;
        }
        var bench = Nearest(sim, me, Carpenters.BenchDef);
        if (bench.IsNull)
            return false;
        if (w.TryGet<Workbench>(bench, out var b) && b.Active is { } begun && b.By == me && !b.Untouched)
            return Make(sim, me, view, bench, begun, now);
        var fletch = Recipe(sim, bench, Carpenters.Fletches);
        if (fletch == null || !Crafting.CanMake(sim, me, fletch))
            return false;
        if (fletch.Ingredients.TrueForAll(x => Obtainable(sim, me, x)))
            return Make(sim, me, view, bench, fletch, now);
        var horse = Nearest(sim, me, Carpenters.SawhorseDef);
        if (horse.IsNull)
            return false;
        return SawRecipe(sim, me, horse) is { } saw ? Make(sim, me, view, horse, saw, now) : GetALog(sim, me, view, now, "for planks", great: false);
    }

    /// <summary>
    /// The call to arms, answered from the woodshop: a bow of its own tillering hung on the muster
    /// rack (the town shoots as well as stabs), or a cudgel when there aren't the planks for a bow.
    /// False if it can do neither.
    /// </summary>
    private bool BowsForTheMuster(Simulation sim, Entity me, PerceptionView view, Entity bench, long now)
    {
        if (RackSpare(sim, me, now) is { } racking)
            return racking;
        foreach (var which in new Func<RecipeDef, bool>[] { Carpenters.MakesBow, r => r.Name == Carpenters.CudgelRecipe })
            if (Recipe(sim, bench, which) is { } recipe && RecipeBooks.Knows(sim, me, recipe) && Crafting.CanMake(sim, me, recipe)
                && recipe.Ingredients.TrueForAll(x => Obtainable(sim, me, x)))
            {
                Doing = $"making a {recipe.Label} for the muster";
                return Make(sim, me, view, bench, recipe, now);
            }
        return false;
    }

    /// <summary>
    /// The town's firewood running short (<see cref="Carpenters.FirewoodLow"/>): a log split at the
    /// chopping block if there's one to be had; else the nearest tree felled and its trunk bucked
    /// into logs, to split. True while at it.
    /// </summary>
    private bool Firewood(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        var block = Nearest(sim, me, Carpenters.BlockDef);
        if (block.IsNull || Carpenters.TownFirewood(sim) >= Carpenters.FirewoodLow)
            return false;
        if (w.TryGet<Workbench>(block, out var b) && b.Active is { } begun && b.By == me && !b.Untouched)
            return Make(sim, me, view, block, begun, now);
        foreach (var split in w.Get<Thing>(block).Def.Recipes.Where(Carpenters.Splits))
            if (split.Ingredients.TrueForAll(x => Obtainable(sim, me, x)))
            {
                Doing = "splitting firewood";
                return Make(sim, me, view, block, split, now);
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
            if (Known(sim, me, view, sim.Defs.Thing(def)) is { IsNull: false } lying)
            {
                Doing = $"fetching a log {what}";
                return Fetch(sim, me, view, lying, 1, now);
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
            Doing = "bucking a trunk into logs";
            return AtStation(sim, me, trunk, now, t => Send(new AvatarWorkCommand(me, true, w.Get<Thing>(t).Cell)));
        }
        var tree = Near(e => w.Get<Thing>(e).Def.Felling != null && !w.Has<Falling>(e));
        if (tree.IsNull)
            return false;
        Doing = $"felling a tree {what}";
        return AtStation(sim, me, tree, now, t => Send(new AvatarWorkCommand(me, true, w.Get<Thing>(t).Cell)));
    }

    /// <summary>Felling: the aim left as it starts (away from the bot), the notch to the mark, round the back, the hinge thinned and not cut through.</summary>
    private (bool, bool, int) Fell(FellSession s)
    {
        var per = FellSession.DeepestBlow / Math.Max(0.3f, s.Girth);
        if (!s.Backing)
        {
            if (s.Notch >= FellSession.NotchMark - 0.02f && s.Raised <= 0f)
            {
                _act = true;
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
            _act = true;
            return (false, false, 0);
        }
        if (s.Stroke >= 0.97f)
            _sawWay = -1;
        else if (s.Stroke <= 0.03f)
            _sawWay = 1;
        _axis2 = _sawWay;
        return (false, false, 0);
    }

    /// <summary>Nailing: the head kept over the nail, a light tap to set it, then full blows; a bent one drawn.</summary>
    private (bool, bool, int) Nail(NailSession s)
    {
        if (s.Bent)
        {
            _act = true;
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
            _act = true;
            return (false, false, 0);
        }
        if (s.Raised <= 0f && MathF.Abs(s.Aim - s.Check) > s.CheckWidthNow * 0.4f)
            return (false, false, Math.Sign(s.Check - s.Aim));
        return (false, s.Raised < s.Need + 0.1f, 0);
    }

    /// <summary>The plank recipe at the sawhorse it has the makings of (a log first, then timber), or null. Not a great log's: that wants a pitman.</summary>
    private static RecipeDef? SawRecipe(Simulation sim, Entity me, Entity horse) =>
        sim.World.Get<Thing>(horse).Def.Recipes.FirstOrDefault(r => Carpenters.Saws(r) && !r.TwoHanded && r.Ingredients.TrueForAll(x => Obtainable(sim, me, x)));

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
                    _act = true;
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
            Doing = $"setting a new {loss.Label} in the {loss.Where}";
            if (!AvatarSystem.InReach(w.Get<Position>(me).Cell, loss.Cell))
            {
                Walk(sim, me, loss.Cell, now);
                return true;
            }
            Send(new AvatarWorkCommand(me, true, loss.Cell));
            Send(new AvatarWorkCommand(me, false));
            return true;
        }
        if (view.Losses().Select(l => (KnownLoss?)l).FirstOrDefault() is not { } lost)
            return false;
        if (OnMe(sim, me, Carpenters.PlanksDef) < lost.Planks)
            return Fetch(sim, me, view, now, Planks(sim, lost.Planks), "planks");
        Doing = $"making a {lost.Label} to replace the one that burned";
        return AtStation(sim, me, bench, now, b => Send(new MakeFurnitureCommand(me, b, lost.Piece)));
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
            return Fetch(sim, me, view, now, logs, "a log");
        }
        if (board && !axle && Carpentry.WagonJoinery.PlanksOn(sim, me) < Tinkering.Wagons.BoardPlanks)
            return Fetch(sim, me, view, now, Planks(sim, Tinkering.Wagons.BoardPlanks), "a plank");
        if (!AvatarSystem.InReach(sim, w.Get<Position>(me).Cell, wagon))
        {
            Doing = "going out to a wagon";
            Walk(sim, me, w.Get<Thing>(wagon).Cell, now);
            return true;
        }
        Doing = axle ? "hewing a wagon a new axle" : "pegging a wagon's sprung board";
        Send(axle ? new Carpentry.FitAxleCommand(me, wagon) : new Carpentry.PegBoardCommand(me, wagon));
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
            // Flat enough (or bitten past the line, nothing more to gain): turn the log.
            if (s.Proud[s.Side] <= s.FlatWidth && s.Raised <= 0f)
            {
                _act = true;
                return (false, false, 0);
            }
            var want = Math.Min(SawSession.DeepestBlow, s.Proud[s.Side] - s.FlatWidth * 0.4f);
            return (false, SawSession.BlowDepth(s.Raised) < want, 0);
        }
        if (s.Stroke >= 0.97f)
            _sawWay = -1;
        else if (s.Stroke <= 0.03f)
            _sawWay = 1;
        _axis2 = _sawWay;
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
                    if (_slip)
                        Slipped();
                    _act = true;
                    _sighted = true;
                    return (false, false, 0);
                }
                if (seen > 0.15f)
                    return (false, true, 0);
                _working = (_working + 1) % 3;
                return (false, _working == 0, 0);
            }
            case ArrowStage.Shave:
            {
                if (_passes >= 4)
                {
                    _act = true;
                    return (false, false, 0);
                }
                // The edge a hair over the line, then along the shaft and back.
                var wantY = FletchSession.LineV - 0.03f * FletchSession.HeightV;
                _axis2 = MathF.Abs(s.KnifeY - wantY) <= FletchSession.KnifeSpeed ? 0 : Math.Sign(wantY - s.KnifeY);
                if (_axis2 != 0)
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
                _act = true;
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
            _act = true;
            return (false, false, 0);
        }
        if (s.Selected != stiffest)
            return (false, false, _working++ % 2 == 0 ? Math.Sign(BowSession.ColumnOf(stiffest) - BowSession.ColumnOf(s.Selected)) : 0); // a press a section
        _working = (_working + 1) % 20;
        return (false, _working < 12, 0);
    }
}
