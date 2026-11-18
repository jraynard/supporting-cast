using HeroesNotIncluded.Musician;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The musician, a trade built as a mod from nothing (mods/musician): turned on here, as a player would.</summary>
[Collection(nameof(SimContentTests))]
public class MusicianModTests
{
    private static readonly Guid Arin = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bea = new("00000000-0000-0000-0000-00000000000b");

    private static DefDatabase MusicianDefs() =>
        DefDatabase.LoadDirectories([.. TestWorld.DefDirectories(), Path.Combine(TestWorld.FindDefsDirectory(), "..", "..", "mods", "musician", "defs")]);

    private static T WithBard<T>(Func<T> test)
    {
        try
        {
            SimContent.Use([.. TestContent.Standard, ("musician", typeof(MusicianMod).Assembly)]);
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

    private static List<Entity> Musicians(Simulation sim) =>
        [.. sim.Creatures().Where(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Musician")];

    [Fact]
    public void NobodyIsTheBardUnlessTheyAskedToBe() => WithBard(() =>
    {
        var defs = MusicianDefs();
        Assert.True(defs.Trade("Musician").PlayerOnly);
        Assert.Same(defs.Trade("Musician"), defs.Trade("Bard")); // its name before: a professional musician, not an adventurer's class
        var sim = Round(defs, (Arin, null), (Bea, "Blacksmith"));
        Assert.Empty(Musicians(sim)); // not an AI villager, not a seat that took what was left
        return 0;
    });

    [Fact]
    public void TheBardComesToTownWithAPipeAndTheVillagersStay() => WithBard(() =>
    {
        var defs = MusicianDefs();
        var without = Round(defs, (Bea, "Blacksmith"));
        var with = Round(defs, (Arin, "Musician"), (Bea, "Blacksmith"));
        var musician = Assert.Single(Musicians(with));
        Assert.Equal(Players.CharacterOf(with, Arin), musician);
        Assert.False(MusicianModule.InstrumentOf(with, musician).IsNull, "the kit's pipe");
        // A newcomer: every villager the round would have had is still there, doing their trade.
        Assert.Equal(without.Creatures().Count(e => with.World.Has<Trade>(e)) + 1, with.Creatures().Count(e => with.World.Has<Trade>(e)));
        return 0;
    });

    [Fact]
    public void NotesArePlayedInOrderAndKeptThroughASave() => WithBard(() =>
    {
        var defs = MusicianDefs();
        var sim = Round(defs, (Arin, "Musician"));
        var me = Players.CharacterOf(sim, Arin);
        var pipe = MusicianModule.InstrumentOf(sim, me);
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
        var pipe = MusicianDefs().ModDef<InstrumentDef>(InstrumentDef.Kind, "Pipe");
        Assert.NotNull(pipe);
        Assert.Equal(["musician.pipe.d5", "musician.pipe.d6"], pipe.Octaves);
        Assert.Equal(8, pipe.Scale.Count);
        return 0;
    });

    /// <summary>A playing session with the book, worked by notes played at these steps (degree, octave above).</summary>
    private static PlaySession Played(DefDatabase defs, params (int Step, int Degree, bool Up)[] notes)
    {
        var session = new PlaySession { InstrumentName = "Pipe", Book = [.. defs.ModDefs<SongDef>(SongDef.Kind_)] };
        session.Advance([.. notes.Select(n => new WorkInput(n.Step, WorkInputKind.Custom, PlaySession.Pack(n.Degree, n.Up, true)))], notes.Max(n => n.Step) + 1);
        return session;
    }

    [Fact]
    public void ATunesOpeningFromAnyNoteStrikesItUpAndTheHandsCarryTheRest() => WithBard(() =>
    {
        var defs = MusicianDefs();
        var wheel = defs.ModDef<SongDef>(SongDef.Kind_, "MillersWheel")!;
        // Its opening (0 2 4 4 5) two steps up: 2 4 6 6 7.
        var s = Played(defs, (0, 2, false), (20, 4, false), (40, 6, false), (60, 6, false), (80, 7, false));
        Assert.Same(wheel, s.Carrying);
        Assert.Equal(2, s.Shift);
        var before = s.Played;
        s.Advance([], 80 + 60 * 60); // a minute on: the rest of the tune plays itself
        Assert.Null(s.Carrying);
        Assert.Equal(1, s.Finished);
        Assert.Equal(before + wheel.Notes.Count - PlaySession.Opening, s.Played);
        // The hands wait for the opening's last note to finish, and a breath more, before going on.
        var beat = SimClock.TicksPerSecond * 60f / wheel.Tempo;
        Assert.True(s.Notes[PlaySession.Opening].Step >= 80 + wheel.BeatsAt(PlaySession.Opening - 1) * beat + PlaySession.LeadInBreath);
        // Every carried note is the book's, two steps up.
        var carried = s.Notes.Skip(PlaySession.Opening).Select(n => n.Index).ToList();
        Assert.Equal(wheel.Notes.Skip(PlaySession.Opening).Select(n => (int)n[0] + 2).ToList(), carried);
        return 0;
    });

    [Fact]
    public void ANearMissOrALongPauseStrikesNothingUp() => WithBard(() =>
    {
        var defs = MusicianDefs();
        Assert.Null(Played(defs, (0, 0, false), (20, 2, false), (40, 4, false), (60, 4, false), (80, 6, false)).Carrying); // its fifth note wrong
        Assert.Null(Played(defs, (0, 0, false), (20, 2, false), (40, 4, false), (300, 4, false), (320, 5, false)).Carrying); // a long breath in it
        return 0;
    });

    [Fact]
    public void ANoteOfYourOwnTakesTheTuneBack() => WithBard(() =>
    {
        var defs = MusicianDefs();
        var s = Played(defs, (0, 0, false), (20, 2, false), (40, 4, false), (60, 4, false), (80, 5, false));
        Assert.NotNull(s.Carrying);
        s.Advance([new WorkInput(100, WorkInputKind.Custom, PlaySession.Pack(3, false, true))], 101);
        Assert.Null(s.Carrying);
        Assert.Equal(0, s.Finished);
        return 0;
    });

    [Fact]
    public void ThePredictorsCopyPlaysOnItsOwnNotes() => WithBard(() =>
    {
        var defs = MusicianDefs();
        var sim = Played(defs, (0, 1, false));
        var ahead = (PlaySession)sim.Clone();
        ahead.Advance([new WorkInput(5, WorkInputKind.Custom, PlaySession.Pack(3, false, true))], 6);
        Assert.Single(sim.Notes);
        Assert.Equal(2, ahead.Notes.Count);
        return 0;
    });
}
