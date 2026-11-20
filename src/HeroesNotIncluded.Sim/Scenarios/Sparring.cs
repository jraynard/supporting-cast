using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>
/// The tutorial's fighting corner (<see cref="LessonDef.Sparring"/>): a training dummy to land a
/// first blow on, and a sparring partner who throws slow, readable punches to block, parry and dodge.
/// It's bare knuckles: the player's weapon goes in their pack. The partner is no one's
/// (<see cref="Faction.Stranger"/>): the town doesn't rush him as it would a raider, and with
/// friendly fire off the player's fists still find him (<see cref="Reach.Harms"/>).
/// </summary>
public static class Sparring
{
    public const string DummyDef = "TrainingDummy";

    /// <summary>The sparring partner, every time: a retired caravan guard who'll go a round with anyone.</summary>
    public const string PartnerName = "Hobb";

    /// <summary>How far from the player (cells) the dummy is looked for a place, at most.</summary>
    public const int Within = 12;

    /// <summary>The dummy and the partner, Entity.Null for whichever isn't out.</summary>
    public static (Entity Dummy, Entity Partner) Of(Simulation sim)
    {
        var w = sim.World;
        var dummy = Entity.Null;
        foreach (var e in w.Each<Thing>())
            if (w.Get<Thing>(e) is { Spawned: true } t && t.Def.Name == DummyDef)
            {
                dummy = e;
                break;
            }
        var partner = Entity.Null;
        foreach (var e in sim.Creatures())
            if (w.Get<Creature>(e) is { Faction: Faction.Stranger, Dead: false } c && c.Name == PartnerName)
            {
                partner = e;
                break;
            }
        return (dummy, partner);
    }

    /// <summary>
    /// The lesson's over: the partner drops his fists, says so, and spars no more (he stays by the
    /// dummy, to be set up again). Nothing if he isn't sparring.
    /// </summary>
    public static void StandDown(Simulation sim)
    {
        var partner = Of(sim).Partner;
        if (partner.IsNull || sim.World.Get<Mind>(partner).Job is not SparJob)
            return;
        AiSystem.EndJob(sim, partner, sim.World.Get<Mind>(partner), true);
        Barks.Say(sim, partner, "spar_done", Volume.Say, "That'll do. Get your breath.");
    }

    /// <summary>
    /// Set the corner out near <paramref name="player"/>: any earlier dummy and partner gone, the
    /// player whole, fists down and their weapon in their pack, a dummy on open ground nearby and the
    /// partner a few steps past it, waiting for them.
    /// </summary>
    public static void SetUp(Simulation sim, Entity player)
    {
        var w = sim.World;
        if (!w.IsAlive(player) || !sim.Defs.TryThing(DummyDef, out var def))
            return;
        var (oldDummy, oldPartner) = Of(sim);
        if (!oldPartner.IsNull)
            sim.Depart(oldPartner);
        if (!oldDummy.IsNull)
            sim.DestroyThing(oldDummy);
        w.FlushDeferred();

        Arena.Freshen(sim, player);
        Inventory.Unequip(sim, player, GearSlot.Weapon); // bare knuckles
        if (w.Has<FreeMover>(player))
            w.Get<FreeMover>(player).Drawn = false;

        var here = w.Get<Position>(player).Cell;
        // Out in the open, with room round it to circle, and a way to it from where the player stands.
        bool Open(int3 c) => sim.Map.InBounds(c) && !sim.Map.HasRoof[sim.Map.Idx(c)] && sim.Map.Passable(c);
        var spot = sim.SpiralFrom(here, Within).Where(c => c.Z == here.Z && Maps.Map.Distance(c, here) >= 3
                && sim.CanPlaceStructure(c, def) && sim.Map.Standable(sim.Map.Idx(c))
                && Open(c + new int3(1, 0, 0)) && Open(c + new int3(-1, 0, 0)) && Open(c + new int3(0, 1, 0)) && Open(c + new int3(0, -1, 0))
                && sim.Regions.CanReach(here, c, PathEnd.Touch))
            .Cast<int3?>().FirstOrDefault();
        if (spot is not { } at)
            return;
        sim.SpawnStructure(def, at);

        var stand = sim.SpiralFrom(at, 6).Where(c => c.Z == at.Z && Maps.Map.Distance(c, at) >= 3 && Open(c)
                && sim.Regions.CanReach(c, here, PathEnd.Touch))
            .OrderByDescending(c => Maps.Map.Distance(c, here)).Cast<int3?>().FirstOrDefault() ?? here;
        var partner = sim.SpawnCreature(sim.Defs.Creature("Human"), PartnerName, stand, Faction.Stranger, ageDays: 45 * 365);
        Array.Fill(w.Get<Needs>(partner).Values, 1f);
        if (w.TryGet<Skills>(partner, out var skills))
            skills.Level[(int)Skill.Melee] = Math.Max(skills.Level[(int)Skill.Melee], Skills.Average);
        AiSystem.Order(sim, partner, new SparJob(player));
        Barks.Say(sim, partner, "spar_hello", Volume.Say, "Hit the dummy first. Then me, if you're feeling brave.");
        sim.Messages.Add(MessageKind.Info, $"{PartnerName} has set out a training dummy, and waits by it.", at);
    }
}

