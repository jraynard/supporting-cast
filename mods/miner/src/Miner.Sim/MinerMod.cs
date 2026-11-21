using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Miner;

/// <summary>
/// The miner's sim half (docs/MODDING.md): the trade's hooks, its bot and its drill. The mine itself, the
/// hill east of Millbrook with its adit and its sure seams of iron and coal, is the map's
/// (<c>MapDef.Tunnels</c>, <c>MapDef.Seams</c>), so it's there whether or not anyone takes the trade.
/// </summary>
public sealed class MinerMod : ISimMod
{
    public void Register(SimRegistry registry)
    {
        registry.Trade(new MinerModule());
        registry.Lesson([.. MinerLessons.All]);
        registry.Bot(bot => new MinerBot(bot));
        registry.System(() => new ChalkMarks());
        registry.System(() => new BarrowSystem());
        registry.System(() => new OreTally());
        registry.Component<BarrowLoad>("BarrowLoad", Barrows.Write, Barrows.Read);
        // --drill hewing: stood in the mine at a face, pick in hand, the hewing begun. --drill sounding: the same, the hammer
        // in hand. --drill hauling: a pile of hewn ore at the face and a barrow beside it, the loading begun.
        registry.Drill("hewing", HewDrill.Stage);
        registry.Drill("sounding", HewDrill.StageSounding);
        registry.Drill("hauling", HewDrill.StageHauling);
    }
}

/// <summary>
/// The miner's hooks (<see cref="ITradeModule"/>): F at a rock face hews it (<see cref="HewSession"/>), or with the
/// sounding hammer in hand sounds it (<see cref="SoundSession"/>). The barrow: F at one set down with ore in reach loads
/// it (<see cref="LoadSession"/>), F at ore with one in hand sets it down and loads it, and F at the smithy's chest (or
/// the shed's) with a loaded one in hand tips it in (<see cref="BarrowSystem.TipOut"/>). With nothing to load, F at a
/// barrow picks it up, as anything is.
/// The core's dig key still digs as it always has (the trade <c>mines</c>, so it may); hewing is the quicker
/// way for a hand that strikes well, and the one that gets the most out of a seam.
/// </summary>
public sealed class MinerModule : ITradeModule
{
    public const string TradeName = "Miner";

    /// <summary>Barrows left by the shed at the start of a round.</summary>
    public const int ShedBarrows = 2;

    public string? Trade => TradeName;

    public static bool IsMiner(Simulation sim, Entity who) => sim.World.TryGet<Trade>(who, out var t) && t.Def.Name == TradeName;

    private enum Deed : byte { Hew, Sound, Load, SetDownAndLoad, Tip }

    /// <summary>
    /// What F does for the miner at <paramref name="cell"/>, and the words for it; null if nothing of the miner's. One answer
    /// for both <see cref="EarlyActionAt"/> and <see cref="Perform"/>, so what's offered is what's done.
    /// </summary>
    private static (Deed Deed, string Label, Entity Target)? Decide(Simulation sim, Entity avatar, int3 cell, int3 here)
    {
        var w = sim.World;
        if (!IsMiner(sim, avatar) || w.Has<WorkSession>(avatar) || !sim.Map.InBounds(cell))
            return null;
        var held = Barrows.Held(sim, avatar);
        var item = sim.Map.Item[sim.Map.Idx(cell)];
        if (!held.IsNull)
        {
            var load = Barrows.LoadOf(sim, held);
            if (load.Pieces.Count > 0 && BarrowSystem.TipInto(sim, avatar, cell, out var paid) is { IsNull: false } chest)
                return (Deed.Tip, paid
                    ? $"Tip the barrow into the smithy's chest ({Barrows.Describe(load.Pieces)}: {Barrows.Worth(sim, load.Pieces)} silver)"
                    : $"Tip the barrow into the chest ({Barrows.Describe(load.Pieces)})", chest);
            // Only with room to set it down beside you: in a tunnel full of hewn stone, step back first.
            if (!item.IsNull && w.TryGet<Thing>(item, out var lying) && Barrows.Loadable(sim, lying.Def)
                && AvatarSystem.InReach(here, cell) && load.Mass + lying.Def.Mass <= Barrows.MaxMass + 1e-4f && Barrows.SpotBy(sim, avatar) != null)
                return (Deed.SetDownAndLoad, $"Set the barrow down and load the {lying.Def.Label}", held);
            return null;
        }
        if (Barrows.IsBarrow(sim, item) && LoadSession.Problem(sim, avatar, item) == null)
            return (Deed.Load, $"Load the barrow ({Barrows.Describe(Barrows.LoadOf(sim, item).Pieces)} in it)", item);
        if (SoundSession.HammerInHand(sim, avatar))
            return SoundSession.Problem(sim, avatar, cell, here) == null ? (Deed.Sound, $"Sound the {HewSession.FaceOf(sim, cell).Label}", Entity.Null) : null;
        if (HewSession.Problem(sim, avatar, cell, here) == null)
            return (Deed.Hew, $"Hew the {HewSession.FaceOf(sim, cell).Label}", Entity.Null);
        return null;
    }

    public AvatarAction? EarlyActionAt(Simulation sim, Entity avatar, int3 cell, int3 here) =>
        Decide(sim, avatar, cell, here) is { } d ? new AvatarAction(AvatarActionKind.Custom, d.Label, cell, d.Target) : null;

