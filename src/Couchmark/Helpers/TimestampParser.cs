using System;

namespace Couchmark.Helpers;

/// <summary>
/// Utility helper for parsing and formatting timestamp strings
/// (e.g., "01:30" <-> 90 seconds, "01:15:30" <-> 4530 seconds).
/// </summary>
public static class TimestampParser
{
    /// <summary>
    /// Formats seconds into a clean display string: "mm:ss" or "hh:mm:ss".
    /// Returns empty string if seconds is less than or equal to 0.
    /// </summary>
    public static string FormatSeconds(int totalSeconds)
    {
        if (totalSeconds <= 0) return string.Empty;
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.Hours > 0
            ? ts.ToString(@"hh\:mm\:ss")
            : ts.ToString(@"mm\:ss");
    }

    /// <summary>
    /// Formats seconds into "mm:ss" or "hh:mm:ss".
    /// If totalSeconds is 0, returns "00:00".
    /// </summary>
    public static string FormatSecondsOrZero(int totalSeconds)
    {
        if (totalSeconds <= 0) return "00:00";
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.Hours > 0
            ? ts.ToString(@"hh\:mm\:ss")
            : ts.ToString(@"mm\:ss");
    }

    /// <summary>
    /// Parses user text input into total seconds.
    /// Accepts raw seconds ("90"), mm:ss ("01:30", "1:30"), or hh:mm:ss ("01:15:30").
    /// Returns 0 for invalid or empty inputs.
    /// </summary>
    public static int ParseToSeconds(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return 0;
        input = input.Trim();

        // 1. Raw integer (e.g., "90")
        if (int.TryParse(input, out int rawSeconds))
        {
            return Math.Max(0, rawSeconds);
        }

        // 2. Colon-separated format
        var parts = input.Split(':');
        if (parts.Length == 2)
        {
            if (int.TryParse(parts[0], out int minutes) && int.TryParse(parts[1], out int seconds))
            {
                return Math.Max(0, minutes * 60 + seconds);
            }
        }
        else if (parts.Length == 3)
        {
            if (int.TryParse(parts[0], out int hours) &&
                int.TryParse(parts[1], out int minutes) &&
                int.TryParse(parts[2], out int seconds))
            {
                return Math.Max(0, hours * 3600 + minutes * 60 + seconds);
            }
        }

        // 3. Fallback to standard TimeSpan.TryParse
        if (TimeSpan.TryParse(input, out TimeSpan parsed))
        {
            return (int)Math.Max(0, parsed.TotalSeconds);
        }

        return 0;
    }
}
