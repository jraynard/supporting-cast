using HeroesNotIncluded.Bard;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The bard, a trade built as a mod from nothing (mods/bard): turned on here, as a player would.</summary>
[Collection(nameof(SimContentTests))]
public class BardModTests
{
    private static readonly Guid Arin = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bea = new("00000000-0000-0000-0000-00000000000b");

    private static DefDatabase BardDefs() =>
        DefDatabase.LoadDirectories([.. TestWorld.DefDirectories(), Path.Combine(TestWorld.FindDefsDirectory(), "..", "..", "mods", "bard", "defs")]);

    private static T WithBard<T>(Func<T> test)
    {
        try
        {
            SimContent.Use([.. TestContent.Standard, ("bard", typeof(BardMod).Assembly)]);
            return test();
        }
        finally
        {
            SimContent.Use(TestContent.Standard);
        }
    }

    private static Simulation Round(DefDatabase defs, params (Guid Id, string? Wants)[] seats)
    {
        var sim = VillageRound.NewSimulation(defs, 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        foreach (var (id, wants) in seats)
        {
            sim.ApplyNow(new JoinLobbyCommand(id, id.ToString()[^1..], default));
            if (wants != null)
                sim.ApplyNow(new SetTradePreferencesCommand(id, [sim.Defs.Trade(wants)], []));
        }
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    private static List<Entity> Bards(Simulation sim) =>
        [.. sim.Creatures().Where(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Bard")];

    [Fact]
    public void NobodyIsTheBardUnlessTheyAskedToBe() => WithBard(() =>
    {
        var defs = BardDefs();
        Assert.True(defs.Trade("Bard").PlayerOnly);
        var sim = Round(defs, (Arin, null), (Bea, "Blacksmith"));
        Assert.Empty(Bards(sim)); // not an AI villager, not a seat that took what was left
        return 0;
    });

    [Fact]
    public void TheBardComesToTownWithAPipeAndTheVillagersStay() => WithBard(() =>
    {
        var defs = BardDefs();
        var without = Round(defs, (Bea, "Blacksmith"));
        var with = Round(defs, (Arin, "Bard"), (Bea, "Blacksmith"));
        var bard = Assert.Single(Bards(with));
        Assert.Equal(Players.CharacterOf(with, Arin), bard);
        Assert.False(BardModule.InstrumentOf(with, bard).IsNull, "the kit's pipe");
        // A newcomer: every villager the round would have had is still there, doing their trade.
        Assert.Equal(without.Creatures().Count(e => with.World.Has<Trade>(e)) + 1, with.Creatures().Count(e => with.World.Has<Trade>(e)));
        return 0;
    });

    [Fact]
    public void NotesArePlayedInOrderAndKeptThroughASave() => WithBard(() =>
    {
        var defs = BardDefs();
        var sim = Round(defs, (Arin, "Bard"));
        var me = Players.CharacterOf(sim, Arin);
        var pipe = BardModule.InstrumentOf(sim, me);
        WorkSystem.Start(sim, me, new PlaySession { Instrument = pipe, InstrumentName = "Pipe" });
        var session = (PlaySession)sim.World.Get<WorkSession>(me);
        session.Advance([
            new WorkInput(0, WorkInputKind.Custom, PlaySession.Pack(0, false, true)),
            new WorkInput(3, WorkInputKind.Custom, PlaySession.Pack(2, false, true)),
            new WorkInput(3, WorkInputKind.Custom, PlaySession.Pack(4, true, true)),
            new WorkInput(5, WorkInputKind.Custom, PlaySession.Pack(9, false, true)), // off the scale: no note
            new WorkInput(6, WorkInputKind.Custom, PlaySession.Pack(1, false, false)), // let go: no note
        ], 10);
        Assert.Equal([new NoteEvent(0, 0, 0), new NoteEvent(3, 2, 0), new NoteEvent(3, 4, 1)], session.Notes);

        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), defs);
        var back = Assert.IsType<PlaySession>(loaded.World.Get<WorkSession>(me));
        Assert.Equal(session.Notes, back.Notes);
        Assert.Equal(3, back.Played);
        return 0;
    });

    [Fact]
    public void TheInstrumentIsTheModsOwnKindOfDef() => WithBard(() =>
    {
        var pipe = BardDefs().ModDef<InstrumentDef>(InstrumentDef.Kind, "Pipe");
        Assert.NotNull(pipe);
        Assert.Equal(["bard.pipe.d5", "bard.pipe.d6"], pipe.Octaves);
        Assert.Equal(8, pipe.Scale.Count);
        return 0;
    });
}
