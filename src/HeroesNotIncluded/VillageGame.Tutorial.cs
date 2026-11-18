using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Characters;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Scenarios.Lessons;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The tutorial (the title's Tutorial, or --practice TRADE): the practice yard with a guide. What
// the trade's about, then the basics (unless done before), then the trade's core chapters one after
// another, each a lesson whose scene is set up through PracticeCommand and whose step is read every
// frame from what the player knows (Lessons.Stage, through Perception only). The step at hand is
// pointed at in the world (an arrow at the screen's edge when it's off it) and its button glowed;
// a step that's taking a while offers a fuller hint and the scene set up again. Then the extra
// chapters, a round in the trade, or the yard to practise in. Nothing about it is in the sim: a
// finished chapter is kept on the saved character (CharacterProgress.Lessons).
public sealed partial class VillageGame
{
    private enum TutorialState
    {
        /// <summary>No guide: the practice yard's own panel.</summary>
        Off,

        /// <summary>What the trade's about, before the first chapter.</summary>
        Intro,

        /// <summary>A chapter under way.</summary>
        Running,

        /// <summary>A chapter done: what's worth knowing, and on to the next.</summary>
        Between,

        /// <summary>The main run through: a round, another chapter, or the yard.</summary>
        Finished,
    }

    private TutorialState _tutorial;
    private bool _tutorialBegun;

    /// <summary>
    /// The chapter's card folded away to a button at the top ("Got it"): on a Steam Deck the open
    /// card covered the room being worked in. A new step opens it again, there being something new to read.
    /// </summary>
    private bool _tutorialFolded;

    /// <summary>The run's chapters, and which one's at hand.</summary>
    private List<LessonDef> _chapters = [];
    private int _chapter;

    private LessonRun? _lesson;
    private LessonStage _lessonStage;

    /// <summary>When the step at hand came up (game-side clock): how long the player's been on it.</summary>
    private string _stepAt = "";
    private float _stepSince;

    /// <summary>
    /// The step just finished, and when: it stays on the card a few seconds, ticked, so a step the
    /// world moves on by itself (customers arriving) isn't gone before it's read.
    /// </summary>
    private string _doneStep = "";
    private float _doneAt = float.NegativeInfinity;

    /// <summary>How long a finished step stays on the card (seconds).</summary>
    private const float DoneShownFor = 5f;

    /// <summary>On one step this long (seconds), the fuller hint and "set it up again" are offered.</summary>
    private const float StuckAfter = 45f;

    /// <summary>The basics' windows as they were last frame: opening one (or closing the shop) counts.</summary>
    private bool _sawPack, _sawShop, _sawControls;

    /// <summary>Lessons finished this session: what's ticked when there's no saved character to keep them on.</summary>
    private readonly HashSet<string> _lessonsLearned = [];

    private bool Learned(string key) =>
        _lessonsLearned.Contains(key) || (Character is { } c && (_roster?.Get(c.Id) ?? c).Progress.Lessons.Contains(key));

    /// <summary>A chapter's being guided: glows show whatever the settings say (<see cref="Guiding"/>).</summary>
    /// <summary>The card's up to read (not folded to its button, and the tutorial not off): what <see cref="TutorialHold"/> holds time for.</summary>
    private bool TutorialCardOpen =>
        _sim.Round.Practice && HasAvatar && _tutorial != TutorialState.Off && !(_tutorial == TutorialState.Running && _lesson != null && _tutorialFolded);

    private bool TutorialRunning => _tutorial == TutorialState.Running && _lesson != null && !_lessonStage.Done;

    /// <summary>The step at hand, while a chapter runs ("take", "giveback"); empty otherwise.</summary>
    private string TutorialStep => TutorialRunning ? _lessonStage.Step : "";

    /// <summary>Does the step at hand want this action's button (its <see cref="LessonStepDef.Ui"/>)?</summary>
    private bool TutorialWants(string act) =>
        TutorialRunning && _lesson!.Def.IndexOf(_lessonStage.Step) is var i and >= 0 && _lesson.Def.Steps[i].Ui == act;

    private void BeginLesson(TradeDef trade, LessonDef def)
    {
        _lesson = new LessonRun(_sim, _avatar, trade, def);
        _lessonStage = Lessons.Stage(_sim, _avatar, _lesson);
        (_stepAt, _stepSince) = (_lessonStage.Step, _time);
        (_doneStep, _doneAt) = ("", float.NegativeInfinity);
        (_sawPack, _sawShop, _sawControls) = (_inventoryOpen, _showShop, _showControls);
        _tutorial = TutorialState.Running;
        SetUpLesson(trade, def);
    }

    /// <summary>The chapter's scene (customers, stock, a patient), through the yard's command; the basics need none.</summary>
    private void SetUpLesson(TradeDef trade, LessonDef def)
    {
        var index = trade.Lessons.IndexOf(def);
        if (index >= 0)
            Issue(new PracticeCommand(_avatar, PracticeAction.Lesson, index));
    }

    /// <summary>The run from the start: the trade's chapters, the basics first if they're new.</summary>
    private void StartTutorial(TradeDef trade)
    {
        _chapters = Tutorial.Chapters(_sim.Defs, trade, Learned);
        _chapter = 0;
        _lesson = null;
        _tutorial = TutorialState.Intro;
    }

    /// <summary>Where the chapter's got to, each frame; a finished one is kept on the character.</summary>
    private void UpdateLesson()
    {
        if (!_sim.Round.Practice || !HasAvatar || !_sim.World.TryGet<Trade>(_avatar, out var trade))
        {
            (_lesson, _tutorial) = (null, TutorialState.Off);
            return;
        }
        if (!_tutorialBegun)
        {
            _tutorialBegun = true;
            if (StartLesson == null)
                StartTutorial(trade.Def);
            else if (string.Equals(StartLesson, Tutorial.BasicsId, StringComparison.OrdinalIgnoreCase))
                (_chapters, _chapter) = ([_sim.Defs.Basics], 0);
            else if (trade.Def.Lessons.Find(l => string.Equals(l.Id, StartLesson, StringComparison.OrdinalIgnoreCase)) is { } wanted)
                (_chapters, _chapter) = ([wanted], 0);
            else
                Console.WriteLine($"warning: the {trade.Def.Label} has no lesson '{StartLesson}'");
            if (StartLesson != null && _chapters.Count == 1)
                BeginLesson(trade.Def, _chapters[0]);
        }
        if (_tutorial != TutorialState.Running || _lesson == null)
            return;

        // The basics' windows: the game's to know, so the run is told.
        if (_inventoryOpen && !_sawPack)
            _lesson.Passed.Add("pack");
        if (_showShop && !_sawShop)
            _lesson.Passed.Add("shop");
        if (_showControls && !_sawControls)
            _lesson.Passed.Add("controls");
        (_sawPack, _sawShop, _sawControls) = (_inventoryOpen, _showShop, _showControls);

        var was = _lessonStage.Done;
        _lessonStage = Lessons.Stage(_sim, _avatar, _lesson);
        if (_lessonStage.Step != _stepAt)
        {
            // Moved on (not back: a step come round again isn't one done).
            if (_lesson.Def.IndexOf(_lessonStage.Step) > _lesson.Def.IndexOf(_stepAt) && _lesson.Def.IndexOf(_stepAt) >= 0)
                (_doneStep, _doneAt) = (_stepAt, _time);
            (_stepAt, _stepSince, _tutorialFolded) = (_lessonStage.Step, _time, false);
            // The shop starts open: folded away for the step that teaches opening it, so J opens it rather than closing it.
            if (_lessonStage.Step == "shop" && _lesson.Def.Id == Tutorial.BasicsId)
                (_showShop, _sawShop) = (false, false);
        }
        if (_lessonStage.Done && !was)
        {
            RecordLesson(_lesson.Key);
            _tutorial = TutorialState.Between;
            return;
        }
        PointAtStep();
    }

    /// <summary>Point at the step's thing: a station ringed and named, a person or a building tagged, with an arrow when off screen.</summary>
    private void PointAtStep()
    {
        var w = _sim.World;
        var point = _lessonStage.Point;
        if (point.IsNull || !w.IsAlive(point))
            return;
        // A customer you owe a visit has a pointer already (VillageGame.Errands).
        if (_renderer.Pointers.Exists(p => p.Who == point))
            return;
        if (w.Has<Creature>(point) && w.TryGet<Position>(point, out var pos))
        {
            var name = View.CanSee(point) ? w.Get<Creature>(point).Name : "your customer";
            _renderer.Pointers.Add(new(_renderer.DrawPos(_sim, point, pos.Pos) + Rendering.WorldScale.Rise(2.4f), point, $"This one: {name}"));
        }
        else if (w.TryGet<Thing>(point, out var t))
        {
            _renderer.StationMarks.Add(t.Cell);
            _renderer.Pointers.Add(new(Position.CenterOf(t.Cell) + Rendering.WorldScale.Rise(1.4f), point, Cap(t.Def.Label)));
        }
        else if (w.TryGet<PresetBuilding>(point, out var b))
            _renderer.Pointers.Add(new(Position.CenterOf(b.Anchor) + Rendering.WorldScale.Rise(2f), point, Cap(Buildings.Called(_sim, point))));
    }

    private void RecordLesson(string key)
    {
        _lessonsLearned.Add(key);
        if (Character is not { } c)
            return;
        var roster = _roster ??= CharacterRoster.Load(Host, _sim.Defs);
        if (roster.Get(c.Id) is { } saved && !saved.Progress.Lessons.Contains(key))
            roster.Update(saved with { Progress = saved.Progress.Learned(key) });
    }

    // ---------------- The card ----------------

    /// <summary>The tutorial's card, top centre, while it's guiding; false when it's off (the yard's own panel shows).</summary>
    private bool DrawTutorial(TradeDef trade)
    {
        if (_tutorial == TutorialState.Off)
            return false;
        if (_tutorial == TutorialState.Running && _lesson != null && _tutorialFolded)
        {
            DrawTutorialButton();
            return true;
        }
        _hud.Next(Anchor.Top, ScreenFit.Width(480, 0.5f));
        Panel.Begin("Tutorial###tutorial", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar);
        _hud.Took();
        // While time holds for it, the pad drives the card (A presses, B is Got it): nothing else moves anyway.
        if (TutorialHold)
            PadWindow("###tutorial", _tutorial == TutorialState.Running && _lesson != null ? () => _tutorialFolded = true : null);
        switch (_tutorial)
        {
            case TutorialState.Intro:
                DrawTutorialIntro(trade);
                break;
            case TutorialState.Running when _lesson != null:
                DrawChapter(trade);
                break;
            case TutorialState.Between when _lesson != null:
                DrawChapterDone(trade);
                break;
            default:
                DrawTutorialFinished(trade);
                break;
        }
        Panel.End();
        return true;
    }

    /// <summary>The folded card: a button at the top saying where you are in the chapter, the step itself on hover. Pressed, the card opens.</summary>
    private void DrawTutorialButton()
    {
        var def = _lesson!.Def;
        var current = def.IndexOf(_lessonStage.Step);
        _hud.Next(Anchor.Top);
        Panel.Begin("###tutorial-folded", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoNav);
        _hud.Took();
        var label = current >= 0 ? $"Tutorial: {def.Title} ({current + 1} of {def.Steps.Count})" : $"Tutorial: {def.Title}";
        if (ImGui.Button($"{label}###tutorial-open", in _autoSize))
            _tutorialFolded = false;
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip(current >= 0 ? Controls.Keys(def.Steps[current].Text) + "\n\nPress for the card." : "Press for the card.");
        // Stuck a while on it: the card has a fuller hint, so say so.
        if (_time - _stepSince > StuckAfter)
            GuideGlow(true);
        Panel.End();
    }

    private static void CardTitle(string title)
    {
        using (Gui.DisplayFont(22f))
            ImGuiText.Colored(Theme.Text, title);
    }

    /// <summary>The card's body text: bigger than the HUD's, it's the one thing to read.</summary>
    private static void CardText(string text)
    {
        using (Gui.BodyFont(17f))
            Wrapped(Controls.Keys(text));
    }

    private void DrawTutorialIntro(TradeDef trade)
    {
        CardTitle($"The {trade.Label}");
        if (trade.TutorialIntro.Length > 0)
            CardText(trade.TutorialIntro);
        TextC(Theme.Dim, _chapters.Count switch
        {
            0 => "You've been through all of this before.",
            1 => "One chapter. Nobody's waiting: take your time.",
            _ => $"{_chapters.Count} chapters. Nobody's waiting: take your time.",
        });
        if (_chapters.Count > 0 && ImGui.Button("Begin"u8, in _autoSize))
            BeginLesson(trade, _chapters[0]);
        if (_chapters.Count > 0)
            ImGui.SameLine(0, Gui.S(8));
        if (ImGui.Button("Choose a chapter"u8, in _autoSize))
            _tutorial = TutorialState.Finished;
        ImGui.SameLine(0, Gui.S(8));
        if (ImGui.Button("Just practise"u8, in _autoSize))
            _tutorial = TutorialState.Off;
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip("The practice yard: customers when you send for them, and nothing else. The tutorial's a button away.");
    }

    private void DrawChapter(TradeDef trade)
    {
        var def = _lesson!.Def;
        CardTitle(def.Title);
        if (_chapters.Count > 1 && _chapters.Contains(def))
        {
            ImGui.SameLine(0, Gui.S(10));
            TextC(Theme.Dim, $"chapter {_chapters.IndexOf(def) + 1} of {_chapters.Count}");
        }
        var current = def.IndexOf(_lessonStage.Step);
        DrawLessonPips(def, current);
        if (_time - _doneAt < DoneShownFor && def.IndexOf(_doneStep) is var done and >= 0)
        {
            TextC(Theme.Good, "Done:");
            ImGui.SameLine(0, Gui.S(6));
            WrappedC(Theme.Dim, Controls.Keys(def.Steps[done].Text));
            TextC(Theme.Parchment, "Now:");
        }
        if (current >= 0)
        {
            var step = def.Steps[current];
            CardText(step.Text);
            if (_time - _stepSince > StuckAfter)
            {
                Gui.Rule();
                WrappedC(Theme.Parchment, Controls.Keys(step.More ?? "Taking a while? The arrow points where to go next."));
                if (trade.Lessons.Contains(def))
                {
                    if (ImGui.Button("Set it up again"u8, in _autoSize))
                    {
                        SetUpLesson(trade, def);
                        _stepSince = _time;
                    }
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                        Tip("Customers sent again, the workplace restocked, the patient brought back: whatever the chapter needs.");
                }
            }
        }
        if (TutorialHold)
            TextC(Theme.Dim, Controls.UsingPad ? $"Time holds while you read. Got it ({Controls.Glyph(Act.Back)}) carries on." : "Time holds while you read. Got it carries on.");
        if (ImGui.Button("Got it"u8, in _autoSize))
            _tutorialFolded = true;
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip("Fold the card away while you work: the Tutorial button at the top brings it back, and the next step opens it again.");
        ImGui.SameLine(0, Gui.S(8));
        if (ImGui.SmallButton("Skip this chapter"u8))
            NextChapter(trade);
        ImGui.SameLine(0, Gui.S(8));
        if (ImGui.SmallButton("Leave the tutorial"u8))
            (_lesson, _tutorial) = (null, TutorialState.Off);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip("Practise as you like. Whatever's in your hands or pack stays there; the tutorial is on the practice yard's panel.");
    }

    private void DrawChapterDone(TradeDef trade)
    {
        var def = _lesson!.Def;
        CardTitle(def.Title);
        WrappedC(Theme.Good, "Done. Nobody saw, which is how the trade usually goes.");
        if (def.Notes.Count > 0)
        {
            Gui.Rule();
            TextC(Theme.Parchment, "Worth knowing");
            foreach (var note in def.Notes)
                Wrapped("· " + Controls.Keys(note));
        }
        var more = _chapters.IndexOf(def) is var i and >= 0 && i + 1 < _chapters.Count;
        if (ImGui.Button(more ? "Next chapter" : "Carry on", in _autoSize))
            NextChapter(trade);
    }

    /// <summary>On to the run's next chapter, or the end of it.</summary>
    private void NextChapter(TradeDef trade)
    {
        var at = _lesson == null ? -1 : _chapters.IndexOf(_lesson.Def);
        _lesson = null;
        if (at >= 0 && at + 1 < _chapters.Count)
        {
            _chapter = at + 1;
            BeginLesson(trade, _chapters[_chapter]);
        }
        else
            _tutorial = TutorialState.Finished;
    }

    private void DrawTutorialFinished(TradeDef trade)
    {
        var core = Tutorial.CoreLearned(trade, Learned);
        CardTitle(core ? $"You know the {trade.Label}'s trade" : "Chapters");
        if (core)
            CardText("Enough to keep the place running. The rest you'll learn the way everyone does: with customers waiting.");
        if (Character != null && ImGui.Button($"Start a round as the {trade.Label}", in _autoSize))
            StartRoundAs(trade);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip("A whole season in Millbrook, with the regulars in the other trades.");

        Gui.Rule();
        TextC(Theme.Parchment, "Chapters");
        // Every chapter, the main run's first: any of them again, any time.
        var all = new List<LessonDef> { _sim.Defs.Basics };
        all.AddRange(trade.Lessons.Where(l => l.Core));
        all.AddRange(Tutorial.Extras(trade));
        foreach (var lesson in all)
        {
            if (ImGui.Button($"{lesson.Title}##chapter-{lesson.Id}", in _autoSize))
            {
                _chapters = [lesson];
                BeginLesson(trade, lesson);
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None) && lesson.Blurb.Length > 0)
                Tip(lesson.Blurb);
            if (Learned(LessonRun.KeyOf(trade, lesson)))
            {
                ImGui.SameLine(0, Gui.S(8));
                TextC(Theme.Good, "learned");
            }
            else if (!lesson.Core && lesson != _sim.Defs.Basics)
            {
                ImGui.SameLine(0, Gui.S(8));
                TextC(Theme.Dim, "extra");
            }
        }
        Gui.Rule();
        if (ImGui.Button("Practise freely"u8, in _autoSize))
            _tutorial = TutorialState.Off;
    }

    /// <summary>Out of the yard and into a solo round in this trade, as the same character.</summary>
    private void StartRoundAs(TradeDef trade)
    {
        if (Character is not { } c)
            return;
        var who = _roster?.Get(c.Id) ?? c;
        Host.SwitchTo(new LoadingScreen(() => new VillageGame(TitleGame.DefaultSeed) { Adventure = true, Character = who, HeroTrade = trade.Name },
            error => new TitleGame { StartError = error }));
    }

    /// <summary>A pip per step (asides only while they're the one at hand): done, now, to come.</summary>
    private static void DrawLessonPips(LessonDef def, int current)
    {
        var dl = ImGui.GetWindowDrawList();
        var r = Gui.S(4);
        var gap = Gui.S(5);
        var shown = def.Steps.Where((s, i) => !s.Aside || i == current).Count();
        var size = new float2(shown * (r * 2 + gap), r * 2 + Gui.S(4));
        var at = ImGui.GetCursorScreenPos() + new float2(r, size.Y * 0.5f);
        ImGui.Dummy(in size);
        for (var i = 0; i < def.Steps.Count; i++)
        {
            if (def.Steps[i].Aside && i != current)
                continue;
            if (i < current)
                dl.AddCircleFilled(in at, r, Theme.U8(Theme.Good), 12);
            else if (i == current)
                dl.AddCircleFilled(in at, r, Theme.U8(Theme.Accent), 12);
            else
                dl.AddCircle(in at, r, Theme.U8(Theme.Iron), 12, Gui.S(1.5f));
            at.X += r * 2 + gap;
        }
    }
}
