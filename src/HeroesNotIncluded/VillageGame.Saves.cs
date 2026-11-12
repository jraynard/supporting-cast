using System.Globalization;
using Brutal.ImGuiApi;
using Narsil;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

/// <summary>
/// Saving and loading in the village: F6 quicksave, F9 quickload, autosaves at dawn and just before
/// a fight (fights themselves can't be saved), and the Save/Load windows from the top bar. Loading
/// always switches to a fresh <see cref="VillageGame"/>, so nothing render-side survives from before.
/// </summary>
public sealed partial class VillageGame
{
    /// <summary>Game hour the daily autosave happens at.</summary>
    private const float AutosaveHour = 6f;

    private enum SaveWindow { None, Save, Load }

    private SaveWindow _saveWindow;
    private readonly ImInputString _saveName = new(64, ""u8);
    private IReadOnlyList<SaveSlot> _slots = [];
    private SaveSlot? _confirmDelete;
    private string? _saveWindowError;

    /// <summary>Sim tick and real time of the last save or load; -1 if this village was never saved.</summary>
    private long _lastSaveTick = -1;
    private float _lastSaveTime;
    private int _lastAutosaveDay;
    private long _lastBattleAutosaveTick = long.MinValue;
    private float _quickloadArmedUntil = -1;
    private bool _openedSaves;

    /// <summary>Demos don't autosave over the player's village.</summary>
    private bool AutosaveEnabled => SaveSlots.Directory != null;

    private void OnSavedOrLoaded(string? slot)
    {
        if (slot != null)
            MarkSaved();
        // The day's autosave is due at the next dawn, not straight away.
        _lastAutosaveDay = _sim.Clock.HourOfDay >= AutosaveHour ? _sim.Clock.Day : _sim.Clock.Day - 1;
    }

    private void MarkSaved()
    {
        _lastSaveTick = _sim.Clock.Tick;
        _lastSaveTime = _time;
    }

    private void OnScreenshotSaved(string path, string? error) =>
        Notify(error == null ? MessageKind.Info : MessageKind.Warning,
            error == null ? $"Screenshot saved: {Path.GetFileName(path)}" : $"Screenshot failed: {error}");

    private void Notify(MessageKind kind, string text) => Toast(new Message(_sim.Clock.Tick, kind, text, null));

    /// <summary>Save into a slot; returns why it failed, or null.</summary>
    private string? SaveTo(string slot)
    {
        var error = SaveSlots.Write(slot, _sim, ViewMeta());
        if (error == null)
            MarkSaved();
        return error;
    }

    private void Autosave(string when)
    {
        // A fight that was predicted but didn't start may be predicted again: one battle save an hour is plenty.
        if (when == "before the fight")
        {
            if (_sim.Clock.Tick - _lastBattleAutosaveTick < SimClock.TicksPerHour)
                return;
            _lastBattleAutosaveTick = _sim.Clock.Tick;
        }
        else
        {
            _lastAutosaveDay = _sim.Clock.Day;
        }
        if (_autosaving is { IsCompleted: false })
            return; // the last one is still being written
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var slot = SaveSlots.NextAutosave();
        // Only the capture stalls the frame; compressing and writing finish in the background.
        var (error, done) = SaveSlots.WriteInBackground(slot, _sim, ViewMeta());
        if (error != null)
        {
            Notify(MessageKind.Warning, $"Autosave failed: {error}");
            return;
        }
        MarkSaved();
        var captured = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _autosaving = done;
        _autosaveNote = $"Autosaved {when}.";
        Log.Engine.Info($"Autosaving {when} to {slot}: {captured:F0} ms on the main thread");
    }

    private Task<string?>? _autosaving;
    private string _autosaveNote = "";

    /// <summary>Report a background autosave once its file is written (called every frame).</summary>
    private void PollAutosave()
    {
        if (_autosaving is not { IsCompleted: true } task)
            return;
        _autosaving = null;
        if (task.IsCompletedSuccessfully && task.Result == null)
            Notify(MessageKind.Info, _autosaveNote);
        else
            Notify(MessageKind.Warning, $"Autosave failed: {(task.IsCompletedSuccessfully ? task.Result : task.Exception?.GetBaseException().Message)}");
    }

    private void QuickSave()
    {
        if (SaveTo(SaveSlots.Quicksave) is { } error)
            Notify(MessageKind.Warning, error);
        else
            Notify(MessageKind.Info, "Quicksaved (F9 to load).");
    }

    private void QuickLoad()
    {
        if (SaveSlots.Directory == null || !File.Exists(SaveSlots.PathOf(SaveSlots.Quicksave)))
        {
            Notify(MessageKind.Warning, "No quicksave yet (F6 saves one).");
            return;
        }
        // More than an hour of unsaved play: ask for a second press.
        if (_sim.Clock.Tick - _lastSaveTick > SimClock.TicksPerHour && _time > _quickloadArmedUntil)
        {
            _quickloadArmedUntil = _time + 3f;
            Notify(MessageKind.Warning, "Press F9 again to load the quicksave. Unsaved progress will be lost.");
            return;
        }
        LoadFrom(SaveSlots.PathOf(SaveSlots.Quicksave));
    }

