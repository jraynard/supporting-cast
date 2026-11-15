using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Characters;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Scenarios.Lessons;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The practice yard's lessons: one of the trade's games at a time, its scene set up through
// PracticeCommand, the step at hand read every frame from what the player knows (Lessons.Stage,
// through Perception only) and its station ringed in the world, a customer flagged. Nothing about
// a lesson is in the sim: a finished one is kept on the saved character (CharacterProgress.Lessons).
public sealed partial class VillageGame
{
    private LessonRun? _lesson;
    private LessonStage _lessonStage;
    private bool _startedLesson;

    /// <summary>Lessons finished this session: what's ticked when there's no saved character to keep them on.</summary>
    private readonly HashSet<string> _lessonsLearned = [];

    private bool Learned(string key) =>
        _lessonsLearned.Contains(key) || (Character is { } c && (_roster?.Get(c.Id) ?? c).Progress.Lessons.Contains(key));

    private void BeginLesson(TradeDef trade, int index)
    {
        _lesson = new LessonRun(_sim, _avatar, trade, trade.Lessons[index]);
        _lessonStage = Lessons.Stage(_sim, _avatar, _lesson);
        Issue(new PracticeCommand(_avatar, PracticeAction.Lesson, index));
    }

    /// <summary>Where the lesson's got to, each frame; a finished one is kept on the character.</summary>
    private void UpdateLesson()
    {
        if (StartLesson != null && !_startedLesson && _sim.Round.Practice && HasAvatar && _sim.World.TryGet<Trade>(_avatar, out var trade))
        {
            _startedLesson = true;
            var index = trade.Def.Lessons.FindIndex(l => string.Equals(l.Id, StartLesson, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                BeginLesson(trade.Def, index);
            else
                Console.WriteLine($"warning: the {trade.Def.Label} has no lesson '{StartLesson}'");
        }
        if (_lesson == null)
            return;
        if (!_sim.Round.Practice || !HasAvatar)
        {
            _lesson = null;
            return;
        }
        var was = _lessonStage.Done;
        _lessonStage = Lessons.Stage(_sim, _avatar, _lesson);
        if (_lessonStage.Done && !was)
            RecordLesson(_lesson.Key);
        if (_lessonStage.Done)
            return;
        // Point at it: a ring on a station, a flag over a person.
        var w = _sim.World;
        var point = _lessonStage.Point;
        if (point.IsNull || !w.IsAlive(point))
            return;
        if (w.Has<Creature>(point) && w.TryGet<Position>(point, out var pos))
        {
            // A customer you owe a visit has a pointer already (VillageGame.Errands).
            if (View.CanSee(point) && !_renderer.Pointers.Exists(p => p.Who == point))
                _renderer.Markers.Add(new(_renderer.DrawPos(_sim, point, pos.Pos) + Rendering.WorldScale.Rise(2.4f), "This one", false));
        }
        else if (w.TryGet<Thing>(point, out var t))
            _renderer.StationMarks.Add(t.Cell);
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

    /// <summary>The practice panel's lessons: a list to pick from, or the one under way, step by step.</summary>
    private void DrawLessons(TradeDef trade)
    {
        if (trade.Lessons.Count == 0)
            return;
        Gui.Rule();
        if (_lesson == null || _lesson.Trade != trade)
        {
            _lesson = null;
            TextC(Theme.Parchment, "Lessons");
            for (var i = 0; i < trade.Lessons.Count; i++)
            {
                var lesson = trade.Lessons[i];
                if (ImGui.Button($"{lesson.Title}##lesson-{lesson.Id}"))
                    BeginLesson(trade, i);
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip(lesson.Blurb);
                if (Learned(LessonRun.KeyOf(trade, lesson)))
                {
                    ImGui.SameLine(0, Gui.S(8));
                    TextC(Theme.Good, "learned");
                }
            }
            return;
        }

        var def = _lesson.Def;
        TextC(Theme.Parchment, def.Title);
        if (_lessonStage.Done)
        {
            WrappedC(Theme.Good, "Done. Nobody saw, which is how the trade usually goes.");
            if (ImGui.Button("Back to the lessons"u8))
                _lesson = null;
            return;
        }
        var current = def.IndexOf(_lessonStage.Step);
        DrawLessonPips(def, current);
        if (current >= 0)
            Wrapped(Controls.Keys(def.Steps[current].Text));
        if (ImGui.Button("Stop the lesson"u8))
            _lesson = null;
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip("Whatever's in your tongs or pack stays there.");
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
