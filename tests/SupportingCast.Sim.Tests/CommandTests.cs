using SupportingCast.Sim.Core;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;

namespace SupportingCast.Sim.Tests;

/// <summary>Player commands: every one survives the wire, and the overseer's orders do what the tools did.</summary>
public class CommandTests
{
    private static object? Sample(Type t, DefDatabase defs)
    {
        if (Nullable.GetUnderlyingType(t) is { } inner)
            return Sample(inner, defs);
        if (t.IsEnum)
            return Enum.GetValues(t).Cast<object>().Last();
        if (t == typeof(bool)) return true;
        if (t == typeof(byte)) return (byte)9;
        if (t == typeof(int)) return 7;
        if (t == typeof(long)) return 1234567890123L;
        if (t == typeof(float)) return 1.5f;
        if (t == typeof(string)) return "Bea";
        if (t == typeof(Guid)) return new Guid("0123456789abcdef0123456789abcdef");
        if (t == typeof(Entity)) return new Entity(3, 2);
        if (t == typeof(int2)) return new int2(4, -1);
        if (t == typeof(int3)) return new int3(5, 6, 1);
        if (t == typeof(ThingDef)) return defs.Thing("WoodWall");
        if (t == typeof(CreatureDef)) return defs.Creature("Human");
        if (t == typeof(RecipeDef)) return defs.Recipe("SawPlanks");
        if (t == typeof(MaterialDef)) return defs.Material("Oak");
        if (t == typeof(RaiderKindDef)) return defs.AllRaiderKinds[0];
        if (t == typeof(BuildingDef)) return defs.Building("Dwelling");
        if (t.IsArray)
        {
            var a = Array.CreateInstance(t.GetElementType()!, 2);
            a.SetValue(Sample(t.GetElementType()!, defs), 0);
            a.SetValue(Sample(t.GetElementType()!, defs), 1);
            return a;
        }
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            return Sample(t.GetGenericArguments()[0].MakeArrayType(), defs);
        if (t.IsValueType)
            return Activator.CreateInstance(t);
        throw new NotSupportedException($"no sample for {t.Name}");
    }

    [Fact]
    public void EveryCommandRoundTrips()
    {
        var defs = TestWorld.LoadDefs();
        var types = CommandCodec.CommandTypes.ToList();
        Assert.Contains(typeof(AvatarMoveCommand), types);
        Assert.Contains(typeof(AreaOrderCommand), types);
        foreach (var type in types)
        {
            var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            var command = (ISimCommand)ctor.Invoke(ctor.GetParameters().Select(p => Sample(p.ParameterType, defs)).ToArray());
            var bytes = CommandCodec.Encode(command, defs);
            var back = CommandCodec.Decode(bytes, defs);
            Assert.IsType(type, back);
            Assert.Equal(bytes, CommandCodec.Encode(back, defs));
        }
    }

