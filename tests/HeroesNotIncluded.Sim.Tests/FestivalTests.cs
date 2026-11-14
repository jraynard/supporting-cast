using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The season's festival (<see cref="FestivalSystem"/>): what goes up in the square, the revels
/// (the feast, the dance, the show table, the games) and the ceremony that ends the round.
/// </summary>
public class FestivalTests
{
    private static Simulation Round(ulong seed = 7)
    {
        var (sim, _) = TestWorld.Millbrook(seed, 4);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Guid.Empty, "Arin", default, []));
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    private static Entity Me(Simulation sim) => sim.Creatures().Single(e => sim.World.Has<Avatar>(e));

    /// <summary>Jump to the festival's start and play a little into it.</summary>
    private static void ToFestival(Simulation sim, int ticks = SimClock.TicksPerHour / 4)
    {
        sim.Clock.Tick = sim.Round.FestivalTick(sim) - 1;
        sim.TickMany(ticks);
        Assert.Equal(RoundPhase.Festival, sim.Round.Phase);
    }

    private static void ToRevels(Simulation sim)
    {
        ToFestival(sim);
        sim.TickMany(SimClock.HoursToTicks(sim.Defs.Festival.OpeningHours));
        Assert.Equal(FestivalPart.Revels, sim.Festival.Part);
    }

    /// <summary>Somewhere to stand beside a thing, in reach of it.</summary>
    private static int3 Beside(Simulation sim, Entity thing)
    {
        var at = sim.World.Get<Thing>(thing).Cell;
        return sim.SpiralFrom(at, 3).First(c => c.Z == at.Z && sim.Map.Passable(c) && sim.Map.Structure[sim.Map.Idx(c)].IsNull
            && AvatarSystem.InReach(sim, c, thing));
    }

    private static List<Entity> SendParty(Simulation sim, float satisfaction)
    {
        var party = sim.Parties.Send(sim, sim.Defs.AllParties[0]);
        Assert.NotEmpty(party);
        foreach (var e in party)
        {
            ref var a = ref sim.World.Get<Adventurer>(e);
            a.Satisfaction = satisfaction;
            a.LeaveTick = long.MaxValue / 4; // staying for the festival
        }
        return party;
    }

    [Fact]
    public void EverythingTheMapListsGoesUpWhenTheFestivalBegins()
    {
        var sim = Round();
        var map = sim.Defs.Map("Millbrook");
        Assert.NotEmpty(map.Festival);
        Assert.Empty(sim.Festival.Props);
        Assert.True(sim.World.Query<Thing>().All(e => sim.World.Get<Thing>(e).Def.Name != FestivalSystem.BonfireDef), "not before the festival");

        ToFestival(sim, 1);
        Assert.Equal(map.Festival.Count, sim.Festival.Props.Count);
        var bonfire = sim.Festival.Prop(sim, FestivalSystem.BonfireDef);
        Assert.False(bonfire.IsNull);
        Assert.True(sim.Burning(bonfire), "the bonfire's lit");
        Assert.All(sim.Festival.PropsOf(sim, "FestivalLantern"), l => Assert.True(sim.Burning(l)));
        Assert.Equal(FestivalPart.Opening, sim.Festival.Part);
        Assert.Equal(sim.Defs.Festival.Games.Count, sim.Festival.Games.Count);
        Assert.True(sim.Festival.BellTick >= 0);
    }

    [Fact]
    public void TheRevelsFillTheSquareWithFeastingDancingAndGames()
    {
        var sim = Round();
        SendParty(sim, 2f);
        ToRevels(sim);
        var seen = new HashSet<FestivalActivity>();
        var sat = false;
        var danced = false;
        for (var t = 0; t < SimClock.TicksPerHour * 2; t += 20)
        {
            sim.TickMany(20);
            foreach (var e in sim.Creatures())
                if (sim.World.Get<Mind>(e).Job is FestivalJob job)
                {
                    seen.Add(job.Activity);
                    sat |= job.Seated;
                    danced |= job.Dancing;
                }
        }
        Assert.True(seen.Count >= 3, $"only {string.Join(", ", seen)}");
        Assert.True(sat, "someone sat down at the feast");
        Assert.True(danced, "someone danced round the bonfire");
    }

    [Fact]
    public void TheInnServesAtTheFeastTables()
    {
        var sim = Round();
        ToRevels(sim);
        var villager = sim.Creatures().First(e => sim.World.Get<Creature>(e).Faction == Faction.Village && !sim.World.Has<Avatar>(e));
        var seat = Hospitality.Table.FreeSeat(sim, villager);
        Assert.False(seat.IsNull);
        Assert.True(sim.Festival.IsProp(seat), "a meal at the festival is eaten at its tables");
    }

    [Fact]
    public void AShowPieceWarmsTheVisitorsByHowWellItsMade()
    {
        var sim = Round();
        var party = SendParty(sim, 0f);
        ToRevels(sim);
        var w = sim.World;
        var table = sim.Festival.Prop(sim, FestivalSystem.ShowTableDef);
        var maker = Me(sim);
        var fine = sim.CreateItem(sim.Defs.Thing("IronSpear"), 1);
        w.Add(fine, new Crafted { Quality = Quality.Masterwork, Maker = maker });
        Assert.True(sim.Store(table, fine).IsNull);
        Assert.False(sim.IsAvailable(fine), "nobody's job takes it off the show table");

        var guest = party[0];
        w.Get<Mind>(guest).Job = null;
        w.Get<Position>(guest) = Position.At(Beside(sim, table));
        sim.Festival.Update(sim);
        var liking = sim.Defs.Festival.AdmireSatisfaction[(int)Quality.Masterwork];
        Assert.Equal(liking, w.Get<Adventurer>(guest).Satisfaction, 3);
        sim.Festival.Update(sim);
        Assert.Equal(liking, w.Get<Adventurer>(guest).Satisfaction, 3); // each piece once
    }

    [Fact]
    public void AStrikerSwingAtTheTopOfTheGaugeRingsTheBell()
    {
        var s = new StrikerSession { Game = "Striker" };
        s.Rand = new SimRandom(3);
        s.Hands = new Hands(1f, 1f, 5);
        // Swing whenever the needle stands at its top: the session is pure, so play it out step by step.
        for (var step = 0; step < SimClock.TicksPerSecond * 20 && !s.Done; step++)
        {
            var press = s.Flight == 0 && s.Needle >= 0.95f;
            s.Advance(press ? [new WorkInput(step, WorkInputKind.Press)] : [], step + 1);
        }
        Assert.True(s.Done);
        Assert.Equal(FestivalGames.Turns, s.Swings);
        Assert.True(s.Best >= StrikerSession.Bell, $"best {s.Best}");
    }

    [Fact]
    public void TheButtsScoreByTheRings()
    {
        Assert.Equal(10, ButtsSession.RingScore(0f));
        Assert.Equal(8, ButtsSession.RingScore(0.2f));
        Assert.Equal(1, ButtsSession.RingScore(0.9f));
        Assert.Equal(0, ButtsSession.RingScore(1.2f));
    }

    [Fact]
    public void AGameHasAWinnerWhoIsPaidAndHonoured()
    {
        var sim = Round();
        var party = SendParty(sim, 1f);
        ToRevels(sim);
        var w = sim.World;
        var me = Me(sim);
        var striker = sim.Festival.Prop(sim, "HighStriker");
        w.Get<Position>(me) = Position.At(Beside(sim, striker));
        Assert.Equal(AvatarActionKind.FestivalGame, AvatarSystem.ActionAt(sim, me, w.Get<Thing>(striker).Cell).Kind);
        var watcher = party[0];
        w.Get<Position>(watcher) = Position.At(w.Get<Position>(me).Cell + new int3(0, 1, 0));
        var before = w.Get<Adventurer>(watcher).Satisfaction;

        var game = sim.Defs.Festival.Games.Single(g => g.Name == "Striker");
        sim.Festival.Played(sim, me, game, 101); // clamped
        Assert.Equal(100, sim.Festival.Games.Single(g => g.Game == game).BestScore);
        Assert.True(w.Get<Adventurer>(watcher).Satisfaction > before, "the visitors enjoyed watching");

        var coin = Shops.Coin.Count(sim, me);
        sim.Clock.Tick = sim.Festival.CeremonyTick(sim) - 1;
        sim.TickMany(SimClock.TicksPerHour / 10);
        Assert.Equal(FestivalPart.Ceremony, sim.Festival.Part);
        Assert.Equal(coin + sim.Defs.Festival.PrizeSilver, Shops.Coin.Count(sim, me));
        Assert.Contains(sim.Festival.Honours, h => h.Who == me && h.Title == game.Honour);

        sim.Clock.Tick = sim.Round.EndTick(sim) - 1;
        sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.Equal(RoundPhase.Over, sim.Round.Phase);
        Assert.Contains(sim.Round.Summary!.Honours, h => h.Name == w.Get<Creature>(me).Name && h.Title == game.Honour);
    }

    [Fact]
    public void EachPartyIsToastedAndCountedOnceAtTheCeremony()
    {
        var sim = Round();
        var party = SendParty(sim, 4f);
        ToFestival(sim);
        Assert.Equal(0, sim.Reckoning.AdventurersCounted);

        // Play the ceremony through, a few minutes at a time so each step's heard.
        sim.Clock.Tick = sim.Festival.CeremonyTick(sim) - 1;
        while (sim.Round.Phase == RoundPhase.Festival)
            sim.TickMany(SimClock.TicksPerHour / 20);
        Assert.Equal(RoundPhase.Over, sim.Round.Phase);
        Assert.Equal(party.Count, sim.Reckoning.AdventurersCounted); // not again at the final count
        // What they thought by then (the revels move it), counted once: never twice over.
        var most = party.Count * sim.Defs.Season.SatisfactionRange[1] * sim.Defs.Season.RenownPerSatisfaction
            + sim.Defs.Festival.Games.Count * sim.Defs.Festival.GameRenown;
        Assert.InRange(sim.Reckoning.Renown, 0.01f, most);
        var said = sim.Speech.Kept.Select(u => u.Text).ToList();
        Assert.Contains(said, t => t.StartsWith("Gather round, everyone!"));
        var toasts = new[] { 6f, 3f, 0f, -3f, -6f }.Select(FestivalSystem.ToastLine).ToHashSet();
        var toast = said.FindIndex(toasts.Contains);
        Assert.True(toast >= 0, "the party gave its toast");
        Assert.True(toast < said.FindIndex(t => t.StartsWith("Friends! The season's over.")), "the toasts come before the verdict");
    }

    [Fact]
    public void TheFestivalSurvivesASave()
    {
        var sim = Round();
        SendParty(sim, 4f);
        ToRevels(sim);
        var me = Me(sim);
        sim.Festival.Played(sim, me, sim.Defs.Festival.Games[0], 70);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(sim.Festival.StartTick, loaded.Festival.StartTick);
        Assert.Equal(sim.Festival.Part, loaded.Festival.Part);
        Assert.Equal(sim.Festival.Props, loaded.Festival.Props);
        Assert.Equal(70, loaded.Festival.Games[0].BestScore);
        Assert.Equal(sim.Round.EndTick(sim), loaded.Round.EndTick(loaded));

        // A save from before the festival keeps what the map will put up.
        var (fresh, _) = SaveGame.Read(SaveGame.Write(Round()), sim.Defs);
        Assert.True(fresh.Festival.Planned);
    }

    [Fact]
    public void AnAiVillagerWithOrdersWaitingKeepsServing()
    {
        var sim = Round();
        ToRevels(sim);
        var w = sim.World;
        var cook = sim.Creatures().First(e => !w.Has<Avatar>(e) && w.TryGet<Trade>(e, out var t) && t.Def.Serving(ServiceKind.Drink) && t.Def.WorkplaceDef != null);
        Assert.False(Festival.StillServing(sim, cook));
        var guest = SendParty(sim, 0f)[0];
        var inn = w.Query<PresetBuilding>().First(b => w.Get<PresetBuilding>(b).Def == w.Get<Trade>(cook).Def.WorkplaceDef);
        sim.Shops.Post(sim, guest, sim.Defs.Service(ServiceKind.Drink), "An ale.", 0, sim.Clock.Tick + 1000, Entity.Null, inn, 0);
        Assert.True(Festival.StillServing(sim, cook));
        Assert.Null(Festival.Gathering(sim, cook));
    }
}