    /// <summary>What a def reload hands the next <see cref="VillageGame"/>: it's the same village, not a load.</summary>
    public sealed record CarryOver(long LastSaveTick, float SecondsSinceSave, string Notice);

    public CarryOver? CarriedOver { get; init; }

    /// <summary>
    /// Def hot reload: read the defs again and move the village onto them through a save round trip
    /// (every def is looked up by name), then continue in a fresh VillageGame so nothing render-side
    /// holds an old def. Jobs restart, as after any load. Bad defs leave the game as it was.
    /// </summary>
    private void ReloadDefs()
    {
        if (CoopForbids("reload defs") is { } no)
        {
            Notify(MessageKind.Info, $"{no} (def changes apply next time)");
            return;
        }
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var dir = _defReloader!.Directory;
        DefDatabase defs;
        try
        {
            defs = DefDatabase.LoadDirectory(dir);
        }
        catch (Exception ex) // bad JSON, a dangling reference, a file mid-write: report and keep playing
        {
            Log.Engine.Warning($"Defs in {dir} didn't load: {ex}");
            var message = ex.Message.Replace(dir + Path.DirectorySeparatorChar, ""); // file names are enough in a toast
            Notify(MessageKind.Danger, $"Defs not reloaded: {ShaderReload.Summary(message)}");
            return;
        }
        LoadedGame moved;
        try
        {
            var (sim, header) = SaveGame.Read(SaveGame.Write(_sim, ViewMeta()), defs);
            moved = new LoadedGame(sim, header, "defs");
        }
        catch (SaveException ex)
        {
            Log.Engine.Warning($"Couldn't move the village onto the new defs: {ex.Message}");
            Notify(MessageKind.Danger, $"Defs not reloaded: {ex.Message}");
            return;
        }
        var ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Log.Engine.Info($"Defs reloaded from {dir} in {ms:F0} ms");
        Host.SwitchTo(new VillageGame(seed)
        {
            Loaded = moved,
            AutoPause = AutoPause,
            CarriedOver = new CarryOver(_lastSaveTick, _time - _lastSaveTime, "Defs reloaded (jobs restarted)."),
        });
    }

    private void ReloadShaders()
    {
        if (ShaderReload.Reload(Host, _quads) is { } error)
            Notify(MessageKind.Danger, $"Shader error: {error}");
        else
            Notify(MessageKind.Info, "Shaders reloaded.");
    }

    private void LoadFrom(string path)
    {
        try
        {
            var save = SaveSlots.Load(path, _sim.Defs);
            Host.SwitchTo(FromSave(save, AutoPause));
        }
        catch (SaveException ex)
        {
            Log.Engine.Warning($"Couldn't load {path}: {ex.Message}");
            Notify(MessageKind.Danger, ex.Message);
            _saveWindowError = ex.Message;
        }
    }

    // ---------------- View state stored with the save ----------------

    private Dictionary<string, string> ViewMeta()
    {
        var c = _camera;
        var inv = CultureInfo.InvariantCulture;
        return new Dictionary<string, string>
        {
            ["savedAt"] = DateTime.UtcNow.ToString("o", inv),
            ["camera"] = string.Join(",", new[] { c.Target.X, c.Target.Y, c.Target.Z, c.Yaw, c.Zoom }.Select(v => v.ToString("R", inv))),
            ["cutaway"] = _followLevel ? "follow" : _renderer.CutawayZ.ToString(inv),
            ["speed"] = _speed.ToString(inv),
        };
    }

    private void RestoreView(IReadOnlyDictionary<string, string> meta)
    {
        var inv = CultureInfo.InvariantCulture;
        if (meta.TryGetValue("camera", out var camera)
            && camera.Split(',') is { Length: 5 } parts
            && parts.All(p => float.TryParse(p, NumberStyles.Float, inv, out _)))
        {
            var v = parts.Select(p => float.Parse(p, NumberStyles.Float, inv)).ToArray();
            _camera.Target = new float3(v[0], v[1], v[2]);
            _camera.Yaw = v[3];
            _camera.Zoom = v[4];
        }
        // Older saves wrote the number even when it showed everything: that's following too.
        if (meta.TryGetValue("cutaway", out var cut) && int.TryParse(cut, inv, out var z) && z < _sim.Map.Depth - 1)
            SetCutaway(z);
        else
            FollowLevel();
        if (meta.TryGetValue("speed", out var speed) && int.TryParse(speed, inv, out var s) && s >= 0 && s < SpeedMultipliers.Length)
            SetSpeed(s);
    }

