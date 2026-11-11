using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Scenarios;

/// <summary>Escape's camp on this map: where the cage and its gate are, the dragon's chains, the confiscated gear.</summary>
public sealed class EscapeState
{
    public int3 Gate;

    /// <summary>The cage's wall ring (inclusive); inside it is one cell in from each side.</summary>
    public int3 CageMin, CageMax;

    /// <summary>Where the dragon is chained (and is chained again after a recapture).</summary>
    public int3 DragonSpot;

    /// <summary>The chest holding the player's confiscated gear.</summary>
    public Entity Chest = Entity.Null;

    /// <summary>The camp's reinforcements have been sent for in this fight.</summary>
    public bool Reinforced;

    /// <summary>Who has the chain key on them (the jailer, to start with); Entity.Null once it's dropped. It falls at their feet when they go down.</summary>
    public Entity KeyHolder = Entity.Null;

    /// <summary>No supper round before this tick: after a recapture, the camp waits for the next evening.</summary>
    public long NoSupperBefore;

    public int3 CageCenter => new((CageMin.X + CageMax.X) / 2, (CageMin.Y + CageMax.Y) / 2, CageMin.Z);

    public bool InCage(int3 c) => c.Z == CageMin.Z && c.X > CageMin.X && c.X < CageMax.X && c.Y > CageMin.Y && c.Y < CageMax.Y;
}

/// <summary>
/// Escape's opening (combat on): a dragon rider, separated from their squadron, held in a
/// captors' camp, their dragon in chains across it and their gear in a chest. Each evening the
/// jailer opens the cage to bring supper: F on him then knocks him down and takes his club and key, and
/// the alarm turns the captors into raiders (turn-based combat from there). Free the dragon with
/// the key (F, or Unchain in a fight), then fly off from beside it before the camp's
/// reinforcements arrive. Downed in the attempt, you wake up back in the cage with the dragon
/// chained again, and another evening to try. The flight lands in the wild (<see cref="Travel"/>).
/// Placeholder story: the words here are stand-ins.
/// </summary>
public static class EscapeCamp
{
    public const string JailerName = "Varrick";
    // Two guards besides the jailer, all hand-to-hand: you break out with only his club, so bows
    // picking you off across the camp made the run for the dragon too hard.
    private static readonly string[] GuardNames = ["Hesk", "Dunmore"];
    private static readonly string[] GuardWeapons = ["ShortSword", "Club"];

    /// <summary>The jailer's supper round: the gate stands open from this hour...</summary>
    public const float SupperHour = 19f;

    /// <summary>...until this one, when he locks up again.</summary>
    public const float LockUpHour = 20.5f;

    /// <summary>The game starts this late on day 1, so the first chance comes within the hour or so.</summary>
    public const float StartHour = 18.25f;

    /// <summary>Combat rounds after the alarm before the camp's reinforcements walk in.</summary>
    public const int ReinforcementRound = 6;

    /// <summary>What's taken from you, in the chest: weapons, armor, your pack, a little food.</summary>
    private static readonly (string Def, int Count)[] Confiscated = [("ShortSword", 1), ("Bow", 1), ("LeatherJerkin", 1), ("Satchel", 1), ("Bandage", 6), ("Berries", 12), ("StoneAxe", 1), ("StonePick", 1)];

    public static EscapeState? Of(Simulation sim) => sim.Story.Escape;

