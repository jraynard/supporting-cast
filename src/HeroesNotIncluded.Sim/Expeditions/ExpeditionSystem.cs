using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Expeditions;

public enum QuestStatus : byte
{
    Open,

    /// <summary>A party is out on it.</summary>
    Taken,

    /// <summary>Done: nothing left there to send anyone to.</summary>
    Cleared,
}

/// <summary>One of the round's quests, with the truth about it that nobody is told.</summary>
public sealed class Quest
{
    public int Id { get; init; }
    public required QuestDef Def { get; init; }
    public float Danger { get; internal set; }
    public int Reward { get; init; }
    public QuestStatus Status { get; set; }
}

/// <summary>What one person believes about a quest: their guess at its danger and reward, and how sure they are (0..1).</summary>
public sealed class Intel
{
    public float Danger;
    public float Reward;
    public float Confidence;
}

/// <summary>A quest as someone who's heard of it sees it (<c>PerceptionView.Quests</c>).</summary>
public readonly record struct KnownQuest(int Id, string Label, string Description, float Danger, float Reward, float Confidence, QuestStatus Status);

/// <summary>A piece of gear as it went out, to be put back on its owner when they return.</summary>
public sealed record GearRecord(ThingDef Def, MaterialDef? Material, Quality? Quality, float Hp, float Dullness);

/// <summary>A wound as it went out.</summary>
public sealed record WoundRecord(int Part, DamageType Type, float Damage, bool Tended, float TendQuality, bool Absorbed);

/// <summary>
/// One party member while they're off the map: who they are and what they carry, enough to put
/// them back as they were (plus whatever the quest did to them). Kept rather than the entity, so
/// no system has to learn to skip people who aren't there.
/// </summary>
public sealed class MemberRecord
{
    public string Name = "";
    public required AdventurerKindDef Kind;
    public required PartyDef PartyDef;
    public int Party;
    public int LookId;
    public float AgeDays;
    public int Melee, Shooting;
    public int Coin;
    public float Satisfaction;
    public float Blood = 1f;
    public float Pain;
    public float Food = 1f;
    public GearRecord? Weapon;
    public List<GearRecord> Apparel = [];
    public List<WoundRecord> Wounds = [];

    /// <summary>The empath's charms they carry (<see cref="Healing.Charms"/>): wards for the road.</summary>
    public List<Healing.CharmRecord> Charms = [];

    /// <summary>The tinker's contraptions they carry (<see cref="Tinkering.Contraptions"/>).</summary>
    public List<Tinkering.ContraptionRecord> Contraptions = [];

    /// <summary>The cook's rations they carry (<see cref="Work.Rations"/>): eaten on the way, and gone.</summary>
    public List<Work.RationRecord> Rations = [];

    /// <summary>How the food on the road went, set when the quest is resolved: none, fed, or sick.</summary>
    public RoadFood Fed;
}

/// <summary>How a member ate on the road (<see cref="Work.Rations"/>).</summary>
public enum RoadFood : byte
{
    /// <summary>No rations: what they could find.</summary>
    None,
    Fed,

    /// <summary>The rations had turned.</summary>
    Sick,
}

/// <summary>A party out on a quest.</summary>
public sealed class Expedition
{
    public int Quest;
    public int Party;
    public required PartyDef PartyDef;

    /// <summary>The danger the broker said it was, and who sent them.</summary>
    public float Stated;
    public string BrokerName = "";
    public long ReturnTick;
    public List<MemberRecord> Members = [];

    /// <summary>They've a sound wagon to bring home what they find, or none to worry about (<see cref="Tinkering.Wagons"/>).</summary>
    public bool Hauls = true;

    /// <summary>
    /// Nonzero while they've taken the quest but are still in town, waiting on gear they handed
    /// over to be mended: they set out once it's back, or at this tick without it.
    /// </summary>
    public long SetOutBy;
}

/// <summary>
/// Quests and the parties sent on them (docs/DESIGN.md, "The loop"). The round rolls a few quests
/// whose true danger and reward nobody knows. The Innkeeper, who brokers quests, starts with a guess at each;
/// adventurers trade rumours in the tavern that sharpen what listeners believe; a party that
/// comes back says how it really was. Sending a party off is <see cref="Offer"/>: they go if the
/// danger the broker states seems right for them. Off the map, how it goes comes down to what the
/// town gave them: their gear and its upkeep, their wounds, how well they were fed and served,
/// against the quest's true danger (<see cref="Strength"/>). They come back changed by it, or don't.
/// </summary>
public sealed class ExpeditionSystem(ulong seed) : ISimSystem, ISaveState
{
    public string Name => "Expeditions";
    public int Interval => SimClock.TicksPerHour / 12;

    /// <summary>Quests a round has on offer.</summary>
    public const int QuestsPerRound = 3;

    /// <summary>Game hours of walking either way, on top of the quest's own.</summary>
    public const float TravelHours = 1.5f;

    /// <summary>Game hours between one rumour told in the tavern and the next.</summary>
    public const float RumourHours = 1.25f;

    /// <summary>A party turns down a quest stated as less than this share of its strength (beneath them), or more than this (madness).</summary>
    public const float TooEasy = 0.5f, TooHard = 1.4f;

    /// <summary>Game hours a returned party stays before moving on.</summary>
    public const float StayAfterHours = 8;

    private readonly SimRandom _rand = new(seed ^ 0xE4BED17u);
    private readonly List<Quest> _quests = [];
    private readonly Dictionary<(Entity Who, int Quest), Intel> _intel = [];
    private readonly List<Expedition> _out = [];
    private int _nextQuest = 1;
    private long _nextRumour = -1;
    private bool _rolled;

    public IReadOnlyList<Quest> Quests => _quests;
    public IReadOnlyList<Expedition> Out => _out;

