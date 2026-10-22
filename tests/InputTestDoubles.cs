// Input/settings native-boundary doubles; tests execute the production controller.
namespace Godot
{
    public enum Key { None, C, J, K, Enter, Escape, Key1, Key0, Ctrl, Alt, Shift, Meta, Backspace, Delete }
    public enum JoyButton { Invalid = -1, A, B, Paddle1 = 16, Max = 128 }
    public class InputEvent { public bool IsEcho() => false; }
    public class InputEventAction : InputEvent
    {
        public string Action = "";
        public bool Pressed;
        public bool IsActionPressed(string action) => Pressed && Action == action;
    }
    public class InputEventKey : InputEvent
    {
        public Key Keycode, PhysicalKeycode;
        public bool Pressed;
        public bool CtrlPressed, AltPressed, ShiftPressed, MetaPressed;
    }
    public class InputEventJoypadButton : InputEvent { public bool Pressed; public JoyButton ButtonIndex; public int Device; }
    public enum HorizontalAlignment { Right }
    public partial class Control
    {
        public enum FocusModeEnum { None, All }
        public FocusModeEnum FocusMode;
        public enum SizeFlags { ExpandFill }
        public enum MouseFilterEnum { Ignore }
        public SizeFlags SizeFlagsHorizontal;
        public MouseFilterEnum MouseFilter;
        public Vector2 CustomMinimumSize;
        public bool IsVisibleInTree() => Visible;
        public bool HasFocus() => Node.Tree.Root.Focus == this;
        public void GrabFocus() => Node.Tree.Root.Focus = this;
        public void AddThemeFontOverride(string name, object font) { }
        public void AddThemeFontSizeOverride(string name, int size) { }
        public void AddThemeColorOverride(string name, object color) { }
    }
    public class VBoxContainer : Control { }
    public class HBoxContainer : Control { }
    public class Label : Control { public string Text = ""; public HorizontalAlignment HorizontalAlignment; }
    public static class Input
    {
        public static HashSet<(int, JoyButton)> Held = new();
        public static IEnumerable<int> GetConnectedJoypads() => Held.Select(x => x.Item1).Distinct();
        public static bool IsJoyButtonPressed(int device, JoyButton button) => Held.Contains((device, button));
    }
    public class LineEdit : Control { }
    public class TextEdit : Control { }
    public partial class Node
    {
        public string Name { get; set; } = "";
        private readonly Dictionary<StringName, Callable> _signals = new();
        public virtual void _Ready() { }
        public virtual void _Process(double delta) { }
        public virtual void _UnhandledInput(InputEvent evt) { }
        public virtual void _Input(InputEvent evt) { }
        public Node GetViewport() => Tree.Root;
        public bool InputHandled;
        public void SetInputAsHandled() => InputHandled = true;
        public virtual void _ExitTree() { }
        public bool IsNodeReady() => true;
        public Control? Focus;
        public Control? GuiGetFocusOwner() => Focus;
        public void Connect(StringName signal, Callable callback) => _signals.Add(signal, callback);
        public void Disconnect(StringName signal, Callable callback) => _signals.Remove(signal);
        public void Emit(StringName signal) { if (_signals.TryGetValue(signal, out var callback)) callback.Call(); }
    }
    public partial class SceneTree { public Node Root { get; } = new(); }
    public static class Engine { public static object GetMainLoop() => Node.Tree; }
    public static class ProjectSettings
    {
        public static string UserPath = "";
        public static string GlobalizePath(string path) => Path.Combine(UserPath, "sts_nndx", "input.json");
    }
    public static class InputMap
    {
        public static Dictionary<StringName, List<InputEvent>> Actions = new();
        public static IEnumerable<StringName> GetActions() => Actions.Keys;
        public static bool HasAction(StringName action) => Actions.ContainsKey(action);
        public static IEnumerable<InputEvent> ActionGetEvents(StringName action)
            => Actions.TryGetValue(action, out var events) ? events : [];
    }
}
namespace MegaCrit.Sts2.Core.ControllerInput
{
    public static class MegaInput
    {
        // Deliberately omit the numeric key: production must include the remappable list too.
        public static string[] AllInputs => ["ui_accept", "ui_select"];
        public const string back = "mega_back";
        public const string cancel = "ui_cancel";
    }
}
namespace MegaCrit.Sts2.Core.Nodes
{
    public class NRun
    {
        public static NRun? Instance;
        public PortraitTestGlobalUi GlobalUi { get; } = new();
    }
    public class PortraitTestGlobalUi
    {
        public Godot.Control MultiplayerPlayerContainer { get; } = new();
    }

