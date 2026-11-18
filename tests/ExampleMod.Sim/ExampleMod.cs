using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Work;

namespace ExampleMod;

/// <summary>A mini-game as a mod writes one: a count of strums, saved with the session.</summary>
public sealed class StrumSession : WorkSession
{
    public int Strums;
    public override Skill Skill => Skill.Crafting;
    public override float Xp => 0f;
    public override string Title => "Strumming";
    public override bool StillValid(Simulation sim) => true;
    public override void Finish(Simulation sim) { }
    protected override void Step() => Strums++;
    protected override void WriteState(SaveWriter w) => w.Int(Strums);
    protected override void ReadState(SaveReader r) => Strums = r.Int();
}

/// <summary>A system a mod adds: counts its ticks, placed just before the AI decides.</summary>
public sealed class MetronomeSystem : ISimSystem
{
    public int Beats;
    public string Name => "Metronome";
    public int Interval => 1;
    public void Update(Simulation sim) => Beats++;
}

public sealed class Example : ISimMod
{
    public void Register(SimRegistry registry) => registry.System(() => new MetronomeSystem(), before: "AI");
}
