using Narsil.Rendering;
using SupportingCast.Sim;
using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Ecs;
using SupportingCast.Sim.Environment;
using SupportingCast.Sim.Maps;

namespace SupportingCast.Rendering;

/// <summary>What a creature is visibly doing this frame, read from sim state. Input to <see cref="CreatureAnimator.ChooseTag"/>.</summary>
public readonly record struct AnimInputs(
    bool Dead,
    bool Downed,
    bool Asleep,
    bool Breathing,
    bool WindingUp,
    bool Airborne,
    int VerticalStep,
    bool Landing,
    bool Soaring,
    bool Working,
    bool Moving,
    bool Hatching = false,
    string? Care = null
);

/// <summary>The resolved pose for one creature this frame.</summary>
public readonly record struct CreaturePose(
    SpriteSheet Sheet,
    SheetAnim? Anim,
    SheetFrame Frame,
    string Tag,
    int Facing,
    float Roll,
    bool EventFired,
    bool TagStarted
);

/// <summary>
/// Render-side animation for creatures with a <see cref="SpriteSheet"/>. Picks an animation tag from
/// sim state (never writes to the sim), keeps per-creature playback time, facing (left/right flip
/// from the screen-space direction of travel or of the work/breath target) and a smoothed bank
/// for flyers. Time comes from <see cref="Advance"/>: real time scaled by game speed, so animations
/// pause with the game, and plain real time during combat turns when the sim clock stands still.
/// </summary>
public sealed class CreatureAnimator
{
    private sealed class State
    {
        public int Generation;
        public string Tag = "";

        /// <summary>A one-shot tag that has played out; we stay on its fallback until something else is wanted.</summary>
        public string? Done;
        public string Want = "";
        public float TagTime;
        public int Facing = 1;

        /// <summary>Which way it faces on screen: "down" (towards the viewer), "up" (away) or "side".</summary>
        public string View = "down";
        public float Roll;
        public float LastHeading = float.NaN;
        public int LastFrame = -1;
        public BreathEvent? Breath;
        public float NextZzz;
        public long LastDrawn;
        public float3 Mouth;
        public SheetFrame Shown;
    }

    private readonly Dictionary<int, State> _states = [];
    private long _frame;
    private float _dt;

    /// <summary>How long after birth a creature plays its <c>hatch</c> animation (game ticks; 4 s at 1x).</summary>
    public const int HatchTicks = 240;

    /// <summary>
    /// A soaring flyer's rhythm (seconds): a few wingbeats, then a glide. The glide frames are a
    /// held pose, so soaring on them alone looked like frozen wings.
    /// </summary>
    public const float SoarFlapSeconds = 1.5f, SoarCycleSeconds = 4f;

    /// <summary>In the glide part of its soaring rhythm, offset per creature so a flock doesn't beat in time.</summary>
    private bool GlidingNow(Entity e)
    {
        var t = (Clock + (SimRandom.Hash(e.Index, 5, 11) & 1023) / 1023f * SoarCycleSeconds) % SoarCycleSeconds;
        return t >= SoarFlapSeconds;
    }

    /// <summary>Animation time in seconds; advances with game speed.</summary>
    public float Clock { get; private set; }

    /// <summary>Seconds of animation time since the previous frame.</summary>
    public float Dt => _dt;

    public void Advance(float dt)
    {
        _dt = dt;
        Clock += dt;
        if (++_frame % 300 == 0)
            foreach (var key in _states.Where(kv => _frame - kv.Value.LastDrawn > 300).Select(kv => kv.Key).ToList())
                _states.Remove(key);
    }

    /// <summary>
    /// Priority order: lying states, then breath, flight, work, movement. One-shot tags
    /// (<c>takeoff</c>, <c>land</c>, <c>breathe</c>) are chosen here and fall back when finished.
    /// </summary>
    public static string ChooseTag(in AnimInputs s)
    {
        if (s.Dead || s.Downed)
            return "downed";
        if (s.Hatching)
            return "hatch";
        if (s.Asleep)
            return "sleep";
        if (s.Breathing)
            return "breathe";
        if (s.WindingUp)
            return "breathe_windup";
        if (s.Care != null && !s.Airborne && !s.Moving)
            return s.Care;
        if (s.Airborne)
        {
            if (s.Landing)
                return "land";
            if (s.VerticalStep > 0)
                return "takeoff";
            return s.Soaring && s.VerticalStep == 0 ? "glide" : "fly";
        }
        if (s.Working)
            return "work";
        return s.Moving ? "walk" : "idle";
    }