    public class NGame : Godot.Node
    {
        public static NGame? Instance;
        public static bool Focused = true;
        public static bool IsGameFocusedWindow() => Focused;
        public TransitionState Transition = new();
        public class TransitionState { public bool InTransition; }
    }
}
namespace MegaCrit.Sts2.Core.Nodes.CommonUi
{
    public class NInputManager : Godot.Node
    {
        public static NInputManager? Instance = new();
        public static Godot.StringName[] remappableKeyboardInputs = ["mega_select_card_1", "mega_release_card"];
        public readonly Dictionary<Godot.StringName, Godot.Key> Mapping = new();
        public Godot.Key GetShortcutKey(Godot.StringName action) => Mapping.GetValueOrDefault(action);
        public static class SignalName { public const string InputRebound = "InputRebound"; }
    }
    public class NControllerManager
    {
        public static NControllerManager? Instance = new();
        public bool ShouldAllowControllerRebinding = true;
    }
}
namespace MegaCrit.Sts2.Core.Nodes.Debug
{
    public class NDevConsole { public static NDevConsole? Instance = new(); public bool Visible; }
}
namespace STS2RitsuLib
{
    public record GameReadyEvent(MegaCrit.Sts2.Core.Nodes.NGame Game);
    public static class RitsuLibFramework
    {
        public static void SubscribeLifecycle<T>(Action<T> action) { }
        public static void RegisterModSettings(string id, Action<Settings.ModSettingsPageBuilder> configure)
            => configure(new());
    }
}
namespace STS2RitsuLib.Utils.Persistence { public enum SaveScope { Global } }
namespace STS2RitsuLib.Settings
{
    public class ModSettingsText
    {
        private Func<string> _text = () => "";
        public static ModSettingsText Literal(string text) => new() { _text = () => text };
        public static ModSettingsText Dynamic(Func<string> text) => new() { _text = text };
        public override string ToString() => _text();
    }
    public interface IModSettingsValueBinding<T> { T Read(); void Write(T value); }
    public class ModSettingsCallbackValueBinding<T>(string id, string key,
        Utils.Persistence.SaveScope scope, Func<T> read, Action<T> write, Action save) : IModSettingsValueBinding<T>
    {
        public T Read() => read();
        public void Write(T value) => write(value);
        public void Save() => save();
        public string Id => id + key + scope;
    }
    public class ModSettingsMiniButton(string text, Action callback) : Godot.Control
    {
        public string Text = text;
        public bool Disabled;
        public void Press() { if (!Disabled) callback(); }
    }
    public record ModSettingsChoiceOption<T>(T Value, ModSettingsText Label);
    public class ModSettingsPageBuilder
    {
        public ModSettingsPageBuilder WithTitle(ModSettingsText text) => this;
        public ModSettingsPageBuilder WithModDisplayName(ModSettingsText text) => this;
        public ModSettingsPageBuilder AddSection(string id, Action<ModSettingsSectionBuilder> configure)
        { configure(new()); return this; }
    }
    public class ModSettingsSectionBuilder
    {
        public static Dictionary<string, object> Bindings = new();
        public static Dictionary<string, Godot.Control> CustomControls = new();
        public ModSettingsSectionBuilder AddCustom(string id, ModSettingsText label, Func<object, Godot.Control> factory)
        { CustomControls[id] = factory(new()); return this; }
        public static Dictionary<string, ModSettingsText> Paragraphs = new();
        public ModSettingsSectionBuilder WithTitle(ModSettingsText text) => this;
        public ModSettingsSectionBuilder AddKeyBinding(string id, ModSettingsText label, IModSettingsValueBinding<string> binding,
            bool allowModifierCombos, bool allowModifierOnly, ModSettingsText description)
        { Bindings[id] = binding; return this; }
        public ModSettingsSectionBuilder AddButton(string id, ModSettingsText label, ModSettingsText text, Action callback) => this;
        public ModSettingsSectionBuilder AddChoice<T>(string id, ModSettingsText label, IModSettingsValueBinding<T> binding,
            IEnumerable<ModSettingsChoiceOption<T>> choices, ModSettingsText description)
        { Bindings[id] = binding; return this; }
        public ModSettingsSectionBuilder AddToggle(string id, ModSettingsText label, IModSettingsValueBinding<bool> binding,
            ModSettingsText? description = null)
        { Bindings[id] = binding; return this; }
        public ModSettingsSectionBuilder AddParagraph(string id, ModSettingsText text)
        { Paragraphs[id] = text; return this; }
    }
}
namespace STS2RitsuLib.Ui.Toast
{
    public static class RitsuToastService
    {
        public static List<string> Warnings = new();
        public static void ShowWarning(string text, string title) => Warnings.Add(text);
    }
}
namespace STS2RitsuLib.RuntimeInput
{
    public interface IRuntimeHotkeyHandle : IDisposable { }
    public class RuntimeHotkeyOptions
    {
        public string? Id, DisplayName, Category, Description;
        public bool ExposeToSteamInput;
    }
    public record RuntimeHotkeyRegistrationInfo(string? Id, string? DisplayName, IReadOnlyList<string> CurrentBindings);
    public static class RuntimeHotkeyService
    {
        public static readonly Dictionary<string, Handle> Active = new();
        public class Handle(string binding, Action callback, RuntimeHotkeyOptions options) : IRuntimeHotkeyHandle
        {
            public string Binding = binding;
            public RuntimeHotkeyOptions Options = options;
            public void Invoke() => callback();
            public void Dispose() => Active.Remove(Options.Id!);
        }
        public static IRuntimeHotkeyHandle Register(string binding, Action callback, RuntimeHotkeyOptions options)
        {
            var handle = new Handle(binding, callback, options);
            Active.Add(options.Id!, handle);
            return handle;
        }
        public static string ActionBinding(string action) => "action:" + action;
        public static bool TryNormalizeBinding(string value, out string normalized)
        {
            normalized = value;
            if (value.StartsWith("action:")) return true;
            return value.Split('+').All(x => Enum.TryParse<Godot.Key>(x, out _));
        }
        public static IEnumerable<RuntimeHotkeyRegistrationInfo> GetRegisteredHotkeys()
            => Active.Values.Select(x => new RuntimeHotkeyRegistrationInfo(x.Options.Id, x.Options.DisplayName, [x.Binding])).ToArray();
    }
}

namespace STS2RitsuLib.Ui.Shell.Theme
{
    public static class RitsuShellTheme
    {
        public static Theme Current = new();
        public class Theme { public Fonts Font = new(); public Metrics Metric = new(); public Colors Text = new(); }
        public class Fonts { public object Body = new(); }
        public class Metrics { public FontSizes FontSize = new(); public BindingMetrics Keybinding = new(); }
        public class FontSizes { public int ValueLabel = 20; }
        public class BindingMetrics { public int HintFontSize = 16; }
        public class Colors { public object LabelPrimary = new(); public object LabelSecondary = new(); }
    }
}

namespace MegaCrit.Sts2.Core.Nodes.Multiplayer
{
    public class NMultiplayerPlayerState : Godot.Control
    {
        public MegaCrit.Sts2.Core.Entities.Players.Player Player { get; set; } = null!;
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public class HarmonyPatch(Type type, string method) : Attribute
    {
        public string Target => type.Name + method;
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPostfix : Attribute { }
}
