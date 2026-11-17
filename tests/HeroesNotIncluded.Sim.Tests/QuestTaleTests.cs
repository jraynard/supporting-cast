using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Expeditions;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>"Behind every hero": a quest's tale credits the town's hands that went with the party.</summary>
public class QuestTaleTests
{
    private static Entity Villager(Simulation sim, string trade) =>
        sim.Creatures().First(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == trade);

    private static string Name(Simulation sim, Entity e) => sim.World.Get<Creature>(e).Name;

    [Fact]
    public void ASentPartysTaleNamesTheHandsBehindIt()
    {
        var (sim, me, party) = ExpeditionTests.Round("Innkeeper");
        var smith = Villager(sim, "Blacksmith");
        var healer = Villager(sim, "Healer");
        var cook = Villager(sim, "Cook");
        // The smith's blade in the first one's hand, the cook's rations in their pack, the healer's work on the second.
        var blade = sim.World.Get<Equipment>(party[0]).Weapon;
        Assert.True(sim.World.IsAlive(blade));
        sim.World.Add(blade, new Crafted { Quality = Quality.Fine, Maker = smith });
        Rations.Restore(sim, party[0], [new RationRecord(0.9f, 0f, false, cook)]);
        sim.Shops.Treated(sim, party[^1], healer);

        ExpeditionTests.SendAndWait(sim, me, party);

        var tale = Assert.Single(sim.Expeditions.Tales);
        Assert.Equal(party.Count, tale.Went);
        Assert.Contains(tale.Deeds, d => d.Kind == DeedKind.Armed && d.Who == Name(sim, smith));
        Assert.Contains(tale.Deeds, d => d.Kind is DeedKind.Fed or DeedKind.Sickened && d.Who == Name(sim, cook));
        Assert.Contains(tale.Deeds, d => d.Kind == DeedKind.Healed && d.Who == Name(sim, healer));
        Assert.Contains(tale.Deeds, d => d.Kind == DeedKind.Sent && d.Who == Name(sim, me));
        Assert.All(tale.Deeds, d => Assert.False(string.IsNullOrEmpty(d.Phrase)));

        // Saved with the game, and kept for the reveal.
        var loaded = SaveLoadTests.RoundTrip(sim);
        var again = Assert.Single(loaded.Expeditions.Tales);
        Assert.Equal(tale.Headline, again.Headline);
        Assert.Equal(tale.Deeds, again.Deeds);
        sim.Round.End(sim, RoundOutcome.FellShort);
        Assert.Equal(tale.Deeds, Assert.Single(sim.Round.Summary!.Quests).Deeds);
    }

    [Fact]
    public void TheSmithsNameComesHomeOnTheBlade()
    {
        var (sim, me, party) = ExpeditionTests.Round("Innkeeper");
        var smith = Villager(sim, "Blacksmith");
        foreach (var e in party)
            if (sim.World.TryGet<Equipment>(e, out var eq) && sim.World.IsAlive(eq.Weapon))
                sim.World.Add(eq.Weapon, new Crafted { Quality = Quality.Fine, Maker = smith });
        var names = ExpeditionTests.SendAndWait(sim, me, party).Names;
        var back = sim.Creatures().Where(e => sim.World.Has<Adventurer>(e) && names.Contains(Name(sim, e))).ToList();
        Assert.All(back, e =>
        {
            if (sim.World.TryGet<Equipment>(e, out var eq) && sim.World.IsAlive(eq.Weapon))
                Assert.Equal(smith, sim.World.Get<Crafted>(eq.Weapon).Maker);
        });
    }

    [Fact]
    public void HiddenTruthsAreNeverToastedAndAPartyThanksWhoSavedThem()
    {
        var tale = new QuestTale { Quest = "the barrow", Party = "the iron lantern", PartyId = 3, Cleared = true, Went = 3, Back = 3 };
        tale.Credit(DeedKind.ShotWide, "Cole", "arrows");
        tale.Credit(DeedKind.Jammed, "Dara", "grapnel");
        tale.Credit(DeedKind.Shot, "Cole", "arrows");
        tale.Credit(DeedKind.Armed, "Bea", "longsword");
        tale.Credit(DeedKind.Armed, "Bea", "longsword");
        tale.Credit(DeedKind.SparedALife, "Arin", "charm");

        Assert.Equal(2, tale.Deeds.Single(d => d.Kind == DeedKind.Armed).Count); // counted, not repeated
        Assert.All(tale.Deeds.Where(d => d.Kind is DeedKind.ShotWide or DeedKind.Jammed or DeedKind.Shot), d => Assert.False(d.Public));
        Assert.True(tale.Deeds.Single(d => d.Kind == DeedKind.ShotWide).Hidden);

        // A life saved comes first in a toast; nothing hidden is ever said there.
        var toasted = QuestTale.Toasted([tale], 3)!;
        Assert.Equal("Arin", toasted.Who);
        var line = FestivalSystem.ThanksLine(toasted, 4f);
        Assert.Contains("Arin", line);
        Assert.DoesNotContain("warp", line);
        Assert.Null(QuestTale.Toasted([tale], 4)); // another party has nothing of theirs to toast
        Assert.Null(QuestTale.Toasted([new QuestTale { PartyId = 5, Cleared = false, Deeds = [new Deed(DeedKind.Armed, "Bea", "axe")] }], 5));
    }

    [Fact]
    public void TheHandsBehindTheHeroesAreWhoeverWentOutWithTheMostClearedQuests()
    {
        QuestTale Cleared(params (DeedKind Kind, string Who)[] deeds)
        {
            var t = new QuestTale { Cleared = true };
            foreach (var (kind, who) in deeds)
                t.Credit(kind, who, "thing");
            return t;
        }
        var tales = new List<QuestTale>
        {
            Cleared((DeedKind.Armed, "Bea"), (DeedKind.Armed, "Bea"), (DeedKind.Fed, "Arin")),
            Cleared((DeedKind.Fed, "Arin"), (DeedKind.ShotWide, "Cole")),
            Cleared((DeedKind.Healed, "Dara"), (DeedKind.Fed, "Arin"), (DeedKind.ShotWide, "Cole")),
            new() { Cleared = false, Deeds = [new Deed(DeedKind.Armed, "Bea", "axe"), new Deed(DeedKind.Armed, "Bea", "sword")] },
        };
        var hands = QuestTale.Hands(tales);
        Assert.Equal(("Arin", 3), hands[0]);
        Assert.DoesNotContain(hands, h => h.Who == "Cole"); // warped work earns nobody a toast
        Assert.Equal(1, hands.Single(h => h.Who == "Bea").Quests); // once per quest, and only those that came good
    }
}