    /// <summary>Quests that came good or didn't, for the round's reckoning (M3's Renown and Dread).</summary>
    public int Succeeded { get; private set; }
    public int Failed { get; private set; }
    public int Dead { get; private set; }

    public Quest? Find(int id) => _quests.Find(q => q.Id == id);

    public Intel? IntelOf(Entity who, int quest) => _intel.GetValueOrDefault((who, quest));

    // ---------------- Danger in words, the same scale for quests and parties ----------------

    public static string DangerWord(float danger) => danger switch
    {
        < 1.5f => "light work",
        < 2.5f => "risky",
        < 3.5f => "dangerous",
        < 4.5f => "deadly",
        _ => "certain death",
    };

    public static string StrengthWord(float strength) => strength switch
    {
        < 1.5f => "a sorry lot",
        < 2.5f => "able enough",
        < 3.5f => "a solid company",
        < 4.5f => "formidable",
        _ => "a warband",
    };

    public static string SureWord(float confidence) => confidence switch
    {
        < 0.3f => "a guess",
        < 0.6f => "by rumour",
        < 0.9f => "fairly sure",
        _ => "certain",
    };

    // ---------------- The round ----------------

    public void Update(Simulation sim)
    {
        if (!sim.Round.Live)
            return;
        if (!_rolled)
            Roll(sim);
        BriefBrokers(sim);
        var now = sim.Clock.Tick;
        if (_nextRumour < 0)
            _nextRumour = now + SimClock.HoursToTicks(RumourHours);
        else if (now >= _nextRumour)
        {
            TellRumour(sim);
            _nextRumour = now + SimClock.HoursToTicks(RumourHours * _rand.Range(0.7f, 1.3f));
        }
        Report(sim);
        SetOutWhenReady(sim);
        // Back after dark, they camp out and come in with the morning.
        for (var i = 0; i < _out.Count; i++)
            if (now >= _out[i].ReturnTick && !Ai.AiQueries.IsNight(sim))
            {
                var ex = _out[i];
                _out.RemoveAt(i--);
                Resolve(sim, ex);
            }
    }

    private void Roll(Simulation sim)
    {
        _rolled = true;
        var pool = sim.Defs.AllQuests.ToList();
        for (var n = 0; n < QuestsPerRound && pool.Count > 0; n++)
        {
            var def = pool[_rand.NextInt(pool.Count)];
            pool.Remove(def);
            _quests.Add(new Quest
            {
                Id = _nextQuest++, Def = def, Danger = _rand.Range(def.DangerMin, def.DangerMax),
                Reward = _rand.Range(def.RewardMin, def.RewardMax + 1),
            });
        }
    }

    /// <summary>Whoever holds a broker's trade has a guess at every quest (a late joiner too).</summary>
    private void BriefBrokers(Simulation sim)
    {
        foreach (var e in sim.Creatures())
        {
            if (!sim.World.TryGet<Trade>(e, out var trade) || !trade.Def.Brokers)
                continue;
            foreach (var q in _quests)
                if (!_intel.ContainsKey((e, q.Id)))
                    _intel[(e, q.Id)] = new Intel { Danger = q.Danger * _rand.Range(0.6f, 1.4f), Reward = q.Reward * _rand.Range(0.6f, 1.4f), Confidence = 0.2f };
        }
    }

    /// <summary>
    /// Hear something about a quest: a guess at its danger (and reward), believed as far as
    /// <paramref name="weight"/>. Certain knowledge (weight 1) replaces what they had.
    /// </summary>
    public void Hear(Entity who, int quest, float danger, float? reward, float weight)
    {
        if (!_intel.TryGetValue((who, quest), out var intel) || weight >= 1f)
        {
            _intel[(who, quest)] = new Intel { Danger = danger, Reward = reward ?? intel?.Reward ?? 0f, Confidence = Math.Min(weight, 1f) };
            return;
        }
        if (intel.Confidence >= 1f)
            return; // they know better than hearsay
        intel.Danger += (danger - intel.Danger) * weight;
        if (reward is { } r)
            intel.Reward += (r - intel.Reward) * weight;
        intel.Confidence = Math.Min(0.9f, intel.Confidence + weight * 0.5f); // only seeing for yourself is certain
    }

    /// <summary>
    /// A drinker running their mouth: they tell a quest's rumour unbidden, nearer the truth the
    /// less <paramref name="spread"/> there is in it (drink loosens what they'd otherwise keep back).
    /// </summary>
    public void Gossip(Simulation sim, Entity teller, float spread)
    {
        var open = _quests.Where(q => q.Status != QuestStatus.Cleared).ToList();
        if (open.Count == 0)
            return;
        Tell(sim, teller, open[_rand.NextInt(open.Count)], spread);
    }

    /// <summary>A rumour of a quest, told off by <paramref name="spread"/> either way: whoever catches it clearly takes it in.</summary>
    private void Tell(Simulation sim, Entity teller, Quest q, float spread)
    {
        var told = q.Danger * _rand.Range(1f - spread, 1f + spread);
        var line = q.Def.Rumours.Count > 0 ? q.Def.Rumours[_rand.NextInt(q.Def.Rumours.Count)] : $"They say {q.Def.Label} is {{0}}.";
        if (sim.Speech.Say(sim, teller, string.Format(line, DangerWord(told)), Volume.Say) is not { } said)
            return;
        // Said plainer, it's taken in more: a gossiping drinker isn't just passing on what they heard.
        var weight = spread < 0.2f ? 0.5f : 0.35f;
        foreach (var h in said.Listeners)
            if (h.Clarity == Clarity.Clear && h.Listener != teller)
                Hear(h.Listener, q.Id, told, null, weight);
    }

