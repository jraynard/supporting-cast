using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>
/// Every job for two pairs of hands, ready at once (<c>--pairs-test</c>; docs/DESIGN.md, "Two pairs
/// of hands"), for trying them in co-op: a greatsword's iron by the forge (the striker), a great log's
/// wood by the sawhorse (the pitman), a party's wagon with a cracked axle by the woodshop and a log to
/// hew it from (the lifter), and a villager lying dead on a shrine pallet, with the empath's mana full
/// (the keeper). The work's begun the usual way, by whoever holds the trade; anyone else lends a hand
/// with F where it's going on, and alone Help! from the quick-say wheel brings an AI villager.
/// Staged through <see cref="DevPairsDrillCommand"/> once the round's dealt, so every peer stages the same.
/// </summary>
public static class PairsDrill
{
    /// <summary>Stage it all. Call once the round's dealt; what couldn't be staged (no such building on this map) is left out.</summary>
    public static List<string> Stage(Simulation sim)
    {
        var staged = new List<string>();
        var w = sim.World;

        // The striker: a greatsword's iron and leather by the forge, the recipe known to all.
        RecipeBooks.Discover(sim, Entity.Null, "handle:IronBar");
        if (Station(sim, TownRole.Forge, "Forge") is { IsNull: false } forge)
        {
            Lay(sim, forge, "IronBar", 5);
            Lay(sim, forge, "Leather", 1);
            Work.Kindling.LightAll(sim); // the work for two, not the morning's lighting
            staged.Add("a greatsword's iron by the forge (the smith makes it; someone takes the sledge)");
        }

        // Each trade's own work for two (the carpenter's great log and wagon axle).
        foreach (var module in Modding.Trades.All(sim))
            staged.AddRange(module.PairsDrill(sim));

        // The keeper: a villager dead on a shrine pallet, and every empath's mana full.
        if (Station(sim, TownRole.Shrine, "StrawPallet") is { IsNull: false } pallet && Victim(sim) is { IsNull: false } victim)
        {
            FireDrill.MoveTo(sim, victim, w.Get<Thing>(pallet).Cell);
            NeedsSystem.Kill(sim, victim, ref w.Get<Creature>(victim), "was found dead");
            foreach (var e in w.Query<Mana>())
                w.Get<Mana>(e).Current = w.Get<Mana>(e).Max;
            staged.Add($"{w.Get<Creature>(victim).Name} dead on a shrine pallet (the empath calls them back; someone holds them)");
        }
        sim.Messages.Add(MessageKind.Info, staged.Count == 0
            ? "The pairs drill found nothing to stage on this map."
            : "Work for two pairs of hands, ready: " + string.Join("; ", staged) + ". Lend a hand with F, or call Help!.");
        return staged;
    }

    /// <summary>The first piece of furniture of this def in the building of this role, Entity.Null if none.</summary>
    public static Entity StationOf(Simulation sim, TownRole role, string def) => Station(sim, role, def);

    /// <summary>Stand someone where they can reach a station (on its level), as if they'd walked up to it.</summary>
    public static void StandBy(Simulation sim, Entity who, Entity station)
    {
        var at = sim.World.Get<Thing>(station).Cell;
        var cell = sim.SpiralFrom(at, 4).FirstOrDefault(c => c != at && c.Z == at.Z && sim.Map.InBounds(c) && sim.Map.Passable(c)
            && AvatarSystem.InReach(sim, c, station) && !sim.Creatures().Any(e => e != who && sim.World.Get<Position>(e).Cell == c), at);
        if (cell != at)
            FireDrill.MoveTo(sim, who, cell);
    }

    private static Entity Station(Simulation sim, TownRole role, string def)
    {
        var building = Shops.ShopSystem.BuildingFor(sim, role);
        return building.IsNull ? Entity.Null : Systems.Stock.StationOf(sim, building, def);
    }

    /// <summary>Some of a thing set down beside a station.</summary>
    public static void Lay(Simulation sim, Entity station, string def, int count) =>
        sim.PlaceItemNear(sim.CreateItem(sim.Defs.Thing(def), count), sim.World.Get<Thing>(station).Cell);

