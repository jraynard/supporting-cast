using SupportingCast.Sim.Components;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Ai;

/// <summary>
/// A keeper saddles their dragon (<see cref="Saddles"/>): fetches the saddle (from the ground, a
/// store, or their own hands), walks up to the dragon and buckles it on. With no saddle given, takes
/// the dragon's off into their hands (or sets it down if they're full).
/// </summary>
public sealed class SaddleJob(Entity dragon, Entity? saddle) : Job
{
    private readonly Entity _saddle = saddle ?? Entity.Null;

    public override Entity Focus => dragon;

    public const int BuckleTicks = 150, UnbuckleTicks = 90;

    protected override IEnumerable<JobStatus> Run()
    {
        var name = Get<Creature>(dragon).Name;
        if (_saddle.IsNull)
        {
            Report = $"Unsaddling {name}";
            foreach (var s in GoToEntity(dragon, PathEnd.Touch)) yield return s;
            if (HasFailed) yield break;
            foreach (var s in Wait(UnbuckleTicks)) yield return s;
            if (!Alive(dragon) || Saddles.Of(Sim, dragon).IsNull || Sim.World.Has<Mount>(dragon))
            {
                Fail("can't take the saddle off now");
                yield break;
            }
            var off = Saddles.TakeOff(Sim, dragon, placeNear: false);
            if (Get<Carrying>(Pawn).HasItem)
                Sim.PlaceItemNear(off, PawnCell);
            else
                Get<Carrying>(Pawn).Item = off;
            yield break;
        }

        Report = $"Fetching a saddle for {name}";
        if (Get<Carrying>(Pawn).Item != _saddle)
        {
            if (Get<Carrying>(Pawn).HasItem)
            {
                Fail("hands are full");
                yield break;
            }
            if (!Reserve(_saddle)) yield break;
            foreach (var s in GoTo(Sim.LocationOf(_saddle), PathEnd.OnTile)) yield return s;
            if (HasFailed) yield break;
            if (!Sim.IsAvailable(_saddle))
            {
                Fail("the saddle is gone");
                yield break;
            }
            Get<Carrying>(Pawn).Item = Sim.TakeFromStack(_saddle, 1);
        }

        Report = $"Saddling {name}";
        foreach (var s in GoToEntity(dragon, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        foreach (var s in Wait(BuckleTicks)) yield return s;
        var held = Get<Carrying>(Pawn).Item;
        if (!Alive(dragon) || held != _saddle || Saddles.CantWear(Sim, dragon, held) is { } why)
        {
            Fail($"can't saddle {name}");
            yield break;
        }
        Get<Carrying>(Pawn).Item = Entity.Null;
        Saddles.PutOn(Sim, dragon, held);
    }
}
