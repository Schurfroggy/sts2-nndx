using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Debug;
using STS2RitsuLib;
using STS2RitsuLib.RuntimeInput;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Ui.Toast;

namespace Nndx.Scripts;

public partial class NndxInput : Node
{
    private const string KeyboardId = "sts_nndx.toggle_video";
    private static NndxInput? _instance;
    internal static bool PetsLaughEnabled => _instance?._preferences.PetsLaugh ?? true;
    private NndxInputPreferences _preferences = new();
    private string _path = "";
    private string? _loadWarning;
    private string? _keyboardConflict;
    private IRuntimeHotkeyHandle? _keyboard;
    private double _elapsed;
    private NInputManager? _inputManager;

    public static void Initialize()
    {
        if (_instance != null) return;
        var input = _instance = new NndxInput { Name = "NndxInput" };
        input._path = ProjectSettings.GlobalizePath("user://sts_nndx/input.json");
        try
        {
            input._preferences = NndxInputPreferences.Load(input._path);
        }
        catch (Exception ex)
        {
            // Do not overwrite unreadable settings with defaults on startup.
            input._loadWarning = "读取输入配置失败，暂用默认设置；原文件未覆盖。";
            Entry.Logger.Error($"NNDX input settings load failed: {ex}");
        }
        input.RegisterSettings();
        RitsuLibFramework.SubscribeLifecycle<GameReadyEvent>(e => input.Attach(e.Game));
        if (NGame.Instance is { } game && game.IsNodeReady()) input.Attach(game);
    }

    private void Attach(NGame game)
    {
        if (GetParent() != null) return;
        Callable.From(() =>
        {
            if (GetParent() == null) game.AddChild(this);
        }).CallDeferred();
    }

    public override void _Ready()
    {
        RefreshBindings();
        if (_loadWarning != null) Warn(_loadWarning);
    }

    public override void _Process(double delta)
    {
        // Also catches other mods registering or rebinding after this mod.
        _elapsed += delta;
        if (_elapsed < 1) return;
        _elapsed = 0;
        RefreshBindings();
    }

    private static void Toggle()
    {
        if (!NGame.IsGameFocusedWindow() || NGame.Instance?.Transition.InTransition != false
            || NDevConsole.Instance?.Visible == true
            || !NndxVideoOverlay.CanToggleInCurrentScreen()) return;
        if (Engine.GetMainLoop() is SceneTree tree
            && tree.Root.GuiGetFocusOwner() is LineEdit or TextEdit) return;
        NndxVideoOverlay.Toggle();
    }

    private void RefreshBindings()
    {
        if (_inputManager != NInputManager.Instance)
        {
            if (GodotObject.IsInstanceValid(_inputManager))
                _inputManager!.Disconnect(NInputManager.SignalName.InputRebound, Callable.From(OnInputRebound));
            _inputManager = NInputManager.Instance;
            _inputManager?.Connect(NInputManager.SignalName.InputRebound, Callable.From(OnInputRebound));
        }
        string? conflict = FindKeyboardConflict(_preferences.Keyboard);
        if (conflict != null || string.IsNullOrEmpty(_preferences.Keyboard))
        {
            _keyboard?.Dispose();
            _keyboard = null;
        }
        else if (_keyboard == null)
        {
            _keyboard = RuntimeHotkeyService.Register(_preferences.Keyboard, () =>
            {
                if (FindKeyboardConflict(_preferences.Keyboard) == null) Toggle();
                else RefreshBindings();
            }, new RuntimeHotkeyOptions
            {
                Id = KeyboardId, DisplayName = "奶龙大笑", Category = "奶龙大笑",
            });
        }
        if (conflict != null && conflict != _keyboardConflict)
            Warn($"键盘绑定已暂停：{conflict}。请到奶龙设置重新绑定。");
        _keyboardConflict = conflict;

    }

    private void OnInputRebound() => RefreshBindings();

