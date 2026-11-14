using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The rest of the menu: porridge of a morning, pies, and the butcher block (jointing and plucking).</summary>
public class KitchenMenuTests
{
    private static List<Entity> OnMe(Simulation sim, Entity me)
    {
        var list = new List<Entity>();
        if (sim.World.Get<Carrying>(me).Item is { IsNull: false } held)
            list.Add(held);
        if (sim.PackOf(me) is { IsNull: false } pack)
            list.AddRange(sim.StoredIn(pack));
        return list;
    }

    private static Entity Named(Simulation sim, Entity me, string def) =>
        OnMe(sim, me).FirstOrDefault(e => sim.World.Get<Thing>(e).Def.Name == def, Entity.Null);

    // ---------------- Porridge ----------------

    [Fact]
    public void APotOfOatsServesPorridgeAndAPotOfMeatStew()
    {
        var (sim, me, pot) = InnKitchenTests.Kitchen();
        Cooking.Add(sim, pot, sim.CreateItem(sim.Thing("Oats"), 10));
        Assert.Equal("Porridge", Cooking.Serves(sim, pot).Name);
        var p = Cooking.PotOf(sim, pot);
        p.Doneness = 1f;
        var bowl = Cooking.Ladle(sim, me, pot);
        Assert.Equal("Porridge", sim.World.Get<Thing>(bowl).Def.Name);

        var (sim2, _, pot2) = InnKitchenTests.Kitchen();
        Cooking.Add(sim2, pot2, sim2.CreateItem(sim2.Thing("Meat"), 6));
        Cooking.Add(sim2, pot2, sim2.CreateItem(sim2.Thing("Oats"), 2));
        Assert.Equal("Stew", Cooking.Serves(sim2, pot2).Name);
    }

    [Fact]
    public void PorridgeIsAskedForOnlyOfAMorning()
    {
        var (sim, me, _) = ExpeditionTests.Round("Cook");
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        Assert.True(Menu.Set(sim, me, inn, sim.Thing("Porridge"), 2, true));
        Assert.True(Menu.Set(sim, me, inn, sim.Thing("Bread"), 2, true));
        while (sim.Clock.HourOfDay is < 7f or >= 8f)
            sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.Equal(["Porridge"], Menu.Asked(sim, inn).Select(d => d.Dish.Name));
        while (sim.Clock.HourOfDay is < 13f or >= 14f)
            sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.Equal(["Bread"], Menu.Asked(sim, inn).Select(d => d.Dish.Name));
    }

    // ---------------- The butcher block ----------------

    private static (Simulation Sim, Entity Me, Entity Block) Block(params (string Def, int Count)[] stock)
    {
        var (sim, me, _) = InnKitchenTests.Kitchen();
        var block = sim.SpawnStructure(sim.Thing("ButcherBlock"), C(10, 12));
        sim.RebuildDerivedIfNeeded();
        foreach (var (def, count) in stock)
            Inventory.Stow(sim, me, sim.CreateItem(sim.Thing(def), count));
        return (sim, me, block);
    }

    private static void Work(Simulation sim, Entity me, Entity station)
    {
        var at = sim.World.Get<Thing>(station).Cell;
        sim.ApplyNow(new AvatarWorkCommand(me, true, at));
        sim.ApplyNow(new AvatarWorkCommand(me, false));
    }

    /// <summary>Bring the cleaver down as it passes over each joint (or between them).</summary>
    private static void Joint(ButcherSession s, bool onJoints)
    {
        var steps = (int)(ButcherSession.Sweep * SimClock.TicksPerSecond);
        foreach (var j in ButcherSession.JointsAt)
        {
            var target = onJoints ? j : Math.Clamp(j + 0.08f, 0f, 1f);
            // Find the next step at which the sweep (going out) is over the target.
            var at = s.StepsDone;
            for (var t = s.StepsDone; t < s.StepsDone + steps; t++)
            {
                var f = t / (float)SimClock.TicksPerSecond / ButcherSession.Sweep;
                f -= MathF.Floor(f);
                var x = f < 0.5f ? f * 2f : 2f - f * 2f;
                if (MathF.Abs(x - target) < 0.01f)
                {
                    at = t;
                    break;
                }
            }
            s.Advance([new WorkInput(at, WorkInputKind.Press)], at + 1);
            if (s.Done)
                break;
        }
    }

