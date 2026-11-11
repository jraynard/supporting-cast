using Brutal.ImGuiApi;
using Narsil;
using Narsil.Net;
using Narsil.Rendering;
using SupportingCast.Net;
using SupportingCast.Rendering;
using SupportingCast.Sim;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Ecs;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Systems;
using SupportingCast.Ui;

namespace SupportingCast;

/// <summary>What the title screen asks for when it starts a village to host.</summary>
public sealed record HostSettings(PlayerIdentity Identity, string SessionName, int Port = CoopSession.DefaultPort);

/// <summary>
/// Drop-in co-op: the village runs through a <see cref="CoopSession"/> instead of ticking itself.
/// Input still goes through <see cref="Issue"/>, which sends it to the session; the host sets the
/// speed and saves; a player without a character gets the character creator first.
/// </summary>
public sealed partial class VillageGame
{
    /// <summary>Joining: a session whose first snapshot is already in (the title screen waited for it).</summary>
    public CoopSession? Coop { get; init; }

    /// <summary>Hosting: open the village to others once it's set up.</summary>
    public HostSettings? HostCoop { get; init; }

    private CoopSession? _coop;
    private CharacterCreator? _coopCreator;

    /// <summary>A Pact village: newcomers also make the dragon they fly in on.</summary>
    private DragonCreator? _coopDragonCreator;
    private bool _coopDragonStep;
    private bool _characterRequested;

    /// <summary>Your saved characters (<see cref="CharacterRoster"/>), offered before the creators.</summary>
    private CharacterRoster? _roster;
    private CharacterPicker? _coopPicker;
    private bool _coopPicking = true;

    /// <summary>The saved character the creators are editing (or finishing a dragon for); null: a new one.</summary>
    private Guid? _coopEditing;
    private bool _showPlayers = true;
    private readonly ImInputString _chatInput = new(Narsil.Net.ChatRules.MaxLength + 1, ""u8);
    private int _chatSeen;

    private bool IsCoopClient => _coop is { IsHost: false };

    /// <summary>The local player in co-op (null in single player).</summary>
    private Guid? LocalPlayer => _coop?.Me.Id;

    /// <summary>Joining: take the session's village. Returns false when not joining.</summary>
    private bool LoadJoinedVillage()
    {
        if (Coop is not { Sim: { } sim } session)
            return false;
        _coop = session;
        _sim = sim;
        var mine = Players.CharacterOf(sim, session.Me.Id);
        SetUpView(mine.IsNull ? Players.Home(sim) : sim.World.Get<Position>(mine).Cell);
        AttachCoop();
        FindAvatar();
        Log.Engine.Info($"Joined {session.SessionName} at {_sim.Clock.TimeString()}");
        return true;
    }

    /// <summary>Hosting: open the session over the village that was just started or loaded.</summary>
    private void StartHostingIfAsked()
    {
        if (HostCoop is not { } settings || _coop != null)
            return;
        try
        {
            _coop = CoopSession.StartHost(_sim, settings.Identity, settings.SessionName, settings.Port, online: Settings.Current.HostOnline);
        }
        catch (System.Net.Sockets.SocketException e)
        {
            Log.Engine.Warning($"Couldn't host on port {settings.Port}: {e.Message}");
            Notify(MessageKind.Danger, $"Couldn't open the game to others on port {settings.Port}: {e.Message}");
            return;
        }
        AttachCoop();
        Notify(MessageKind.Info, $"Hosting \"{settings.SessionName}\" on port {_coop.Port}. Friends can join from Co-op → Join.");
        Log.Engine.Info($"Hosting '{settings.SessionName}' on port {_coop.Port} ({CoopSession.BuildTag(_sim.Defs)})");
    }

