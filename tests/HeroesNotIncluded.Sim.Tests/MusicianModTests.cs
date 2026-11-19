using HeroesNotIncluded.Musician;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Speech;
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

    private static T WithMusician<T>(Func<T> test)
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
    public void NobodyIsTheMusicianUnlessTheyAskedToBe() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        Assert.True(defs.Trade("Musician").PlayerOnly);
        Assert.Same(defs.Trade("Musician"), defs.Trade("Bard")); // its name before: a professional musician, not an adventurer's class
        var sim = Round(defs, (Arin, null), (Bea, "Blacksmith"));
        Assert.Empty(Musicians(sim)); // not an AI villager, not a seat that took what was left
        return 0;
    });

    [Fact]
    public void TheMusicianComesToTownWithAPipeAndTheVillagersStay() => WithMusician(() =>
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
    public void NotesArePlayedInOrderAndKeptThroughASave() => WithMusician(() =>
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
    public void TheInstrumentIsTheModsOwnKindOfDef() => WithMusician(() =>
    {
        var pipe = MusicianDefs().ModDef<InstrumentDef>(InstrumentDef.Kind, "Pipe");
        Assert.NotNull(pipe);
        Assert.Equal(["musician.pipe.d5", "musician.pipe.d6"], pipe.Octaves);
        Assert.Equal(8, pipe.Scale.Count);
        // Its keys named as a pipe in D plays them: D5 to D6, the octave above, a semitone up.
        Assert.Equal(["D5", "E5", "F#5", "G5", "A5", "B5", "C#6", "D6"], Enumerable.Range(0, 8).Select(i => pipe.NoteName(i, 0, 0)));
        Assert.Equal("A6", pipe.NoteName(4, 1, 0));
        Assert.Equal("D6", pipe.NoteName(6, 0, 1)); // C# up a semitone
        Assert.Equal("F5", pipe.NoteName(1, 0, 1));
        return 0;
    });

    /// <summary>A playing session with the book, in the given mode, worked by notes played at these steps (degree, octave above).</summary>
    private static PlaySession Played(DefDatabase defs, PlayMode mode, params (int Step, int Degree, bool Up)[] notes)
    {
        var session = new PlaySession { InstrumentName = "Pipe", Book = [.. defs.ModDefs<SongDef>(SongDef.Kind_)] };
        List<WorkInput> inputs = mode == PlayMode.Book ? [new WorkInput(0, WorkInputKind.Act)] : [];
        inputs.AddRange(notes.Select(n => new WorkInput(n.Step, WorkInputKind.Custom, PlaySession.Pack(n.Degree, n.Up, true))));
        session.Advance(inputs, notes.Max(n => n.Step) + 1);
        return session;
    }

    /// <summary>A tune's call played as written (each note 20 steps on), from <paramref name="shift"/> steps up, at <paramref name="from"/>.</summary>
    private static List<WorkInput> Opening(SongDef tune, int shift = 0, int from = 0) =>
        [.. tune.Call.Select((note, i) =>
        {
            var index = note.Step + shift;
            return new WorkInput(from + i * 20, WorkInputKind.Custom, PlaySession.Pack(index <= 7 ? index : index - 7, index > 7, true, note.Accidental > 0));
        })];

    /// <summary>What a musician minds while the hands carry a tune: the beat, the breath, the long notes.</summary>
    [Flags]
    private enum Minding { None = 0, Beat = 1, Breath = 2, Swell = 4, All = Beat | Breath | Swell }

    /// <summary>Play on to <paramref name="upTo"/> (or the tune's end), minding what's asked: F on every beat, a breath at each mark, F held through each long note.</summary>
    private static void KeepTime(PlaySession s, int upTo, Minding minding = Minding.All)
    {
        while (s.StepsDone < upTo)
        {
            List<WorkInput> inputs = [];
            var carried = s.Carrying != null;
            if (carried && minding.HasFlag(Minding.Beat) && !s.BeatKept && !s.Held && s.StepsDone >= (int)MathF.Ceiling(s.Beat)) // held through a long note: no tap till it ends
                inputs.Add(new WorkInput(s.StepsDone, WorkInputKind.Press));
            var breathe = carried && minding.HasFlag(Minding.Breath) && s.InGap && !(s.GapTaken && s.Breath >= 1f);
            if (breathe != s.Breathing)
                inputs.Add(new WorkInput(s.StepsDone, WorkInputKind.Custom, PlaySession.PackBreath(breathe)));
            var swell = carried && minding.HasFlag(Minding.Swell) && s.LongAt >= 0 && s.RingingNote != null;
            if (swell != s.Held)
                inputs.Add(new WorkInput(s.StepsDone, WorkInputKind.Hold, swell ? 1 : 0));
            s.Advance(inputs, s.StepsDone + 1);
        }
    }

    [Fact]
    public void ANoteHeldRingsTillItsKeyIsLetGo() => WithMusician(() =>
    {
        var s = new PlaySession { InstrumentName = "Pipe" };
        s.Advance([new WorkInput(0, WorkInputKind.Custom, PlaySession.Pack(4, false, true))], 120);
        Assert.Equal(4, s.RingingNote?.Degree); // two seconds on, still sounding
        s.Advance([new WorkInput(120, WorkInputKind.Custom, PlaySession.Pack(2, false, false))], 121);
        Assert.NotNull(s.RingingNote); // another key let go: not this one
        s.Advance([new WorkInput(121, WorkInputKind.Custom, PlaySession.Pack(4, false, false))], 122);
        Assert.Null(s.RingingNote);
        // A new note takes over from one still held (one breath, one note), and letting the old key go then changes nothing.
        s.Advance([new WorkInput(130, WorkInputKind.Custom, PlaySession.Pack(0, false, true)), new(140, WorkInputKind.Custom, PlaySession.Pack(5, false, true)),
            new(150, WorkInputKind.Custom, PlaySession.Pack(0, false, false))], 160);
        Assert.Equal(5, s.RingingNote?.Degree);
        return 0;
    });

    [Fact]
    public void TheHandsHoldEachNoteForItsLengthAndLiftBeforeABreath() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var sailor = defs.ModDef<SongDef>(SongDef.Kind_, "DrunkenSailor")!;
        var s = new PlaySession { InstrumentName = "Pipe", Book = [sailor] };
        s.Advance([new WorkInput(0, WorkInputKind.Act), .. Opening(sailor, from: 1)], 150);
        Assert.Same(sailor, s.Carrying);
        var perBeat = s.StepsPerBeat;
        // From the first carried note on: it rings most of its beat, then lifts just before the next.
        KeepTime(s, (int)MathF.Ceiling(s.Due) + 1);
        var first = s.Played;
        KeepTime(s, s.StepsDone + (int)(perBeat * 0.8f));
        Assert.Equal(first, s.Ringing);
        KeepTime(s, s.StepsDone + (int)(perBeat * 0.18f));
        Assert.True(s.Ringing == -1 || s.Ringing > first);
        // The line's last note, with a breath marked after it, lifts halfway through: the gap's for the breath.
        while (!(s.Carrying!.Sequence[s.Next - 1].BreathAfter && s.Ringing == s.Played))
            KeepTime(s, s.StepsDone + 1);
        KeepTime(s, s.StepsDone + (int)(perBeat * PlaySession.BeforeBreath) + 1);
        Assert.Equal(-1, s.Ringing);
        return 0;
    });

    [Fact]
    public void InFreePlayNoOpeningStrikesUpATune() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var wheel = defs.ModDef<SongDef>(SongDef.Kind_, "MillersWheel")!;
        var s = new PlaySession { InstrumentName = "Pipe", Book = [.. defs.ModDefs<SongDef>(SongDef.Kind_)] };
        s.Advance(Opening(wheel), 200);
        Assert.Equal(PlayMode.Free, s.Mode);
        Assert.Null(s.Carrying);
        Assert.Equal(wheel.Opening, s.Played); // every note heard, as your own
        // Nor do the notes played free count once the book's opened: the opening must be played in the book.
        s.Advance([new WorkInput(200, WorkInputKind.Act)], 220);
        Assert.Equal(PlayMode.Book, s.Mode);
        Assert.Null(s.Carrying);
        return 0;
    });

    [Fact]
    public void ATunesOpeningFromAnyNoteStrikesItUpAndTheHandsCarryTheRest() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var wheel = defs.ModDef<SongDef>(SongDef.Kind_, "MillersWheel")!;
        Assert.Equal(wheel.Notes.Count(n => (int)n[0] != SongDef.BreathMark), wheel.Sequence.Count); // a tune of one part, written the short way
        // Its opening (0 2 4 4 5) two steps up: 2 4 6 6 7.
        var s = Played(defs, PlayMode.Book, (0, 2, false), (20, 4, false), (40, 6, false), (60, 6, false), (80, 7, false));
        Assert.Same(wheel, s.Carrying);
        Assert.Equal(2, s.Shift);
        var before = s.Played;
        KeepTime(s, 80 + 60 * 60); // a minute on, keeping time: the rest of the tune plays itself
        Assert.Null(s.Carrying);
        Assert.Equal(1, s.Finished);
        Assert.Equal(0, s.Slipped);
        Assert.Equal(before + wheel.Sequence.Count - wheel.Opening, s.Played);
        Assert.True(s.BeatsKept > 10);
        Assert.Equal(0, s.BeatsMissed);
        // The hands wait for the opening's last note to finish, and a breath more, before going on.
        var beat = SimClock.TicksPerSecond * 60f / wheel.Tempo;
        Assert.True(s.Notes[wheel.Opening].Step >= 80 + wheel.BeatsAt(wheel.Opening - 1) * beat + PlaySession.LeadInBreath);
        // Every carried note is the book's, two steps up.
        var carried = s.Notes.Skip(wheel.Opening).Select(n => n.Index).ToList();
        Assert.Equal(wheel.Sequence.Skip(wheel.Opening).Select(n => n.Step + 2).ToList(), carried);
        return 0;
    });

    [Fact]
    public void ANearMissALongPauseOrPlayingOnStrikesNothingUp() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        Assert.Null(Played(defs, PlayMode.Book, (0, 0, false), (20, 2, false), (40, 4, false), (60, 4, false), (80, 6, false)).Carrying); // its fifth note wrong
        Assert.Null(Played(defs, PlayMode.Book, (0, 0, false), (20, 2, false), (40, 4, false), (300, 4, false), (320, 5, false)).Carrying); // a long breath in it
        // Playing on from a phrase of your own: the opening must start after a quiet.
        Assert.Null(Played(defs, PlayMode.Book, (0, 3, false), (20, 0, false), (40, 2, false), (60, 4, false), (80, 4, false), (100, 5, false)).Carrying);
        Assert.NotNull(Played(defs, PlayMode.Book, (0, 3, false), (200, 0, false), (220, 2, false), (240, 4, false), (260, 4, false), (280, 5, false)).Carrying);
        return 0;
    });

    [Fact]
    public void LosingTheBeatLosesConcentrationTillTheTuneSlipsAway() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var sailor = defs.ModDef<SongDef>(SongDef.Kind_, "DrunkenSailor")!;
        var s = new PlaySession { InstrumentName = "Pipe", Book = [sailor] };
        s.Advance([new WorkInput(0, WorkInputKind.Act), .. Opening(sailor, from: 1)], 200);
        Assert.Same(sailor, s.Carrying);
        KeepTime(s, 200 + 60 * 60, Minding.Breath | Minding.Swell); // breathing and shaping, but not a beat kept
        Assert.Null(s.Carrying);
        Assert.Equal(1, s.Slipped);
        Assert.Equal(0, s.Finished);
        Assert.True(s.BeatsMissed >= 5);
        Assert.True(s.Dropped > 0, "notes go missing as the mind wanders");
        Assert.Equal(PlayMode.Book, s.Mode); // still in the book, to start it again
        return 0;
    });

    [Fact]
    public void DrunkenSailorPlaysEveryVerseAndChorusMindedThroughout() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var sailor = defs.ModDef<SongDef>(SongDef.Kind_, "DrunkenSailor")!;
        Assert.Equal(["Verse", "Chorus", "Verse", "Chorus", "Verse", "Chorus"], sailor.Form);
        var marks = sailor.Parts.Values.Sum(p => p.Count(n => (int)n[0] == SongDef.BreathMark));
        Assert.Equal(3 * (sailor.Parts["Verse"].Count + sailor.Parts["Chorus"].Count - marks), sailor.Sequence.Count);
        Assert.Equal(3 * 8, sailor.Sequence.Count(n => n.BreathAfter)); // every line's end, every time round
        var beats = sailor.Sequence.Sum(n => n.Beats);
        Assert.InRange(beats * 60f / sailor.Tempo, 80f, 120f); // a minute and a half or so, sung through
        // Called by its chorus, from the book; the hands play it from the verse.
        Assert.Equal((7, 0), (sailor.Call.Count, sailor.CarriesFrom));
        Assert.Equal([5, 5, 5, 5, 1, 3, 5], sailor.Call.Select(n => n.Step));
        var s = new PlaySession { InstrumentName = "Pipe", Book = [.. defs.ModDefs<SongDef>(SongDef.Kind_)] };
        s.Advance([new WorkInput(0, WorkInputKind.Act), .. Opening(sailor, from: 1)], 200);
        Assert.Same(sailor, s.Carrying);
        var sections = new HashSet<int>();
        while (s.Carrying != null)
        {
            sections.Add(s.Section);
            KeepTime(s, s.StepsDone + 30);
        }
        Assert.Equal((1, 0), (s.Finished, s.Slipped));
        Assert.Equal(sailor.Call.Count + sailor.Sequence.Count, s.Played); // the call, then every note of the song from the top
        Assert.Equal(6, sections.Count);
        Assert.Equal(("Chorus", 3), sailor.SectionAt(5));
        Assert.Equal(0, s.BreathsMisplaced);
        Assert.True(s.Breaths >= 20, $"{s.Breaths} breaths");
        Assert.Equal(sailor.Sequence.Count(n => n.IsLong), s.Flourishes); // every long note shaped
        return 0;
    });

    [Fact]
    public void WithoutABreathTheAirRunsOutAndNotesGoMissing() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var sailor = defs.ModDef<SongDef>(SongDef.Kind_, "DrunkenSailor")!;
        var s = new PlaySession { InstrumentName = "Pipe", Book = [sailor] };
        s.Advance([new WorkInput(0, WorkInputKind.Act), .. Opening(sailor, from: 1)], 200);
        KeepTime(s, 200 + 30 * 60, Minding.Beat | Minding.Swell);
        Assert.Equal(0, s.Breaths);
        Assert.True(s.Dropped > 0, "no air for them");
        Assert.Equal(1, s.Slipped); // keeping the beat alone won't hold a tune with no air in it
        return 0;
    });

    [Fact]
    public void ABreathSnatchedMidPhraseCostsANote() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var sailor = defs.ModDef<SongDef>(SongDef.Kind_, "DrunkenSailor")!;
        var s = new PlaySession { InstrumentName = "Pipe", Book = [sailor] };
        s.Advance([new WorkInput(0, WorkInputKind.Act), .. Opening(sailor, from: 1)], 200);
        KeepTime(s, 300); // into the second line, mid-phrase
        Assert.False(s.InGap);
        var (played, mind) = (s.Played, s.Concentration);
        s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Custom, PlaySession.PackBreath(true))], s.StepsDone + (int)s.StepsPerBeat + 2);
        Assert.True(s.Breathing);
        Assert.Equal(played, s.Played); // the note due went for the breath
        Assert.True(s.BreathsMisplaced >= 1);
        Assert.True(s.Concentration < mind);
        Assert.Equal(1f, s.Breath); // but the air's back
        return 0;
    });

    [Fact]
    public void TheHandsFindTheNotesSoANoteKeyChangesNothing() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var sailor = defs.ModDef<SongDef>(SongDef.Kind_, "DrunkenSailor")!;
        var s = new PlaySession { InstrumentName = "Pipe", Book = [sailor] };
        s.Advance([new WorkInput(0, WorkInputKind.Act), .. Opening(sailor, from: 1)], 200);
        var (played, mind) = (s.Played, s.Concentration);
        s.Advance([new WorkInput(200, WorkInputKind.Custom, PlaySession.Pack(3, false, true))], 201);
        Assert.NotNull(s.Carrying);
        Assert.Equal((played, mind), (s.Played, s.Concentration));
        // Space lets it go, back to free play.
        s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Act)], s.StepsDone + 1);
        Assert.Null(s.Carrying);
        Assert.Equal(PlayMode.Free, s.Mode);
        return 0;
    });

    [Fact]
    public void ALongNoteSwelledThroughIsShapedAndAShortSwellIsNot() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var sailor = defs.ModDef<SongDef>(SongDef.Kind_, "DrunkenSailor")!;
        var s = new PlaySession { InstrumentName = "Pipe", Book = [sailor] };
        s.Advance([new WorkInput(0, WorkInputKind.Act), .. Opening(sailor, from: 1)], 200);
        KeepTime(s, 200 + 60 * 60, Minding.Beat | Minding.Breath); // no swelling at all
        Assert.Equal(0, s.Flourishes);
        var shaped = new PlaySession { InstrumentName = "Pipe", Book = [sailor] };
        shaped.Advance([new WorkInput(0, WorkInputKind.Act), .. Opening(sailor, from: 1)], 200);
        KeepTime(shaped, 200 + 60 * 60);
        Assert.True(shaped.Flourishes > 5);
        Assert.True(shaped.Concentration >= s.Concentration);
        return 0;
    });

    [Fact]
    public void ATunesPartsComeRoundInItsFormWithRestsAndASharp() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var tune = new SongDef
        {
            Name = "Test", Tempo = 120, Opening = 3,
            Parts = new() { ["A"] = [[0, 1], [2, 1, 1], [4, 1], [-1, 1], [5, 1]], ["B"] = [[7, 1], [-1, 2], [6, 1], [-2]] },
            Form = ["A", "B", "A"],
        };
        tune.Resolve(defs);
        Assert.Equal(13, tune.Sequence.Count);
        Assert.True(tune.Sequence[7].BreathAfter);
        Assert.Equal(("A", 2), tune.SectionAt(2));
        var s = new PlaySession { InstrumentName = "Pipe", Book = [tune] };
        // Without its sharp the opening's another tune's shape: nothing.
        s.Advance([new WorkInput(0, WorkInputKind.Act), new(1, WorkInputKind.Custom, PlaySession.Pack(0, false, true)),
            new(21, WorkInputKind.Custom, PlaySession.Pack(2, false, true)), new(41, WorkInputKind.Custom, PlaySession.Pack(4, false, true))], 50);
        Assert.Null(s.Carrying);
        s.Advance(Opening(tune, from: 200), 260);
        Assert.Same(tune, s.Carrying);
        Assert.Equal(1, s.Notes[^2].Accidental);
        KeepTime(s, 2000);
        Assert.Equal(1, s.Finished);
        // Rests make no sound: 13 in the form, 3 of them rests, the opening's 3 played by hand.
        Assert.Equal(3 + 3 + 10 - 3, s.Played);
        Assert.Equal(1, s.Notes.Count(n => n.Accidental == 1 && n.Step > 260)); // the sharp, second time round A
        return 0;
    });

    [Fact]
    public void AMarkWithNoNoteBeforeItIsRefused() => WithMusician(() =>
    {
        var tune = new SongDef { Name = "Bad", Parts = new() { ["A"] = [[-2], [0, 1], [1, 1], [2, 1], [3, 1], [4, 1], [5, 1]] } };
        Assert.Throws<Exception>(() => tune.Resolve(MusicianDefs()));
        return 0;
    });

    [Fact]
    public void ASessionMidTuneIsKeptThroughASave() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var sim = Round(defs, (Arin, "Musician"));
        var me = Players.CharacterOf(sim, Arin);
        WorkSystem.Start(sim, me, PlaySession.For(sim, MusicianModule.InstrumentOf(sim, me)));
        var s = (PlaySession)sim.World.Get<WorkSession>(me);
        var sailor = s.Book.Single(t => t.Name == "DrunkenSailor");
        s.Advance([new WorkInput(0, WorkInputKind.Act), new(1, WorkInputKind.Axis2, 1), new(2, WorkInputKind.Axis2, 0), .. Opening(sailor, from: 3)], 200);
        KeepTime(s, 900);
        Assert.Same(sailor, s.Carrying);
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), defs);
        var back = Assert.IsType<PlaySession>(loaded.World.Get<WorkSession>(me));
        Assert.Equal((PlayMode.Book, 1, "DrunkenSailor"), (back.Mode, back.Picked, back.Carrying?.Name));
        Assert.Equal((s.Next, s.Concentration, s.Beat, s.BeatsKept, s.Played, s.Breath, s.Breaths, s.Flourishes, s.GapTo, s.Ringing, s.RingingUntil),
            (back.Next, back.Concentration, back.Beat, back.BeatsKept, back.Played, back.Breath, back.Breaths, back.Flourishes, back.GapTo, back.Ringing, back.RingingUntil));
        Assert.Equal(s.Notes, back.Notes);
        return 0;
    });

    [Fact]
    public void ThePredictorsCopyPlaysOnItsOwnNotes() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var sim = Played(defs, PlayMode.Free, (0, 1, false));
        var ahead = (PlaySession)sim.Clone();
        ahead.Advance([new WorkInput(5, WorkInputKind.Custom, PlaySession.Pack(3, false, true))], 6);
        Assert.Single(sim.Notes);
        Assert.Equal(2, ahead.Notes.Count);
        return 0;
    });

    [Fact]
    public void ACopyRunningAheadKeepsTheTuneTheSameAsTheSim() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var sailor = defs.ModDef<SongDef>(SongDef.Kind_, "DrunkenSailor")!;
        var sim = new PlaySession { InstrumentName = "Pipe", Book = [sailor] };
        sim.Advance([new WorkInput(0, WorkInputKind.Act), .. Opening(sailor, from: 1)], 200);
        var ahead = (PlaySession)sim.Clone();
        // The same taps, breaths and swells, some well placed and some not, played to the same end by both.
        List<WorkInput> inputs = [];
        for (var i = 0; i < 40; i++)
        {
            inputs.Add(new WorkInput(230 + i * 33, WorkInputKind.Press));
            if (i % 5 == 0)
                inputs.AddRange([new(231 + i * 33, WorkInputKind.Custom, PlaySession.PackBreath(true)), new(250 + i * 33, WorkInputKind.Custom, PlaySession.PackBreath(false))]);
            if (i % 7 == 0)
                inputs.AddRange([new(232 + i * 33, WorkInputKind.Hold, 1), new(290 + i * 33, WorkInputKind.Hold, 0)]);
        }
        inputs.Sort((x, y) => x.Step.CompareTo(y.Step));
        ahead.Advance(inputs, 2000);
        sim.Advance(inputs, 2000);
        Assert.Equal((sim.Concentration, sim.BeatsKept, sim.BeatsMissed, sim.Dropped, sim.Played, sim.Slipped, sim.Breath, sim.Flourishes),
            (ahead.Concentration, ahead.BeatsKept, ahead.BeatsMissed, ahead.Dropped, ahead.Played, ahead.Slipped, ahead.Breath, ahead.Flourishes));
        Assert.Equal(sim.Notes, ahead.Notes);
        return 0;
    });

    /// <summary>A session on the named instrument, as starting it from one would make it, with the whole book.</summary>
    private static PlaySession On(DefDatabase defs, string instrument) => new()
    {
        InstrumentName = instrument,
        Def = defs.ModDef<InstrumentDef>(InstrumentDef.Kind, instrument)!,
        Book = [.. defs.ModDefs<SongDef>(SongDef.Kind_)],
    };

    [Fact]
    public void TheMusicianBringsFourInstrumentsAndPlaysTheOneInHand() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var sim = Round(defs, (Arin, "Musician"));
        var me = Players.CharacterOf(sim, Arin);
        foreach (var name in new[] { "Pipe", "Lute", "Fiddle", "HandDrum" })
            Assert.Single(Inventory.CarriedOf(sim, me, sim.Defs.AllThings.First(t => t.Name == name)));
        var fiddle = Inventory.CarriedOf(sim, me, sim.Defs.AllThings.First(t => t.Name == "Fiddle"))[0];
        FireDrill.PutInHand(sim, me, fiddle);
        Assert.Equal(fiddle, MusicianModule.InstrumentOf(sim, me));
        var s = PlaySession.For(sim, fiddle);
        Assert.Equal("Bow", s.Def.Air);
        Assert.Equal("Playing the fiddle", s.Title);
        return 0;
    });

    [Fact]
    public void ALuteNoteRingsOfItselfAndItsTunesAskOnlyTheBeat() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var lute = On(defs, "Lute");
        lute.Advance([new WorkInput(0, WorkInputKind.Custom, PlaySession.Pack(0, false, true)), new(5, WorkInputKind.Custom, PlaySession.Pack(2, false, true))], 10);
        Assert.Equal(2, lute.Played); // both sounding, plucked over each other: nothing held to stop
        Assert.Null(lute.RingingNote);
        // Drunken Sailor, the beat alone minded: no breath to take, no long note to swell, and it plays through.
        var sailor = defs.ModDef<SongDef>(SongDef.Kind_, "DrunkenSailor")!;
        var s = On(defs, "Lute");
        s.Advance([new WorkInput(0, WorkInputKind.Act), .. Opening(sailor, from: 1)], 200);
        Assert.Same(sailor, s.Carrying);
        s.Advance([new WorkInput(200, WorkInputKind.Custom, PlaySession.PackBreath(true))], 260);
        Assert.False(s.Breathing); // a lute has no air to take
        KeepTime(s, 200 + 200 * 60, Minding.Beat);
        Assert.Equal(1, s.Finished);
        Assert.Equal((0, 0, 1f), (s.Slipped, s.Flourishes, s.Breath));
        Assert.Equal(sailor.Call.Count + sailor.Sequence.Count, s.Played);
        return 0;
    });

    [Fact]
    public void AFiddlesBowRunsOutAsTheBreathDoesAndItsNotesSlide() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var sailor = defs.ModDef<SongDef>(SongDef.Kind_, "DrunkenSailor")!;
        var minded = On(defs, "Fiddle");
        minded.Advance([new WorkInput(0, WorkInputKind.Act), .. Opening(sailor, from: 1)], 200);
        KeepTime(minded, 200 + 200 * 60);
        Assert.Equal(1, minded.Finished);
        Assert.Equal(sailor.Sequence.Count(n => n.IsLong), minded.Flourishes);
        var unturned = On(defs, "Fiddle");
        unturned.Advance([new WorkInput(0, WorkInputKind.Act), .. Opening(unturned.Book.First(t => t.Name == "DrunkenSailor"), from: 1)], 200);
        KeepTime(unturned, 200 + 200 * 60, Minding.Beat | Minding.Swell);
        Assert.Equal(1, unturned.Slipped); // the bow never turned

        // Free, D held slides the note up (all the way in a few steps, not at once), and back as it's let go.
        var s = On(defs, "Fiddle");
        s.Advance([new WorkInput(0, WorkInputKind.Custom, PlaySession.Pack(4, false, true)), new(2, WorkInputKind.Axis, 1)], 6);
        Assert.InRange(s.Bend, 0.1f, 0.9f);
        s.Advance([], 20);
        Assert.Equal(1f, s.Bend);
        s.Advance([new WorkInput(20, WorkInputKind.Axis, 0)], 40);
        Assert.Equal(0f, s.Bend);
        return 0;
    });

    [Fact]
    public void ADrumHasThreeStrokesAPressedHeadAndNoBookYet() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var s = On(defs, "HandDrum");
        Assert.Equal("tek", s.Def.NoteName(1, 0, 0));
        s.Advance([
            new WorkInput(0, WorkInputKind.Custom, PlaySession.Pack(0, true, true, true)), // the octave and semitone mean nothing to it
            new(4, WorkInputKind.Custom, PlaySession.Pack(2, false, true)),
            new(8, WorkInputKind.Custom, PlaySession.Pack(5, false, true)), // no fourth stroke
            new(9, WorkInputKind.Act), // no book
            new(10, WorkInputKind.Axis2, -1)], 30);
        Assert.Equal([new NoteEvent(0, 0, 0), new NoteEvent(4, 2, 0)], s.Notes);
        Assert.Equal(PlayMode.Free, s.Mode);
        Assert.Equal(1f, s.Bend); // W: the head pressed
        Assert.Null(s.RingingNote);
        return 0;
    });

    [Fact]
    public void AnInstrumentAndItsBendComeBackFromASave() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var sim = Round(defs, (Arin, "Musician"));
        var me = Players.CharacterOf(sim, Arin);
        var fiddle = Inventory.CarriedOf(sim, me, sim.Defs.AllThings.First(t => t.Name == "Fiddle"))[0];
        WorkSystem.Start(sim, me, PlaySession.For(sim, fiddle));
        var session = (PlaySession)sim.World.Get<WorkSession>(me);
        session.Advance([new WorkInput(0, WorkInputKind.Custom, PlaySession.Pack(3, false, true)), new(1, WorkInputKind.Axis, -1)], 5);
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), defs);
        var back = Assert.IsType<PlaySession>(loaded.World.Get<WorkSession>(me));
        Assert.Equal("Fiddle", back.Def.Name);
        Assert.Equal((session.Bend, session.BendTo), (back.Bend, back.BendTo));
        Assert.Equal(3, back.RingingNote?.Degree);
        return 0;
    });

    /// <summary>A round with the musician playing <paramref name="instrument"/>, and a villager spawned a few steps off them.</summary>
    private static (Simulation Sim, Entity Musician, PlaySession Session, Entity Listener) Recital(DefDatabase defs, string instrument = "Pipe")
    {
        var sim = Round(defs, (Arin, "Musician"));
        var me = Players.CharacterOf(sim, Arin);
        if (!sim.World.Has<Avatar>(me))
            sim.World.Add(me, new Avatar()); // the player at the keys: a session lasts only while someone's playing it
        var held = Inventory.CarriedOf(sim, me, sim.Defs.AllThings.First(t => t.Name == instrument))[0];
        WorkSystem.Start(sim, me, PlaySession.For(sim, held));
        var session = (PlaySession)sim.World.Get<WorkSession>(me);
        var at = sim.World.Get<Position>(me).Cell;
        var near = Sim.Ai.AiQueries.RandomReachableNear(sim, me, at, 3, reject: c => c == at)!.Value;
        var listener = sim.SpawnCreature(sim.Defs.Creature("Human"), "Hob", near, ageDays: 25 * 365);
        return (sim, me, session, listener);
    }

    [Fact]
    public void WithNothingElseToDoWhoeverHearsTheMusicGoesToIt() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var (sim, me, session, listener) = Recital(defs);
        Assert.Contains(sim.Pastimes, p => p is Listening);
        var pastime = new Listening();
        Assert.Null(pastime.JobFor(sim, listener)); // a pipe held, nothing played
        session.Carry(session.Book.First(t => t.Name == "DrunkenSailor"), 0, 0);
        var job = Assert.IsType<AudienceJob>(pastime.JobFor(sim, listener));
        Assert.Equal(me, job.Musician);
        Assert.True(Performance.Of(session).Danceable); // a lively march, the musician's mind on it
        Assert.Equal(112f, Performance.Of(session).Tempo);
        Assert.Null(pastime.JobFor(sim, me)); // not their own audience
        return 0;
    });

    /// <summary>A tick, the musician at the keys still (as their batches would say), the tune where it stands.</summary>
    private static void Playing(Simulation sim, PlaySession session)
    {
        session.LastInputTick = sim.Clock.Tick;
        sim.Tick();
    }

    [Fact]
    public void TheAudienceDancesToTheTunesBeatTillItStopsAndRemembersIt() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var (sim, me, session, listener) = Recital(defs);
        session.Carry(session.Book.First(t => t.Name == "DrunkenSailor"), 0, 0);
        var spot = Sim.Ai.AiQueries.RandomReachableNear(sim, listener, sim.World.Get<Position>(me).Cell, 3, reject: c => c == sim.World.Get<Position>(me).Cell)!.Value;
        Array.Fill(sim.World.Get<Mind>(listener).WorkPriority, 0); // a free evening: no work to call them away
        var dance = new AudienceJob(me, spot, dance: true, most: AudienceJob.Remembered + 60);
        AiSystem.Order(sim, listener, dance);
        for (var t = 0; t < 600 && !dance.Dancing; t++)
            Playing(sim, session);
        Assert.True(dance.Dancing);
        Assert.Equal(112f, dance.DanceTempo); // a hop to the tune's own beat
        Assert.Equal(me, dance.Focus); // facing the musician
        for (var t = 0; t < AudienceJob.Remembered + 120 && sim.World.Get<Mind>(listener).Job == dance; t++)
            Playing(sim, session);
        Assert.NotSame(dance, sim.World.Get<Mind>(listener).Job);
        Assert.Contains(sim.World.Get<Mood>(listener).Memories, m => m.Def.Name == "Danced");

        // The music stops: so do they.
        var listen = new AudienceJob(me, spot, dance: false, most: 5000);
        AiSystem.Order(sim, listener, listen);
        Playing(sim, session);
        WorkSystem.Stop(sim, me, null);
        for (var t = 0; t < 10; t++)
            sim.Tick();
        Assert.NotSame(listen, sim.World.Get<Mind>(listener).Job);
        return 0;
    });

    [Fact]
    public void FreePlayDrawsACrowdByHowItsPlayedNotByRightNotes() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        // Even notes, a beat apart: steady and lively enough to dance to, whatever they are.
        var steady = On(defs, "HandDrum");
        steady.Advance([.. Enumerable.Range(0, 12).Select(i => new WorkInput(i * 15, WorkInputKind.Custom, PlaySession.Pack(i % 3, false, true)))], 12 * 15);
        var p = Performance.Of(steady);
        Assert.True(p.Danceable, $"{p}");
        Assert.Equal(120f, p.Tempo); // four a second: eighths of a 120 beat
        // Ragged: notes at odd gaps, heard but not danced to.
        var ragged = On(defs, "Lute");
        int[] at = [0, 17, 29, 70, 77, 128, 137, 170, 199];
        ragged.Advance([.. at.Select(t => new WorkInput(t, WorkInputKind.Custom, PlaySession.Pack(2, false, true)))], 200);
        Assert.True(Performance.Of(ragged).Playing);
        Assert.False(Performance.Of(ragged).Danceable);
        // A few seconds' quiet: the music's over.
        ragged.Advance([], 200 + Performance.Lately);
        Assert.False(Performance.Of(ragged).Playing);
        return 0;
    });

    [Fact]
    public void ARhythmIsFoundUnderItsNotesNotJustInEvenOnes() => WithMusician(() =>
    {
        // doum, tek-tek, doum, tek-tek: a beat and two halves, round again: steady, at 120 a minute.
        List<int> pattern = [];
        for (var t = 0; pattern.Count < 13; t += 60)
            pattern.AddRange([t, t + 30, t + 45]);
        var (steady, beat) = Performance.Rhythm(pattern);
        Assert.True(steady > 0.9f, $"{steady}");
        Assert.Equal(30f, beat);
        // A step out here and there (a sixtieth of a second), as a hand keeps it: still steady enough to dance to.
        var (human, _) = Performance.Rhythm([.. pattern.Select((t, i) => t + (i % 3 == 1 ? 1 : i % 4 == 0 ? -1 : 0))]);
        Assert.True(human >= 0.75f, $"{human}");
        // Flourishes: a quick run between two beats, grace notes just before others. Still the beat.
        List<int> flourished = [];
        for (var b = 0; b < 12; b++)
        {
            flourished.Add(b * 30);
            if (b == 5)
                flourished.AddRange([b * 30 + 8, b * 30 + 15, b * 30 + 22]);
            if (b % 4 == 2)
                flourished.Add(b * 30 + 27);
        }
        flourished.Sort();
        var (ornate, _) = Performance.Rhythm(flourished);
        Assert.True(ornate >= 0.85f, $"{ornate}");
        // Gaps of any length at all: no rhythm in it.
        var (ragged, _) = Performance.Rhythm([0, 7, 29, 40, 77, 85, 128, 133, 170, 199, 207]);
        Assert.True(ragged < 0.5f, $"{ragged}");
        return 0;
    });

    [Fact]
    public void ALuteHoldsAChordOnAKeyAndStrumsItDownAndUp() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var lute = defs.ModDef<InstrumentDef>(InstrumentDef.Kind, "Lute")!;
        Assert.Equal(["D", "Em", "F#m", "G", "A", "Bm", "C#dim", "D"], Enumerable.Range(0, 8).Select(lute.ChordName));
        var s = On(defs, "Lute");
        s.Advance([new WorkInput(0, WorkInputKind.Custom, PlaySession.Pack(1, false, true, chord: true))], 1);
        // Em, a down-stroke as it's taken: every string from the bass up, E, G, B and the E above.
        Assert.Equal([1, 3, 5, 8], s.Notes.Select(n => n.Index));
        Assert.Equal(1, s.Chord);
        s.Advance([new WorkInput(30, WorkInputKind.Press), new(45, WorkInputKind.Press)], 50);
        Assert.Equal([8, 5], s.Notes.Skip(4).Take(2).Select(n => n.Index)); // the up-stroke: the top two, high to low
        Assert.Equal([1, 3, 5, 8], s.Notes.Skip(6).Select(n => n.Index)); // and down again
        Assert.Equal(3, s.Strums);
        // The chord let go: F strums nothing.
        s.Advance([new WorkInput(50, WorkInputKind.Custom, PlaySession.Pack(1, false, false, chord: true)), new(60, WorkInputKind.Press)], 70);
        Assert.Equal((-1, 10), (s.Chord, s.Played));
        // Strings off the instrument's top go down an octave: the chord on the upper octave's key.
        s.Advance([new WorkInput(70, WorkInputKind.Custom, PlaySession.Pack(7, true, true, chord: true))], 71);
        Assert.Equal([9, 11, 14], s.Notes.TakeLast(3).Select(n => n.Index)); // its own octave down is itself: not twice
        // A strum is played once, by the rhythm: its strings one onset.
        Assert.Equal(4, Performance.Onsets(s).Count);
        // Not on a pipe, and not in the book.
        var pipe = On(defs, "Pipe");
        pipe.Advance([new WorkInput(0, WorkInputKind.Custom, PlaySession.Pack(1, false, true, chord: true))], 1);
        Assert.Equal((0, -1), (pipe.Played, pipe.Chord));
        var book = On(defs, "Lute");
        book.Advance([new WorkInput(0, WorkInputKind.Act), new(1, WorkInputKind.Custom, PlaySession.Pack(1, false, true, chord: true))], 2);
        Assert.Equal(0, book.Played);
        return 0;
    });

    [Fact]
    public void AFiddleHasASoundForEveryKeyAndAChordHeldComesBackFromASave() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        Assert.Equal(16, defs.ModDef<InstrumentDef>(InstrumentDef.Kind, "Fiddle")!.Held.Count);
        var sim = Round(defs, (Arin, "Musician"));
        var me = Players.CharacterOf(sim, Arin);
        var lute = Inventory.CarriedOf(sim, me, sim.Defs.AllThings.First(t => t.Name == "Lute"))[0];
        WorkSystem.Start(sim, me, PlaySession.For(sim, lute));
        var session = (PlaySession)sim.World.Get<WorkSession>(me);
        session.Advance([new WorkInput(0, WorkInputKind.Custom, PlaySession.Pack(4, false, true, chord: true))], 5);
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), defs);
        var back = Assert.IsType<PlaySession>(loaded.World.Get<WorkSession>(me));
        Assert.Equal((4, true, 1), (back.Chord, back.StrumUp, back.Strums));
        return 0;
    });

    [Fact]
    public void APlayerInEarshotDancesOnTheMusiciansBeat() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var (sim, me, session, dancer) = Recital(defs);
        sim.World.Add(dancer, new Avatar()); // a player, with no instrument
        var here = sim.World.Get<Position>(dancer).Cell;
        var module = new MusicianModule();
        Assert.Null(module.EarlyActionAt(sim, dancer, here, here)); // no music yet
        var sailor = session.Book.First(t => t.Name == "DrunkenSailor");
        session.Carry(sailor, 0, 0);
        var action = module.EarlyActionAt(sim, dancer, here, here);
        Assert.Equal("Dance to the music", action?.Label);
        module.Perform(sim, dancer, action!.Value with { By = module });
        var dance = Assert.IsType<DanceSession>(sim.World.Get<WorkSession>(dancer));
        Assert.True(dance.Dancing);
        // Steps on the tune's beat (its first falls on the musician's session's start), and some well off it.
        var perBeat = session.StepsPerBeat;
        int StepOn(int beat, float off = 0f) => (int)MathF.Round(session.StartTick + beat * perBeat + off - dance.StartTick);
        List<WorkInput> steps = [];
        for (var b = 2; b < 12; b++)
            steps.Add(new WorkInput(StepOn(b), WorkInputKind.Axis, b % 2 == 0 ? -1 : 1));
        steps.Add(new WorkInput(StepOn(12, perBeat / 2f), WorkInputKind.Act)); // a turn between beats
        var stepped = new List<WorkInput>();
        foreach (var st in steps) // each key pressed and let go, as a player's would
            stepped.AddRange([st, new WorkInput(st.Step + 3, st.Kind == WorkInputKind.Axis ? WorkInputKind.Axis : WorkInputKind.Hold, 0)]);
        var upTo = steps[^1].Step + 5;
        while (sim.Clock.Tick - dance.StartTick < upTo)
        {
            Playing(sim, session);
            dance.LastInputTick = sim.Clock.Tick;
        }
        dance.Advance(stepped, upTo);
        dance.Settle(sim);
        Assert.Equal((10, 1, 0), (dance.OnBeat, dance.OffBeat, dance.Streak));
        Assert.Equal(10, dance.BestStreak);
        Assert.Equal(112f, dance.DanceTempo);
        // The music stops: so, after a moment, does the dance, remembered.
        WorkSystem.Stop(sim, me, null);
        for (var t = 0; t < DanceSession.Pause + 10; t++)
        {
            dance.LastInputTick = sim.Clock.Tick;
            sim.Tick();
        }
        Assert.False(sim.World.Has<WorkSession>(dancer));
        Assert.Contains(sim.World.Get<Mood>(dancer).Memories, m => m.Def.Name == "Danced");
        return 0;
    });

    [Fact]
    public void SourMusicDrawsNoOneAndDrivesTheAudienceOff() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var (sim, me, session, listener) = Recital(defs);
        session.Carry(session.Book.First(t => t.Name == "DrunkenSailor"), 0, 0);
        var spot = Sim.Ai.AiQueries.RandomReachableNear(sim, listener, sim.World.Get<Position>(me).Cell, 3, reject: c => c == sim.World.Get<Position>(me).Cell)!.Value;
        Array.Fill(sim.World.Get<Mind>(listener).WorkPriority, 0);
        var job = new AudienceJob(me, spot, dance: false, most: 5000);
        AiSystem.Order(sim, listener, job);
        for (var t = 0; t < 300; t++)
            Playing(sim, session);
        Assert.Same(job, sim.World.Get<Mind>(listener).Job);
        session.Concentration = 0.2f; // the musician's mind gone off it: the tune's falling apart
        Assert.True(Performance.Of(session).Sour);
        Assert.Null(new Listening().JobFor(sim, listener));
        for (var t = 0; t < AudienceJob.SourPatience + 10; t++)
            Playing(sim, session);
        Assert.True(job.Soured);
        Assert.NotSame(job, sim.World.Get<Mind>(listener).Job);
        Assert.Contains(sim.World.Get<Mood>(listener).Memories, m => m.Def.Name == "SourMusic");
        return 0;
    });

    [Fact]
    public void AMarchIsDancedRoundTheMusicianInARing() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var (sim, me, session, listener) = Recital(defs);
        session.Carry(session.Book.First(t => t.Name == "DrunkenSailor"), 0, 0); // a march
        var jobs = Enumerable.Range(0, 20).Select(_ => new Listening().JobFor(sim, listener)).OfType<AudienceJob>().ToList();
        var ring = jobs.FirstOrDefault(j => j.ToDance);
        Assert.NotNull(ring);
        Assert.True(ring.InRing);
        var centre = sim.World.Get<Position>(me).Cell;
        Assert.All(Listening.Ring(sim, listener, centre), c => Assert.Equal(Sim.Ai.Festival.DanceRadius, Math.Max(Math.Abs(c.X - centre.X), Math.Abs(c.Y - centre.Y))));
        Assert.Contains(jobs, j => !j.ToDance); // and some just listen
        return 0;
    });

    /// <summary>The musician's playing as F starts it (the hat set down with it), a listener with ten silver standing by.</summary>
    private static (Simulation Sim, Entity Musician, PlaySession Session, Entity Listener) Busking(DefDatabase defs)
    {
        var (sim, me, old, listener) = Recital(defs);
        WorkSystem.Stop(sim, me, null);
        var module = new MusicianModule();
        var here = sim.World.Get<Position>(me).Cell;
        var pipe = Inventory.CarriedOf(sim, me, sim.Defs.AllThings.First(t => t.Name == "Pipe"))[0];
        FireDrill.PutInHand(sim, me, pipe);
        var play = module.EarlyActionAt(sim, me, here, here)!.Value;
        module.Perform(sim, me, play with { By = module });
        var session = (PlaySession)sim.World.Get<WorkSession>(me);
        Array.Fill(sim.World.Get<Mind>(listener).WorkPriority, 0);
        return (sim, me, session, listener);
    }

    /// <summary>Ten silver in the listener's hands (they've no pack), once they're in the audience: a job ended drops what's carried.</summary>
    private static void Purse(Simulation sim, Entity listener) =>
        Inventory.Stow(sim, listener, sim.CreateItem(sim.Defs.AllThings.First(t => t.Name == Sim.Shops.Coin.Silver), 10));

    [Fact]
    public void ListenersTipIntoTheHatOnceADayAndItComesBackUpFull() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var (sim, me, session, listener) = Busking(defs);
        Assert.True(sim.World.IsAlive(session.Hat) && sim.World.Get<Thing>(session.Hat).Spawned, "the hat down at their feet");
        session.Carry(session.Book.First(t => t.Name == "MillersWheel"), 0, 0); // a jig, danced on the spot
        var spot = Sim.Ai.AiQueries.RandomReachableNear(sim, listener, sim.World.Get<Position>(me).Cell, 2, reject: c => c == sim.World.Get<Position>(me).Cell)!.Value;
        var job = new AudienceJob(me, spot, dance: false, most: AudienceJob.Remembered + 30);
        AiSystem.Order(sim, listener, job);
        Purse(sim, listener);
        for (var t = 0; t < AudienceJob.Remembered + 400 && sim.World.Get<Mind>(listener).Job == job; t++)
            Playing(sim, session);
        Assert.Equal(1, Takings.InHat(sim, session.Hat)); // a villager who listened: a silver
        Assert.Equal((listener, 1), (session.Tips[^1].From, session.Tips[^1].Silver)); // who, for the window: to thank them
        // A bow for it, playing on (an emote's otherwise refused mid-work): shown as the sim settles.
        session.Advance([new WorkInput(session.StepsDone, WorkInputKind.Custom, PlaySession.BowBit)], session.StepsDone + 1);
        Playing(sim, session);
        Assert.Equal(Emote.Bow, sim.World.Get<Emoting>(me).Kind);
        Assert.Same(session, sim.World.Get<WorkSession>(me));
        Assert.Contains(sim.World.Get<Mood>(listener).Memories, m => m.Def.Name == "HeardMusic");
        // Again the same day: they've given their tip.
        var again = new AudienceJob(me, spot, dance: false, most: AudienceJob.Remembered + 30);
        AiSystem.Order(sim, listener, again);
        Purse(sim, listener);
        for (var t = 0; t < AudienceJob.Remembered + 400 && sim.World.Get<Mind>(listener).Job == again; t++)
            Playing(sim, session);
        Assert.Equal(1, Takings.InHat(sim, session.Hat));
        // Put away: the hat comes back up, the silver into the pack.
        var before = Sim.Shops.Coin.Count(sim, me);
        var hat = session.Hat;
        WorkSystem.Stop(sim, me, null);
        Assert.Equal(before + 1, Sim.Shops.Coin.Count(sim, me));
        Assert.False(sim.World.Get<Thing>(hat).Spawned);
        Assert.True(Inventory.OnPerson(sim, me, hat));
        return 0;
    });

    [Fact]
    public void ATuneCalledForAndPlayedIsTippedWell() => WithMusician(() =>
    {
        var defs = MusicianDefs();
        var (sim, me, session, listener) = Busking(defs);
        var sailor = session.Book.First(t => t.Name == "DrunkenSailor");
        Takings.Ask(sim, listener, me, sailor);
        Assert.Equal("DrunkenSailor", Takings.Wants(sim, listener, me));
        Assert.True(sim.World.Get<TuneRequest>(listener).Heard); // said aloud, beside the musician
        Assert.Contains(sim.Speech.Kept, u => u.Speaker == listener && u.Text.Contains("Drunken Sailor"));
        session.Carry(sailor, 0, 0);
        var spot = Sim.Ai.AiQueries.RandomReachableNear(sim, listener, sim.World.Get<Position>(me).Cell, 2, reject: c => c == sim.World.Get<Position>(me).Cell)!.Value;
        var job = new AudienceJob(me, spot, dance: false, most: AudienceJob.Remembered + 30);
        AiSystem.Order(sim, listener, job);
        Purse(sim, listener);
        for (var t = 0; t < AudienceJob.Remembered + 400 && sim.World.Get<Mind>(listener).Job == job; t++)
            Playing(sim, session);
        Assert.Null(Takings.Wants(sim, listener, me)); // answered
        Assert.Equal(Takings.ForTheirTune + Takings.ForListening, Takings.InHat(sim, session.Hat));
        Assert.Contains(sim.World.Get<Mood>(listener).Memories, m => m.Def.Name == "HeardTheirTune");
        return 0;
    });
}