    [Fact]
    public void ASideJointedCleanlyGivesItsFullYield()
    {
        var (sim, me, block) = Block(("PorkSide", 1));
        Assert.Equal("Joint the side of pork", AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(block).Cell).Label);
        Work(sim, me, block);
        var s = Assert.IsType<ButcherSession>(sim.World.Get<WorkSession>(me));
        Joint(s, onJoints: true);
        Assert.True(s.Done);
        Assert.Equal(0, s.Hacks);
        sim.Tick();
        Assert.True(Named(sim, me, "PorkSide").IsNull);
        Assert.Equal(sim.Thing("PorkSide").Joints!.Yield, sim.World.Get<Stack>(Named(sim, me, "Meat")).Count);
    }

    [Fact]
    public void ASideHackedThroughTheBoneGivesLess()
    {
        var (sim, me, block) = Block(("PorkSide", 1));
        Work(sim, me, block);
        var s = Assert.IsType<ButcherSession>(sim.World.Get<WorkSession>(me));
        while (!s.Done)
            Joint(s, onJoints: false);
        Assert.True(s.Hacks > 0);
        sim.Tick();
        Assert.True(sim.World.Get<Stack>(Named(sim, me, "Meat")).Count < sim.Thing("PorkSide").Joints!.Yield);
    }

    [Fact]
    public void ShortFirmPullsPluckABirdWithoutATear()
    {
        var (sim, me, block) = Block(("Fowl", 1));
        Assert.Equal("Pluck the fowl", AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(block).Cell).Label);
        Work(sim, me, block);
        var s = Assert.IsType<PluckSession>(sim.World.Get<WorkSession>(me));
        // Hold for two-thirds of a second, let go, again: firm, never taut.
        var hold = (int)(0.6f * SimClock.TicksPerSecond);
        for (var i = 0; i < 30 && !s.Done; i++)
        {
            var at = s.StepsDone;
            s.Advance([new WorkInput(at, WorkInputKind.Hold, 1), new WorkInput(at + hold, WorkInputKind.Hold, 0)], at + hold + 3);
        }
        Assert.True(s.Done);
        Assert.Equal(0, s.Tears);
        sim.Tick();
        Assert.Equal(2, sim.World.Get<Stack>(Named(sim, me, "DressedFowl")).Count);
    }

    [Fact]
    public void HangingOnTearsTheSkin()
    {
        var (sim, me, block) = Block(("Fowl", 1));
        Work(sim, me, block);
        var s = Assert.IsType<PluckSession>(sim.World.Get<WorkSession>(me));
        s.Advance([new WorkInput(0, WorkInputKind.Hold, 1)], 20 * SimClock.TicksPerSecond);
        Assert.True(s.Tears >= 2, $"{s.Tears} tears");
        Assert.True(s.Done);
        sim.Tick();
        Assert.Equal(1, sim.World.Get<Stack>(Named(sim, me, "DressedFowl")).Count);
    }

    [Fact]
    public void DressedFowlGoesOnTheSpit()
    {
        var (sim, me, _) = InnKitchenTests.Kitchen();
        var spit = sim.SpawnStructure(sim.Thing("Spit"), C(10, 10));
        sim.RebuildDerivedIfNeeded();
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("DressedFowl"), 2);
        Assert.Equal("Put the meat on the spit", AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(spit).Cell).Label);
    }

    [Fact]
    public void AnAiCookDressesWhatsWholeInTheLarder()
    {
        var sim = Create(20, 20);
        var larder = sim.SpawnStructure(sim.Thing("Larder"), C(5, 5));
        sim.RebuildDerivedIfNeeded();
        sim.Store(larder, sim.CreateItem(sim.Thing("PorkSide"), 1));
        sim.Store(larder, sim.CreateItem(sim.Thing("Fowl"), 2));
        Assert.Equal(3, Butchery.DressAll(sim, larder));
        var names = sim.StoredIn(larder).Select(e => sim.World.Get<Thing>(e).Def.Name).ToList();
        Assert.Contains("Meat", names);
        Assert.Contains("DressedFowl", names);
        Assert.DoesNotContain("PorkSide", names);
        Assert.DoesNotContain("Fowl", names);
    }

    // ---------------- Pies ----------------

    private static (Simulation Sim, Entity Me, Entity Table, Entity Oven) Bakery()
    {
        var (sim, me, _) = InnKitchenTests.Kitchen();
        var table = sim.SpawnStructure(sim.Thing("PrepTable"), C(10, 12));
        var oven = sim.SpawnStructure(sim.Thing("Oven"), C(9, 11));
        sim.RebuildDerivedIfNeeded();
        return (sim, me, table, oven);
    }

    /// <summary>Dough in hand and filling in the pack, a pie crimped (on the marks or not), and the raw pie.</summary>
    private static Entity MakePie(Simulation sim, Entity me, Entity table, bool onMarks)
    {
        var dough = sim.CreateItem(sim.Thing(Baking.DoughDef), 1);
        sim.World.Add(dough, new Dough { Knead = 0.9f, MadeAt = sim.Clock.Tick, Maker = me });
        sim.World.Get<Carrying>(me).Item = dough;
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("Berries"), 2));
        Assert.Equal("Make a pie", AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(table).Cell).Label);
        Work(sim, me, table);
        var s = Assert.IsType<CrimpSession>(sim.World.Get<WorkSession>(me));
        var perMark = CrimpSession.Round * SimClock.TicksPerSecond / CrimpSession.Marks;
        var inputs = Enumerable.Range(1, CrimpSession.Marks)
            .Select(i => new WorkInput((int)(i * perMark + (onMarks ? 0 : perMark / 2)), WorkInputKind.Press)).ToList();
        s.Advance(inputs, inputs[^1].Step + 2);
        Assert.True(s.Done);
        sim.Tick();
        return Named(sim, me, Pies.Raw);
    }

    private static Entity BakePie(Simulation sim, Entity me, Entity oven)
    {
        var at = sim.World.Get<Thing>(oven).Cell;
        Assert.Equal("Put the pie in the oven", AvatarSystem.ActionAt(sim, me, at).Label);
        Work(sim, me, oven);
        var s = Assert.IsType<BakeSession>(sim.World.Get<WorkSession>(me));
        for (var t = 0; t < 60 * 60 && s.Smell != "a golden crust"; t++)
            s.Advance([], s.StepsDone + 1);
        s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Act)], s.StepsDone + 2);
        sim.Tick();
        return Named(sim, me, Pies.Pie);
    }

    [Fact]
    public void APieCrimpedTightBakesIntoFineSlicesTastingOfItsFilling()
    {
        var (sim, me, table, oven) = Bakery();
        var raw = MakePie(sim, me, table, onMarks: true);
        Assert.False(raw.IsNull);
        Assert.True(sim.World.Get<RawPie>(raw).Crimp >= 0.8f);
        Assert.True(Named(sim, me, "Berries").IsNull, "the filling went in");
        var pie = BakePie(sim, me, oven);
        Assert.Equal(Pies.Slices, sim.World.Get<Stack>(pie).Count);
        Assert.Equal(DishFault.None, sim.World.Get<Dish>(pie).Fault);
        Assert.True(sim.World.Get<Crafted>(pie).Quality >= Quality.Fine, $"{sim.World.Get<Crafted>(pie).Quality}");
        Assert.Equal(FlavourKind.Sweet, sim.World.Get<Dish>(pie).Flavour.Leading);
    }

    [Fact]
    public void ALooseRimLeaksInTheOven()
    {
        var (sim, me, table, oven) = Bakery();
        MakePie(sim, me, table, onMarks: false);
        var pie = BakePie(sim, me, oven);
        Assert.Equal(DishFault.Leaked, sim.World.Get<Dish>(pie).Fault);
    }

    [Fact]
    public void APieInTheOvenSurvivesASave()
    {
        var (sim, me, table, oven) = Bakery();
        MakePie(sim, me, table, onMarks: true);
        Work(sim, me, oven);
        Assert.True(Baking.LoadOf(sim, oven).Pie);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var (a, b) = (Baking.LoadOf(sim, oven), Baking.LoadOf(loaded, oven));
        Assert.Equal((a.Pie, a.Loaves, a.Knead, a.Filling), (b.Pie, b.Loaves, b.Knead, b.Filling));
    }
}
