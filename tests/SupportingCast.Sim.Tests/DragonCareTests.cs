using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Environment;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;
using static SupportingCast.Sim.Tests.TestWorld;

namespace SupportingCast.Sim.Tests;

/// <summary>The rules of caring for a dragon (Systems/Bonding.cs, docs/plans/dragon-interactions.md).</summary>
public class DragonCareTests
{
    /// <summary>A warm map, a player's character and their dragon beside them, fed and rested.</summary>
    private static (Simulation Sim, Entity Me, Entity Dragon) Pair(float ageDays = 2f, float affinity = 0f)
    {
        var sim = Create(40, 40, 3);
        sim.Climate.BaseTemperature = 28;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 28f);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Arin", C(10, 10), ageDays: 25 * 365);
        sim.World.Add(me, new Avatar());
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Ember", C(11, 10), ageDays: ageDays);
        EggSystem.AssignKeeper(sim, dragon, me);
        sim.World.Get<Dragon>(dragon).Affinity = affinity;
        Array.Fill(sim.World.Get<Needs>(dragon).Values, 0.9f);
        sim.World.Get<Needs>(dragon).Set(sim.Defs.Need("Bond"), 0.2f);
        return (sim, me, dragon);
    }

    private static DragonInteractionDef Def(Simulation sim, string name) => sim.Defs.DragonInteraction(name);

    [Fact]
    public void TheSameCareAgainSoonCountsHalfThenAQuarter()
    {
        var (sim, me, dragon) = Pair();
        var pet = Def(sim, "Pet");
        Assert.Equal(1f, Bonding.Interact(sim, me, dragon, pet).Novelty);
        Assert.Equal(0.5f, Bonding.Interact(sim, me, dragon, pet).Novelty);
        Assert.Equal(0.25f, Bonding.Interact(sim, me, dragon, pet).Novelty);
        Assert.Equal(0.25f, Bonding.Interact(sim, me, dragon, pet).Novelty);
        // Nobody's dragon for a while, so no pet in passing (social care) keeps it stale.
        sim.World.Get<Dragon>(dragon).Keeper = Entity.Null;
        sim.TickMany((int)(pet.NoveltyHours * SimClock.TicksPerHour) + 1);
        Assert.Equal(1f, Bonding.Novelty(sim, dragon, pet));
        // Something else is fresh all along.
        Assert.Equal(1f, Bonding.Novelty(sim, dragon, Def(sim, "Play")));
    }

    [Fact]
    public void APetInPassingIsCareTooAndCountsTowardsNovelty()
    {
        var (sim, me, dragon) = Pair();
        var social = sim.Defs.AllInteractions.First(i => i.Name == "Pet");
        var before = sim.World.Get<Dragon>(dragon).Affinity;
        sim.Social.Interact(sim, me, dragon, social);
        Assert.True(sim.World.Get<Dragon>(dragon).Affinity > before, "a little trust, through the care rules");
        Assert.Contains(sim.DragonCare.Recent, ev => ev.Dragon == dragon && ev.Def.Name == "Pet");
        // A deliberate scratch straight after counts half: it was just petted.
        Assert.Equal(0.5f, Bonding.Novelty(sim, dragon, Def(sim, "Pet")));
        Assert.Single(sim.World.Get<Mood>(dragon).Memories, m => m.Def.Name == "Petted");

        // In passing, it doesn't count as answering what it asked for.
        var care = Bonding.Care(sim, dragon);
        care.Want = DragonWant.Scratch;
        sim.Social.Interact(sim, me, dragon, social);
        Assert.Equal(DragonWant.Scratch, care.Want);
    }

    [Fact]
    public void AnsweringWhatItAskedForCountsHalfAgainAndItRemembers()
    {
        var (sim, me, dragon) = Pair();
        var plain = Bonding.Interact(sim, me, dragon, Def(sim, "Play"));
        var (sim2, me2, dragon2) = Pair();
        Bonding.Care(sim2, dragon2).Want = DragonWant.Play;
        var answered = Bonding.Interact(sim2, me2, dragon2, Def(sim2, "Play"));
        Assert.True(answered.Answered);
        Assert.Equal(plain.Affinity * Bonding.AnsweredBonus, answered.Affinity, 4);
        Assert.Equal(DragonWant.None, Bonding.Peek(sim2, dragon2).Want);
        Assert.Contains(sim2.World.Get<Mood>(dragon2).Memories, m => m.Def.Name == "CameWhenCalled");
        Assert.False(plain.Answered);
    }

    [Fact]
    public void AHatchlingImprintsAndTrustSlowsAsItClimbs()
    {
        var (young, me1, hatchling) = Pair(ageDays: 0.2f);
        var (older, me2, juvenile) = Pair(ageDays: 2f);
        var (trusting, me3, trusted) = Pair(ageDays: 2f, affinity: 0.6f);
        var a = Bonding.Interact(young, me1, hatchling, Def(young, "Pet")).Affinity;
        var b = Bonding.Interact(older, me2, juvenile, Def(older, "Pet")).Affinity;
        var c = Bonding.Interact(trusting, me3, trusted, Def(trusting, "Pet")).Affinity;
        Assert.Equal(2f * b, a, 5);
        Assert.Equal(b * trusting.Defs.TrustLevels.Find(t => t.Name == "Trusting")!.GainFactor, c, 5);
        Assert.Equal("Trusting", Bonding.Level(trusting, trusted)!.Name);
        Assert.Equal("Devoted", Bonding.NextLevel(trusting, 0.6f)!.Name);
    }

    [Fact]
    public void AWantNobodyAnswersCostsTrustAndIsRemembered()
    {
        var (sim, me, dragon) = Pair(affinity: 0.3f);
        var care = Bonding.Care(sim, dragon);
        care.Want = DragonWant.Scratch;
        care.WantSince = sim.Clock.Tick;
        sim.TickMany(Bonding.WantWindowTicks + SimClock.TicksPerHour);
        Assert.Equal(DragonWant.None, care.Want == DragonWant.Scratch ? DragonWant.Scratch : DragonWant.None);
        Assert.Contains(sim.World.Get<Mood>(dragon).Memories, m => m.Def.Name == "IgnoredMe");
        Assert.True(sim.World.Get<Dragon>(dragon).Affinity < 0.3f);
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("gave up waiting"));
    }

    [Fact]
    public void AHatchlingWaitsForAHandOrATroughUnlessItsStarving()
    {
        var (sim, me, dragon) = Pair(ageDays: 0.5f);
        var w = sim.World;
        var food = sim.Defs.Need("Food");
        w.Get<Needs>(dragon).Set(food, 0.4f); // peckish, not starving
        w.Get<Needs>(dragon).Set(sim.Defs.Need("Bond"), 0.9f);
        sim.SpawnItem(sim.Thing("Meat"), 10, C(14, 10));
        var brain = new DragonBrain();
        Assert.IsNotType<EatJob>(brain.Think(sim, dragon));

        var trough = sim.SpawnStructure(sim.Thing("FeedingTrough"), C(12, 12));
        Assert.True(sim.Store(trough, sim.CreateItem(sim.Thing("Meat"), 5)).IsNull);
        Assert.IsType<EatJob>(brain.Think(sim, dragon));
        sim.TickMany(SimClock.TicksPerHour / 2);
        Assert.True(sim.StoredIn(trough).Sum(e => w.Get<Stack>(e).Count) < 5, "it ate from the trough");
        Assert.True(w.Get<Needs>(dragon).Get(food) > 0.4f);

        // Starving, it eats what it can find.
        var (sim2, _, dragon2) = Pair(ageDays: 0.5f);
        sim2.World.Get<Needs>(dragon2).Set(food, 0.1f);
        sim2.SpawnItem(sim2.Thing("Meat"), 10, C(14, 10));
        Assert.IsType<EatJob>(new DragonBrain().Think(sim2, dragon2));
    }

    [Fact]
    public void FHandFeedsWhatYouHoldAndItsFavouriteCountsDouble()
    {
        var (sim, me, dragon) = Pair();
        var w = sim.World;
        var favourite = Bonding.Favourite(sim, dragon)!;
        var other = sim.Thing(new[] { "Meat", "RawFish", "DragonCake" }.First(n => n != favourite.Name));
        w.Get<Needs>(dragon).Set(sim.Defs.Need("Food"), 0.3f);
        w.Get<Carrying>(me).Item = sim.CreateItem(other, 3);
        var action = AvatarSystem.ActionAt(sim, me, C(11, 10));
        Assert.Equal(AvatarActionKind.FeedDragon, action.Kind);
        var before = w.Get<Dragon>(dragon).Affinity;
        AvatarSystem.Perform(sim, me, action);
        var plain = w.Get<Dragon>(dragon).Affinity - before;
        Assert.True(plain > 0);
        Assert.True(w.Get<Needs>(dragon).Get(sim.Defs.Need("Food")) > 0.3f);
        Assert.Contains(w.Get<Mood>(dragon).Memories, m => m.Def.Name == "FedByKeeper");

        var (sim2, me2, dragon2) = Pair();
        sim2.World.Get<Needs>(dragon2).Set(sim2.Defs.Need("Food"), 0.3f);
        sim2.World.Get<Carrying>(me2).Item = sim2.CreateItem(favourite, 3);
        var before2 = sim2.World.Get<Dragon>(dragon2).Affinity;
        AvatarSystem.Perform(sim2, me2, AvatarSystem.ActionAt(sim2, me2, C(11, 10)));
        Assert.Equal(2f * plain, sim2.World.Get<Dragon>(dragon2).Affinity - before2, 5);
        Assert.Contains(sim2.World.Get<Mood>(dragon2).Memories, m => m.Def.Name == "AteFavourite");
    }

    [Fact]
    public void FAnswersItsWantAndPlayIsWorkedWhileHeld()
    {
        var (sim, me, dragon) = Pair();
        var w = sim.World;
        Bonding.Care(sim, dragon).Want = DragonWant.Play;
        Bonding.Care(sim, dragon).WantSince = sim.Clock.Tick;
        var action = AvatarSystem.ActionAt(sim, me, C(11, 10));
        Assert.Equal(AvatarActionKind.AnswerDragon, action.Kind);
        Assert.Equal("Play with Ember (it wants to play)", action.Label);
        sim.ApplyNow(new AvatarWorkCommand(me, true, C(11, 10)));
        Assert.IsType<DragonCareJob>(w.Get<Mind>(me).Job);
        sim.TickMany(Def(sim, "Play").Ticks + 120);
        Assert.Equal(DragonWant.None, Bonding.Peek(sim, dragon).Want);
        Assert.Contains(sim.DragonCare.Recent, ev => ev.Def.Name == "Play" && ev.Answered);

        // With nothing asked and nothing in hand, F opens the care window (back side by side: it wanders off after play).
        w.Get<Position>(me) = Position.At(C(10, 10));
        w.Get<Position>(dragon) = Position.At(C(11, 10));
        Bonding.Care(sim, dragon).Want = DragonWant.None;
        Assert.Equal(AvatarActionKind.TendDragon, AvatarSystem.ActionAt(sim, me, sim.LocationOf(dragon)).Kind);
    }

    [Fact]
    public void LessonsTeachATrickAndComeMakesItAlwaysCome()
    {
        var (sim, me, dragon) = Pair(ageDays: 6f, affinity: 0.3f);
        var come = sim.Defs.Trick("Come");
        var train = Def(sim, "Train");
        for (var i = 0; i < come.Sessions; i++)
        {
            Bonding.Interact(sim, me, dragon, train, trick: come);
            Bonding.Care(sim, dragon).Last.Remove(train.Name); // lessons on different days
        }
        Assert.True(Bonding.Peek(sim, dragon).Knows(come));
        Assert.True(Bonding.Knows(sim, dragon, TrickEffect.Come));
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("has learned \"come\""));

        // It comes from across the map, every time.
        sim.World.Get<Position>(dragon) = Position.At(C(38, 38));
        sim.World.Get<Dragon>(dragon).Affinity = 0f; // knowing it is enough
        Assert.Null(Bonding.CantCall(sim, me, dragon));
        Assert.True(Bonding.Call(sim, me, dragon));
        Assert.IsType<ComeToJob>(sim.World.Get<Mind>(dragon).Job);

        // Crammed lessons count for less.
        var (sim2, me2, dragon2) = Pair(ageDays: 6f);
        Bonding.Interact(sim2, me2, dragon2, Def(sim2, "Train"), trick: come);
        Bonding.Interact(sim2, me2, dragon2, Def(sim2, "Train"), trick: come);
        Assert.Equal(1.5f, Bonding.Peek(sim2, dragon2).Training["Come"], 3);
    }

    [Fact]
    public void AnUntrustingDragonWontComeAndTricksAddObedience()
    {
        var (sim, me, dragon) = Pair(ageDays: 6f, affinity: 0.1f);
        Assert.NotNull(Bonding.CantCall(sim, me, dragon));
        var before = DragonSystem.Obedience(sim, dragon, WorkType.Burn);
        Bonding.Care(sim, dragon).Training["FireOnCommand"] = 99;
        Assert.Equal(before + sim.Defs.Trick("FireOnCommand").Obedience, DragonSystem.Obedience(sim, dragon, WorkType.Burn), 4);
        Assert.Equal(before, DragonSystem.Obedience(sim, dragon, WorkType.Haul), 4);
    }

    [Fact]
    public void GroomingAnItchyDragonBringsLooseScalesAndNeedsABrush()
    {
        var (sim, me, dragon) = Pair(ageDays: 6f);
        var groom = Def(sim, "Groom");
        Assert.Contains("scale brush", Bonding.Problem(sim, me, dragon, groom));
        Toolbelt.PutOn(sim, me, sim.CreateItem(sim.Thing("ScaleBrush"), 1));
        Assert.Null(Bonding.Problem(sim, me, dragon, groom));
        Bonding.Care(sim, dragon).ItchyUntil = sim.Clock.Tick + 100;
        Bonding.Interact(sim, me, dragon, groom);
        Assert.Contains(sim.World.Query<Thing>(), e => sim.World.Get<Thing>(e).Def.Name == "DragonScale");
        Assert.Equal(0, Bonding.Peek(sim, dragon).ItchyUntil);
        // A hatchling is too small to groom.
        var (young, me2, hatchling) = Pair(ageDays: 0.5f);
        Assert.Contains("hatchling", Bonding.Problem(young, me2, hatchling, Def(young, "Groom")));
    }

    [Fact]
    public void ASulkingDragonRefusesEverythingUntilItsWonBack()
    {
        var (sim, me, dragon) = Pair(ageDays: 14f, affinity: 0.6f);
        var care = Bonding.Care(sim, dragon);
        care.Sulking = true;
        Assert.Equal(0f, DragonSystem.Obedience(sim, dragon));
        Assert.Contains("sulking", RidingSystem.CantMount(sim, me, dragon));
        Assert.Contains("sulking", Bonding.CantCall(sim, me, dragon));
        Assert.True(MoodSystem.Holds(sim, dragon, sim.Defs.Thought("Sulking")));
        Bonding.Interact(sim, me, dragon, Def(sim, "Play"));
        Assert.False(care.Sulking);
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("has forgiven"));
    }

    [Fact]
    public void SleepingBesideItCountsOnceANight()
    {
        var (sim, me, dragon) = Pair(ageDays: 6f);
        AiSystem.Order(sim, dragon, new SleepJob(Entity.Null));
        AiSystem.Order(sim, me, new SleepJob(Entity.Null));
        sim.TickMany(600);
        Assert.Single(sim.DragonCare.Recent, ev => ev.Def.Kind == CareKind.Rest);
        sim.TickMany(1200);
        Assert.Single(sim.DragonCare.Recent, ev => ev.Def.Kind == CareKind.Rest);
        Assert.Contains(sim.World.Get<Mood>(dragon).Memories, m => m.Def.Name == "SleptBesideKeeper");
    }

    [Fact]
    public void ASoulboundDragonsFireNeverTouchesItsKeeper()
    {
        var (sim, me, dragon) = Pair(ageDays: 14f, affinity: 2f);
        sim.World.Get<Position>(me) = Position.At(C(14, 10));
        var mouth = sim.World.Get<Position>(dragon).Pos + new float3(0, 0, 1f);
        var ignited = 0;
        for (var i = 0; i < 6; i++)
            ignited += Breath.Breathe(sim, mouth, Position.CenterOf(C(18, 10)), new BreathShape(11, 26, 16, 1f, 1.2f), dragon).Ignited;
        Assert.True(ignited > 0, "the grass beyond burns");
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
            Assert.Equal(0, sim.Map.Fire[sim.Map.Idx(C(14 + dx, 10 + dy))]);
    }

    [Fact]
    public void ADevotedDragonBringsItsKeeperAShareOfItsKill()
    {
        var (sim, me, dragon) = Pair(ageDays: 14f, affinity: 1f);
        var w = sim.World;
        var deer = sim.SpawnCreature(sim.Defs.Creature("Deer"), "deer", C(25, 25), ageDays: 400);
        w.Get<Creature>(deer).Dead = true;
        w.Get<Needs>(dragon).Set(sim.Defs.Need("Food"), 0.85f); // nearly full: most of it is left
        DragonHunting.Feed(sim, dragon, deer);
        Assert.False(Bonding.Peek(sim, dragon).BringMeat.IsNull);
        // (Checked before ticking: its own brain picks the share up on its next think.)
        var bring = new DragonBrain().Think(sim, dragon);
        Assert.IsType<BringKillJob>(bring);
        AiSystem.Order(sim, dragon, bring!);
        sim.TickMany(SimClock.TicksPerHour / 2);
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("share of its kill"));
        Assert.Contains(sim.SpiralFrom(C(10, 10), 2), c => w.IsAlive(sim.Map.Item[sim.Map.Idx(c)]) && w.Get<Thing>(sim.Map.Item[sim.Map.Idx(c)]).Def.IsMeat);
    }

    [Fact]
    public void DragonCareSurvivesASave()
    {
        var (sim, me, dragon) = Pair(ageDays: 6f);
        var care = Bonding.Care(sim, dragon);
        Bonding.Interact(sim, me, dragon, Def(sim, "Train"), trick: sim.Defs.Trick("Stay"));
        care.Want = DragonWant.Groom;
        care.WantSince = 77;
        care.NeglectHours = 3.5f;
        care.ItchyUntil = 1234;
        care.StayAt = C(5, 6);
        care.StayUntil = 999;
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var again = loaded.World.Get<DragonCare>(dragon);
        Assert.Equal(DragonWant.Groom, again.Want);
        Assert.Equal(77, again.WantSince);
        Assert.Equal(3.5f, again.NeglectHours);
        Assert.Equal(1234, again.ItchyUntil);
        Assert.Equal(C(5, 6), again.StayAt);
        Assert.Equal(care.Training["Stay"], again.Training["Stay"]);
        Assert.Equal(care.Last["Train"], again.Last["Train"]);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
    }

    [Fact]
    public void ASleepingDragonsBondDoesntFade()
    {
        var (sim, me, dragon) = Pair(ageDays: 6f);
        var w = sim.World;
        var bond = sim.Defs.Need("Bond");
        w.Get<Needs>(dragon).Set(bond, 0.8f);
        w.Get<Needs>(dragon).Set(sim.Defs.Need("Rest"), 0.05f); // tired enough to sleep the two hours through
        AiSystem.Order(sim, dragon, new SleepJob(Entity.Null));
        sim.TickMany(SimClock.TicksPerHour * 2);
        Assert.True(w.Get<Mind>(dragon).Asleep);
        Assert.True(w.Get<Needs>(dragon).Get(bond) >= 0.8f, "no fading in its sleep");

        // Awake (and with nobody about to top it up), it does.
        var sim2 = Create(40, 40, 3);
        var dragon2 = sim2.SpawnCreature(sim2.Defs.Creature("Dragon"), "Cinder", C(20, 20), ageDays: 6f);
        sim2.World.Get<Needs>(dragon2).Set(bond, 0.8f);
        sim2.TickMany(SimClock.TicksPerHour * 2);
        Assert.True(sim2.World.Get<Needs>(dragon2).Get(bond) < 0.8f);
    }
}
