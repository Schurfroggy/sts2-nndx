using Godot;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Ui.Shell.Theme;

namespace Nndx.Scripts;

// These controls belong only to this mod. No dependency-library text or controls are patched.
public partial class NndxBindingCapture : VBoxContainer
{
    internal string Title = "";
    internal Func<string> Read = () => "";
    internal Action<string> Write = _ => { };
    private ModSettingsMiniButton? _captureButton;
    private ModSettingsMiniButton? _clearButton;
    private bool _capturing;

    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Ignore;
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var title = new Label
        {
            Text = Title, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        title.AddThemeFontOverride("font", RitsuShellTheme.Current.Font.Body);
        title.AddThemeFontSizeOverride("font_size", RitsuShellTheme.Current.Metric.FontSize.ValueLabel);
        title.AddThemeColorOverride("font_color", RitsuShellTheme.Current.Text.LabelPrimary);
        row.AddChild(title);
        _captureButton = new ModSettingsMiniButton("", BeginCapture)
        {
            FocusMode = FocusModeEnum.All,
            CustomMinimumSize = new Vector2(220, 48),
        };
        _clearButton = new ModSettingsMiniButton("清除", () => Finish(""))
        {
            FocusMode = FocusModeEnum.All,
            CustomMinimumSize = new Vector2(80, 48),
        };
        row.AddChild(_captureButton);
        row.AddChild(_clearButton);
        AddChild(row);
        var hint = new Label
        {
            Text = "支持组合键，并且必须包含一个非修饰键",
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        hint.AddThemeFontOverride("font", RitsuShellTheme.Current.Font.Body);
        hint.AddThemeFontSizeOverride("font_size", RitsuShellTheme.Current.Metric.Keybinding.HintFontSize);
        hint.AddThemeColorOverride("font_color", RitsuShellTheme.Current.Text.LabelSecondary);
        AddChild(hint);
        Refresh();
    }

    private void BeginCapture()
    {
        if (_capturing) return;
        _capturing = true;
        _captureButton!.GrabFocus();
        _captureButton.Text = "按下按键";
        _clearButton!.Disabled = true;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_capturing) return;
        if (!IsVisibleInTree() || !_captureButton!.HasFocus()) { Cancel(); return; }
        if (@event is InputEventKey key)
        {
            GetViewport().SetInputAsHandled();
            if (!key.Pressed || key.IsEcho()) return;
            if (key.Keycode == Key.Escape) { Cancel(); return; }
            if (key.Keycode is Key.Backspace or Key.Delete) { Finish(""); return; }
            Key primary = key.Keycode == Key.None ? key.PhysicalKeycode : key.Keycode;
            if (primary is Key.None or Key.Ctrl or Key.Alt or Key.Shift or Key.Meta) return;
            var parts = new List<string>();
            if (key.CtrlPressed) parts.Add("Ctrl");
            if (key.AltPressed) parts.Add("Alt");
            if (key.ShiftPressed) parts.Add("Shift");
            if (key.MetaPressed) parts.Add("Meta");
            parts.Add(primary.ToString());
            Finish(string.Join('+', parts));
        }
    }

    private void Finish(string value)
    {
        Cancel();
        Write(value);
        Refresh(); // A rejected edit must display the previous binding immediately.
    }

    private void Cancel()
    {
        _capturing = false;
        if (_clearButton != null) _clearButton.Disabled = false;
        Refresh();
    }

    private void Refresh()
    {
        if (_capturing || _captureButton == null) return;
        string value = Read();
        string text = string.IsNullOrEmpty(value) ? "未绑定" : value;
        if (_captureButton.Text != text) _captureButton.Text = text;
    }

    public override void _Process(double delta)
    {
        if (_capturing && (!IsVisibleInTree() || !_captureButton!.HasFocus())) Cancel();
        Refresh();
    }

    public override void _ExitTree() => Cancel();
}
