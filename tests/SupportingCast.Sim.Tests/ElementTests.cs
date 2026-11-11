using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Environment;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;
using static SupportingCast.Sim.Tests.TestWorld;

namespace SupportingCast.Sim.Tests;

/// <summary>
/// What each element's breath does (docs/plans/dragon-types.md): in a fight (damage, statuses,
/// lightning leaping), to the land (frost douses, poison lingers and withers, thorns nurture),
/// and the work each can be given. Fire is the breath the game always had.
/// </summary>
public class ElementTests
{
    private static Simulation Field(int w = 30, int h = 20)
    {
        var sim = Create(w, h, depth: 6);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        var dirt = sim.Defs.Block("Dirt"); // bare ground: only what we place can burn
        Array.Fill(sim.Map.Block, dirt, 0, sim.Map.LayerSize);
        sim.Map.MarkStructureChanged();
        sim.RebuildDerivedIfNeeded();
        return sim;
    }

    private static Entity Dragon(Simulation sim, DragonElement element, int3 at, string breed = "Dragon")
    {
        var def = sim.Defs.Creature(breed);
        var d = Dragons.Spawn(sim, def, element, "Vesper", at, ageDays: Dragons.AdultAgeDays(def) + 2);
        sim.World.Get<Dragon>(d).Affinity = 2f;
        Array.Fill(sim.World.Get<Needs>(d).Values, 1f);
        return d;
    }