    private void AttachCoop()
    {
        var session = _coop!;
        session.Notice += text => Notify(MessageKind.Info, text);
        // With the Players window shut, chat would go unseen: show it as a toast instead.
        session.ChatReceived += (from, text) =>
        {
            if (!_showPlayers)
                Notify(MessageKind.Info, $"{from}: {text}");
        };
        session.SimReplaced += OnCoopSimReplaced;
        session.BecameHost += () =>
        {
            SetSpeed(1);
            session.AfterStep = AutosaveAtDawn;
            Notify(MessageKind.Info, $"The host left; you're hosting {session.SessionName} now. Friends rejoin with the same code.");
        };
        session.BeforeStep = () => _interp.Capture(_sim);
        if (session.IsHost)
            session.AfterStep = AutosaveAtDawn;
    }

    private void AutosaveAtDawn()
    {
        // Not mid-fight (a round's world time can cross dawn): it saves once the fight is over.
        if (AutosaveEnabled && !_sim.Combat.Active && _sim.Clock.Day > _lastAutosaveDay && _sim.Clock.HourOfDay >= AutosaveHour)
            Autosave("at dawn");
    }

    /// <summary>A resync replaced the village: rebuild everything render-side that read the old one.</summary>
    private void OnCoopSimReplaced()
    {
        _sim = _coop!.Sim!;
        var old = _quads;
        var cut = _renderer.CutawayZ;
        _renderer = new WorldRenderer(Host.Atlas) { CutawayZ = cut, Interp = _interp };
        _quads = new QuadRenderer(Host, new int3(_sim.Map.Width, _sim.Map.Height, _sim.Map.Depth), ShaderReload.QuadShaders(Host));
        old.Dispose();
        _selected = Entity.Null;
        _lastSeenMessages = _sim.Messages.Total;
        // A snapshot can land mid-fight (a resync): its feedback isn't saved, and the turn should be framed afresh.
        _seenFeedback = _sim.Combat.Feedback.Count;
        _seenReactions = _sim.Combat.ReactionCount;
        _framedTurn = default;
        _range = null;
        _watched.Clear();
        _interp.Reset();
        _predictor.Reset();
        FindAvatar();
    }

    /// <summary>Co-op's share of a frame: play turns at the host's speed, and keep track of our character.</summary>
    private void RunCoop(float dt)
    {
        var session = _coop!;
        if (session.Ended != null)
            return;
        // A fight runs at its own pace (Encounter.Advance), whatever the speed or pause.
        var stepsPerSecond = InCombat ? SimClock.TicksPerSecond : Paused ? 0 : SimClock.TicksPerSecond * SpeedMultipliers[_speed];
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var before = _sim.Clock.Tick;
        session.Update(dt, stepsPerSecond);
        _interp.Alpha = session.StepFraction;
        if (_sim.Clock.Tick > before)
            _lastTickMs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds / (_sim.Clock.Tick - before);
        if (IsCoopClient)
        {
            // Show the host's speed on our buttons.
            var speed = session.Speed;
            _speed = speed <= 0.01f ? 0 : Array.FindIndex(SpeedMultipliers, m => m > 0 && m >= speed - 0.5f) is >= 0 and var i ? i : SpeedMultipliers.Length - 1;
        }
        // A dead character keeps its Avatar (the corpse stays in the world), so check for death too:
        // otherwise the view stays on the corpse after a new character is made.
        if (!HasAvatar || _sim.World.Get<Creature>(_avatar).Dead)
        {
            var had = _avatar;
            FindAvatar();
            if (had != _avatar && HasAvatar)
            {
                var at = _sim.World.Get<Position>(_avatar).Cell;
                _camera.Target = new float3(at.X + 0.5f, at.Y + 0.5f, at.Z);
            }
        }
    }