    /// <summary>How long (ticks) a dragon keeps leaning into a scratch or a meal from the hand after it.</summary>
    private const int CareAfterglowTicks = 90;

    /// <summary>What to show when a sheet lacks a tag, or a one-shot tag has finished.</summary>
    public static string? Fallback(string tag) => tag switch
    {
        "glide" or "takeoff" or "land" => "fly",
        "fly" or "breathe_windup" or "work" or "walk" or "sleep" => "idle",
        "breathe" => "breathe_windup",
        "downed" => "sleep",
        "hatch" or "pet" or "play" or "groom" or "ride" => "idle",
        _ => null,
    };

    /// <summary>
    /// The screen view for a heading: <paramref name="side"/> is its part along the screen's right,
    /// <paramref name="away"/> along the camera's heading (up the screen, away from the viewer).
    /// People have no profile: mostly toward or away from the camera is "down" or "up"; anything
    /// with a real sideways part is a three-quarter view, "downside" (turned toward the viewer, the
    /// default for straight across) or "upside" (turned away), flipped for leftward.
    /// </summary>
    public static string ViewOf(float side, float away)
    {
        var across = MathF.Abs(side);
        if (away > 0)
            return across < away * 0.45f ? "up" : across < away * 2.4f ? "upside" : "downside";
        return across < -away * 0.45f ? "down" : "downside";
    }

    /// <summary>Views drawn facing right, flipped for leftward headings (front and back aren't).</summary>
    public static bool Flips(string view) => view is "side" or "downside" or "upside";

    /// <summary>
    /// A sheet's version of a tag for a view (<c>walk_up</c>), if it has one: JRPG-style people
    /// face four ways; creatures drawn side-on only (dragons, animals) just have the plain tag.
    /// </summary>
    public static SheetAnim? Directional(SpriteSheet sheet, string tag, string view)
    {
        if (!DirectionalNames.TryGetValue((tag, view), out var name))
            DirectionalNames[(tag, view)] = name = $"{tag}_{view}";
        if (sheet.Find(name) is { } anim)
            return anim;
        // Older (hand-drawn) sheets have a profile instead of the three-quarter views.
        return view is "downside" or "upside" ? Directional(sheet, tag, "side") : null;
    }

    /// <summary><c>tag_view</c> names, built once: every person's pose asks for one each frame.</summary>
    private static readonly Dictionary<(string, string), string> DirectionalNames = [];

    /// <summary>The first tag in the fallback chain the sheet actually has (null if none, not even idle).</summary>
    public static SheetAnim? Resolve(SpriteSheet sheet, string tag, out string resolved)
    {
        for (string? t = tag; t != null; t = Fallback(t))
            if (sheet.Find(t) is { } anim)
            {
                resolved = t;
                return anim;
            }
        resolved = tag;
        return null;
    }

