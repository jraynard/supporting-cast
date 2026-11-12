using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Expeditions;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Charms: mendings woven ahead into linen, torn later, sold, and carried on quests as wards.</summary>
public class CharmTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>A round with Ana the healer, a party in town, and the shrine.</summary>
    private static (Simulation Sim, Entity Me, Entity Shrine, List<Entity> Party) Town()
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade("Healer")], []));
        sim.ApplyNow(new StartRoundCommand());
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        Assert.NotEmpty(party);
        return (sim, Players.CharacterOf(sim, Ana), ShopSystem.BuildingOf(sim, sim.Defs.Building("Shrine")), party);
    }

    /// <summary>Weave these runes into a charm, each on the beat, and let it go.</summary>
    private static void WeaveCharm(Simulation sim, Entity me, params string[] runes)
    {
        sim.ApplyNow(new WeaveCharmCommand(me));
        Assert.True(((CastSession)sim.World.Get<WorkSession>(me)).IntoCharm);
        var keys = runes.Select(n => sim.Defs.Rune(n).Index).ToList();
        var predictor = new WorkPredictor();
        for (var t = 0; t < 60 * 60 && sim.World.Has<WorkSession>(me); t++)
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

    private static Entity NewCharm(Simulation sim, string mending, float quality = 1f, int extra = 0)
    {
        var item = sim.CreateItem(sim.Defs.Thing(Charms.Def), 1, null);
        sim.World.Add(item, new Charm { Mending = sim.Defs.Mending(mending), Quality = quality, Extra = extra, Maker = Entity.Null });
        return item;
    }

    [Fact]
    public void WeavingIntoACharmUsesUpALinenStripAndKeepsTheMending()
    {
        var (sim, me, _, _) = Town();
        var bandages = Bandaging.BandagesOn(sim, me);
        Assert.True(bandages > 0, "the healer's kit has bandages");
        var mana = sim.World.Get<Mana>(me).Current;

        WeaveCharm(sim, me, "Thread", "Flesh", "Thread");
        var charm = Assert.Single(Charms.Carried(sim, me));
        var known = Perception.For(sim, me).Charm(charm)!.Value;
        Assert.Equal(sim.Defs.Mending("Knit"), known.Mending);
        Assert.Equal(1, known.Extra);
        Assert.True(known.Quality > 0.9f, "its maker knows how well it was woven");
        Assert.Null(Perception.For(sim, sim.Creatures().First(e => e != me && sim.World.Has<Mind>(e))).Charm(charm)!.Value.Quality);
        Assert.Equal("charm of knitting (+)", sim.LabelOf(charm));
        Assert.Equal(bandages - 1, Bandaging.BandagesOn(sim, me));
        Assert.Equal(mana - sim.Defs.Mending("Knit").CostAt(1), sim.World.Get<Mana>(me).Current, 0.5f);
    }

    [Fact]
    public void ACharmWantsLinenAndAWeaveThatFizzlesMakesNone()
    {
        var (sim, me, _, _) = Town();
        WeaveCharm(sim, me, "Salt", "Rime", "Thread");
        Assert.Empty(Charms.Carried(sim, me));

        foreach (var b in Charms.Carried(sim, me).Concat(sim.StoredIn(sim.PackOf(me)).Where(i => Bandaging.IsBandage(sim, i)).ToList()))
            sim.DestroyThing(b);
        Assert.NotNull(CastSession.Problem(sim, me, 0, intoCharm: true));
    }

    [Fact]
    public void TearingACharmMendsWhoeverTearsItGiftOrNo()
    {
        var (sim, me, _, _) = Town();
        var bearer = sim.SpawnCreature(sim.Defs.Creature("Human"), "Torvi", sim.World.Get<Position>(me).Cell, ageDays: 9000);
        var h = sim.World.Get<Health>(bearer);
        var arm = h.Body.Parts.FindIndex(p => p.Label.Contains("arm") && !p.Vital);
        HealthSystem.Damage(sim, bearer, 12f, DamageType.Cut, arm);
        HealthSystem.Damage(sim, bearer, 5f, DamageType.Blunt, arm);
        var before = h.Injuries.Single(i => i.Type == DamageType.Cut).Damage;
        var charm = NewCharm(sim, "Knit");
        Inventory.Stow(sim, bearer, charm);

        Assert.True(Charms.Tear(sim, bearer, charm));
        Assert.False(sim.World.IsAlive(charm));
        Assert.True((h.Injuries.Find(i => i.Type == DamageType.Cut)?.Damage ?? 0f) < before - 10f, "the cut is knit");
        Assert.Equal(5f, h.Injuries.Single(i => i.Type == DamageType.Blunt).Damage, 0.5f); // not what it doesn't fit
    }

    [Fact]
    public void AnAdventurerBuysACharmOnShowForTheRoad()
    {
        var (sim, me, shrine, party) = Town();
        var cell = ShopSystem.LineCells(sim, shrine).Last();
        var charm = sim.PlaceItemNear(NewCharm(sim, "Knit"), cell);
        Assert.Equal(shrine, Buildings.At(sim, sim.World.Get<Thing>(charm).Cell));
        sim.ApplyNow(new SetItemPriceCommand(me, charm, 10, true));
        Assert.Equal(shrine, Goods.ShownIn(sim, charm));
        Assert.All(party, e => Assert.True(Goods.IsUpgrade(sim, e, charm)));

        for (var t = 0; t < SimClock.HoursToTicks(3) && !sim.Shops.All.Any(o => o.Subject == charm); t += 300)
            sim.TickMany(300);
        var order = sim.Shops.All.Single(o => o.Subject == charm);
        var buyer = order.Poster;
        sim.World.Get<Carrying>(me).Item = Inventory.PickUp(sim, me, charm, sim.World.Get<Carrying>(me).Item);
        var at = sim.World.Get<Position>(buyer).Cell;
        var by = sim.SpiralFrom(at, 3).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z);
        sim.World.Get<Position>(me).Cell = by;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(by);
        sim.ApplyNow(new GiveCommand(me, buyer, charm, 1));

        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.Contains(charm, Charms.Carried(sim, buyer));
        // A second of the same mending is no use to them.
        Assert.False(Goods.IsUpgrade(sim, buyer, NewCharm(sim, "Knit")));
        Assert.True(Goods.IsUpgrade(sim, buyer, NewCharm(sim, "Ease")));
    }

    [Fact]
    public void OnAQuestACharmWardsTheWoundsItFitsAndMayCheatDeath()
    {
        var defs = LoadDefs();
        CharmRecord Record(string m, float q = 1f) => new(defs.Mending(m), 0, q, Entity.Null);
        var charms = new List<CharmRecord> { Record("Cool"), Record("Knit") };

        Assert.Equal(10f, Charms.Ward(charms, DamageType.Blunt, 10f)); // nothing fits a bruise
        Assert.Equal(2, charms.Count);
        Assert.Equal(0f, Charms.Ward(charms, DamageType.Cut, 10f)); // knitting closes it
        Assert.Single(charms);
        Assert.Equal(20f - defs.Mending("Cool").Heal, Charms.Ward(charms, DamageType.Burn, 20f), 0.01f);
        Assert.Empty(charms);

        Assert.True(Charms.StrengthFactor([Record("Knit"), Record("Ease")]) > 1.05f);
        Assert.Equal(1f, Charms.StrengthFactor([new(defs.Mending("Steady"), 0, 1f, Entity.Null)])); // steadying heals nothing

        var spared = new List<CharmRecord> { Record("Knit", 1f) };
        Assert.NotNull(Charms.Spare(spared, 0.8f));
        Assert.Empty(spared);
        var loose = new List<CharmRecord> { Record("Knit", 0f) };
        Assert.Null(Charms.Spare(loose, 0.8f)); // too loosely woven to hold
        Assert.Empty(loose); // and spent all the same
    }

    [Fact]
    public void CharmsGoOutWithTheirBearerAndComeBackIfUnspent()
    {
        var (sim, _, _, party) = Town();
        var bearer = party[0];
        Inventory.Stow(sim, bearer, NewCharm(sim, "Kindle", 0.6f, 2));
        var record = ExpeditionSystem.Record(sim, bearer);
        var c = Assert.Single(record.Charms);
        Assert.Equal((sim.Defs.Mending("Kindle"), 2, 0.6f), (c.Mending, c.Extra, c.Quality));

        var again = sim.SpawnCreature(sim.Defs.Creature("Human"), "Back", sim.World.Get<Position>(bearer).Cell, ageDays: 9000);
        Charms.Restore(sim, again, record.Charms);
        var back = Assert.Single(Charms.Carried(sim, again));
        Assert.Equal(2, sim.World.Get<Charm>(back).Extra);
    }

    [Fact]
    public void ACharmSurvivesASave()
    {
        var (sim, me, _, _) = Town();
        WeaveCharm(sim, me, "Still", "Flesh");
        var charm = Charms.Carried(sim, me).Single();
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var copy = loaded.World.Get<Charm>(charm);
        Assert.Equal(sim.Defs.Mending("Ease"), copy.Mending);
        Assert.Equal(sim.World.Get<Charm>(charm).Quality, copy.Quality);
        Assert.Equal(me, copy.Maker);
    }
}
