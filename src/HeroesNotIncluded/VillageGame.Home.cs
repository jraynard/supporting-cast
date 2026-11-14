using Brutal.Numerics;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded;

// Your home, marked: the deal gives you a cottage bed (Systems/Homes), and a house over its roof
// shows which, from anywhere in town. It goes while you're inside. Your own bed is yours to know,
// so this reads it directly; nobody else's home is marked.
public sealed partial class VillageGame
{
    /// <summary>Where the home marker floats, for the renderer: over the middle of your cottage's roof (or your room at the inn).</summary>
    private void UpdateHomeMark()
    {
        _renderer.HomeMark = HomeMark();
    }

    private float3? HomeMark()
    {
        if (!HasAvatar || !InRound || !_sim.World.TryGet<BedOwner>(_avatar, out var own) || !_sim.World.IsAlive(own.Bed))
            return null;
        var w = _sim.World;
        var home = Homes.HomeOf(_sim, _avatar);
        if (home.IsNull || Buildings.At(_sim, w.Get<Position>(_avatar).Cell) == home)
            return null;
        var b = w.Get<PresetBuilding>(home);
        // A cottage is all yours: mark its middle. A room at the inn: over the bed.
        var at = b.Def.Home ? b.Anchor : w.Get<Thing>(own.Bed).Cell;
        // Clear of the ridge (a pitched roof rises well over its eaves), or the thatch hides it.
        return new float3(at.X + 0.5f, at.Y + 0.5f, b.Anchor.Z + b.Def.Storeys + 1.6f);
    }
}
