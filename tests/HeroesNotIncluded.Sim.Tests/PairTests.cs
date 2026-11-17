using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Two pairs of hands (Work.Pairs): a great blade is striker's work, and bad help is worse than none.</summary>
public class PairTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>A smithy with a greatsword begun, and a second player standing at the anvil's other side.</summary>
    private static (Simulation Sim, Entity Me, Entity Striker, SmithyKit Kit) Smithy(bool striker = true)
    {
        var sim = Create(40, 40);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Smith", C(10, 10), ageDays: 25 * 365);
        sim.Round.OpenLobby();
        sim.Round.Sit(Ana, "Smith", null, null);
        sim.Round.Start(sim);
        var kit = SmithyKit.Place(sim);
        var r = sim.Defs.Recipe("ForgeGreatsword");
        Assert.True(r.TwoHanded);
        sim.World.Get<Workbench>(kit.Forge).Begin(r, me);
        Smithing.BeginMaking(sim, me, kit.Forge, r);
        var other = Entity.Null;
        if (striker)
        {
            other = sim.SpawnCreature(sim.Defs.Creature("Human"), "Striker", C(8, 10), ageDays: 25 * 365);
            sim.World.Add(other, new Avatar());
        }
        return (sim, me, other, kit);
    }

    /// <summary>
    /// The striker's hands, each tick: take up the sledge with the work key when the smith's at the
    /// anvil alone, then heave up and come down on the call (or, careless, whenever it's up).
    /// </summary>
    private static Action Strike(Simulation sim, Entity striker, Entity anvil, WorkPredictor predictor, bool onTheCall = true) => () =>
    {
        var w = sim.World;
        if (!w.Has<WorkSession>(striker))
        {
            if (AvatarSystem.ActionAt(sim, striker, w.Get<Thing>(anvil).Cell, AvatarTool.Use).Kind == AvatarActionKind.LendAHand)
                sim.ApplyNow(new AvatarWorkCommand(striker, true, w.Get<Thing>(anvil).Cell));
            return;
        }
        if (predictor.Session?.StartTick != w.Get<WorkSession>(striker).StartTick)
        {
            predictor.Update(sim, striker, 1, false, false, 0, c => sim.ApplyNow(c));
            return;
        }
        var held = predictor.Session is SledgeSession s && (onTheCall ? !(s.CallOpen && s.Raise >= SledgeSession.FullHeave) : s.Raise < SledgeSession.FullHeave);
        predictor.Update(sim, striker, 1, false, held, 0, c => sim.ApplyNow(c));
        // The striker's copy follows every beat the smith sends (and the smith's follows the striker's).
        Assert.Equal(w.Get<WorkSession>(striker).Revision, predictor.Session!.Revision);
    };

    private const int Budget = 60 * 60 * 6;

    [Fact]
    public void ASmithAndAStrikerForgeAGreatswordTogether()
    {
        var (sim, me, striker, kit) = Smithy();
        var before = SkillRules.LevelOf(sim, striker, Skill.Crafting);
        var xpBefore = sim.World.Get<Skills>(striker).Xp[(int)Skill.Crafting];
        kit.Drive(sim, me, maxTicks: Budget, alongside: Strike(sim, striker, kit.Anvil, new WorkPredictor()));

        Assert.Null(Smithing.PieceOf(sim, me));
        var blade = sim.World.Query<Crafted>().Single(e => sim.World.Get<Thing>(e).Def.Name == "Greatsword");
        var made = sim.World.Get<Crafted>(blade);
        Assert.Equal(me, made.Maker);
        Assert.Equal(striker, made.Helper); // the striker's named on it
        Assert.True(made.Quality >= Quality.Plain, $"{made.Quality}");
        Assert.False(sim.World.Has<WorkSession>(striker)); // the sledge put down once the work was done
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("no more need of you"));
        Assert.True(SkillRules.LevelOf(sim, striker, Skill.Crafting) > before || sim.World.Get<Skills>(striker).Xp[(int)Skill.Crafting] > xpBefore,
            "the striker learns a little of the smith's trade");
    }

    [Fact]
    public void AloneAGreatPieceBarelyMoves()
    {
        // As long as a smith and a striker take over it, three times over: alone, it's still not done.
        var (pair, smith, striker, pairKit) = Smithy();
        var from = pair.Clock.Tick;
        pairKit.Drive(pair, smith, maxTicks: Budget, alongside: Strike(pair, striker, pairKit.Anvil, new WorkPredictor()));
        Assert.Null(Smithing.PieceOf(pair, smith));
        var together = (int)(pair.Clock.Tick - from);

        var (sim, me, _, kit) = Smithy(striker: false);
        kit.Drive(sim, me, maxTicks: together * 3);
        var piece = Smithing.PieceOf(sim, me);
        Assert.True(piece != null, string.Join("\n", sim.Messages.Messages.TakeLast(15).Select(m => m.Text)));
        Assert.True(piece.Shape < 0.6f, $"shaped {piece.Shape:F2} alone in {together * 3} ticks; together it took {together}");
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("striker's work"));
    }

    [Fact]
    public void ASledgeBroughtDownOffTheBeatDoesHarm()
    {
        var (sim, me, striker, kit) = Smithy();
        kit.Drive(sim, me, maxTicks: Budget, alongside: Strike(sim, striker, kit.Anvil, new WorkPredictor(), onTheCall: false));
        var flaw = Smithing.PieceOf(sim, me)?.Flaw
            ?? (sim.World.Query<Flawed>().Select(e => (PieceFlaw?)sim.World.Get<Flawed>(e).Flaw).FirstOrDefault() ?? PieceFlaw.None);
        Assert.Equal(PieceFlaw.Crack, flaw);
    }

    [Fact]
    public void AVillagerAnswersACallForAHand()
    {
        var (sim, me, _, kit) = Smithy(striker: false);
        // A neighbour within earshot, getting on with their day.
        var neighbour = sim.SpawnCreature(sim.Defs.Creature("Human"), "Wyn", C(14, 12), ageDays: 30 * 365);
        var asked = false;
        void Ask()
        {
            if (!asked && sim.World.TryGet<WorkSession>(me, out var s) && s is HammerSession { Heavy: true })
            {
                asked = true;
                sim.ApplyNow(new AskForHandCommand(me));
                Assert.IsType<HelpJob>(sim.World.Get<Mind>(neighbour).Job);
            }
        }
        kit.Drive(sim, me, maxTicks: Budget, alongside: Ask);

        Assert.True(asked);
        Assert.Null(Smithing.PieceOf(sim, me));
        var blade = sim.World.Query<Crafted>().Single(e => sim.World.Get<Thing>(e).Def.Name == "Greatsword");
        Assert.Equal(neighbour, sim.World.Get<Crafted>(blade).Helper);
        Assert.False(sim.World.Has<Helping>(neighbour)); // back to their day
    }

    [Fact]
    public void APairAtTheAnvilSavesAndLoads()
    {
        var (sim, me, striker, kit) = Smithy();
        var predictor = new WorkPredictor();
        var strike = Strike(sim, striker, kit.Anvil, predictor);
        // Until both are at the anvil, a few blows in.
        try
        {
            kit.Drive(sim, me, maxTicks: 60 * 60, alongside: () =>
            {
                strike();
                if (sim.World.TryGet<WorkSession>(me, out var s) && s is HammerSession { StrikerBlows: >= 2 })
                    throw new StopDriving();
            });
        }
        catch (StopDriving)
        {
        }
        var lead = Assert.IsType<HammerSession>(sim.World.Get<WorkSession>(me));
        Assert.Equal(striker, lead.Partner);
        var loaded = SaveLoadTests.RoundTrip(sim);
        var lead2 = Assert.IsType<HammerSession>(loaded.World.Get<WorkSession>(me));
        var sledge = Assert.IsType<SledgeSession>(loaded.World.Get<WorkSession>(striker));
        Assert.Equal(striker, lead2.Partner);
        Assert.Equal(me, sledge.Partner);
        Assert.Equal(lead.StrikerBlows, lead2.StrikerBlows);
        Assert.Equal(lead.Revision, lead2.Revision);
    }

    [Fact]
    public void NoVillagerTakesUpAGreatPieceAlone()
    {
        var (sim, _, _, kit) = Smithy(striker: false);
        var smith = sim.SpawnCreature(sim.Defs.Creature("Human"), "Ewan", C(12, 12), ageDays: 30 * 365);
        sim.World.Get<Skills>(smith).Level[(int)Skill.Crafting] = 12;
        var bench = sim.World.Get<Workbench>(kit.Forge);
        bench.Clear();
        bench.Bills.Add(new Bill { Recipe = sim.Defs.Recipe("ForgeGreatsword"), Mode = BillMode.Forever });
        Assert.Null(Crafting.NextForVillager(sim, kit.Forge, smith, WorkType.Craft));
    }
}

/// <summary>Thrown from a test's per-tick hook to stop <see cref="SmithyKit.Drive"/> where it stands.</summary>
internal sealed class StopDriving : Exception;