    /// <summary>Generate the map, build the camp, and put the player in its cage. Returns the avatar.</summary>
    public static Entity Setup(Simulation sim, Hero hero, string? difficulty)
    {
        var center = MapGenerator.Generate(sim, new MapGenerator.Options { Villagers = 0, SpawnStartingKit = false });
        sim.RebuildDerivedIfNeeded();
        sim.Clock.Tick = SimClock.HoursToTicks(StartHour - sim.Clock.StartHour);
        sim.Story.Start = StartKind.Escape;
        sim.Story.PendingDifficulty = null; // raids start in the wild, once you've got away
        var w = sim.World;
        var defs = sim.Defs;
        int3 At(int dx, int dy) => new(center.X + dx, center.Y + dy, center.Z);

        foreach (var c in sim.SpiralFrom(center, 13))
            if (c.Z == center.Z && sim.Map.Structure[sim.Map.Idx(c)] is { IsNull: false } s)
                sim.DestroyThing(s);

        // A palisade with a gap in each side.
        var stakes = defs.Thing("WoodWall");
        const int R = 11;
        for (var k = -R; k <= R; k++)
            foreach (var cell in new[] { At(k, -R), At(k, R), At(-R, k), At(R, k) })
                if (Math.Abs(k) > 1 && sim.CanPlaceStructure(cell, stakes))
                    sim.SpawnStructure(stakes, cell);

        // The cage, its gate facing the fire.
        var state = new EscapeState { CageMin = At(-8, -8), CageMax = At(-4, -4), Gate = At(-6, -4), DragonSpot = At(6, -6) };
        for (var x = state.CageMin.X; x <= state.CageMax.X; x++)
        for (var y = state.CageMin.Y; y <= state.CageMax.Y; y++)
        {
            var c = new int3(x, y, center.Z);
            if (state.InCage(c))
                continue;
            if (sim.Map.Structure[sim.Map.Idx(c)] is { IsNull: false } old)
                sim.DestroyThing(old);
            var built = sim.SpawnStructure(c == state.Gate ? defs.Thing("CageGate") : stakes, c);
            if (c == state.Gate)
                sim.SetLocked(built, true);
        }

        // The camp: a fire, bedrolls, and a chest with what they took from you.
        var fire = sim.SpawnStructure(defs.Thing("Campfire"), center);
        w.Get<Fueled>(fire).Fuel = defs.Thing("Campfire").Building!.Fuel!.Capacity;
        foreach (var x in new[] { 5, 7, 9 })
            sim.SpawnStructure(defs.Thing("Bed"), At(x, 6));
        state.Chest = sim.SpawnStructure(defs.Thing("Chest"), At(-2, 6));
        foreach (var (def, count) in Confiscated)
            if (sim.Store(state.Chest, sim.CreateItem(defs.Thing(def), count)) is { IsNull: false } left)
                sim.PlaceItemNear(left, At(-2, 6));
        sim.Story.Escape = state;

        // You, in the cage: nothing on you but your clothes.
        var me = sim.SpawnCreature(defs.Creature("Human"), string.IsNullOrWhiteSpace(hero.Name) ? "Arin" : hero.Name.Trim(), state.CageCenter, ageDays: 24 * 365f);
        var needs = w.Get<Needs>(me).Values;
        Array.Fill(needs, 0.6f);
        w.Add(me, new Avatar());
        w.Add(me, new Captive());
        w.Add(me, new Quest { Stage = QuestStage.Captive });
        if (hero.Look is { } look)
            w.Add(me, look);
        if (hero.Traits != null)
            TraitRules.Set(sim, me, hero.Traits);
        // A straw pallet in the cage's far corner (where the starting view sees it over the walls):
        // yours, so nobody counts it as a free bed.
        var pallet = sim.SpawnStructure(defs.Thing("StrawPallet"), new int3(state.CageMax.X - 1, state.CageMin.Y + 1, center.Z));
        w.Add(me, new BedOwner { Bed = pallet });
        var dragon = Companions.SpawnBondedDragon(sim, me, hero.DragonName, hero.DragonLook, state.DragonSpot, breed: hero.Breed(sim.Defs));
        w.Get<Position>(dragon) = Position.At(state.DragonSpot);
        w.Add(dragon, new Chained());

        // The captors: the jailer by the fire, guards at their posts (one watching the dragon).
        var human = defs.Creature("Human");
        var jailer = sim.SpawnCreature(human, JailerName, At(-3, -1), Faction.Captors, ageDays: 41 * 365f);
        w.Add(jailer, new StoryNpc { Role = StoryRole.Jailer, Home = At(-3, -1) });
        sim.Equip(jailer, defs.Thing("Club"));
        state.KeyHolder = jailer;
        int3[] posts = [At(4, -4), At(3, 3)];
        for (var i = 0; i < GuardNames.Length; i++)
        {
            var post = sim.SpiralFrom(posts[i], 3).First(c => sim.Map.Passable(c));
            var guard = sim.SpawnCreature(human, GuardNames[i], post, Faction.Captors, ageDays: (26 + i * 7) * 365f);
            w.Add(guard, new StoryNpc { Role = StoryRole.Captor, Home = post });
            sim.Equip(guard, defs.Thing(GuardWeapons[i]));
            sim.Wear(guard, defs.Thing("LeatherCap"));
        }
        foreach (var e in w.Query<StoryNpc>())
        {
            var skills = w.Get<Skills>(e);
            skills.Level[(int)Skill.Melee] = 5;
            skills.Level[(int)Skill.Shooting] = 5;
        }
        // Clothes last, so they don't shift the entities (and the rolls seeded by them) above.
        Clothing.DressHero(sim, me, hero.Clothes);
        if (defs.AllOutfits.Find(o => o.Name == "Captor") is { } captor)
            foreach (var e in w.Query<StoryNpc>())
                Clothing.Dress(sim, e, captor, Clothing.StreamFor(sim, e));
        sim.RebuildDerivedIfNeeded();

        sim.Messages.Add(MessageKind.Info,
            $"Separated from your squadron in the storm, {w.Get<Creature>(me).Name} and {w.Get<Creature>(dragon).Name} came down in the wrong valley. "
            + $"Now you're in a cage, and {w.Get<Creature>(dragon).Name} is in chains across the camp. "
            + $"The jailer, {JailerName}, brings supper at dusk: be ready.",
            state.CageCenter);
        return me;
    }

