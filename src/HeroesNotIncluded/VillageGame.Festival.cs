using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Work;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The festival for a player: a card saying what's on and where (the games, the dance, the show
// table), markers over each in the square, and the games' own windows (Sim/Work/FestivalGames).
public sealed partial class VillageGame
{
    /// <summary>What you've done at the festival this evening, for the card's ticks (presentation only).</summary>
    private readonly HashSet<string> _festivalDone = [];

    /// <summary>The festival part whose card was put away; it comes back when the festival moves on.</summary>
    private (FestivalPart Part, bool Telling)? _festivalCardHidden;

    /// <summary>One thing to do at the festival: where it is, what it's called, how it's done, how you're getting on.</summary>
    private readonly record struct FestivalSpot(Entity Thing, string Name, string How, string Status, bool Done);

    private List<FestivalSpot> FestivalSpots()
    {
        var spots = new List<FestivalSpot>();
        var f = _sim.Festival;
        var w = _sim.World;
        var use = Controls.Glyph(Act.Use);
        foreach (var g in f.Games)
        {
            var thing = f.PlayedAt(_sim, g);
            if (thing.IsNull)
                continue;
            var status = g.BestScore < 0 ? "Nobody's had a go yet"
                : g.Best == _avatar ? $"You lead with {g.BestScore}!"
                : $"Best tonight: {g.BestName}, {g.BestScore}";
            // At the mark now: how the turn's going, arrow by arrow.
            if (HasAvatar && w.TryGet<ButtsTurn>(_avatar, out var turn) && turn.Mark == thing)
                status = $"Arrow {Math.Min(turn.Shots + 1, FestivalGames.Turns)} of {FestivalGames.Turns}, {turn.Points} points";
            var how = g.Game.MarkDef != null
                ? $"{use} at the red mark, then hold {Controls.Glyph(Act.Heavy)} to draw and aim, {Controls.Glyph(Act.Strike)} to loose: three arrows"
                : $"{use}: three goes";
            spots.Add(new(thing, Cap(g.Game.Label), $"{how}; the night's best wins {_sim.Defs.Festival.PrizeSilver} silver and an honour",
                status, _festivalDone.Contains(g.Game.Name)));
        }
        var fire = f.Prop(_sim, FestivalSystem.BonfireDef);
        if (!fire.IsNull)
            spots.Add(new(fire, "The dance", $"{use} at the bonfire, hands empty",
                _festivalDone.Contains("Dance") ? "You've danced!" : "", _festivalDone.Contains("Dance")));
        var table = f.Prop(_sim, FestivalSystem.ShowTableDef);
        if (!table.IsNull)
        {
            var mine = _sim.StoredIn(table).FirstOrDefault(e => w.TryGet<Crafted>(e, out var c) && c.Maker == _avatar, Entity.Null);
            spots.Add(new(table, "The show table", $"{use}: lay out something you made for the visitors to judge",
                mine.IsNull ? "" : $"Your {w.Get<Thing>(mine).Label} is on show", !mine.IsNull));
        }
        return spots;
    }

    /// <summary>Notes what you're doing at the festival: a game's window open, the dance under way.</summary>
    private void UpdateFestival()
    {
        _renderer.Markers.Clear();
        if (_sim.Round.Phase != RoundPhase.Festival)
        {
            if (_sim.Round.Phase != RoundPhase.Over)
                _festivalDone.Clear();
            return;
        }
        if (!HasAvatar)
            return;
        var w = _sim.World;
        if (w.TryGet<WorkSession>(_avatar, out var ws))
        {
            if (ws is StrikerSession st) _festivalDone.Add(st.Game);
        }
        if (w.TryGet<ButtsTurn>(_avatar, out var turn) && _sim.Festival.GameAt(_sim, turn.Mark) is { } shooting)
            _festivalDone.Add(shooting.Game.Name);
        if (w.Get<Mind>(_avatar).Job is Sim.Ai.FestivalJob { Dancing: true })
            _festivalDone.Add("Dance");
        // Markers over each thing to do, while the revels are open.
        if (!_sim.Festival.Open || _sim.Festival.Telling)
            return;
        foreach (var spot in FestivalSpots())
        {
            var t = w.Get<Thing>(spot.Thing);
            var top = (t.Def.Model?.Max(b => b.Max[2]) ?? 1f) * WorldScale.HeightOf(t.Def);
            var at = Sim.Components.Position.CenterOf(t.Cell) + WorldScale.Rise(top + 0.5f);
            if (_sim.Map.InBounds(t.Cell))
                _renderer.Markers.Add(new(at, spot.Name, spot.Done));
        }
    }

