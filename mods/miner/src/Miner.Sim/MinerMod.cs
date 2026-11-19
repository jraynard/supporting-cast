using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Round;
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
        registry.Bot(bot => new MinerBot(bot));
        // --drill hewing: stood in the mine at a face, pick in hand, the hewing begun.
        registry.Drill("hewing", HewDrill.Stage);
    }
}

/// <summary>
/// The miner's hooks (<see cref="ITradeModule"/>): F at a rock face hews it (<see cref="HewSession"/>).
/// The core's dig key still digs as it always has (the trade <c>mines</c>, so it may); hewing is the quicker
/// way for a hand that strikes well, and the one that gets the most out of a seam.
/// </summary>
public sealed class MinerModule : ITradeModule
{
    public const string TradeName = "Miner";

    public string? Trade => TradeName;

    public static bool IsMiner(Simulation sim, Entity who) => sim.World.TryGet<Trade>(who, out var t) && t.Def.Name == TradeName;

    /// <summary>F at a rock face in reach, a pick that breaks it to hand: hew it. Read as the face looks (hidden ore passes for its rock).</summary>
    public AvatarAction? EarlyActionAt(Simulation sim, Entity avatar, int3 cell, int3 here)
    {
        if (!IsMiner(sim, avatar) || sim.World.Has<WorkSession>(avatar) || HewSession.Problem(sim, avatar, cell, here) != null)
            return null;
        return new AvatarAction(AvatarActionKind.Custom, $"Hew the {HewSession.FaceOf(sim, cell).Label}", cell, Entity.Null);
    }

    public void Perform(Simulation sim, Entity avatar, AvatarAction action)
    {
        if (!sim.World.Has<WorkSession>(avatar) && HewSession.Problem(sim, avatar, action.Cell, sim.World.Get<Position>(avatar).Cell) == null)
            WorkSystem.Start(sim, avatar, HewSession.At(sim, action.Cell));
    }
}

/// <summary>
/// How a bot hews (<see cref="IBotTrade"/>): only through the session it sees and the keys a player has. It
/// walks the edge over to the crack, raises the pick past the mark, and lets go. Between orders it does
/// nothing of its own yet (a first pass: the season sweep doesn't deal a player-only trade to bots).
/// </summary>
public sealed class MinerBot(BotPlayer bot) : IBotTrade
{
    public BotPlayer Bot => bot;

    public (bool Press, bool Held, int Axis)? Play(WorkSession s)
    {
        if (s is not HewSession hew)
            return null;
        var off = hew.Crack - hew.Aim;
        var onCrack = MathF.Abs(off) <= hew.CrackWidthNow * 0.5f;
        // Line up first; then hold to raise, and let go once it's well past the mark.
        if (!onCrack && hew.Raised <= 0f)
            return (false, false, Math.Sign(off));
        return (false, hew.Raised < HewSession.NeedRaise + 0.15f, 0);
    }
}

/// <summary>
/// The mine on demand (<c>--drill hewing</c>): the miner stood in the mine at a face, a pick to hand, the
/// hewing begun. The face is the nearest rock to the mine shed that a tunnel lays open, as anyone walking in
/// would find it.
/// </summary>
public static class HewDrill
{
    public static Entity Stage(Simulation sim, Entity me)
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
                    WorkSystem.Start(sim, me, HewSession.At(sim, face));
                return Entity.Null;
            }
        }
        return Entity.Null;
    }
}