    /// <summary>The cage gate (a lockable door), or Entity.Null if it's been destroyed.</summary>
    public static Entity Gate(Simulation sim) =>
        Of(sim) is { } st && sim.Map.Structure[sim.Map.Idx(st.Gate)] is { IsNull: false } g && sim.World.Get<Thing>(g).Def.Building?.Lockable == true
            ? g : Entity.Null;

    /// <summary>Is the cage locked? (A gate that's gone, burned or smashed, locks nothing.)</summary>
    public static bool GateLocked(Simulation sim) => sim.IsLocked(Gate(sim));

    /// <summary>
    /// Is the gate open for the jailer's supper round now? Not on an evening you were dragged back
    /// (<see cref="EscapeState.NoSupperBefore"/>), and only if someone's on their feet to bring it.
    /// </summary>
    public static bool SupperTime(Simulation sim) =>
        sim.Clock.HourOfDay is >= SupperHour and < LockUpHour && Of(sim) is { } st && sim.Clock.Tick >= st.NoSupperBefore && !Jailer(sim).IsNull;

    /// <summary>
    /// Whoever's acting as jailer: the one with the key if they're on their feet, else the jailer
    /// himself, else any captor still standing (who'd bring supper while he's laid up). Null if none.
    /// </summary>
    public static Entity Jailer(Simulation sim)
    {
        var w = sim.World;
        bool Fit(Entity e) => w.IsAlive(e) && w.Has<StoryNpc>(e) && w.Get<Creature>(e).Able && !w.Has<Leaving>(e)
            && w.Get<StoryNpc>(e).Role is StoryRole.Captor or StoryRole.Jailer;
        if (Of(sim) is { } st && Fit(st.KeyHolder))
            return st.KeyHolder;
        var stand = Entity.Null;
        foreach (var e in w.Query<StoryNpc>())
        {
            if (!Fit(e))
                continue;
            if (w.Get<StoryNpc>(e).Role == StoryRole.Jailer)
                return e;
            if (stand.IsNull)
                stand = e;
        }
        return stand;
    }

    /// <summary>Whoever had the key dropped it: it falls at their feet when they go down (or die). Called from where creatures are downed or killed.</summary>
    public static void OnIncapacitated(Simulation sim, Entity e)
    {
        if (Of(sim) is not { } st || st.KeyHolder != e || e.IsNull)
            return;
        st.KeyHolder = Entity.Null;
        sim.PlaceItemNear(sim.CreateItem(sim.Defs.Thing("ChainKey"), 1), sim.World.Get<Position>(e).Cell);
    }

    /// <summary>The captive the jailer is to bring supper to (for his brain), if it's time.</summary>
    public static Entity SupperFor(Simulation sim)
    {
        if (!SupperTime(sim) || Of(sim) == null)
            return Entity.Null;
        foreach (var e in sim.World.Query<Quest>())
            if (sim.World.Get<Quest>(e).Stage is QuestStage.Captive or QuestStage.Opportunity)
                return e;
        return Entity.Null;
    }

    /// <summary>The jailer, if the avatar can jump him at this cell now (in the cage, his back to the open gate).</summary>
    public static Entity JailerAt(Simulation sim, Entity avatar, int3 cell)
    {
        var w = sim.World;
        if (!w.TryGet<Quest>(avatar, out var q) || q.Stage != QuestStage.Opportunity)
            return Entity.Null;
        var jailer = Jailer(sim);
        return !jailer.IsNull && w.Get<Creature>(jailer).Able && w.Get<Position>(jailer).Cell == cell ? jailer : Entity.Null;
    }