    /// <summary>An adventurer lounging at the tavern tells what they've heard of a quest, rightly or not.</summary>
    private void TellRumour(Simulation sim)
    {
        var open = _quests.Where(q => q.Status != QuestStatus.Cleared).ToList();
        if (open.Count == 0)
            return;
        var tellers = new List<Entity>();
        foreach (var e in sim.Creatures())
            if (sim.World.TryGet<Adventurer>(e, out var a) && !sim.World.Has<Leaving>(e) && SpeechSystem.CanAct(sim, e)
                && Adventurers.PartySystem.HauntOf(sim, a.PartyDef) is { } haunt
                && Maps.Map.Distance(sim.World.Get<Position>(e).Cell, haunt.Anchor) <= 8)
                tellers.Add(e);
        if (tellers.Count == 0)
            return;
        var teller = tellers[_rand.NextInt(tellers.Count)];
        var q = open[_rand.NextInt(open.Count)];
        Tell(sim, teller, q, 0.25f);
    }

    // ---------------- Sending a party ----------------

    /// <summary>How strong one person is for a quest: skill, gear and its upkeep, wounds, how fed and how pleased they are.</summary>
    public static float Strength(MemberRecord m) => Strength(m, null, null);

    /// <summary>
    /// How strong one person is for this quest, with the contraptions they carry that work there
    /// (none of the <paramref name="jammed"/> ones).
    /// </summary>
    public static float Strength(MemberRecord m, QuestDef? quest, ISet<Tinkering.ContraptionRecord>? jammed) => Strength(m, quest, jammed, 1f);

    /// <summary>As <see cref="Strength(MemberRecord, QuestDef?, ISet{Tinkering.ContraptionRecord}?)"/>, with how their rations served them on the road (<see cref="Work.Rations.StrengthFactor"/>).</summary>
    public static float Strength(MemberRecord m, QuestDef? quest, ISet<Tinkering.ContraptionRecord>? jammed, float rations)
    {
        var skill = Math.Clamp(Math.Max(m.Melee, m.Shooting) / 9f, 0.3f, 2f);
        var weapon = m.Weapon is not { } wpn ? 0.4f
            : Crafting.QualityFactor(wpn.Quality ?? Quality.Plain) * (WeaponWear.Edged(wpn.Def) ? 1f - WeaponWear.DullDamageLoss * wpn.Dullness : 1f) * (0.6f + 0.4f * wpn.Hp);
        var armor = 1f;
        foreach (var a in m.Apparel)
            if (a.Def.Apparel is { } props)
                armor += (props.Sharp + props.Blunt) * 0.25f * Crafting.QualityFactor(a.Quality ?? Quality.Plain) * a.Hp;
        var health = Math.Clamp(1f - 0.8f * m.Pain - (1f - m.Blood), 0.2f, 1f);
        var morale = 1f + 0.08f * Math.Clamp(m.Satisfaction, -4f, 4f);
        var fed = m.Food < 0.3f ? 0.85f : 1f;
        var fever = Fevered(m);
        var kit = quest == null ? 1f : Tinkering.Contraptions.StrengthFactor(Working(m, jammed), quest);
        return skill * weapon * armor * health * morale * fed * fever * Healing.Charms.StrengthFactor(m.Charms) * kit * rations;
    }

    /// <summary>
    /// A fever taken on the road saps them, whatever pain it gave as they left: less once it's seen
    /// to (bound and resting, the body's throwing it off), more left to grow.
    /// </summary>
    private static float Fevered(MemberRecord m)
    {
        var worst = 1f;
        foreach (var w in m.Wounds)
            if (w.Type == DamageType.Fever)
                worst = Math.Min(worst, w.Tended ? 0.92f : 0.8f);
        return worst;
    }

    /// <summary>The contraptions someone carries that haven't jammed.</summary>
    private static IEnumerable<Tinkering.ContraptionRecord> Working(MemberRecord m, ISet<Tinkering.ContraptionRecord>? jammed) =>
        jammed == null ? m.Contraptions : m.Contraptions.Where(c => !jammed.Contains(c));

    /// <summary>Everyone in a party who's in town and not leaving.</summary>
    public static List<Entity> PartyInTown(Simulation sim, int party)
    {
        var list = new List<Entity>();
        foreach (var e in sim.Creatures())
            if (sim.World.TryGet<Adventurer>(e, out var a) && a.Party == party && !sim.World.Has<Leaving>(e))
                list.Add(e);
        return list;
    }

    /// <summary>A party's true strength, as it stands now.</summary>
    public static float PartyStrength(Simulation sim, int party) => PartyInTown(sim, party).Sum(e => Strength(Record(sim, e)));

    /// <summary>The party's open request for a quest, if any.</summary>
    public static Order? QuestOrder(Simulation sim, int party) =>
        sim.Shops.All.FirstOrDefault(e => e.IsOpen && e.Kind.Kind == ServiceKind.Quest
            && sim.World.TryGet<Adventurer>(e.Poster, out var a) && a.Party == party);