    /// <summary>The co-op windows: the creator for a new player, the player list, and "waiting"/"disconnected" notes.</summary>
    private void DrawCoop()
    {
        if (_coop is not { } session)
            return;
        var screen = ImGui.GetIO().DisplaySize;
        var center = screen * 0.5f;
        float2? middle = new float2(0.5f, 0.5f);

        if (session.Ended is { } reason)
        {
            ImGui.SetNextWindowPos(in center, ImGuiCond.Always, in middle);
            ImGui.Begin("Disconnected###coop-ended"u8, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
            Wrapped($"The game with {session.SessionName} ended: {reason}.");
            if (ImGui.Button("Back to the title screen"u8, in _autoSize))
                Host.SwitchTo(new TitleGame());
            ImGui.End();
            return;
        }

        if (session.Ready && Players.CharacterOf(_sim, session.Me.Id).IsNull)
            DrawCoopCreator(session, center, middle);

        if (session.Stalled || session.Reconnecting)
        {
            var pos = new float2(screen.X * 0.5f, 90);
            float2? top = new float2(0.5f, 0f);
            ImGui.SetNextWindowPos(in pos, ImGuiCond.Always, in top);
            ImGui.Begin("##coop-stall"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoInputs);
            TextC(Theme.Warn, session.Reconnecting ? "Connection lost: reconnecting to the host..." : "Waiting for the host...");
            ImGui.End();
        }

        if (!_showPlayers)
            return;
        var at = new float2(screen.X - 16, 120);
        float2? corner = new float2(1f, 0f);
        ImGui.SetNextWindowPos(in at, ImGuiCond.FirstUseEver, in corner);
        var open = true;
        if (ImGui.Begin("Players###coop-players"u8, ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse))
        {
            // It grows to fit (chat, long names) from a remembered left edge: keep it on screen.
            var pos = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            if (pos.X + size.X > screen.X)
            {
                var inside = new float2(Math.Max(0, screen.X - size.X), pos.Y);
                ImGui.SetWindowPos(in inside, ImGuiCond.Always);
            }
            TextC(Theme.Dim, session.IsHost ? $"Hosting on port {session.Port}" : $"Joined {session.SessionName}");
            if (session.JoinCode is { } code)
            {
                ImGui.Text($"Join code: {code}");
                ImGui.SameLine(0, 8);
                if (ImGui.SmallButton("Copy"u8))
                    ImGui.SetClipboardText(code);
                Tip("Friends anywhere type this into Co-op → Join a Game.");
            }
            else if (session.RelayStatus is { } status)
                TextC(Theme.Warn, status);
            if (session.ClientStats is var (buffered, target, jitter, starved))
            {
                TextC(Theme.Dim, $"Turns queued {buffered} (keeps {target}), jitter {jitter:F0} ms, ran dry {starved}x");
                Tip("Each queued turn adds a little input delay; the client keeps just enough to cover how unevenly turns arrive.");
            }
            foreach (var p in session.Players)
            {
                var character = Players.CharacterOf(_sim, p.Id);
                var label = character.IsNull ? $"{p.Name} (making a character)" : $"{p.Name} as {_sim.World.Get<Creature>(character).Name}";
                ImGui.Text(p.IsHost ? $"{label}  [host]" : p.Away ? $"{label}  (reconnecting)" : $"{label}  {p.PingMs} ms");
                if (!character.IsNull && ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    _selected = character;
                    var cell = _sim.World.Get<Position>(character).Cell;
                    _camera.Target = new float3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z);
                }
                if (session.IsHost && !p.IsHost)
                {
                    ImGui.SameLine(0, 8);
                    ImGui.PushID(p.Slot);
                    if (ImGui.SmallButton("Kick"u8))
                        session.Kick(p.Slot);
                    ImGui.PopID();
                }
            }
            DrawChat(session);
            if (session.IsHost)
                TextC(Theme.Dim, "Peaceful: raids are off in co-op for now.");
            TextC(Theme.Dim, $"{BuildInfo.Label}, build {BuildInfo.Tag(_sim.Defs)}");
        }
        ImGui.End();
        if (!open)
            _showPlayers = false;
    }