    public CreaturePose Pose(Simulation sim, Entity e, SpriteSheet sheet, Camera3D cam)
    {
        var w = sim.World;
        if (!_states.TryGetValue(e.Index, out var st) || st.Generation != e.Generation)
        {
            _states[e.Index] = st = new State { Generation = e.Generation, TagTime = (SimRandom.Hash(e.Index, 3, 11) & 1023) / 1023f };
            st.Facing = (SimRandom.Hash(e.Index, 4, 11) & 1) == 0 ? 1 : -1;
        }
        st.LastDrawn = _frame;

        var c = w.Get<Creature>(e);
        var mind = w.Get<Mind>(e);
        var mover = w.Get<Mover>(e);
        var pos = w.Get<Position>(e);
        var map = sim.Map;

        // A new breath by this creature restarts the breath animation and turns it to face the jet.
        BreathEvent? breath = null;
        foreach (var ev in sim.RecentBreaths)
            if (ev.Source == e && (breath == null || ev.Tick > breath.Tick))
                breath = ev;
        var newBreath = breath != null && !ReferenceEquals(breath, st.Breath);
        if (newBreath)
            st.Breath = breath;
        // Stays "breathing" until the breath animation (a one-shot) has played out.
        var breathing = newBreath || (breath != null && ReferenceEquals(breath, st.Breath) && st.Want == "breathe" && st.Done != "breathe");

        // Airborne: in a step through the air (as MovementSystem judges it), or hanging in open air.
        var vertical = 0;
        var landing = false;
        var airStep = false;
        if (mover.Moving)
        {
            var next = mover.Path![mover.PathIndex];
            var from = mover.StepFrom;
            int3? perch = w.TryGet<Perched>(e, out var perched) ? perched.Cell : null; // climbing onto furniture is on foot
            airStep = (!map.Passable(next) && next != perch) || (!map.Passable(from) && from != perch);
            if (airStep && next.X == from.X && next.Y == from.Y)
            {
                vertical = Math.Sign(next.Z - from.Z);
                landing = vertical < 0 && map.Passable(next);
            }
        }
        var airborne = airStep || (map.InBounds(pos.Cell) && !map.Standable(map.Idx(pos.Cell)) && !map.IsSolid(map.Idx(pos.Cell)));

        var job = mind.Job;
        // A player in a hands-on session (at the anvil, the tub, a stain on the floor) isn't in an AI
        // job, but works all the same, facing the work.
        var station = w.TryGet<Sim.Work.WorkSession>(e, out var session) ? session.Focus(sim) : null;
        var inputs = new AnimInputs(
            c.Dead,
            c.Downed,
            mind.Asleep,
            breathing,
            false,
            airborne,
            vertical,
            landing,
            job is SoarJob && GlidingNow(e),
            (job is BuildJob or DeconstructJob or HarvestJob or DigJob or Sim.Shops.CleanJob or Sim.Shops.MakeBedJob or Sim.Shops.WashJob
                || station != null) && !mover.Moving,
            mover.Moving,
            sim.Clock.Tick - c.BornTick < HatchTicks,
            null
        );
        var want = ChooseTag(inputs);
        if (want != st.Want || newBreath)
            st.Done = null;
        st.Want = want;
        var anim = Resolve(sheet, want, out var tag);
        // A finished one-shot gives way to its fallback (takeoff → fly) until something else is wanted.
        if (anim != null && !anim.Loop && tag != "downed" && (tag == st.Done || (tag == st.Tag && anim.Finished(st.TagTime))))
        {
            st.Done = tag;
            anim = Resolve(sheet, Fallback(tag) ?? "idle", out tag);
        }

        var started = tag != st.Tag || (newBreath && tag == "breathe");
        if (started)
        {
            // Loops keep their phase between related tags so a flap doesn't restart on every turn.
            st.TagTime = anim is { Loop: true } && st.Tag is "fly" or "glide" && tag is "fly" or "glide" ? st.TagTime : 0f;
            st.Tag = tag;
            st.LastFrame = -1;
        }
        else
        {
            st.TagTime += _dt;
        }

        // Facing: towards the breath or work target, else along the path.
        float3? look = null;
        if (tag is "breathe" or "breathe_windup" && st.Breath != null && st.Breath.RayEnds.Count > 0)
        {
            var sum = float3.Zero;
            foreach (var end in st.Breath.RayEnds)
                sum += end;
            look = sum * (1f / st.Breath.RayEnds.Count) - st.Breath.Mouth;
        }
        if (look == null && station is { } at)
            look = Position.CenterOf(at) - pos.Pos;
        if (look == null && job != null && (tag is "work" or "breathe_windup" || inputs.WindingUp))
        {
            if (!job.Focus.IsNull && w.IsAlive(job.Focus))
                look = Position.CenterOf(sim.LocationOf(job.Focus)) - pos.Pos;
            else if (job.FocusCell is { } fc)
                look = Position.CenterOf(fc) - pos.Pos;
        }
        if (look == null && mover.Moving)
        {
            var next = mover.Path![mover.PathIndex];
            look = new float3(next.X - mover.StepFrom.X, next.Y - mover.StepFrom.Y, 0);
        }
        if (look is { } dir && !(tag is "sleep" or "downed"))
        {
            var side = dir.X * cam.Right.X + dir.Y * cam.Right.Y;
            var away = dir.X * cam.Heading.X + dir.Y * cam.Heading.Y;
            if (MathF.Abs(side) > 0.05f)
                st.Facing = side > 0 ? 1 : -1;
            if (MathF.Abs(side) > 0.05f || MathF.Abs(away) > 0.05f)
                st.View = ViewOf(side, away);
        }
        // Four-way sheets show the view's frames (same timing as the plain tag); front and back aren't
        // flipped. Standing about, people show their stance (HumanLook.IdleTag) if the sheet has it.
        var directional = anim == null ? null
            : tag == "idle" && c.Def.Race == Race.Human && HumanLook.Of(sim, e).Stance != Stance.Relaxed
                ? sheet.Find(HumanLook.IdleTag(sheet, st.View, HumanLook.Of(sim, e).Stance)) ?? Directional(sheet, tag, st.View)
                : Directional(sheet, tag, st.View);
        if (directional != null)
            anim = directional;
        var facing = directional != null && !Flips(st.View) ? 1 : st.Facing;

        // Banking and pitch for flyers: lean into turns, nose up climbing and down descending.
        var targetRoll = 0f;
        if (airborne && mover.Moving)
        {
            var next = mover.Path![mover.PathIndex];
            float dx = next.X - mover.StepFrom.X, dy = next.Y - mover.StepFrom.Y;
            if (dx != 0 || dy != 0)
            {
                var heading = MathF.Atan2(dy, dx);
                if (!float.IsNaN(st.LastHeading) && _dt > 0)
                {
                    var turn = MathF.IEEERemainder(heading - st.LastHeading, MathF.Tau);
                    targetRoll = Math.Clamp(-turn * 0.6f, -0.15f, 0.15f);
                }
                st.LastHeading = heading;
            }
            targetRoll += vertical > 0 ? 0.12f : vertical < 0 ? -0.1f : 0f;
        }
        else
        {
            st.LastHeading = float.NaN;
        }
        st.Roll += (targetRoll * st.Facing - st.Roll) * Math.Min(1f, _dt * 4f);

        var frameIndex = anim?.FrameAt(st.TagTime) ?? 0;
        var frame = anim != null ? anim.Frames[frameIndex] : sheet.Frames[0];
        var fired = anim != null && anim.EventFrame >= 0 && frameIndex == anim.EventFrame && st.LastFrame != frameIndex;
        st.LastFrame = frameIndex;
        st.Shown = frame;
        return new CreaturePose(sheet, anim, frame, tag, facing, st.Roll, fired, started);
    }