    /// <summary>
    /// A broker in reach of a party member puts a quest to them, at the danger they believe it is.
    /// The party takes it if that seems right for them, pays the broker and sets out; otherwise
    /// they say no. Returns whether they took it.
    /// </summary>
    public bool Offer(Simulation sim, Entity broker, Entity member, int questId)
    {
        var w = sim.World;
        if (Find(questId) is not { Status: QuestStatus.Open } quest || !_intel.TryGetValue((broker, questId), out var intel)
            || !w.TryGet<Adventurer>(member, out var a) || w.Has<Leaving>(member) || !Handing.InReach(sim, broker, member)
            || !SpeechSystem.CanAct(sim, member) || QuestOrder(sim, a.Party) is not { } order)
            return false;
        var strength = PartyStrength(sim, a.Party);
        var label = quest.Def.Label;
        if (intel.Danger < TooEasy * strength)
        {
            sim.Speech.Say(sim, member, $"{Capital(label)}, {DangerWord(intel.Danger)}? That's work for farmhands, not us.", Volume.Say);
            return false;
        }
        if (intel.Danger > TooHard * strength)
        {
            sim.Speech.Say(sim, member, $"{Capital(label)}, {DangerWord(intel.Danger)}? Do we look mad to you?", Volume.Say);
            return false;
        }
        sim.Speech.Say(sim, member, $"{Capital(label)}. {Capital(DangerWord(intel.Danger))}, you say? We'll do it.", Volume.Say);
        sim.Shops.Complete(sim, order, broker);
        quest.Status = QuestStatus.Taken;
        var party = PartyInTown(sim, a.Party);
        // A wagon still broken: they'll go, but they'll carry home only what's in their packs.
        var hauls = !Tinkering.Wagons.Damaged(sim, Tinkering.Wagons.Of(sim, a.Party));
        if (!hauls)
        {
            foreach (var e in party)
                w.Get<Adventurer>(e).Satisfaction -= Tinkering.Wagons.BrokenMood;
            sim.Speech.Say(sim, member, "And the wagon's still broken. Whatever we find out there stays there, then.", Volume.Say);
        }
        foreach (var e in party)
            w.Get<Adventurer>(e).Quest = questId;
        var ex = new Expedition
        {
            Quest = questId, Party = a.Party, PartyDef = a.PartyDef, Stated = intel.Danger, BrokerName = w.Get<Creature>(broker).Name, Hauls = hauls,
        };
        _out.Add(ex);
        // Nobody sets out with their sword still at the smith's, if they can help it.
        if (party.FindIndex(e => sim.Shops.GearOut(sim, e)) is var i and >= 0)
        {
            var waiting = party[i];
            ex.SetOutBy = sim.Clock.Tick + SimClock.HoursToTicks(Shops.ShopSystem.HandedOverHours);
            ex.ReturnTick = long.MaxValue;
            sim.Speech.Say(sim, waiting, "Soon as I've my gear back from mending, we're off.", Volume.Say);
        }
        else
            SetOut(sim, ex, party);
        return true;
    }

    /// <summary>The party leaves for its quest: the clock on it starts now.</summary>
    private static void SetOut(Simulation sim, Expedition ex, List<Entity> party)
    {
        var w = sim.World;
        ex.SetOutBy = 0;
        ex.ReturnTick = sim.Clock.Tick + SimClock.HoursToTicks(sim.Expeditions.Find(ex.Quest)!.Def.Hours + 2 * TravelHours);
        foreach (var e in party)
        {
            w.Add(e, new Leaving());
            AiSystem.EndJob(sim, e, w.Get<Mind>(e), false);
        }
        sim.Messages.Add(MessageKind.Info, $"{Capital(ex.PartyDef.Label)} set out for {sim.Expeditions.Find(ex.Quest)!.Def.Label}.",
            party.Count > 0 ? w.Get<Position>(party[0]).Cell : default);
    }

    /// <summary>
    /// Parties that took a quest while gear of theirs was out for mending: off once it's all back,
    /// or once they've waited long enough. If nobody's left in town to go, the quest's open again.
    /// </summary>
    private void SetOutWhenReady(Simulation sim)
    {
        var now = sim.Clock.Tick;
        for (var i = 0; i < _out.Count; i++)
        {
            var ex = _out[i];
            if (ex.SetOutBy == 0)
                continue;
            var party = PartyInTown(sim, ex.Party).FindAll(e => !sim.World.Get<Creature>(e).Dead);
            if (party.Count == 0)
            {
                if (Find(ex.Quest) is { } quest)
                    quest.Status = QuestStatus.Open;
                _out.RemoveAt(i--);
                continue;
            }
            if (now >= ex.SetOutBy || !party.Exists(e => sim.Shops.GearOut(sim, e)))
                SetOut(sim, ex, party);
        }
    }

    /// <summary>
    /// A member reaching the map edge on their way to a quest (<see cref="Simulation.Depart"/>):
    /// they're written down to be put back on return. False if their party isn't out any more.
    /// </summary>
    public bool Embark(Simulation sim, Entity e)
    {
        if (!sim.World.TryGet<Adventurer>(e, out var a) || a.Quest == 0
            || _out.Find(x => x.Quest == a.Quest && x.Party == a.Party) is not { } ex)
            return false;
        ex.Members.Add(Record(sim, e));
        return true;
    }

    /// <summary>Who someone is and what they carry, as a record.</summary>
    public static MemberRecord Record(Simulation sim, Entity e)
    {
        var w = sim.World;
        var a = w.Get<Adventurer>(e);
        var skills = w.Get<Skills>(e);
        var m = new MemberRecord
        {
            Name = w.Get<Creature>(e).Name, Kind = a.Kind, PartyDef = a.PartyDef, Party = a.Party, LookId = Clothing.LookId(sim, e),
            AgeDays = w.Get<Creature>(e).AgeDays(sim.Clock.Tick), Melee = skills[Skill.Melee], Shooting = skills[Skill.Shooting],
            Coin = Shops.Coin.Count(sim, e), Satisfaction = a.Satisfaction,
            Food = w.TryGet<Needs>(e, out var needs) ? needs.Get(sim.Defs.Need("Food")) : 1f,
        };
        if (w.TryGet<Equipment>(e, out var eq) && w.IsAlive(eq.Weapon))
            m.Weapon = Gear(sim, eq.Weapon);
        foreach (var item in sim.Worn(e))
            if (!sim.IsPack(item))
                m.Apparel.Add(Gear(sim, item));
        if (w.TryGet<Health>(e, out var h))
        {
            m.Blood = h.Blood;
            m.Pain = Math.Clamp(h.Pain, 0f, 1f);
            foreach (var inj in h.Injuries)
                m.Wounds.Add(new WoundRecord(inj.Part, inj.Type, inj.Damage, inj.Tended, inj.Quality, inj.Absorbed));
        }
        m.Charms = Healing.Charms.Records(sim, e);
        m.Contraptions = Tinkering.Contraptions.Records(sim, e);
        m.Rations = Work.Rations.Records(sim, e);
        return m;
    }

