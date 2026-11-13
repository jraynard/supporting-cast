using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Saving;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The log keeps its newest messages (it's in every save and co-op snapshot) and still counts them all.</summary>
public class MessageLogTests
{
    [Fact]
    public void KeepsTheNewestAndCountsThemAll()
    {
        var sim = TestWorld.Create();
        for (var i = 0; i < 2000; i++)
            sim.Messages.Add(MessageKind.Info, $"message {i}");

        var kept = sim.Messages.Messages;
        Assert.InRange(kept.Count, MessageLog.MaxKept, MessageLog.MaxKept + MessageLog.TrimBatch);
        Assert.Equal("message 1999", kept[^1].Text);
        Assert.Equal(2000, sim.Messages.Total);
        Assert.Equal(kept.Count - 3, sim.Messages.IndexAfter(1997)); // the last three are new
        Assert.Equal(0, sim.Messages.IndexAfter(0)); // everything kept is newer than that

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(2000, loaded.Messages.Total);
        Assert.Equal(kept.Select(m => m.Text), loaded.Messages.Messages.Select(m => m.Text));
    }

    [Fact]
    public void OldThrottlesAreForgottenWhenTheLogTrims()
    {
        var sim = TestWorld.Create();
        sim.Messages.AddThrottled("cold:1", SimClock.TicksPerHour, MessageKind.Warning, "cold hands");
        sim.TickMany((int)MessageLog.CooldownKeptTicks + 1);
        for (var i = 0; i < MessageLog.MaxKept + MessageLog.TrimBatch; i++)
            sim.Messages.Add(MessageKind.Info, "filler");
        Assert.False(sim.Messages.Cooldowns.ContainsKey("cold:1"));
        // Its hour is long past either way, so it posts again as it would have.
        sim.Messages.AddThrottled("cold:1", SimClock.TicksPerHour, MessageKind.Warning, "cold hands again");
        Assert.Equal("cold hands again", sim.Messages.Messages[^1].Text);
    }
}
