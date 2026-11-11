using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Errands;
using SupportingCast.Sim.Expeditions;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Systems;
using static SupportingCast.Sim.Tests.TestWorld;

namespace SupportingCast.Sim.Tests;

/// <summary>The Quest Broker and the off-map expedition (Stage 6 of M2).</summary>
public class ExpeditionTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    internal static (Simulation Sim, Entity Me, List<Entity> Party) Round(string trade, ulong seed = 1234)
    {
        var sim = Simulation.CreateStandard(LoadDefs(), 160, 160, seed, MapGenerator.RecommendedDepth);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(trade)], []));
        sim.ApplyNow(new StartRoundCommand());
        var party = sim.Parties.Send(sim, sim.Defs.Party("IronLantern"));
        Assert.NotEmpty(party);
        // Until they've asked for work.
        for (var t = 0; t < 40 && ExpeditionSystem.QuestErrand(sim, sim.World.Get<Adventurer>(party[0]).Party) == null; t++)
            sim.TickMany(SimClock.TicksPerHour / 4);
        return (sim, Players.CharacterOf(sim, Ana), party);
    }

    private static void Beside(Simulation sim, Entity who, Entity other)
    {
        var at = sim.World.Get<Position>(other).Cell;
        var cell = sim.SpiralFrom(at, 2).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z);
        sim.World.Get<Position>(who).Cell = cell;
        sim.World.Get<Position>(who).Pos = Position.CenterOf(cell);
    }

    private static int PartyOf(Simulation sim, Entity e) => sim.World.Get<Adventurer>(e).Party;

    [Fact]
    public void ARoundRollsQuestsAndOnlyTheBrokerStartsWithAGuess()
    {
        var (sim, me, _) = Round("QuestBroker");
        Assert.Equal(ExpeditionSystem.QuestsPerRound, sim.Expeditions.Quests.Count);
        var known = Perception.For(sim, me).Quests().ToList();
        Assert.Equal(ExpeditionSystem.QuestsPerRound, known.Count);
        Assert.All(known, q => Assert.InRange(q.Confidence, 0.1f, 0.5f));
        var villager = sim.Creatures().First(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Healer");
        Assert.All(sim.Expeditions.Quests, q => Assert.Null(sim.Expeditions.IntelOf(villager, q.Id)));
    }

    [Fact]
    public void APartyTurnsDownAQuestThatSoundsWrongForThem()
    {
        var (sim, me, party) = Round("QuestBroker");
        var q = sim.Expeditions.Quests[0];
        var strength = ExpeditionSystem.PartyStrength(sim, PartyOf(sim, party[0]));
        sim.Expeditions.Hear(me, q.Id, strength * 3f, null, 1f);
        Beside(sim, me, party[0]);

        Assert.False(sim.Expeditions.Offer(sim, me, party[0], q.Id));
        Assert.Equal(QuestStatus.Open, q.Status);
        Assert.False(sim.World.Has<Combat.Leaving>(party[0]));
        Assert.Contains(sim.Speech.Kept, u => u.Text.Contains("mad"));
    }

    /// <summary>Send the party on a quest that sounds right, and wait until they're back (or known lost).</summary>
    private static (Quest Quest, List<string> Names) SendAndWait(Simulation sim, Entity me, List<Entity> party)
    {
        var q = sim.Expeditions.Quests[0];
        var strength = ExpeditionSystem.PartyStrength(sim, PartyOf(sim, party[0]));
        sim.Expeditions.Hear(me, q.Id, strength * 0.9f, null, 0.6f);
        Beside(sim, me, party[0]);
        var purse = Coin.Count(sim, me);
        var names = party.Select(e => sim.World.Get<Creature>(e).Name).ToList();

        Assert.True(sim.Expeditions.Offer(sim, me, party[0], q.Id));
        Assert.True(Coin.Count(sim, me) > purse, "the broker is paid for the match");
        Assert.Equal(QuestStatus.Taken, q.Status);
        Assert.All(party, e => Assert.True(sim.World.Has<Combat.Leaving>(e)));

        for (var h = 0; h < 30 && sim.Expeditions.Succeeded + sim.Expeditions.Failed == 0 && !sim.Messages.Messages.Any(m => m.Text.StartsWith("No one")); h++)
            sim.TickMany(SimClock.TicksPerHour);
        Assert.Equal(1, sim.Expeditions.Succeeded + sim.Expeditions.Failed);
        return (q, names);
    }

    [Fact]
    public void ASentPartyComesBackChangedAndTellsItAsItWas()
    {
        var (sim, me, party) = Round("QuestBroker");
        var gone = party.ToList();
        var id = PartyOf(sim, party[0]);
        var (q, names) = SendAndWait(sim, me, party);
        Assert.All(gone, e => Assert.False(sim.World.IsAlive(e))); // the ones who left are records now

        var back = sim.Creatures().Where(e => sim.World.TryGet<Adventurer>(e, out var a) && a.Party == id).ToList();
        Assert.All(back, e => Assert.Contains(sim.World.Get<Creature>(e).Name, names));
        Assert.NotEmpty(back);
        Assert.All(back, e => Assert.True(sim.World.Has<LookedAs>(e), "they keep their faces"));
        Assert.Contains(back, e => sim.World.Get<Health>(e).Injuries.Count > 0
            || (sim.World.TryGet<Equipment>(e, out var eq) && Combat.WeaponWear.HpFraction(sim, eq.Weapon) < 0.99f));
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("return from"));

        // The one with the tale tells it at the tavern; whoever hears knows the truth.
        var teller = back.Single(e => sim.World.Has<ReportPending>(e));
        var haunt = Adventurers.PartySystem.HauntOf(sim, sim.World.Get<Adventurer>(teller).PartyDef)!.Anchor;
        for (var t = 0; t < 600 && sim.World.Has<ReportPending>(teller); t++)
        {
            Beside(sim, me, teller);
            sim.TickMany(SimClock.TicksPerSecond);
        }
        Assert.False(sim.World.Has<ReportPending>(teller), $"never told it (at {sim.World.Get<Position>(teller).Cell}, tavern {haunt})");
        var intel = sim.Expeditions.IntelOf(me, q.Id)!;
        Assert.Equal(1f, intel.Confidence);
        Assert.Equal(q.Danger, intel.Danger, 0.01f);
    }

    [Fact]
    public void RumoursAtTheTavernReachThoseWhoListen()
    {
        // A player broker, so no AI one sends the party off before they've talked.
        var (sim, me, party) = Round("QuestBroker");
        var haunt = Adventurers.PartySystem.HauntOf(sim, sim.Defs.Party("IronLantern"))!.Anchor;
        bool Heard() => Perception.For(sim, me).Quests().Any(q => q.Confidence > 0.3f);
        Assert.False(Heard());
        for (var h = 0; h < 10 && !Heard(); h++)
        {
            var cell = sim.SpiralFrom(haunt, 3).First(c => sim.Map.Passable(c) && c.Z == haunt.Z);
            sim.World.Get<Position>(me).Cell = cell;
            sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);
            sim.TickMany(SimClock.TicksPerHour);
        }
        Assert.True(Heard(), "a rumour should have sharpened what the broker believes");
    }

    [Fact]
    public void AnAIBrokerSendsAPartyOut()
    {
        var (sim, _, party) = Round("Healer");
        for (var h = 0; h < 12 && sim.Expeditions.Out.Count == 0; h++)
            sim.TickMany(SimClock.TicksPerHour / 2);
        Assert.NotEmpty(sim.Expeditions.Out);
    }

    [Fact]
    public void WellKeptGearAndTendedWoundsMakeAStrongerAdventurer()
    {
        var (sim, _, party) = Round("Healer");
        var fresh = ExpeditionSystem.Record(sim, party[0]);
        var neglected = ExpeditionSystem.Record(sim, party[0]);
        neglected.Weapon = neglected.Weapon! with { Hp = 0.3f, Dullness = 0.9f, Quality = Quality.Crude };
        neglected.Pain = 0.5f;
        neglected.Blood = 0.8f;
        var master = ExpeditionSystem.Record(sim, party[0]);
        master.Weapon = master.Weapon! with { Quality = Quality.Masterwork };
        master.Satisfaction = 3f;

        Assert.True(ExpeditionSystem.Strength(neglected) < ExpeditionSystem.Strength(fresh) * 0.6f);
        Assert.True(ExpeditionSystem.Strength(master) > ExpeditionSystem.Strength(fresh));
    }
}
