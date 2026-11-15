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

    /// <summary>Signing a guest in: the page, the strokes so far, a blot for each one off the beat, the quill as it comes round.</summary>
    private void DrawSignIn(SignInSession s)
    {
        BeginWork(s.Title, ("F", "as the quill comes round"), ("Esc", "leave it"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(360, 110);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        dl.AddRectFilled(in min, in max, new ImColor8(226, 212, 178, 255), Gui.S(4), ImDrawFlags.None);
        var ruled = new ImColor8(170, 150, 120, 140);
        for (var i = 1; i < 4; i++)
        {
            var a = new float2(min.X + Gui.S(10), min.Y + size.Y * i / 4f);
            var b = new float2(max.X - Gui.S(10), a.Y);
            dl.AddLine(in a, in b, ruled, Gui.S(1));
        }
        // Today's line, half-way down: the strokes written so far, in ink.
        var ink = new ImColor8(40, 34, 30, 255);
        var lineY = min.Y + size.Y * 0.5f - Gui.S(6);
        var x0 = min.X + Gui.S(18);
        var span = (size.X - Gui.S(36)) / SignInSession.Strokes;
        for (var i = 0; i < s.Written; i++)
        {
            var from = new float2(x0 + span * i, lineY);
            for (var k = 0; k < 6; k++)
            {
                var p0 = from + new float2(span * 0.8f * k / 6f, MathF.Sin(k * 1.7f + i) * Gui.S(5));
                var p1 = from + new float2(span * 0.8f * (k + 1) / 6f, MathF.Sin((k + 1) * 1.7f + i) * Gui.S(5));
                dl.AddLine(in p0, in p1, ink, Gui.S(2));
            }
        }
        for (var i = 0; i < s.Blots; i++)
        {
            var c = new float2(x0 + span * Math.Min(s.Written, SignInSession.Strokes - 1) + Gui.S(8 + 9 * (i % 4)), lineY + Gui.S(14 + 4 * (i % 2)));
            dl.AddCircleFilled(in c, Gui.S(4 + i % 3), new ImColor8(30, 26, 40, 220), 12);
        }
        // The quill, sweeping over the stroke to come; bright when it's time.
        var qx = x0 + span * Math.Min(s.Written, SignInSession.Strokes - 1) + span * 0.8f * s.Sweep;
        var tip = new float2(qx, lineY + Gui.S(4));
        var top = tip + new float2(Gui.S(18), -Gui.S(34));
        dl.AddLine(in tip, in top, s.OnCue ? Theme.U8(Theme.Good) : new ImColor8(240, 236, 226, 255), Gui.S(3));
        ImGui.Text($"Room {s.Room}: {s.Written} of {SignInSession.Strokes} written{(s.Blots > 0 ? $", {s.Blots} blot{(s.Blots == 1 ? "" : "s")}" : "")}.");
        Bar("", s.Sweep, s.OnCue ? Theme.Good : Theme.Dim);
        EndWork();
    }
}