    /// <summary>
    /// Top: the festival card. While the town gathers and the crier tells the season, where to go
    /// and to listen; through the revels, everything there is to do (click one to look at it);
    /// at the ceremony, to the well.
    /// </summary>
    private void DrawFestivalCard()
    {
        var f = _sim.Festival;
        if (_sim.Round.Phase != RoundPhase.Festival || (!HasAvatar && !Ghosting))
            return;
        var key = (f.Part, f.Telling);
        if (_festivalCardHidden == key)
            return;
        _hud.Next(Anchor.Top);
        Panel.Begin("##festival-card", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDecoration);
        _hud.Took();
        using (Gui.DisplayFont(20f))
            TextC(Theme.Parchment, f.Part switch
            {
                FestivalPart.Opening when f.Telling => "The festival: the crier tells the season",
                FestivalPart.Opening => "The festival bell!",
                FestivalPart.Revels => "The festival: the revels",
                _ => "The festival: the ceremony",
            });
        ImGui.SameLine(0, 16);
        if (ImGui.SmallButton("Hide"u8))
            _festivalCardHidden = key;
        switch (f.Part)
        {
            case FestivalPart.Opening:
                TextC(Theme.Text, f.Telling ? "Gather round the well and listen: then the revels begin."
                    : f.WaitingForPlayers(_sim) ? "The crier's waiting for everyone to reach the well before the tale."
                    : "Down tools: everyone to the well in the square.");
                if (_sim.Round.Square is { } sq && ImGui.SmallButton("Look at the square"u8))
                    LookAtCell(sq);
                break;
            case FestivalPart.Revels:
            {
                // One line each: what (click to look at it), how, and how it's going.
                var spots = FestivalSpots();
                var nameW = spots.Count == 0 ? 0f : spots.Max(p => ImGui.CalcTextSize($"· {p.Name}", false, -1).X) + Gui.S(10);
                var howW = spots.Count == 0 ? 0f : spots.Max(p => ImGui.CalcTextSize(p.How, false, -1).X) + Gui.S(14);
                foreach (var spot in spots)
                {
                    ImGui.PushID(spot.Thing.Index);
                    var x = ImGui.GetCursorPosX();
                    TextC(spot.Done ? Theme.Good : Theme.Parchment, $"· {spot.Name}");
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                        LookAtCell(_sim.World.Get<Thing>(spot.Thing).Cell);
                    Gui.ItemTip("Click to look at it");
                    ImGui.SameLine(x + nameW, 0);
                    TextC(Theme.Text, spot.How);
                    if (spot.Status.Length > 0)
                    {
                        ImGui.SameLine(x + nameW + howW, 0);
                        TextC(spot.Done ? Theme.Good : Theme.Dim, spot.Status);
                    }
                    ImGui.PopID();
                }
                var toasts = (f.CeremonyTick(_sim) - _sim.Clock.Tick) / (double)SimClock.TicksPerHour;
                TextC(Theme.Dim, toasts > 1 ? $"And the feast tables. The toasts at the well in about {Math.Round(toasts)} hours."
                    : "And the feast tables. The toasts at the well within the hour.");
                break;
            }
            default:
                TextC(Theme.Text, f.Step switch
                {
                    CeremonyStep.Toasts => "The guests' toasts: hear what they made of the town.",
                    CeremonyStep.Honours => "The honours: who the town calls up tonight.",
                    CeremonyStep.Verdict or CeremonyStep.Done => "The verdict: how the season went.",
                    _ => "The bell again: gather round the well for the toasts.",
                });
                break;
        }
        DrawTaleSoFar(f);
        Panel.End();
    }

    /// <summary>
    /// The crier's tale as far as it's got, folded away: for anyone who came late or missed a line.
    /// The tale is public (who came, the crises, the dead), so it's read from the sim as it is.
    /// </summary>
    private void DrawTaleSoFar(FestivalSystem f)
    {
        var told = f.Told.ToList();
        if (told.Count == 0)
            return;
        if (!ImGui.CollapsingHeader("The tale so far"u8, ImGuiTreeNodeFlags.None))
            return;
        foreach (var line in told)
            WrappedC(Theme.Dim, line);
    }