    public static bool HasKey(Simulation sim, Entity avatar)
    {
        var w = sim.World;
        if (w.Get<Carrying>(avatar).Item is { IsNull: false } held && w.Get<Thing>(held).Def.Name == "ChainKey")
            return true;
        return sim.PackOf(avatar) is { IsNull: false } pack && sim.StoredIn(pack).Any(e => w.Get<Thing>(e).Def.Name == "ChainKey");
    }

    /// <summary>The avatar's chained dragon, if they hold the key and stand beside it.</summary>
    public static Entity ChainedDragonInReach(Simulation sim, Entity avatar)
    {
        var w = sim.World;
        var dragon = Companions.DragonOf(sim, avatar);
        if (dragon.IsNull || !w.Has<Chained>(dragon) || !HasKey(sim, avatar))
            return Entity.Null;
        return Pathfinder.Touches(w.Get<Position>(avatar).Cell, w.Get<Position>(dragon).Cell) ? dragon : Entity.Null;
    }

    /// <summary>
    /// Jump the jailer: a blow to the head drops him, and his club and chain key fall at his feet
    /// (pick them up: F, or for time units in the fight). The camp is roused; the fight starts with your turn.
    /// </summary>
    public static void Overpower(Simulation sim, Entity avatar, Entity jailer)
    {
        var w = sim.World;
        var health = w.Get<Health>(jailer);
        var head = health.Body.PartIndex("Head");
        if (head >= 0)
            HealthSystem.Damage(sim, jailer, health.PartMaxHp(head) * 0.75f, Defs.DamageType.Blunt, head);
        ref var c = ref w.Get<Creature>(jailer);
        if (!c.Dead)
            HealthSystem.SetDowned(sim, jailer, ref c, true);
        var at = w.Get<Position>(jailer).Cell;
        // His club falls at his feet, and (going down) the key if he has it: grab them (each costs TU in the fight).
        if (w.TryGet<Equipment>(jailer, out var eq) && w.IsAlive(eq.Weapon))
        {
            var club = eq.Weapon;
            w.Remove<Equipment>(jailer);
            sim.PlaceItemNear(club, at);
        }
        sim.Messages.Add(MessageKind.Good,
            $"{w.Get<Creature>(avatar).Name} drives the bowl into {w.Get<Creature>(jailer).Name}'s face and brings them down. "
            + "What they carried falls at their feet: grab it!",
            at);
        RaiseAlarm(sim, avatar);
    }

    /// <summary>The camp is roused: the captors fight (as raiders), and you're no longer anyone's prisoner.</summary>
    public static void RaiseAlarm(Simulation sim, Entity avatar)
    {
        var w = sim.World;
        if (w.Has<Captive>(avatar))
            w.Remove<Captive>(avatar);
        w.Get<Quest>(avatar).Stage = QuestStage.Escaping;
        foreach (var e in w.Query<StoryNpc>())
        {
            ref var c = ref w.Get<Creature>(e);
            if (w.Get<StoryNpc>(e).Role is StoryRole.Captor or StoryRole.Jailer && !c.Dead)
                c.Faction = Faction.Raider;
        }
        sim.Messages.Add(MessageKind.Danger, "A shout goes up across the camp. Free your dragon and get out!", w.Get<Position>(avatar).Cell);
        if (!sim.Combat.Active && sim.Combat.HostilesInSight(sim))
            sim.Combat.Start(sim);
    }

    /// <summary>Strike the chains off the avatar's dragon (beside it, with the key).</summary>
    public static bool Unchain(Simulation sim, Entity avatar, Entity dragon)
    {
        var w = sim.World;
        if (!w.Has<Chained>(dragon) || !HasKey(sim, avatar))
            return false;
        w.Remove<Chained>(dragon);
        if (w.TryGet<Quest>(avatar, out _))
            w.Get<Quest>(avatar).Stage = QuestStage.TakeWing;
        var name = w.Get<Creature>(dragon).Name;
        sim.Messages.Add(MessageKind.Good, $"The chains fall away and {name} rears up, wings wide. Get beside {name} and fly!", w.Get<Position>(dragon).Cell);
        return true;
    }