    /// <summary>
    /// A well-formed command can still carry anything a peer cares to send: entities that are gone or
    /// never were, cells off the map, undefined enum values, missing defs, extreme numbers. Every peer
    /// applies it, so one that throws would stop the whole session. Developer commands are left out:
    /// only the host may send them.
    /// </summary>
    [Fact]
    public void HostileCommandsDoNoHarm()
    {
        var defs = TestWorld.LoadDefs();
        var sim = Simulation.CreateStandard(defs, 64, 64, 7, MapGenerator.RecommendedDepth);
        var center = MapGenerator.Generate(sim, new MapGenerator.Options { Villagers = 2 });
        sim.RebuildDerivedIfNeeded();
        VillageSetup.ApplyVillageDefaults(sim, center);
        var avatar = VillageRound.SpawnHero(sim, new Hero("Arin", null, null), center);
        sim.TickMany(SimClock.TicksPerHour);

        var rand = new Random(1234);
        var real = sim.World.Query<Thing>().Concat(sim.World.Query<Creature>()).Distinct().ToList();
        object? Hostile(Type t)
        {
            if (Nullable.GetUnderlyingType(t) is { } inner)
                return rand.Next(4) == 0 ? null : Hostile(inner);
            if (t.IsEnum)
            {
                var values = Enum.GetValues(t);
                return rand.Next(3) == 0
                    ? Enum.ToObject(t, rand.Next(-3, 300))
                    : values.GetValue(rand.Next(values.Length));
            }
            int Int() => rand.Next(6) switch { 0 => int.MinValue, 1 => int.MaxValue, 2 => -1, 3 => 0, _ => rand.Next(-100, 200) };
            if (t == typeof(bool)) return rand.Next(2) == 0;
            if (t == typeof(byte)) return (byte)rand.Next(256);
            if (t == typeof(int)) return Int();
            if (t == typeof(long)) return (long)Int();
            if (t == typeof(float)) return rand.Next(5) switch { 0 => float.NaN, 1 => float.PositiveInfinity, 2 => -1e30f, _ => (float)rand.NextDouble() * 100 - 50 };
            if (t == typeof(string)) return rand.Next(4) == 0 ? null : new string('x', rand.Next(0, 300));
            if (t == typeof(Guid)) return rand.Next(2) == 0 ? Guid.Empty : Guid.NewGuid();
            if (t == typeof(Entity))
                return rand.Next(5) switch
                {
                    0 => Entity.Null,
                    1 => new Entity(rand.Next(int.MaxValue), rand.Next(5)),
                    2 => real[rand.Next(real.Count)] with { Generation = real[rand.Next(real.Count)].Generation + 1 },
                    3 => avatar,
                    _ => real[rand.Next(real.Count)],
                };
            if (t == typeof(int2)) return new int2(Int(), Int());
            if (t == typeof(int3)) return rand.Next(3) == 0 ? new int3(Int(), Int(), Int()) : new int3(rand.Next(-2, 66), rand.Next(-2, 66), rand.Next(-1, 13));
            if (typeof(Def).IsAssignableFrom(t))
                return rand.Next(4) == 0 ? null : Sample(t, defs);
            if (t.IsArray || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)))
            {
                var element = t.IsArray ? t.GetElementType()! : t.GetGenericArguments()[0];
                if (rand.Next(5) == 0)
                    return null;
                var a = Array.CreateInstance(element, rand.Next(5) == 0 ? 4096 : rand.Next(4));
                for (var i = 0; i < a.Length; i++)
                    a.SetValue(element == typeof(string) ? (rand.Next(3) == 0 ? "NoSuchTrait" : "Optimist") : Hostile(element), i);
                return a;
            }
            return Sample(t, defs);
        }

        foreach (var type in CommandCodec.CommandTypes.Where(t => !typeof(IDevCommand).IsAssignableFrom(t)))
        {
            var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            for (var k = 0; k < 150; k++)
            {
                var sent = (ISimCommand)ctor.Invoke(ctor.GetParameters().Select(p => Hostile(p.ParameterType)).ToArray());
                // As a peer would get it: what the codec refuses never reaches the sim.
                ISimCommand command;
                try
                {
                    command = CommandCodec.Decode(CommandCodec.Encode(sent, defs), defs);
                }
                catch (InvalidDataException)
                {
                    continue;
                }
                var clock = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    sim.ApplyNow(command);
                    sim.TickMany(3);
                }
                catch (Exception e)
                {
                    Assert.Fail($"{command} threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                }
                Assert.True(clock.ElapsedMilliseconds < 2000, $"{command} took {clock.ElapsedMilliseconds} ms");
            }
        }
    }

    private sealed record ThrowingCommand : ISimCommand
    {
        public void Apply(Simulation sim) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void OutsideTestsACommandThatThrowsFailsAlone()
    {
        var sim = TestWorld.Create();
        Assert.Throws<InvalidOperationException>(() => sim.ApplyNow(new ThrowingCommand())); // tests stay strict
        sim.StrictJobs = false;
        sim.ApplyNow(new ThrowingCommand());
        sim.Enqueue(new ThrowingCommand());
        sim.Tick();
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("ThrowingCommand failed unexpectedly"));
    }

    [Fact]
    public void GarbageIsRejectedCleanly()
    {
        var defs = TestWorld.LoadDefs();
        Assert.Throws<InvalidDataException>(() => CommandCodec.Decode([0xff, 0xff, 0xff, 0x7f], defs));
        Assert.Throws<InvalidDataException>(() => CommandCodec.Decode([], defs));
        var good = CommandCodec.Encode(new AreaOrderCommand(AreaTool.Build, new int2(1, 1), new int2(2, 2), 1, 1, defs.Thing("WoodWall")), defs);
        Assert.Throws<InvalidDataException>(() => CommandCodec.Decode(good.AsSpan(0, good.Length - 3), defs));
    }

    [Fact]
    public void AreaOrdersMarkWhatTheToolsDid()
    {
        var sim = TestWorld.Create();
        sim.SpawnStructure(sim.Thing("Pine"), C(10, 10));
        sim.SpawnStructure(sim.Thing("Pine"), C(12, 11));

        sim.ApplyNow(new AreaOrderCommand(AreaTool.Chop, new int2(12, 12), new int2(9, 9), 1, sim.Map.Depth - 1));
        Assert.True((sim.Map.Designations[sim.Map.Idx(C(10, 10))] & Designation.Chop) != 0);
        Assert.True((sim.Map.Designations[sim.Map.Idx(C(12, 11))] & Designation.Chop) != 0);

        // A dragged wall box goes around the edge only.
        sim.ApplyNow(new AreaOrderCommand(AreaTool.Build, new int2(20, 20), new int2(23, 23), 1, 1, sim.Thing("WoodWall")));
        Assert.False(sim.Map.Structure[sim.Map.Idx(C(20, 20))].IsNull);
        Assert.True(sim.Map.Structure[sim.Map.Idx(C(21, 21))].IsNull);

        sim.ApplyNow(new AreaOrderCommand(AreaTool.Stockpile, new int2(30, 30), new int2(31, 31), 1, sim.Map.Depth - 1));
        Assert.Contains(sim.Map.Idx(C(30, 30)), sim.Map.ZonedCells);

        sim.ApplyNow(new AreaOrderCommand(AreaTool.Cancel, new int2(0, 0), new int2(39, 39), 1, sim.Map.Depth - 1));
        Assert.True((sim.Map.Designations[sim.Map.Idx(C(10, 10))] & Designation.Chop) == 0);
        Assert.True(sim.Map.Structure[sim.Map.Idx(C(20, 20))].IsNull, "blueprints cancelled");
    }

    [Fact]
    public void OutOfRangeAreasAreClampedOrIgnored()
    {
        var sim = TestWorld.Create();
        sim.ApplyNow(new AreaOrderCommand(AreaTool.Stockpile, new int2(-5000, -5000), new int2(5000, 5000), 1, 1));
        Assert.Equal(40 * 40, sim.Map.ZonedCells.Count);
        sim.ApplyNow(new AreaOrderCommand(AreaTool.Build, new int2(1, 1), new int2(2, 2), 99, 1, sim.Thing("WoodWall")));
        Assert.True(sim.Map.Structure[sim.Map.Idx(C(1, 1))].IsNull);
    }

    [Fact]
    public void WorkPrioritiesAndDraftingGoThroughCommands()
    {
        var sim = TestWorld.Create();
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Bea", C(5, 5), ageDays: 25 * 365);
        sim.ApplyNow(new WorkPriorityCommand(e, WorkType.Haul, 9));
        Assert.Equal(4, sim.World.Get<Mind>(e).WorkPriority[(int)WorkType.Haul]);
        sim.ApplyNow(new DraftCommand(e, true));
        Assert.True(sim.World.Get<Mind>(e).Drafted);
        sim.ApplyNow(new MoveOrderCommand(e, C(9, 5)));
        sim.TickMany(SimClock.TicksPerSecond * 10);
        Assert.Equal(C(9, 5), sim.World.Get<Position>(e).Cell);
    }
}
