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

    /// <summary>On through the gathering and the crier's tale to the revels.</summary>
    private static void ToRevels(Simulation sim)
    {
        ToFestival(sim);
        for (var t = 0; t < SimClock.TicksPerHour * 4 && sim.Festival.Part == FestivalPart.Opening; t += 30)
            sim.TickMany(30);
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
        var before = w.Get<Adventurer>(guest).Satisfaction; // the evening's moved it already
        sim.Festival.Update(sim);
        var liking = sim.Defs.Festival.AdmireSatisfaction[(int)Quality.Masterwork];
        Assert.Equal(before + liking, w.Get<Adventurer>(guest).Satisfaction, 3);
        sim.Festival.Update(sim);
        Assert.Equal(before + liking, w.Get<Adventurer>(guest).Satisfaction, 3); // each piece once
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains($"{w.Get<Creature>(maker).Name}'s") && m.Text.Contains("show table"));
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

    [Fact]
    public void TheCrierTellsTheSeasonAtTheWellBeforeTheRevels()
    {
        var sim = Round();
        SendParty(sim, 2f);
        sim.Reckoning.CrisisResolved(sim, CrisisKind.Raid, true);
        sim.Reckoning.CrisisResolved(sim, CrisisKind.Fire, false);
        ToFestival(sim, 1);
        Assert.False(sim.Festival.Telling, "the town gathers first");
        var crier = sim.Festival.Crier;
        Assert.False(crier.IsNull);
        Assert.False(sim.World.Has<PlayerCharacter>(crier), "a player's character isn't made to make speeches");

        var tale = sim.Festival.Tale(sim);
        Assert.Contains(tale, l => l.Contains("drove them off"));
        Assert.Contains(tale, l => l.Contains("burned what it would"));
        Assert.Contains(tale, l => l.Contains("show table"));

        // Every line's said, in order, each given its reading time before the next; then the revels.
        var heardAt = new Dictionary<string, long>();
        while (sim.Festival.Part == FestivalPart.Opening)
        {
            sim.TickMany(10);
            foreach (var u in sim.Speech.Kept)
                if (u.Speaker == sim.Festival.Crier || tale.Contains(u.Text))
                    heardAt.TryAdd(u.Text, u.Tick);
            Assert.True(sim.Clock.Tick < sim.Festival.CeremonyTick(sim), "the tale's over well before the ceremony");
        }
        Assert.Equal(FestivalPart.Revels, sim.Festival.Part);
        var said = tale.Where(heardAt.ContainsKey).ToList();
        Assert.Equal(tale, said);
        for (var k = 1; k < tale.Count; k++)
            Assert.True(heardAt[tale[k]] - heardAt[tale[k - 1]] >= Speech.SpeechSystem.ReadingTicks(tale[k - 1]),
                $"\"{tale[k - 1]}\" was given time to be read");
        Assert.Contains(sim.Messages.Messages, m => m.Text.StartsWith("The revels are open") && m.Location == null);
    }

    [Fact]
    public void TheCrowdGathersCloseForTheTale()
    {
        var sim = Round();
        ToFestival(sim, 1);
        while (!sim.Festival.Telling)
            sim.TickMany(30);
        sim.TickMany(SimClock.TicksPerSecond * 10);
        var w = sim.World;
        var square = sim.Round.Square!.Value;
        var listening = sim.Creatures().Count(e => !w.Has<Avatar>(e) && w.Get<Mind>(e).Job is FestivalJob { Activity: FestivalActivity.Gather });
        Assert.True(listening > 0, "the town gathers round the well to hear it");
    }

    [Fact]
    public void APlayerLaysTheirOwnWorkOnTheShowTableFromTheirPack()
    {
        var sim = Round();
        ToRevels(sim);
        var w = sim.World;
        var me = Me(sim);
        var table = sim.Festival.Prop(sim, FestivalSystem.ShowTableDef);
        w.Get<Position>(me) = Position.At(Beside(sim, table));
        var cell = w.Get<Thing>(table).Cell;
        Assert.Equal(AvatarActionKind.OpenStorage, AvatarSystem.ActionAt(sim, me, cell).Kind); // nothing of mine to show yet

        var plain = sim.CreateItem(sim.Defs.Thing("IronSpear"), 1);
        w.Add(plain, new Crafted { Quality = Quality.Plain, Maker = me });
        var fine = sim.CreateItem(sim.Defs.Thing("IronSpear"), 1);
        w.Add(fine, new Crafted { Quality = Quality.Fine, Maker = me });
        Assert.True(sim.Store(sim.PackOf(me), plain).IsNull);
        Assert.True(sim.Store(sim.PackOf(me), fine).IsNull);

        var action = AvatarSystem.ActionAt(sim, me, cell);
        Assert.Equal(AvatarActionKind.ShowPiece, action.Kind);
        Assert.Equal(fine, action.Item); // the best of it
        Assert.Contains("show table", action.Label);
        AvatarSystem.Perform(sim, me, action);
        Assert.Contains(fine, sim.StoredIn(table));
    }

    [Fact]
    public void TheCeremonyGivesEachLineItsTimeAndEndsOnceTheVerdictsSunkIn()
    {
        var sim = Round();
        SendParty(sim, 4f);
        ToFestival(sim);
        sim.Clock.Tick = sim.Festival.CeremonyTick(sim) - 1;
        long? verdictAt = null;
        string verdict = "";
        while (sim.Round.Phase == RoundPhase.Festival)
        {
            sim.TickMany(10);
            if (verdictAt == null && sim.Speech.Kept.LastOrDefault(u => u.Text.StartsWith("Friends! The season's over.")) is { } v)
                (verdictAt, verdict) = (v.Tick, v.Text);
        }
        Assert.Equal(RoundPhase.Over, sim.Round.Phase);
        Assert.NotNull(verdictAt);
        Assert.True(sim.Round.Summary!.EndedTick - verdictAt >= Speech.SpeechSystem.ReadingTicks(verdict), "the verdict's read before the reveal");
        Assert.True(sim.Round.Summary.EndedTick < sim.Round.EndTick(sim), "with one party and a few honours, it's over sooner than the clock");

        string[] ceremony = ["Gather round", "Our guests first", "No travellers", "And for ", "Friends! The season's over"];
        var said = sim.Speech.Kept.Where(u => u.Tick >= sim.Festival.CeremonyTick(sim) && ceremony.Any(u.Text.StartsWith)).ToList();
        Assert.True(said.Count >= 3, "the bell, the guests' turn and the verdict");
        for (var k = 1; k < said.Count; k++)
            if (said[k - 1].Speaker == said[k].Speaker)
                Assert.True(said[k].Tick - said[k - 1].Tick >= Speech.SpeechSystem.ReadingTicks(said[k - 1].Text),
                    $"\"{said[k - 1].Text}\" was given time to be read");
    }

    [Fact]
    public void ASaveMidTaleCarriesOnTheTale()
    {
        var sim = Round();
        ToFestival(sim, 1);
        while (!sim.Festival.Telling)
            sim.TickMany(30);
        sim.TickMany(SimClock.TicksPerSecond * 3);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.True(loaded.Festival.Telling);
        Assert.Equal(sim.Festival.Crier, loaded.Festival.Crier);
        for (var t = 0; t < SimClock.TicksPerHour * 4 && loaded.Festival.Part == FestivalPart.Opening; t += 30)
            loaded.TickMany(30);
        Assert.Equal(FestivalPart.Revels, loaded.Festival.Part);
    }

    /// <summary>Loose items (not plants, not furniture) within reach of the square.</summary>
    private static List<string> LooseInTheSquare(Simulation sim)
    {
        var w = sim.World;
        var square = sim.Round.Square ?? new int3(40, 48, 0);
        return w.Query<Thing>().Select(e => w.Get<Thing>(e))
            .Where(t => t.Spawned && t.Def.Category == ThingCategory.Item && t.Cell.Z == square.Z
                && Math.Abs(t.Cell.X - square.X) <= 10 && Math.Abs(t.Cell.Y - square.Y) <= 10
                && Buildings.At(sim, t.Cell).IsNull && sim.Map.Structure[sim.Map.Idx(t.Cell)].IsNull
                && t.Def.Name is not ("Bucket" or "WaterBucket"))
            .Select(t => $"{t.Def.Name} at {t.Cell}").ToList();
    }

    [Fact]
    public void TheSquareIsntLitteredWithTheTownsStores()
    {
        var sim = Round();
        Assert.Empty(LooseInTheSquare(sim));
        // An AI smith's kit stock is in the smithy, not dropped where they stood.
        var w = sim.World;
        var smith = sim.Creatures().First(e => !w.Has<PlayerCharacter>(e) && w.TryGet<Trade>(e, out var t) && t.Def.Kit.Any(k => k.Def == "IronBar"));
        var smithy = Shops.ShopSystem.BuildingOf(sim, w.Get<Trade>(smith).Def.WorkplaceDef!);
        Assert.Contains(w.Query<PartOf>(), e => w.Get<PartOf>(e).Building == smithy && sim.StoredIn(e).Any(i => w.Get<Thing>(i).Def.Name == "Leather"));
    }

    [Fact]
    public void TheFeastIsLaidOnItsTables()
    {
        var sim = Round();
        ToFestival(sim, 1);
        var w = sim.World;
        var tables = sim.Festival.PropsOf(sim, sim.Defs.Festival.FeastTable).ToList();
        Assert.NotEmpty(tables);
        var dishes = sim.Defs.Festival.Feast.Select(d => d.Def).ToHashSet();
        foreach (var table in tables)
        {
            var on = sim.StoredIn(table).Select(e => w.Get<Thing>(e).Def.Name).ToHashSet();
            Assert.True(dishes.SetEquals(on), $"laid with {string.Join(", ", on)}");
            Assert.All(sim.StoredIn(table), e => Assert.False(sim.IsAvailable(e), "nobody's job clears the feast away"));
        }
    }

    [Fact]
    public void APlayerSitsOnAFeastStoolUntilTheyMove()
    {
        var sim = Round();
        ToRevels(sim);
        var w = sim.World;
        var me = Me(sim);
        var stool = sim.Festival.PropsOf(sim, "Stool").First(s => !sim.Reservations.IsReservedByOther(s, me)
            && !sim.Creatures().Any(c => w.Get<Position>(c).Cell == w.Get<Thing>(s).Cell));
        w.Get<Position>(me) = Position.At(Beside(sim, stool));
        var action = AvatarSystem.ActionAt(sim, me, w.Get<Thing>(stool).Cell);
        Assert.Equal(AvatarActionKind.Sit, action.Kind);
        AvatarSystem.Perform(sim, me, action);
        sim.TickMany(SimClock.TicksPerSecond * 3);
        Assert.True(w.Get<Mind>(me).Job is SitJob { Seated: true }, $"{w.Get<Mind>(me).Job?.GetType().Name}");
        Assert.Equal(w.Get<Thing>(stool).Cell, w.Get<Position>(me).Cell);
        Assert.False(SitJob.CanSit(sim, sim.Creatures().First(e => e != me && w.Has<Mind>(e)), stool), "nobody else takes the seat");

        // Walking off stands them up.
        w.Get<Combat.FreeMover>(me).Wish = new float2(1, 0);
        sim.TickMany(10);
        Assert.False(w.Get<Mind>(me).Job is SitJob);
    }
}
