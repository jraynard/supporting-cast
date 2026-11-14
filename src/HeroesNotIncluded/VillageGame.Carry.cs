using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

/// <summary>
/// Moving a pack item by hand: a click on its tile picks it up (only in the UI: the sim knows
/// nothing of it), the places it could go light up, and a click on one sends the command that
/// puts it there. A click anywhere else, or a right click, puts it back.
/// </summary>
public sealed partial class VillageGame
{
    /// <summary>The pack item picked up to move, if any. Read through <see cref="CarriedItem"/>.</summary>
    private Entity _carried = Entity.Null;

    /// <summary>Something this frame took the click that picked up or put down <see cref="_carried"/>.</summary>
    private bool _carryHandled;

    /// <summary>The item picked up, while it's still in your pack and a window showing it is open.</summary>
    private Entity CarriedItem =>
        !_carried.IsNull && HasAvatar && (_inventoryOpen || !_openStorage.IsNull)
        && _sim.World.IsAlive(_carried) && Inventory.InOwnPack(_sim, _avatar, _carried)
            ? _carried : Entity.Null;

    /// <summary>Pick up a pack item to move (or, with null, put back what's picked up).</summary>
    private void PickUp(Entity item)
    {
        _carried = item;
        _carryHandled = true;
    }

    /// <summary>Put what's picked up where the command says.</summary>
    private void PutCarried(ISimCommand command)
    {
        _carried = Entity.Null;
        _carryHandled = true;
        Issue(command);
    }

    /// <summary>Light a slot the carried item can go in, brighter under the pointer.</summary>
    private static void MarkDropTarget(float2 min, float2 box, bool hovered)
    {
        var dl = ImGui.GetWindowDrawList();
        var max = min + box;
        if (hovered)
            dl.AddRectFilled(in min, in max, Theme.U8(Theme.Good, 0.18f), Gui.S(4), ImDrawFlags.None);
        dl.AddRect(in min, in max, Theme.U8(Theme.Good, hovered ? 1f : 0.6f), Gui.S(4), ImDrawFlags.None, Gui.S(2));
    }

    /// <summary>
    /// After every window's drawn: a click nothing took puts the item back; otherwise it follows
    /// the pointer, and the world doesn't get the mouse (a click to put it down mustn't walk you).
    /// </summary>
    private void DrawCarried()
    {
        var handled = _carryHandled;
        _carryHandled = false;
        var item = CarriedItem;
        if (item.IsNull)
        {
            _carried = Entity.Null;
            return;
        }
        // Released, not clicked: the slots' buttons act on the release, so a press must not undo it first.
        if (!handled && (ImGui.IsMouseReleased(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right, false)))
        {
            _carried = Entity.Null;
            return;
        }
        ImGui.SetNextFrameWantCaptureMouse(true);
        if (Controls.UsingPad)
            return; // its tile is outlined; the pointer isn't where the pad's focus is
        var icon = Gui.S(IconSize);
        var (sprite, tint) = _renderer.ArtOf(_sim.World.Get<Thing>(item));
        var at = ImGui.GetMousePos() - new float2(icon * 0.5f, icon * 0.5f);
        DrawSprite(ImGui.GetForegroundDrawList(ImGui.GetMainViewport()), sprite, at, new float2(icon, icon), Color8(tint));
    }
}
