using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Nndx.Scripts;
using STS2RitsuLib.Networking.Sidecar;

// Exercise the real production entry points with native-boundary doubles. The
// assertions describe geometry and lifecycle invariants, not the layout formula.
NndxOverlaySync.Initialize();
int passed = 0;
List<object> nativeCases = new();
Run("All five character heights keep their feet fixed and scale only once", () =>
{
    foreach (float height in new[] { 244f, 260f, 270f, 278f, 335f })
    foreach (float scale in new[] { 0.2f, 0.5f, 0.65f, 1f, 1.35f, 2f, 3f })
    {
        NCreature creature = Setup(height, scale);
        NndxVideoOverlay.StartForPlayer(1001, broadcast: false, locallyControlled: true);
        AssertGeometry(creature, Sprite(creature), height * 1.5f * scale);
    }
});
Run("Scaling during playback preserves the feet and uses the current scale", () =>
{
    foreach (float initialScale in new[] { 0.2f, 0.5f, 1f, 2f })
    {
        NCreature creature = Setup(278, initialScale);
        NndxVideoOverlay.StartForPlayer(1001, broadcast: false, locallyControlled: true);
        AnimatedSprite2D sprite = Sprite(creature);
        foreach (float scale in new[] { 3f, 0.5f, 1.35f, 0.2f, 1f })
        {
            creature.Visuals.Scale = Vector2.One * scale;
            AssertGeometry(creature, sprite, 417f * scale);
        }
    }
});
Run("Viewport/ancestor scaling and asymmetric bounds do not shift the anchor", () =>
{
    NCreature creature = Setup(278, 2);
    creature.Scale = new(1.25f, 0.75f);
    creature.Visuals.Bounds.Position = new(-90, -260);
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false);
    AssertGeometry(creature, Sprite(creature), 417f * 2 * 0.75f);
});
Run("Missing bounds height falls back without reapplying default scale", () =>
{
    NCreature creature = Setup(0, 3);
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false);
    AssertGeometry(creature, Sprite(creature), 330f * 1.5f * 3);
});
Run("Remote playback restores the body when its animation finishes", () =>
{
    NCreature creature = Setup(278, 1.35f);
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false);
    Sprite(creature).Finish();
    Node.FlushDeletion();
    Check(creature.Visuals.Body.Visible, "Remote body was not restored");
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false);
    Check(Sprite(creature).IsPlaying(), "Remote playback could not restart");
});
Run("Old animation and timeout callbacks cannot stop replacement playback", () =>
{
    NCreature creature = Setup(278, 1);
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false, locallyControlled: true);
    Action oldFinished = Sprite(creature).CaptureFinished();
    Action oldTimeout = Node.Tree.Timers.Last().CaptureTimeout();
    NndxVideoOverlay.StopForPlayer(1001, broadcast: false);
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false, locallyControlled: true);
    AnimatedSprite2D replacement = Sprite(creature);
    oldFinished();
    oldTimeout();
    Check(replacement.IsPlaying() && !replacement.IsQueuedForDeletion(), "Old callback stopped replacement");
});
Run("Partial startup and sprite cleanup failures do not lock playback", () =>
{
    NCreature creature = Setup(278, 0.5f);
    AudioStreamPlayer.FailNextPlay = true;
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false);
    Node.FlushDeletion();
    Check(creature.Visuals.Body.Visible, "Failed startup left the body hidden");
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false);
    AnimatedSprite2D.FailNextStop = true;
    NndxVideoOverlay.StopForPlayer(1001, broadcast: false);
    Node.FlushDeletion();
    Check(creature.Visuals.Body.Visible, "Failed sprite stop left the body hidden");
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false);
    AssertGeometry(creature, Sprite(creature), 208.5f);
});
Run("A delayed host relay for our player cannot recreate or stop our session", () =>
{
    NCreature creature = Setup(278, 2);
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false, locallyControlled: true);
    NndxVideoOverlay.StopForPlayer(1001, broadcast: false);
    RitsuLibSidecarTypedMessageRegistry.Deliver(new NndxOverlaySyncMessage { PlayerNetId = 1001, IsPlaying = true }, 1);
    Callable.Flush();
    Check(creature.Visuals.Body.Visible, "Own relayed start recreated a remote session");
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false, locallyControlled: true);
    AnimatedSprite2D current = Sprite(creature);
    RitsuLibSidecarTypedMessageRegistry.Deliver(new NndxOverlaySyncMessage { PlayerNetId = 1001, IsPlaying = false }, 1);
    Callable.Flush();
    Check(current.IsPlaying() && !current.IsQueuedForDeletion(), "Own relayed stop interrupted local session");
});
Run("Scale console changes and restores only the requested visual default scale", () =>
{
    NCreature creature = Setup(278, 0.98f);
    RunManager.Instance!.NetService.Type = NetGameType.Singleplayer;
    NndxScaleConsoleCmd command = new();
    foreach (string size in new[] { "0.2", "0.5", "1", "1.35", "2", "3" })
    {
        Check(command.Process(LocalContext.Local, new[] { size }).Success, "Valid scale was rejected");
        Near(creature.Visuals.DefaultScale, float.Parse(size, System.Globalization.CultureInfo.InvariantCulture), "Requested scale");
    }
    Check(command.Process(LocalContext.Local, new[] { "reset" }).Success, "Reset failed");
    Near(creature.Visuals.DefaultScale, 0.98f, "Pre-test scale");
});
Run("Scale console rejects invalid input and multiplayer usage", () =>
{
    NCreature creature = Setup(278, 1);
    NndxScaleConsoleCmd command = new();
    Check(!command.Process(LocalContext.Local, new[] { "2" }).Success, "Multiplayer scale test was allowed");
    RunManager.Instance!.NetService.Type = NetGameType.Singleplayer;
    foreach (string invalid in new[] { "0", "-1", "3.01", "NaN", "Infinity", "1e50", "1,2", "bad" })
    {
        Check(!command.Process(LocalContext.Local, new[] { invalid }).Success, "Invalid scale was accepted");
        Near(creature.Visuals.DefaultScale, 1, "Invalid input changed scale");
    }
    Check(command.Process(LocalContext.Local, new[] { "2" }).Success, "Valid command failed");
    NndxScaleConsoleCmd.RestoreOriginalSize();
    Near(creature.Visuals.DefaultScale, 1, "Lifecycle restore");
});
Run("Mod shortcut overlap respects modifiers and left/right modifier variants", () =>
{
    Check(NndxBindingRules.Overlaps("C", "c"), "Same unmodified key must conflict");
    Check(!NndxBindingRules.Overlaps("Ctrl+C", "C"), "Distinct mod chords should coexist");
    Check(!NndxBindingRules.Overlaps("Ctrl+C", "Shift+C"), "Different modifiers should coexist");
    Check(NndxBindingRules.Overlaps("Ctrl+C", "LeftCtrl+C"), "Generic Ctrl overlaps left Ctrl");
    Check(NndxBindingRules.Overlaps("RightCtrl+C", "Ctrl+C"), "Generic Ctrl overlaps right Ctrl");
    Check(!NndxBindingRules.Overlaps("LeftCtrl+C", "RightCtrl+C"), "Opposite sides do not overlap");
    Check(NndxBindingRules.Overlaps("Alt+Ctrl+C", "Ctrl+Alt+C"), "Modifier order is irrelevant");
    Check(!NndxBindingRules.Overlaps("Ctrl+C", "Ctrl+V"), "Different primary keys should coexist");
});
Run("InputMap shortcuts require their modifiers but vanilla keys ignore extra modifiers", () =>
{
    Check(!NndxBindingRules.SatisfiesModifiers("C", true, false, false, false),
        "Godot Ctrl+C copy shortcut must not reserve plain C");
    Check(NndxBindingRules.SatisfiesModifiers("Ctrl+C", true, false, false, false),
        "Ctrl+C must reserve matching chord");
    Check(NndxBindingRules.SatisfiesModifiers("LeftCtrl+Shift+C", true, false, true, false),
        "Side-specific modifiers satisfy Godot generic modifiers");
    Check(!NndxBindingRules.SatisfiesModifiers("Ctrl+C", true, true, false, false),
        "Missing Alt must not match Ctrl+Alt");
    Check(NndxBindingRules.SatisfiesModifiers("Ctrl+C", false, false, false, false),
        "Plain vanilla C remains occupied even when Ctrl is held");
    Check(NndxBindingRules.PrimaryKey("Ctrl+Shift+C") == "C", "Extract primary key for vanilla lookup");
});
Run("Hardcoded game shortcuts are reserved even outside the remappable input table", () =>
{
    Check(NndxBindingRules.FixedGameConflict("Quoteleft") != null, "Console key is fixed");
    Check(NndxBindingRules.FixedGameConflict("Ctrl+F2") != null, "Feedback ignores modifiers");
    Check(NndxBindingRules.FixedGameConflict("Shift+Key8") != null, "Shift+8 opens console");
    Check(NndxBindingRules.FixedGameConflict("Ctrl+Meta+F") != null, "macOS fullscreen is reserved");
    Check(NndxBindingRules.FixedGameConflict("Alt+Enter") != null, "Windows fullscreen is reserved");
    Check(NndxBindingRules.FixedGameConflict("F") == null, "Plain F is not fullscreen");
    Check(NndxBindingRules.FixedGameConflict("Key8") == null, "Plain 8 is checked through vanilla mappings");
});
Run("Local input preferences persist keyboard and ignore legacy controller settings", () =>
{
    string directory = Path.Combine(Path.GetTempPath(), "nndx-input-" + Guid.NewGuid());
    string path = Path.Combine(directory, "input.json");
    try
    {
        NndxInputPreferences defaults = NndxInputPreferences.Load(path);
        Check(defaults.Keyboard == "C", "Default keyboard binding must remain C");
        var chosen = defaults with { Keyboard = "Ctrl+J" };
        chosen.Save(path);
        Check(NndxInputPreferences.Load(path) == chosen, "Input settings must survive restart");
        var cleared = chosen with { Keyboard = "" };
        cleared.Save(path);
        Check(NndxInputPreferences.Load(path) == cleared, "Cleared bindings must stay cleared after restart");
        Check(!File.Exists(path + ".tmp"), "Atomic save should not leave temp file");
        File.WriteAllText(path, "{\"Keyboard\":\"J\",\"ControllerButton\":16,\"SteamInputEnabled\":true}");
        var migrated = NndxInputPreferences.Load(path);
        Check(migrated.Keyboard == "J", "Legacy controller preferences must not reset keyboard binding");
        migrated.Save(path);
        Check(!File.ReadAllText(path).Contains("ControllerButton") && !File.ReadAllText(path).Contains("SteamInputEnabled"),
            "Legacy controller fields must not be written back");
        File.WriteAllText(path, "invalid JSON");
        bool failed = false;
        try { NndxInputPreferences.Load(path); } catch (System.Text.Json.JsonException) { failed = true; }
        Check(failed && File.ReadAllText(path) == "invalid JSON", "Corrupt preferences must be preserved");
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
});
Run("Production input controller validates, suspends, restores and dispatches local bindings", () =>
{
    string directory = Path.Combine(Path.GetTempPath(), "nndx-input-controller-" + Guid.NewGuid());
    NndxInput? controller = null;
    try
    {
        NCreature creature = Setup(278, 1);
        ProjectSettings.UserPath = directory;
        MegaCrit.Sts2.Core.Nodes.NGame.Instance = new();
        var input = MegaCrit.Sts2.Core.Nodes.CommonUi.NInputManager.Instance!;
        input.Mapping["mega_select_card_1"] = Key.Key1;
        InputMap.Actions["ui_copy"] = [new InputEventKey { Keycode = Key.C, CtrlPressed = true }];
        InputMap.Actions["controller_confirm"] = [new InputEventJoypadButton { ButtonIndex = JoyButton.A }];
        NndxInput.Initialize();
        Callable.Flush();
        controller = Node.All.OfType<NndxInput>().Single();
        controller._Ready();
        var keyboard = (NndxBindingCapture)STS2RitsuLib.Settings.ModSettingsSectionBuilder.CustomControls["keyboard"];
        keyboard._Ready();
        Check(!STS2RitsuLib.Settings.ModSettingsSectionBuilder.Paragraphs.ContainsKey("status")
            && !STS2RitsuLib.Settings.ModSettingsSectionBuilder.Paragraphs.ContainsKey("notes"),
            "Settings must not include secondary status and notes paragraphs");
        Check(keyboard.Children.OfType<Label>().Single().Text == "支持组合键，并且必须包含一个非修饰键",
            "Keyboard hint must match exact requested text without punctuation");
        Check(STS2RitsuLib.Settings.ModSettingsSectionBuilder.CustomControls.Keys.SequenceEqual(new[] { "keyboard" }),
            "Controller and Steam Input settings must be absent");
        string path = ProjectSettings.GlobalizePath("");
        const string keyboardId = "sts_nndx.toggle_video";
        Check(STS2RitsuLib.RuntimeInput.RuntimeHotkeyService.Active[keyboardId].Binding == "C",
            "Default C must coexist with Godot Ctrl+C copy");
        Check(STS2RitsuLib.RuntimeInput.RuntimeHotkeyService.Active.Count == 1
            && STS2RitsuLib.RuntimeInput.RuntimeHotkeyService.Active.Values.All(x => !x.Options.ExposeToSteamInput),
            "Only the keyboard shortcut should be registered; no Steam actions");
        keyboard.Write("Ctrl+Key1");
        Check(keyboard.Read() == "C" && !File.Exists(path), "Numeric selection conflicts must reject without saving");
        keyboard.Write("J");
        Check(keyboard.Read() == "J" && NndxInputPreferences.Load(path).Keyboard == "J", "Rebind must persist immediately");
        keyboard.Write("Ctrl+C");
        Check(keyboard.Read() == "J", "Godot copy chord must be rejected");
        keyboard.Write("");
        Check(!STS2RitsuLib.RuntimeInput.RuntimeHotkeyService.Active.ContainsKey(keyboardId)
            && NndxInputPreferences.Load(path).Keyboard == "", "Clearing must unregister and persist");
        keyboard.Write("Ctrl+J");
        input.Mapping["ui_accept"] = Key.J;
        input.Emit(MegaCrit.Sts2.Core.Nodes.CommonUi.NInputManager.SignalName.InputRebound);
        Check(!STS2RitsuLib.RuntimeInput.RuntimeHotkeyService.Active.ContainsKey(keyboardId),
            "Vanilla rebinding must immediately suspend conflicting Ctrl+J");
        Check(NndxInputPreferences.Load(path).Keyboard == "Ctrl+J", "Suspension must preserve preference");
        input.Mapping.Remove("ui_accept");
        input.Emit(MegaCrit.Sts2.Core.Nodes.CommonUi.NInputManager.SignalName.InputRebound);
        Check(STS2RitsuLib.RuntimeInput.RuntimeHotkeyService.Active.ContainsKey(keyboardId), "Clearing conflict must restore binding");
        using (STS2RitsuLib.RuntimeInput.RuntimeHotkeyService.Register("Ctrl+J", () => { },
            new() { Id = "other_mod", DisplayName = "Other Mod" }))
        {
            controller._Process(1);
            Check(!STS2RitsuLib.RuntimeInput.RuntimeHotkeyService.Active.ContainsKey(keyboardId), "Late mod registration must suspend binding");
            keyboard.Write("Ctrl+J");
            Check(keyboard.Read() == "Ctrl+J", "Rejected setting must retain existing saved value");
        }
        controller._Process(1);
        Check(STS2RitsuLib.RuntimeInput.RuntimeHotkeyService.Active.ContainsKey(keyboardId), "Removing mod conflict must restore binding");
        keyboard.Write("C");
        File.Delete(path);
        Directory.CreateDirectory(path); // Force atomic rename to fail.
        keyboard.Write("J");
        Check(keyboard.Read() == "C" && STS2RitsuLib.RuntimeInput.RuntimeHotkeyService.Active[keyboardId].Binding == "C",
            "Save failure must retain both saved preference in memory and active binding");
        Directory.Delete(path);
        var keyboardHandle = STS2RitsuLib.RuntimeInput.RuntimeHotkeyService.Active[keyboardId];
        Time.Now += 1000;
        keyboardHandle.Invoke();
        Check(!creature.Visuals.Body.Visible, "Keyboard must start playback");
        Time.Now += 1000;
        keyboardHandle.Invoke();
        Check(creature.Visuals.Body.Visible, "Keyboard must stop playback");
        Time.Now += 1000;
        controller._UnhandledInput(new InputEventJoypadButton { Pressed = true, ButtonIndex = JoyButton.Paddle1 });
        Check(creature.Visuals.Body.Visible, "Native controller input must not trigger playback");
        MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance = new() { ScreenCount = 1 };
        Time.Now += 1000;
        keyboardHandle.Invoke();
        Check(creature.Visuals.Body.Visible, "Settings/overlay screens must suppress playback");
        MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance = null;
        MegaCrit.Sts2.Core.Nodes.Debug.NDevConsole.Instance!.Visible = true;
        keyboardHandle.Invoke();
        Check(creature.Visuals.Body.Visible, "Developer console must suppress playback");
        MegaCrit.Sts2.Core.Nodes.Debug.NDevConsole.Instance.Visible = false;
        Node.Tree.Root.Focus = new LineEdit();
        keyboardHandle.Invoke();
        Check(creature.Visuals.Body.Visible, "Text entry must suppress playback");
    }
    finally
    {
        controller?._ExitTree();
        Node.Tree.Root.Focus = null;
        InputMap.Actions.Clear();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
});
Run("Keyboard capture supports recording, clearing, cancellation and ignores controller input", () =>
{
    string binding = "C";
    var recorded = new List<string>();
    var keyboard = new NndxBindingCapture
    {
        Title = "键盘", Read = () => binding,
        Write = value => { binding = value; recorded.Add(value); },
    };
    keyboard._Ready();
    var keyboardRow = keyboard.Children.OfType<HBoxContainer>().Single();
    var keyboardButton = (STS2RitsuLib.Settings.ModSettingsMiniButton)keyboardRow.Children[1];
    var keyboardClear = (STS2RitsuLib.Settings.ModSettingsMiniButton)keyboardRow.Children[2];
    keyboardButton.Press();
    keyboard._Input(new InputEventKey { Keycode = Key.Ctrl, CtrlPressed = true, Pressed = true });
    Check(recorded.Count == 0, "Modifier-only input must not finish recording");
    keyboard._Input(new InputEventKey { Keycode = Key.J, CtrlPressed = true, Pressed = true });
    Check(binding == "Ctrl+J" && keyboardButton.Text == "Ctrl+J", "Keyboard must record combination");
    keyboardButton.Press();
    keyboard._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true });
    Check(binding == "Ctrl+J" && keyboardButton.Text == "Ctrl+J", "Escape must preserve binding");
    keyboardClear.Press();
    Check(binding == "" && keyboardButton.Text == "未绑定", "Clear must update binding and text");

    keyboardButton.Press();
    int before = recorded.Count;
    Node.Tree.Root.InputHandled = false;
    keyboard._Input(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.A, Pressed = true });
    Check(recorded.Count == before && !Node.Tree.Root.InputHandled, "Keyboard recorder must ignore joypad buttons");
    Node.Tree.Root.Focus = null;
    keyboard._Process(0);
    Check(keyboardButton.Text == "未绑定" && !keyboardClear.Disabled, "Losing focus must cancel recording");
    keyboardButton.Press();
    keyboard.Visible = false;
    keyboard._Process(0);
    Check(keyboardButton.Text == "未绑定" && !keyboardClear.Disabled, "Closing settings must cancel recording");
    keyboard.Visible = true;
    keyboard.Write = _ => { };
    keyboardButton.Press();
    keyboard._Input(new InputEventKey { Keycode = Key.J, Pressed = true });
    Check(keyboardButton.Text == "未绑定", "Rejected binding must restore previous display");
    Node.Tree.Root.Focus = null;
});
Run("All four portraits swap only the target player and restore on every exit path", () =>
{
    NCreature creature = Setup(278, 1);
    NndxPortraitSwap.Preload();
    var panel = AddPortrait(1001);
    var original = panel.Icon.Texture;
    var other = AddPortrait(2002);
    var otherOriginal = other.Icon.Texture;
    for (byte face = 0; face < NndxPortraitSwap.FaceCount; face++)
    {
        NndxVideoOverlay.StartForPlayer(1001, broadcast: false, faceIndex: face);
        Check(ReferenceEquals(panel.Icon.Texture, Face(face)), "Requested face must be applied");
        Check(ReferenceEquals(other.Icon.Texture, otherOriginal), "Other player's icon must remain unchanged");
        Sprite(creature).Finish();
        Node.FlushDeletion();
        Check(ReferenceEquals(panel.Icon.Texture, original), "Natural end must restore original portrait");
        NndxVideoOverlay.StartForPlayer(1001, broadcast: false, faceIndex: face);
        NndxVideoOverlay.StopForPlayer(1001, broadcast: false);
        Check(ReferenceEquals(panel.Icon.Texture, original), "Manual stop must restore original portrait");
        NndxVideoOverlay.StartForPlayer(1001, broadcast: false, faceIndex: face);
        NndxVideoOverlay.ForceStop();
        Check(ReferenceEquals(panel.Icon.Texture, original), "Battle end must restore original portrait");
    }
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false, faceIndex: 1);
    creature.Entity.IsDead = true;
    NndxVideoOverlay.HandlePlayerDeath(creature.Entity);
    Check(ReferenceEquals(panel.Icon.Texture, original), "Death must restore original portrait");
});
Run("Portrait ownership survives panel rebuilds, old callbacks and other mod changes", () =>
{
    NCreature creature = Setup(278, 1);
    var panel = AddPortrait(1001);
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false, faceIndex: 0);
    Action stale = Sprite(creature).CaptureFinished();
    NndxVideoOverlay.StopForPlayer(1001, broadcast: false);
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false, faceIndex: 2);
    stale();
    Check(ReferenceEquals(panel.Icon.Texture, Face(2)), "Old callback must not restore a newer face");
    panel.Panel.Free();
    var rebuilt = AddPortrait(1001);
    var rebuiltOriginal = rebuilt.Icon.Texture;
    NndxVideoOverlay.OnPortraitPanelReady(rebuilt.Panel);
    NndxVideoOverlay.OnPortraitPanelReady(rebuilt.Panel);
    Check(ReferenceEquals(rebuilt.Icon.Texture, Face(2)), "Rebuilt panel must display current session's face");
    NndxVideoOverlay.StopForPlayer(1001, broadcast: false);
    Check(ReferenceEquals(rebuilt.Icon.Texture, rebuiltOriginal), "Rebuilt panel must restore its own original texture");
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false, faceIndex: 1);
    var anotherModsTexture = new Texture2D();
    rebuilt.Icon.Texture = anotherModsTexture;
    NndxVideoOverlay.StopForPlayer(1001, broadcast: false);
    Check(ReferenceEquals(rebuilt.Icon.Texture, anotherModsTexture), "Cleanup must not overwrite another mod's replacement");
    RunManager.Instance!.NetService.Type = NetGameType.Singleplayer;
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false, faceIndex: 0);
    NndxVideoOverlay.OnPortraitPanelReady(rebuilt.Panel);
    Check(ReferenceEquals(rebuilt.Icon.Texture, anotherModsTexture), "Singleplayer must not replace UI portraits");
    NndxVideoOverlay.ForceStop();
});
Run("Versioned sync messages round-trip every field and reject malformed payloads", () =>
{
    var message = Message(2002, true, 123, 3);
    byte[] data = NndxOverlaySyncMessage.Serialize(message);
    Check(data.Length == 26, "Payload must stay bounded at 26 bytes");
    var decoded = NndxOverlaySyncMessage.Deserialize(data);
    Check(decoded.PlayerNetId == 2002 && decoded.IsPlaying && decoded.FaceIndex == 3
        && decoded.Sequence == 123 && decoded.RunSeed == 123 && decoded.RoomId == 42,
        "Sync must preserve player, chosen portrait, order and combat scope");
    var stop = NndxOverlaySyncMessage.Deserialize(NndxOverlaySyncMessage.Serialize(Message(2002, false, 124)));
    Check(!stop.IsPlaying && stop.FaceIndex == 255, "Stop payload must have no face");
    foreach (int length in new[] { 0, 9, 25, 27 }) ExpectInvalid(new byte[length]);
    byte[] invalidFlag = (byte[])data.Clone(); invalidFlag[0] = 2; ExpectInvalid(invalidFlag);
    byte[] invalidFace = (byte[])data.Clone(); invalidFace[9] = NndxPortraitSwap.FaceCount; ExpectInvalid(invalidFace);
    byte[] zeroSequence = (byte[])data.Clone(); Array.Clear(zeroSequence, 10, 8); ExpectInvalid(zeroSequence);
    byte[] negativeRoom = (byte[])data.Clone(); Array.Fill(negativeRoom, (byte)255, 22, 4); ExpectInvalid(negativeRoom);
    void ExpectInvalid(byte[] bytes)
    {
        bool rejected = false;
        try { NndxOverlaySyncMessage.Deserialize(bytes); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Malformed packet must be rejected");
    }
});
Run("Remote sequence guards reject duplicates, stale transitions, wrong sender and old rooms", () =>
{
    Setup(278, 1);
    var remote = AddRemote(2002);
    var panel = AddPortrait(2002);
    var original = panel.Icon.Texture;
    RunManager.Instance!.NetService.Type = NetGameType.Host;
    RitsuLibSidecarTypedMessageRegistry.Sent.Clear();
    RitsuLibSidecarTypedMessageRegistry.Deliver(Message(2002, true, 10, 1), 2002);
    Check(ReferenceEquals(panel.Icon.Texture, original), "Receive callback must defer native UI work");
    Callable.Flush();
    Check(ReferenceEquals(panel.Icon.Texture, Face(1)), "Host must use sender's selected face");
    Action stale = Sprite(remote).CaptureFinished();
    Deliver(Message(2002, true, 10, 2), 2002);
    Deliver(Message(2002, false, 9), 2002);
    Check(RitsuLibSidecarTypedMessageRegistry.Sent.Count == 1, "Duplicates and stale packets must not be relayed");
    Deliver(Message(2002, true, 12, 2), 2002);
    stale();
    Deliver(Message(2002, false, 11), 2002);
    Check(ReferenceEquals(panel.Icon.Texture, Face(2)), "Old callback/stop must not revert newer playback");
    Deliver(Message(2002, false, 13), 2002);
    Check(ReferenceEquals(panel.Icon.Texture, original), "Newest stop must restore portrait");
    Deliver(Message(2002, true, 12, 0), 2002);
    Deliver(Message(2002, true, 20, 0), 3003);
    Deliver(new NndxOverlaySyncMessage { PlayerNetId = 2002, IsPlaying = true, FaceIndex = 0,
        Sequence = 30, RunSeed = 999, RoomId = 42 }, 2002);
    Deliver(new NndxOverlaySyncMessage { PlayerNetId = 2002, IsPlaying = true, FaceIndex = 0,
        Sequence = 31, RunSeed = 123, RoomId = 41 }, 2002);
    Deliver(Message(9009, true, 32, 0), 9009);
    Check(ReferenceEquals(panel.Icon.Texture, original) && remote.Visuals.Body.Visible,
        "Stale/replayed, forged, old-room and unknown-player starts must not apply");
    int sent = RitsuLibSidecarTypedMessageRegistry.Sent.Count;
    RunManager.Instance.NetService.Type = NetGameType.Client;
    Deliver(Message(2002, true, 40, 0), 1);
    Check(ReferenceEquals(panel.Icon.Texture, Face(0)), "Client must display host-relayed chosen face");
    Check(RitsuLibSidecarTypedMessageRegistry.Sent.Count == sent, "Client must not echo host relay");
    Sprite(remote).Finish();
    Deliver(Message(2002, true, 40, 0), 1);
    Check(ReferenceEquals(panel.Icon.Texture, original), "Natural completion plus duplicate start must stay stopped");
    void Deliver(NndxOverlaySyncMessage message, ulong sender)
    { RitsuLibSidecarTypedMessageRegistry.Deliver(message, sender); Callable.Flush(); }
});
Run("Rapid local toggles share the chosen portrait and remain bounded without reloading textures", () =>
{
    NCreature creature = Setup(278, 1);
    var panel = AddPortrait(1001);
    var original = panel.Icon.Texture;
    RitsuLibSidecarTypedMessageRegistry.Sent.Clear();
    Time.Now += 1000;
    for (int i = 0; i < 120; i++)
    {
        Time.Now += 150;
        NndxVideoOverlay.Toggle();
        int accepted = RitsuLibSidecarTypedMessageRegistry.Sent.Count;
        for (int spam = 0; spam < 20; spam++) NndxVideoOverlay.Toggle();
        Check(RitsuLibSidecarTypedMessageRegistry.Sent.Count == accepted, "Spam inside cooldown must not send more messages");
        var message = (NndxOverlaySyncMessage)RitsuLibSidecarTypedMessageRegistry.Sent[^1];
        Check(message.IsPlaying == (i % 2 == 0), "Each accepted toggle must alternate state");
        Check(message.IsPlaying ? ReferenceEquals(panel.Icon.Texture, Face(message.FaceIndex))
            : ReferenceEquals(panel.Icon.Texture, original), "Local displayed face must equal transmitted face");
        Node.FlushDeletion();
    }
    var messages = RitsuLibSidecarTypedMessageRegistry.Sent.Cast<NndxOverlaySyncMessage>().ToArray();
    Check(messages.Length == 120, "Only accepted start/stop transitions should send messages");
    Check(messages.Zip(messages.Skip(1), (a, b) => b.Sequence > a.Sequence).All(x => x), "Outgoing operations must be monotonic");
    for (int i = 1; i <= NndxPortraitSwap.FaceCount; i++)
        Check(ResourceLoader.Loads[$"res://res/face/face{i}.png"] == 1, "Portraits must only be loaded once");
    Check(Node.All.OfType<AnimatedSprite2D>().All(x => x.Freed), "Stopped rapid toggles must not leak live sprites");
    Check(Node.All.OfType<AudioStreamPlayer>().All(x => x.Freed), "Stopped rapid toggles must not leak live audio nodes");
    Check(creature.Visuals.Body.Visible, "Rapid toggles must finish with original body restored");
});
NndxVideoOverlay.ForceStop();
Node.FlushDeletion();
Console.WriteLine($"PASS: {passed} regression groups");
if (args.Length == 1)
{
    File.WriteAllText(args[0], System.Text.Json.JsonSerializer.Serialize(nativeCases));
    Console.WriteLine($"Wrote {nativeCases.Count} production layout cases for native Godot verification");
}

void Run(string name, Action test)
{
    test();
    passed++;
    Console.WriteLine($"PASS: {name}");
}

static NCreature Setup(float height, float scale)
{
    NndxScaleConsoleCmd.RestoreOriginalSize();
    NndxVideoOverlay.ForceStop();
    Node.FlushDeletion();
    Node.All.Clear();
    Node.Tree.Timers.Clear();
    MegaCrit.Sts2.Core.Nodes.NRun.Instance = null;
    CombatManager.Instance.State = new();
    CombatManager.Instance.IsInProgress = true;
    NCombatRoom.Instance = new();
    RunManager.Instance = new();
    RunManager.Instance.NetService.Type = NetGameType.Client;
    RunManager.Instance.NetService.NetId = 1001;
    NCreature creature = new() { Position = new(600, 500) };
    creature.AddChild(creature.Visuals);
    creature.Visuals.AddChild(creature.Visuals.Bounds);
    creature.Visuals.AddChild(creature.Visuals.Body);
    creature.Visuals.Bounds.Position = new(-121, -height);
    creature.Visuals.Bounds.Size = new(242, height);
    creature.Visuals.DefaultScale = scale;
    creature.Visuals.Scale = Vector2.One * scale;
    Creature entity = new() { Node = creature };
    Player player = new() { NetId = 1001, Creature = entity };
    entity.Player = player;
    creature.Entity = entity;
    CombatManager.Instance.State.Players.Add(player);
    LocalContext.Local = player;
    NCombatRoom.Instance.CreatureNodes.Add(creature);
    return creature;
}

static AnimatedSprite2D Sprite(NCreature creature) => creature.Visuals.Children
    .OfType<AnimatedSprite2D>().Single(sprite => !sprite.IsQueuedForDeletion() && !sprite.Freed);

void AssertGeometry(NCreature creature, AnimatedSprite2D sprite, float expectedHeight)
{
    // The texture's authored ground line is y=315; y=330 includes transparent padding.
    Vector2 bottomCenter = sprite.ToGlobal(sprite.Offset + new Vector2(135, 315));
    Vector2 baseline = creature.Visuals.GlobalPosition;
    Near(bottomCenter.X, baseline.X, "Foot X");
    Near(bottomCenter.Y, baseline.Y, "Foot Y");
    float height = sprite.ToGlobal(sprite.Offset + new Vector2(135, 330)).Y
        - sprite.ToGlobal(sprite.Offset + new Vector2(135, 0)).Y;
    Near(height, expectedHeight, "Rendered height");
    nativeCases.Add(new
    {
        parent_scale = new[] { creature.Scale.X, creature.Scale.Y },
        body_scale = new[] { creature.Visuals.Scale.X, creature.Visuals.Scale.Y },
        sprite_scale = new[] { sprite.Scale.X, sprite.Scale.Y },
        sprite_position = new[] { sprite.Position.X, sprite.Position.Y },
        sprite_offset = new[] { sprite.Offset.X, sprite.Offset.Y },
        expected_height = expectedHeight,
    });
}

static void Near(float actual, float expected, string label) =>
    Check(Math.Abs(actual - expected) < 0.005f, $"{label}: expected {expected}, got {actual}");

static void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

static Texture2D Face(byte index) => (Texture2D)ResourceLoader.Resources[$"res://res/face/face{index + 1}.png"];

static NndxOverlaySyncMessage Message(ulong player, bool playing, ulong sequence, byte face = 0)
    => new() { PlayerNetId = player, IsPlaying = playing, FaceIndex = playing ? face : (byte)255,
        Sequence = sequence, RunSeed = 123, RoomId = 42 };

static (MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState Panel, TextureRect Icon) AddPortrait(ulong player)
{
    MegaCrit.Sts2.Core.Nodes.NRun.Instance ??= new();
    var target = CombatManager.Instance.State.GetPlayer(player) ?? new Player { NetId = player };
    var panel = new MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState { Player = target };
    var icon = new TextureRect { Texture = new Texture2D() };
    panel.AddChild(icon);
    MegaCrit.Sts2.Core.Nodes.NRun.Instance.GlobalUi.MultiplayerPlayerContainer.AddChild(panel);
    return (panel, icon);
}

static NCreature AddRemote(ulong id)
{
    var creature = new NCreature();
    creature.AddChild(creature.Visuals);
    creature.Visuals.AddChild(creature.Visuals.Bounds);
    creature.Visuals.AddChild(creature.Visuals.Body);
    creature.Visuals.Bounds.Size = new(242, 278);
    var entity = new Creature { Node = creature };
    var player = new Player { NetId = id, Creature = entity };
    entity.Player = player;
    creature.Entity = entity;
    CombatManager.Instance.State.Players.Add(player);
    NCombatRoom.Instance!.CreatureNodes.Add(creature);
    return creature;
}
