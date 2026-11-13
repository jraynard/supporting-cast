using HeroesNotIncluded.Rendering;

namespace HeroesNotIncluded.Tests;

public class ReloadTests
{
    [Fact]
    public void DevPathsFindTheCheckoutsDataFromTheTestBinary()
    {
        Assert.NotNull(DevPaths.Defs);
        Assert.True(File.Exists(Path.Combine(DevPaths.Defs, "core.json")));
        Assert.True(File.Exists(Path.Combine(DevPaths.VoxelShaders!, "quad.frag")));
        Assert.True(File.Exists(Path.Combine(DevPaths.EngineShaders!, "sprite.frag")));
    }

    [Fact]
    public void DefReloadWaitsForAQuietSpell()
    {
        using var reloader = new DefReloader(Path.GetTempPath(), watch: false);
        Assert.False(reloader.Update(false, 1f)); // nothing changed

        reloader.MarkChanged();
        Assert.False(reloader.Update(false, 0.2f));
        reloader.MarkChanged(); // another file of the same save: start waiting again
        Assert.False(reloader.Update(false, 0.2f));
        Assert.True(reloader.Update(false, 0.2f));
        Assert.False(reloader.Update(false, 1f)); // once

        Assert.True(reloader.Update(true, 0f)); // F8
    }

    [Fact]
    public void EditingADefFileTriggersAReload()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dw-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var reloader = new DefReloader(dir, watch: true);
            File.WriteAllText(Path.Combine(dir, "core.json"), "{}");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "ignored");
            var reloaded = false;
            for (var i = 0; i < 100 && !reloaded; i++)
            {
                Thread.Sleep(20);
                reloaded = reloader.Update(false, 0.05f);
            }
            Assert.True(reloaded, "the watcher should have seen core.json");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ShaderErrorsShowTheLineThatMatters()
    {
        var message = "Shader '/x/quad.frag' failed to compile:\nquad.frag:12: error: 'colr' : undeclared identifier\n1 error generated.\n";
        Assert.Equal("quad.frag:12: error: 'colr' : undeclared identifier", ShaderReload.Summary(message));
        Assert.Equal("file not found", ShaderReload.Summary("file not found"));
    }
}
