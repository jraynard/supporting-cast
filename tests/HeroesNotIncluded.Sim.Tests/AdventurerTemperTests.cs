using HeroesNotIncluded.Sim.Adventurers;
using HeroesNotIncluded.Sim.Afterlife;
using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Adventurers as people, not order slips (<see cref="Tempers"/>): each has a temper of their own,
/// drink changes what they do, and what goes wrong around them gets a word out of them.
/// </summary>
public class AdventurerTemperTests
{
    private static Simulation Round()
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Guid.Empty, "Arin", default));
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    private static List<Entity> Party(Simulation sim, int which = 0) => sim.Parties.Send(sim, sim.Defs.AllParties[which]);

    private static bool Said(Simulation sim, Entity who) => sim.Speech.Kept.Any(u => u.Speaker == who);

    [Fact]
    public void EachAdventurersTemperIsTheirOwnAndKeepsAndComesFromTheirKind()
    {
        var sim = Round();
        var all = Enumerable.Range(0, sim.Defs.AllParties.Count).SelectMany(i => Party(sim, i)).ToList();
        Assert.True(all.Count > 8);
        foreach (var e in all)
        {
            var t = Tempers.Of(sim, e);
            Assert.Equal(t, Tempers.Of(sim, e));
            Assert.Contains(sim.World.Get<Adventurer>(e).Kind.TemperWeights, w => w.Temper == t);
        }
        Assert.True(all.Select(e => Tempers.Of(sim, e)).Distinct().Count() >= 3, "a town's worth of customers isn't all one sort");
    }

    [Fact]
    public void OutColdTheyGoDownWhereTheyStand()
    {
        var sim = Round();
        var drunk = Party(sim)[0];
        sim.TickMany(SimClock.TicksPerSecond * 5);
        sim.World.Add(drunk, new Tipsy { Level = DrinkingSystem.OutAt + 0.2f });
        AiSystem.InterruptAll(sim);
        var at = sim.World.Get<Position>(drunk).Cell;
        sim.TickMany(SimClock.TicksPerSecond * 10);
        Assert.IsType<SleepJob>(sim.World.Get<Mind>(drunk).Job);
        Assert.True(sim.World.Get<Mind>(drunk).Asleep);
        Assert.True(Maps.Map.Distance(at, sim.World.Get<Position>(drunk).Cell) <= 1, "asleep where they fell, not off to a bed");
    }

    [Fact]
    public void MadeIllByTheFoodACustomerSaysSoAndThinksTheLessOfTheTown()
    {
        var sim = Round();
        var diner = Party(sim)[0];
        var before = sim.World.Get<Adventurer>(diner).Satisfaction;
        for (var t = 0; t < 400 && !sim.World.Has<FoodPoisoned>(diner); t++)
        {
            FoodPoisoning.AfterEating(sim, diner, 1f);
            sim.Tick();
        }
        Assert.True(sim.World.Has<FoodPoisoned>(diner));
        Assert.True(sim.World.Get<Adventurer>(diner).Satisfaction <= before - FoodPoisoning.PoisonedMood + 0.01f);
        Assert.Contains(sim.Speech.Kept, u => u.Speaker == diner && u.Volume == Speech.Volume.Shout);
    }

    [Fact]
    public void ATimidAdventurerWhoFeelsTheDeadLeavesEarly()
    {
        var sim = Round();
        var all = Enumerable.Range(0, sim.Defs.AllParties.Count).SelectMany(i => Party(sim, i)).ToList();
        var timid = all.FirstOrDefault(e => Tempers.Of(sim, e) == Temper.Timid);
        Assert.False(timid.IsNull, "some adventurer's timid");
        sim.World.Get<Adventurer>(timid).LeaveTick = sim.Clock.Tick + SimClock.TicksPerDay;
        Hauntings.Leave(sim, sim.World.Get<Position>(timid).Cell, HauntKind.Knock);
        Assert.True(sim.World.Get<Adventurer>(timid).LeaveTick <= sim.Clock.Tick + SimClock.HoursToTicks(Hauntings.TimidLeaveHours));
        Assert.True(Said(sim, timid));
    }

    [Fact]
    public void APartySaysHelloWithItsFirstOrderOnly()
    {
        var sim = Round();
        var party = Party(sim);
        sim.TickMany(SimClock.TicksPerHour * 3);
        var orders = sim.Shops.All.Where(o => party.Contains(o.Poster)).ToList();
        Assert.NotEmpty(orders);
        var hellos = sim.Speech.Kept.Count(u => party.Contains(u.Speaker) && sim.Defs.Bark("adv_hello")!.Lines
            .Any(l => Speech.Barks.Fill(l.Text, sim.World.Get<Adventurer>(party[0]).PartyDef.Label) == u.Text));
        Assert.Equal(1, hellos);
    }

    [Fact]
    public void ABarFightIsFistsAndEndsBeforeAnyoneIsBadlyHurt()
    {
        var sim = Round();
        var a = Party(sim, 0)[0];
        var b = Party(sim, 1)[0];
        sim.TickMany(SimClock.TicksPerSecond * 2);
        var at = sim.World.Get<Position>(a).Cell;
        var next = sim.SpiralFrom(at, 2).First(c => c != at && c.Z == at.Z && sim.Map.Passable(c));
        sim.World.Get<Position>(b) = Position.At(next);
        // Some come in off the road with wounds already: only what the fight adds counts.
        var before = (sim.World.Get<Health>(a).Injuries.ToList(), sim.World.Get<Health>(b).Injuries.ToList());
        Brawls.Start(sim, a, b);
        Assert.True(Brawls.InOne(sim, a) && Brawls.InOne(sim, b));
        var swords = (sim.World.Get<Equipment>(a).Weapon, sim.World.Get<Equipment>(b).Weapon);
        var windups = 0;
        for (var t = 0; t < SimClock.TicksPerSecond * 30; t++)
        {
            sim.Tick();
            if (sim.World.Has<Combat.Windup>(a) || sim.World.Has<Combat.Windup>(b)) windups++;
        }
        Assert.True(windups > 0, "nobody swung");
        var taken = sim.World.Get<Health>(a).Injuries.Except(before.Item1).Concat(sim.World.Get<Health>(b).Injuries.Except(before.Item2)).ToList();
        // Armour soaks most of a punch: what does get through is bruising, never a cut.
        Assert.All(taken, i => Assert.Equal(Defs.DamageType.Blunt, i.Type));
        Assert.False(sim.World.Get<Creature>(a).Dead || sim.World.Get<Creature>(b).Dead);
        Assert.False(Brawls.InOne(sim, a) || Brawls.InOne(sim, b), "it's over");
        Assert.Equal(swords, (sim.World.Get<Equipment>(a).Weapon, sim.World.Get<Equipment>(b).Weapon));
    }
}
