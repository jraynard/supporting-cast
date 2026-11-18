using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Expeditions;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The Director as a game master (docs/DIRECTOR-PLAN.md): it reads the town (<see cref="Tension"/>),
/// eases off or pushes (<see cref="RuleMind"/>), waits for trouble to settle, follows quests, and
/// fills lulls with incidents.
/// </summary>
public class DirectorPacingTests
{
    private static readonly Guid Me = new("00000000-0000-0000-0000-00000000a11e");

    /// <summary>A round in Millbrook on its own copy of the defs, with these beats for its season (the real ones if null).</summary>
    private static Simulation Round(List<CrisisBeat>? beats = null)
    {
        var defs = TestWorld.LoadFreshDefs(); // a private copy: the season's beats are changed
        if (beats != null)
            defs.Season.Beats = beats;
        var sim = VillageRound.NewSimulation(defs, 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Me, "Arin", default));
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    private static CrisisBeat Beat(int day, float from, float to, string kinds, string id = "") =>
        new() { Id = id, Day = day, FromHour = from, ToHour = to, Kinds = kinds.Split(',').ToDictionary(k => k, _ => 1f) };

    private static void Every(Simulation sim, int updates) => sim.TickMany(SimClock.TicksPerHour / 6 * updates);

    [Fact]
    public void ABeatComesSoonerAndHarderForACoastingTownAndLaterAndGentlerForAStrugglingOne()
    {
        Assert.Equal(500, RuleMind.Due(500, 0, 1000, 0f));
        Assert.Equal(0, RuleMind.Due(500, 0, 1000, 1f));
        Assert.Equal(1000, RuleMind.Due(500, 0, 1000, -1f));
        Assert.Equal(250, RuleMind.Due(500, 0, 1000, 0.5f));

        var pacing = new PacingDef { StrengthRange = [0.75f, 1.35f] };
        Assert.Equal(1f, RuleMind.Strength(pacing, 1f, 0f));
        Assert.Equal(1.35f, RuleMind.Strength(pacing, 1f, 1f), 3);
        Assert.Equal(0.75f, RuleMind.Strength(pacing, 1f, -1f), 3);
        Assert.Equal(1.35f, RuleMind.Strength(pacing, 1f, 5f), 3); // never past the range
    }

    [Fact]
    public void TheReadingFollowsTheTown()
    {
        var sim = Round();
        Every(sim, 1);
        Assert.InRange(sim.Director.Reading.Push, -0.2f, 0.2f); // on course, nothing wrong
        sim.Reckoning.Add(sim, Reckoning.RenownTarget(sim) * 0.4f, 0f);
        Assert.True(Tension.Read(sim, 0).Push > 0.5f, "a town well ahead is pushed");
        sim.Reckoning.Add(sim, -Reckoning.RenownTarget(sim) * 0.4f, Reckoning.DreadLimit(sim) * 0.7f);
        Assert.True(Tension.Read(sim, 0).Push < -0.5f, "a town deep in dread is eased off");
        Assert.True(Tension.Read(sim, 1).Harm > Tension.Read(sim, 0).Harm, "trouble under way counts as harm");
    }

    /// <summary>Tick by updates until a crisis of this kind is under way, or the clock reaches <paramref name="until"/>; the tick it came, or -1.</summary>
    private static long Until(Simulation sim, CrisisKind kind, long until)
    {
        while (sim.Clock.Tick < until)
        {
            Every(sim, 1);
            if (sim.Director.Active.Any(c => c.Kind == kind))
                return sim.Clock.Tick;
        }
        return -1;
    }

    [Fact]
    public void ABeatWaitsForTheLastTroubleToSettleButComesByTheEndOfItsWindow()
    {
        // Calm, with a party in town and the town well ahead: it comes at the start of its window.
        var calm = Round([Beat(2, 10, 14, "Shortage")]);
        Every(calm, 1);
        calm.Clock.Tick = calm.Round.TickAt(calm, 2, 9);
        calm.Parties.Send(calm, calm.Defs.AllParties[0]);
        calm.Reckoning.Add(calm, Reckoning.RenownTarget(calm), 0f);
        var came = Until(calm, CrisisKind.Shortage, calm.Round.TickAt(calm, 2, 15));
        Assert.InRange(came, calm.Round.TickAt(calm, 2, 10), calm.Round.TickAt(calm, 2, 11));

        // The same, with raiders about and nobody going out to them: it waits till its window's end.
        var busy = Round([Beat(2, 10, 14, "Shortage")]);
        Every(busy, 1);
        busy.Clock.Tick = busy.Round.TickAt(busy, 2, 9);
        busy.Parties.Send(busy, busy.Defs.AllParties[0]);
        busy.Reckoning.Add(busy, Reckoning.RenownTarget(busy), 0f);
        Assert.NotNull(busy.Director.Send(busy, CrisisKind.Raid));
        foreach (var e in busy.Creatures())
            busy.World.Get<Mind>(e).Drafted = true;
        came = Until(busy, CrisisKind.Shortage, busy.Round.TickAt(busy, 2, 15));
        Assert.True(came >= busy.Round.TickAt(busy, 2, 14), $"came at {came}, before its window's end {busy.Round.TickAt(busy, 2, 14)}");
    }

