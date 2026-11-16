using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The inn's rooms: the book on the key board (read through Perception: what was written, and which
// keys are on their hooks, never who's really where), and signing a guest in.
public sealed partial class VillageGame
{
    /// <summary>
    /// Under the key board's hooks: the book, a line a room. For a room order you've taken (or
    /// can see), a button to write the guest into a room; for a room with someone written in, a
    /// button to strike them out once they've gone.
    /// </summary>
    private void DrawRoomBook(Entity board)
    {
        if (View.RoomBoard(board) is not { } book)
            return;
        ImGui.Spacing();
        Theme.Header(Host.ImGui, "The book");
        var waiting = View.Orders().Where(o => o.Status == Sim.Shops.OrderStatus.Open && o.Kind == Sim.Defs.ServiceKind.Lodging
            && !Ledger.SignedIn(_sim, board, o.Poster) && (o.MineToDo || string.IsNullOrEmpty(o.AssigneeName))).ToList();
        if (waiting.Count > 0)
        {
            var o = waiting[Math.Min(_signFor, waiting.Count - 1)];
            ImGui.TextWrapped($"{o.PosterName} wants a room. Write them in:");
            if (waiting.Count > 1 && ImGui.SmallButton("Next guest"u8))
                _signFor = (_signFor + 1) % waiting.Count;
        }
        if (!ImGui.BeginTable("##book"u8, 4, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
            return;
        for (var r = 1; r <= book.Rooms; r++)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text($"Room {r}");
            ImGui.TableSetColumnIndex(1);
            TextC(book.Hung[r] ? Theme.Text : Theme.Dim, book.Hung[r] ? "key on its hook" : "key out");
            ImGui.TableSetColumnIndex(2);
            var line = book.Lines[r];
            if (line is { } l)
            {
                var when = l.Night == book.Tonight ? "tonight" : l.Night == book.Tonight - 1 ? "last night" : $"day {l.Night + 1}";
                TextC(l.Out ? Theme.Dim : Theme.Text, l.Out ? $"{l.Name}, {when} (gone)" : $"{l.Name}, {when}");
            }
            else
                ImGui.TextDisabled("nobody yet"u8);
            ImGui.TableSetColumnIndex(3);
            ImGui.PushID(r);
            if (waiting.Count > 0 && ImGui.SmallButton("Write in"u8))
                Issue(new SignInCommand(_avatar, board, waiting[Math.Min(_signFor, waiting.Count - 1)].Id, r));
            if (line is { Out: false })
            {
                if (waiting.Count > 0)
                    ImGui.SameLine(0, Gui.S(4));
                if (ImGui.SmallButton("Strike out"u8))
                    Issue(new MarkOutCommand(_avatar, board, r));
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip("They've gone: the room's free again, as far as the book knows.");
            }
            ImGui.PopID();
        }
        ImGui.EndTable();
        TextC(Theme.Dim, "The book says what was written, not who's where. Hand over the key to the room you wrote.");
    }

    // Which of several guests waiting for a room the book's buttons write in.
    private int _signFor;

    /// <summary>Signing a guest in: the book (<see cref="SignInScene"/>), how far it's got, the quill's beat.</summary>
    private void DrawSignIn(SignInSession s)
    {
        BeginWork(s.Title, ("F", "as the quill comes round"), ("Esc", "leave it"));
        SignInScene(s);
        ImGui.Text($"Room {s.Room}: {s.Written} of {SignInSession.Strokes} written{(s.Blots > 0 ? $", {s.Blots} blot{(s.Blots == 1 ? "" : "s")}" : "")}.");
        Bar("", s.Sweep, s.OnCue ? Theme.Good : Theme.Dim);
        EndWork();
    }

    /// <summary>Emptying a chamber pot into a bucket (<see cref="SlopScene"/>), how far it's got, the sway's beat.</summary>
    private void DrawSlop(SlopSession s)
    {
        BeginWork(s.Title, ("F", "as the pour comes round"), ("Esc", "leave it"));
        SlopScene(s);
        ImGui.Text($"{s.Poured} of {SlopSession.Pours} poured{(s.Spills > 0 ? $", {s.Spills} on the floor" : "")}.");
        Bar("", s.Sweep, s.OnCue ? Theme.Good : Theme.Dim);
        EndWork();
    }
}
