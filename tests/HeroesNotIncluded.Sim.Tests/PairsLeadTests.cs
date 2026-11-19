using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Work for two played as the helper alone (<c>--help-as</c>, <see cref="PairsLead"/>): a bot leads and waits for a hand
/// rather than calling a villager, and with someone at the helper's end the work gets done. The helper here is a
/// second bot, which lends a hand where one's wanted, as a player would with F.
/// </summary>
[Collection(nameof(SimContentTests))]
public class PairsLeadTests
{
    private static readonly Guid Lead = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Helper = new("00000000-0000-0000-0000-00000000000b");

    private static (Simulation Sim, Entity Lead, Entity Helper, BotPlayer LeadBot) Staged(string kind)
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        var trade = PairsLead.LeadTrade(sim, kind)!;
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Lead, "Lead", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Lead, [sim.Defs.Trade(trade)], []));
        sim.ApplyNow(new JoinLobbyCommand(Helper, "Helper", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Helper, [sim.Defs.Trade(trade == "Tinker" ? "Innkeeper" : "Tinker")], []));
        sim.ApplyNow(new StartRoundCommand());
        var (lead, helper) = (Players.CharacterOf(sim, Lead), Players.CharacterOf(sim, Helper));
        foreach (var e in new[] { lead, helper })
            Array.Fill(sim.World.Get<Needs>(e).Values, 1f); // awake and fed: the work, not their supper
        sim.ApplyNow(new DevPairsLeadCommand(kind, lead, helper));
        var bot = new BotPlayer(() => sim, Lead, c => sim.ApplyNow(c), 7) { Temper = BotTemperament.Steady, WaitsForHand = true };
        return (sim, lead, helper, bot);
    }

    [Theory]
    [InlineData(PairsLead.Striker)]
    [InlineData(PairsLead.Keeper)]
    [InlineData("pitman")]
    [InlineData("lifter")]
    public void ABotLeadsWaitsForAHandAndTheWorkGetsDone(string kind)
    {
        var (sim, lead, helper, leadBot) = Staged(kind);
        var w = sim.World;
        Assert.Equal(PairsLead.LeadTrade(sim, kind), w.Get<Trade>(lead).Def.Name);

        // Alone, the lead gets to the work and waits there: nobody's called, so nobody comes.
        var waited = false;
        for (var t = 0; t < SimClock.TicksPerHour * 2 && !waited; t++)
        {
            sim.Tick();
            leadBot.Tick();
            waited = Pairs.WantsHand(sim, lead);
        }
        Assert.True(waited, $"{kind}: the lead got to the part that wants a hand; doing '{leadBot.Doing}', in {(w.TryGet<WorkSession>(lead, out var ls) ? ls.GetType().Name : "nothing")}; "
            + string.Join(" | ", sim.Messages.Messages.TakeLast(8).Select(m => m.Text)));
        // Left a moment, nobody's called in. (Not long: a smith's piece cooling and back to the fire with the striker
        // stood idle heats the smithy, and a bot steps back from it, BotPlayer.Safety.)
        for (var t = 0; t < SimClock.TicksPerSecond * 5; t++)
        {
            sim.Tick();
            leadBot.Tick();
            Assert.DoesNotContain(sim.Creatures(), e => w.TryGet<Helping>(e, out var h) && h.Lead == lead);
        }

        // A hand at the other end, and it's seen through.
        var helperBot = new BotPlayer(() => sim, Helper, c => sim.ApplyNow(c), 11) { Temper = BotTemperament.Steady };
        var victim = kind == PairsLead.Keeper ? w.Query<Creature>().First(e => w.Get<Creature>(e).Dead) : Entity.Null;
        var wagon = kind == "lifter" ? w.Query<Wagon>().First() : Entity.Null;
        var greatswords = Greatswords(sim);
        var planks = Planks(sim);
        bool Done() => kind switch
        {
            PairsLead.Striker => Greatswords(sim) > greatswords,
            PairsLead.Keeper => !w.Get<Creature>(victim).Dead,
            "pitman" => Planks(sim) >= planks + 5, // the great log's long planks (a rough cut makes fewer)
            _ => w.Get<Wagon>(wagon).Damage == WagonDamage.None,
        };
        var helped = false;
        var log = new List<string>();
        var strikes = 0;
        for (var t = 0; t < SimClock.TicksPerHour * 4 && !Done(); t++)
        {
            // F at the work whenever it wants a hand, as the player would (a bot of its own accord steps back from the
            // smithy's heat); then the helper's game is the bot's to play.
            if (!w.Has<WorkSession>(helper) && Pairs.WantsHand(sim, lead) && w.Get<WorkSession>(lead).Focus(sim) is { } at)
            {
                if (log.Count < 6)
                    log.Add($"t{t} {AvatarSystem.ActionAt(sim, helper, at).Kind} from {w.Get<Position>(helper).Cell} to {at}");
                sim.ApplyNow(new AvatarWorkCommand(helper, true, at));
                sim.ApplyNow(new AvatarWorkCommand(helper, false));
            }
            sim.Tick();
            leadBot.Tick();
            if (w.Has<WorkSession>(helper))
                helperBot.Tick(); // only the helper's game: a bot left to itself would step back from the smithy's heat
            helped |= w.TryGet<WorkSession>(helper, out var s) && s is HelperSession;
            if (s is SledgeSession sledge)
                strikes = Math.Max(strikes, sledge.Strikes);
        }
        Assert.True(helped, $"{kind}: the helper lent a hand; " + string.Join("; ", log));
        if (kind == PairsLead.Striker)
            Assert.True(strikes > 0, "the striker's sledge came down on the smith's call");
        Assert.True(Done(), $"{kind}: the work for two got done; {string.Join(" | ", sim.Messages.Messages.TakeLast(8).Select(m => m.Text))}");
    }

    [Fact]
    public void FAtTheSmithWaitingForAStrikerLendsTheHandNotATrade()
    {
        // A co-op try: the second player went up to the smith and F offered "Trade with" them, not the sledge.
        var (sim, lead, helper, leadBot) = Staged(PairsLead.Striker);
        var w = sim.World;
        for (var t = 0; t < SimClock.TicksPerHour && !Pairs.WantsHand(sim, lead); t++)
        {
            sim.Tick();
            leadBot.Tick();
        }
        Assert.True(Pairs.WantsHand(sim, lead));
        var anvil = w.Get<WorkSession>(lead).Focus(sim)!.Value;
        Assert.True(AvatarSystem.InReach(w.Get<Position>(helper).Cell, anvil), "the striker by the anvil");
        var smith = w.Get<Position>(lead).Cell;
        var action = AvatarSystem.ActionAt(sim, helper, smith);
        Assert.Equal(AvatarActionKind.LendAHand, action.Kind);
        sim.ApplyNow(new AvatarWorkCommand(helper, true, smith));
        sim.ApplyNow(new AvatarWorkCommand(helper, false));
        Assert.IsType<SledgeSession>(w.Get<WorkSession>(helper));
    }

    private static int Greatswords(Simulation sim) =>
        sim.World.Query<Thing>().Count(e => sim.World.Get<Thing>(e).Def.Name == "Greatsword");

    private static int Planks(Simulation sim) =>
        sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e).Def.Name == "Planks").Sum(e => sim.World.TryGet<Stack>(e, out var s) ? s.Count : 1);
}
