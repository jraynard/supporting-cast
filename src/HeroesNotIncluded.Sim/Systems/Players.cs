using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;

namespace HeroesNotIncluded.Sim.Systems;

/// <summary>
/// Co-op players and their characters. A player is a stable id (from their machine); their
/// character carries <see cref="PlayerCharacter"/> for good and <see cref="Avatar"/> only while
/// they're connected, so leaving turns them into an ordinary villager and coming back hands
/// control straight back. Everything here runs from lockstep turn events or commands, so every
/// peer does it at the same tick.
/// </summary>
public static class Players
{
    /// <summary>Longest character name accepted from the network.</summary>
    public const int MaxNameLength = 24;

    /// <summary>The player's living character, or null.</summary>
    public static Entity CharacterOf(Simulation sim, Guid player)
    {
        var w = sim.World;
        foreach (var e in w.Each<PlayerCharacter>())
            if (w.Get<PlayerCharacter>(e).PlayerId == player && w.TryGet<Creature>(e, out var c) && !c.Dead)
                return e;
        return Entity.Null;
    }

    /// <summary>
    /// Every character a player is steering right now is asleep (and there's at least one), or no
    /// player is alive at all and the dead are watching. While that's so the host runs time fast,
    /// so nobody watches their character sleep, or a table of ghosts the season play out; anyone
    /// awake holds it at the usual speed. A sim read, so every peer agrees on it. Someone downed
    /// isn't asleep.
    /// </summary>
    public static bool AllAsleep(Simulation sim)
    {
        var w = sim.World;
        var any = false;
        foreach (var e in w.Each<Avatar>())
        {
            if (w.Get<Creature>(e).Dead)
                continue;
            if (!w.Get<Mind>(e).Asleep)
                return false;
            any = true;
        }
        return any || Afterlife.Ghosts.Any(sim);
    }

    /// <summary>The player a character (or a ghost) belongs to, if any.</summary>
    public static Guid? OwnerOf(Simulation sim, Entity e) =>
        !sim.World.IsAlive(e) ? null
        : sim.World.TryGet<PlayerCharacter>(e, out var p) ? p.PlayerId
        : sim.World.TryGet<Afterlife.Ghost>(e, out var g) ? g.PlayerId
        : null;

    /// <summary>
    /// A player arrives. If they have a character, it becomes theirs to steer again (true);
    /// otherwise they need to make one (<see cref="CreateCharacterCommand"/>).
    /// <paramref name="adoptLooseAvatar"/>: the host claims a single-player save's avatar.
    /// </summary>
    public static bool Join(Simulation sim, Guid player, bool adoptLooseAvatar = false)
    {
        var w = sim.World;
        var me = CharacterOf(sim, player);
        if (me.IsNull && Afterlife.Ghosts.Of(sim, player) is { IsNull: false } ghost)
        {
            w.Get<Afterlife.Ghost>(ghost).Away = false; // back among the dead
            return true;
        }
        if (me.IsNull && adoptLooseAvatar)
        {
            foreach (var e in w.Query<Avatar>())
            {
                if (w.Has<PlayerCharacter>(e) || w.Get<Creature>(e).Dead)
                    continue;
                w.Add(e, new PlayerCharacter { PlayerId = player });
                me = e;
                break;
            }
        }
        if (me.IsNull)
            return false;
        if (!w.Has<Avatar>(me))
        {
            var mind = w.Get<Mind>(me);
            // Whatever the brain was doing, the player decides now; keep what's in their hands.
            AiSystem.EndJob(sim, me, mind, false);
            mind.Drafted = false;
            if (w.Has<Request>(me))
                w.Remove<Request>(me);
            w.Add(me, new Avatar());
            sim.Messages.Add(MessageKind.Info, $"{w.Get<Creature>(me).Name} is back.", w.Get<Position>(me).Cell);
        }
        return true;
    }

    /// <summary>A player leaves: their character carries on as a villager until they return.</summary>
    public static void Leave(Simulation sim, Guid player)
    {
        var w = sim.World;
        var me = CharacterOf(sim, player);
        if (me.IsNull && Afterlife.Ghosts.Of(sim, player) is { IsNull: false } ghost)
            w.Get<Afterlife.Ghost>(ghost).Away = true; // called back while gone, they come by themselves
        if (me.IsNull || !w.Has<Avatar>(me))
            return;
        w.Remove<Avatar>(me);
        Work.WorkSystem.Stop(sim, me, null);
        // Ending the avatar's job as a villager also sets down what it carried,
        // where the village will tend it.
        AiSystem.EndJob(sim, me, w.Get<Mind>(me), false);
        sim.Messages.Add(MessageKind.Info, $"{w.Get<Creature>(me).Name} carries on with village life for now.", w.Get<Position>(me).Cell);
    }

    /// <summary>
    /// A player's connection dropped (their seat is held while they reconnect): let go of every
    /// held key, so their character stands still instead of walking or working on unattended.
    /// </summary>
    public static void Away(Simulation sim, Guid player)
    {
        var w = sim.World;
        var me = CharacterOf(sim, player);
        if (me.IsNull || !w.Has<Avatar>(me))
            return;
        ref var avatar = ref w.Get<Avatar>(me);
        avatar.WorkHeld = false;
        avatar.Chain = AvatarActionKind.None;
        if (w.Has<Combat.FreeMover>(me))
            w.Get<Combat.FreeMover>(me).Wish = float2.Zero;
        Work.WorkSystem.Stop(sim, me, null); // no one is there to play it
    }

    /// <summary>Hands every connected character except <paramref name="keep"/>'s back to the AI (a save hosted again).</summary>
    public static void ReleaseAllBut(Simulation sim, Guid? keep)
    {
        var w = sim.World;
        foreach (var e in w.Query<PlayerCharacter>())
            if (w.Get<PlayerCharacter>(e).PlayerId != keep && w.Has<Avatar>(e))
                Leave(sim, w.Get<PlayerCharacter>(e).PlayerId);
    }

