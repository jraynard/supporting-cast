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
    /// <summary>
    /// Between orders, the innkeeper keeps the place: beds made up, floors mopped, the dirty
    /// crockery washed. True while it's at it.
    /// </summary>
    private bool Upkeep(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        // Anyone who's asked the way within earshot is told it.
        if (view.WaysAsked().FirstOrDefault() is { Id: > 0 } ask)
        {
            Doing = $"pointing {ask.AskerName} the way";
            Send(new Hospitality.DirectCommand(me, ask.Id));
            return true;
        }
        if (!w.TryGet<Trade>(me, out var trade) || !trade.Def.HasWorkplace)
            return false;
        var inn = ShopSystem.WorkplaceOf(sim, trade.Def);
        if (inn.IsNull || trade.Def.Role != Defs.TownRole.Tavern)
            return false;
        var bed = trade.Def.Serving(ServiceKind.Lodging) ? Buildings.Parts(sim, inn).FirstOrDefault(p => Hospitality.Lodging.IsUnmade(sim, p), Entity.Null) : Entity.Null;
        if (!bed.IsNull)
        {
            Doing = "making up a bed";
            // A locked room: its key off the board (or the spare from the storeroom), then the door.
            if (w.TryGet<Hospitality.GuestRoom>(bed, out var room) && sim.IsLocked(room.Door))
            {
                if (Hospitality.InnRooms.KeyOn(sim, me, bed).IsNull)
                {
                    var seed = Hospitality.InnRooms.SeedOf(sim, bed);
                    var store = Hospitality.InnRooms.OnBoard(sim, inn, seed).IsNull ? Hospitality.InnRooms.SpareBox(sim, inn) : Hospitality.InnRooms.Board(sim, inn);
                    if (Hospitality.InnRooms.KeyIn(sim, store, seed).IsNull)
                        return false; // no key to be had: leave it
                    Doing = "fetching a room key";
                    return AtStation(sim, me, store, now, s =>
                    {
                        if (view.InContainer(s).FirstOrDefault(k => Hospitality.InnRooms.RoomOfKey(sim, k) == room.Number, Entity.Null) is { IsNull: false } key)
                            Send(new TakeFromStorageCommand(me, key));
                    });
                }
                Doing = "unlocking a room";
                return AtStation(sim, me, room.Door, now, d => Send(new Tinkering.TurnKeyCommand(me, d, false)));
            }
            // Clean linen from the storeroom first.
            if (Hospitality.RoomChores.LinenOn(sim, me).IsNull)
            {
                // Where it can see some; else the storeroom chest, to look (it's kept there).
                var store = Buildings.Parts(sim, inn).FirstOrDefault(p => sim.IsContainer(p)
                    && view.InContainer(p).Any(i => w.Get<Thing>(i).Def.Name == Hospitality.RoomChores.LinenDef), Entity.Null);
                if (store.IsNull)
                    store = Buildings.Parts(sim, inn).FirstOrDefault(p => w.Get<Thing>(p) is { Def.Name: "Chest" } c && c.Cell.Z == w.Get<PresetBuilding>(inn).Anchor.Z
                        && !AvatarSystem.InReach(sim, w.Get<Position>(me).Cell, p), Entity.Null);
                if (store.IsNull)
                    return false;
                Doing = "fetching clean linen";
                return AtStation(sim, me, store, now, s =>
                {
                    if (view.InContainer(s).FirstOrDefault(i => w.Get<Thing>(i).Def.Name == Hospitality.RoomChores.LinenDef, Entity.Null) is { IsNull: false } linen)
                        Send(new TakeFromStorageCommand(me, linen));
                });
            }
            return AtStation(sim, me, bed, now, b => Send(new BarWorkCommand(me, b)));
        }
        var anchor = w.Get<PresetBuilding>(inn).Anchor;
        var mess = view.Messes().Where(m => Maps.Map.Distance(m.Cell, anchor) <= CleanJob.InnReach && CleanJob.Mine(sim, trade.Def, inn, m.Cell))
            .OrderBy(m => Maps.Map.Distance(m.Cell, w.Get<Position>(me).Cell)).Select(m => (int3?)m.Cell).FirstOrDefault();
        if (mess is { } at)
        {
            Doing = "mopping the floor";
            if (AvatarSystem.InReach(w.Get<Position>(me).Cell, at))
                Send(new Hospitality.ScrubCommand(me, at));
            else
                Walk(sim, me, at, now);
            return true;
        }
        // Its own crockery: the bar's tankards, or the kitchen's bowls.
        var mine = WashJob.VesselsOf(sim, me);
        if (BarWork.DirtyOn(sim, me) is { IsNull: false } dirty && w.Get<Thing>(dirty).Def.WashesInto is { } clean && mine.Contains(clean))
        {
            Doing = "washing up";
            return AtStation(sim, me, Nearest(sim, me, "WashTub"), now, tub => Send(new BarWorkCommand(me, tub)));
        }
        return false;
    }

    /// <summary>
    /// A room key carried once the room's made up goes back before the next order: on the board if
    /// it's short of it, else with the spares (it was left in a pack an hour while the bar was busy).
    /// </summary>
    private bool PutKeyBack(Simulation sim, Entity me, long now)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(me, out var trade) || !trade.Def.Serving(ServiceKind.Lodging) || trade.Def.Role != Defs.TownRole.Tavern)
            return false;
        var inn = ShopSystem.WorkplaceOf(sim, trade.Def);
        if (inn.IsNull || Tinkering.Locksmithing.Keys(sim, me).FirstOrDefault(k => Hospitality.InnRooms.IsRoomKey(sim, k), Entity.Null) is not { IsNull: false } spare)
            return false;
        var seed = w.Get<Tinkering.Key>(spare).Seed;
        // Still wanted for a room to make up: keep it.
        if (Buildings.Parts(sim, inn).Any(p => Hospitality.Lodging.IsUnmade(sim, p) && Hospitality.InnRooms.SeedOf(sim, p) == seed))
            return false;
        var board = Hospitality.InnRooms.Board(sim, inn);
        var to = Hospitality.InnRooms.KeyIn(sim, board, seed).IsNull ? board : Hospitality.InnRooms.SpareBox(sim, inn);
        if (to.IsNull || sim.ContainerRoom(to, w.Get<Thing>(spare).Def, null) <= 0)
            return false;
        Doing = "putting a room key back";
        // Taken off the board, a key comes into an empty hand: into the pack first, to put it away from there.
        if (w.Get<Carrying>(me).Item == spare)
        {
            Send(new StowCommand(me));
            return true;
        }
        return AtStation(sim, me, to, now, s => Send(new StoreFromPackCommand(me, s, spare)));
    }

    /// <summary>
    /// A named drink: the adds from the shelf behind the bar, a clean tankard, then mixed at the bar
    /// to its cues, and handed over. Without what it takes, an ale will have to do.
    /// </summary>
    private bool MixFor(Simulation sim, Entity me, PerceptionView view, Entity drinker, ThingDef named, long now)
    {
        var w = sim.World;
        var held = w.Get<Carrying>(me).Item;
        if (w.IsAlive(held) && w.Get<Thing>(held).Def.Drink)
        {
            if (Reach(sim, me, drinker, now))
                Send(new GiveCommand(me, drinker, held, 1));
            return true;
        }
        var bar = Nearest(sim, me, "BarCounter");
        var cask = bar.IsNull ? Entity.Null : MixSession.CaskFor(sim, bar, named);
        if (cask.IsNull)
            return Pour(sim, me, view, drinker, now);
        if (PourSession.ServingsLeft(sim, cask) <= 0)
            return Restock(sim, me, view, now); // run dry: a mix needs its measure from the cask
        var into = w.Get<Thing>(cask).Def.Building!.Cask!.Into;
        if (Carried(sim, me, into).IsNull)
            return Crockery(sim, me, view, now, into, cask);
        // Each add, from the shelf behind the bar (or the larder, for berries).
        foreach (var add in MixSession.AddsOf(sim, named, null))
        {
            if (!AvatarSystem.Supply(sim, me, d => d == add).IsNull)
                continue;
            var store = w.Query<Thing>().Where(e => w.Get<Thing>(e) is { Spawned: true } t && t.Def.Building?.StorageStacks > 0
                    && sim.StoredIn(e).Any(i => w.Get<Thing>(i).Def == add))
                .OrderBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, w.Get<Thing>(bar).Cell)).FirstOrDefault(Entity.Null);
            if (store.IsNull)
                return Pour(sim, me, view, drinker, now);
            Doing = $"fetching {add.Label}";
            if (!w.Get<Carrying>(me).Item.IsNull && w.Get<Thing>(w.Get<Carrying>(me).Item).Def.Name != into)
            {
                Send(new StowCommand(me));
                return true;
            }
            return AtStation(sim, me, store, now, s =>
            {
                if (view.InContainer(s).FirstOrDefault(i => w.Get<Thing>(i).Def == add, Entity.Null) is { IsNull: false } stack)
                    Send(new TakeFromStorageCommand(me, stack));
            });
        }
        if (!held.IsNull && w.Get<Thing>(held).Def.Name != into)
        {
            Send(new StowCommand(me));
            return true;
        }
        Doing = $"mixing a {named.Label}";
        return AtStation(sim, me, bar, now, b => Send(new MixCommand(me, b, named, null)));
    }

    private bool Pour(Simulation sim, Entity me, PerceptionView view, Entity drinker, long now)
    {
        var w = sim.World;
        var held = w.Get<Carrying>(me).Item;
        if (w.IsAlive(held) && w.Get<Thing>(held).Def.Drink)
        {
            if (Reach(sim, me, drinker, now))
                Send(new GiveCommand(me, drinker, held, 1));
            return true;
        }
        var cask = ServeJob.Cask(sim, me);
        if (cask.IsNull)
            return Restock(sim, me, view, now);
        var into = w.Get<Thing>(cask).Def.Building!.Cask!.Into;
        if (Carried(sim, me, into).IsNull)
            return Crockery(sim, me, view, now, into, cask);
        if (!held.IsNull && w.Get<Thing>(held).Def.Name != into)
        {
            Send(new StowCommand(me)); // a free hand to pour with
            return true;
        }
        // Walk up to the cask and use it: that starts the pour.
        if (now - _lastWalk >= ThinkTicks * 4)
        {
            Send(new AvatarGoToCommand(me, w.Get<Thing>(cask).Cell, Interact: true));
            _lastWalk = now;
        }
        return true;
    }

    /// <summary>A room: walk up to the guest, and the order system shows them up to a bed.</summary>
    /// <summary>
    /// A room: written into the book at the key board for a room whose key is on its hook and that
    /// the book has nobody in tonight, then that key off the board, then handed over. Another night
    /// for someone the book had in last night, they only need seeing to in person.
    /// </summary>
    private bool ShowUp(Simulation sim, Entity me, PerceptionView view, Entity guest, long now)
    {
        Doing = "letting a room";
        var board = Nearest(sim, me, Hospitality.InnRooms.BoardDef);
        if (board.IsNull)
        {
            Reach(sim, me, guest, now);
            return true;
        }
        // The book, as last read at the board (it can't be read from across the room).
        if (view.RoomBoard(board) is { } read)
            _roomBoard = read;
        if (_roomBoard is not { } book || book.Board != board)
            return AtStation(sim, me, board, now, _ => { });
        int RoomOf(Func<Hospitality.LedgerLine, bool> match)
        {
            for (var r = 1; r <= book.Rooms; r++)
                if (book.Lines[r] is { } l && l.Guest == guest && match(l))
                    return r;
            return 0;
        }
        var signed = RoomOf(l => l.Night == book.Tonight && !l.Out);
        if (signed == 0 && RoomOf(l => l.Night == book.Tonight - 1 && !l.Out) > 0)
        {
            Doing = "another night for a guest";
            Reach(sim, me, guest, now); // they have their key: paying for another night is all
            return true;
        }
        if (signed == 0)
        {
            var free = 0;
            for (var r = 1; r <= book.Rooms && free == 0; r++)
                if (book.Hung[r] && !(book.Lines[r] is { Out: false } l && l.Night == book.Tonight))
                    free = r; // its key on the hook, and nobody written in tonight
            if (free == 0)
                return false; // every room taken, as far as the book goes
            Doing = $"writing a guest into room {free}";
            return AtStation(sim, me, board, now, b => Send(new Hospitality.SignInCommand(me, b, _task, free)));
        }
        // A key to the room on them: the one off the board.
        var mine = Tinkering.Locksmithing.Keys(sim, me).Where(k => Hospitality.InnRooms.RoomOfKey(sim, k) == signed).ToList();
        if (mine.Count >= 1)
        {
            Doing = $"handing over room {signed}'s key";
            if (Reach(sim, me, guest, now, returning: true))
                Send(new GiveCommand(me, guest, mine[^1], 1));
            return true;
        }
        Doing = $"taking room {signed}'s key down";
        return AtStation(sim, me, board, now, b =>
        {
            if (view.InContainer(b).FirstOrDefault(k => Hospitality.InnRooms.RoomOfKey(sim, k) == signed, Entity.Null) is { IsNull: false } key)
                Send(new TakeFromStorageCommand(me, key));
        });
    }

    /// <summary>The key board and its book as last read standing at it.</summary>
    private KnownRoomBoard? _roomBoard;

    /// <summary>
    /// A clean vessel of a kind to pour or ladle into: from the shelf nearest where it's used, and
    /// if there are none clean, gather up the dirty ones and wash them.
    /// </summary>
    private bool Crockery(Simulation sim, Entity me, PerceptionView view, long now, string clean, Entity usedAt)
    {
        var w = sim.World;
        var dirtyDef = sim.Defs.AllThings.FirstOrDefault(d => d.WashesInto == clean)?.Name;
        var dirtyHeld = BarWork.DirtyOn(sim, me);
        if (!dirtyHeld.IsNull)
        {
            Doing = "washing up";
            return AtStation(sim, me, Nearest(sim, me, "WashTub"), now, tub => Send(new BarWorkCommand(me, tub)));
        }
        var at = w.Get<Thing>(usedAt).Cell;
        var shelf = w.Query<Thing>().Where(e => w.Get<Thing>(e) is { Spawned: true, Def.Name: "Shelf" or "BarShelf" })
            .OrderBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, at)).FirstOrDefault(Entity.Null);
        if (!shelf.IsNull && sim.StoredIn(shelf).Any(e => w.Get<Thing>(e).Def.Name == clean))
        {
            Doing = $"fetching a clean {sim.Defs.Thing(clean).Label}";
            if (!w.Get<Carrying>(me).Item.IsNull && w.Get<Thing>(w.Get<Carrying>(me).Item).Def.Name != clean)
            {
                Send(new StowCommand(me));
                return true;
            }
            return AtStation(sim, me, shelf, now, s =>
            {
                if (view.InContainer(s).FirstOrDefault(e => w.Get<Thing>(e).Def.Name == clean, Entity.Null) is { IsNull: false } stack)
                    Send(new TakeFromStorageCommand(me, stack, ServeJob.VesselsAtOnce));
            });
        }
        // None clean on the shelf: pick up the dirty ones lying about, then to the tub.
        if (dirtyDef == null)
            return false;
        var dirty = w.Query<Thing>().Where(e => w.Get<Thing>(e) is { Spawned: true } t && t.Def.Name == dirtyDef && view.CanSeeCell(t.Cell))
            .OrderBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, w.Get<Position>(me).Cell)).FirstOrDefault(Entity.Null);
        if (dirty.IsNull)
            return false;
        Doing = "clearing up the dirty crockery";
        if (now - _lastWalk >= ThinkTicks * 4)
        {
            Send(new AvatarGoToCommand(me, w.Get<Thing>(dirty).Cell, Interact: true));
            _lastWalk = now;
        }
        return true;
    }

    /// <summary>The cask's dry: fetch a keg from the storeroom and knock it in.</summary>
    private bool Restock(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        var cask = Nearest(sim, me, "AleCask");
        if (cask.IsNull)
            return false;
        Doing = "changing the keg";
        if (!BarWork.KegFor(sim, me, cask).IsNull)
            return AtStation(sim, me, cask, now, c => Send(new BarWorkCommand(me, c)));
        if (!w.Get<Carrying>(me).Item.IsNull)
        {
            Send(new StowCommand(me));
            return true;
        }
        var keg = w.Get<Thing>(cask).Def.Building!.Cask!.Keg;
        var store = w.Query<Thing>().Where(e => w.Get<Thing>(e) is { Spawned: true } t && t.Def.Building?.StorageStacks > 0
                && sim.StoredIn(e).Any(i => w.Get<Thing>(i).Def.Name == keg))
            .OrderBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, w.Get<Position>(me).Cell)).FirstOrDefault(Entity.Null);
        if (store.IsNull)
            return false;
        return AtStation(sim, me, store, now, s =>
        {
            if (view.InContainer(s).FirstOrDefault(e => w.Get<Thing>(e).Def.Name == keg, Entity.Null) is { IsNull: false } k)
                Send(new TakeFromStorageCommand(me, k));
        });
    }

    private bool Broker(Simulation sim, Entity me, PerceptionView view, Entity member, long now)
    {
        if (QuestFor(sim, view, member) is not { } quest)
            return false;
        if (!Reach(sim, me, member, now))
            return true;
        _offered.Add((sim.World.Get<Adventurer>(member).Party, quest));
        Send(new OfferQuestCommand(me, member, quest));
        return false;
    }

    /// <summary>The open quest it believes best fits how strong the party looks, not already offered them.</summary>
    private int? QuestFor(Simulation sim, PerceptionView view, Entity member)
    {
        if (view.PartyLooks(member) is not { } looks || !sim.World.TryGet<Adventurer>(member, out var a))
            return null;
        int? best = null;
        var bestOff = float.MaxValue;
        foreach (var q in view.Quests())
        {
            if (q.Status != QuestStatus.Open || _offered.Contains((a.Party, q.Id))
                || q.Danger < ExpeditionSystem.TooEasy * looks * 1.1f || q.Danger > ExpeditionSystem.TooHard * looks * 0.9f)
                continue;
            var off = MathF.Abs(q.Danger - 0.85f * looks);
            if (off < bestOff)
            {
                best = q.Id;
                bestOff = off;
            }
        }
        return best;
    }
}
