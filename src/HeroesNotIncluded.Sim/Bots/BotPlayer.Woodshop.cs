using HeroesNotIncluded.Sim.Carpentry;
using HeroesNotIncluded.Sim.Components;
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
        if (ShowGoods(sim, me, view, now))
            return true;
        var bench = Nearest(sim, me, Carpenters.BenchDef);
        var horse = Nearest(sim, me, Carpenters.SawhorseDef);
        // Something already begun at a station is seen through first.
        foreach (var station in new[] { bench, horse })
            if (!station.IsNull && w.TryGet<Workbench>(station, out var b) && b.Active is { } begun && b.By == me && !b.Untouched)
                return Make(sim, me, view, station, begun, now);
        if (Stock(sim, me) >= StockWanted || bench.IsNull)
            return false;
        var fletch = Recipe(sim, bench, Carpenters.Fletches);
        if (fletch == null || !Crafting.CanMake(sim, me, fletch))
            return false;
        if (fletch.Ingredients.TrueForAll(x => Obtainable(sim, me, x)))
            return Make(sim, me, view, bench, fletch, now);
        // Short of planks: saw some, if there are logs.
        var saw = horse.IsNull ? null : Recipe(sim, horse, Carpenters.Saws);
        var planks = fletch.Ingredients.Find(x => x.Def == Carpenters.PlanksDef);
        if (saw == null || planks == null || Obtainable(sim, me, planks) || !saw.Ingredients.TrueForAll(x => Obtainable(sim, me, x)))
            return false;
        return Make(sim, me, view, horse, saw, now);
    }

    private static RecipeDef? Recipe(Simulation sim, Entity station, Func<RecipeDef, bool> which) =>
        sim.World.Get<Thing>(station).Def.Recipes.FirstOrDefault(which);

    /// <summary>The carpenter's games; null for a session that isn't one.</summary>
    private (bool Press, bool Held, int Axis)? PlayWoodshop(WorkSession s) => s switch
    {
        SawSession saw => Saw(saw),
        FletchSession fletch => Fletch(fletch),
        BowSession bow => Tiller(bow),
        _ => null,
    };

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