    private void DrawChat(CoopSession session)
    {
        ImGui.Separator();
        float2? size = new float2(360, 140);
        if (ImGui.BeginChild("##chat-log"u8, in size, ImGuiChildFlags.Borders, ImGuiWindowFlags.None))
        {
            foreach (var (from, text) in session.ChatLog)
            {
                TextC(Theme.Dim, from + ":");
                ImGui.SameLine(0, 4);
                Wrapped(text);
            }
            // Follow new lines unless the reader has scrolled up.
            if (session.ChatLog.Count != _chatSeen && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4)
                ImGui.SetScrollHereY(1f);
            _chatSeen = session.ChatLog.Count;
        }
        ImGui.EndChild();
        ImGui.SetNextItemWidth(360);
        if (ImGui.InputTextWithHint("##chat"u8, "Say something (Enter)"u8, _chatInput, ImGuiInputTextFlags.EnterReturnsTrue, null, IntPtr.Zero))
        {
            session.SendChat(Narsil.Ui.ImGuiText.Value(_chatInput));
            _chatInput.SetValue("".AsSpan());
            ImGui.SetKeyboardFocusHere(-1); // keep typing
        }
    }

    private void DrawCoopCreator(CoopSession session, float2 center, float2? middle)
    {
        ImGui.SetNextWindowPos(in center, ImGuiCond.Always, in middle);
        ImGui.Begin("Who are you?###coop-creator"u8, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
        if (_characterRequested)
        {
            TextC(Theme.Dim, "Arriving...");
            ImGui.End();
            return;
        }
        var pact = _sim.Story.Start == StartKind.Pact;
        _coopCreator ??= new CharacterCreator(Host, _sim.Defs);
        _roster ??= CharacterRoster.Load(Host, _sim.Defs);

        // --character: a saved character straight in (scripted tests), once.
        if (CharacterRoster.PickOverride is { } wanted && _roster.Find(wanted) is { } forced)
        {
            CharacterRoster.PickOverride = null;
            UseSaved(session, forced, pact);
        }
        else if (_coopPicking && _roster.Characters.Count > 0)
        {
            _coopPicker ??= new CharacterPicker(Host, _roster, _sim.Defs);
            Wrapped($"You're new to {session.SessionName}.");
            switch (_coopPicker.Draw(_time, pact))
            {
                case CharacterPicker.Result.Use:
                    UseSaved(session, _coopPicker.Picked!, pact);
                    break;
                case CharacterPicker.Result.Edit:
                    Prefill(_coopPicker.Picked!);
                    (_coopEditing, _coopPicking, _coopDragonStep) = (_coopPicker.Picked!.Id, false, false);
                    break;
                case CharacterPicker.Result.New:
                    (_coopEditing, _coopPicking, _coopDragonStep) = (null, false, false);
                    break;
                case CharacterPicker.Result.Back:
                    LeaveCoop();
                    break;
            }
        }
        else if (_coopDragonStep && _coopDragonCreator != null)
        {
            Wrapped(_coopDragonCreator.EggOnly ? "And the egg you carry:" : "And the dragon you fly in on:");
            switch (_coopDragonCreator.Draw(_time))
            {
                case DragonCreator.Result.Continue:
                    SendCharacter(session);
                    break;
                case DragonCreator.Result.Back:
                    _coopDragonStep = false;
                    break;
            }
        }
        else
        {
            Wrapped(pact
                ? $"You're new to {session.SessionName}. Make your character, then your dragon: you'll fly in to help raise the sorcerer's tower."
                : $"You're new to {session.SessionName}. Make your character: you'll arrive with a dragon egg of your own to keep warm.");
            switch (_coopCreator.Draw(_time))
            {
                case CharacterCreator.Result.Continue:
                    _coopDragonCreator ??= new DragonCreator(Host, _sim.Defs);
                    _coopDragonCreator.EggOnly = !pact; // off the Pact site they bring an egg: choose what hatches
                    _coopDragonStep = true;
                    break;
                case CharacterCreator.Result.Back when _roster.Characters.Count > 0:
                    _coopPicking = true;
                    break;
                case CharacterCreator.Result.Back:
                    LeaveCoop();
                    break;
            }
        }
        ImGui.End();
    }

    /// <summary>The creators on a saved character (and its dragon, if it has one).</summary>
    private void Prefill(SavedCharacter c)
    {
        _coopCreator!.Load(c.Name, c.Look, c.Traits, c.Clothes);
        if (c.HasDragon)
            (_coopDragonCreator ??= new DragonCreator(Host, _sim.Defs)).Load(c.DragonName!, c.DragonLook!.Value, c.DragonBreed);
    }

    /// <summary>
    /// Arrive as a saved character. A Pact needs a dragon: one made in an egg game gets the dragon
    /// step first, and keeps that dragon for next time.
    /// </summary>
    private void UseSaved(CoopSession session, SavedCharacter c, bool pact)
    {
        if (pact && !c.HasDragon)
        {
            Prefill(c);
            (_coopEditing, _coopPicking) = (c.Id, false);
            _coopDragonCreator ??= new DragonCreator(Host, _sim.Defs);
            _coopDragonCreator.EggOnly = false;
            _coopDragonStep = true;
            return;
        }
        _roster!.Put(c); // most recently used
        session.Send(pact
            ? new CreateCharacterCommand(session.Me.Id, c.Name, c.Look, c.Traits, c.DragonName!, c.DragonLook!.Value, c.Clothes, BreedDef(c.DragonBreed))
            : new CreateCharacterCommand(session.Me.Id, c.Name, c.Look, c.Traits, Clothes: c.Clothes));
        _characterRequested = true;
    }

    /// <summary>A breed picked in the creator (by def name) as the command sends it; null for the default.</summary>
    private Sim.Defs.CreatureDef? BreedDef(string? name) => name == null ? null : Sim.Systems.Dragons.BreedOrDefault(_sim.Defs, name);

    /// <summary>Arrive as the character in the creators, and remember them (a new one, or the one edited).</summary>
    private void SendCharacter(CoopSession session)
    {
        var creator = _coopCreator!;
        var step = _coopDragonCreator is { } d && _coopDragonStep ? d : null;
        var withDragon = step is { EggOnly: false } ? step : null;
        var before = _coopEditing is { } id ? _roster?.Characters.Find(c => c.Id == id) : null;
        _roster?.Put(new SavedCharacter
        {
            Id = before?.Id ?? Guid.NewGuid(),
            Name = creator.Name, Look = creator.Look, Traits = creator.Traits.ToList(), Clothes = creator.Clothes.ToList(),
            DragonName = withDragon?.Name ?? before?.DragonName,
            DragonLook = withDragon?.Look ?? before?.DragonLook,
            DragonBreed = withDragon != null ? withDragon.Breed : before?.DragonBreed,
        });
        session.Send(withDragon != null
            ? new CreateCharacterCommand(session.Me.Id, creator.Name, creator.Look, creator.Traits, withDragon.Name, withDragon.Look, creator.Clothes, BreedDef(withDragon.Breed))
            : step != null // the egg step: its breed and element go to the egg
                ? new CreateCharacterCommand(session.Me.Id, creator.Name, creator.Look, creator.Traits, "", step.Look, creator.Clothes, BreedDef(step.Breed))
                : new CreateCharacterCommand(session.Me.Id, creator.Name, creator.Look, creator.Traits, Clothes: creator.Clothes));
        _characterRequested = true;
    }

    /// <summary>Leave the session (closing it for everyone when hosting) and go back to the title.</summary>
    private void LeaveCoop()
    {
        _coop?.Dispose();
        _coop = null;
        Host.SwitchTo(new TitleGame());
    }

    /// <summary>Things single player can do that would break a shared game. Returns why, or null if allowed.</summary>
    private string? CoopForbids(string what) => _coop switch
    {
        null => null,
        { IsHost: false } => $"Only the host can {what} in co-op.",
        // Debug buttons that change the world directly rather than through a command would desync peers.
        _ when what is "load a game" or "reload defs" or "skip time" or "send a raid" or "send a herd" or "put out every fire"
            => $"Can't {what} while hosting co-op.",
        _ => null,
    };

    private void DisposeCoop()
    {
        _coop?.Dispose();
        _coop = null;
    }
}
