using HeroesNotIncluded.Mods;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Work;
using Narsil.Mods;

namespace HeroesNotIncluded.Tests;

/// <summary>A public session in an assembly that references the engine: the kind of sim type a mod mustn't ship there.</summary>
public sealed class NotDeterministicSession : WorkSession
{
    public override Sim.Components.Skill Skill => Sim.Components.Skill.Crafting;
    public override float Xp => 0f;
    public override string Title => "";
    public override bool StillValid(Simulation sim) => true;
    public override void Finish(Simulation sim) { }
    protected override void Step() { }
    protected override void WriteState(Sim.Saving.SaveWriter w) { }
    protected override void ReadState(Sim.Saving.SaveReader r) { }
}

[CollectionDefinition(nameof(ModCodeTests), DisableParallelization = true)]
public sealed class ModCodeCollection;

/// <summary>Mod assemblies, loaded from a mod folder as the game loads them. The sim's content is process-wide, so these run alone.</summary>
[Collection(nameof(ModCodeTests))]
public class ModCodeTests
{
    private static ModManifest ModIn(string dir, string id, params (string From, string As)[] dlls)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ModManifest.FileName), $$"""{ "id": "{{id}}" }""");
        foreach (var (from, name) in dlls)
            File.Copy(from, Path.Combine(dir, name));
        return ModManifest.Load(dir);
    }

    private static string ExampleDll()
    {
        var config = Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd('/', '\\'))!); // bin/<config>/net10.0
        return Path.GetFullPath(Path.Combine(DevPaths.Defs!, "..", "..", "tests", "ExampleMod.Sim", "bin", config, "net10.0", "ExampleMod.Sim.dll"));
    }

    [Fact]
    public void ASimHalfLoadsAndItsSystemTakesItsPlace()
    {
        var root = Directory.CreateTempSubdirectory("hni-modcode").FullName;
        try
        {
            var dll = ExampleDll();
            Assert.True(File.Exists(dll), dll);
            // With a copy of an engine assembly beside it, as a build leaves one: that stays the game's.
            var mod = ModIn(Path.Combine(root, "example"), "example", (dll, "ExampleMod.Sim.dll"), (typeof(SimRandom).Assembly.Location, "Narsil.Sim.dll"));
            Assert.True(mod.RunsCode);

            var problems = new List<string>();
            var failed = new HashSet<string>();
            var halves = ModCode.Load(new ModOrder([mod], []), problems, failed);
            Assert.Empty(problems);
            var (id, asm) = Assert.Single(halves.Sim);
            Assert.Equal("example", id);
            Assert.Empty(halves.Game);

            SimContent.Use(halves.Sim);
            var session = asm.GetType("ExampleMod.StrumSession")!;
            Assert.Equal("example:StrumSession", SimContent.NameOf(session));
            Assert.True(WorkSession.Sessions.ContainsKey("example:StrumSession"));

            var sim = Simulation.CreateStandard(Sim.Defs.DefDatabase.LoadDirectory(DevPaths.Defs!), 8, 8, 1);
            var names = sim.Systems.Select(s => s.Name).ToList();
            Assert.Equal(names.IndexOf("AI") - 1, names.IndexOf("Metronome"));
        }
        finally
        {
            SimContent.Use(TestContent.Standard);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SimTypesBesideTheEngineAreRefused()
    {
        var root = Directory.CreateTempSubdirectory("hni-modcode").FullName;
        try
        {
            var mod = ModIn(Path.Combine(root, "bad"), "bad", (typeof(ModCodeTests).Assembly.Location, "BadMod.dll"));
            var problems = new List<string>();
            var failed = new HashSet<string>();
            var halves = ModCode.Load(new ModOrder([mod], []), problems, failed);
            Assert.Contains("bad", failed);
            Assert.Contains(problems, p => p.Contains("NotDeterministicSession") && p.Contains("sim half must reference only"));
            Assert.Empty(halves.Sim);
            Assert.Empty(halves.Game);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
