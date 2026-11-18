using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Players handing things over and trading face to face (<see cref="BarterSystem"/>).</summary>
public class BarterTests
{
    private static Entity Player(Simulation sim, string name, int3 at)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), name, at, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        sim.World.Add(e, new Avatar());
        sim.Wear(e, sim.Thing("Satchel"));
        return e;
    }

    private static (Simulation Sim, Entity Arin, Entity Bea) Two()
    {
        var sim = Create(30, 20);
        return (sim, Player(sim, "Arin", C(10, 10)), Player(sim, "Bea", C(11, 10)));
    }

    private static Entity Give(Simulation sim, Entity to, string thing, int count = 1, string? material = null)
    {
        var item = sim.CreateItem(sim.Thing(thing), count, material == null ? null : sim.Defs.Material(material));
        Inventory.Stow(sim, to, item);
        return Inventory.CarriedOf(sim, to, sim.Thing(thing)).First();
    }

    private static int Has(Simulation sim, Entity who, string thing) =>
        Inventory.CarriedOf(sim, who, sim.Thing(thing)).Sum(s => sim.World.Get<Stack>(s).Count);

    [Fact]
    public void BothAgreeAndItAllChangesHandsAtOnce()
    {
        var (sim, arin, bea) = Two();
        var wood = Give(sim, arin, "Wood", 10, "Birch");
        Give(sim, bea, Coin.Silver, 12);

        sim.ApplyNow(new BarterCommand(arin, bea));
        Assert.False(sim.Barter.Of(bea)!.Open, "asked, not yet answered");
        sim.ApplyNow(new BarterOfferCommand(arin, wood, 4));
        Assert.Empty(sim.Barter.Of(arin)!.Asker.Items); // nothing goes on the table till they say yes
        sim.ApplyNow(new BarterCommand(bea, arin)); // yes
        Assert.True(sim.Barter.Of(bea)!.Open);

        sim.ApplyNow(new BarterOfferCommand(arin, wood, 4));
        sim.ApplyNow(new BarterSilverCommand(bea, 5));
        var seen = new PerceptionView(sim, bea).Barter()!;
        Assert.Equal(4, seen.Theirs.Single().Count);
        Assert.Equal(5, seen.MySilver);

        sim.ApplyNow(new BarterAgreeCommand(arin, true));
        Assert.Equal(10, Has(sim, arin, "Wood")); // one agreement isn't a trade
        sim.ApplyNow(new BarterAgreeCommand(bea, true));

        Assert.Null(sim.Barter.Of(arin));
        Assert.Equal(6, Has(sim, arin, "Wood"));
        Assert.Equal(4, Has(sim, bea, "Wood"));
        Assert.Equal(5, Coin.Count(sim, arin));
        Assert.Equal(7, Coin.Count(sim, bea));
    }

    [Fact]
    public void ChangingTheTableUndoesBothAgreements()
    {
        var (sim, arin, bea) = Two();
        var wood = Give(sim, arin, "Wood", 10, "Birch");
        Give(sim, bea, Coin.Silver, 12);
        sim.ApplyNow(new BarterCommand(arin, bea));
        sim.ApplyNow(new BarterCommand(bea, arin));
        sim.ApplyNow(new BarterOfferCommand(arin, wood, 4));
        sim.ApplyNow(new BarterSilverCommand(bea, 5));
        sim.ApplyNow(new BarterAgreeCommand(bea, true));

        // Arin quietly takes three back before agreeing: Bea's yes was to four.
        sim.ApplyNow(new BarterOfferCommand(arin, wood, 1));
        Assert.False(sim.Barter.Of(bea)!.Asked.Agreed);
        sim.ApplyNow(new BarterAgreeCommand(arin, true));
        Assert.Equal(10, Has(sim, arin, "Wood"));

        // Something put out, then spent elsewhere, comes off the table too.
        sim.ApplyNow(new BarterAgreeCommand(bea, true));
        Assert.Null(sim.Barter.Of(bea)); // that one went through: one wood for five
        Assert.Equal(9, Has(sim, arin, "Wood"));
    }

    [Fact]
    public void WalkingAwayCallsItOffAndAnUnansweredAskLapses()
    {
        var (sim, arin, bea) = Two();
        sim.ApplyNow(new BarterCommand(arin, bea));
        sim.ApplyNow(new BarterCommand(bea, arin));
        sim.World.Get<Position>(bea) = Position.At(C(20, 10));
        sim.TickMany(30);
        Assert.Null(sim.Barter.Of(arin));

        sim.World.Get<Position>(bea) = Position.At(C(11, 10));
        sim.ApplyNow(new BarterCommand(arin, bea));
        sim.TickMany((int)BarterSystem.AskTicks + 30);
        Assert.Null(sim.Barter.Of(arin));
    }

    [Fact]
    public void FacingAnotherPlayerHandsOverWhatYouHoldOrAsksToTrade()
    {
        var (sim, arin, bea) = Two();
        var at = sim.World.Get<Position>(bea).Cell;
        var ask = AvatarSystem.ActionAt(sim, arin, at);
        Assert.Equal(AvatarActionKind.Barter, ask.Kind);
        Assert.Contains("Bea", ask.Label);

        var held = sim.CreateItem(sim.Thing("Wood"), 3, sim.Defs.Material("Birch"));
        sim.World.Get<Carrying>(arin).Item = held;
        var hand = AvatarSystem.ActionAt(sim, arin, at);
        Assert.Equal(AvatarActionKind.HandOver, hand.Kind);
        sim.ApplyNow(new AvatarWorkCommand(arin, true, at));
        Assert.True(sim.World.Get<Carrying>(arin).Item.IsNull);
        Assert.Equal(3, Has(sim, bea, "Wood"));
    }

    [Fact]
    public void AnOpenTableIsSaved()
    {
        var (sim, arin, bea) = Two();
        var wood = Give(sim, arin, "Wood", 10, "Birch");
        sim.ApplyNow(new BarterCommand(arin, bea));
        sim.ApplyNow(new BarterCommand(bea, arin));
        sim.ApplyNow(new BarterOfferCommand(arin, wood, 4));
        sim.ApplyNow(new BarterAgreeCommand(arin, true));

        var loaded = SaveLoadTests.RoundTrip(sim);
        var deal = loaded.Barter.Of(arin)!;
        Assert.True(deal.Open);
        Assert.True(deal.Asker.Agreed);
        Assert.Equal(4, deal.Asker.Items.Single().Count);
        Assert.Equal(sim.FullHash(), loaded.FullHash());
    }
}