    /// <summary>
    /// The camp's beats (each game second): the gate at supper time, the jailer's round, slipping
    /// out raising the alarm, the reinforcements, and a recapture if you go down.
    /// </summary>
    internal static void Update(Simulation sim)
    {
        if (Of(sim) is not { } st)
            return;
        var w = sim.World;
        foreach (var e in w.Query<StoryNpc>())
            if (w.Get<Creature>(e).Faction != Faction.Raider && w.TryGet<Needs>(e, out var needs))
                Array.Fill(needs.Values, 1f); // the camp keeps itself fed: it's the player's hunger that counts

        foreach (var avatar in w.Query<Quest>())
        {
            ref var q = ref w.Get<Quest>(avatar);
            var me = w.Get<Creature>(avatar);
            var here = w.Get<Position>(avatar).Cell;
            switch (q.Stage)
            {
                case QuestStage.Captive or QuestStage.Opportunity:
                {
                    if (!st.InCage(here) && me.Able)
                    {
                        sim.Messages.Add(MessageKind.Danger, $"{me.Name} slips out of the cage, and someone sees.", here);
                        RaiseAlarm(sim, avatar);
                        break;
                    }
                    var gate = Gate(sim);
                    var locked = sim.IsLocked(gate);
                    var jailer = Jailer(sim);
                    var supper = SupperTime(sim);
                    if (supper && locked && q.Stage == QuestStage.Captive)
                    {
                        sim.SetLocked(gate, false);
                        sim.Messages.Add(MessageKind.Info, $"Dusk. {w.Get<Creature>(jailer).Name} unlocks the cage and comes in with a bowl of something.", st.Gate);
                    }
                    else if (q.Stage == QuestStage.Captive && supper && !jailer.IsNull && w.Get<Creature>(jailer).Able
                        && st.InCage(w.Get<Position>(jailer).Cell) && Pathfinder.Touches(w.Get<Position>(jailer).Cell, here))
                    {
                        q.Stage = QuestStage.Opportunity;
                        w.Get<Needs>(avatar).Add(sim.Defs.Need("Food"), 0.4f);
                        sim.Messages.Add(MessageKind.Info,
                            $"{w.Get<Creature>(jailer).Name} sets the bowl down, back to the open gate. Now, while they're this close (F on them), or wait for another night.", here);
                    }
                    else if (!supper && q.Stage == QuestStage.Opportunity)
                    {
                        q.Stage = QuestStage.Captive;
                        sim.Messages.Add(MessageKind.Info, "The bowl's taken away and the gate swings shut. The moment's passed.", here);
                    }
                    else if (!supper && !locked && (jailer.IsNull || !st.InCage(w.Get<Position>(jailer).Cell)) && !Occupied(sim, st.Gate))
                        sim.SetLocked(gate, true);
                    break;
                }
                case QuestStage.Escaping or QuestStage.TakeWing:
                    if (sim.Combat.Active && sim.Combat.Round >= ReinforcementRound && !st.Reinforced)
                        SendReinforcements(sim, st);
                    // Beaten (not just knocked down in a fight you won): they drag you back.
                    if (!sim.Combat.Active && me.Downed && !me.Dead && sim.Combat.LastFightLost && CaptorsStanding(sim))
                        Recapture(sim, avatar, st);
                    break;
            }
        }
    }

    private static bool Occupied(Simulation sim, int3 cell)
    {
        foreach (var e in sim.Creatures())
            if (sim.World.Get<Position>(e).Cell == cell)
                return true;
        return false;
    }

