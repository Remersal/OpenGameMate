namespace OpenGameMate.Configuration;

[Flags]
public enum HotKeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Windows = 8,
}

public sealed record ManualCaptureHotKey(HotKeyModifiers Modifiers, string KeyName)
{
    public const string DefaultGesture = "Ctrl+Alt+F10";

    public string DisplayText
    {
        get
        {
            var parts = new List<string>(5);
            if (Modifiers.HasFlag(HotKeyModifiers.Control))
            {
                parts.Add("Ctrl");
            }

            if (Modifiers.HasFlag(HotKeyModifiers.Alt))
            {
                parts.Add("Alt");
            }

            if (Modifiers.HasFlag(HotKeyModifiers.Shift))
            {
                parts.Add("Shift");
            }

            if (Modifiers.HasFlag(HotKeyModifiers.Windows))
            {
                parts.Add("Win");
            }

            parts.Add(KeyName);
            return string.Join('+', parts);
        }
    }

    public static ManualCaptureHotKey Parse(string value)
    {
        if (!TryParse(value, out var hotKey))
        {
            throw new ConfigurationValidationException(
                "Manual capture hotkey must contain at least one modifier and one A-Z, 0-9, or F1-F12 key.");
        }

        return hotKey;
    }

    public static bool TryParse(string? value, out ManualCaptureHotKey hotKey)
    {
        hotKey = null!;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
        {
            return false;
        }

        var modifiers = HotKeyModifiers.None;
        string? keyName = null;
        foreach (var rawPart in value.Split('+'))
        {
            var part = rawPart.Trim();
            if (part.Length == 0)
            {
                return false;
            }

            var modifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotKeyModifiers.Control,
                "ALT" => HotKeyModifiers.Alt,
                "SHIFT" => HotKeyModifiers.Shift,
                "WIN" or "WINDOWS" => HotKeyModifiers.Windows,
                _ => HotKeyModifiers.None,
            };
            if (modifier != HotKeyModifiers.None)
            {
                if (modifiers.HasFlag(modifier))
                {
                    return false;
                }

                modifiers |= modifier;
                continue;
            }

            if (keyName is not null || !TryNormalizeKey(part, out keyName))
            {
                return false;
            }
        }

        if (modifiers == HotKeyModifiers.None || keyName is null)
        {
            return false;
        }

        hotKey = new ManualCaptureHotKey(modifiers, keyName);
        return true;
    }

    private static bool TryNormalizeKey(string value, out string keyName)
    {
        keyName = string.Empty;
        var normalized = value.ToUpperInvariant();
        if (normalized.Length == 1 &&
            (normalized[0] is >= 'A' and <= 'Z' or >= '0' and <= '9'))
        {
            keyName = normalized;
            return true;
        }

        if (normalized.Length is 2 or 3 && normalized[0] == 'F' &&
            int.TryParse(normalized.AsSpan(1), out var functionKey) &&
            functionKey is >= 1 and <= 12)
        {
            keyName = $"F{functionKey}";
            return true;
        }

        return false;
    }
}
