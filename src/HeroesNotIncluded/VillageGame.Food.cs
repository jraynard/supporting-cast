using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded;

// Hungry, a pointer to the nearest food you know of: your own cottage's bread crock (if there's
// anything in it), or the inn's serving hatch. A round with nobody playing the cook is no reason to
// go without. Nothing hidden: your home and the town's inn are things every villager knows.
public sealed partial class VillageGame
{
    /// <summary>A tag over the nearest food when you're hungry and have none on you; after the errands' tags, which clear the list.</summary>
    private void UpdateFoodPointer()
    {
        if (!HasAvatar || !_sim.Round.Live || Working || !_sim.World.TryGet<Needs>(_avatar, out var needs))
            return;
        var food = _sim.Defs.Need("Food");
        if (needs.Get(food) >= food.SeekBelow || _sim.World.Get<Mind>(_avatar).Job is EatHeldJob || HasFoodOnYou())
            return;
        var w = _sim.World;
        var me = w.Get<Position>(_avatar).Cell;
        Entity best = Entity.Null;
        var bestD = int.MaxValue;
        string? text = null;
        void Consider(Entity e, string label)
        {
            var d = Sim.Maps.Map.Distance(me, w.Get<Thing>(e).Cell);
            if (d < bestD)
                (best, bestD, text) = (e, d, label);
        }
        var home = Homes.HomeOf(_sim, _avatar);
        if (!home.IsNull)
            foreach (var part in Buildings.Parts(_sim, home))
                if (Homes.IsCrock(_sim, part) && _sim.StoredIn(part).Any())
                    Consider(part, "Food at home");
        var inn = ShopSystem.BuildingOf(_sim, _sim.Defs.Building("Inn"));
        if (Hatch.Of(_sim, inn) is { IsNull: false } hatch && ShopPrices.Posted(_sim, inn, ServiceKind.Meal) is { Offered: true }
            && !(w.TryGet<Trade>(_avatar, out var trade) && trade.Def.WorkplaceDef == w.Get<PresetBuilding>(inn).Def))
            Consider(hatch, Hatch.DishesOn(_sim, hatch, ServiceKind.Meal).Count > 0 ? "A hot meal, ready" : "Meals at the hatch");
        if (best.IsNull || bestD <= 1)
            return;
        var cell = w.Get<Thing>(best).Cell;
        _renderer.Pointers.Add(new(Position.CenterOf(cell) + WorldScale.Rise(1.4f), best, text!));
    }

    /// <summary>Something you could eat in your hands or pack.</summary>
    private bool HasFoodOnYou()
    {
        var w = _sim.World;
        var eater = w.Get<Creature>(_avatar).Def;
        bool Edible(Entity e) => w.IsAlive(e) && w.Get<Thing>(e).Def is { IsFood: true } d && d.EdibleBy(eater);
        if (Edible(w.Get<Carrying>(_avatar).Item))
            return true;
        var pack = _sim.PackOf(_avatar);
        return !pack.IsNull && _sim.StoredIn(pack).Any(Edible);
    }
}