    private static GearRecord Gear(Simulation sim, Entity item)
    {
        var t = sim.World.Get<Thing>(item);
        return new GearRecord(t.Def, t.Material, sim.World.TryGet<Crafted>(item, out var c) ? c.Quality : null,
            WeaponWear.HpFraction(sim, item), WeaponWear.Dullness(sim, item));
    }

    // ---------------- How it went ----------------

    /// <summary>
    /// The quest's true danger against what the party took with them. The stronger they were for
    /// it, the fewer die, the lighter the wounds and the less their gear suffers; strong enough,
    /// they bring back the reward.
    /// </summary>
    private void Resolve(Simulation sim, Expedition ex)
    {
        var quest = Find(ex.Quest)!;
        if (ex.Members.Count == 0)
        {
            quest.Status = QuestStatus.Open;
            return;
        }
        // Contraptions that bind may jam on the road, and do nothing all the way (they still come home).
        var jammed = new HashSet<Tinkering.ContraptionRecord>(
            Tinkering.Contraptions.Jam(ex.Members.SelectMany(m => m.Contraptions), _rand.NextFloat), ReferenceEqualityComparer.Instance);
        // Rations are eaten on the way: how they served each traveller, then they're gone.
        var hoursOut = quest.Def.Hours + 2 * TravelHours;
        var fedBy = new Dictionary<MemberRecord, float>(ReferenceEqualityComparer.Instance);
        foreach (var m in ex.Members)
        {
            fedBy[m] = Work.Rations.StrengthFactor(sim, m.Rations, hoursOut);
            m.Fed = m.Rations.Count == 0 ? RoadFood.None : Work.Rations.Turned(sim, m.Rations[0], hoursOut) ? RoadFood.Sick : RoadFood.Fed;
            if (m.Rations.Count > 0)
                m.Rations.RemoveAt(0);
        }
        var strength = ex.Members.Sum(m => Strength(m, quest.Def, jammed, fedBy[m]));
        var ratio = strength / quest.Danger * _rand.Range(0.85f, 1.15f);
        var success = ratio >= 0.9f;
        var hit = Math.Clamp(ratio, 0.5f, 2f);
        var survivors = new List<MemberRecord>();
        var warded = 0;
        var caught = 0;
        var rocket = (Tinkering.ContraptionRecord?)null;
        foreach (var m in ex.Members)
        {
            var frail = Math.Clamp(1f - 0.8f * m.Pain - (1f - m.Blood), 0.2f, 1f) < 0.5f;
            var death = Math.Clamp(0.55f - 0.5f * ratio, 0f, 0.6f) * (frail ? 1.5f : 1f);
            if (_rand.Chance(death))
            {
                // A charm that heals may turn it into a bad wound instead (the roll only if they carry one);
                // then a grapnel; then a rocket, once one's gone up, brings help in time.
                if (m.Charms.Count > 0 && Healing.Charms.Spare(m.Charms, _rand.NextFloat()) != null)
                    warded++;
                else if (Spared(m, ex, jammed, ref rocket))
                    caught++;
                else
                {
                    Dead++;
                    continue;
                }
                m.Wounds.Add(new WoundRecord(-1, quest.Def.Threats[_rand.NextInt(quest.Def.Threats.Count)], 14f / hit, false, 0f, false));
            }
            var wounds = _rand.NextInt(2) + (ratio < 1f ? 2 : ratio < 1.5f ? 1 : 0);
            for (var i = 0; i < wounds; i++)
            {
                var type = quest.Def.Threats[_rand.NextInt(quest.Def.Threats.Count)];
                var damage = _rand.Range(4f, 12f) / hit;
                // A tapping rod finds some of it before it finds them.
                if (Working(m, jammed).FirstOrDefault(c => c.Use == ContraptionUse.Traps) is { } rod && _rand.Chance(Tinkering.Contraptions.TrapsAvoided(rod)))
                    continue;
                // Charms ward what they fit: the first that fits spends itself on it.
                var before = m.Charms.Count;
                damage = Healing.Charms.Ward(m.Charms, type, damage);
                warded += before - m.Charms.Count;
                if (damage > 0.5f)
                    m.Wounds.Add(new WoundRecord(-1, type, damage, false, 0f, false));
            }
            if (m.Weapon is { } wpn)
                m.Weapon = wpn with { Hp = Math.Max(0.05f, wpn.Hp - _rand.Range(0.1f, 0.3f) / hit), Dullness = WeaponWear.Edged(wpn.Def) ? Math.Min(1f, wpn.Dullness + _rand.Range(0.2f, 0.5f)) : 0f };
            m.Apparel = m.Apparel.Select(a => a with { Hp = Math.Max(0.05f, a.Hp - _rand.Range(0.05f, 0.25f) / hit) }).ToList();
            m.Food = m.Fed == RoadFood.Fed ? Work.Rations.FedOnReturn : 0.35f;
            survivors.Add(m);
        }
        if (success)
        {
            Succeeded++;
            quest.Status = QuestStatus.Cleared;
            // Most of it in their purses; the rest locked in a strongbox they bring back (Tinkering/).
            foreach (var m in survivors)
                m.Coin += (quest.Reward - BoxShare(quest)) / Math.Max(1, survivors.Count);
        }
        else
        {
            Failed++;
            quest.Status = QuestStatus.Open;
        }
        if (survivors.Count == 0)
        {
            Tinkering.Wagons.Leave(sim, ex.Party); // nobody to come back for it
            sim.Messages.Add(MessageKind.Warning, $"No one comes back from {quest.Def.Label}.", null);
            return;
        }
        if (warded > 0)
            sim.Messages.Add(MessageKind.Good, $"{Capital(ex.PartyDef.Label)} spent {warded} {(warded == 1 ? "charm" : "charms")} on the road to {quest.Def.Label}.", null);
        if (caught > 0)
            sim.Messages.Add(MessageKind.Good, $"{Capital(ex.PartyDef.Label)} owe {caught} {(caught == 1 ? "life" : "lives")} to a tinker's work at {quest.Def.Label}.", null);
        foreach (var c in jammed)
            sim.Messages.Add(MessageKind.Warning, $"A {c.Def.Label} {ex.PartyDef.Label} carried jammed on the road to {quest.Def.Label}.", null);
        Return(sim, ex, quest, survivors, success);
    }

