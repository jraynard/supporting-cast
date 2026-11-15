using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Hospitality;

/// <summary>One line in the inn's book: who was written into which room, for which night.</summary>
public record struct LedgerLine(int Room, Entity Guest, string Name, int Night, bool Out);

/// <summary>
/// The inn's book of rooms, kept on the key board. It holds what the keeper wrote, not what's so:
/// written into one room and handed another's key, a guest is where the key sent them, and the
/// book is wrong until someone puts it right. Anyone at the board can read it.
/// </summary>
public sealed class InnLedger
{
    public List<LedgerLine> Lines = [];
}

/// <summary>Writing guests into the book, and reading it (<see cref="Perception.PerceptionView"/> for what a viewer sees).</summary>
public static class Ledger
{
    /// <summary>Lines the book keeps: the oldest go once it's past this (the page turned).</summary>
    public const int MaxLines = 40;

    /// <summary>The book on an inn's key board, or null.</summary>
    public static InnLedger? Of(Simulation sim, Entity board) =>
        sim.World.IsAlive(board) && sim.World.TryGet<InnLedger>(board, out var l) ? l : null;

    /// <summary>The rooms the board keeps keys for, by number: the room numbers there are.</summary>
    public static int Rooms(Simulation sim)
    {
        var most = 0;
        foreach (var e in sim.World.Each<GuestRoom>())
            most = Math.Max(most, sim.World.Get<GuestRoom>(e).Number);
        return most;
    }

    /// <summary>Is this guest written in for tonight (in any room)?</summary>
    public static bool SignedIn(Simulation sim, Entity board, Entity guest)
    {
        if (Of(sim, board) is not { } book)
            return true; // no book here: nobody asks to be written in
        var tonight = Lodging.Tonight(sim);
        return book.Lines.Exists(l => l.Guest == guest && l.Night == tonight && !l.Out);
    }

    /// <summary>Write a guest into a room for tonight.</summary>
    public static void Write(Simulation sim, Entity board, int room, Entity guest)
    {
        if (Of(sim, board) is not { } book || !sim.World.IsAlive(guest) || !sim.World.TryGet<Creature>(guest, out var c))
            return;
        book.Lines.Add(new LedgerLine(room, guest, c.Name, Lodging.Tonight(sim), false));
        if (book.Lines.Count > MaxLines)
            book.Lines.RemoveRange(0, book.Lines.Count - MaxLines);
    }

    /// <summary>The latest line for a room, if it has one.</summary>
    public static LedgerLine? Latest(InnLedger book, int room)
    {
        for (var i = book.Lines.Count - 1; i >= 0; i--)
            if (book.Lines[i].Room == room)
                return book.Lines[i];
        return null;
    }

    /// <summary>Strike through a room's latest line: they've gone.</summary>
    public static bool MarkOut(Simulation sim, Entity board, int room)
    {
        if (Of(sim, board) is not { } book)
            return false;
        for (var i = book.Lines.Count - 1; i >= 0; i--)
            if (book.Lines[i].Room == room)
            {
                if (book.Lines[i].Out)
                    return false;
                book.Lines[i] = book.Lines[i] with { Out = true };
                return true;
            }
        return false;
    }

    public static void WriteState(SaveWriter w, InnLedger book)
    {
        w.Int(book.Lines.Count);
        foreach (var l in book.Lines)
        {
            w.Int(l.Room);
            w.Entity(l.Guest);
            w.String(l.Name);
            w.Int(l.Night);
            w.Bool(l.Out);
        }
    }

    public static InnLedger ReadState(SaveReader r)
    {
        var book = new InnLedger();
        for (var n = r.Count(1 << 10); n > 0; n--)
            book.Lines.Add(new LedgerLine(r.Int(), r.Entity(), r.String(), r.Int(), r.Bool()));
        return book;
    }
}

/// <summary>
/// Signing a guest in: their name, the room and the night, written into the book a stroke at a
/// time, each pressed as the quill comes round to it. A stroke off the beat blots and is written
/// again: it only costs time.
/// </summary>
public sealed class SignInSession : WorkSession
{
    public const int Strokes = 4;

    /// <summary>Seconds a stroke takes.</summary>
    public const float Beat = 0.55f;

    public Entity Board { get; set; } = Entity.Null;
    public Entity Guest { get; set; } = Entity.Null;
    public int Room { get; set; }

    public int Written { get; private set; }
    public int Blots { get; private set; }

    public float Sweep => Triangle(StepsDone, Beat);

    public bool OnCue => Sweep > 0.75f;

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Board) && sim.World.TryGet<Thing>(Board, out var t) ? t.Cell : null;

    public override Skill Skill => Skill.Social;
    public override float Xp => 1.5f;
    public override string Title => $"Writing them into room {Room}";

    public override bool StillValid(Simulation sim) =>
        sim.World.IsAlive(Board) && sim.World.IsAlive(Guest) && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Board);

    protected override void Press()
    {
        if (!OnCue)
        {
            Blots++;
            return;
        }
        Written++;
        if (Written >= Strokes)
            Done = true;
    }

    protected override void Step() { }

    public override void Finish(Simulation sim)
    {
        Ledger.Write(sim, Board, Room, Guest);
        sim.Messages.Tell(Worker, MessageKind.Info, $"{sim.World.Get<Creature>(Guest).Name}, room {Room}, written in.", sim.World.Get<Thing>(Board).Cell);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Board);
        w.Entity(Guest);
        w.Int(Room);
        w.Int(Written);
        w.Int(Blots);
    }

    protected override void ReadState(SaveReader r)
    {
        Board = r.Entity();
        Guest = r.Entity();
        Room = r.Int();
        Written = r.Int();
        Blots = r.Int();
    }
}

/// <summary>
/// At the key board, write the guest of a room order into the book for a room (<see cref="SignInSession"/>).
/// The order must be open and at this inn; the room, one the board keeps keys for.
/// </summary>
public sealed record SignInCommand(Entity Avatar, Entity Board, int Order, int Room) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!InventoryCommand.Ready(sim, Avatar) || Ledger.Of(sim, Board) == null || Room < 1 || Room > Ledger.Rooms(sim)
            || !AvatarSystem.InReach(sim, w.Get<Position>(Avatar).Cell, Board) || w.Has<WorkSession>(Avatar))
            return;
        if (sim.Shops.Find(Order) is not { IsOpen: true } order || order.Kind.Kind != ServiceKind.Lodging || !w.IsAlive(order.Poster)
            || Buildings.Of(sim, Board) != order.Where)
            return;
        WorkSystem.Start(sim, Avatar, new SignInSession { Board = Board, Guest = order.Poster, Room = Room });
    }
}

/// <summary>Strike a room's latest line in the book: its guest has gone.</summary>
public sealed record MarkOutCommand(Entity Avatar, Entity Board, int Room) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && Ledger.Of(sim, Board) != null
            && AvatarSystem.InReach(sim, sim.World.Get<Position>(Avatar).Cell, Board))
            Ledger.MarkOut(sim, Board, Room);
    }
}