    [Fact]
    public void AQuestCanSetOffABeatInPlaceOfAnother()
    {
        var sim = Round([Beat(3, 6, 8, "Dragon", id: "dragon"), Beat(1, 0, 0, "Fire")]);
        var follower = sim.Defs.Season.Beats[1];
        follower.FollowsQuest = "none yet"; // the round's quests are rolled on its first update
        follower.AfterHours = [2, 3];
        follower.Replaces = "dragon";
        follower.Omen = "The hills rumble.";
        Every(sim, 1);
        var quest = sim.Expeditions.Quests.First(q => q.Def.Party == null);
        follower.FollowsQuest = quest.Def.Name;
        Every(sim, 6);
        Assert.Empty(sim.Director.Active); // nobody's gone: it sleeps
        quest.Status = QuestStatus.Cleared; // they went, and came back
        Every(sim, 1);
        Assert.Contains(sim.Messages.Messages, m => m.Text == "The hills rumble.");
        var came = Until(sim, CrisisKind.Fire, sim.Clock.Tick + SimClock.HoursToTicks(4));
        Assert.True(came > 0, "what the party stirred up came to town");
        // And the dragon it came in place of doesn't.
        sim.Clock.Tick = sim.Round.TickAt(sim, 3, 5);
        Assert.Equal(-1, Until(sim, CrisisKind.Dragon, sim.Round.TickAt(sim, 3, 9)));
    }

    [Fact]
    public void AQuietTownGetsAnIncident()
    {
        var sim = Round([]);
        sim.Parties.Send(sim, sim.Defs.AllParties[0]);
        for (var h = 0; h < 12 && sim.Director.IncidentsSent.Count == 0; h++)
            sim.TickMany(SimClock.TicksPerHour);
        var (name, count) = Assert.Single(sim.Director.IncidentsSent);
        Assert.Equal(1, count);
        Assert.Contains(sim.Defs.AllIncidents, d => d.Name == name);
    }

    [Fact]
    public void EachIncidentDoesWhatItSays()
    {
        var sim = Round([]);
        sim.Parties.Send(sim, sim.Defs.AllParties[0]);
        sim.TickMany(SimClock.TicksPerHour); // they've walked in
        var w = sim.World;
        Assert.True(sim.Director.CallIncident(sim, "RatsInTheLarder"));
        Assert.NotEmpty(w.Query<Vermin>());
        Assert.True(sim.Director.CallIncident(sim, "FeverFromTheRoad"));
        Assert.Contains(sim.Creatures(), e => w.Has<Adventurer>(e) && Healing.FeverSystem.Has(sim, e));
        Assert.True(sim.Director.CallIncident(sim, "TravellersErrand"));
        Assert.Contains(sim.Favours.All, f => f.IsOpen && w.Has<Adventurer>(f.Poster));
        // Each only so often a season.
        Assert.False(sim.Director.CallIncident(sim, "RatsInTheLarder"));
        Assert.False(sim.Director.CallIncident(sim, "NoSuchIncident"));
        var loaded = SaveLoadTests.RoundTrip(sim);
        Assert.Equal(sim.Director.IncidentsSent, loaded.Director.IncidentsSent);
    }

    [Fact]
    public void TroubleLeansTowardWhatsNewAndWhatSomebodyCanMeet()
    {
        var sim = Round();
        Every(sim, 1);
        var w = sim.World;
        var me = Players.CharacterOf(sim, Me);
        var mine = w.Get<Trade>(me).Def.Role;
        Assert.Equal(Cover.Player, RuleMind.CoverOf(sim, mine));
        var other = new[] { TownRole.Kitchen, TownRole.Shrine, TownRole.Forge }.First(r => r != mine);
        Assert.Equal(Cover.Ai, RuleMind.CoverOf(sim, other));

        var pacing = sim.Defs.Season.Pacing;
        var weighed = RuleMind.Weigh(sim, [(CrisisKind.Fire, 1f), (CrisisKind.Raid, 1f)], [CrisisKind.Fire]).ToDictionary(k => k.Kind, k => k.Weight);
        Assert.Equal(pacing.SeenWeight, weighed[CrisisKind.Fire], 3);

        // Nobody able to answer it: the empath down.
        foreach (var e in w.Query<Trade>().Where(e => w.Get<Trade>(e).Def.Role == TownRole.Shrine).ToList())
            w.Get<Creature>(e).Downed = true;
        Assert.Equal(Cover.Nobody, RuleMind.CoverOf(sim, TownRole.Shrine));
        weighed = RuleMind.Weigh(sim, [(CrisisKind.Plague, 1f)], []).ToDictionary(k => k.Kind, k => k.Weight);
        Assert.Equal(pacing.NobodyWeight, weighed[CrisisKind.Plague], 3);
    }

    [Fact]
    public void AChoiceFromOutsideSettlesTheNextBeat()
    {
        var sim = Round([Beat(1, 21, 22, "Raid"), Beat(2, 10, 14, "Fire,Shortage,Plague")]);
        Every(sim, 1);
        // The first beat can only be a raid.
        Assert.False(sim.Director.Choose(sim, CrisisKind.Plague));
        sim.ApplyNow(new DirectorChoiceCommand(CrisisKind.Raid, null));
        sim.Clock.Tick = sim.Round.TickAt(sim, 1, 23);
        Every(sim, 1); // the first comes and goes by
        Assert.True(sim.Director.Choose(sim, CrisisKind.Shortage));
        Assert.False(sim.Director.Choose(sim, CrisisKind.Fire)); // settled already
        sim.Clock.Tick = sim.Round.TickAt(sim, 2, 13);
        Assert.True(Until(sim, CrisisKind.Shortage, sim.Round.TickAt(sim, 2, 15)) > 0);
    }
}
