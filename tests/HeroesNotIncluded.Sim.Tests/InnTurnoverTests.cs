using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Turning a room round after its guest has gone: linen, the pot, the jug, and what they left.</summary>
public class InnTurnoverTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static (Simulation Sim, List<Entity> Party, Entity Me) Tavern(string playerTrade)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(playerTrade)], []));
        sim.ApplyNow(new StartRoundCommand());
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        return (sim, party, Players.CharacterOf(sim, Ana));
    }

    private static Entity Inn(Simulation sim) => ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));

    private static Entity Room(Simulation sim, int n) => sim.World.Query<GuestRoom>().First(b => InnRooms.Number(sim, b) == n);

    /// <summary>Stands someone in reach of a thing.</summary>
    private static void Beside(Simulation sim, Entity who, Entity thing)
    {
        var at = sim.World.Get<Thing>(thing).Cell;
        var cell = sim.SpiralFrom(at, 2).First(c => c != at && c.Z == at.Z && sim.Map.Passable(c) && AvatarSystem.InReach(sim, c, thing));
        sim.World.Get<Position>(who).Cell = cell;
        sim.World.Get<Position>(who).Pos = Position.CenterOf(cell);
    }

    private static void InHand(Simulation sim, Entity who, string def)
    {
        var held = sim.World.Get<Carrying>(who).Item;
        if (!held.IsNull)
            Inventory.Stow(sim, who, held);
        sim.World.Get<Carrying>(who).Item = sim.CreateItem(sim.Thing(def), 1);
    }

    /// <summary>Plays a press-on-cue session to its end, pressing on every cue (or never, for <paramref name="miss"/> presses off it first).</summary>
    private static void Play(Simulation sim, Entity me, int miss = 0)
    {
        var predictor = new WorkPredictor();
        var was = false;
        for (var t = 0; t < 60 * 30 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var cue = predictor.Session is SlopSession sl && sl.OnCue;
            var (press, held, axis) = RoomWork(predictor.Session, ref was);
            if (miss > 0 && predictor.Session is SlopSession && !cue && t % 7 == 0 && t > 3)
            {
                press = true;
                miss--;
            }
            predictor.Update(sim, me, 1, press, held, axis, c => sim.ApplyNow(c));
        }
        sim.Tick();
    }

    [Fact]
    public void ACheckedOutRoomWantsMakingUpEmptyingAndFilling()
    {
        var (sim, party, _) = Tavern("Innkeeper");
        var bed = Room(sim, 2);
        Assert.True(RoomChores.Ready(sim, bed), "a fresh round's rooms are ready");
        Lodging.Let(sim, party[0], bed);
        Lodging.CheckOut(sim, party[0]);
        Assert.True(Lodging.IsUnmade(sim, bed));
        Assert.True(sim.World.Has<Slops>(RoomChores.FixtureOf(sim, bed, RoomChores.PotDef)));
        Assert.True(sim.World.Has<JugEmpty>(RoomChores.FixtureOf(sim, bed, RoomChores.StandDef)));
        Assert.False(RoomChores.Ready(sim, bed));
        Assert.NotEqual(bed, ShopSystem.FreeLetBed(sim, Inn(sim))); // a ready room's let first
    }

    [Fact]
    public void ThePotGoesIntoABucketAndTheBucketToTheMidden()
    {
        var (sim, _, me) = Tavern("Innkeeper");
        var bed = Room(sim, 1);
        var pot = RoomChores.FixtureOf(sim, bed, RoomChores.PotDef);
        sim.World.Add(pot, new Slops());
        Beside(sim, me, pot);
        Assert.StartsWith("The pot wants emptying", AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(pot).Cell).Label);
        InHand(sim, me, Environment.Buckets.Empty);
        sim.ApplyNow(new BarWorkCommand(me, pot));
        Assert.IsType<SlopSession>(sim.World.Get<WorkSession>(me));
        Play(sim, me, miss: 1);
        Assert.False(sim.World.Has<Slops>(pot));
        Assert.False(RoomChores.SlopsOn(sim, me).IsNull, "a bucket of slops");
        Assert.NotNull(sim.Mess.At(sim, sim.World.Get<Thing>(pot).Cell)); // the one off the beat went on the floor

        var midden = sim.World.Query<Thing>().First(e => sim.World.Get<Thing>(e).Def.Name == RoomChores.MiddenDef);
        Beside(sim, me, midden);
        sim.ApplyNow(new BarWorkCommand(me, midden));
        Assert.True(RoomChores.SlopsOn(sim, me).IsNull);
        Assert.False(RoomChores.EmptyBucketOn(sim, me).IsNull, "the bucket's empty again");
    }

    [Fact]
    public void TheJugIsFilledFromABucketOfWater()
    {
        var (sim, _, me) = Tavern("Innkeeper");
        var bed = Room(sim, 3);
        var stand = RoomChores.FixtureOf(sim, bed, RoomChores.StandDef);
        sim.World.Add(stand, new JugEmpty());
        Beside(sim, me, stand);
        sim.ApplyNow(new BarWorkCommand(me, stand));
        Assert.True(sim.World.Has<JugEmpty>(stand), "not without water");
        InHand(sim, me, Environment.Buckets.Full);
        sim.ApplyNow(new BarWorkCommand(me, stand));
        Assert.False(sim.World.Has<JugEmpty>(stand));
        Assert.False(RoomChores.EmptyBucketOn(sim, me).IsNull);
    }

    [Fact]
    public void SomethingLeftInTheBedTurnsUpWhenItsStrippedAndIsHandedBack()
    {
        var (sim, party, me) = Tavern("Innkeeper");
        var guest = party[0];
        var bed = Room(sim, 4);
        var whetstone = sim.CreateItem(sim.Thing("Herbs"), 1); // anything of theirs will do
        sim.World.Add(whetstone, new LeftBy { Owner = guest, Name = sim.World.Get<Creature>(guest).Name });
        sim.World.Add(bed, new Tucked { Item = whetstone });
        sim.World.Add(bed, new Unmade());
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing(RoomChores.LinenDef), 1));
        MakeBedSession.MakeUp(sim, bed, me);
        Assert.False(sim.World.Has<Tucked>(bed));
        Assert.True(Inventory.OnPerson(sim, me, whetstone), "found in the sheets");
        Assert.StartsWith(sim.World.Get<Creature>(guest).Name + "'s", RoomChores.Label(sim, whetstone));

        var pleased = sim.World.Get<Adventurer>(guest).Satisfaction;
        sim.World.Get<Position>(me).Cell = sim.World.Get<Position>(guest).Cell + new int3(1, 0, 0);
        sim.ApplyNow(new GiveCommand(me, guest, whetstone, 1));
        Assert.True(Inventory.OnPerson(sim, guest, whetstone));
        Assert.True(sim.World.Get<Adventurer>(guest).Satisfaction > pleased);
        Assert.False(sim.World.Has<LeftBy>(whetstone));
    }

    [Fact]
    public void AnAiKeeperTurnsTheRoomRoundAndTheSheetsGoToTheWash()
    {
        var (sim, party, _) = Tavern("Healer");
        var guest = party[0];
        var bed = Room(sim, 5);
        Lodging.Let(sim, guest, bed);
        var stuff = sim.CreateItem(sim.Thing("Herbs"), 1);
        Lodging.CheckOut(sim, guest);
        sim.World.Add(stuff, new LeftBy { Owner = guest, Name = "Someone" });
        sim.PlaceItemNear(stuff, sim.World.Get<Thing>(bed).Cell);
        for (var i = 0; i < 64 && !RoomChores.Ready(sim, bed); i++)
            sim.TickMany(SimClock.TicksPerHour / 8);
        Assert.True(RoomChores.Ready(sim, bed), "made up, emptied and filled");
        var w = sim.World;
        var lost = w.Query<Thing>().First(e => w.Get<Thing>(e).Def.Name == RoomChores.LostPropertyDef);
        for (var i = 0; i < 32 && !sim.StoredIn(lost).Contains(stuff); i++)
            sim.TickMany(SimClock.TicksPerHour / 8);
        Assert.Contains(stuff, sim.StoredIn(lost));
        for (var i = 0; i < 64 && w.Query<Thing>().Any(e => w.Get<Thing>(e).Def.Name == RoomChores.SoiledDef); i++)
            sim.TickMany(SimClock.TicksPerHour / 8);
        Assert.DoesNotContain(w.Query<Thing>(), e => w.Get<Thing>(e).Def.Name == RoomChores.SoiledDef);
    }

    [Fact]
    public void FAtAnEmptyJugWithABucketOfWaterFillsItRatherThanThrowing()
    {
        var (sim, _, me) = Tavern("Innkeeper");
        var stand = RoomChores.FixtureOf(sim, Room(sim, 3), RoomChores.StandDef);
        sim.World.Add(stand, new JugEmpty());
        Beside(sim, me, stand);
        InHand(sim, me, Environment.Buckets.Full);
        var cell = sim.World.Get<Thing>(stand).Cell;
        var action = AvatarSystem.ActionAt(sim, me, cell);
        Assert.Equal(AvatarActionKind.BarWork, action.Kind);
        Assert.Equal("Fill the jug", action.Label);
        sim.ApplyNow(new AvatarWorkCommand(me, true, cell));
        Assert.False(sim.World.Has<JugEmpty>(stand));
        // Filled, the jug wants no more: the bucket's for swinging and throwing again.
        InHand(sim, me, Environment.Buckets.Full);
        Assert.NotEqual(AvatarActionKind.BarWork, AvatarSystem.ActionAt(sim, me, cell).Kind);
        Assert.True(Environment.Buckets.Prime(sim, me, true, sim.Clock.Tick));
    }

    [Fact]
    public void ABedIsStrippedHeldThenSpreadOnTheBillowThenTuckedSideBySide()
    {
        var (sim, _, me) = Tavern("Innkeeper");
        var bed = Room(sim, 1);
        sim.World.Add(bed, new Unmade());
        Beside(sim, me, bed);
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing(RoomChores.LinenDef), 1));
        var s = new MakeBedSession { Bed = bed };
        WorkSystem.Start(sim, me, s);
        var step = 0;
        void Run(int steps, params WorkInput[] inputs)
        {
            s.Advance(inputs.Select(i => i with { Step = step + i.Step }).ToList(), step + steps);
            step += steps;
        }
        // Not held: nothing comes off. Held long enough: stripped.
        Run(30);
        Assert.Equal(BedStep.Strip, s.At);
        Run(80, new WorkInput(0, WorkInputKind.Hold, 1));
        Assert.Equal(BedStep.Spread, s.At);
        // Pressed low in the billow, it flops and lies a moment; at the top, it's laid.
        while (s.Billow > 0.3f || s.Billow == 0f) Run(1);
        Run(1, new WorkInput(0, WorkInputKind.Press));
        Assert.True(s.Flopped);
        Assert.Equal(BedStep.Spread, s.At);
        Run(MakeBedSession.FlopSteps);
        while (!s.OnCue) Run(1);
        Run(1, new WorkInput(0, WorkInputKind.Press));
        Assert.Equal(BedStep.Tuck, s.At);
        // The wrong side does nothing; A then D, twice over, tucks all four.
        Run(2, new WorkInput(0, WorkInputKind.Axis, 1), new WorkInput(1, WorkInputKind.Axis, 0));
        Assert.Equal(0, s.Tucked);
        foreach (var side in new[] { -1, 1, -1, 1 })
            Run(2, new WorkInput(0, WorkInputKind.Axis, side), new WorkInput(1, WorkInputKind.Axis, 0));
        Assert.True(s.Done);
    }

    [Fact]
    public void SigningInIsOnePressAndAMomentsWriting()
    {
        var s = new SignInSession { Room = 2 };
        s.Advance([], 120);
        Assert.False(s.Begun);
        Assert.False(s.Done);
        s.Advance([new WorkInput(120, WorkInputKind.Press)], 121);
        Assert.True(s.Begun);
        s.Advance([], 121 + (int)(SignInSession.WriteSeconds * SimClock.TicksPerSecond) + 2);
        Assert.True(s.Done);
    }

    [Fact]
    public void TurnoverSurvivesASave()
    {
        var (sim, party, me) = Tavern("Innkeeper");
        var bed = Room(sim, 6);
        var pot = RoomChores.FixtureOf(sim, bed, RoomChores.PotDef);
        var stand = RoomChores.FixtureOf(sim, bed, RoomChores.StandDef);
        sim.World.Add(pot, new Slops());
        sim.World.Add(stand, new JugEmpty());
        var thing = sim.CreateItem(sim.Thing("Herbs"), 1);
        sim.World.Add(thing, new LeftBy { Owner = party[0], Name = "Bryn" });
        sim.World.Add(bed, new Tucked { Item = thing });
        Beside(sim, me, pot);
        InHand(sim, me, Environment.Buckets.Empty);
        sim.ApplyNow(new BarWorkCommand(me, pot));
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.True(loaded.World.Has<Slops>(pot));
        Assert.True(loaded.World.Has<JugEmpty>(stand));
        Assert.Equal(thing, loaded.World.Get<Tucked>(bed).Item);
        Assert.Equal("Bryn", loaded.World.Get<LeftBy>(thing).Name);
        Assert.IsType<SlopSession>(loaded.World.Get<WorkSession>(me));
    }
}
