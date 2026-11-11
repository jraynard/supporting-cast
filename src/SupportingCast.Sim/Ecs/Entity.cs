namespace SupportingCast.Sim.Ecs;

/// <summary>
/// Handle to an entity. The generation detects stale handles after an index is recycled.
/// </summary>
public readonly record struct Entity(int Index, int Generation)
{
    public static readonly Entity Null = new(-1, 0);
    public bool IsNull => Index < 0;
    public override string ToString() => IsNull ? "Entity(null)" : $"Entity({Index}v{Generation})";
}