    public void Perform(Simulation sim, Entity avatar, AvatarAction action)
    {
        var w = sim.World;
        var here = w.Get<Position>(avatar).Cell;
        if (Decide(sim, avatar, action.Cell, here) is not { } d || d.Target != action.Target)
            return;
        switch (d.Deed)
        {
            case Deed.Hew:
                WorkSystem.Start(sim, avatar, HewSession.At(sim, action.Cell));
                break;
            case Deed.Sound:
                WorkSystem.Start(sim, avatar, SoundSession.At(sim, action.Cell, here));
                break;
            case Deed.Load:
                WorkSystem.Start(sim, avatar, LoadSession.At(sim, avatar, d.Target));
                break;
            case Deed.SetDownAndLoad:
                w.Get<Carrying>(avatar).Item = Entity.Null;
                Barrows.PutDown(sim, avatar, d.Target);
                if (LoadSession.Problem(sim, avatar, d.Target) == null)
                    WorkSystem.Start(sim, avatar, LoadSession.At(sim, avatar, d.Target));
                break;
            case Deed.Tip:
                BarrowSystem.TipOut(sim, avatar, d.Target, w.Get<PresetBuilding>(Buildings.At(sim, action.Cell)).Def.Name == OreCarterSystem.SmithyDef);
                break;
        }
    }

    /// <summary>An AI miner's own mine work, once there's no marked digging: ore to the smithy (<see cref="OreRuns"/>).</summary>
    public Job? JobFor(Simulation sim, Entity pawn, WorkType type) => type == WorkType.Mine ? OreRuns.Find(sim, pawn) : null;

    /// <summary>Barrows by the mine shed's chest, as a round's town is set up.</summary>
    public void Stock(Simulation sim, Entity building)
    {
        if (!sim.World.TryGet<PresetBuilding>(building, out var b) || sim.Defs.AllTrades.FirstOrDefault(t => t.Name == TradeName) is not { } trade
            || !b.Def.Has(trade.Role))
            return;
        var near = HeroesNotIncluded.Sim.Systems.Stock.ChestOf(sim, building) is { IsNull: false } chest ? sim.World.Get<Thing>(chest).Cell : b.Anchor;
        for (var k = 0; k < ShedBarrows; k++)
            sim.PlaceItemNear(sim.CreateItem(sim.Defs.Thing(Barrows.BarrowDef), 1), near);
    }
}

/// <summary>
/// The mine on demand (<c>--drill hewing</c>): the miner stood in the mine at a face, a pick to hand, the
/// hewing begun. The face is the nearest rock to the mine shed that a tunnel lays open, as anyone walking in
/// would find it.
/// </summary>
public static class HewDrill
{
    /// <summary>The sounding on demand (<c>--drill sounding</c>): as the hewing's, the sounding hammer in hand.</summary>
    public static Entity StageSounding(Simulation sim, Entity me)
    {
        if (!SoundSession.HammerInHand(sim, me))
        {
            Inventory.Stow(sim, me, sim.CreateItem(sim.Defs.Thing(SoundSession.HammerDef), 1));
            if (sim.PackOf(me) is { IsNull: false } pack
                && sim.StoredIn(pack).FirstOrDefault(e => sim.World.Get<Thing>(e).Def.Name == SoundSession.HammerDef, Entity.Null) is { IsNull: false } hammer)
                Inventory.Hold(sim, me, hammer);
        }
        return Stage(sim, me, sounding: true);
    }

    public static Entity Stage(Simulation sim, Entity me) => Stage(sim, me, sounding: false);

    /// <summary>
    /// The hauling on demand (<c>--drill hauling</c>): at a face in the mine as the hewing's, a pile of iron ore and coal hewn
    /// at the miner's feet and a barrow set down beside them, the loading begun.
    /// </summary>
    public static Entity StageHauling(Simulation sim, Entity me)
    {
        Stage(sim, me, sounding: false);
        WorkSystem.Stop(sim, me, null);
        Barrows.PutDown(sim, me, sim.CreateItem(sim.Defs.Thing("IronOre"), 6));
        Barrows.PutDown(sim, me, sim.CreateItem(sim.Defs.Thing("Coal"), 6));
        var barrow = Barrows.PutDown(sim, me, sim.CreateItem(sim.Defs.Thing(Barrows.BarrowDef), 1));
        if (LoadSession.Problem(sim, me, barrow) == null)
            WorkSystem.Start(sim, me, LoadSession.At(sim, me, barrow));
        return Entity.Null;
    }

    private static Entity Stage(Simulation sim, Entity me, bool sounding)
    {
        var w = sim.World;
        if (Crafting.ToolSpeed(sim, me, "Pick") <= 0f)
            Inventory.Stow(sim, me, sim.CreateItem(sim.Defs.Thing("Pick"), 1));
        var shed = sim.Defs.AllTrades.FirstOrDefault(t => t.Name == MinerModule.TradeName) is { } trade
            ? HeroesNotIncluded.Sim.Shops.ShopSystem.WorkplaceOf(sim, trade) : Entity.Null;
        var from = !shed.IsNull && w.TryGet<PresetBuilding>(shed, out var b) ? b.Anchor : w.Get<Position>(me).Cell;
        var map = sim.Map;
        // Standing room under rock (a tunnel), beside a face that can be hewn.
        foreach (var stand in sim.SpiralFrom(from, 40))
        {
            if (!map.InBounds(stand) || !map.Passable(stand) || !map.CoveredAbove(map.Idx(stand)))
                continue;
            foreach (var d in new[] { new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 1, 0), new int3(0, -1, 0) })
            {
                var face = stand + d;
                if (HewSession.Problem(sim, me, face, stand) != null)
                    continue;
                HeroesNotIncluded.Sim.Scenarios.FireDrill.MoveTo(sim, me, stand);
                if (!w.Has<WorkSession>(me))
                    WorkSystem.Start(sim, me, sounding ? SoundSession.At(sim, face, stand) : HewSession.At(sim, face));
                return Entity.Null;
            }
        }
        return Entity.Null;
    }
}
