using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// What's under the pointer (the hover tooltip, from DrawHoverInfo): rest it on a piece of
// furniture, an item or a building a moment and the tooltip names it and says what it's for (its
// def's description), above the cursor's other notes. Only what can be seen (items
// through Perception, as picking does); a def's words are never anything hidden. Creatures have
// their names over them already.
public sealed partial class VillageGame
{
    /// <summary>Seconds the pointer rests on something before its tooltip shows.</summary>
    private const float HoverInfoAfter = 0.35f;

    private Entity _hoverInfo = Entity.Null;
    private float _hoverInfoSince;

    /// <summary>
    /// The hover tooltip: what's under the pointer (once it's rested there a moment) and then the
    /// cursor's other notes (<paramref name="notes"/>: fire, rock, a placement, dev mode's cell).
    /// </summary>
    private void DrawThingInfo(List<string> notes)
    {
        var what = HoverInfoTarget();
        if (what != _hoverInfo)
            (_hoverInfo, _hoverInfoSince) = (what, _time);
        var info = what.IsNull || _time - _hoverInfoSince < HoverInfoAfter ? null : HoverInfoText(what);
        if (info == null && notes.Count == 0)
            return;
        if (info != null)
            ImGui.SetNextWindowSize(new float2(Gui.S(300), 0), ImGuiCond.Always);
        if (!ImGui.BeginTooltip())
            return;
        if (info is { } i)
        {
            TextC(Theme.Accent, Cap(i.Name));
            if (!string.IsNullOrEmpty(i.Where))
                TextC(Theme.Dim, i.Where);
            if (!string.IsNullOrEmpty(i.About))
                WrappedC(Theme.Text, i.About);
        }
        foreach (var note in notes)
        {
            if (info != null)
                WrappedC(Theme.Dim, note);
            else
                TextC(Theme.Text, note);
        }
        ImGui.EndTooltip();
    }

    /// <summary>The furniture, item or building under the pointer, when the pointer's free to point at the world.</summary>
    private Entity HoverInfoTarget()
    {
        if (_tool != ToolKind.Select || ImGui.GetIO().WantCaptureMouse || Host.Input.MouseCaptured || (Controls.UsingPad && !PadPointing) || _pointer.Stale
            || (HasAvatar && Working) || !_sim.Map.InBounds(_hoverCell))
            return Entity.Null;
        if (!PickCreature(Host.Input.MousePosition).IsNull)
            return Entity.Null;
        // Furniture stands up out of its cell: the first piece the pointer's ray passes through, so
        // pointing at the top of a tall one names it, not the floor behind.
        if (FurnitureUnderPointer() is { IsNull: false } piece)
            return piece;
        var i = _sim.Map.Idx(_hoverCell);
        var item = _sim.Map.Item[i];
        if (!item.IsNull && View.CanSee(item))
            return item;
        if (!_sim.Map.Structure[i].IsNull)
            return _sim.Map.Structure[i];
        return Buildings.At(_sim, _hoverCell);
    }

    /// <summary>The first piece of furniture (not a wall, door or block) the pointer's ray passes through, before anything solid; Entity.Null if none.</summary>
    private Entity FurnitureUnderPointer()
    {
        var map = _sim.Map;
        var cut = Math.Min(_renderer.CutawayZ, map.Depth - 1);
        var (origin, dir) = _camera.ScreenToRay(Host.Input.MousePosition);
        var found = Entity.Null;
        bool Hits(int i)
        {
            if (i / map.LayerSize > cut)
                return false;
            if (map.IsSolid(i))
                return true;
            var s = map.Structure[i];
            if (s.IsNull || _sim.World.Has<Construction>(s) || _sim.World.Get<Thing>(s).Def is not { Building: { } b } def
                || b.IsWall || b.IsDoor || def.IsRoof || def.Shape == Sim.Defs.RenderShape.Block)
                return false;
            // Only the height it stands: a ray crossing the cell above a low stool misses it.
            var c = map.CellOf(i);
            if (!RayEntersBelow(c, c.Z + Math.Max(0.3f, WorldScale.HeightOf(def))))
                return false;
            found = s;
            return true;
        }
        bool RayEntersBelow(int3 c, float top)
        {
            // Is the ray ever inside this cell's column below the piece's top? Sampled down its height.
            for (var k = 0; k <= 8; k++)
            {
                var z = top - k * (top - c.Z) / 8f;
                if (MathF.Abs(dir.Z) < 1e-6f)
                    break;
                var t = (z - origin.Z) / dir.Z;
                var q = origin + dir * t;
                if ((int)MathF.Floor(q.X) == c.X && (int)MathF.Floor(q.Y) == c.Y)
                    return true;
            }
            return false;
        }
        map.Raycast(origin, dir, _camera.DepthRange * 3, Hits, out _, out _);
        return found;
    }

    /// <summary>A name, where it is, and what it's for; null if there's nothing to say.</summary>
    private (string Name, string? Where, string About)? HoverInfoText(Entity e)
    {
        var w = _sim.World;
        if (!w.IsAlive(e))
            return null;
        if (w.TryGet<PresetBuilding>(e, out var b))
            return (Buildings.Called(_sim, e).Replace("the ", ""), null, b.Def.Description);
        if (!w.TryGet<Thing>(e, out var t))
            return null;
        var name = w.TryGet<Stack>(e, out var s) && s.Count > 1 ? $"{s.Count} {t.Label}" : t.Label;
        var building = Buildings.Of(_sim, e);
        var where = !building.IsNull && t.Def.Building != null ? $"In {Buildings.Called(_sim, building)}" : null;
        return (name, where, t.Def.Description);
    }
}
