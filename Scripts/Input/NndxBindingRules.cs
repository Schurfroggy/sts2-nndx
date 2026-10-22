namespace Nndx.Scripts;

// Independent of Godot so overlap semantics can be regression tested.
internal static class NndxBindingRules
{
    internal static string PrimaryKey(string binding) => binding.Split('+')[^1];

    internal static bool Overlaps(string left, string right)
    {
        if (!string.Equals(PrimaryKey(left), PrimaryKey(right), StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (string modifier in new[] { "Ctrl", "Alt", "Shift", "Meta" })
        {
            int a = Requirement(left, modifier), b = Requirement(right, modifier);
            if ((a == 0) != (b == 0) || (a > 1 && b > 1 && a != b))
                return false;
        }
        return true;
    }

    internal static string? FixedGameConflict(string binding)
    {
        string primary = PrimaryKey(binding);
        if (primary is "Apostrophe" or "Asterisk" or "Asciicircum" or "Quoteleft"
            || (primary == "Key8" && SatisfiesModifiers(binding, false, false, true, false)))
            return "打开开发者控制台";
        if (primary == "F2") return "发送游戏反馈";
        if ((primary == "Enter" && SatisfiesModifiers(binding, false, true, false, false))
            || (primary == "F" && SatisfiesModifiers(binding, true, false, false, true)))
            return "切换游戏全屏";
        return null;
    }

    internal static bool SatisfiesModifiers(string binding, bool ctrl, bool alt, bool shift, bool meta)
        => (!ctrl || Requirement(binding, "Ctrl") != 0)
           && (!alt || Requirement(binding, "Alt") != 0)
           && (!shift || Requirement(binding, "Shift") != 0)
           && (!meta || Requirement(binding, "Meta") != 0);

    private static int Requirement(string binding, string modifier)
    {
        string[] parts = binding.Split('+');
        if (parts.Contains(modifier, StringComparer.OrdinalIgnoreCase)) return 1;
        if (parts.Contains("Left" + modifier, StringComparer.OrdinalIgnoreCase)) return 2;
        if (parts.Contains("Right" + modifier, StringComparer.OrdinalIgnoreCase)) return 3;
        return 0;
    }
}
