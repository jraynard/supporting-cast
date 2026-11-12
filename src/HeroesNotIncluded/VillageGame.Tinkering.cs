using Brutal.ImGuiApi;
using Brutal.GlfwApi;
using Narsil.Ui;
using SupportingCast.Sim;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Ecs;
using SupportingCast.Sim.Shops;
using SupportingCast.Sim.Systems;
using SupportingCast.Sim.Tinkering;
using SupportingCast.Ui;

namespace SupportingCast;

// The tinker's: pinning locks and filing keys, fitting contraptions together, truing wagon wheels,
// curios under the loupe; the window at a door's lock or a party's wagon; and what can be done
// with locks, keys and curios from the pack. Curios and locks come through View (Perception), which
// never says what a curio's really worth or how tight someone else's lock is.
public sealed partial class VillageGame
{
    /// <summary>The door or wagon whose window is open (F at it), or null.</summary>
    private Entity _tinkerAt = Entity.Null;

    /// <summary>The curio whose worth you're talking over (quote, offer), or null; and the sliders' silver.</summary>
    private Entity _curioTalk = Entity.Null;
    private int _quote = 20, _offer = 15;

    // ---------------- The games ----------------

    /// <summary>The pins as columns, each filed down toward its mark; the one in the file, and how each set one sits.</summary>
    private void DrawPin(PinSession s)
    {
        BeginWork(s.Title, ("F", "hold to file"), ("D", "set the pin, take up the next"), ("Esc", "stop"));
        TextC(Theme.Dim, "File to the mark: the longer a stroke, the harder the file bites. Past the mark, it's sloppy; far past, ruined.");
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var w = Gui.S(40);
        var h = Gui.S(130);
        ImGui.Dummy(new float2(Gui.S(20) + s.Pins * (w + Gui.S(12)), h + Gui.S(6)));
        for (var i = 0; i < s.Pins; i++)
        {
            var colMin = new float2(origin.X + Gui.S(10) + i * (w + Gui.S(12)), origin.Y);
            var colMax = new float2(colMin.X + w, origin.Y + h);
            dl.AddRectFilled(in colMin, in colMax, Theme.U8(Theme.Well), Gui.S(3), ImDrawFlags.None);
            if (i > s.Pin)
                continue;
            var length = i < s.Pin ? Math.Clamp(s.Mark - s.Errors[i], 0f, 1f) : s.Length;
            var top = colMax.Y - length * (h - Gui.S(8));
            var pinMin = new float2(colMin.X + Gui.S(10), top);
            var pinMax = new float2(colMax.X - Gui.S(10), colMax.Y - Gui.S(2));
            var colour = i < s.Pin ? (s.Accuracy(s.Errors[i]) > 0.7f ? Theme.Good : s.Errors[i] >= PinSession.Ruined ? Theme.Bad : Theme.Warn) : Theme.Iron;
            dl.AddRectFilled(in pinMin, in pinMax, Theme.U8(colour), Gui.S(2), ImDrawFlags.None);
            if (i == s.Pin)
            {
                // The mark scribed on the pin's side: the maker sees where it goes.
                var y = colMax.Y - s.Mark * (h - Gui.S(8));
                var a = new float2(colMin.X + Gui.S(2), y);
                var b = new float2(colMax.X - Gui.S(2), y);
                dl.AddLine(in a, in b, Theme.U8(Theme.Accent), Gui.S(2));
            }
        }
        EndWork();
    }

    /// <summary>The part's bind and rattle as the fingers feel them; then the spring's tension and the pawl.</summary>
    private void DrawFit(FitSession s)
    {
        if (s.Winding)
        {
            BeginWork(s.Title, ("F", "tap to wind a click"), ("D", "latch it"), ("Esc", "stop"));
            TextC(Theme.Dim, "Wind it till the pawl catches, and latch it there. Too far and the spring snaps.");
            Bar("Tension", Math.Clamp(s.Tension, 0f, 1f), Theme.Accent);
            TextC(s.Catching ? Theme.Good : Theme.Dim, s.Catching ? "Click... click... the pawl's catching." : "The ratchet runs free.");
            EndWork();
            return;
        }
        BeginWork(s.Title, ("F", "hold to file the part"), ("D", "seat it"), ("Esc", "stop"));
        TextC(Theme.Dim, "File while it binds. Seat it at the fit: forced home tight it'll bind, too loose it rattles.");
        Bar("Bind", s.Bind, s.Bind > 0.5f ? Theme.Warn : Theme.Dim);
        Bar("Rattle", s.Rattle, s.Rattle > 0.1f ? Theme.Bad : Theme.Dim);
        TextC(Theme.Dim, $"Part {Math.Min(s.Part + 1, s.Parts)} of {s.Parts}{(s.Winds ? ", then the spring" : "")}.");
        EndWork();
    }

