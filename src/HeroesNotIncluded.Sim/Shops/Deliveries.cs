using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Shops;

/// <summary>
/// The carter from the hill mines: every morning of a round after the first, at
/// <see cref="DeliveryHour"/>, leaves what <see cref="Defs.SeasonDef.OreDelivery"/> lists (ore to
/// smelt, and charcoal) in the smithy's chest, and beside it what won't fit. The smith starts a
/// round with only a couple of bars, so the morning's ore is what keeps the forge busy before the
/// first adventurers come in. Nothing happens outside a round.
/// </summary>
public sealed class OreCarterSystem : ISimSystem, ISaveState
{
    public const int DeliveryHour = 6;
    public const string SmithyDef = "Smithy";

    public string Name => "OreCarter";
    public int Interval => SimClock.TicksPerHour / 4;

    private int _lastDay = -1;

    public void Update(Simulation sim)
    {
        if (!sim.Round.Live || sim.Clock.Day == _lastDay)
            return;
        // The round's first day is stocked by the deal (VillageRound.StockSmithy): the carter comes from the next.
        if (_lastDay < 0)
        {
            _lastDay = sim.Clock.Day;
            return;
        }
        if (sim.Clock.HourOfDay < DeliveryHour)
            return;
        _lastDay = sim.Clock.Day;
        Deliver(sim);
    }

    /// <summary>Leave the morning's load at each smithy (there's one in Millbrook).</summary>
    public static void Deliver(Simulation sim)
    {
        var load = sim.Defs.Season.OreDelivery;
        if (load.Count == 0)
            return;
        var w = sim.World;
        foreach (var smithy in w.Query<PresetBuilding>().Where(e => w.Get<PresetBuilding>(e).Def.Name == SmithyDef).OrderBy(e => e.Index))
        {
            var chest = Buildings.Parts(sim, smithy).Where(e => w.Get<Thing>(e).Def.Name == "Chest")
                .OrderBy(e => e.Index).DefaultIfEmpty(Entity.Null).First();
            if (chest.IsNull)
                continue;
            var cell = w.Get<Thing>(chest).Cell;
            foreach (var t in load)
            {
                var item = sim.CreateItem(t.ThingDef, t.Count, t.MaterialDef);
                var rest = sim.Store(chest, item);
                if (!rest.IsNull)
                    sim.PlaceItemNear(rest, cell);
            }
            sim.Messages.Add(MessageKind.Info, "The carter's left a load of ore at the smithy.", cell);
        }
    }

    public void Save(SaveWriter w, Simulation sim) => w.Int(_lastDay);

    public void Load(SaveReader r, Simulation sim) => _lastDay = r.Int();
}
