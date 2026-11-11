using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

/// <summary>
/// The player's combat orders as commands, and the fight paced by <see cref="Encounter.Advance"/>
/// (<c>Core/CombatCommands.cs</c>): what co-op fights are built on.
/// </summary>
public class CombatCommandTests
{
    private static Simulation Field(ulong seed = 1)
    {
        var sim = TestWorld.Create(40, 20, seed, 6);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Human(Simulation sim, int3 at, Faction faction = Faction.Village, string? weapon = null)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), faction == Faction.Village ? "Villager" : "Bandit", at, faction, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        sim.SetSkills(e);
        if (weapon != null)
            sim.Equip(e, sim.Thing(weapon));
        return e;
    }

    /// <summary>A connected co-op player's character.</summary>
    private static Entity Player(Simulation sim, int3 at, Guid id, string weapon)
    {
        var e = Human(sim, at, weapon: weapon);
        sim.World.Add(e, new PlayerCharacter { PlayerId = id });
        sim.World.Add(e, new Avatar());
        return e;
    }

    /// <summary>Run the fight's steps until nothing is moving or waiting, as the game would at 60 a second.</summary>
    private static void Play(Simulation sim, int steps = 600)
    {
        for (var i = 0; i < steps && sim.Combat.Active; i++)
            sim.Combat.Advance(sim);
    }

    [Fact]
    public void AMoveOrderWalksTheUnitAndPaysItsTu()
    {
        var sim = Field();
        var archer = Human(sim, C(5, 10), weapon: "Bow");
        Human(sim, C(30, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        var unit = sim.Combat.Unit(archer)!;
        var full = unit.TimeUnits;
        sim.ApplyNow(new CombatMoveCommand(archer, C(8, 10)));
        Assert.True(sim.Combat.Busy(sim));
        Play(sim, 120);
        Assert.Equal(C(8, 10), sim.World.Get<Position>(archer).Cell);
        Assert.True(unit.TimeUnits < full);
        Assert.Equal(Side.Village, sim.Combat.Phase); // a move doesn't end the turn
    }

    [Fact]
    public void OrdersSentWhileAMoveIsUnderWayWaitTheirTurn()
    {
        var sim = Field();
        var a = Human(sim, C(5, 10), weapon: "Bow");
        var b = Human(sim, C(5, 12), weapon: "Bow");
        Human(sim, C(30, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        sim.ApplyNow(new CombatMoveCommand(a, C(9, 10)));
        sim.ApplyNow(new CombatMoveCommand(b, C(9, 12))); // another player's unit, say: not refused as "still moving"
        Assert.Single(sim.Combat.Queued);
        Assert.Empty(sim.Combat.Feedback);
        Play(sim, 300);
        Assert.Equal(C(9, 10), sim.World.Get<Position>(a).Cell);
        Assert.Equal(C(9, 12), sim.World.Get<Position>(b).Cell);
        Assert.Empty(sim.Combat.Queued);
    }

    [Fact]
    public void AnAttackOrderIsResolvedAndReported()
    {
        var sim = Field();
        var archer = Human(sim, C(5, 10), weapon: "Bow");
        var bandit = Human(sim, C(12, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        var bow = CombatRules.Attacks(sim, archer).FindIndex(x => x.Ranged);
        sim.ApplyNow(new CombatAttackCommand(archer, bandit, bow, ShotMode.Aimed));
        Assert.Contains(sim.Combat.Log, l => l.Text.StartsWith("Aimed: "));
        Assert.Contains(sim.Combat.Feedback, f => f.Unit == archer && f.Text.Contains("shoots at"));
        Assert.True(sim.Combat.Unit(archer)!.TimeUnits < TimeUnits.PerTurn(sim, archer));
    }

    [Fact]
    public void BadOrdersAreReportedNotObeyed()
    {
        var sim = Field();
        var archer = Human(sim, C(5, 10), weapon: "Bow");
        var bandit = Human(sim, C(12, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        var full = sim.Combat.Unit(archer)!.TimeUnits;
        sim.ApplyNow(new CombatAttackCommand(archer, bandit, 99, ShotMode.Snap));
        sim.ApplyNow(new CombatMoveCommand(bandit, C(20, 10))); // not the village's to move
        sim.ApplyNow(new CombatMoveCommand(archer, C(39, 10))); // too far for the TU
        Assert.Equal(full, sim.Combat.Unit(archer)!.TimeUnits);
        Assert.Equal(C(12, 10), sim.World.Get<Position>(bandit).Cell);
        Assert.Equal(3, sim.Combat.Feedback.Count);
    }

    [Fact]
    public void EndingTheTurnHandsThePhaseToTheRaiders()
    {
        var sim = Field();
        Human(sim, C(5, 10), weapon: "Bow");
        Human(sim, C(5, 12), weapon: "Axe");
        Human(sim, C(30, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        sim.ApplyNow(new CombatEndTurnCommand(null));
        Assert.All(sim.Combat.Units.Where(u => u.Side == Side.Village), u => Assert.True(u.Acted));
        sim.Combat.Advance(sim);
        Assert.Equal(Side.Raiders, sim.Combat.Phase);
        Play(sim, 6000);
        Assert.True(sim.Combat.Round >= 2 || !sim.Combat.Active, "the raiders' turn played out and the round came back round");
    }

    [Fact]
    public void InCoopThePhasePassesOnlyOnceEveryPlayerIsDone()
    {
        var sim = Field();
        var arin = Guid.NewGuid();
        var bea = Guid.NewGuid();
        var a = Player(sim, C(5, 10), arin, "Bow");
        var b = Player(sim, C(5, 12), bea, "Axe");
        Human(sim, C(30, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        Assert.False(sim.Combat.AiControls(sim.Combat.Unit(a)!));
        Assert.False(sim.Combat.AiControls(sim.Combat.Unit(b)!));

        sim.ApplyNow(new CombatEndTurnCommand(arin));
        Assert.True(sim.Combat.Unit(a)!.Acted);
        Assert.False(sim.Combat.Unit(b)!.Acted); // Bea's turn is Bea's to end
        Play(sim, 120);
        Assert.Equal(Side.Village, sim.Combat.Phase);

        sim.ApplyNow(new CombatEndTurnCommand(bea));
        sim.Combat.Advance(sim);
        Assert.Equal(Side.Raiders, sim.Combat.Phase);
    }

    [Fact]
    public void APlayerWhoLeavesMidFightHandsTheirCharacterToTheAi()
    {
        var sim = Field();
        var arin = Guid.NewGuid();
        var bea = Guid.NewGuid();
        Player(sim, C(5, 10), arin, "Bow");
        var b = Player(sim, C(5, 12), bea, "Axe");
        Human(sim, C(30, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        Players.Leave(sim, bea);
        sim.Combat.Advance(sim);
        Assert.True(sim.Combat.Unit(b)!.Companion);
        // Arin ending their turn is enough now: the AI plays Bea's character's turn, then it passes.
        sim.ApplyNow(new CombatEndTurnCommand(arin));
        Play(sim, 600);
        Assert.True(sim.Combat.Phase == Side.Raiders || sim.Combat.Round > 1 || !sim.Combat.Active);
    }

    [Fact]
    public void AFightPlayedByCommandsIsDeterministic()
    {
        string Run()
        {
            var sim = Field(3);
            var archer = Human(sim, C(5, 10), weapon: "Bow");
            var axeman = Human(sim, C(6, 12), weapon: "Axe");
            Human(sim, C(22, 10), Faction.Raider, "Club");
            Human(sim, C(22, 12), Faction.Raider, "ShortSword");
            sim.Combat.Start(sim);
            for (var round = 0; round < 8 && sim.Combat.Active; round++)
            {
                if (sim.Combat.Phase == Side.Village)
                {
                    var enemy = sim.Combat.Units.FirstOrDefault(u => u.Side == Side.Raiders && sim.World.Get<Creature>(u.Entity).Able);
                    if (enemy != null)
                    {
                        sim.ApplyNow(new CombatMoveCommand(axeman, sim.World.Get<Position>(axeman).Cell + new int3(2, 0, 0)));
                        sim.ApplyNow(new CombatAttackCommand(archer, enemy.Entity, 0, ShotMode.Snap));
                    }
                    sim.ApplyNow(new CombatEndTurnCommand(null));
                }
                Play(sim, 3000);
            }
            return string.Join("\n", sim.Combat.Log.Select(l => l.Text)) + sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
