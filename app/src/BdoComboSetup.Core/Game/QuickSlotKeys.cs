using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Game;

public enum QuickKeyKind
{
    Keyboard,
    Mouse,
}

/// <summary>A key or mouse button bound to a quick slot. KeyCode is the value stored in ShortcutKey_Action_Key.</summary>
public sealed record QuickSlotKey(QuickKeyKind Kind, int KeyCode, string Label);

/// <summary>
/// The one table of quick bar keys. A keyCode is a Windows virtual-key code, except 124-127 which the game uses for mouse buttons 1-4.
/// Mouse buttons 5 and 6 are stored as the XButton virtual keys 5 and 6. Verified against the saved character files.
/// </summary>
public static class QuickSlotKeys
{
    /// <summary>Quick slots 1-20. Slots 1-10 are the main set (key 0 is slot 10).</summary>
    public const int SlotCount = 20;

    /// <summary>Quick bar positions a combo fills: 1-20, like the quick slots. QuickSlotData refuses any other position.</summary>
    public const int ComboPositionCount = QuickSlotData.PositionCount;

    private static readonly IReadOnlyDictionary<int, string> MouseLabels = new Dictionary<int, string>
    {
        [124] = "M1",
        [125] = "M2",
        [126] = "M3",
        [127] = "M4",
        [5] = "M5",
        [6] = "M6",
    };

    /// <summary>Keys of the main set when the character file has no entry for a slot: the user's own bindings.</summary>
    public static IReadOnlyDictionary<int, int> Defaults { get; } = new Dictionary<int, int>
    {
        [1] = 124,
        [2] = 125,
        [3] = 126,
        [4] = 127,
        [5] = 53,
        [6] = 54,
        [7] = 5,
        [8] = 6,
        [9] = 57,
        [10] = 48,
    };

    /// <summary>The key a quick slot shows when the character file has no binding for it, or null for slots 11-20.</summary>
    public static QuickSlotKey? DefaultKey(int slot)
    {
        ValidateSlot(slot);
        return Defaults.TryGetValue(slot, out var keyCode) ? FromKeyCode(keyCode) : null;
    }

