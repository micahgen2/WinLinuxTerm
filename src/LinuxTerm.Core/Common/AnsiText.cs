using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace LinuxTerm.Core.Common;

/// <summary>
/// Represents a slice of styled text parsed from ANSI escape sequences.
/// </summary>
public record AnsiRun(
    string Text,
    AnsiColor? Foreground = null,
    AnsiColor? Background = null,
    bool Bold = false,
    bool Dim = false,
    bool Italic = false,
    bool Underline = false,
    bool Invert = false);

public record AnsiColor(byte R, byte G, byte B)
{
    public static AnsiColor FromRgb(byte r, byte g, byte b) => new(r, g, b);
    public static AnsiColor FromConsole(ConsoleColor color) => color switch
    {
        ConsoleColor.Black => new(0, 0, 0),
        ConsoleColor.DarkBlue => new(0, 0, 170),
        ConsoleColor.DarkGreen => new(0, 170, 0),
        ConsoleColor.DarkCyan => new(0, 170, 170),
        ConsoleColor.DarkRed => new(170, 0, 0),
        ConsoleColor.DarkMagenta => new(170, 0, 170),
        ConsoleColor.DarkYellow => new(170, 85, 0),
        ConsoleColor.Gray => new(170, 170, 170),
        ConsoleColor.DarkGray => new(85, 85, 85),
        ConsoleColor.Blue => new(85, 85, 255),
        ConsoleColor.Green => new(85, 255, 85),
        ConsoleColor.Cyan => new(85, 255, 255),
        ConsoleColor.Red => new(255, 85, 85),
        ConsoleColor.Magenta => new(255, 85, 255),
        ConsoleColor.Yellow => new(255, 255, 85),
        ConsoleColor.White => new(255, 255, 255),
        _ => new(200, 200, 200)
    };
}

/// <summary>
/// Utilities for working with ANSI terminal codes.
/// </summary>
public static class AnsiText
{
    public const string Reset = "\x1b[0m";
    public const string Bold = "\x1b[1m";
    public const string Dim = "\x1b[2m";
    public const string Italic = "\x1b[3m";
    public const string Underline = "\x1b[4m";
    public const string Invert = "\x1b[7m";

    // Standard foregrounds
    public const string Black = "\x1b[30m";
    public const string Red = "\x1b[31m";
    public const string Green = "\x1b[32m";
    public const string Yellow = "\x1b[33m";
    public const string Blue = "\x1b[34m";
    public const string Magenta = "\x1b[35m";
    public const string Cyan = "\x1b[36m";
    public const string White = "\x1b[37m";

    // Bright foregrounds
    public const string BrightBlack = "\x1b[90m";
    public const string BrightRed = "\x1b[91m";
    public const string BrightGreen = "\x1b[92m";
    public const string BrightYellow = "\x1b[93m";
    public const string BrightBlue = "\x1b[94m";
    public const string BrightMagenta = "\x1b[95m";
    public const string BrightCyan = "\x1b[96m";
    public const string BrightWhite = "\x1b[97m";

    // Backgrounds
    public const string BgRed = "\x1b[41m";
    public const string BgGreen = "\x1b[42m";
    public const string BgYellow = "\x1b[43m";
    public const string BgBlue = "\x1b[44m";
    public const string BgMagenta = "\x1b[45m";
    public const string BgCyan = "\x1b[46m";
    public const string BgWhite = "\x1b[47m";

    private static readonly Regex AnsiRegex = new(@"\x1b\[([0-9;]*)m", RegexOptions.Compiled);
    private static readonly Regex StripAnsiRegex = new(@"\x1b\[[0-9;]*[a-zA-Z]", RegexOptions.Compiled);

    public static string StripAnsi(string text) => StripAnsiRegex.Replace(text, string.Empty);

    public static string Colorize(string text, string ansiCode) => $"{ansiCode}{text}{Reset}";

    private static readonly Dictionary<int, AnsiColor> StandardColors = new()
    {
        [30] = new(0, 0, 0),
        [31] = new(205, 49, 49),
        [32] = new(13, 188, 121),
        [33] = new(229, 229, 16),
        [34] = new(36, 114, 200),
        [35] = new(188, 63, 188),
        [36] = new(17, 168, 205),
        [37] = new(229, 229, 229),

        [90] = new(102, 102, 102),
        [91] = new(241, 76, 76),
        [92] = new(35, 209, 139),
        [93] = new(245, 245, 67),
        [94] = new(59, 142, 234),
        [95] = new(214, 112, 214),
        [96] = new(41, 184, 219),
        [97] = new(255, 255, 255),

        [40] = new(0, 0, 0),
        [41] = new(205, 49, 49),
        [42] = new(13, 188, 121),
        [43] = new(229, 229, 16),
        [44] = new(36, 114, 200),
        [45] = new(188, 63, 188),
        [46] = new(17, 168, 205),
        [47] = new(229, 229, 229)
    };

