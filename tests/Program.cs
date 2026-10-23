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
        Check(defaults.PetsLaugh, "Pets default to enabled");
        var chosen = defaults with { Keyboard = "Ctrl+J", PetsLaugh = false };
        chosen.Save(path);
        Check(NndxInputPreferences.Load(path) == chosen, "Input settings must survive restart");
        var cleared = chosen with { Keyboard = "" };
        cleared.Save(path);
        Check(NndxInputPreferences.Load(path) == cleared, "Cleared bindings must stay cleared after restart");
        Check(!File.Exists(path + ".tmp"), "Atomic save should not leave temp file");
        File.WriteAllText(path, "{\"Keyboard\":\"J\",\"ControllerButton\":16,\"SteamInputEnabled\":true}");
        var migrated = NndxInputPreferences.Load(path);
        Check(migrated.PetsLaugh, "Old settings without pet field default to enabled");
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
        var portrait = AddPortrait(1001);
        var originalPortrait = portrait.Icon.Texture;
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
        var petSetting = (STS2RitsuLib.Settings.IModSettingsValueBinding<bool>)
            STS2RitsuLib.Settings.ModSettingsSectionBuilder.Bindings["pets_laugh"];
        petSetting.Write(false);
        Check(petSetting.Read() && NndxInput.PetsLaughEnabled, "Failed pet preference save must keep the previous setting");
        Directory.Delete(path);
        petSetting.Write(false);
        Check(!NndxInputPreferences.Load(path).PetsLaugh && NndxInputPreferences.Load(path).Keyboard == "C",
            "Pet toggle must persist without changing keyboard binding");
        petSetting.Write(true);
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
        MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance = new() { IsOpen = true };
        Time.Now += 1000;
        keyboardHandle.Invoke();
        Check(LiveAudio().Count() == 1 && !Sprite(creature).Visible
            && !ReferenceEquals(portrait.Icon.Texture, originalPortrait),
            "Keyboard on the map must start sound/portrait with the body animation hidden");
        MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance.IsOpen = false;
        MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance.Update();
        Check(Sprite(creature).Visible, "Closing the map must reveal the ongoing animation");
        MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance.IsOpen = true;
        MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance.Update();
        Time.Now += 1000;
        keyboardHandle.Invoke();
        Check(!LiveAudio().Any() && creature.Visuals.Body.Visible
            && ReferenceEquals(portrait.Icon.Texture, originalPortrait),
            "Keyboard on the map must also stop playback and restore the portrait/body");
        MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance = null;
        MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance = new() { ScreenCount = 1 };
        Time.Now += 1000;
        keyboardHandle.Invoke();
        Check(LiveAudio().Count() == 1 && Sprite(creature).Visible,
            "An overlay with the multiplayer portrait visible must allow playback");
        Time.Now += 150; keyboardHandle.Invoke();
        MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance = new() { InUse = true };
        Time.Now += 150; keyboardHandle.Invoke();
        Check(LiveAudio().Count() == 1 && !Sprite(creature).Visible,
            "Deck/capstone screen must allow audio and portrait without drawing the body above it");
        Time.Now += 150; keyboardHandle.Invoke();
        Check(!LiveAudio().Any() && creature.Visuals.Body.Visible, "Deck screen must allow stopping playback too");
        MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance = null;
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
    NndxVideoOverlay.HandleCreatureDeath(creature.Entity);
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
    Check(data.Length == 38, "Payload must stay bounded at 38 bytes");
    var decoded = NndxOverlaySyncMessage.Deserialize(data);
    Check(decoded.PlayerNetId == 2002 && decoded.IsPlaying && decoded.FaceIndex == 3
        && decoded.Sequence == 123 && decoded.RunSeed == 123 && decoded.RoomId == 42
        && decoded.ActIndex == 0 && decoded.MapCol == 3 && decoded.MapRow == 7,
        "Sync must preserve player, chosen portrait, order and combat scope");
    var stop = NndxOverlaySyncMessage.Deserialize(NndxOverlaySyncMessage.Serialize(Message(2002, false, 124)));
    Check(!stop.IsPlaying && stop.FaceIndex == 255, "Stop payload must have no face");
    foreach (int length in new[] { 0, 9, 25, 26, 37, 39 }) ExpectInvalid(new byte[length]);
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
Run("Bodyless rooms play audio and portraits, finish naturally and ignore stale callbacks", () =>
{
    var creature = SetupRoom(MegaCrit.Sts2.Core.Rooms.RoomType.Event);
    var panel = AddPortrait(1001);
    var original = panel.Icon.Texture;
    NndxVideoOverlay.StartForPlayer(1001, true, true, 2);
    Check(ReferenceEquals(panel.Icon.Texture, Face(2)), "Bodyless event must change the portrait");
    Check(creature.Visuals.Body.Visible && !LiveSprites().Any(), "Bodyless event must not hide a stale combat body");
    var audio = LiveAudio().Single();
    var oldFinish = audio.CaptureFinished();
    var oldTimeout = Node.Tree.Timers.Last().CaptureTimeout();
    audio.Finish();
    Check(ReferenceEquals(panel.Icon.Texture, original), "Audio completion must restore the portrait without a sprite");
    NndxVideoOverlay.StartForPlayer(1001, true, true, 3);
    var replacement = LiveAudio().Single();
    oldFinish(); oldTimeout();
    Check(replacement.Playing && !replacement.IsQueuedForDeletion(), "Stale audio/timer must not stop the replacement");
    Node.Tree.Timers.Last().Expire();
    Check(ReferenceEquals(panel.Icon.Texture, original) && !LiveAudio().Any(), "Timeout must recover missing audio completion");
});
Run("All non-rest room kinds use a safe fallback and rest sites reject local and remote playback", () =>
{
    foreach (var kind in new[] { MegaCrit.Sts2.Core.Rooms.RoomType.Event, MegaCrit.Sts2.Core.Rooms.RoomType.Treasure,
        MegaCrit.Sts2.Core.Rooms.RoomType.Shop, MegaCrit.Sts2.Core.Rooms.RoomType.Map, MegaCrit.Sts2.Core.Rooms.RoomType.Unassigned })
    {
        SetupRoom(kind);
        AddPortrait(1001);
        Time.Now += 1000;
        NndxVideoOverlay.Toggle();
        Check(LiveAudio().Count() == 1 && !LiveSprites().Any(), $"Unknown body in {kind} must play audio only");
        Time.Now += 150;
        NndxVideoOverlay.Toggle();
        Check(!LiveAudio().Any(), "Second key press must stop audio-only playback");
    }
    SetupRoom(MegaCrit.Sts2.Core.Rooms.RoomType.RestSite);
    AddRemoteWithoutBody(2002);
    var panel = AddPortrait(2002);
    var original = panel.Icon.Texture;
    NndxVideoOverlay.Toggle();
    NndxVideoOverlay.StartForPlayer(2002, false, faceIndex: 1);
    DeliverRoom(Message(2002, true, 20, 1));
    Check(!LiveAudio().Any() && !LiveSprites().Any() && ReferenceEquals(panel.Icon.Texture, original),
        "Rest site must reject local, direct and network starts");
});
Run("Visual-only event bodies animate without an active combat, including Architect layouts", () =>
{
    var creature = Setup(278, 0.5f);
    CombatManager.Instance.State.RunState.CurrentRoom = new() { RoomType = MegaCrit.Sts2.Core.Rooms.RoomType.Event };
    CombatManager.Instance.IsInProgress = false;
    NCombatRoom.Instance!.Mode = MegaCrit.Sts2.Core.Rooms.CombatRoomMode.VisualOnly;
    NndxVideoOverlay.StartForPlayer(1001, false);
    AssertGeometry(creature, Sprite(creature), 208.5f);
    Check(!creature.Visuals.Body.Visible, "Visible event body must be replaced");
    NndxVideoOverlay.StopForPlayer(1001, false);
    Check(creature.Visuals.Body.Visible, "Event body must be restored");
    creature.Visuals.Visible = false;
    NndxVideoOverlay.StartForPlayer(1001, false);
    Check(!LiveSprites().Any() && LiveAudio().Any(), "Hidden event bodies must fall back to audio");
});
Run("Merchant replacements preserve anchors, scale and multiplayer player identity", () =>
{
    SetupRoom(MegaCrit.Sts2.Core.Rooms.RoomType.Shop);
    AddRemoteWithoutBody(2002);
    AddRemoteWithoutBody(3003);
    // Run order differs from the local-first shop order on this client.
    var state = CombatManager.Instance.State.RunState;
    var me = state.Players[0]; state.Players.Remove(me); state.Players.Insert(1, me);
    var shop = new NMerchantRoom();
    MegaCrit.Sts2.Core.Nodes.NRun.Instance!.MerchantRoom = shop;
    foreach (int i in new[] { 0, 1, 2 })
    {
        var character = new MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantCharacter
            { Position = new(500 - i * 275, 500), Scale = Vector2.One * 1.2f };
        character.AddChild(new MegaCrit.Sts2.Core.Bindings.MegaSpine.TestSpineSprite { Scale = Vector2.One * 0.47f });
        shop.PlayerVisuals.Add(character);
    }
    NndxVideoOverlay.StartForPlayer(2002, false);
    var target = shop.PlayerVisuals[1];
    var sprite = target.Children.OfType<AnimatedSprite2D>().Single();
    Check(!target.Children.OfType<MegaCrit.Sts2.Core.Bindings.MegaSpine.TestSpineSprite>().Single().Visible,
        "Remote player's own merchant body must be hidden");
    Check(shop.PlayerVisuals[0].Children.OfType<MegaCrit.Sts2.Core.Bindings.MegaSpine.TestSpineSprite>().Single().Visible,
        "Local player's merchant body must be untouched");
    var foot = sprite.ToGlobal(sprite.Offset + new Vector2(135, 315));
    Near(foot.X, target.GlobalPosition.X, "Shop foot X"); Near(foot.Y, target.GlobalPosition.Y, "Shop foot Y");
    Near(sprite.Scale.Y * 330, 600 * 0.47f * 1.5f, "Shop body-local height");
    NndxVideoOverlay.ForceStop();
    Check(target.Children.OfType<MegaCrit.Sts2.Core.Bindings.MegaSpine.TestSpineSprite>().Single().Visible,
        "Merchant body must be restored");
    target.Children.OfType<MegaCrit.Sts2.Core.Bindings.MegaSpine.TestSpineSprite>().Single().SkeletonReady = false;
    NndxVideoOverlay.StartForPlayer(2002, false);
    Check(!LiveSprites().Any() && LiveAudio().Count() == 1,
        "Unready merchant skeleton must fall back safely without hiding its body");
});
Run("Fake Merchant maps reversed visual children to local-first players", () =>
{
    SetupRoom(MegaCrit.Sts2.Core.Rooms.RoomType.Event);
    AddRemoteWithoutBody(2002);
    var fake = new MegaCrit.Sts2.Core.Nodes.Events.Custom.NFakeMerchant();
    var container = new Control(); fake.AddChild(container);
    var remote = CreateVisuals(278, 0.5f);
    var local = CreateVisuals(335, 2f);
    container.AddChild(remote); container.AddChild(local);
    MegaCrit.Sts2.Core.Nodes.NRun.Instance!.EventRoom = new() { CustomEventNode = fake };
    NndxVideoOverlay.StartForPlayer(2002, false);
    Check(!remote.Body.Visible && local.Body.Visible, "Fake Merchant must replace only the requested player's visual");
    var sprite = remote.Children.OfType<AnimatedSprite2D>().Single();
    var foot = sprite.ToGlobal(sprite.Offset + new Vector2(135, 315));
    Near(foot.Y, remote.GlobalPosition.Y, "Fake merchant foot Y");
    Near(sprite.Scale.Y * 330, 417, "Fake merchant body-local height");
    NndxVideoOverlay.ForceStop();
    Check(remote.Body.Visible, "Fake Merchant body must restore");
});
Run("Room, combat and run boundaries immediately clear playback and prevent reward retriggers", () =>
{
    foreach (Type hook in new[] { typeof(NndxRoomDeparturePatch), typeof(NndxCombatSetupPatch), typeof(NndxTravelAndExitPatch) })
    {
        var creature = Setup(278, 1);
        var panel = AddPortrait(1001); var original = panel.Icon.Texture;
        NndxVideoOverlay.StartForPlayer(1001, false);
        InvokePrefix(hook);
        Check(creature.Visuals.Body.Visible && !LiveAudio().Any() && !LiveSprites().Any()
            && ReferenceEquals(panel.Icon.Texture, original), "Boundary prefix must restore every component immediately");
        NndxVideoOverlay.StartForPlayer(1001, false);
        Check(!LiveAudio().Any(), "Old room must stay blocked while asynchronous transition is pending");
        CombatManager.Instance.State.RunState.CurrentRoom = new() { Id = 43 };
        CombatManager.Instance.IsInProgress = false; NCombatRoom.Instance = null;
        NndxVideoOverlay.StartForPlayer(1001, false);
        Check(LiveAudio().Any(), "Next room must be playable");
    }
    Setup(278, 1);
    NndxRoomContext.BeginTransition();
    NndxRoomContext.CombatStarting();
    NndxVideoOverlay.StartForPlayer(1001, false);
    Check(LiveSprites().Any(), "New combat must release its setup barrier");
    NndxVideoOverlay.ForceStop();
    CombatManager.Instance.IsInProgress = false;
    NndxVideoOverlay.StartForPlayer(1001, false);
    Check(!LiveAudio().Any(), "Finished combat must not allow audio-only fallback behind rewards");
    CombatManager.Instance.IsInProgress = true; CombatManager.Instance.IsEnding = true;
    NndxVideoOverlay.StartForPlayer(1001, false);
    Check(!LiveAudio().Any(), "Ending combat must reject new playback");
    SetupRoom(MegaCrit.Sts2.Core.Rooms.RoomType.Event);
    var eventPanel = AddPortrait(1001); var eventOriginal = eventPanel.Icon.Texture;
    NndxVideoOverlay.StartForPlayer(1001, false);
    MegaCrit.Sts2.Core.Nodes.NRun.Instance!.Free();
    Check(!LiveAudio().Any() && ReferenceEquals(eventPanel.Icon.Texture, eventOriginal),
        "Run tree exit must stop bodyless sessions too");
});
Run("Victory rewards and map both allow fresh playback while preserving battle-end and room-exit cleanup", () =>
{
    var creature = Setup(278, 1);
    var panel = AddPortrait(1001); var original = panel.Icon.Texture;
    NndxVideoOverlay.StartForPlayer(1001, false, locallyControlled: true);
    InvokePrefix(typeof(NndxCombatBoundaryPatch));
    Check(!LiveAudio().Any() && creature.Visuals.Body.Visible, "Combat end must stop the old session");
    CombatManager.Instance.IsInProgress = false;
    CombatManager.Instance.State.RunState.CurrentRoom.IsPreFinished = true;
    MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance = new() { ScreenCount = 1 };
    Time.Now += 1000; NndxVideoOverlay.Toggle();
    Check(LiveAudio().Count() == 1 && Sprite(creature).Visible && !creature.Visuals.Body.Visible
        && !ReferenceEquals(panel.Icon.Texture, original),
        "Reward view with visible portrait and body must start all effects");
    MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance = new() { IsOpen = true };
    MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance.Update();
    Check(!Sprite(creature).Visible && LiveAudio().Any(), "Opening map must only hide the body animation");
    Time.Now += 150; NndxVideoOverlay.Toggle();
    Check(!LiveAudio().Any() && ReferenceEquals(panel.Icon.Texture, original), "Map key must stop playback");
    Time.Now += 150; NndxVideoOverlay.Toggle();
    Check(LiveAudio().Any() && !Sprite(creature).Visible, "Map key must start playback with animation hidden");
    MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance.IsOpen = false;
    MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance.Update();
    Check(Sprite(creature).Visible, "Returning to rewards with a visible body must show the ongoing animation");
    InvokePrefix(typeof(NndxTravelAndExitPatch));
    Check(!LiveAudio().Any() && ReferenceEquals(panel.Icon.Texture, original), "Map travel must stop all effects");
    Time.Now += 150; NndxVideoOverlay.Toggle();
    Check(!LiveAudio().Any(), "Visible portrait must not bypass a genuine departure barrier");
    MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance = null;
    MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance = null;
});
Run("Local multiplayer input follows portrait visibility; solo and remote playback are independent", () =>
{
    SetupRoom(MegaCrit.Sts2.Core.Rooms.RoomType.Event);
    Time.Now += 1000; NndxVideoOverlay.Toggle();
    Check(!LiveAudio().Any(), "Without the multiplayer portrait local key must not trigger");
    var panel = AddPortrait(1001); var original = panel.Icon.Texture;
    panel.Icon.Visible = false;
    NndxVideoOverlay.Toggle();
    Check(!LiveAudio().Any(), "A hidden multiplayer portrait must not allow a new local start");
    panel.Icon.Visible = true;
    MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance = new() { InUse = true };
    NndxVideoOverlay.Toggle();
    Check(LiveAudio().Count() == 1 && !LiveSprites().Any() && !ReferenceEquals(panel.Icon.Texture, original),
        "Bodyless screen with visible portrait must still play audio and change portrait");
    Time.Now += 150; NndxVideoOverlay.Toggle();
    Check(!LiveAudio().Any() && ReferenceEquals(panel.Icon.Texture, original), "Repeated key must restore bodyless playback");
    CombatManager.Instance.State.GetPlayer(1001)!.Creature.IsDead = true;
    Time.Now += 150; NndxVideoOverlay.Toggle();
    Check(LiveAudio().Count() == 1 && !LiveSprites().Any(),
        "A spectating player with a visible portrait must still be able to laugh without a body");
    MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance = null;
    SetupRoom(MegaCrit.Sts2.Core.Rooms.RoomType.Event);
    RunManager.Instance!.NetService.Type = NetGameType.Singleplayer;
    NndxVideoOverlay.Toggle();
    Check(LiveAudio().Count() == 1, "Solo playback must remain available without multiplayer UI");
});
Run("Post-victory network playback does not depend on whether the receiver has opened its map", () =>
{
    Setup(278, 1);
    var remote = AddRemote(2002); var panel = AddPortrait(2002); var original = panel.Icon.Texture;
    CombatManager.Instance.IsInProgress = false;
    CombatManager.Instance.State.RunState.CurrentRoom.IsPreFinished = true;
    RunManager.Instance!.NetService.Type = NetGameType.Host;
    RitsuLibSidecarTypedMessageRegistry.Sent.Clear();
    DeliverRoom(Message(2002, true, 10, 2));
    Check(ReferenceEquals(panel.Icon.Texture, Face(2)) && LiveAudio().Count() == 1 && Sprite(remote).Visible
        && !remote.Visuals.Body.Visible && RitsuLibSidecarTypedMessageRegistry.Sent.Count == 1,
        "Host with map closed must still apply and relay the remote map player's audio and portrait");
    DeliverRoom(Message(2002, false, 11));
    Check(!LiveAudio().Any() && ReferenceEquals(panel.Icon.Texture, original), "Remote stop must restore even while receiver's map is closed");
    CombatManager.Instance.State.RunState.CurrentRoom.IsPreFinished = false;
    MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance = new() { IsOpen = true };
    DeliverRoom(Message(2002, true, 12, 1));
    NndxVideoOverlay.Toggle();
    Check(!LiveAudio().Any(), "Defeat or unfinished combat cleanup must remain blocked even with a map open");
    MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance = null;
});
Run("Opening screens preserves audio, hides animation and restores it when the screen closes", () =>
{
    var creature = Setup(278, 1);
    NndxVideoOverlay.StartForPlayer(1001, false);
    var sprite = Sprite(creature);
    MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance = new() { IsOpen = true };
    MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance.Update();
    Check(!sprite.Visible && LiveAudio().Any(), "Opening map must hide animation without ending playback");
    MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance = null;
    MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance.Update();
    Check(sprite.Visible && LiveAudio().Any(), "Closing map must restore playback visibility");
    InvokePrefix(typeof(NndxRoomScenePatch));
    Check(creature.Visuals.Body.Visible && !LiveAudio().Any(), "Replacing the actual room scene must clear playback");
});
Run("Multiplayer bodyless playback relays one selected face and rejects reused room IDs at other map locations", () =>
{
    SetupRoom(MegaCrit.Sts2.Core.Rooms.RoomType.Event);
    AddRemoteWithoutBody(2002);
    var panel = AddPortrait(2002); var original = panel.Icon.Texture;
    RunManager.Instance!.NetService.Type = NetGameType.Host;
    RitsuLibSidecarTypedMessageRegistry.Sent.Clear();
    DeliverRoom(Message(2002, true, 10, 3));
    Check(ReferenceEquals(panel.Icon.Texture, Face(3)) && LiveAudio().Count() == 1 && !LiveSprites().Any(),
        "Host must display and relay bodyless playback");
    Check(RitsuLibSidecarTypedMessageRegistry.Sent.Count == 1, "Host must relay one accepted start");
    NndxRoomContext.BeginTransition();
    CombatManager.Instance.State.RunState.CurrentRoom = new() { Id = 42 };
    CombatManager.Instance.State.RunState.CurrentMapCoord = new(4, 8);
    DeliverRoom(Message(2002, true, 20, 1));
    Check(!LiveAudio().Any() && ReferenceEquals(panel.Icon.Texture, original), "Same room ID on another map tile must reject old start");
    DeliverRoom(new() { PlayerNetId = 2002, IsPlaying = true, FaceIndex = 2, Sequence = 30,
        RunSeed = 123, RoomId = 42, MapCol = 4, MapRow = 8 });
    Check(ReferenceEquals(panel.Icon.Texture, Face(2)), "Current tile message must still play");
    InvokePrefix(typeof(NndxPlayerDisconnectedPatch), 2002UL);
    Check(!LiveAudio().Any() && ReferenceEquals(panel.Icon.Texture, original), "Disconnect must stop that player");
    NndxRoomContext.BeginTransition();
    CombatManager.Instance.State.RunState.CurrentRoom = new() { Id = 42 };
    CombatManager.Instance.State.RunState.CurrentActIndex = 1;
    DeliverRoom(new() { PlayerNetId = 2002, IsPlaying = true, FaceIndex = 0, Sequence = 40,
        RunSeed = 123, RoomId = 42, MapCol = 4, MapRow = 8 });
    Check(!LiveAudio().Any(), "Same coordinates in another act must reject old start");
});
Run("Rapid bodyless toggles stay bounded and audio startup errors release their session", () =>
{
    SetupRoom(MegaCrit.Sts2.Core.Rooms.RoomType.Event);
    var panel = AddPortrait(1001); var original = panel.Icon.Texture;
    AudioStreamPlayer.FailNextPlay = true;
    NndxVideoOverlay.StartForPlayer(1001, false);
    Check(!LiveAudio().Any() && ReferenceEquals(panel.Icon.Texture, original), "Failed audio startup must roll back");
    for (int i = 0; i < 120; i++)
    {
        Time.Now += 150; NndxVideoOverlay.Toggle();
        for (int j = 0; j < 20; j++) NndxVideoOverlay.Toggle();
        Check(LiveAudio().Count() <= 1 && !LiveSprites().Any(), "Repeated input must not accumulate playback");
        Node.FlushDeletion();
    }
    Check(!LiveAudio().Any() && ReferenceEquals(panel.Icon.Texture, original), "Final toggle must restore portrait");
});
Run("Osty uses a square canvas at its own baseline and inherits HP scaling exactly once", () =>
{
    foreach (var size in new[] { new Vector2(232, 204), new Vector2(360, 150), new Vector2(150, 330) })
    foreach (float scale in new[] { 0.2f, 0.5f, 1f, 1.35f, 2f, 3f })
    {
        var owner = Setup(278, 1);
        var pet = AddOsty(1001);
        pet.Visuals.Bounds.Size = size;
        pet.Visuals.Scale = Vector2.One * scale;
        NndxVideoOverlay.StartForPlayer(1001, false);
        var sprite = Sprite(pet);
        AssertPetGeometry(pet, sprite, Math.Min(size.X, size.Y));
        Check(!pet.Visuals.Body.Visible && !owner.Visuals.Body.Visible, "Both native bodies must be hidden");
        Check(ReferenceEquals(sprite.SpriteFrames, Sprite(owner).SpriteFrames), "Owner and pet must reuse the same cached frames");
        Check(LiveAudio().Count() == 1, "Pet must not add another laugh audio player");
    }
});
Run("Osty stops and restores with manual stop, natural end, timeout, room exit and owner death", () =>
{
    foreach (string end in new[] { "manual", "natural", "timeout", "room", "owner-death" })
    {
        var owner = Setup(278, 1); var pet = AddOsty(1001);
        var panel = AddPortrait(1001); var original = panel.Icon.Texture;
        NndxVideoOverlay.StartForPlayer(1001, false, true);
        switch (end)
        {
            case "manual": NndxVideoOverlay.StopForPlayer(1001, false); break;
            case "natural": Sprite(owner).Finish(); break;
            case "timeout": Node.Tree.Timers.Last().Expire(); break;
            case "room": NndxRoomContext.BeginTransition(); break;
            case "owner-death": owner.Entity.IsDead = true; NndxVideoOverlay.HandleCreatureDeath(owner.Entity); break;
        }
        Node.FlushDeletion();
        Check(owner.Visuals.Body.Visible && pet.Visuals.Body.Visible && !LiveSprites().Any(), $"{end} must restore both bodies");
        Check(ReferenceEquals(panel.Icon.Texture, original), $"{end} must restore the owner's portrait");
        Check(!LiveAudio().Any(a => ReferenceEquals(a.Stream, ResourceLoader.Resources["res://res/nndx.mp3"])), "Laugh audio must stop with its session");
        foreach (var sound in LiveAudio().ToArray()) sound.Finish();
    }
});
Run("Osty death plays one death sound without stopping its owner; revival joins the existing frame", () =>
{
    var owner = Setup(278, 1); var pet = AddOsty(1001);
    var panel = AddPortrait(1001);
    NndxVideoOverlay.StartForPlayer(1001, false, true, 2);
    var main = Sprite(owner); main.Frame = 137; main.FrameProgress = 0.375f;
    var oldPet = Sprite(pet); var stale = oldPet.CaptureFinished();
    NndxVideoOverlay.HandleCreatureDeath(pet.Entity);
    Check(!pet.Visuals.Body.Visible && DeathAudio().Count() == 0, "A prevented death must not stop a live pet or play its death sound");
    pet.Entity.IsDead = true;
    NndxVideoOverlay.HandleCreatureDeath(pet.Entity);
    NndxVideoOverlay.HandleCreatureDeath(pet.Entity);
    Check(pet.Visuals.Body.Visible && oldPet.IsQueuedForDeletion() && DeathAudio().Count() == 1,
        "Dying Osty must restore its own body and play exactly one death sound");
    Check(main.IsPlaying() && !main.IsQueuedForDeletion() && !owner.Visuals.Body.Visible
        && ReferenceEquals(panel.Icon.Texture, Face(2)), "Pet death must not stop owner, laughter or portrait");
    pet.Entity.IsDead = false;
    NndxVideoOverlay.RefreshPets(1001);
    var revived = Sprite(pet);
    Check(revived.Frame == 137 && revived.FrameProgress == 0.375f, "Revival must continue the owner's animation phase");
    stale();
    Check(!revived.IsQueuedForDeletion() && !pet.Visuals.Body.Visible, "Old pet completion must not affect revival");
    foreach (var audio in DeathAudio().ToArray()) audio.Finish();
    Check(DeathAudio().Count() == 0, "Death sound must free itself after completion");
});
Run("Growth and repeated summons preserve the Osty sprite and never add transport or audio", () =>
{
    Setup(278, 1); var pet = AddOsty(1001);
    RitsuLibSidecarTypedMessageRegistry.Sent.Clear();
    NndxVideoOverlay.StartForPlayer(1001, true);
    var sprite = Sprite(pet);
    foreach (float scale in new[] { 1f, 1.5f, 2f, 0.7f })
    {
        pet.Visuals.Scale = Vector2.One * scale;
        pet.Position += new Vector2(25, -10);
        NndxVideoOverlay.RefreshPets(1001);
        Check(ReferenceEquals(sprite, Sprite(pet)), "A living pet's summon/HP increase must not recreate the overlay");
        AssertPetGeometry(pet, sprite, 204);
    }
    Check(RitsuLibSidecarTypedMessageRegistry.Sent.Count == 1 && LiveAudio().Count() == 1,
        "Pet growth must not send extra packets or play duplicate laugh audio");
});
Run("Late summons join either the owner sprite or an audio-only session without spawning missing pets", () =>
{
    var owner = Setup(278, 1);
    NndxVideoOverlay.StartForPlayer(1001, false);
    Check(LiveSprites().Count() == 1, "No Osty must be invented before the game summons it");
    Sprite(owner).Frame = 88; Sprite(owner).FrameProgress = 0.25f;
    var pet = AddOsty(1001);
    NndxVideoOverlay.RefreshPets(1001);
    Check(Sprite(pet).Frame == 88 && Sprite(pet).FrameProgress == 0.25f, "New pet must join the sprite's current phase");
    Setup(278, 1).Visuals.Visible = false;
    NndxVideoOverlay.StartForPlayer(1001, false);
    LiveAudio().Single().PlaybackPosition = 3.125;
    pet = AddOsty(1001);
    NndxVideoOverlay.RefreshPets(1001);
    Check(Sprite(pet).Frame == 75 && LiveSprites().Count() == 1 && LiveAudio().Count() == 1,
        "Audio-only owner must still drive a subsequently-visible pet with the same playback phase");
});
Run("Host and client messages animate only the sender's Osty and honor local map/deck visibility", () =>
{
    Setup(278, 1); var localPet = AddOsty(1001);
    var remote = AddRemote(2002); var remotePet = AddOsty(2002);
    RunManager.Instance!.NetService.Type = NetGameType.Host;
    RitsuLibSidecarTypedMessageRegistry.Sent.Clear();
    DeliverRoom(Message(2002, true, 100, 3));
    var sprite = Sprite(remotePet);
    Check(localPet.Visuals.Body.Visible && !remotePet.Visuals.Body.Visible && !remote.Visuals.Body.Visible,
        "Remote start must replace the pet identified by its owner, never a local pet");
    DeliverRoom(Message(2002, true, 100, 1));
    Check(ReferenceEquals(sprite, Sprite(remotePet)) && RitsuLibSidecarTypedMessageRegistry.Sent.Count == 1,
        "Duplicate starts must not recreate or re-broadcast pet playback");
    MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance = new() { IsOpen = true };
    MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance.Update();
    Check(!sprite.Visible && !Sprite(remote).Visible && LiveAudio().Count() == 1, "Map must hide both sprites but preserve one laugh");
    MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance = null;
    MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance = new() { InUse = true };
    MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance.Update();
    Check(!sprite.Visible, "Deck must hide pet animation too");
    MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance = null;
    MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance.Update();
    Check(sprite.Visible && Sprite(remote).Visible, "Closing deck must restore both sprites");
    DeliverRoom(Message(2002, false, 101));
    Check(remotePet.Visuals.Body.Visible && remote.Visuals.Body.Visible && !LiveAudio().Any(), "Remote stop must restore the same owner's full group");
    RunManager.Instance.NetService.Type = NetGameType.Client;
    DeliverRoom(Message(2002, true, 102, 1));
    Check(!remotePet.Visuals.Body.Visible && localPet.Visuals.Body.Visible, "Host relay on a client must target the same pet owner");
});
Run("Osty removal and partial attachment/cleanup failures leave the owner playable", () =>
{
    var owner = Setup(278, 1);
    NndxVideoOverlay.StartForPlayer(1001, false);
    var pet = AddOsty(1001);
    AnimatedSprite2D.FailNextPlay = true;
    NndxVideoOverlay.RefreshPets(1001);
    Check(pet.Visuals.Body.Visible && Sprite(owner).IsPlaying(), "Failed pet start must roll back only the pet");
    Node.FlushDeletion();
    NndxVideoOverlay.RefreshPets(1001);
    Check(!pet.Visuals.Body.Visible, "A failed pet attachment must be retryable");
    pet.Free();
    NCombatRoom.Instance!.CreatureNodes.Remove(pet);
    Check(Sprite(owner).IsPlaying() && DeathAudio().Count() == 0, "Tree removal must not stop the owner or masquerade as a pet death");
    pet = AddOsty(1001);
    NndxVideoOverlay.RefreshPets(1001);
    AnimatedSprite2D.FailNextStop = true;
    NndxVideoOverlay.StopForPlayer(1001, false);
    Node.FlushDeletion();
    Check(pet.Visuals.Body.Visible && owner.Visuals.Body.Visible && !LiveSprites().Any(), "Pet/native cleanup failure must not leave bodies hidden or sprites leaked");
});
Run("Rapid pet toggles and stale callbacks remain bounded", () =>
{
    Setup(278, 1); var pet = AddOsty(1001); AddPortrait(1001);
    Action? stale = null;
    for (int i = 0; i < 100; i++)
    {
        Time.Now += 150;
        NndxVideoOverlay.Toggle();
        if (i % 2 == 0)
        {
            stale?.Invoke();
            Check(!pet.Visuals.Body.Visible && LiveSprites().Count() == 2 && LiveAudio().Count() == 1,
                "Each start must own exactly two sprites and one sound");
            stale = Sprite(pet).CaptureFinished();
        }
        else Check(pet.Visuals.Body.Visible && !LiveSprites().Any() && !LiveAudio().Any(), "Each stop must release the group");
        Node.FlushDeletion();
    }
});
Run("Generic pets preserve aspect ratio, ownership and independent death alongside Osty", () =>
{
    foreach (float scale in new[] { 0.2f, 1f, 2f, 3f })
    {
        var owner = Setup(278, 1); var osty = AddOsty(1001);
        var first = AddOsty(1001); first.Entity.Monster = new object();
        var second = AddOsty(1001); second.Entity.Monster = new object();
        first.Visuals.Scale = Vector2.One * scale;
        first.Visuals.Bounds.Size = new(180, 300);
        second.Visuals.Bounds.Size = new(400, 150);
        var otherOwner = AddRemote(2002); var otherPet = AddOsty(2002); otherPet.Entity.Monster = new object();
        NndxVideoOverlay.StartForPlayer(1001, true);
        Check(LiveSprites().Count() == 4 && LiveAudio().Count() == 1 && otherPet.Visuals.Body.Visible,
            "All and only the owner's pets must share one laugh");
        AssertPetGeometry(osty, Sprite(osty), 204);
        AssertPetGeometry(first, Sprite(first), 180f * 330 / 270, 180);
        AssertPetGeometry(second, Sprite(second), 150, 150f * 270 / 330);
        first.Entity.IsDead = true;
        NndxVideoOverlay.HandleCreatureDeath(first.Entity);
        NndxVideoOverlay.HandleCreatureDeath(first.Entity);
        Check(DeathAudio().Count() == 1 && first.Visuals.Body.Visible && !second.Visuals.Body.Visible
            && !osty.Visuals.Body.Visible && !owner.Visuals.Body.Visible, "One death must not stop siblings or owner");
        NndxVideoOverlay.StopForPlayer(1001, false);
        Check(second.Visuals.Body.Visible && osty.Visuals.Body.Visible && !LiveSprites().Any(), "Stop restores every pet");
    }
});
Run("Generic late arrivals and revivals join playback; unknown layouts do not block siblings", () =>
{
    var owner = Setup(278, 1);
    NndxVideoOverlay.StartForPlayer(1001, false);
    Sprite(owner).Frame = 90;
    var bad = AddOsty(1001); bad.Entity.Monster = new object(); bad.Visuals.Bounds.Size = Vector2.Zero;
    var hidden = AddOsty(1001); hidden.Entity.Monster = new object(); hidden.Visuals.Body.Visible = false;
    var pet = AddOsty(1001); pet.Entity.Monster = new object();
    NndxVideoOverlay.RefreshPet(pet.Entity);
    Check(Sprite(pet).Frame == 90 && bad.Visuals.Body.Visible && !hidden.Visuals.Body.Visible
        && LiveSprites().Count() == 2, "Unsupported or hidden bodies must be left alone");
    pet.Entity.IsDead = true;
    NndxVideoOverlay.HandleCreatureDeath(pet.Entity);
    pet.Entity.IsDead = false;
    Sprite(owner).Frame = 150;
    NndxVideoOverlay.RefreshPet(pet.Entity);
    Check(Sprite(pet).Frame == 150, "Generic revival must join owner progress");
    var extra = AddOsty(1001); extra.Entity.Monster = new object();
    AnimatedSprite2D.FailNextPlay = true;
    NndxVideoOverlay.RefreshPet(extra.Entity);
    Check(extra.Visuals.Body.Visible && Sprite(pet).IsPlaying(), "One failing pet must not break existing pets");
});
Run("Pet death before its lifecycle callback produces one sound; ordinary removal is silent", () =>
{
    var owner = Setup(278, 1); var pet = AddOsty(1001); pet.Entity.Monster = new object();
    NndxVideoOverlay.StartForPlayer(1001, false);
    pet.Entity.IsDead = true;
    pet.Free();
    NCombatRoom.Instance!.CreatureNodes.Remove(pet);
    NndxVideoOverlay.HandleCreatureDeath(pet.Entity);
    Check(DeathAudio().Count() == 1 && Sprite(owner).IsPlaying(), "Early removal must preserve one death sound");
});
Run("Pet setting persists, restores all active pets immediately and leaves owner playback intact", () =>
{
    var binding = (STS2RitsuLib.Settings.IModSettingsValueBinding<bool>)
        STS2RitsuLib.Settings.ModSettingsSectionBuilder.Bindings["pets_laugh"];
    var owner = Setup(278, 1); var osty = AddOsty(1001); var pet = AddOsty(1001); pet.Entity.Monster = new object();
    pet.ZIndex = 6; pet.ZAsRelative = false;
    var remote = AddRemote(2002); var remotePet = AddOsty(2002);
    NndxVideoOverlay.StartForPlayer(1001, false);
    NndxVideoOverlay.StartForPlayer(2002, false);
    var old = Sprite(pet).CaptureFinished();
    binding.Write(false);
    Check(pet.ZIndex == 6 && !pet.ZAsRelative, "Disabling must restore a custom pet's original draw order");
    Check(!binding.Read() && !NndxInput.PetsLaughEnabled && osty.Visuals.Body.Visible && pet.Visuals.Body.Visible
        && remotePet.Visuals.Body.Visible && LiveSprites().Count() == 2 && LiveAudio().Count() == 2,
        "Disabling must immediately restore all local pet visuals while retaining both owners");
    var extra = AddOsty(1001); extra.Entity.Monster = new object();
    NndxVideoOverlay.RefreshPet(extra.Entity);
    extra.Entity.IsDead = true; NndxVideoOverlay.HandleCreatureDeath(extra.Entity);
    Check(extra.Visuals.Body.Visible && DeathAudio().Count() == 0, "Disabled pets must not animate or play death sounds");
    Sprite(owner).Frame = 123;
    binding.Write(true); old();
    Check(!osty.Visuals.Body.Visible && !pet.Visuals.Body.Visible && !remotePet.Visuals.Body.Visible
        && Sprite(pet).Frame == 123, "Re-enabling joins active owners; stale callbacks cannot restore new bodies");
    NndxVideoOverlay.ForceStop();
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
    MegaCrit.Sts2.Core.Nodes.NRun.Instance = new();
    MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance = null;
    MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance = null;
    MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance = null;
    CombatManager.Instance.State = new();
    CombatManager.Instance.IsInProgress = true;
    CombatManager.Instance.IsEnding = false;
    CombatManager.Instance.IsStarting = false;
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
        Sequence = sequence, RunSeed = 123, RoomId = 42, MapCol = 3, MapRow = 7 };

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

static NCreature SetupRoom(MegaCrit.Sts2.Core.Rooms.RoomType kind)
{
    var creature = Setup(278, 1);
    NCombatRoom.Instance = null;
    CombatManager.Instance.IsInProgress = false;
    CombatManager.Instance.State.RunState.CurrentRoom = new() { RoomType = kind };
    return creature;
}
static void AddRemoteWithoutBody(ulong id)
{
    var entity = new Creature();
    var player = new Player { NetId = id, Creature = entity }; entity.Player = player;
    CombatManager.Instance.State.Players.Add(player);
}
static NCreatureVisuals CreateVisuals(float height, float scale)
{
    var visuals = new NCreatureVisuals { Scale = Vector2.One * scale };
    visuals.AddChild(visuals.Body); visuals.AddChild(visuals.Bounds);
    visuals.Bounds.Size = new(242, height);
    return visuals;
}
static IEnumerable<AnimatedSprite2D> LiveSprites() => Node.All.OfType<AnimatedSprite2D>().Where(n => !n.Freed && !n.IsQueuedForDeletion());
static IEnumerable<AudioStreamPlayer> LiveAudio() => Node.All.OfType<AudioStreamPlayer>().Where(n => !n.Freed && !n.IsQueuedForDeletion());
static void DeliverRoom(NndxOverlaySyncMessage message)
{
    RitsuLibSidecarTypedMessageRegistry.Deliver(message, message.PlayerNetId); Callable.Flush();
}
static void InvokePrefix(Type type, params object[] args) => type.GetMethod("Prefix",
    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, args);

static NCreature AddOsty(ulong ownerId)
{
    var owner = CombatManager.Instance.State.GetPlayer(ownerId)!;
    var pet = new NCreature { Position = owner.Creature.Node.Position + new Vector2(150, -75) };
    pet.AddChild(pet.Visuals); pet.Visuals.AddChild(pet.Visuals.Body); pet.Visuals.AddChild(pet.Visuals.Bounds);
    pet.Visuals.Bounds.Size = new(232, 204); pet.Visuals.Bounds.Position = new(-116, -204);
    pet.Entity = new Creature { IsPlayer = false, Monster = new MegaCrit.Sts2.Core.Models.Monsters.Osty(), PetOwner = owner, Node = pet };
    NCombatRoom.Instance!.CreatureNodes.Add(pet); NCombatRoom.Instance.AddChild(pet);
    return pet;
}
static IEnumerable<AudioStreamPlayer> DeathAudio() => LiveAudio().Where(a => ReferenceEquals(a.Stream, ResourceLoader.Resources["res://res/dead.mp3"]));
void AssertPetGeometry(NCreature pet, AnimatedSprite2D sprite, float side, float? width = null)
{
    var foot = sprite.ToGlobal(sprite.Offset + new Vector2(135, 315));
    Near(foot.X, pet.Visuals.GlobalPosition.X, "Osty foot X"); Near(foot.Y, pet.Visuals.GlobalPosition.Y, "Osty foot Y");
    var top = sprite.ToGlobal(sprite.Offset);
    var right = sprite.ToGlobal(sprite.Offset + new Vector2(270, 0));
    var bottom = sprite.ToGlobal(sprite.Offset + new Vector2(0, 330));
    Near(right.X - top.X, (width ?? side) * pet.Visuals.GlobalScale.X, "Pet displayed width");
    Near(bottom.Y - top.Y, side * pet.Visuals.GlobalScale.Y, "Osty displayed height");
    nativeCases.Add(new
    {
        parent_scale = new[] { pet.Scale.X, pet.Scale.Y },
        body_scale = new[] { pet.Visuals.Scale.X, pet.Visuals.Scale.Y },
        sprite_scale = new[] { sprite.Scale.X, sprite.Scale.Y },
        sprite_position = new[] { sprite.Position.X, sprite.Position.Y },
        sprite_offset = new[] { sprite.Offset.X, sprite.Offset.Y },
        expected_height = side * pet.Visuals.GlobalScale.Y,
        expected_width = (width ?? side) * pet.Visuals.GlobalScale.X,
    });
}
