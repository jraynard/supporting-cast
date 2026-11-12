using System.Reflection;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Saving;

namespace SupportingCast.Sim.Core;

/// <summary>
/// Turns <see cref="ISimCommand"/>s into bytes and back, for lockstep multiplayer (and later
/// replays). Commands are positional records, so this writes each constructor parameter in order
/// by type: no per-command code to forget. The tag is the command's index among all command types
/// sorted by name, which is stable for one build; peers only talk to identical builds.
/// </summary>
public static class CommandCodec
{
    /// <param name="Required">Reference parameters declared non-nullable: a null there is refused on decode.</param>
    private sealed record Shape(Type Type, ConstructorInfo Ctor, ParameterInfo[] Params, PropertyInfo[] Props, bool[] Required);

    private static readonly Shape[] Shapes = typeof(ISimCommand).Assembly.GetTypes()
        .Where(t => typeof(ISimCommand).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
        .OrderBy(t => t.FullName, StringComparer.Ordinal)
        .Select(ShapeOf)
        .ToArray();

    private static readonly Dictionary<Type, int> Tags = Shapes.Select((s, i) => (s.Type, i)).ToDictionary(x => x.Type, x => x.i);

    private static readonly MethodInfo ReadDef = typeof(SaveReader).GetMethod(nameof(SaveReader.DefOrNull))!;

    /// <summary>Every command type, in tag order.</summary>
    public static IEnumerable<Type> CommandTypes => Shapes.Select(s => s.Type);

    private static Shape ShapeOf(Type t)
    {
        var ctor = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var ps = ctor.GetParameters();
        var props = ps.Select(p => t.GetProperty(p.Name!) ?? throw new InvalidOperationException($"{t.Name}: no property for parameter {p.Name}")).ToArray();
        var nullability = new NullabilityInfoContext();
        var required = ps.Select(p => !p.ParameterType.IsValueType && nullability.Create(p).WriteState == NullabilityState.NotNull).ToArray();
        return new Shape(t, ctor, ps, props, required);
    }

    public static byte[] Encode(ISimCommand command, DefDatabase defs)
    {
        if (!Tags.TryGetValue(command.GetType(), out var tag))
            throw new InvalidOperationException($"{command.GetType().Name} isn't a known command");
        var shape = Shapes[tag];
        var ms = new MemoryStream();
        using (var w = new SaveWriter(ms, defs))
        {
            w.Int(tag);
            for (var i = 0; i < shape.Params.Length; i++)
                Write(w, shape.Params[i].ParameterType, shape.Props[i].GetValue(command));
        }
        return ms.ToArray();
    }

    /// <summary>Decodes a command; <see cref="InvalidDataException"/> for anything malformed (it came off the network).</summary>
    public static ISimCommand Decode(ReadOnlySpan<byte> data, DefDatabase defs)
    {
        try
        {
            using var r = new SaveReader(new MemoryStream(data.ToArray(), writable: false), defs);
            var tag = r.Int();
            if (tag < 0 || tag >= Shapes.Length)
                throw new InvalidDataException($"unknown command tag {tag}");
            var shape = Shapes[tag];
            var args = new object?[shape.Params.Length];
            for (var i = 0; i < args.Length; i++)
            {
                args[i] = Read(r, shape.Params[i].ParameterType);
                if (args[i] == null && shape.Required[i])
                    throw new InvalidDataException($"{shape.Type.Name}.{shape.Params[i].Name} can't be null");
            }
            return (ISimCommand)shape.Ctor.Invoke(args);
        }
        catch (Exception e) when (e is SaveException or EndOfStreamException or TargetInvocationException or ArgumentException or InvalidCastException)
        {
            throw new InvalidDataException($"bad command: {e.Message}", e);
        }
    }

    private static void Write(SaveWriter w, Type t, object? v)
    {
        if (Nullable.GetUnderlyingType(t) is { } inner)
        {
            w.Bool(v != null);
            if (v != null)
                Write(w, inner, v);
            return;
        }
        if (t.IsEnum)
            w.Long(Convert.ToInt64(v));
        else if (t == typeof(bool)) w.Bool((bool)v!);
        else if (t == typeof(byte)) w.Byte((byte)v!);
        else if (t == typeof(int)) w.Int((int)v!);
        else if (t == typeof(long)) w.Long((long)v!);
        else if (t == typeof(float)) w.Float((float)v!);
        else if (t == typeof(string)) w.NullableString((string?)v);
        else if (t == typeof(Guid)) w.String(((Guid)v!).ToString("N"));
        else if (t == typeof(Entity)) w.Entity((Entity)v!);
        else if (t == typeof(int2)) { var p = (int2)v!; w.Int(p.X); w.Int(p.Y); }
        else if (t == typeof(int3)) w.Int3((int3)v!);
        else if (typeof(Def).IsAssignableFrom(t)) w.Def((Def?)v);
        else if (ElementType(t) is { } element)
        {
            if (v is not System.Collections.IList list)
            {
                w.Int(-1);
                return;
            }
            w.Int(list.Count);
            foreach (var item in list)
                Write(w, element, item);
        }
        else if (ComponentCodecs.For(t) is { } codec) codec.WriteValue(w, v!);
        else throw new NotSupportedException($"Commands can't carry a {t.Name}");
    }

    private static object? Read(SaveReader r, Type t)
    {
        if (Nullable.GetUnderlyingType(t) is { } inner)
            return r.Bool() ? Read(r, inner) : null;
        if (t.IsEnum)
        {
            var value = Enum.ToObject(t, r.Long());
            // Off the network: a value the enum doesn't name would fall through every switch.
            if (!t.IsDefined(typeof(FlagsAttribute), false) && !Enum.IsDefined(t, value))
                throw new InvalidDataException($"{t.Name} {value} isn't defined");
            return value;
        }
        if (t == typeof(bool)) return r.Bool();
        if (t == typeof(byte)) return r.Byte();
        if (t == typeof(int)) return r.Int();
        if (t == typeof(long)) return r.Long();
        if (t == typeof(float)) return r.Float();
        if (t == typeof(string)) return r.NullableString();
        if (t == typeof(Guid)) return Guid.ParseExact(r.String(), "N");
        if (t == typeof(Entity)) return r.Entity();
        if (t == typeof(int2)) return new int2(r.Int(), r.Int());
        if (t == typeof(int3)) return r.Int3();
        if (typeof(Def).IsAssignableFrom(t)) return ReadDef.MakeGenericMethod(t).Invoke(r, null);
        if (ElementType(t) is { } element)
        {
            var n = r.Int();
            if (n < 0)
                return null;
            if (n > 4096)
                throw new InvalidDataException($"list of {n}");
            var array = Array.CreateInstance(element, n);
            for (var i = 0; i < n; i++)
                array.SetValue(Read(r, element), i);
            return array;
        }
        if (ComponentCodecs.For(t) is { } codec) return codec.ReadValue(r);
        throw new NotSupportedException($"Commands can't carry a {t.Name}");
    }

    /// <summary>T for T[] and IReadOnlyList&lt;T&gt; parameters (read back as arrays).</summary>
    private static Type? ElementType(Type t)
    {
        if (t.IsArray)
            return t.GetElementType();
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            return t.GetGenericArguments()[0];
        return null;
    }
}