    /// <summary>True once per <paramref name="interval"/> seconds of animation time for this creature (Zzz, embers).</summary>
    public bool Every(Entity e, float interval)
    {
        if (!_states.TryGetValue(e.Index, out var st))
            return false;
        if (Clock < st.NextZzz)
            return false;
        st.NextZzz = Clock + interval;
        return true;
    }

    /// <summary>
    /// A frame for UI portraits: what the creature is showing in the world (so a flying dragon flaps
    /// in the villager bar), except lying poses, which use the idle loop instead.
    /// </summary>
    public SheetFrame Portrait(Entity e, SpriteSheet sheet)
    {
        if (_states.TryGetValue(e.Index, out var st) && st.Generation == e.Generation && st.Shown.Sprite.PixelSize.X > 0
            && st.Tag is not ("sleep" or "downed") && _frame - st.LastDrawn < 2)
            return st.Shown;
        var idle = Resolve(sheet, "idle", out _);
        return idle != null ? idle.Frames[idle.FrameAt(Clock + e.Index * 0.37f)] : sheet.Frames[0];
    }

    /// <summary>Where the creature's mouth was drawn this frame, so breath jets leave the sprite's mouth.</summary>
    public void SetMouth(Entity e, float3 mouth)
    {
        if (_states.TryGetValue(e.Index, out var st))
            st.Mouth = mouth;
    }

    public bool TryGetMouth(Entity e, out float3 mouth)
    {
        if (_states.TryGetValue(e.Index, out var st) && st.Generation == e.Generation && st.LastDrawn == _frame)
        {
            mouth = st.Mouth;
            return true;
        }
        mouth = default;
        return false;
    }
}