    /// <summary>A party's wagon, its axle cracked, on open ground near the building; Entity.Null if there's nowhere.</summary>
    public static Entity Wagon(Simulation sim, Entity near)
    {
        var def = sim.Defs.Thing(Wagons.Def);
        var at = sim.World.TryGet<PresetBuilding>(near, out var building) ? building.Anchor : sim.LocationOf(near);
        foreach (var c in sim.SpiralFrom(at, 14))
        {
            if (Maps.Map.Distance(c, at) < 4 || !sim.CanPlaceStructure(c, def, 0)
                || !sim.FootprintCells(def, c, 0).All(f => sim.Map.Passable(f) && sim.Map.Item[sim.Map.Idx(f)].IsNull && Buildings.At(sim, f).IsNull))
                continue;
            var wagon = sim.SpawnStructure(def, c);
            sim.World.Add(wagon, new Wagon { Party = -1, Owner = Entity.Null, Damage = WagonDamage.Axle, WheelSeed = 77, MendedBy = Entity.Null });
            return wagon;
        }
        return Entity.Null;
    }

    /// <summary>Who's found dead: the first AI villager of the town who isn't an empath, Entity.Null if none.</summary>
    public static Entity Victim(Simulation sim)
    {
        var w = sim.World;
        return sim.Creatures().Where(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human, Dead: false }
                && !w.Has<PlayerCharacter>(e) && !w.Has<Avatar>(e) && !w.Has<Mana>(e) && !w.Has<Adventurer>(e))
            .OrderBy(e => e.Index).FirstOrDefault(Entity.Null);
    }
}

/// <summary>
/// A job for two played as the second player, alone (<c>--help-as KIND</c>): a bot leads it (its <see cref="Bots.BotPlayer.WaitsForHand"/>,
/// so no villager's called), and the player stands by to lend a hand. The game's own kinds are the striker (the
/// smith's greatsword) and the keeper (the empath's Calling); a trade's module adds its own (<see cref="Modding.ITradeModule.PairsKinds"/>).
/// </summary>
public static class PairsLead
{
    public const string Striker = "striker", Keeper = "keeper";

    /// <summary>Every kind that can be played this way: the game's, then each trade module's.</summary>
    public static IEnumerable<string> Kinds(Simulation sim) => [Striker, Keeper, .. Modding.Trades.All(sim).SelectMany(m => m.PairsKinds)];

    /// <summary>The trade that leads a kind of work for two, null if none here.</summary>
    public static string? LeadTrade(Simulation sim, string kind) => kind switch
    {
        Striker => "Blacksmith",
        Keeper => "Empath",
        _ => Modding.Trades.All(sim).FirstOrDefault(m => m.PairsKinds.Contains(kind))?.Trade,
    };