    /// <summary>
    /// The input shown for a combo quick position: the character's bound key when <paramref name="bound"/> has one, else the
    /// default key, else the plain number (positions 11-20 have no default key). "M1" to "M6" for mouse buttons, the digit or letter for keys.
    /// </summary>
    /// <param name="position">Quick position 1-20.</param>
    /// <param name="bound">Slot to keyCode read from a character file, or null for the defaults only.</param>
    public static string ComboLabel(int position, IReadOnlyDictionary<int, int>? bound = null)
    {
        if (position is < 1 or > ComboPositionCount)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, $"Quick positions are 1 to {ComboPositionCount}.");
        }

        return QuickSlotLegend.Resolve(position, bound)?.Label ?? position.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The key for a stored keyCode, or null when the code is unbound (0) or not a key this table knows.</summary>
    public static QuickSlotKey? FromKeyCode(int keyCode)
    {
        if (MouseLabels.TryGetValue(keyCode, out var mouse))
        {
            return new QuickSlotKey(QuickKeyKind.Mouse, keyCode, mouse);
        }

        // Digits '0'-'9' (48-57) and letters 'A'-'Z' (65-90) have the same virtual-key codes as their characters.
        if (keyCode is (>= 48 and <= 57) or (>= 65 and <= 90))
        {
            return new QuickSlotKey(QuickKeyKind.Keyboard, keyCode, ((char)keyCode).ToString(CultureInfo.InvariantCulture));
        }

        return null;
    }

    /// <summary>
    /// The ShortcutKey_Action_Key InputType of a quick slot. Slots 1-9 are QuickSlot1-9, slot 10 is QuickSlot0 (key 0).
    /// Slots 11-19 follow the same pattern, and slot 20 is QuickSlot10 (by analogy with slot 10; not verified in a saved file).
    /// </summary>
    public static string InputType(int slot)
    {
        ValidateSlot(slot);
        return slot switch
        {
            10 => "QuickSlot0",
            20 => "QuickSlot10",
            _ => "QuickSlot" + slot.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>The quick slot of an InputType, or null when the name is not a quick slot (for example a gamepad row).</summary>
    public static int? SlotForInputType(string inputType)
    {
        ArgumentNullException.ThrowIfNull(inputType);
        return inputType switch
        {
            "QuickSlot0" => 10,
            "QuickSlot10" => 20,
            _ when inputType.StartsWith("QuickSlot", StringComparison.Ordinal)
                && int.TryParse(inputType.AsSpan("QuickSlot".Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                && number is >= 1 and <= 19 && number != 10 => number,
            _ => null,
        };
    }

    public static void ValidateSlot(int slot)
    {
        if (slot < 1 || slot > SlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, $"Quick slots are 1 to {SlotCount}.");
        }
    }
}

/// <summary>Chooses the key legend of a quick slot: the bound key from the character file, else the default.</summary>
public static class QuickSlotLegend
{
    /// <param name="slot">Quick slot 1-20.</param>
    /// <param name="bound">Slot to keyCode from the character file, or null when the file was not read.</param>
    /// <returns>The key, or null when the slot is unbound or has no default.</returns>
    public static QuickSlotKey? Resolve(int slot, IReadOnlyDictionary<int, int>? bound)
    {
        QuickSlotKeys.ValidateSlot(slot);
        if (bound is not null && bound.TryGetValue(slot, out var keyCode))
        {
            return QuickSlotKeys.FromKeyCode(keyCode);
        }

        return QuickSlotKeys.DefaultKey(slot);
    }
}

/// <summary>
/// Reads the quick slot bindings from a character's gameVariable.xml (UserCache\&lt;account&gt;\11\&lt;pcid&gt;\gameVariable.xml).
/// Rows look like: ShortcutKey_Action_Key InputType="QuickSlot1" ... keyCode="124". Pad rows are ignored.
/// </summary>
public static partial class QuickSlotKeyReader
{
    private const string RowStart = "<ShortcutKey_Action_Key";

    /// <summary>Quick slot to keyCode for every ShortcutKey_Action_Key row of the text. Unknown rows are skipped.</summary>
    public static IReadOnlyDictionary<int, int> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var bound = new Dictionary<int, int>();
        foreach (var line in text.Split('\n'))
        {
            if (!line.Contains(RowStart, StringComparison.Ordinal))
            {
                continue;
            }

            var input = InputTypePattern().Match(line);
            var code = KeyCodePattern().Match(line);
            if (!input.Success || !code.Success || QuickSlotKeys.SlotForInputType(input.Groups["input"].Value) is not { } slot)
            {
                continue;
            }

            bound[slot] = int.Parse(code.Groups["code"].Value, CultureInfo.InvariantCulture);
        }

        return bound;
    }

    /// <summary>Reads the bindings of a character file. Returns null when the file is missing or unreadable (the reason is logged).</summary>
    public static IReadOnlyDictionary<int, int>? Load(string path, AppLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);

        // The full path is not logged: it contains the Windows user name and the account folder.
        using var scope = logger.BeginOperation("quick_keys.load", new Dictionary<string, object?>
        {
            ["file"] = Path.GetFileName(path),
        });

        try
        {
            if (!File.Exists(path))
            {
                logger.Warn("quick_keys.missing", "The character file was not found; the default quick keys are used");
                return null;
            }

            var bound = Parse(File.ReadAllText(path, Encoding.UTF8));
            logger.Info("quick_keys.parsed", "Quick slot keys read", new Dictionary<string, object?>
            {
                ["bound_slots"] = bound.Count,
            });
            return bound;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            scope.Fail(ex);
            return null;
        }
    }

    [GeneratedRegex("InputType=\"(?<input>[^\"]+)\"")]
    private static partial Regex InputTypePattern();

    [GeneratedRegex("\\skeyCode=\"(?<code>\\d+)\"")]
    private static partial Regex KeyCodePattern();
}
