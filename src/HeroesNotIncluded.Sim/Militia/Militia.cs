using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Militia;

/// <summary>
/// A weapon hung on the muster rack: the town's now, paid for once. It doesn't pay again however
/// often it's taken down and hung back up, and the bandits don't count it when they size a raid
/// against the town's arms (<c>Raids.Points</c>): they size it before the town arms.
/// </summary>
public struct TownArms;

/// <summary>
/// The call to arms. When word of a raid comes in (<c>Director</c>), the town asks for weapons for
/// the folk who have none and pays the smith (or anyone) for each new one hung on the muster rack,
/// from a town purse (<see cref="Defs.SeasonDef.TownPurse"/>) until it's spent: those hours are the
/// smith's. When the bandits actually come (<see cref="Muster"/>), the town musters: anyone of the
/// village without a weapon takes one from the rack (players by hand), and when the raid's over the
/// muster stands down and the rack's left alone again.
/// The purse is the town's, held by nobody yet: it'll be the mayor's when there is one.
/// </summary>
public sealed class MilitiaSystem : ISimSystem, ISaveState
{
    public const string RackDef = "WeaponRack";

    public string Name => "Militia";
    public int Interval => SimClock.TicksPerHour / 4;

    private bool _funded;

    /// <summary>Silver left in the town purse.</summary>
    public int Coffer { get; private set; }

    /// <summary>A call to arms is out: weapons are wanted for the rack.</summary>
    public bool Open { get; private set; }

    /// <summary>The raid's here: the town arms itself from the rack (no one takes from it otherwise).</summary>
    public bool Mustering { get; private set; }

    /// <summary>Weapons the call asks for, and how many it's paid for so far.</summary>
    public int Wanted { get; private set; }
    public int Paid { get; private set; }

    /// <summary>Silver for each weapon, this call.</summary>
    public int Price { get; private set; }

    /// <summary>When the raid's expected, as the shepherd said it ("tonight").</summary>
    public string When { get; private set; } = "";

    /// <summary>The town would pay for another weapon now.</summary>
    public bool Paying => Open && Paid < Wanted && Coffer >= Price && Price > 0;

    public void Update(Simulation sim)
    {
        if (sim.Round.Live)
            Fund(sim);
    }

    /// <summary>The purse is filled once, for the round.</summary>
    private void Fund(Simulation sim)
    {
        if (_funded)
            return;
        _funded = true;
        Coffer = Math.Max(0, sim.Defs.Season.TownPurse);
    }

    public static bool IsRack(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Name == RackDef && !sim.World.Has<Construction>(e);

    public static bool IsWeapon(Simulation sim, Entity item) =>
        sim.World.IsAlive(item) && sim.World.TryGet<Thing>(item, out var t) && t.Def.Weapon != null;

    /// <summary>Folk of the village who'd fight if they had something to fight with.</summary>
    public static int Unarmed(Simulation sim)
    {
        var w = sim.World;
        var n = 0;
        foreach (var e in sim.Creatures())
            if (w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human, Dead: false } && !w.Has<NonViolent>(e)
                && !w.Has<Adventurer>(e) && (!w.TryGet<Equipment>(e, out var eq) || !w.IsAlive(eq.Weapon)))
                n++;
        return n;
    }

    /// <summary>The call goes out: so many weapons wanted, at the season's price, until the raid's over.</summary>
    public void Call(Simulation sim, string when)
    {
        var season = sim.Defs.Season;
        Fund(sim);
        Open = true;
        Paid = 0;
        Price = Math.Max(0, season.ArmsPrice);
        Wanted = Math.Min(Math.Max(0, season.ArmsWanted), Unarmed(sim));
        When = when;
        Players.WakeAll(sim); // the shepherd's news goes door to door
        var rack = sim.World.Query<Thing>().Where(e => IsRack(sim, e)).OrderBy(e => e.Index).DefaultIfEmpty(Entity.Null).First();
        int3? at = rack.IsNull ? null : sim.World.Get<Thing>(rack).Cell;
        if (Wanted == 0)
            sim.Messages.Add(MessageKind.Info, "The town musters at the rack: everyone has a weapon already.", at);
        else if (Coffer < Price)
            sim.Messages.Add(MessageKind.Warning, $"The town calls for arms, {Wanted} wanted, but the purse is empty: hang what you can spare on the muster rack.", at);
        else
            sim.Messages.Add(MessageKind.Info, $"The town will pay {Price} silver for each weapon hung on the muster rack: {Wanted} wanted.", at);
    }

    /// <summary>
    /// The bandits are here: the town takes up arms from the rack. Called or not (a raid nobody
    /// was warned of musters too, but then nothing's wanted or paid for).
    /// </summary>
    public void Muster(Simulation sim)
    {
        if (!Open)
        {
            Open = true;
            Wanted = Paid = 0;
            When = "";
        }
        Mustering = true;
    }

    /// <summary>The raid's over (or never came): the muster stands down and the rack's left alone.</summary>
    public void StandDown(Simulation sim)
    {
        if (!Open)
            return;
        Open = false;
        Mustering = false;
        sim.Messages.Add(MessageKind.Info, "The muster stands down.", null);
    }

