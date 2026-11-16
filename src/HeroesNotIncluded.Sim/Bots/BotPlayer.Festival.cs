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
    private bool _laidOut;
    private readonly HashSet<string> _played = [];

    /// <summary>
    /// The festival, as a player would spend it between orders: a piece of their own work on the
    /// show table, a go at each game, then to the well for the ceremony.
    /// </summary>
    private void Festival(Simulation sim, Entity me, long now)
    {
        var w = sim.World;
        var f = sim.Festival;
        // At the butts' mark: draw, aim at the gold and loose, three times.
        if (w.TryGet<Work.ButtsTurn>(me, out var turn) && w.IsAlive(turn.Target))
        {
            Doing = "shooting at the butts";
            var from = w.Get<Position>(me).Pos;
            var at = w.Get<Thing>(turn.Target).Cell;
            var aim = DetMath.Atan2(at.Y + 0.5f - from.Y, at.X + 0.5f - from.X);
            if (!w.Has<Combat.Aiming>(me))
                Send(new BowCommand(me, true, false, aim, now));
            else if (Combat.Archery.Ready(sim, me))
                Send(new BowCommand(me, true, true, aim, now, from.X, from.Y, w.Get<Position>(me).Cell.Z));
            return;
        }
        if (f.Open)
        {
            var table = f.Prop(sim, Round.FestivalSystem.ShowTableDef);
            var pack = sim.PackOf(me);
            if (!_laidOut && !table.IsNull && !pack.IsNull
                && sim.StoredIn(pack).FirstOrDefault(e => w.TryGet<Crafted>(e, out var c) && c.Maker == me, Entity.Null) is { IsNull: false } piece)
            {
                Doing = "laying out a piece on the show table";
                AtStation(sim, me, table, now, t =>
                {
                    Send(new StoreFromPackCommand(me, t, piece));
                    _laidOut = true;
                });
                return;
            }
            if (f.Part == Round.FestivalPart.Revels)
                foreach (var g in f.Games)
                {
                    var thing = f.PlayedAt(sim, g);
                    if (thing.IsNull || _played.Contains(g.Game.Name))
                        continue;
                    Doing = $"having a go at {g.Game.Label}";
                    AtStation(sim, me, thing, now, t =>
                    {
                        Send(new FestivalGameCommand(me, t));
                        _played.Add(g.Game.Name);
                    });
                    return;
                }
        }
        Doing = f.Part == Round.FestivalPart.Ceremony ? "gathered round the well" : "at the festival";
        if (sim.Round.Square is { } square && Maps.Map.Distance(w.Get<Position>(me).Cell, square) > 5)
            Walk(sim, me, square, now);
    }
}