    private static bool CaptorsStanding(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Query<StoryNpc>())
            if (w.Get<StoryNpc>(e).Role is StoryRole.Captor or StoryRole.Jailer && w.Get<Creature>(e).Able)
                return true;
        return false;
    }

    /// <summary>More of the camp's band (two, hand-to-hand), coming in from the east at the noise.</summary>
    private static void SendReinforcements(Simulation sim, EscapeState st)
    {
        st.Reinforced = true;
        if (Raids.EdgeEntry(sim, 1, st.CageCenter) is not { } entry)
            return;
        var kinds = new[] { "Swordsman", "Brawler" }.Select(sim.Defs.RaiderKind).ToList(); // no archers: see GuardWeapons
        Raids.Spawn(sim, kinds, entry, st.CageCenter, sim.Rand);
        sim.Messages.Add(MessageKind.Danger, "Horns to the east: more of them are coming. Don't stay to fight them all!", entry);
    }

    /// <summary>
    /// Downed and beaten: they drag you back to the cage, bind your wounds (you're worth more
    /// alive), chain the dragon again and lock up. Another evening, another chance.
    /// </summary>
    public static void Recapture(Simulation sim, Entity avatar, EscapeState st)
    {
        var w = sim.World;
        foreach (var e in w.Query<StoryNpc>())
        {
            ref var c = ref w.Get<Creature>(e);
            if (c.Dead || w.Get<StoryNpc>(e).Role is not (StoryRole.Captor or StoryRole.Jailer))
                continue;
            c.Faction = Faction.Captors;
            if (w.Has<Leaving>(e))
                w.Remove<Leaving>(e);
            AiSystem.EndJob(sim, e, w.Get<Mind>(e), false);
        }

        // The key goes back in the jailer's pocket (wherever it ended up); you go back in the cage, patched up.
        ref var carrying = ref w.Get<Carrying>(avatar);
        if (carrying.HasItem && w.Get<Thing>(carrying.Item).Def.Name == "ChainKey")
        {
            w.Destroy(carrying.Item);
            carrying.Item = Entity.Null;
        }
        foreach (var key in w.Query<Thing>().Where(k => w.Get<Thing>(k).Def.Name == "ChainKey").ToList())
            if (w.IsAlive(key) && (w.Get<Thing>(key).Spawned || w.Has<Stored>(key)))
            {
                if (w.Get<Thing>(key).Spawned)
                    sim.DestroyThing(key);
                else
                    w.Destroy(sim.TakeFromStack(key, 1));
            }
        st.KeyHolder = Jailer(sim); // a new key on whoever's acting jailer now
        st.NoSupperBefore = sim.Clock.Tick + SimClock.HoursToTicks(LockUpHour - SupperHour + 0.5f); // not tonight: next evening
        AiSystem.EndJob(sim, avatar, w.Get<Mind>(avatar), false);
        if (w.Has<Riding>(avatar))
            RidingSystem.Dismount(sim, avatar);
        w.Get<Position>(avatar) = Position.At(st.CageCenter);
        foreach (var injury in w.Get<Health>(avatar).Injuries)
            if (!injury.Tended)
                (injury.Tended, injury.Quality, injury.TendedTick) = (true, 0.4f, sim.Clock.Tick);
        w.Get<Health>(avatar).Changed();
        if (!w.Has<Captive>(avatar))
            w.Add(avatar, new Captive());
        w.Get<Quest>(avatar).Stage = QuestStage.Captive;

        var dragon = Companions.DragonOf(sim, avatar);
        if (!dragon.IsNull)
        {
            AiSystem.EndJob(sim, dragon, w.Get<Mind>(dragon), false);
            w.Get<Position>(dragon) = Position.At(st.DragonSpot);
            if (!w.Has<Chained>(dragon))
                w.Add(dragon, new Chained());
        }
        if (!Occupied(sim, st.Gate))
            sim.SetLocked(Gate(sim), true); // (someone in the gateway: it's locked once the camp's beats see it clear)
        st.Reinforced = false;
        sim.Messages.Add(MessageKind.Danger,
            $"They drag {w.Get<Creature>(avatar).Name} back to the cage and bind their wounds: you're worth more alive. "
            + "The dragon's in chains again. There'll be another supper tomorrow.",
            st.CageCenter);
    }

    /// <summary>What the camp asks of you right now, for the HUD (null outside it).</summary>
    public static string? Objective(Simulation sim, Entity avatar)
    {
        if (Of(sim) == null || !sim.World.TryGet<Quest>(avatar, out var q))
            return null;
        var dragon = Companions.DragonOf(sim, avatar);
        var name = dragon.IsNull ? "your dragon" : sim.World.Get<Creature>(dragon).Name;
        return q.Stage switch
        {
            QuestStage.Captive => SupperTime(sim)
                ? $"{JailerName} is coming with supper. Wait for him to come close."
                : $"Wait for supper at dusk ({SupperHour:0}:00), when the gate opens.",
            QuestStage.Opportunity => $"Overpower {JailerName} (F on him) and take his keys, or wait for another night.",
            QuestStage.Escaping => HasKey(sim, avatar)
                ? $"Free {name}: get beside the chains with the key (F, or Free in the fight). Your gear's in the chest by the tents."
                : Of(sim)?.KeyHolder is { IsNull: false } holder && sim.World.IsAlive(holder)
                    ? $"{sim.World.Get<Creature>(holder).Name} has the chain key: bring them down for it, then free {name}."
                    : $"Grab the chain key where it fell (F, or Pick up in the fight), then free {name}.",
            QuestStage.TakeWing when !dragon.IsNull && Departure.CantFlyOff(sim, avatar) is { } why => $"Get beside {name} and fly away! ({why})",
            QuestStage.TakeWing => "Fly away! (the button below)",
            _ => null,
        };
    }
}