    /// <summary>The striker: a needle sweeping the gauge; F brings the mallet down. The puck flies as high as the needle stood.</summary>
    private void DrawStriker(StrikerSession s)
    {
        BeginWork(s.Title, ("F", "swing the mallet as the needle tops the gauge"), ("Esc", "leave it"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(360, 190);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // The post: a tall track with the bell at its top and marks up its side.
        var postX = min.X + size.X * 0.72f;
        var bottom = max.Y - Gui.S(16);
        var top = min.Y + Gui.S(30);
        var trackMin = new float2(postX - Gui.S(7), top);
        var trackMax = new float2(postX + Gui.S(7), bottom);
        dl.AddRectFilled(in trackMin, in trackMax, new ImColor8(84, 58, 36, 255), Gui.S(2), ImDrawFlags.None);
        for (var k = 1; k < 10; k++)
        {
            var y = bottom - (bottom - top) * k / 10f;
            var a = new float2(postX - Gui.S(12), y);
            var b = new float2(postX - Gui.S(7), y);
            dl.AddLine(in a, in b, Theme.U8(Theme.Dim), Gui.S(1));
        }
        var bellAt = new float2(postX, top - Gui.S(10));
        var rang = s.Flight > 0 && s.Last >= StrikerSession.Bell;
        dl.AddCircleFilled(in bellAt, Gui.S(11), rang ? Theme.U8(Theme.Accent) : Theme.U8(Theme.Iron), 20);

        // The puck: up the track with the last swing, falling back as its flight ends.
        var rise = s.Flight > 0 ? s.Last * Math.Min(1f, 2f * (1f - s.Flight / (float)StrikerSession.FlightSteps) + 0.0001f) : 0f;
        var puckY = bottom - (bottom - top) * rise;
        var pMin = new float2(postX - Gui.S(9), puckY - Gui.S(8));
        var pMax = new float2(postX + Gui.S(9), puckY);
        dl.AddRectFilled(in pMin, in pMax, new ImColor8(200, 70, 50, 255), Gui.S(2), ImDrawFlags.None);
        // The best so far, a mark on the post.
        if (s.Best > 0)
        {
            var y = bottom - (bottom - top) * s.Best;
            var a = new float2(postX + Gui.S(7), y);
            var b = new float2(postX + Gui.S(18), y);
            dl.AddLine(in a, in b, Theme.U8(Theme.Good), Gui.S(2));
        }

        // The gauge: a needle swinging across an arc, the top of it in the bell's colour.
        var c = new float2(min.X + size.X * 0.3f, max.Y - Gui.S(30));
        var r = Gui.S(90);
        for (var k = 0; k < 24; k++)
        {
            float a0 = MathF.PI * (1f - k / 24f), a1 = MathF.PI * (1f - (k + 1) / 24f);
            var p0 = c + new float2(MathF.Cos(a0), -MathF.Sin(a0)) * r;
            var p1 = c + new float2(MathF.Cos(a1), -MathF.Sin(a1)) * r;
            var hot = (k + 0.5f) / 24f >= StrikerSession.Bell;
            dl.AddLine(in p0, in p1, hot ? Theme.U8(Theme.Accent) : Theme.U8(Theme.Dim), Gui.S(hot ? 6 : 3));
        }
        var needle = s.Flight > 0 ? s.Last : s.Needle;
        var na = MathF.PI * (1f - needle);
        var tip = c + new float2(MathF.Cos(na), -MathF.Sin(na)) * (r - Gui.S(6));
        dl.AddLine(in c, in tip, Theme.U8(Theme.Text), Gui.S(3));
        dl.AddCircleFilled(in c, Gui.S(5), Theme.U8(Theme.Iron), 12);

        ImGui.Spacing();
        ImGui.Text($"Swing {Math.Min(s.Swings + 1, FestivalGames.Turns)} of {FestivalGames.Turns}.");
        if (s.Best > 0)
        {
            ImGui.SameLine(0, 10);
            TextC(s.Best >= StrikerSession.Bell ? Theme.Good : Theme.Dim, s.Best >= StrikerSession.Bell ? "You rang the bell!" : $"Best: {s.Best * 100:F0}");
        }
        EndWork();
    }
}
