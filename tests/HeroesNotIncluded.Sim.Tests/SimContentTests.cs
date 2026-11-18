using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>A mod's session, command and component, as a mod would write them (this assembly stands in for its .Sim.dll).</summary>
public sealed class LuteSession : WorkSession
{
    public int Notes;
    public override Skill Skill => Skill.Crafting;
    public override float Xp => 0f;
    public override string Title => "Playing the lute";
    public override bool StillValid(Simulation sim) => true;
    public override void Finish(Simulation sim) { }
    protected override void Step() => Notes++;
    protected override void WriteState(SaveWriter w) => w.Int(Notes);
    protected override void ReadState(SaveReader r) => Notes = r.Int();
}

public sealed record StrumCommand(int Strings) : ISimCommand
{
    public void Apply(Simulation sim) { }
}

public struct Tuned
{
    public float Pitch;
}

public sealed class LuteMod : ISimMod
{
    public void Register(SimRegistry registry) =>
        registry.Component<Tuned>("Tuned", (w, t) => w.Float(t.Pitch), r => new Tuned { Pitch = r.Float() });
}

/// <summary>Content is process-wide, so these run alone.</summary>
[CollectionDefinition(nameof(SimContentTests), DisableParallelization = true)]
public sealed class SimContentCollection;

[Collection(nameof(SimContentTests))]
public class SimContentTests
{
    [Fact]
    public void TheGameAloneNamesTypesAsItAlwaysHas()
    {
        Assert.Equal("HammerSession", SimContent.NameOf(typeof(HammerSession)));
        // Every session the old hand-written list named still reads.
        foreach (var name in (string[])["HammerSession", "StewSession", "PackSession", "Healing.CastSession", "Tinkering.PinSession", "Environment.ThrowSession", "DrawSession", "FeelSession", "SledgeSession", "Afterlife.HoldSession"])
            Assert.True(WorkSession.Sessions.ContainsKey(name.Split('.')[^1]), name);
        // The carpenter's are its mod's now, and the names saves knew them by still read.
        Assert.Equal("carpenter:FellSession", SimContent.NameOf(typeof(Carpentry.FellSession)));
        Assert.True(WorkSession.Sessions.ContainsKey("carpenter:FellSession"));
        Assert.True(WorkSession.Sessions.ContainsKey("FellSession"));
        Assert.Equal("carpenter:Falling", SimContent.CodecName("Falling"));
    }

    [Fact]
    public void AModsSessionCommandAndComponentSaveUnderItsName()
    {
        var defs = TestWorld.LoadDefs();
        var coreCommands = CommandCodec.CommandTypes.Count();
        try
        {
            SimContent.Use([.. TestContent.Standard, ("lutes", typeof(LuteSession).Assembly)]);
            Assert.Equal("lutes:LuteSession", SimContent.NameOf(typeof(LuteSession)));
            Assert.True(WorkSession.Sessions.ContainsKey("lutes:LuteSession"));

            // A command travels.
            Assert.Contains(typeof(StrumCommand), CommandCodec.CommandTypes);
            Assert.True(CommandCodec.CommandTypes.Count() > coreCommands);
            var back = CommandCodec.Decode(CommandCodec.Encode(new StrumCommand(6), defs), defs);
            Assert.Equal(new StrumCommand(6), back);

            // A component saves and loads.
            var sim = Simulation.CreateStandard(defs, 8, 8, 1);
            var e = sim.World.Create();
            sim.World.Add(e, new Tuned { Pitch = 440f });
            var (loaded, _) = SaveGame.Read(SaveGame.Write(sim, new Dictionary<string, string>()), defs);
            Assert.Equal(440f, loaded.World.Get<Tuned>(e).Pitch);
        }
        finally
        {
            SimContent.Use(TestContent.Standard);
        }
        Assert.Equal(coreCommands, CommandCodec.CommandTypes.Count());
        Assert.False(WorkSession.Sessions.ContainsKey("lutes:LuteSession"));
    }
}