/// <summary>
/// A sparring partner's bout with <paramref name="player"/> (<see cref="Sparring"/>): he waits till
/// they come close with their fists up (plain to see), then circles and throws a punch at a steady,
/// readable pace, never quickly. He stops, with a word, once either of them has had enough, and
/// stands down while they walk away or lower their fists.
/// </summary>
public sealed class SparJob(Entity player) : Job
{
    /// <summary>Ticks between his punches: long enough to read each one and answer it.</summary>
    public const int Between = SimClock.TicksPerSecond * 5 / 2;

    /// <summary>How close (cells) the player must come, fists up, for him to square up.</summary>
    public const float SquareUp = 3.5f;

    public override Entity Focus => player;

    protected override IEnumerable<JobStatus> Run()
    {
        var fists = Get<Creature>(Pawn).Def.Attacks.FirstOrDefault(a => !a.Ranged);
        if (fists == null)
        {
            Fail("nothing to spar with");
            yield break;
        }
        Report = "Sparring";
        var ready = false;
        var next = 0L;
        while (Alive(player) && Get<Creature>(Pawn).Able)
        {
            if (!Fit(Pawn) || !Fit(player))
            {
                Combat.FreeFight.Leave(Sim, Pawn);
                Barks.Say(Sim, Pawn, "spar_done", Volume.Say, "That'll do. Get your breath.");
                yield break;
            }
            var me = Get<Position>(Pawn).Pos;
            var them = Get<Position>(player).Pos;
            var dist = MathF.Sqrt((them.X - me.X) * (them.X - me.X) + (them.Y - me.Y) * (them.Y - me.Y));
            var up = Sim.World.TryGet<FreeMover>(player, out var fm) && fm.Drawn;
            if (!ready)
            {
                // Waiting by the dummy, facing the player, till they come with their fists up.
                if (up && dist <= SquareUp && Get<Position>(player).Cell.Z == PawnCell.Z)
                {
                    ready = true;
                    next = Sim.Clock.Tick + Between;
                    Combat.FreeFight.Join(Sim, Pawn);
                    Barks.Say(Sim, Pawn, "spar_ready", Volume.Say, "Fists up, then.");
                }
                yield return JobStatus.Running;
                continue;
            }
            if (!up || dist > SquareUp * 3f)
            {
                // Fists down, or walked off: he stands down and waits for them again.
                Combat.FreeFight.Leave(Sim, Pawn);
                ready = false;
                yield return JobStatus.Running;
                continue;
            }
            Combat.FreeFight.Steer(Sim, Pawn, player, fists, pressing: true, backOffUntil: 0);
            var now = Sim.Clock.Tick;
            if (now >= next && !Sim.World.Has<Windup>(Pawn) && Combat.FreeFight.InReach(Sim, Pawn, player, fists, dist)
                && Blows.Begin(Sim, Pawn, player, fists))
                next = now + Between;
            yield return JobStatus.Running;
        }
    }

    /// <summary>Not yet hurt enough to call it a day (a bar fight's measure, <see cref="Hospitality.Brawls.EnoughDamage"/>).</summary>
    private bool Fit(Entity e) =>
        Get<Creature>(e).Able && (!Sim.World.TryGet<Health>(e, out var h) || h.Injuries.Sum(i => i.Damage) < Hospitality.Brawls.EnoughDamage);

    protected override void OnEnd(bool succeeded) => Combat.FreeFight.Leave(Sim, Pawn);
}