    /// <summary>The wheel turning past the gauge: the needle out for a bulge, in for a dip; the band it must run within.</summary>
    private void DrawTrue(TrueSession s)
    {
        BeginWork(s.Title, ("F", "tighten the spoke at the gauge"), ("A", "slacken it"), ("Esc", "stop"));
        TextC(Theme.Dim, "Tighten where the needle swings out, slacken where it swings in. Get it running within the band.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(420, 60);
        var max = min + size;
        ImGui.Dummy(in size);
        dl.AddRectFilled(in min, in max, Theme.U8(Theme.Well), Gui.S(3), ImDrawFlags.None);
        var mid = (min.X + max.X) * 0.5f;
        var scale = size.X * 0.5f / 0.4f;
        var bandMin = new float2(mid - s.BandWidth * scale, min.Y);
        var bandMax = new float2(mid + s.BandWidth * scale, max.Y);
        dl.AddRectFilled(in bandMin, in bandMax, Theme.U8(Theme.Good, 0.45f), Gui.S(2), ImDrawFlags.None);
        var x = Math.Clamp(mid + s.Gauge * scale, min.X + 2, max.X - 2);
        var a = new float2(x, min.Y + Gui.S(4));
        var b = new float2(x, max.Y - Gui.S(4));
        dl.AddLine(in a, in b, Theme.U8(MathF.Abs(s.Gauge) <= s.BandWidth ? Theme.Text : Theme.Accent), Gui.S(3));
        TextC(Theme.Dim, "in");
        ImGui.SameLine(size.X - Gui.S(20), 0);
        TextC(Theme.Dim, "out");
        Bar("Wheel", s.Turn, Theme.Dim, $"spoke {s.AtGauge + 1} of {TrueSession.Spokes}");
        EndWork();
    }

    /// <summary>The piece under the glass, the loupe on it, what catches the eye, and the reading.</summary>
    private void DrawLoupe(LoupeSession s)
    {
        BeginWork(s.Title, ("A/D", "move the loupe along it"), ("F", "hold still to study"), ("Esc", "stop"));
        TextC(Theme.Dim, "Where the eye catches, stop and study it. The far end finishes.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = Gui.S(420, 28);
        var max = min + size;
        ImGui.Dummy(in size);
        dl.AddRectFilled(in min, in max, new ImColor8(120, 96, 58, 255), Gui.S(4), ImDrawFlags.None);
        var x = min.X + Gui.S(4) + s.At * (size.X - Gui.S(8));
        var c = new float2(x, (min.Y + max.Y) * 0.5f);
        dl.AddCircle(in c, Gui.S(11), Theme.U8(Theme.Accent), 20, Gui.S(2.5f));
        Bar("Eye", s.Catch, s.Catch > 0.6f ? Theme.Warn : Theme.Dim);
        if (s.Reading > 0f)
            Bar("Reading", s.Reading, Theme.Good);
        TextC(Theme.Dim, $"Tells read: {s.ReadCount}");
        EndWork();
    }

    // ---------------- At a door or a wagon ----------------

    /// <summary>F at a door's lock or a wagon: its window.</summary>
    private void OpenTinkerWindow(Entity target, bool toggle) =>
        _tinkerAt = toggle && _tinkerAt == target ? Entity.Null : target;

    /// <summary>
    /// A door's lock (lock or unlock it with your key, pick it, or fit one you carry) or a party's
    /// wagon (what's wrong with it, and mending it). Closes when you walk off or press Esc.
    /// </summary>
    private void DrawTinkerWindow()
    {
        if (_tinkerAt.IsNull)
            return;
        var w = _sim.World;
        if (!HasAvatar || !w.IsAlive(_tinkerAt) || !AvatarSystem.InReach(_sim, w.Get<Position>(_avatar).Cell, _tinkerAt)
            || Host.Input.WasKeyPressed(GlfwKey.Escape))
        {
            _tinkerAt = Entity.Null;
            return;
        }
        var pos = ImGui.GetIO().DisplaySize * 0.5f + new float2(60, 0);
        float2? pivot = new float2(0f, 0.5f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Appearing, in pivot);
        var open = true;
        var label = w.Get<Thing>(_tinkerAt).Label;
        Panel.Begin($"{Cap(label)}###tinker", ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);
        if (View.Wagon(_tinkerAt) is { } wagon)
            DrawWagonInside(wagon);
        else
            DrawDoorInside(label);
        Panel.End();
        if (!open)
            _tinkerAt = Entity.Null;
    }

    private void DrawDoorInside(string label)
    {
        var door = _tinkerAt;
        if (View.Lock(door) is not { } known)
        {
            var lk = Tinkers.CarriedLock(_sim, _avatar);
            Wrapped($"No lock on the {label}.");
            ImGui.BeginDisabled(lk.IsNull || Working);
            if (ImGui.Button("Fit your lock to it"u8, in _autoSize))
                Issue(new FitLockCommand(_avatar, door, lk));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(lk.IsNull ? "You've no lock on you. Pin one at a tinker's bench." : "It's part of the door from then on: only its keys turn it, or a good pick.");
            return;
        }
        TextC(known.Locked ? Theme.Warn : Theme.Dim, $"{(known.Locked ? "Locked" : "Unlocked")}: a {known.Pins}-pin lock.");
        if (known.Tightness is { } tight)
            TextC(Theme.Dim, tight >= 0.8f ? "Your work, and tight: a hard one to pick." : tight >= 0.5f ? "Your work: fair to middling." : "Your work, and loose: an easy pick.");
        ImGui.BeginDisabled(!known.HaveKey || Working);
        if (ImGui.Button(known.Locked ? "Unlock it"u8 : "Lock it"u8, in _autoSize))
            Issue(new TurnKeyCommand(_avatar, door, !known.Locked));
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip(known.HaveKey ? "With the key you carry. A poorly cut one sometimes sticks." : "None of your keys fits it.");
        if (known.Locked)
        {
            var why = PickSession.DoorProblem(_sim, _avatar, door);
            ImGui.BeginDisabled(why != null || Working);
            if (ImGui.Button("Pick the lock"u8, in _autoSize))
                Issue(new PickDoorCommand(_avatar, door));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(why != null ? Cap(why) + "." : "A/D lift the pin; F sets it where it gives most. A tight lock gives little room.");
        }
    }

    private void DrawWagonInside(KnownWagon wagon)
    {
        Wrapped($"{wagon.Owner}'s wagon: {Wagons.DamageLabel(wagon.Damage)}.");
        if ((wagon.Damage & WagonDamage.Wheel) != 0)
        {
            var why = Wagons.WheelProblem(_sim, _avatar, _tinkerAt);
            ImGui.BeginDisabled(why != null || Working);
            if (ImGui.Button("True the wheel"u8, in _autoSize))
                Issue(new TrueWheelCommand(_avatar, _tinkerAt));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(why != null ? Cap(why) + "." : "As each spoke comes past the gauge: F tightens it (where the rim bulges out), A slackens it (where it dips in).");
        }
        if ((wagon.Damage & WagonDamage.Axle) != 0)
        {
            var why = Wagons.AxleProblem(_sim, _avatar, _tinkerAt);
            ImGui.BeginDisabled(why != null || Working);
            if (ImGui.Button($"Fit a new axle ({Wagons.AxleWood} wood)"))
                Issue(new FitAxleCommand(_avatar, _tinkerAt));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(why != null ? Cap(why) + "." : "Hold F to file each part while it binds; D seats it at the fit.");
        }
        if (wagon.Damage == WagonDamage.None)
            TextC(Theme.Good, "It's sound.");
    }

    // ---------------- From the pack ----------------

    /// <summary>What can be done with a key, a curio or a contraption you carry. True if anything was offered.</summary>
    private bool TinkerMenuEntries(Entity item, Action<ISimCommand> Later)
    {
        var w = _sim.World;
        var any = false;
        if (Locksmithing.IsKey(_sim, item))
        {
            any = true;
            var bench = BenchInReach();
            var why = bench.IsNull ? "you need to be at a tinker's bench" : PinSession.CopyProblem(_sim, _avatar, bench, item);
            ImGui.BeginDisabled(why != null || Working);
            if (ImGui.MenuItem("Copy the key"u8))
                Later(new CopyKeyCommand(_avatar, bench, item));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(why != null ? Cap(why) + "." : "File a blank from an iron bar to this key's bits: hold F to file, D to set each.");
        }
        if (View.Curio(item) is { } curio)
        {
            any = true;
            var why = Working ? "you're busy" : null;
            ImGui.BeginDisabled(why != null);
            if (ImGui.MenuItem(curio.Low == null ? "Look it over under the loupe"u8 : "Look it over again"u8))
                Later(new AppraiseCommand(_avatar, item));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip("A/D move the loupe along it; where the eye catches, hold F still to study it. The far end finishes.");
            if (curio.Mine)
            {
                if (ImGui.MenuItem("Talk over its worth..."u8))
                {
                    _curioTalk = item;
                    _quote = curio.Quoted ?? ((curio.Low ?? 0) + (curio.High ?? 0)) / 2;
                    _offer = Math.Max(0, curio.Low ?? 0);
                }
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip("Say what you'd put it at, aloud; or offer its owner silver for it.");
            }
        }
        return any;
    }

    /// <summary>A tinker's bench beside the avatar, if any.</summary>
    private Entity BenchInReach()
    {
        var at = _sim.World.Get<Position>(_avatar).Cell;
        foreach (var e in _sim.World.Each<Thing>())
            if (Tinkers.IsBench(_sim, e) && AvatarSystem.InReach(_sim, at, e))
                return e;
        return Entity.Null;
    }

    /// <summary>
    /// Talking over a curio's worth: what you reckon (from your own look), what to say it's worth
    /// (aloud: whoever's by hears), and an offer to its owner if it's been left with you.
    /// </summary>
    private void DrawCurioTalk()
    {
        if (_curioTalk.IsNull)
            return;
        var w = _sim.World;
        if (!HasAvatar || View.Curio(_curioTalk) is not { Mine: true } curio || !Inventory.OnPerson(_sim, _avatar, _curioTalk)
            || Host.Input.WasKeyPressed(GlfwKey.Escape))
        {
            _curioTalk = Entity.Null;
            return;
        }
        var pos = ImGui.GetIO().DisplaySize * 0.5f + new float2(60, 0);
        float2? pivot = new float2(0f, 0.5f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Appearing, in pivot);
        var open = true;
        Panel.Begin($"{Cap(curio.Label)}###curio", ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);
        TextC(curio.Doubts ? Theme.Warn : Theme.Text, curio.Doubts
            ? $"A fake, you'd say: worth {curio.Low} to {curio.High} silver as a curiosity."
            : $"You'd put it at {curio.Low} to {curio.High} silver.");
        if (curio.Quoted is { } said)
            TextC(Theme.Dim, $"You've said {said}.");
        ImGui.SetNextItemWidth(Gui.S(200));
        ImGui.SliderInt("##quote"u8, ref _quote, 0, Math.Max(120, curio.High ?? 0), "%d silver"u8, ImGuiSliderFlags.AlwaysClamp);
        ImGui.SameLine(0, 6);
        if (ImGui.Button("Say it's worth that"u8, in _autoSize))
            Issue(new QuoteCurioCommand(_avatar, _curioTalk, _quote));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip("Aloud: whoever's by hears it. Its owner takes your word for it when you hand it back.");
        if (w.TryGet<Entrusted>(_curioTalk, out var left))
        {
            ImGui.Separator();
            ImGui.SetNextItemWidth(Gui.S(200));
            ImGui.SliderInt("##offer"u8, ref _offer, 0, Math.Max(120, curio.High ?? 0), "%d silver"u8, ImGuiSliderFlags.AlwaysClamp);
            ImGui.SameLine(0, 6);
            var near = Handing.InReach(_sim, _avatar, left.Owner);
            ImGui.BeginDisabled(!near || Working);
            if (ImGui.Button("Offer to buy it"u8, in _autoSize))
                Issue(new OfferForCurioCommand(_avatar, _curioTalk, _offer));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(near ? "They'll take it if it's near what they believe it's worth. Sell it on through the collector's crate."
                    : "Its owner needs to be beside you.");
        }
        Panel.End();
        if (!open)
            _curioTalk = Entity.Null;
    }

    /// <summary>Lines about a lock, key, curio or contraption, for its tooltip.</summary>
    private void TinkerDetails(Entity item, List<string> lines)
    {
        var w = _sim.World;
        if (View.Lock(item) is { } lk)
            lines.Add($"A {lk.Pins}-pin lock, not yet fitted.{(lk.Tightness is { } t ? t >= 0.8f ? " Tight work." : t < 0.5f ? " Loose work." : "" : "")}");
        if (w.TryGet<Key>(item, out var key) && Inventory.OnPerson(_sim, _avatar, item))
            lines.Add($"A key with {key.Pins} bits. {(key.Fit >= Locksmithing.GoodFit ? "It turns sweetly." : "It sticks now and then.")}");
        if (View.Curio(item) is { } curio)
        {
            if (curio.Low is { } low)
                lines.Add(curio.Doubts ? $"You'd say it's a fake: {low} to {curio.High} silver." : $"You'd put it at {low} to {curio.High} silver.");
            else if (curio.Told is { } told)
                lines.Add($"You've been told it's worth {told} silver.");
            else
                lines.Add("Nobody's looked it over yet.");
        }
        if (w.Get<Thing>(item).Def.Contraption is { } c)
            lines.Add(c.Use switch
            {
                Sim.Defs.ContraptionUse.Traps => "On a quest: finds traps and pits before they find you.",
                Sim.Defs.ContraptionUse.Falls => "On a quest: catches a fall that would kill, once.",
                Sim.Defs.ContraptionUse.Dark => "On a quest: lights dark places (barrows, mines, caves).",
                Sim.Defs.ContraptionUse.Beasts => "On a quest: takes the fight out of beasts.",
                _ => "On a quest: brings help when it's gone wrong, once.",
            });
    }
}