    /// <summary>
    /// Parses a string containing ANSI escape codes into styled runs.
    /// </summary>
    public static List<AnsiRun> Parse(string text)
    {
        var runs = new List<AnsiRun>();
        if (string.IsNullOrEmpty(text))
            return runs;

        AnsiColor? currentFg = null;
        AnsiColor? currentBg = null;
        bool bold = false;
        bool dim = false;
        bool italic = false;
        bool underline = false;
        bool invert = false;

        int lastIndex = 0;
        var matches = AnsiRegex.Matches(text);

        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
            {
                var chunk = text[lastIndex..match.Index];
                runs.Add(new AnsiRun(chunk, currentFg, currentBg, bold, dim, italic, underline, invert));
            }

            var codeStr = match.Groups[1].Value;
            var parts = string.IsNullOrEmpty(codeStr) ? new[] { "0" } : codeStr.Split(';');

            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out int code))
                    continue;

                switch (code)
                {
                    case 0:
                        currentFg = null;
                        currentBg = null;
                        bold = false;
                        dim = false;
                        italic = false;
                        underline = false;
                        invert = false;
                        break;
                    case 1: bold = true; break;
                    case 2: dim = true; break;
                    case 3: italic = true; break;
                    case 4: underline = true; break;
                    case 7: invert = true; break;
                    case 22: bold = false; dim = false; break;
                    case 23: italic = false; break;
                    case 24: underline = false; break;
                    case 27: invert = false; break;
                    case 39: currentFg = null; break;
                    case 49: currentBg = null; break;

                    // Standard foreground
                    case >= 30 and <= 37:
                    case >= 90 and <= 97:
                        if (StandardColors.TryGetValue(code, out var fg))
                            currentFg = fg;
                        break;

                    // Standard background
                    case >= 40 and <= 47:
                    case >= 100 and <= 107:
                        int bgCode = code >= 100 ? code - 60 : code;
                        if (StandardColors.TryGetValue(bgCode, out var bg))
                            currentBg = bg;
                        break;

                    // 24-bit RGB or 256-color
                    case 38: // FG
                        if (i + 4 < parts.Length && parts[i + 1] == "2")
                        {
                            if (byte.TryParse(parts[i + 2], out byte r) &&
                                byte.TryParse(parts[i + 3], out byte g) &&
                                byte.TryParse(parts[i + 4], out byte b))
                            {
                                currentFg = new AnsiColor(r, g, b);
                                i += 4;
                            }
                        }
                        else if (i + 2 < parts.Length && parts[i + 1] == "5")
                        {
                            if (int.TryParse(parts[i + 2], out int idx))
                            {
                                currentFg = Get256Color(idx);
                                i += 2;
                            }
                        }
                        break;

                    case 48: // BG
                        if (i + 4 < parts.Length && parts[i + 1] == "2")
                        {
                            if (byte.TryParse(parts[i + 2], out byte r) &&
                                byte.TryParse(parts[i + 3], out byte g) &&
                                byte.TryParse(parts[i + 4], out byte b))
                            {
                                currentBg = new AnsiColor(r, g, b);
                                i += 4;
                            }
                        }
                        break;
                }
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            runs.Add(new AnsiRun(text[lastIndex..], currentFg, currentBg, bold, dim, italic, underline, invert));
        }

        return runs;
    }

    private static AnsiColor Get256Color(int index)
    {
        if (index < 16 && StandardColors.TryGetValue(30 + index, out var sc))
            return sc;

        if (index is >= 16 and <= 231)
        {
            index -= 16;
            int r = (index / 36) * 51;
            int g = ((index / 6) % 6) * 51;
            int b = (index % 6) * 51;
            return new AnsiColor((byte)r, (byte)g, (byte)b);
        }

        if (index is >= 232 and <= 255)
        {
            int gray = (index - 232) * 10 + 8;
            return new AnsiColor((byte)gray, (byte)gray, (byte)gray);
        }

        return new AnsiColor(200, 200, 200);
    }
}
