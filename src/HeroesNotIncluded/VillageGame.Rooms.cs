using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Components;
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
                if (l.Out)
                    TextC(Theme.Dim, $"{l.Name}, {when} (gone)");
                else
                {
                    // Click a name to have them pointed out: where's the guest in room 3?
                    ImGui.PushStyleColor(ImGuiCol.Text, in Theme.Text);
                    if (ImGui.Selectable($"{l.Name}, {when}##guest{r}", _bookPointer?.Who == l.Guest, ImGuiSelectableFlags.None, default))
                        _bookPointer = (l.Guest, $"{l.Name}: room {r}", _time + BookPointerSeconds);
                    ImGui.PopStyleColor(1);
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                        Tip("Point them out.");
                }
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

    /// <summary>
    /// Where a room's leavings go, marked while you carry them: slops to the midden behind the inn,
    /// soiled sheets to the wash tub, and the empty bucket back to the storeroom chest once it's tipped.
    /// Plain to the one carrying them; nothing hidden.
    /// </summary>
    private void UpdateChoreMarkers()
    {
        if (!HasAvatar || !_sim.Round.Live)
            return;
        var w = _sim.World;
        string? want = null, label = null;
        if (!RoomChores.SlopsOn(_sim, _avatar).IsNull)
            (want, label) = (RoomChores.MiddenDef, "Tip the slops here (F)");
        else if (!RoomChores.SoiledOn(_sim, _avatar).IsNull)
            (want, label) = ("WashTub", "Wash the sheets here (F)");
        if (want == null)
            return;
        var near = w.Get<Position>(_avatar).Cell;
        Entity best = Entity.Null;
        var bestD = int.MaxValue;
        foreach (var e in w.Query<Thing>())
        {
            var t = w.Get<Thing>(e);
            if (!t.Spawned || (want == "WashTub" ? t.Def.Building is not { WashTub: true } : t.Def.Name != want))
                continue;
            var d = Sim.Maps.Map.Distance(t.Cell, near);
            if (d < bestD)
                (best, bestD) = (e, d);
        }
        if (best.IsNull)
            return;
        var at = w.Get<Thing>(best);
        var top = (at.Def.Model?.Max(b => b.Max[2]) ?? 0.5f) * Rendering.WorldScale.HeightOf(at.Def);
        _renderer.Markers.Add(new(Sim.Components.Position.CenterOf(at.Cell) + Rendering.WorldScale.Rise(top + 0.5f), label!, false));
    }

    // Which of several guests waiting for a room the book's buttons write in.
    private int _signFor;

    /// <summary>Signing a guest in: F, and the quill writes their line into the book (<see cref="SignInScene"/>).</summary>
    private void DrawSignIn(SignInSession s)
    {
        var name = _sim.World.IsAlive(s.Guest) && _sim.World.TryGet<Creature>(s.Guest, out var c) ? c.Name : "the guest";
        if (s.Begun)
            BeginWork(s.Title, ("Esc", "leave it"));
        else
            BeginWork(s.Title, ("F", "write them in"), ("Esc", "leave it"));
        SignInScene(s.Ink, s.Begun, $"{name}, room {s.Room}");
        TextC(s.Begun ? Theme.Dim : Theme.Text, s.Begun ? $"Writing {name} into room {s.Room}..." : $"{name}, in room {s.Room}. Press F to write it.");
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
