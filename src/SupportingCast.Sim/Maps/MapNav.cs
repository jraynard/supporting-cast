using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Defs;

namespace SupportingCast.Sim.Maps;

/// <summary>
/// The village <see cref="Map"/> as Narsil's <see cref="INavGrid"/>. A struct so the generic pathing
/// code is compiled for it and these forwards inline (see <see cref="INavGrid"/>).
/// </summary>
public readonly struct MapNav(Map map) : INavGrid
{
    public Map Map => map;
    public int Width => map.Width;
    public int Height => map.Height;
    public int Depth => map.Depth;
    public float[] PathCost => map.PathCost;
    public ClimbKind[] Climb => map.Climb;
    public int[] Region => map.Region;
    public int StructureVersion => map.StructureVersion;
    public bool IsSolid(int idx) => map.IsSolid(idx);
    public bool CoveredAbove(int idx) => map.CoveredAbove(idx);
}
