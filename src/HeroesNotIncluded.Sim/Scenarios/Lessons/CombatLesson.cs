using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;

namespace HeroesNotIncluded.Sim.Scenarios.Lessons;

/// <summary>
/// The fight (trades.json's shared "combat"), the same for every trade, bare knuckles in the
/// practice yard (<see cref="Sparring"/>): fists up, a blow landed on the training dummy, then a
/// round with the sparring partner: one of his punches blocked, one parried, one dodged. Each is
/// read from the blows just thrown (<see cref="Simulation.RecentBlows"/>, all in plain sight) and
/// kept in <see cref="LessonRun.Passed"/>, so they count in whatever order they come.
/// </summary>
public static class CombatLesson
{
    public const string Id = "combat";

    public static readonly Lesson Lesson = new(BasicsLesson.AnyTrade, Id, ["draw", "dummy", "brawl", "block", "parry", "dodge"], Stage);

    public static LessonStage Stage(Simulation sim, Entity me, LessonRun run)
    {
        var w = sim.World;
        var (dummy, partner) = Sparring.Of(sim);
        foreach (var b in sim.RecentBlows)
        {
            if (b.Tick < run.StartTick)
                continue;
            if (b.Attacker == me && b.Target == dummy && !dummy.IsNull && b.Outcome == BlowOutcome.Hit)
                run.Passed.Add("dummy");
            if (b.Target != me || b.Attacker != partner || partner.IsNull)
                continue;
            run.Passed.Add("brawl"); // he's swinging at you: you found him
            switch (b.Outcome)
            {
                case BlowOutcome.Blocked:
                    run.Passed.Add("block");
                    break;
                case BlowOutcome.Parried:
                    run.Passed.Add("parry");
                    break;
                case BlowOutcome.Dodged or BlowOutcome.Miss: // dashed clear, or stepped out of it
                    run.Passed.Add("dodge");
                    break;
            }
        }
        // He's squared up to you (on his toes, fighting freely), or swinging: you found him.
        if (!partner.IsNull && (w.Has<FreeMover>(partner) || w.TryGet<Windup>(partner, out var punch) && punch.Target == me))
            run.Passed.Add("brawl");
        var up = w.TryGet<FreeMover>(me, out var fm) && fm.Drawn;
        if (up)
            run.Passed.Add("draw");
        if (!run.Passed.Contains("draw"))
            return new LessonStage("draw", dummy);
        if (!run.Passed.Contains("dummy"))
            return new LessonStage("dummy", dummy);
        if (!run.Passed.Contains("brawl"))
            return new LessonStage("brawl", partner);
        foreach (var step in (ReadOnlySpan<string>)["block", "parry", "dodge"])
            if (!run.Passed.Contains(step))
                return new LessonStage(step, partner);
        return new LessonStage("dodge", partner, Done: true);
    }
}