    /// <summary>Where newcomers turn up: by the wagon or another player, else the middle of the map.</summary>
    public static int3 Home(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Each<Thing>())
            if (w.Get<Thing>(e) is { Spawned: true, Def.Name: "Wagon" } wagon)
                return wagon.Cell;
        foreach (var e in w.Each<PlayerCharacter>())
            if (w.TryGet<Creature>(e, out var c) && !c.Dead)
                return w.Get<Position>(e).Cell;
        foreach (var e in sim.Creatures())
            if (w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human, Dead: false })
                return w.Get<Position>(e).Cell;
        var map = sim.Map;
        return new int3(map.Width / 2, map.Height / 2, Math.Max(1, map.SurfaceZ(map.Width / 2, map.Height / 2)));
    }

    /// <summary>
    /// Make a player's character: a young villager at <see cref="Home"/>. Does nothing if they
    /// already have one.
    /// </summary>
    public static Entity CreateCharacter(Simulation sim, Guid player, string name, Appearance? look, IReadOnlyList<string>? traits,
        IReadOnlyList<string>? clothes = null)
    {
        var w = sim.World;
        if (!CharacterOf(sim, player).IsNull)
            return Entity.Null;
        name = Clean(name);
        var me = SpawnNewcomer(sim, name);
        var cell = w.Get<Position>(me).Cell;
        w.Add(me, new PlayerCharacter { PlayerId = player });
        w.Add(me, new Avatar());
        sim.GiveStartingPack(me);
        if (look is { } a)
            w.Add(me, Valid(a));
        if (traits != null)
            TraitRules.Set(sim, me, traits.Take(TraitRules.MaxTraits * 2));

        Clothing.DressHero(sim, me, clothes); // last, so it shifts no other entity

        sim.Messages.Add(MessageKind.Good, $"{name} has come to work in the village.", cell);
        return me;
    }

    /// <summary>A young villager at <see cref="Home"/>, not yet anyone's.</summary>
    internal static Entity SpawnNewcomer(Simulation sim, string name)
    {
        var home = Home(sim);
        var cell = sim.SpiralFrom(home + new int3(1, 2, 0), 10)
            .FirstOrDefault(c => sim.Map.Passable(c) && sim.Map.Item[sim.Map.Idx(c)].IsNull && sim.Map.Structure[sim.Map.Idx(c)].IsNull, home);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), name, cell, ageDays: 19 * 365f);
        Array.Fill(sim.World.Get<Needs>(me).Values, 0.85f);
        return me;
    }

    /// <summary>
    /// A player takes over a villager the AI was running. <see cref="Guid.Empty"/> is single
    /// player: the character stays unowned, so hosting that save later can adopt it (<see cref="Join"/>).
    /// </summary>
    internal static void Inhabit(Simulation sim, Guid player, Entity villager)
    {
        var w = sim.World;
        var mind = w.Get<Mind>(villager);
        AiSystem.EndJob(sim, villager, mind, false);
        mind.Drafted = false;
        mind.Asleep = false;
        if (w.Has<Request>(villager))
            w.Remove<Request>(villager);
        if (player != Guid.Empty)
            w.Add(villager, new PlayerCharacter { PlayerId = player });
        w.Add(villager, new Avatar());
        sim.GiveStartingPack(villager);
    }

    /// <summary>At the start of a round, before anyone has met them: a villager becomes the character a player made.</summary>
    internal static void TakeOnLook(Simulation sim, Entity e, string name, Appearance? look, IReadOnlyList<string>? traits, IReadOnlyList<string>? clothes)
    {
        var w = sim.World;
        w.Get<Creature>(e).Name = Clean(name);
        if (look is { } a)
        {
            if (w.Has<Appearance>(e))
                w.Remove<Appearance>(e);
            w.Add(e, Valid(a));
        }
        if (traits is { Count: > 0 })
            TraitRules.Set(sim, e, traits.Take(TraitRules.MaxTraits * 2));
        Clothing.DressHero(sim, e, clothes);
    }

    internal static string Clean(string name)
    {
        var s = new string((name ?? "").Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (s.Length > MaxNameLength)
            s = s[..MaxNameLength].Trim();
        return s.Length == 0 ? "Wanderer" : s;
    }

    // A look from the network: keep only values the renderer knows.
    internal static Appearance Valid(Appearance a) => a with
    {
        Body = Enum.IsDefined(a.Body) ? a.Body : default,
        Hair = Enum.IsDefined(a.Hair) ? a.Hair : default,
        Beard = Enum.IsDefined(a.Beard) ? a.Beard : default,
        Eyes = Enum.IsDefined(a.Eyes) ? a.Eyes : default,
        Stance = Enum.IsDefined(a.Stance) ? a.Stance : default,
        Folk = Enum.IsDefined(a.Folk) ? a.Folk : default,
        HairColor = a.HairColor & 0xFFFFFF,
        EyeColor = a.EyeColor & 0xFFFFFF,
        SkinColor = a.SkinColor & 0xFFFFFF,
        Model = Appearance.ValidModel(a.Model),
    };
}

/// <summary>A newly joined player makes their character (from the character creator).</summary>
public sealed record CreateCharacterCommand(Guid Player, string Name, Appearance Look, IReadOnlyList<string> Traits,
    IReadOnlyList<string>? Clothes = null)
    : ISimCommand, IPlayerCommand
{
    public Guid? Owner => Player;

    public void Apply(Simulation sim) => Players.CreateCharacter(sim, Player, Name, Look, Traits, Clothes);
}