    // ---------------- Windows ----------------

    private void OpenSaveWindow(SaveWindow which)
    {
        if (CoopForbids(which == SaveWindow.Save ? "save" : "load a game") is { } no)
        {
            Notify(MessageKind.Info, no);
            return;
        }
        _saveWindow = _saveWindow == which ? SaveWindow.None : which;
        _slots = SaveSlots.List();
        _confirmDelete = null;
        _saveWindowError = null;
        if (which == SaveWindow.Save)
            _saveName.SetValue($"Day {_sim.Clock.Day}".AsSpan());
    }

    private string LastSavedText() =>
        _lastSaveTick < 0 ? "this village hasn't been saved" : $"last saved {SaveSlots.Ago(DateTime.UtcNow.AddSeconds(_lastSaveTime - _time), DateTime.UtcNow)}";

    private void DrawSaveWindows()
    {
        if (OpenSaves != null && _openedSaves == false && _time > 1f)
        {
            _openedSaves = true;
            OpenSaveWindow(OpenSaves.Equals("save", StringComparison.OrdinalIgnoreCase) ? SaveWindow.Save : SaveWindow.Load);
        }
        if (_saveWindow == SaveWindow.None)
            return;
        var open = true;
        SetNextWindow(new float2(Screen.X / 2f - 230, 140), new float2(460, 0));
        var title = _saveWindow == SaveWindow.Save ? "Save game###saves" : "Load game###saves";
        if (!Narsil.Ui.Panel.Begin(title, ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse))
        {
            Narsil.Ui.Panel.End();
            if (!open) _saveWindow = SaveWindow.None;
            return;
        }

        if (_saveWindow == SaveWindow.Save)
            DrawSaveForm();
        DrawSlotList();
        if (_saveWindowError != null)
        {
            ImGui.Separator();
            Wrapped(_saveWindowError);
        }
        Narsil.Ui.Panel.End();
        if (!open)
            _saveWindow = SaveWindow.None;
    }

    private void DrawSaveForm()
    {
        ImGui.InputText("Name"u8, _saveName, ImGuiInputTextFlags.None, null, IntPtr.Zero);
        var name = SaveSlots.Clean(Narsil.Ui.ImGuiText.Value(_saveName));
        var exists = name.Length > 0 && _slots.Any(s => s.Name == name);
        var disabled = name.Length == 0;
        if (disabled)
            ImGui.BeginDisabled(true);
        if (ImGui.Button(exists ? "Overwrite" : "Save", in _autoSize))
        {
            _saveWindowError = SaveTo(name);
            if (_saveWindowError == null)
            {
                Notify(MessageKind.Info, $"Saved as \"{name}\".");
                _saveWindow = SaveWindow.None;
            }
        }
        if (disabled)
            ImGui.EndDisabled();
        ImGui.SameLine(0, -1);
        TextC(Dim, exists ? "replaces the save with this name" : LastSavedText());
        ImGui.Separator();
    }

    private void DrawSlotList()
    {
        if (_slots.Count == 0)
        {
            TextC(Dim, SaveSlots.Directory == null ? "Saving is disabled." : "No saves yet.");
            return;
        }
        var now = DateTime.UtcNow;
        foreach (var slot in _slots)
        {
            ImGui.PushID(slot.Name);
            var label = slot.IsAutosave ? $"{slot.Name} (auto)" : slot.Name;
            ImGui.Text(label);
            ImGui.SameLine(220, -1);
            TextC(Dim, SaveSlots.Ago(slot.Modified, now));
            TextC(slot.Header == null ? Bad : Dim, "  " + slot.Summary);

            if (_confirmDelete == slot)
            {
                TextC(Warn, "  Delete this save?");
                ImGui.SameLine(0, -1);
                if (ImGui.SmallButton("Delete"u8))
                {
                    SaveSlots.Delete(slot);
                    _slots = SaveSlots.List();
                    _confirmDelete = null;
                }
                ImGui.SameLine(0, -1);
                if (ImGui.SmallButton("Keep"u8))
                    _confirmDelete = null;
            }
            else
            {
                ImGui.Text("  "u8);
                ImGui.SameLine(0, -1);
                if (_saveWindow == SaveWindow.Load)
                {
                    if (slot.Header == null)
                        ImGui.BeginDisabled(true);
                    if (ImGui.SmallButton("Load"u8))
                        LoadFrom(slot.Path);
                    if (slot.Header == null)
                        ImGui.EndDisabled();
                }
                else if (ImGui.SmallButton("Use name"u8))
                {
                    _saveName.SetValue(slot.Name.AsSpan());
                }
                ImGui.SameLine(0, -1);
                if (ImGui.SmallButton("Delete"u8))
                    _confirmDelete = slot;
            }
            ImGui.PopID();
            ImGui.Spacing();
        }
    }
}
