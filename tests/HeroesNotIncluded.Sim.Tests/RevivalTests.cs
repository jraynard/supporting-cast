using HeroesNotIncluded.Sim.Afterlife;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The empath's Calling back, and the dead answering it (docs/DESIGN.md, "Death").</summary>
public class RevivalTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bea = new("00000000-0000-0000-0000-00000000000b");

    private static readonly string[] CallingRunes = ["Thread", "Ember", "Still", "Flesh"];

    /// <summary>A round where Ana has just died and Bea, an empath, kneels beside her.</summary>
    private static (Simulation Sim, Entity Body, Entity Ghost, Entity Empath) Deathbed()
    {
        var (sim, _) = TestWorld.Millbrook(7, 4);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new JoinLobbyCommand(Bea, "Bea", default));
        sim.ApplyNow(new StartRoundCommand());
        var w = sim.World;
        var body = Players.CharacterOf(sim, Ana);
        var empath = Players.CharacterOf(sim, Bea);
        if (!w.Has<Mana>(empath))
            w.Add(empath, new Mana { Current = 100, Max = 100 });
        else
            w.Get<Mana>(empath) = new Mana { Current = 100, Max = 100 };
        Kill(sim, body);
        Beside(sim, empath, body);
        return (sim, body, Ghosts.Of(sim, Ana), empath);
    }

    private static void Kill(Simulation sim, Entity e)
    {
        ref var c = ref sim.World.Get<Creature>(e);
        NeedsSystem.Kill(sim, e, ref c, "bled to death");
    }

    /// <summary>Put <paramref name="who"/> on an open cell touching <paramref name="target"/>.</summary>
    private static void Beside(Simulation sim, Entity who, Entity target)
    {
        var at = sim.World.Get<Position>(target).Cell;
        foreach (var d in Maps.Map.Neighbors4)
        {
            var c = Maps.Map.Offset(at, d);
            if (sim.Map.InBounds(c) && sim.Map.Passable(c))
            {
                sim.World.Get<Position>(who) = Position.At(c);
                return;
            }
        }
        throw new InvalidOperationException("nowhere to kneel");
    }

    /// <summary>Weave these runes, each on the beat, and let it go (as the healer tests do).</summary>
    private static void Weave(Simulation sim, Entity me, string[] runes, int maxTicks = 60 * 60, bool start = true)
    {
        if (start)
            sim.ApplyNow(new WeaveCommand(me, 0));
        var keys = runes.Select(n => sim.Defs.Rune(n).Index).ToList();
        var predictor = new WorkPredictor();
        for (var t = 0; t < maxTicks && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var rune = -1;
            var press = false;
            if (predictor.Session is CastSession c)
            {
                if (c.Woven.Count >= keys.Count)
                    press = true;
                else if (c.StepsDone == c.NextBeat)
                    rune = keys[c.Woven.Count];
            }
            predictor.Update(sim, me, 1, press, false, 0, x => sim.ApplyNow(x), 0, rune);
        }
        sim.Tick();
    }

    [Fact]
    public void TheCallingIsAMendingEveryEmpathKnows()
    {
        var defs = TestWorld.LoadDefs();
        var woven = Weaving.Match(defs, CallingRunes.Select(defs.Rune).ToList());
        Assert.NotNull(woven);
        Assert.True(woven!.Value.Mending.CallsBack);
        Assert.True(woven.Value.Mending.KnownAtStart);
    }

    [Fact]
    public void CalledBackAndAnsweredAtTheirBodyTheDeadComeBackDownedAndWeak()
    {
        var (sim, body, ghost, empath) = Deathbed();
        var w = sim.World;

        Weave(sim, empath, CallingRunes);

        Assert.True(w.Has<Calling>(body), string.Join("; ", sim.Messages.Messages.Select(m => m.Text)));
        Assert.True(w.Get<Mana>(empath).Current <= 100f - sim.Defs.Mending("CallBack").ManaCost + 1f);
        Assert.Contains(w.Get<Health>(empath).Injuries, i => i.Type == DamageType.Frost && i.Absorbed); // the grave's chill

        sim.ApplyNow(new GhostMoveCommand(ghost, 0, 0, 0, ToBody: true));
        sim.ApplyNow(new AnswerCommand(ghost));

        var c = w.Get<Creature>(body);
        Assert.False(c.Dead);
        Assert.True(c.Downed);
        Assert.False(w.IsAlive(ghost));
        Assert.True(w.Has<Avatar>(body));
        Assert.Equal(body, Players.CharacterOf(sim, Ana));
        Assert.False(w.Has<Death>(body));
        Assert.True(w.Get<Health>(body).Blood >= Revival.BloodPoor);
    }

    [Fact]
    public void TheDeadAreLaidOutInTheirClothesAndComeBackDressed()
    {
        var (sim, _) = TestWorld.Millbrook(7, 2);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new StartRoundCommand());
        var body = Players.CharacterOf(sim, Ana);
        var worn = sim.Worn(body).ToList();
        Assert.NotEmpty(worn);
        Kill(sim, body);
        Assert.Equal(worn, sim.Worn(body).ToList());
        Revival.Revive(sim, body);
        Assert.Equal(worn, sim.Worn(body).ToList());
    }

    [Fact]
    public void AGhostMustBeAtItsBodyToAnswer()
    {
        var (sim, body, ghost, empath) = Deathbed();
        Revival.Call(sim, empath, 1f);
        var at = sim.World.Get<Position>(body).Pos;
        for (var i = 0; i < 4; i++)
            sim.ApplyNow(new GhostMoveCommand(ghost, at.X + 40, at.Y, (int)at.Z));

        sim.ApplyNow(new AnswerCommand(ghost));

        Assert.True(sim.World.Get<Creature>(body).Dead);
        Assert.NotNull(Revival.AnswerProblem(sim, ghost));
    }

    [Fact]
    public void ACallingNobodyAnswersCloses()
    {
        var (sim, body, ghost, empath) = Deathbed();
        Revival.Call(sim, empath, 1f);

        sim.TickMany(Revival.AnswerTicks + SimClock.TicksPerSecond * 2);

        Assert.False(sim.World.Has<Calling>(body));
        Assert.True(sim.World.Get<Creature>(body).Dead);
        sim.ApplyNow(new AnswerCommand(ghost));
        Assert.True(sim.World.Get<Creature>(body).Dead);
    }

    [Fact]
    public void TheLongDeadCantBeCalledBack()
    {
        var (sim, body, _, empath) = Deathbed();
        sim.World.Get<Death>(body).Tick -= SimClock.HoursToTicks(Revival.FreshHours + 1);

        Assert.False(Revival.Call(sim, empath, 1f));
        Assert.False(sim.World.Has<Calling>(body));
    }

    [Fact]
    public void ABodyKeepsLongerOnAShrinePallet()
    {
        var (sim, body, _, _) = Deathbed();
        var w = sim.World;
        var pallet = w.Query<Thing>().First(e => w.Get<Thing>(e) is { Spawned: true, Def.Building.IsBed: true } t
            && Buildings.At(sim, t.Cell) is { IsNull: false } b && w.Get<PresetBuilding>(b).Def.Name == "Shrine");
        w.Get<Death>(body).Tick -= SimClock.HoursToTicks(Revival.FreshHours + 2);
        Assert.False(Revival.Fresh(sim, body));

        w.Get<Position>(body) = Position.At(w.Get<Thing>(pallet).Cell);

        Assert.True(Revival.OnShrinePallet(sim, body));
        Assert.True(Revival.Fresh(sim, body));
    }

    [Fact]
    public void NobodyComesBackTwice()
    {
        var (sim, body, ghost, empath) = Deathbed();
        Revival.Call(sim, empath, 1f);
        sim.ApplyNow(new GhostMoveCommand(ghost, 0, 0, 0, ToBody: true));
        sim.ApplyNow(new AnswerCommand(ghost));
        Assert.False(sim.World.Get<Creature>(body).Dead);

        Kill(sim, body);

        Assert.False(Revival.Fresh(sim, body));
        Assert.False(Revival.Call(sim, empath, 1f));
    }

    [Fact]
    public void ThoseWithNoOneToAnswerComeAsTheyreCalled()
    {
        // A player who's left: they come back by themselves, and find their character waiting.
        var (sim, body, _, empath) = Deathbed();
        Players.Leave(sim, Ana);

        Revival.Call(sim, empath, 1f);

        Assert.False(sim.World.Get<Creature>(body).Dead);
        Assert.False(sim.World.Has<Avatar>(body));
        Assert.True(Players.Join(sim, Ana));
        Assert.True(sim.World.Has<Avatar>(body));
    }

    [Fact]
    public void AVillagersBodyComesBackToo()
    {
        var (sim, _, _, empath) = Deathbed();
        var w = sim.World;
        var villager = sim.Creatures().First(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human } && !w.Has<PlayerCharacter>(e));
        Kill(sim, villager);
        foreach (var e in w.Query<Death>().Where(e => e != villager))
            w.Get<Position>(e) = Position.At(new int3(1, 1, w.Get<Position>(e).Cell.Z)); // out of the way
        Beside(sim, empath, villager);

        Assert.True(Revival.Call(sim, empath, 1f));

        Assert.False(w.Get<Creature>(villager).Dead);
    }

    [Fact]
    public void AnAiEmpathCallsBackABodyLaidOutInTheirShrine()
    {
        var (sim, _) = TestWorld.Millbrook(7, 4);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade("Blacksmith")], []));
        sim.ApplyNow(new StartRoundCommand());
        var w = sim.World;
        var healer = sim.Creatures().Single(e => w.TryGet<Trade>(e, out var t) && t.Def.Name == "Healer");
        Assert.False(w.Has<PlayerCharacter>(healer));
        w.Get<Mana>(healer).Current = w.Get<Mana>(healer).Max;
        var body = Players.CharacterOf(sim, Ana);
        Kill(sim, body);
        var ghost = Ghosts.Of(sim, Ana);
        var pallet = w.Query<Thing>().First(e => w.Get<Thing>(e) is { Spawned: true, Def.Building.IsBed: true } t
            && Buildings.At(sim, t.Cell) is { IsNull: false } b && w.Get<PresetBuilding>(b).Def.Name == "Shrine");
        w.Get<Position>(body) = Position.At(w.Get<Thing>(pallet).Cell);

        for (var t = 0; t < SimClock.TicksPerHour * 3 && !w.Has<Calling>(body); t += 30)
            sim.TickMany(30);

        Assert.True(w.Has<Calling>(body), string.Join("; ", sim.Messages.Messages.TakeLast(15).Select(m => m.Text)));
        sim.ApplyNow(new GhostMoveCommand(ghost, 0, 0, 0, ToBody: true));
        sim.ApplyNow(new AnswerCommand(ghost));
        Assert.False(w.Get<Creature>(body).Dead);
    }

    [Fact]
    public void ACallingAndTheOnceBackSurviveASave()
    {
        var (sim, body, _, empath) = Deathbed();
        Revival.Call(sim, empath, 0.8f);

        var (loaded, _) = SaveGame.Read(SaveGame.Snapshot(sim), sim.Defs);

        Assert.True(loaded.World.Has<Calling>(body));
        Assert.Equal(0.8f, loaded.World.Get<Calling>(body).Quality, 3);
    }

    [Fact]
    public void AnEmpathAtAFreshBodyIsOfferedTheCallingAndItsRunes()
    {
        var (sim, body, ghost, empath) = Deathbed();
        var w = sim.World;
        var at = w.Get<Position>(body).Cell;

        var action = AvatarSystem.ActionAt(sim, empath, at);
        Assert.Equal(AvatarActionKind.CallBack, action.Kind);
        Assert.Contains("Ana", action.Label);

        AvatarSystem.Perform(sim, empath, action);
        var cast = Assert.IsType<CastSession>(w.Get<WorkSession>(empath));
        Assert.Equal("Ana", cast.CallingFor);

        Weave(sim, empath, CallingRunes, start: false);
        Assert.True(w.Has<Calling>(body));
        sim.World.Get<Position>(ghost) = w.Get<Position>(body);
        sim.ApplyNow(new AnswerCommand(ghost));
        Assert.False(w.Get<Creature>(body).Dead);
    }

    [Fact]
    public void ShortOfManaTheEmpathIsToldWhyAndCanCarryTheBodyToTheShrine()
    {
        var (sim, body, _, empath) = Deathbed();
        var w = sim.World;
        w.Get<Mana>(empath).Current = 30;

        var why = Revival.CallProblem(sim, empath, body);
        Assert.NotNull(why);
        Assert.Contains("mana", why);
        var action = AvatarSystem.ActionAt(sim, empath, w.Get<Position>(body).Cell);
        Assert.Equal(AvatarActionKind.Carry, action.Kind); // pick up the body instead
        AvatarSystem.Perform(sim, empath, action);
        Assert.True(w.Has<Carried>(body));
    }

    [Fact]
    public void AnyoneCanPickUpABody()
    {
        var (sim, body, _, _) = Deathbed();
        var w = sim.World;
        var smith = sim.Creatures().First(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human } && w.Get<Creature>(e).Able && !w.Has<Mana>(e));
        Beside(sim, smith, body);
        if (!w.Has<Avatar>(smith))
            w.Add(smith, new Avatar());

        var action = AvatarSystem.ActionAt(sim, smith, w.Get<Position>(body).Cell);

        Assert.Equal(AvatarActionKind.Carry, action.Kind);
        Assert.Contains("body", action.Label);
    }

    [Fact]
    public void ABodyInABedIsReachedFromAnySideOfIt()
    {
        var (sim, body, _, empath) = Deathbed();
        var w = sim.World;
        var bed = w.Query<Thing>().First(e => w.Get<Thing>(e) is { Spawned: true, Def.Building.IsBed: true } && sim.FootprintCells(e).Count() >= 2
            && !w.Has<Construction>(e) && w.Get<Thing>(e).Cell.Z == w.Get<Position>(empath).Cell.Z);
        var cells = sim.FootprintCells(bed).ToList();
        var lies = w.Get<Thing>(bed).Cell;
        w.Get<Position>(body) = Position.At(lies);
        // Beside the bed's far end, not touching the cell the body's in.
        var far = cells.First(c => c != lies);
        var kneel = sim.SpiralFrom(far, 1).FirstOrDefault(c => c.Z == far.Z && !cells.Contains(c) && sim.Map.Passable(c)
            && Maps.Map.Distance(c, lies) > 1);
        if (kneel == default)
            Assert.Fail("no bed with room at its far end");
        w.Get<Position>(empath) = Position.At(kneel);

        Assert.Equal(body, Revival.BodyBeside(sim, empath));
        Assert.Equal(body, Revival.BodyAt(sim, far));
        Assert.Null(Revival.CallProblem(sim, empath, body));
    }

    [Fact]
    public void ADeathTellsTheEmpathHowAndHowLong()
    {
        var (sim, _, _, empath) = Deathbed();
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("call them back") && m.Text.Contains("Ana"));
        Assert.True(Revival.FreshHours >= 12); // eight real minutes, not under three
    }

    [Fact]
    public void TheGhostKnowsHowLongItHasAndWhetherAnyoneCanCallIt()
    {
        var (sim, body, ghost, empath) = Deathbed();
        var mine = Perception.For(sim, ghost).MyBody()!;
        Assert.True(mine.EmpathAlive);
        Assert.InRange(mine.HoursLeft, Revival.FreshHours - 1, Revival.FreshHours);

        foreach (var e in sim.World.Query<Mana>().Where(e => !sim.World.Get<Creature>(e).Dead).ToList())
            Kill(sim, e); // every empath in the town, the AI's own included
        Assert.False(Perception.For(sim, ghost).MyBody()!.EmpathAlive);
    }
}