    private static Entity Raider(Simulation sim, int3 at)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Bandit", at, Faction.Raider, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        return e;
    }

    // ---------------------------------------------------------------- defs and shape

    [Fact]
    public void EveryElementHasADefAndFireIsTheBreathItAlwaysWas()
    {
        var defs = LoadDefs();
        foreach (var element in Enum.GetValues<DragonElement>())
            Assert.Equal(element, defs.Element(element).Element);
        var fire = defs.Element(DragonElement.Fire);
        Assert.Equal((1f, 1f, 1f), (fire.Range, fire.Cone, fire.Rays));
        Assert.Equal(DamageType.Burn, fire.DamageType);
        Assert.Equal([WorkType.Burn, WorkType.Stoke], fire.Work);

        var sim = Field();
        var d = Dragon(sim, DragonElement.Fire, C(5, 10));
        var stage = sim.World.Get<Creature>(d).Stage(sim.Clock.Tick);
        Assert.Equal(new BreathShape(stage.BreathRange, stage.BreathCone, stage.BreathRays, 0.9f, 1.5f), Breath.ShapeOf(sim, d));
    }

    [Fact]
    public void LightningReachesFurtherInANarrowerBolt()
    {
        var sim = Field();
        var fire = Breath.ShapeOf(sim, Dragon(sim, DragonElement.Fire, C(5, 5)));
        var bolt = Breath.ShapeOf(sim, Dragon(sim, DragonElement.Lightning, C(5, 15)));
        Assert.True(bolt.Range > fire.Range && bolt.ConeDegrees < fire.ConeDegrees && bolt.Rays < fire.Rays);
    }

    [Fact]
    public void BreathWorkFollowsTheElement()
    {
        var sim = Field();
        var adult = sim.Defs.Creature("Dragon").LifeStages[^1];
        bool Can(DragonElement e, WorkType t) => sim.Defs.Creature("Dragon").CanDo(t, adult, sim.Defs.Element(e));
        Assert.True(Can(DragonElement.Fire, WorkType.Burn) && Can(DragonElement.Fire, WorkType.Stoke));
        Assert.False(Can(DragonElement.Ice, WorkType.Burn) || Can(DragonElement.Ice, WorkType.Stoke));
        Assert.True(Can(DragonElement.Ice, WorkType.Chill));
        Assert.True(Can(DragonElement.Lightning, WorkType.Quarry));
        Assert.True(Can(DragonElement.Poison, WorkType.Blight));
        Assert.True(Can(DragonElement.Nature, WorkType.Nurture));
        Assert.False(Can(DragonElement.Fire, WorkType.Chill));
        // Everyone else keeps away from breath work, whatever the element.
        var human = sim.Defs.Creature("Human");
        Assert.DoesNotContain(Enum.GetValues<WorkType>().Where(ElementDef.IsBreathWork), t => human.CanDo(t, human.StageForAge(9000)));
        // A dragon's own work comes first.
        var ice = Dragon(sim, DragonElement.Ice, C(10, 10));
        Assert.Equal(2, sim.World.Get<Mind>(ice).WorkPriority[(int)WorkType.Chill]);
    }

    [Fact]
    public void IceDragonsShrugOffTheCold()
    {
        var sim = Field();
        var fire = sim.World.Get<Creature>(Dragon(sim, DragonElement.Fire, C(5, 5))).Tolerance(sim.Clock.Tick);
        var ice = sim.World.Get<Creature>(Dragon(sim, DragonElement.Ice, C(5, 15))).Tolerance(sim.Clock.Tick);
        Assert.Equal(fire.ComfortMin - 15, ice.ComfortMin);
        Assert.Equal(fire.SafeMin - 15, ice.SafeMin);
        Assert.Equal(fire.ComfortMax, ice.ComfortMax);
    }

    // ---------------------------------------------------------------- combat

    [Fact]
    public void FrostWoundsAndChillsWhoeverItCatches()
    {
        var sim = Field();
        var dragon = Dragon(sim, DragonElement.Ice, C(5, 10));
        var raider = Raider(sim, C(10, 10));
        var (_, hit) = CombatRules.Breathe(sim, dragon, C(10, 10));
        Assert.Contains(hit, h => h.Who == raider);
        Assert.Contains(sim.World.Get<Health>(raider).Injuries, i => i.Type == DamageType.Frost);
        Assert.True(sim.World.Get<Afflictions>(raider).Chilled(sim.Clock.Tick));
        // A chilled unit walks slower and gets fewer time units.
        Assert.True(AfflictionRules.MoveFactor(sim, raider) < 1f);
        Assert.True(AfflictionRules.TurnTimeUnits(sim, raider, 60f) < 60f);
    }

    [Fact]
    public void AStunCostsTimeUnitsOnceAndThenItsGone()
    {
        var sim = Field();
        var raider = Raider(sim, C(10, 10));
        AfflictionRules.Apply(sim, raider, sim.Defs.Element(DragonElement.Lightning).Status, Entity.Null);
        var first = AfflictionRules.TurnTimeUnits(sim, raider, 60f);
        Assert.Equal(60f * (1f - 0.35f), first, 3);
        Assert.Equal(60f, AfflictionRules.TurnTimeUnits(sim, raider, 60f));
        // Out of a fight it can't swing for a moment, then it's over and the component goes.
        Assert.True(AfflictionRules.Stunned(sim, raider));
        sim.TickMany(120);
        Assert.False(AfflictionRules.Stunned(sim, raider));
        Assert.False(sim.World.Has<Afflictions>(raider));
    }

    [Fact]
    public void LightningLeapsToTheNextFoe()
    {
        var sim = Field();
        var dragon = Dragon(sim, DragonElement.Lightning, C(4, 10));
        var first = Raider(sim, C(10, 10));
        var beside = Raider(sim, C(11, 12)); // off the bolt's narrow line, but close to the first
        var (ev, hit) = CombatRules.Breathe(sim, dragon, C(10, 10));
        Assert.Contains(hit, h => h.Who == first);
        Assert.Contains(hit, h => h.Who == beside);
        Assert.NotEmpty(ev.Arcs);
        Assert.Contains(sim.World.Get<Health>(beside).Injuries, i => i.Type == DamageType.Shock);
    }

    [Fact]
    public void ThornsRootTheirTargetInPlace()
    {
        var sim = Field();
        var dragon = Dragon(sim, DragonElement.Nature, C(5, 10));
        var raider = Raider(sim, C(10, 10));
        CombatRules.Breathe(sim, dragon, C(10, 10));
        Assert.True(AfflictionRules.Rooted(sim, raider));
        Assert.True(float.IsInfinity(TimeUnits.WalkCost(sim, raider)), "rooted: no moves at all in a fight");
        Assert.Equal(0f, MovementSystem.WalkSpeedPerTick(sim, raider));
    }

    [Fact]
    public void VenomKillsSlowlyButNotAPoisonDragon()
    {
        var sim = Field();
        var raider = Raider(sim, C(10, 10));
        var other = Dragon(sim, DragonElement.Poison, C(14, 10));
        var status = sim.Defs.Element(DragonElement.Poison).Status!;
        AfflictionRules.Apply(sim, raider, status, Entity.Null);
        Assert.True(Dragons.Resists(sim, other, DamageType.Venom));
        Assert.True(Dragons.ImmuneTo(sim, other, sim.Defs.Element(DragonElement.Poison)));
        Assert.False(Dragons.Resists(sim, raider, DamageType.Venom));
        sim.TickMany(status.Ticks + 60);
        var venom = sim.World.Get<Health>(raider).Injuries.Where(i => i.Type == DamageType.Venom).ToList();
        Assert.Single(venom); // one wound that worsens, not a new one every update
        // Its full dose, less what the body healed meanwhile.
        Assert.InRange(venom[0].Damage, 0.8f * status.PerHour * status.Ticks / SimClock.TicksPerHour, status.PerHour * status.Ticks / SimClock.TicksPerHour);
    }

    [Fact]
    public void FireStillSparesTheFireproofAndLeavesNoStatus()
    {
        var sim = Field();
        var dragon = Dragon(sim, DragonElement.Fire, C(5, 10));
        var other = Dragon(sim, DragonElement.Ice, C(10, 9));
        var raider = Raider(sim, C(10, 10));
        var (_, hit) = CombatRules.Breathe(sim, dragon, C(10, 10));
        Assert.DoesNotContain(hit, h => h.Who == other); // every dragon is fireproof
        Assert.Contains(hit, h => h.Who == raider);
        Assert.False(sim.World.Has<Afflictions>(raider));
    }

    [Fact]
    public void AnAfflictionSurvivesASave()
    {
        var sim = Field();
        var raider = Raider(sim, C(10, 10));
        foreach (var e in (DragonElement[])[DragonElement.Ice, DragonElement.Poison, DragonElement.Nature, DragonElement.Lightning])
            AfflictionRules.Apply(sim, raider, sim.Defs.Element(e).Status, Entity.Null);
        sim.World.Get<Creature>(raider).Faction = Faction.Wild; // nobody fights it
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        AiSystem.InterruptAll(sim);
        Assert.Equal(sim.FullHash(), loaded.FullHash());
        for (var hour = 0; hour < 2; hour++)
        {
            sim.TickMany(SimClock.TicksPerHour / 2);
            loaded.TickMany(SimClock.TicksPerHour / 2);
            Assert.Equal(sim.FullHash(), loaded.FullHash());
        }
    }

    // ---------------------------------------------------------------- the land

    [Fact]
    public void FrostPutsOutABurningTreeAndLightsNothing()
    {
        var sim = Field();
        for (var y = 9; y <= 11; y++)
            sim.SpawnStructure(sim.Thing("Oak"), C(11, y));
        sim.RebuildDerivedIfNeeded();
        foreach (var y in (int[])[9, 10, 11])
            sim.Fire.Ignite(sim, C(11, y), 0.6f);
        var before = Enumerable.Range(9, 3).Sum(y => sim.Map.Fire[sim.Map.Idx(C(11, y))]);
        var dragon = Dragon(sim, DragonElement.Ice, C(5, 10));
        var ev = Breath.Breathe(sim, Position.CenterOf(C(5, 10)) + new float3(0, 0, 1.2f), Position.CenterOf(C(11, 10)) + new float3(0, 0, 0.3f), Breath.ShapeOf(sim, dragon), dragon);
        Assert.Equal(0, ev.Ignited);
        Assert.True(ev.Affected > 0);
        var after = Enumerable.Range(9, 3).Sum(y => sim.Map.Fire[sim.Map.Idx(C(11, y))]);
        Assert.True(after < before, $"fire {before:F2} -> {after:F2}");
        Assert.True(sim.Map.Temperature[sim.Map.Idx(C(8, 10))] < 20f, "the air it passed through is colder");
    }

    [Fact]
    public void PoisonLingersSickensAndWithersButSparesItsOwn()
    {
        var sim = Field();
        var bush = sim.SpawnStructure(sim.Thing("BerryBush"), C(12, 11));
        sim.RebuildDerivedIfNeeded();
        var dragon = Dragon(sim, DragonElement.Poison, C(5, 10));
        var villager = sim.SpawnCreature(sim.Defs.Creature("Human"), "Tam", C(12, 10));
        sim.World.Get<Mind>(villager).Drafted = true; // stays put in it
        Breath.Breathe(sim, Position.CenterOf(C(5, 10)) + new float3(0, 0, 1.2f), Position.CenterOf(C(12, 10)) + new float3(0, 0, 0.3f), Breath.ShapeOf(sim, dragon), dragon);
        Assert.Single(sim.World.Query<VenomCloud>());

        sim.TickMany(SimClock.TicksPerHour / 2);
        Assert.True(sim.World.Get<Afflictions>(villager).Poisoned(sim.Clock.Tick), "friend and foe alike");
        Assert.False(sim.World.Has<Afflictions>(dragon));
        Assert.Empty(sim.Fire.BurningTiles);
        sim.TickMany(SimClock.TicksPerHour);
        Assert.False(sim.World.IsAlive(bush) && sim.World.Get<Thing>(bush).Hp >= sim.Thing("BerryBush").MaxHp, "the bush withers in it");
        sim.TickMany(SimClock.TicksPerHour);
        Assert.Empty(sim.World.Query<VenomCloud>()); // and it thins away
    }

    [Fact]
    public void PeopleWalkOutOfACloud()
    {
        var sim = Field();
        var villager = sim.SpawnCreature(sim.Defs.Creature("Human"), "Tam", C(12, 10));
        Array.Fill(sim.World.Get<Needs>(villager).Values, 1f);
        VenomClouds.Spawn(sim, C(12, 10), sim.Defs.Element(DragonElement.Poison).Cloud!, sim.Defs.Element(DragonElement.Poison), Entity.Null);
        var cloud = sim.World.Get<VenomCloud>(sim.World.Query<VenomCloud>()[0]);
        sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.False(VenomClouds.Inside(sim, cloud, sim.World.Get<Position>(villager).Cell), "they got out of it");
    }

    [Fact]
    public void AHatchlingsPoisonSneezeLeavesNoCloud()
    {
        var sim = Field();
        var dragon = Dragon(sim, DragonElement.Poison, C(5, 10));
        Breath.Breathe(sim, Position.CenterOf(C(5, 10)) + new float3(0, 0, 0.5f), Position.CenterOf(C(6, 10)),
            new BreathShape(1.8f, 40f, 3, 0.4f, 0.8f, SparesBuildings: true), dragon);
        Assert.Empty(sim.World.Query<VenomCloud>());
    }

    // ---------------------------------------------------------------- work

    private static Entity Worker(Simulation sim, DragonElement element, WorkType work, int3 at)
    {
        var d = Dragon(sim, element, at);
        var p = sim.World.Get<Mind>(d).WorkPriority;
        Array.Fill(p, 0);
        p[(int)work] = 1;
        return d;
    }

    private static void Run(Simulation sim, Entity dragon, int ticks, Func<bool>? until = null)
    {
        for (var t = 0; t < ticks && !(until?.Invoke() ?? false); t++)
        {
            Array.Fill(sim.World.Get<Needs>(dragon).Values, 1f);
            sim.Tick();
        }
    }

    [Fact]
    public void AnIceDragonKeepsTheChestColdAndTheMeatInItFresh()
    {
        var sim = Field(40, 30);
        sim.Climate.BaseTemperature = 25;
        Array.Fill(sim.Map.Temperature, 25f);
        var cold = sim.SpawnStructure(sim.Thing("Chest"), C(20, 15));
        var warm = sim.SpawnStructure(sim.Thing("Chest"), C(35, 25));
        Entity Meat(Entity chest)
        {
            var meat = sim.CreateItem(sim.Thing("Meat"), 10);
            Assert.True(sim.Store(chest, meat).IsNull);
            return meat;
        }
        var (coldMeat, warmMeat) = (Meat(cold), Meat(warm));
        sim.RebuildDerivedIfNeeded();
        var dragon = Worker(sim, DragonElement.Ice, WorkType.Chill, C(10, 15));

        Run(sim, dragon, SimClock.HoursToTicks(1), () => sim.World.Has<Chilled>(cold) || sim.World.Has<Chilled>(warm));
        Assert.True(sim.World.Has<Chilled>(cold), "the nearer chest first");
        float Rot(Entity meat) => sim.World.Get<Rot>(meat).Progress;
        var (coldBefore, warmBefore) = (Rot(coldMeat), Rot(warmMeat));
        var log = new List<string>();
        var last = coldBefore;
        for (var t = 0; t < SimClock.HoursToTicks(6); t++)
        {
            Run(sim, dragon, 1);
            if (Rot(coldMeat) != last)
            {
                log.Add($"t{sim.Clock.Tick} rot {Rot(coldMeat):F4} chilled until {(sim.World.TryGet<Chilled>(cold, out var ch) ? ch.Until : -1)} job {sim.World.Get<Mind>(dragon).Job?.GetType().Name}");
                last = Rot(coldMeat);
            }
        }
        Assert.True(log.Count == 0, string.Join("\n", log));
        Assert.True(sim.TemperatureAt(C(20, 15)) < 25f, $"the chest is {sim.TemperatureAt(C(20, 15)):F1}°C"); // out in the open air: a little
        Assert.True(sim.World.Get<Chilled>(cold).Until > sim.Clock.Tick, "kept cold all along");
        Assert.True(Rot(warmMeat) > warmBefore, "the warm chest's meat goes off");
        Assert.Equal(coldBefore, Rot(coldMeat)); // kept below freezing, it doesn't
    }

    [Fact]
    public void ALightningDragonShattersMarkedRock()
    {
        var sim = Field();
        var rock = C(15, 10);
        var i = sim.Map.Idx(rock);
        sim.Map.Block[i] = sim.Defs.Block("Stone");
        sim.Map.MarkStructureChanged(i);
        sim.RebuildDerivedIfNeeded();
        sim.Map.Designate(i, Designation.Dig);
        var dragon = Worker(sim, DragonElement.Lightning, WorkType.Quarry, C(5, 10));
        Run(sim, dragon, SimClock.HoursToTicks(1), () => !sim.Map.IsSolid(i));
        Assert.False(sim.Map.IsSolid(i));
        Assert.Contains(sim.SpawnedItems(), e => sim.World.Get<Thing>(e).Def.Name == "Stone");
    }

    [Fact]
    public void APoisonDragonClearsMarkedTreesWithoutFire()
    {
        var sim = Field();
        var tree = sim.SpawnStructure(sim.Thing("Oak"), C(15, 10));
        sim.RebuildDerivedIfNeeded();
        sim.Map.Designate(sim.Map.Idx(C(15, 10)), Designation.Burn);
        var dragon = Worker(sim, DragonElement.Poison, WorkType.Blight, C(5, 10));
        var burned = false;
        Run(sim, dragon, SimClock.HoursToTicks(2), () =>
        {
            burned |= sim.Fire.BurningTiles.Count > 0;
            return !sim.World.IsAlive(tree);
        });
        Assert.False(sim.World.IsAlive(tree));
        Assert.False(burned, "no fire anywhere");
        var snag = sim.Map.Structure[sim.Map.Idx(C(15, 10))];
        Assert.Equal("DeadTree", sim.World.Get<Thing>(snag).Def.Name); // the dead snag is left, marked for felling
        Assert.Equal(Designation.Chop, sim.Map.Designations[sim.Map.Idx(C(15, 10))] & (Designation.Chop | Designation.Burn));
    }

    [Fact]
    public void ANatureDragonBringsAHarvestedBushBackSooner()
    {
        var sim = Field();
        var tended = sim.SpawnStructure(sim.Thing("BerryBush"), C(15, 10));
        var left = sim.SpawnStructure(sim.Thing("BerryBush"), C(25, 18));
        foreach (var b in (Entity[])[tended, left])
            sim.World.Get<PlantState>(b).RegrowTicksLeft = SimClock.DaysToTicks(1.5f);
        sim.RebuildDerivedIfNeeded();
        var dragon = Worker(sim, DragonElement.Nature, WorkType.Nurture, C(5, 10));
        Run(sim, dragon, SimClock.HoursToTicks(1), () => sim.World.Has<Nurtured>(tended));
        Assert.True(sim.World.Has<Nurtured>(tended), $"job: {sim.World.Get<Mind>(dragon).Job?.GetType().Name} {sim.World.Get<Mind>(dragon).Job?.Report}");
        Array.Fill(sim.World.Get<Mind>(dragon).WorkPriority, 0); // just the one: it would tend the other next
        Run(sim, dragon, SimClock.HoursToTicks(8));
        Assert.True(sim.World.Get<PlantState>(tended).RegrowTicksLeft < sim.World.Get<PlantState>(left).RegrowTicksLeft - SimClock.HoursToTicks(12));
    }

    [Fact]
    public void AnIceDragonNeverTakesBurnWork()
    {
        var sim = Field();
        var grass = sim.Defs.Block("Grass");
        for (var x = 12; x <= 14; x++)
        {
            sim.Map.Block[sim.Map.Idx(x, 10, 0)] = grass;
            sim.Map.Designate(sim.Map.Idx(C(x, 10)), Designation.Burn);
        }
        sim.RebuildDerivedIfNeeded();
        var dragon = Worker(sim, DragonElement.Ice, WorkType.Burn, C(5, 10));
        Run(sim, dragon, SimClock.HoursToTicks(1));
        Assert.Empty(sim.Fire.BurningTiles);
        Assert.IsNotType<BreatheFireJob>(sim.World.Get<Mind>(dragon).Job);
    }
}
