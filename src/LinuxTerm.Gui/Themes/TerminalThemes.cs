using System.Collections.Generic;
using System.Windows.Media;

namespace LinuxTerm.Gui.Themes;

public record TerminalTheme(
    string Name,
    Color Background,
    Color Foreground,
    Color HeaderBackground,
    Color TabActiveBackground,
    Color TabInactiveBackground,
    Color AccentColor,
    Color PromptUserColor,
    Color PromptPathColor)
{
    public Brush BackgroundBrush => new SolidColorBrush(Background);
    public Brush ForegroundBrush => new SolidColorBrush(Foreground);
    public Brush HeaderBackgroundBrush => new SolidColorBrush(HeaderBackground);
    public Brush TabActiveBackgroundBrush => new SolidColorBrush(TabActiveBackground);
    public Brush TabInactiveBackgroundBrush => new SolidColorBrush(TabInactiveBackground);
    public Brush AccentBrush => new SolidColorBrush(AccentColor);
    public Brush PromptUserBrush => new SolidColorBrush(PromptUserColor);
    public Brush PromptPathBrush => new SolidColorBrush(PromptPathColor);

    public static readonly TerminalTheme Ubuntu = new(
        Name: "Ubuntu Aubergine",
        Background: Color.FromRgb(48, 10, 36),        // #300a24
        Foreground: Color.FromRgb(240, 240, 240),
        HeaderBackground: Color.FromRgb(36, 7, 27),
        TabActiveBackground: Color.FromRgb(48, 10, 36),
        TabInactiveBackground: Color.FromRgb(25, 5, 19),
        AccentColor: Color.FromRgb(233, 84, 32),       // Ubuntu orange
        PromptUserColor: Color.FromRgb(142, 230, 71),  // Bright green
        PromptPathColor: Color.FromRgb(114, 159, 207)  // Blue
    );

    public static readonly TerminalTheme VsCodeDark = new(
        Name: "VS Code Dark+",
        Background: Color.FromRgb(30, 30, 30),         // #1e1e1e
        Foreground: Color.FromRgb(204, 204, 204),
        HeaderBackground: Color.FromRgb(24, 24, 24),
        TabActiveBackground: Color.FromRgb(30, 30, 30),
        TabInactiveBackground: Color.FromRgb(45, 45, 45),
        AccentColor: Color.FromRgb(0, 122, 204),       // VS Code blue
        PromptUserColor: Color.FromRgb(78, 201, 176),
        PromptPathColor: Color.FromRgb(86, 156, 214)
    );

    public static readonly TerminalTheme Monokai = new(
        Name: "Monokai Pro",
        Background: Color.FromRgb(39, 40, 34),         // #272822
        Foreground: Color.FromRgb(248, 248, 242),
        HeaderBackground: Color.FromRgb(25, 26, 22),
        TabActiveBackground: Color.FromRgb(39, 40, 34),
        TabInactiveBackground: Color.FromRgb(30, 31, 26),
        AccentColor: Color.FromRgb(249, 38, 114),      // Pink
        PromptUserColor: Color.FromRgb(166, 226, 46),  // Green
        PromptPathColor: Color.FromRgb(102, 217, 239)  // Cyan
    );

    public static readonly TerminalTheme Matrix = new(
        Name: "Matrix Hacker",
        Background: Color.FromRgb(10, 15, 10),         // Very dark green/black
        Foreground: Color.FromRgb(0, 255, 102),        // Terminal green
        HeaderBackground: Color.FromRgb(5, 8, 5),
        TabActiveBackground: Color.FromRgb(10, 20, 10),
        TabInactiveBackground: Color.FromRgb(5, 12, 5),
        AccentColor: Color.FromRgb(0, 255, 102),
        PromptUserColor: Color.FromRgb(50, 255, 50),
        PromptPathColor: Color.FromRgb(0, 200, 150)
    );

    public static readonly TerminalTheme Cyberpunk = new(
        Name: "Cyberpunk Neon",
        Background: Color.FromRgb(24, 18, 43),         // #18122B
        Foreground: Color.FromRgb(235, 230, 255),
        HeaderBackground: Color.FromRgb(15, 10, 28),
        TabActiveBackground: Color.FromRgb(24, 18, 43),
        TabInactiveBackground: Color.FromRgb(18, 12, 33),
        AccentColor: Color.FromRgb(255, 0, 128),       // Neon magenta
        PromptUserColor: Color.FromRgb(0, 240, 255),   // Neon cyan
        PromptPathColor: Color.FromRgb(255, 220, 0)    // Neon yellow
    );

    public static readonly IReadOnlyList<TerminalTheme> AllThemes = new[]
    {
        Ubuntu,
        VsCodeDark,
        Monokai,
        Matrix,
        Cyberpunk
    };
}