    /// <summary>
    /// The work set out with <paramref name="lead"/> at it (or about to be) and <paramref name="helper"/> beside it. Returns
    /// what was staged, for the message; null if this kind can't be staged here.
    /// </summary>
    public static string? Stage(Simulation sim, string kind, Entity lead, Entity helper)
    {
        var w = sim.World;
        if (!w.IsAlive(lead) || !w.IsAlive(helper) || lead == helper || !w.Has<Creature>(lead) || !w.Has<Creature>(helper)
            || w.Get<Creature>(lead).Dead || w.Get<Creature>(helper).Dead)
            return null;
        // Whatever either was about is put down: a bot's first thought (the kiln, a walk) mustn't stand in the drill's way.
        foreach (var e in new[] { lead, helper })
        {
            if (w.Has<Work.WorkSession>(e))
                Work.WorkSystem.Stop(sim, e, null);
            if (w.TryGet<Mind>(e, out var mind) && mind.Job != null)
                AiSystem.EndJob(sim, e, mind, false);
            if (w.TryGet<Mover>(e, out var mover))
                mover.Stop();
        }
        switch (kind)
        {
            case Striker:
            {
                var forge = PairsDrill.StationOf(sim, TownRole.Forge, "Forge");
                var anvil = PairsDrill.StationOf(sim, TownRole.Forge, "Anvil");
                if (forge.IsNull || anvil.IsNull || sim.Defs.AllRecipes.FirstOrDefault(r => r.TwoHanded && r.StationDef == w.Get<Thing>(forge).Def) is not { } great)
                    return null;
                // Begun as F at the forge begins it: the recipe known, the iron and leather taken from the pack.
                if (great.Unlock is { Discover.Length: > 0 } unlock)
                    RecipeBooks.Discover(sim, Entity.Null, unlock.Discover);
                PairsDrill.StandBy(sim, lead, forge);
                Work.Kindling.LightAll(sim);
                // The forge lit: a heat ends at once in a cold one (and the billet with it).
                if (w.TryGet<Fueled>(forge, out _))
                    w.Get<Fueled>(forge).Fuel = Math.Max(w.Get<Fueled>(forge).Fuel, 5f);
                else
                    w.Add(forge, new Fueled { Fuel = 5f });
                // Anything else the smith had begun (a bot's first thought: a spear for the shelf) is put by, the forge freed.
                if (Work.Smithing.PieceOf(sim, lead) is { } begun && !begun.Recipe.TwoHanded)
                {
                    w.Remove<Work.WorkPiece>(lead);
                    if (w.Get<Workbench>(forge).By == lead)
                        w.Get<Workbench>(forge).Clear();
                }
                if (Work.Smithing.PieceOf(sim, lead) == null)
                {
                    foreach (var need in great.Ingredients)
                        Inventory.Stow(sim, lead, sim.CreateItem(need.ThingDef, need.Count, Simulation.MaterialFor(need.ThingDef, null)));
                    new CraftCommand(lead, forge, great).Apply(sim);
                }
                if (Work.Smithing.PieceOf(sim, lead) is not { } piece || !piece.Recipe.TwoHanded)
                    return null;
                PairsDrill.StandBy(sim, helper, anvil);
                return $"the {great.Label}'s billet in the smith's tongs: they'll heat it and wait at the anvil. Take the sledge there with F";
            }
            case Keeper:
            {
                var pallet = PairsDrill.StationOf(sim, TownRole.Shrine, "StrawPallet");
                if (pallet.IsNull || PairsDrill.Victim(sim) is not { IsNull: false } victim || !w.Has<Mana>(lead))
                    return null;
                FireDrill.MoveTo(sim, victim, w.Get<Thing>(pallet).Cell);
                NeedsSystem.Kill(sim, victim, ref w.Get<Creature>(victim), "was found dead");
                w.Get<Mana>(lead).Current = w.Get<Mana>(lead).Max;
                PairsDrill.StandBy(sim, lead, pallet);
                PairsDrill.StandBy(sim, helper, pallet);
                return $"{w.Get<Creature>(victim).Name} dead on a shrine pallet: the empath calls them back. Hold them with F there";
            }
        }
        foreach (var module in Modding.Trades.All(sim))
            if (module.PairsKinds.Contains(kind) && module.PairsLead(sim, kind, lead, helper) is { } staged)
                return staged;
        return null;
    }
}

/// <summary>Debug (<c>--help-as</c>): a job for two set out, <see cref="Lead"/> (a bot) at it and <see cref="Helper"/> by it (<see cref="PairsLead"/>).</summary>
public sealed record DevPairsLeadCommand(string Kind, Entity Lead, Entity Helper) : IDevCommand
{
    public void Apply(Simulation sim)
    {
        if (!sim.Round.Live || Kind is not { Length: > 0 and < 32 })
            return;
        var staged = PairsLead.Stage(sim, Kind, Lead, Helper);
        sim.Messages.Add(MessageKind.Info, staged == null ? $"Nothing to stage for \"{Kind}\" here." : $"Work for two: {staged}.");
    }
}

/// <summary>Debug (<c>--pairs-test</c>): stage every job for two pairs of hands (<see cref="PairsDrill"/>).</summary>
public sealed record DevPairsDrillCommand : IDevCommand
{
    public void Apply(Simulation sim)
    {
        if (sim.Round.Live)
            PairsDrill.Stage(sim);
    }
}
