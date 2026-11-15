using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Healing;

/// <summary>
/// A feel or a draw from a save made before both became one <see cref="HandsOnSession"/>: read
/// past, then dropped on the first tick (a load already stops what everyone was doing), so an old
/// save still opens. Nothing it held is kept; the wounds are just as they were.
/// </summary>
public sealed class RetiredSession(string name) : WorkSession
{
    public RetiredSession() : this("FeelSession") { }

    public override Skill Skill => Skill.Medicine;
    public override float Xp => 0f;
    public override string Title => "Laying on hands";

    public override bool StillValid(Simulation sim) => false;

    public override void Finish(Simulation sim) { }

    protected override void Step() => Done = true;

    protected override void WriteState(SaveWriter w) =>
        throw new InvalidOperationException("a retired session is never saved: it's dropped on the first tick");

    protected override void ReadState(SaveReader r)
    {
        r.Entity();
        if (name == "FeelSession")
        {
            r.String();
            var n = r.Count(256);
            for (var i = 0; i < n; i++)
            {
                r.Int(); r.Float(); r.Float(); r.Bool(); r.Float(); r.Float();
            }
            var parts = r.Count(64);
            for (var i = 0; i < parts; i++)
                r.String();
            r.Float(); r.Float(); r.Int();
            return;
        }
        // The draw.
        r.Int(); r.String();
        r.Float(); r.Float(); r.Float(); r.Float(); r.Bool(); r.Float(); r.Int();
        if (r.Version < 4)
            return;
        r.Int(); r.Float(); r.Float(); r.Bool(); r.Float(); r.Float(); r.Int(); r.Int(); r.Int();
    }
}
