using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;

namespace HeroesNotIncluded.Tests;

public sealed class SaveSlotsTests : IDisposable
{
    private static readonly Lazy<DefDatabase> Defs = new(() => DefDatabase.LoadDirectory(FindDefs()));
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dw-saves-" + Guid.NewGuid().ToString("N"));
    private static readonly Dictionary<string, string> NoMeta = [];

    public SaveSlotsTests() => SaveSlots.Directory = _dir;

    public void Dispose()
    {
        SaveSlots.Directory = null;
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static Simulation Village()
    {
        var map = Defs.Value.Map("Millbrook");
        var sim = MapBuilder.CreateSimulation(Defs.Value, map, 3);
        MapBuilder.Build(sim, map);
        sim.TickMany(300);
        return sim;
    }

    private static string FindDefs()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "data", "defs")))
                return Path.Combine(dir.FullName, "data", "defs");
        throw new DirectoryNotFoundException("data/defs");
    }

    [Fact]
    public void WritesListsAndLoads()
    {
        var sim = Village();
        Assert.Null(SaveSlots.Write("Day 1", sim, new Dictionary<string, string> { ["cutaway"] = "5" }));
        File.SetLastWriteTimeUtc(SaveSlots.PathOf("Day 1"), DateTime.UtcNow.AddMinutes(-5));
        Assert.Null(SaveSlots.Write(SaveSlots.Quicksave, sim, NoMeta));

        var slots = SaveSlots.List();
        Assert.Equal([SaveSlots.Quicksave, "Day 1"], slots.Select(s => s.Name));
        Assert.Equal("5", slots[1].Header!.Meta["cutaway"]);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));

        var loaded = SaveSlots.Load(slots[1].Path, Defs.Value);
        Assert.Equal("Day 1", loaded.SlotName);
        Assert.Equal(sim.Clock.Tick, loaded.Sim.Clock.Tick);
    }

    [Fact]
    public async Task BackgroundSaveMatchesTheGameWhenItWasTaken()
    {
        var sim = Village();
        Sim.Systems.AiSystem.InterruptAll(sim); // what a load does to jobs (SaveLoadTests' contract)
        var hash = sim.StateHash();
        var tick = sim.Clock.Tick;
        var (error, done) = SaveSlots.WriteInBackground("bg", sim, NoMeta);
        Assert.Null(error);
        // The game keeps playing while the file is written: the save is of the moment it was taken.
        sim.TickMany(120);
        Assert.Null(await done!);
        var loaded = SaveSlots.Load(SaveSlots.PathOf("bg"), Defs.Value);
        Assert.Equal(tick, loaded.Sim.Clock.Tick);
        Assert.Equal(hash, loaded.Sim.StateHash());
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void AutosavesRotateThroughThreeSlots()
    {
        var sim = Village();
        var used = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var slot = SaveSlots.NextAutosave();
            used.Add(slot);
            Assert.Null(SaveSlots.Write(slot, sim, NoMeta));
            File.SetLastWriteTimeUtc(SaveSlots.PathOf(slot), DateTime.UtcNow.AddMinutes(i - 10));
        }
        Assert.Equal(["autosave-1", "autosave-2", "autosave-3", "autosave-1"], used);
        Assert.All(SaveSlots.List(), s => Assert.True(s.IsAutosave));
    }

    [Fact]
    public void UnreadableFilesAreListedButNotContinued()
    {
        var sim = Village();
        Assert.Null(SaveSlots.Write("good", sim, NoMeta));
        File.SetLastWriteTimeUtc(SaveSlots.PathOf("good"), DateTime.UtcNow.AddMinutes(-5));
        File.WriteAllText(SaveSlots.PathOf("broken"), "nope");

        var broken = Assert.Single(SaveSlots.List(), s => s.Name == "broken");
        Assert.Null(broken.Header);
        Assert.NotNull(broken.Error);
        Assert.Equal("good", SaveSlots.Newest()?.Name);
        Assert.Throws<SaveException>(() => SaveSlots.Load(broken.Path, Defs.Value));
    }

    [Theory]
    [InlineData("  My village ", "My village")]
    [InlineData("a/b", "a_b")]
    [InlineData("..", "")]
    public void CleansNames(string typed, string expected) => Assert.Equal(expected, SaveSlots.Clean(typed));
}