    /// <summary>
    /// A death on a quest, against a tinker's work: a grapnel may catch the fall (and is spent), or
    /// once a rocket's gone up (the first death fires it), help may come in time. True if they live.
    /// </summary>
    private bool Spared(MemberRecord m, Expedition ex, ISet<Tinkering.ContraptionRecord> jammed, ref Tinkering.ContraptionRecord? rocket)
    {
        if (Working(m, jammed).FirstOrDefault(c => c.Use == ContraptionUse.Falls) is { } grapnel)
        {
            m.Contraptions.Remove(grapnel);
            if (_rand.Chance(Tinkering.Contraptions.FallCaught(grapnel)))
                return true;
        }
        if (rocket == null)
            foreach (var bearer in ex.Members)
                if (Working(bearer, jammed).FirstOrDefault(c => c.Use == ContraptionUse.Rescue) is { } fired)
                {
                    bearer.Contraptions.Remove(fired);
                    rocket = fired;
                    break;
                }
        return rocket != null && _rand.Chance(Tinkering.Contraptions.Rescued(rocket));
    }

    /// <summary>The survivors walk back in from an edge, worn and wounded and maybe richer, with a tale to tell at the tavern.</summary>
    private void Return(Simulation sim, Expedition ex, Quest quest, List<MemberRecord> survivors, bool success)
    {
        var w = sim.World;
        var toward = Adventurers.PartySystem.HauntOf(sim, ex.PartyDef)?.Anchor ?? sim.Creatures().Select(e => w.Get<Position>(e).Cell).FirstOrDefault();
        int3? entry = null;
        var first = _rand.NextInt(4);
        for (var i = 0; i < 4 && entry == null; i++)
            entry = Raids.EdgeEntry(sim, (first + i) % 4, toward);
        if (entry is not { } from)
            return;
        var spots = Raids.GatherSpots(sim, from, toward, survivors.Count);
        var back = new List<Entity>();
        for (var i = 0; i < spots.Count; i++)
            back.Add(Respawn(sim, survivors[i], spots[i]));
        // They've been here before: they know their way round the town now.
        foreach (var e in back)
            Hospitality.DirectionsSystem.KnowsTheTown(sim, e);
        var lost = ex.Members.Count - survivors.Count;
        var word = DangerWord(quest.Danger);
        var told = MathF.Abs(ex.Stated - quest.Danger) > 1f
            ? $" {ex.BrokerName} told us it was {DangerWord(ex.Stated)}."
            : "";
        var tale = success
            ? $"We've cleared {quest.Def.Label}. It was {word}.{told}{(lost > 0 ? $" {lost} of us didn't come back." : "")}"
            : $"We couldn't do it. {Capital(quest.Def.Label)} is {word}.{told}{(lost > 0 ? $" We left {lost} of ours out there." : "")}";
        // What they found comes home on the wagon, if it's fit to carry it (or they never had one).
        if (success && back.Count > 0 && ex.Hauls)
        {
            Inventory.Stow(sim, back[^1], Tinkering.Lockwork.Make(sim, _rand, quest.Danger, BoxShare(quest), quest.Def.Label));
            for (var i = 0; i < 2 && _rand.Chance(i == 0 ? Tinkering.Curios.FoundChance : 0.3f); i++)
                if (Tinkering.Curios.Make(sim, _rand, quest.Danger) is { IsNull: false } curio)
                    Inventory.Stow(sim, back[i % back.Count], curio);
        }
        else if (success && !ex.Hauls)
            tale += " We had to leave what we found: the wagon wouldn't carry it.";
        // What they ate on the way: the cook's only word of how the rations did.
        var sick = survivors.Count(m => m.Fed == RoadFood.Sick);
        var fed = survivors.Count(m => m.Fed == RoadFood.Fed);
        if (sick > 0)
            tale += sick == survivors.Count
                ? " And something in the inn's rations had turned: we were sick half the way."
                : " Something in the inn's rations had turned, too: some of us were sick half the way.";
        else if (fed > 0)
            tale += " The inn's rations kept us going.";
        if (back.Count > 0)
            w.Add(back[0], new ReportPending { Quest = quest.Id, Danger = quest.Danger, Text = tale });
        // How the broker's word held up counts with them.
        if (told.Length > 0)
            foreach (var e in back)
                w.Get<Adventurer>(e).Satisfaction -= 1.5f;
        sim.Messages.Add(success ? MessageKind.Good : MessageKind.Warning,
            $"{Capital(ex.PartyDef.Label)} return from {quest.Def.Label}{(success ? ", laden" : ", beaten")}: {survivors.Count} of {ex.Members.Count}.", from);
    }

    /// <summary>The part of a quest's reward that comes back locked in a strongbox.</summary>
    public static int BoxShare(Quest quest) => quest.Reward * 2 / 5;