    private static string? FindKeyboardConflict(string binding)
    {
        if (string.IsNullOrEmpty(binding)) return null;
        if (!RuntimeHotkeyService.TryNormalizeBinding(binding, out string normalized)
            || normalized.StartsWith("action:", StringComparison.OrdinalIgnoreCase)
            || !Enum.TryParse(NndxBindingRules.PrimaryKey(normalized), out Key primary)
            || primary is Key.None or Key.Ctrl or Key.Alt or Key.Shift or Key.Meta)
            return "请选择有效的键盘按键（不能只使用修饰键）";
        if (NndxBindingRules.FixedGameConflict(normalized) is { } fixedAction)
            return $"按键已用于 {fixedAction}";
        if (NInputManager.Instance is not { } input) return "游戏输入尚未就绪";
        foreach (StringName action in MegaInput.AllInputs.Select(x => new StringName(x))
                     .Concat(NInputManager.remappableKeyboardInputs).Append(MegaInput.back).Distinct())
            if (input.GetShortcutKey(action) == primary)
                return $"{primary} 已用于游戏操作 {ActionLabel(action.ToString())}（组合键也会触发此操作）";
        // Godot UI shortcuts, fixed keys and mods exposing their bindings through InputMap.
        foreach (StringName action in InputMap.GetActions())
        foreach (InputEvent evt in InputMap.ActionGetEvents(action))
            if (evt is InputEventKey key && EventUsesBinding(key, primary, normalized))
                return $"{primary} 已用于输入动作 {ActionLabel(action.ToString())}";
        foreach (var hotkey in RuntimeHotkeyService.GetRegisteredHotkeys())
        {
            if (hotkey.Id == KeyboardId) continue;
            foreach (string other in hotkey.CurrentBindings)
            {
                if (other.StartsWith("action:", StringComparison.OrdinalIgnoreCase))
                {
                    string action = other[7..];
                    if (input.GetShortcutKey(action) == primary) return $"按键已用于 {hotkey.DisplayName ?? action}";
                    if (!InputMap.HasAction(action)) continue;
                    foreach (InputEvent evt in InputMap.ActionGetEvents(action))
                        if (evt is InputEventKey key && EventUsesBinding(key, primary, normalized))
                            return $"按键已用于 {hotkey.DisplayName ?? action}";
                }
                else if (NndxBindingRules.Overlaps(normalized, other))
                    return $"按键已用于 {hotkey.DisplayName ?? hotkey.Id ?? other}";
            }
        }
        return null;
    }

    private static string ActionLabel(string action) => action switch
    {
        "ui_accept" => "结束回合 / 继续",
        "ui_select" => "确认卡牌",
        "ui_cancel" => "取消 / 返回",
        "mega_view_map" => "查看地图",
        "mega_view_draw_pile" => "查看抽牌堆",
        "mega_view_discard_pile" => "查看弃牌堆",
        "mega_view_deck_and_tab_left" => "查看牌组 / 向左切换",
        "mega_view_exhaust_pile_and_tab_right" => "查看消耗牌堆 / 向右切换",
        "mega_peek" => "查看战场",
        "ui_up" => "向上", "ui_down" => "向下", "ui_left" => "向左", "ui_right" => "向右",
        "mega_release_card" => "放下卡牌",
        _ when action.StartsWith("mega_select_card_") => "选择卡牌 " + action[17..],
        _ => action,
    };

    private static bool EventUsesBinding(InputEventKey key, Key primary, string binding)
    {
        if (key.Keycode != primary && key.PhysicalKeycode != primary) return false;
        return NndxBindingRules.SatisfiesModifiers(binding,
            key.CtrlPressed, key.AltPressed, key.ShiftPressed, key.MetaPressed);
    }

    private bool Commit(NndxInputPreferences next)
    {
        try { next.Save(_path); }
        catch (Exception ex)
        {
            Warn("保存设置失败，原设置保持不变。");
            Entry.Logger.Error($"NNDX input settings save failed: {ex}");
            return false;
        }
        bool petsChanged = _preferences.PetsLaugh != next.PetsLaugh;
        _preferences = next;
        if (petsChanged) NndxVideoOverlay.RefreshPetPreference();
        _keyboard?.Dispose();
        _keyboard = null;
        RefreshBindings();
        return true;
    }

    private void SetKeyboard(string value)
    {
        string binding = value.Trim();
        if (binding.Length > 0 && RuntimeHotkeyService.TryNormalizeBinding(binding, out string normalized))
            binding = normalized;
        string? conflict = FindKeyboardConflict(binding);
        if (conflict != null) { Warn($"未修改键盘绑定：{conflict}。"); return; }
        Commit(_preferences with { Keyboard = binding });
    }

    private void Warn(string text)
    {
        Entry.Logger.Warn(text);
        if (IsInsideTree()) RitsuToastService.ShowWarning(text, "奶龙大笑");
    }

    private static ModSettingsText T(string text) => ModSettingsText.Literal(text);

    private void RegisterSettings()
    {
        RitsuLibFramework.RegisterModSettings(Entry.ModId, page => page
            .WithTitle(T("奶龙大笑")).WithModDisplayName(T("奶龙大笑"))
            .AddSection("input", section => section.WithTitle(T("按键设置"))
                .AddCustom("keyboard", T("播放 / 停止（键盘）"), _ => new NndxBindingCapture
                {
                    Title = "播放 / 停止（键盘）",
                    Read = () => _preferences.Keyboard,
                    Write = SetKeyboard,
                })
                .AddButton("reset_keyboard", T("恢复键盘默认值"), T("恢复 C"), () => SetKeyboard("C")))
            .AddSection("pets", section => section.WithTitle(T("召唤物"))
                .AddToggle("pets_laugh", T("召唤物随主人一起大笑"),
                    new ModSettingsCallbackValueBinding<bool>(Entry.ModId, "pets_laugh",
                        STS2RitsuLib.Utils.Persistence.SaveScope.Global,
                        () => _preferences.PetsLaugh,
                        value => Commit(_preferences with { PetsLaugh = value }), () => { }))));
    }

    public override void _ExitTree()
    {
        _keyboard?.Dispose();
        if (GodotObject.IsInstanceValid(_inputManager))
            _inputManager!.Disconnect(NInputManager.SignalName.InputRebound, Callable.From(OnInputRebound));
    }
}
