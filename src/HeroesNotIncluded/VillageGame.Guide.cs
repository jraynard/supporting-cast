using Brutal.ImGuiApi;
using Narsil;
using Narsil.Platform;
using Narsil.Rendering;
using Narsil.Ui;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// Guide highlights (Settings.GuideHighlights): a glow round the button for what needs doing now, so
// a player new to a trade sees where to go. Playtesters with a patient waiting didn't know the gift
// was where the healing was. And lay on hands as an ability: click it, then click someone.
// What the guide knows comes through View (Perception): a collapse is plain to see, a healing
// asked for is known to those who heard it, and your own hurts you feel.
public sealed partial class VillageGame
{
    private bool Guiding => Settings.Current.GuideHighlights && HasAvatar && InRound;

    /// <summary>A pulsing glow round the last item drawn, when <paramref name="when"/> and guiding.</summary>
    private void GuideGlow(bool when)
    {
        if (!when || !Guiding)
            return;
        var pad = new float2(Gui.S(3), Gui.S(3));
        var min = ImGui.GetItemRectMin() - pad;
        var max = ImGui.GetItemRectMax() + pad;
        var pulse = 0.5f + 0.5f * MathF.Sin(_time * 5f);
        var dl = ImGui.GetForegroundDrawList(ImGui.GetMainViewport());
        dl.AddRect(in min, in max, Theme.U8(Theme.Accent, 0.45f + 0.55f * pulse), Gui.S(4), ImDrawFlags.None, Gui.S(2f + pulse));
    }

    /// <summary>Someone the healer can see lying collapsed, or a healing asked for that they know of: work for the gift.</summary>
    private bool PatientsWaiting()
    {
        if (!HasAvatar)
            return false;
        foreach (var e in View.Creatures())
            if (e != _avatar && _sim.World.TryGet<Creature>(e, out var c) && c.Downed && !c.Dead && _sim.World.Has<Health>(e))
                return true;
        return View.Orders().Any(o => o.Kind == Sim.Defs.ServiceKind.Healing && o.Status == OrderStatus.Open && !o.Mine);
    }

    /// <summary>Wounds you carry, and the mana to weave for them.</summary>
    private bool OwnWoundsToWeave() =>
        HasAvatar && View.FeltWounds(_avatar) is { Count: > 0 } && CastSession.Problem(_sim, _avatar, 0) == null;

    /// <summary>The gift wants opening: patients waiting, or your own wounds to mend.</summary>
    private bool GiftWanted() =>
        Guiding && _sim.World.Has<Mana>(_avatar) && (PatientsWaiting() || OwnWoundsToWeave());

    // ---------------- Lay on hands, aimed ----------------

    /// <summary>The ability's armed: the next click on someone lays hands on them.</summary>
    private bool _aimingHands;

    /// <summary>Who you're walking over to lay hands on, once you can touch them; and for how long you've been at it.</summary>
    private Entity _handsFor = Entity.Null;
    private float _handsForAge;
    private int3 _handsForCell;

    /// <summary>Arm (or disarm) the ability.</summary>
    private void AimHands()
    {
        _aimingHands = !_aimingHands;
        if (_aimingHands)
            Notify(MessageKind.Info, "Lay on hands: click someone (right-click or Esc to stop).");
    }

    /// <summary>With the ability armed, a click on someone lays hands on them, walking over first if need be. Returns whether it took the input.</summary>
    private bool HandleHandsAim(Input input)
    {
        if (!_aimingHands)
            return false;
        if (input.WasMousePressed(MouseButton.Right) || BackPressed() || !Empathy.Gifted(_sim, _avatar))
        {
            _aimingHands = false;
            return true;
        }
        if (!input.WasMousePressed(MouseButton.Left))
            return false;
        var patient = PickCreature(input.MousePosition);
        if (patient.IsNull || patient == _avatar)
        {
            Notify(MessageKind.Info, patient.IsNull ? "Click on someone to lay hands on them." : "You already know your own hurts: weave for them in the gift.");
            return true;
        }
        _aimingHands = false;
        _selected = patient;
        LayHandsOn(patient);
        return true;
    }

    private void LayHandsOn(Entity patient)
    {
        var problem = HandsOnSession.Problem(_sim, _avatar, patient);
        if (problem == null)
        {
            Issue(new FeelCommand(_avatar, patient));
            _handsFor = Entity.Null;
            return;
        }
        if (problem != "you need to be able to touch them")
        {
            Notify(MessageKind.Info, $"Can't lay on hands: {problem}.");
            return;
        }
        _handsForCell = _sim.World.Get<Position>(patient).Cell;
        Issue(new AvatarGoToCommand(_avatar, _handsForCell));
        (_handsFor, _handsForAge) = (patient, 0f);
    }

    /// <summary>Walking over to someone to lay hands on them: the hands go on once you can touch them; after them if they move.</summary>
    private void UpdateHandsFor(float dt)
    {
        if (_handsFor.IsNull)
            return;
        var w = _sim.World;
        if (!HasAvatar || Working || !w.IsAlive(_handsFor) || w.Get<Creature>(_handsFor).Dead)
        {
            _handsFor = Entity.Null;
            return;
        }
        _handsForAge += dt;
        if (HandsOnSession.Problem(_sim, _avatar, _handsFor) == null)
        {
            Issue(new FeelCommand(_avatar, _handsFor));
            _handsFor = Entity.Null;
            return;
        }
        var cell = w.Get<Position>(_handsFor).Cell;
        if (cell != _handsForCell)
        {
            _handsForCell = cell;
            Issue(new AvatarGoToCommand(_avatar, cell)); // they've moved: after them
        }
        // Stopped short (the walk ended or was taken over), or it's taking far too long: let it go.
        else if (_handsForAge > 1f && w.Get<Mind>(_avatar).Job is not Sim.Systems.AvatarWalkJob || _handsForAge > 30f)
            _handsFor = Entity.Null;
    }

    /// <summary>With the ability armed: a ring on whoever's under the cursor (drawn with the world).</summary>
    private void DrawHandsAimRing()
    {
        if (!_aimingHands || !HasAvatar)
            return;
        var target = PickCreature(Host.Input.MousePosition);
        if (target.IsNull || target == _avatar)
            return;
        var ring = Host.Atlas.Get("ui/selection");
        _renderer.Decal(ring, _renderer.DrawPos(_sim, target, _sim.World.Get<Position>(target).Pos) + new float3(0, 0, 0.05f), 1f,
            Rgba.Pack(0.66f, 0.58f, 1f, 0.9f), Quad.Unlit | Quad.Always, translucent: true);
    }

    /// <summary>With the ability armed: what a click will do, by the cursor.</summary>
    private void DrawHandsAimHint()
    {
        if (!_aimingHands || !HasAvatar)
            return;
        var mouse = Host.Input.MousePosition;
        var target = PickCreature(mouse);
        var dl = ImGui.GetForegroundDrawList(ImGui.GetMainViewport());
        var at = mouse + new float2(Gui.S(16), Gui.S(10));
        var text = target.IsNull || target == _avatar ? "Lay on hands: click someone" : $"Lay hands on {_sim.World.Get<Creature>(target).Name}";
        dl.AddText(in at, Theme.U8(Theme.Arcane), text);
    }
}