    private Entity Respawn(Simulation sim, MemberRecord m, int3 cell)
    {
        var w = sim.World;
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), m.Name, cell, Faction.Adventurer, ageDays: m.AgeDays, rand: _rand);
        w.Add(e, new LookedAs { Id = m.LookId });
        if (m.Kind.OutfitDef != null)
            Clothing.Dress(sim, e, m.Kind.OutfitDef, Clothing.StreamFor(sim, e));
        foreach (var a in m.Apparel)
            sim.WearItem(e, Remake(sim, a));
        if (sim.Defs.TryThing(Simulation.StartingPack, out var pack))
            sim.Wear(e, pack);
        if (m.Weapon is { } wpn)
            sim.EquipItem(e, Remake(sim, wpn));
        var skills = w.Get<Skills>(e);
        skills.Level[(int)Skill.Melee] = m.Melee;
        skills.Level[(int)Skill.Shooting] = m.Shooting;
        Array.Fill(w.Get<Needs>(e).Values, 0.6f);
        w.Get<Needs>(e).Values[sim.Defs.Need("Food").Index] = m.Food;
        if (m.Coin > 0)
            Inventory.Stow(sim, e, sim.CreateItem(sim.Defs.Thing(Shops.Coin.Silver), m.Coin));
        Healing.Charms.Restore(sim, e, m.Charms);
        Tinkering.Contraptions.Restore(sim, e, m.Contraptions);
        Work.Rations.Restore(sim, e, m.Rations);
        if (m.Fed == RoadFood.Sick)
            FoodPoisoning.Sicken(sim, e, 0.6f);
        foreach (var wound in m.Wounds)
            if (HealthSystem.Damage(sim, e, wound.Damage, wound.Type, wound.Part) is { } inj)
            {
                inj.Tended = wound.Tended;
                inj.Quality = wound.TendQuality;
                inj.Absorbed = wound.Absorbed;
            }
        w.Add(e, new Adventurer
        {
            Party = m.Party, Kind = m.Kind, PartyDef = m.PartyDef, Satisfaction = m.Satisfaction,
            LeaveTick = sim.Clock.Tick + SimClock.HoursToTicks(StayAfterHours), NextDrink = sim.Clock.Tick + SimClock.HoursToTicks(0.5f),
            Arrived = sim.Clock.Tick,
        });
        return e;
    }

    private static Entity Remake(Simulation sim, GearRecord g)
    {
        var item = sim.CreateItem(g.Def, 1, g.Material);
        ref var t = ref sim.World.Get<Thing>(item);
        t.Hp = Math.Max(1f, t.MaxHp * g.Hp);
        if (g.Quality is { } q)
            sim.World.Add(item, new Crafted { Quality = q, Maker = Entity.Null });
        WeaponWear.SetDullness(sim, item, g.Dullness);
        return item;
    }

    /// <summary>
    /// A returned party's tale, told the first time anyone outside the party is near enough to
    /// catch it clearly (the news comes back with them, wherever they go first): whoever hears it
    /// knows how it really was.
    /// </summary>
    private void Report(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Query<ReportPending>())
        {
            if (!w.IsAlive(e) || !w.TryGet<Adventurer>(e, out var a) || !SpeechSystem.CanAct(sim, e))
                continue;
            // A tale wants an audience: they hold it until someone outside the party would hear it clearly.
            if (!Audience(sim, e, a.Party))
                continue;
            var report = w.Get<ReportPending>(e);
            if (sim.Speech.Say(sim, e, report.Text, Volume.Shout) is not { } said)
                continue;
            w.Remove<ReportPending>(e);
            var reward = Find(report.Quest)?.Reward;
            foreach (var h in said.Listeners)
                if (h.Clarity == Clarity.Clear)
                    Hear(h.Listener, report.Quest, report.Danger, reward, 1f);
        }
    }

    /// <summary>Is anyone not of the party (awake, a person) near enough to catch a shouted tale clearly?</summary>
    private static bool Audience(Simulation sim, Entity teller, int party)
    {
        var w = sim.World;
        var at = w.Get<Position>(teller).Cell;
        foreach (var e in sim.Creatures())
            if (e != teller && !(w.TryGet<Adventurer>(e, out var a) && a.Party == party) && w.Get<Creature>(e).Def.Race == Race.Human
                && SpeechSystem.CanAct(sim, e) && SpeechSystem.Hear(sim, at, w.Get<Position>(e).Cell, Volume.Shout) == Clarity.Clear)
                return true;
        return false;
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ---------------- Saving ----------------

    public void Save(SaveWriter w, Simulation sim)
    {
        var (a, b, c, d) = _rand.State;
        w.UInt(a); w.UInt(b); w.UInt(c); w.UInt(d);
        w.Int(_nextQuest);
        w.Long(_nextRumour);
        w.Bool(_rolled);
        w.Int(Succeeded);
        w.Int(Failed);
        w.Int(Dead);
        w.Int(_quests.Count);
        foreach (var q in _quests)
        {
            w.Int(q.Id);
            w.Def(q.Def);
            w.Float(q.Danger);
            w.Int(q.Reward);
            w.Byte((byte)q.Status);
        }
        var intel = _intel.OrderBy(kv => kv.Key.Who.Index).ThenBy(kv => kv.Key.Quest).ToList();
        w.Int(intel.Count);
        foreach (var ((who, quest), i) in intel)
        {
            w.Entity(who);
            w.Int(quest);
            w.Float(i.Danger);
            w.Float(i.Reward);
            w.Float(i.Confidence);
        }
        w.Int(_out.Count);
        foreach (var ex in _out)
        {
            w.Int(ex.Quest);
            w.Int(ex.Party);
            w.Def(ex.PartyDef);
            w.Float(ex.Stated);
            w.String(ex.BrokerName);
            w.Long(ex.ReturnTick);
            w.Bool(ex.Hauls);
            w.Long(ex.SetOutBy);
            w.Int(ex.Members.Count);
            foreach (var m in ex.Members)
                WriteMember(w, m);
        }
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _rand.State = (r.UInt(), r.UInt(), r.UInt(), r.UInt());
        _nextQuest = r.Int();
        _nextRumour = r.Long();
        _rolled = r.Bool();
        Succeeded = r.Int();
        Failed = r.Int();
        Dead = r.Int();
        _quests.Clear();
        for (var n = r.Count(1 << 10); n > 0; n--)
        {
            var q = new Quest { Id = r.Int(), Def = r.Def<QuestDef>(), Danger = r.Float(), Reward = r.Int() };
            var status = r.Byte();
            if (!Enum.IsDefined((QuestStatus)status))
                throw new SaveException($"Corrupt save: quest status {status}");
            q.Status = (QuestStatus)status;
            _quests.Add(q);
        }
        _intel.Clear();
        for (var n = r.Count(1 << 20); n > 0; n--)
            _intel[(r.Entity(), r.Int())] = new Intel { Danger = r.Float(), Reward = r.Float(), Confidence = r.Float() };
        _out.Clear();
        for (var n = r.Count(1 << 10); n > 0; n--)
        {
            var ex = new Expedition { Quest = r.Int(), Party = r.Int(), PartyDef = r.Def<PartyDef>(), Stated = r.Float(), BrokerName = r.String(), ReturnTick = r.Long() };
            if (r.Version >= 7)
                ex.Hauls = r.Bool();
            if (r.Version >= 19)
                ex.SetOutBy = r.Long(); // waiting on mended gear to set out came with format 19
            for (var k = r.Count(64); k > 0; k--)
                ex.Members.Add(ReadMember(r));
            _out.Add(ex);
        }
    }

    private static void WriteGear(SaveWriter w, GearRecord? g)
    {
        w.Bool(g != null);
        if (g == null)
            return;
        w.Def(g.Def);
        w.Def(g.Material);
        w.Int(g.Quality is { } q ? (int)q : -1);
        w.Float(g.Hp);
        w.Float(g.Dullness);
    }

    private static GearRecord? ReadGear(SaveReader r)
    {
        if (!r.Bool())
            return null;
        var def = r.Def<ThingDef>();
        var material = r.DefOrNull<MaterialDef>();
        var q = r.Int();
        if (q < -1 || q > (int)Quality.Masterwork)
            throw new SaveException($"Corrupt save: quality {q}");
        return new GearRecord(def, material, q < 0 ? null : (Quality)q, r.Float(), r.Float());
    }

    private static void WriteMember(SaveWriter w, MemberRecord m)
    {
        w.String(m.Name);
        w.Def(m.Kind);
        w.Def(m.PartyDef);
        w.Int(m.Party);
        w.Int(m.LookId);
        w.Float(m.AgeDays);
        w.Int(m.Melee);
        w.Int(m.Shooting);
        w.Int(m.Coin);
        w.Float(m.Satisfaction);
        w.Float(m.Blood);
        w.Float(m.Pain);
        w.Float(m.Food);
        WriteGear(w, m.Weapon);
        w.Int(m.Apparel.Count);
        foreach (var a in m.Apparel)
            WriteGear(w, a);
        w.Int(m.Wounds.Count);
        foreach (var x in m.Wounds)
        {
            w.Int(x.Part);
            w.Int((int)x.Type);
            w.Float(x.Damage);
            w.Bool(x.Tended);
            w.Float(x.TendQuality);
            w.Bool(x.Absorbed);
        }
        w.Int(m.Charms.Count);
        foreach (var c in m.Charms)
        {
            w.Def(c.Mending);
            w.Int(c.Extra);
            w.Float(c.Quality);
            w.Entity(c.Maker);
        }
        w.Int(m.Contraptions.Count);
        foreach (var c in m.Contraptions)
        {
            w.Def(c.Def);
            w.Int((int)c.Quality);
            w.Bool(c.Binding);
            w.Entity(c.Maker);
        }
        w.Int(m.Rations.Count);
        foreach (var x in m.Rations)
        {
            w.Float(x.Quality);
            w.Float(x.Rot);
            w.Bool(x.LooseWrap);
            w.Entity(x.Maker);
        }
        w.Byte((byte)m.Fed);
    }

    private static MemberRecord ReadMember(SaveReader r)
    {
        var m = new MemberRecord { Name = r.String(), Kind = r.Def<AdventurerKindDef>(), PartyDef = r.Def<PartyDef>() };
        m.Party = r.Int();
        m.LookId = r.Int();
        m.AgeDays = r.Float();
        m.Melee = r.Int();
        m.Shooting = r.Int();
        m.Coin = r.Int();
        m.Satisfaction = r.Float();
        m.Blood = r.Float();
        m.Pain = r.Float();
        m.Food = r.Float();
        m.Weapon = ReadGear(r);
        for (var n = r.Count(32); n > 0; n--)
            m.Apparel.Add(ReadGear(r)!);
        for (var n = r.Count(256); n > 0; n--)
            m.Wounds.Add(new WoundRecord(r.Int(), (DamageType)r.Int(), r.Float(), r.Bool(), r.Float(), r.Bool()));
        if (r.Version >= 5)
            for (var n = r.Count(64); n > 0; n--)
                m.Charms.Add(new Healing.CharmRecord(r.Def<MendingDef>(), Math.Clamp(r.Int(), 0, MendingDef.MaxRunes), r.Float(), r.Entity()));
        if (r.Version >= 7)
            for (var n = r.Count(64); n > 0; n--)
            {
                var def = r.Def<ThingDef>();
                var q = r.Int();
                if (def.Contraption == null || q < 0 || q > (int)Quality.Masterwork)
                    throw new SaveException($"Corrupt save: contraption {def.Name}, quality {q}");
                m.Contraptions.Add(new Tinkering.ContraptionRecord(def, (Quality)q, r.Bool(), r.Entity()));
            }
        if (r.Version >= 21)
        {
            for (var n = r.Count(64); n > 0; n--)
                m.Rations.Add(new Work.RationRecord(Math.Clamp(r.Float(), 0f, 1f), Math.Clamp(r.Float(), 0f, 1f), r.Bool(), r.Entity()));
            var fed = r.Byte();
            m.Fed = Enum.IsDefined((RoadFood)fed) ? (RoadFood)fed : RoadFood.None;
        }
        return m;
    }
}