    /// <summary>What hanging this on the rack would earn, as the work key offers it.</summary>
    public string RackLabel(Simulation sim, Entity item)
    {
        var label = sim.LabelOf(item);
        return PaysFor(sim, item) ? $"Hang the {label} on the muster rack ({Price} silver)" : $"Hang the {label} on the muster rack";
    }

    private bool PaysFor(Simulation sim, Entity item) =>
        Paying && sim.World.Has<Crafted>(item) && !sim.World.Has<TownArms>(item);

    /// <summary>
    /// A weapon's been hung on the rack by <paramref name="by"/>: it's the town's now, and if the
    /// call still wants one and it's new to the rack, its maker (or whoever brought it) is paid.
    /// </summary>
    public void Racked(Simulation sim, Entity by, Entity item)
    {
        var w = sim.World;
        if (!IsWeapon(sim, item))
            return;
        var cell = w.Get<Thing>(item).Cell;
        if (PaysFor(sim, item))
        {
            Coffer -= Price;
            Paid++;
            Inventory.Stow(sim, by, sim.CreateItem(sim.Defs.Thing(Coin.Silver), Price));
            sim.Messages.Add(MessageKind.Good, $"The town pays {Price} silver for the {sim.LabelOf(item)}. {(Paid < Wanted ? $"{Wanted - Paid} more wanted." : "That's all it asked for.")}", cell);
        }
        if (!w.Has<TownArms>(item))
            w.Add(item, new TownArms());
    }

    public void Save(SaveWriter w, Simulation sim)
    {
        w.Bool(_funded);
        w.Int(Coffer);
        w.Bool(Open);
        w.Int(Wanted);
        w.Int(Paid);
        w.Int(Price);
        w.String(When);
        w.Bool(Mustering);
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _funded = r.Bool();
        Coffer = Math.Max(0, r.Int());
        Open = r.Bool();
        Wanted = Math.Max(0, r.Int());
        Paid = Math.Max(0, r.Int());
        Price = Math.Max(0, r.Int());
        When = r.String();
        Mustering = r.Version >= 14 ? r.Bool() : Open;
    }
}

/// <summary>What the village's AI folk do in a muster (<see cref="MilitiaSystem.Mustering"/>).</summary>
public static class Muster
{
    /// <summary>Unarmed and willing, with the raid here: fetch a weapon from the rack. Null otherwise.</summary>
    public static Ai.Job? ArmFromRack(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!sim.Militia.Mustering || w.Has<NonViolent>(pawn) || w.Has<Adventurer>(pawn) || !w.Get<Creature>(pawn).Able
            || (w.TryGet<Equipment>(pawn, out var eq) && w.IsAlive(eq.Weapon))
            || (w.TryGet<Outfit>(pawn, out var outfit) && outfit.Unarmed))
            return null;
        var item = Ai.AiQueries.FindNearestItem(sim, pawn,
            (e, d) => d.Weapon != null && w.TryGet<Stored>(e, out var s) && MilitiaSystem.IsRack(sim, s.In));
        return item.IsNull ? null : new Ai.EquipJob(item);
    }

    /// <summary>
    /// The muster's over and they still hold one of the town's weapons: hang it back on the rack
    /// (the nearest with room they can reach). Null otherwise.
    /// </summary>
    public static Ai.Job? ReturnArms(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (sim.Militia.Open || !w.TryGet<Equipment>(pawn, out var eq) || !w.IsAlive(eq.Weapon) || !w.Has<TownArms>(eq.Weapon))
            return null;
        var weapon = w.Get<Thing>(eq.Weapon);
        var at = w.Get<Position>(pawn).Cell;
        var rack = w.Query<Thing>()
            .Where(e => MilitiaSystem.IsRack(sim, e) && w.Get<Thing>(e).Spawned && sim.HasRoomFor(e, weapon.Def, weapon.Material)
                && Ai.Navigation.CanReach(sim, pawn, at, w.Get<Thing>(e).Cell, PathEnd.Touch))
            .OrderBy(e => Maps.Map.Distance(at, w.Get<Thing>(e).Cell)).ThenBy(e => e.Index)
            .FirstOrDefault(Entity.Null);
        return rack.IsNull ? null : new ReturnArmsJob(rack);
    }
}

/// <summary>Hang the town's weapon in hand back on the muster rack once the muster's stood down.</summary>
public sealed class ReturnArmsJob(Entity rack) : Ai.Job
{
    public override Entity Focus => rack;

    protected override IEnumerable<Ai.JobStatus> Run()
    {
        Report = "Hanging the town's arms back up";
        foreach (var s in GoTo(Get<Thing>(rack).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        foreach (var s in Wait(45)) yield return s;
        if (!Alive(rack))
        {
            Fail("the rack's gone");
            yield break;
        }
        var w = Sim.World;
        if (!w.TryGet<Equipment>(Pawn, out var eq) || !w.IsAlive(eq.Weapon) || !w.Has<TownArms>(eq.Weapon))
            yield break;
        var t = w.Get<Thing>(eq.Weapon);
        if (!Sim.HasRoomFor(rack, t.Def, t.Material))
        {
            Fail("the rack's full");
            yield break;
        }
        w.Remove<Equipment>(Pawn);
        Sim.Store(rack, eq.Weapon);
    }
}
