using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Healing;

/// <summary>
/// Binding wounds by hand (<see cref="Bandaging"/>): anyone's, your own included, and the
/// empath's mana-free fallback. Each wound takes a few turns of the cloth; for each, hold F to
/// pull it tight and let go with the tension in the band. Too slack and the turn slips; past the
/// band and it bites, and it's on too tight. How well the turns sat, with Medicine, is the
/// dressing's quality.
/// </summary>
public sealed class BandageSession : WorkSession
{
    /// <summary>Turns of cloth per wound, and the most a sitting binds.</summary>
    public const int TurnsPerWound = 3, MaxTurns = 12;

    /// <summary>The tension that sits right (before skill widens it), and past which it's too tight whatever you do.</summary>
    public const float BandLow = 0.5f, BandHigh = 0.75f, Crush = 0.95f;

    /// <summary>How fast the cloth slackens when let go, per step.</summary>
    public const float EasePerStep = 0.05f;

    public Entity Patient { get; set; } = Entity.Null;
    public string PatientName { get; set; } = "";
    public int Turns { get; set; } = TurnsPerWound;

    public int TurnsDone { get; private set; }
    public int Slack { get; private set; }
    public int Tight { get; private set; }

    /// <summary>The cloth's pull, 0..1.</summary>
    public float Tension { get; private set; }

    /// <summary>How the last turn sat, for the scene: 1 right, 0 slack, 2 too tight, -1 none yet.</summary>
    public int Last { get; private set; } = -1;

    private float _rise;
    private bool _pulling;

    public float Low => (BandLow + BandHigh) / 2f - (BandHigh - BandLow) / 2f * Hands.Tolerance;
    public float High => (BandLow + BandHigh) / 2f + (BandHigh - BandLow) / 2f * Hands.Tolerance;

    public override Skill Skill => Skill.Medicine;
    public override float Xp => 10f;
    public override string Title => Patient == Worker ? "Binding your wounds" : $"Binding {PatientName}'s wounds";

    public static void TryStart(Simulation sim, Entity binder, Entity patient)
    {
        if (Bandaging.CantBind(sim, binder, patient) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, $"Can't bandage: {why}.", sim.LocationOf(binder));
            return;
        }
        var wounds = Math.Min(Bandaging.WoundsToBind(sim, patient, binder), Bandaging.BandagesOn(sim, binder));
        WorkSystem.Start(sim, binder, new BandageSession
        {
            Patient = patient, PatientName = sim.World.Get<Creature>(patient).Name,
            Turns = Math.Clamp(wounds * TurnsPerWound, TurnsPerWound, MaxTurns),
        });
        var w = sim.World;
        if (patient != binder && !w.Has<Avatar>(patient) && w.Has<Mind>(patient) && w.Get<Creature>(patient).Able)
            AiSystem.Order(sim, patient, new HoldStillJob(binder));
    }

    public override bool StillValid(Simulation sim) => Bandaging.CantBind(sim, Worker, Patient) == null;

    protected internal override void Begin() => NextTurn();

    /// <summary>Each turn of cloth pulls up a little differently: watch it, don't count.</summary>
    private void NextTurn() => _rise = Rand.Range(0.008f, 0.015f);

    protected override void Step()
    {
        if (Held)
        {
            _pulling = true;
            Tension = Math.Min(1f, Tension + _rise * (1f + (Rand.NextFloat() - 0.5f) * Hands.Tremor * 2f));
            if (Tension >= Crush)
                Judge(); // it bites before you let go
            return;
        }
        if (_pulling)
            Judge();
        Tension = Math.Max(0f, Tension - EasePerStep);
    }

    private void Judge()
    {
        _pulling = false;
        if (Tension > High)
        {
            Tight++;
            Last = 2;
            Sample(0.2f);
        }
        else if (Tension < Low)
        {
            Slack++;
            Last = 0;
            Sample(0.3f);
        }
        else
        {
            Last = 1;
            Sample(1f);
        }
        Tension = Math.Min(Tension, Low * 0.5f); // the turn's laid; the next starts from loose
        TurnsDone++;
        NextTurn();
        Done = TurnsDone >= Turns;
    }

    public override void Finish(Simulation sim)
    {
        if (Bandaging.CantBind(sim, Worker, Patient) != null)
            return;
        var medicine = SkillRules.LevelOf(sim, Worker, Skill.Medicine);
        var quality = Math.Clamp(0.15f + 0.55f * Quality + 0.025f * medicine, 0.05f, 1f);
        Bandaging.Bind(sim, Worker, Patient, quality);
        if (Tight * 3 >= Turns)
            sim.Messages.Add(MessageKind.Warning, Patient == Worker ? "You've bound it too tight. It throbs under the cloth."
                : $"{PatientName} winces: you've bound it too tight.", sim.LocationOf(Patient));
        else if (Slack * 3 >= Turns)
            sim.Messages.Add(MessageKind.Info, "The dressing's loose. It'll do, barely.", sim.LocationOf(Patient));
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Patient);
        w.String(PatientName);
        w.Int(Turns);
        w.Int(TurnsDone);
        w.Int(Slack);
        w.Int(Tight);
        w.Float(Tension);
        w.Int(Last);
        w.Float(_rise);
        w.Bool(_pulling);
    }

    protected override void ReadState(SaveReader r)
    {
        Patient = r.Entity();
        PatientName = r.String();
        Turns = Math.Clamp(r.Int(), 1, MaxTurns);
        TurnsDone = r.Int();
        Slack = r.Int();
        Tight = r.Int();
        Tension = r.Float();
        Last = r.Int();
        _rise = r.Float();
        _pulling = r.Bool();
    }
}
