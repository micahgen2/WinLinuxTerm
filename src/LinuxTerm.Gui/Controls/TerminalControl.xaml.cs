using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using LinuxTerm.Core.Common;
using LinuxTerm.Core.Execution;
using LinuxTerm.Gui.Themes;

namespace LinuxTerm.Gui.Controls;

public partial class TerminalControl : UserControl
{
    public ShellContext Context { get; }
    public ShellEngine Engine { get; }

    public event Action<string>? TitleChanged;

    private int _historyIndex;
    private string? _historyStash;
    private CancellationTokenSource? _currentCts;
    private bool _isRunning;
    private TerminalTheme _currentTheme = TerminalTheme.VsCodeDark;
    private Paragraph? _currentParagraph;

    public TerminalControl(string? initialDirectory = null)
    {
        InitializeComponent();
        Context = new ShellContext(initialDirectory);
        Engine = new ShellEngine();
        _historyIndex = 0;
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        UpdatePrompt();
        UpdateStatusBar();
        PrintWelcome();
        CommandInput.Focus();
    }

    public void ApplyTheme(TerminalTheme theme)
    {
        _currentTheme = theme;

        RootGrid.Background = theme.BackgroundBrush;
        OutputBox.Foreground = theme.ForegroundBrush;
        InputContainer.Background = theme.HeaderBackgroundBrush;
        StatusBarBorder.Background = theme.HeaderBackgroundBrush;
        CommandInput.Foreground = theme.ForegroundBrush;
        CommandInput.CaretBrush = theme.AccentBrush;

        UpdatePrompt();
    }

    public void SetFontSize(double size)
    {
        if (size is >= 9 and <= 32)
        {
            OutputBox.FontSize = size;
            CommandInput.FontSize = size;
            PromptBlock.FontSize = size;
        }
    }

    private void PrintWelcome()
    {
        AppendAnsi($"{AnsiText.BrightCyan}======================================================================{AnsiText.Reset}\n");
        AppendAnsi($"{AnsiText.Bold}{AnsiText.BrightGreen}  WinLinuxTerm - Windows POSIX Terminal Emulator (C# .NET 10){AnsiText.Reset}\n");
        AppendAnsi($"{AnsiText.BrightCyan}======================================================================{AnsiText.Reset}\n");
        AppendAnsi($" Type '{AnsiText.BrightYellow}help{AnsiText.Reset}' for built-in Linux commands, '{AnsiText.BrightYellow}clear{AnsiText.Reset}' or Ctrl+L to clear.\n");
        AppendAnsi($" Supports pipes ({AnsiText.BrightMagenta}|{AnsiText.Reset}), redirects ({AnsiText.BrightMagenta}>{AnsiText.Reset}, {AnsiText.BrightMagenta}>>{AnsiText.Reset}), aliases, and native Windows binaries.\n\n");
    }

    private void UpdatePrompt()
    {
        PromptBlock.Inlines.Clear();

        var userRun = new Run(Context.UserName)
        {
            Foreground = _currentTheme.PromptUserBrush,
            FontWeight = FontWeights.Bold
        };
        var atRun = new Run("@") { Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)) };
        var hostRun = new Run(Context.HostName)
        {
            Foreground = _currentTheme.PromptUserBrush,
            FontWeight = FontWeights.Bold
        };
        var colonRun = new Run(":") { Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)) };
        var pathRun = new Run(Context.PosixCurrentDirectory)
        {
            Foreground = _currentTheme.PromptPathBrush,
            FontWeight = FontWeights.Bold
        };

        PromptBlock.Inlines.Add(userRun);
        PromptBlock.Inlines.Add(atRun);
        PromptBlock.Inlines.Add(hostRun);
        PromptBlock.Inlines.Add(colonRun);
        PromptBlock.Inlines.Add(pathRun);

        var branch = Context.GetGitBranch();
        if (!string.IsNullOrEmpty(branch))
        {
            var branchRun = new Run($" ({branch})")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 170)),
                FontWeight = FontWeights.Normal
            };
            PromptBlock.Inlines.Add(branchRun);
        }

        var symbolRun = new Run(Context.IsAdmin ? " # " : " $ ")
        {
            Foreground = _currentTheme.ForegroundBrush,
            FontWeight = FontWeights.Bold
        };
        PromptBlock.Inlines.Add(symbolRun);
    }

    private void UpdateStatusBar()
    {
        StatusDirectory.Text = Context.PosixCurrentDirectory;
        var branch = Context.GetGitBranch();
        if (!string.IsNullOrEmpty(branch))
        {
            StatusGit.Text = $"git:({branch})";
            StatusGit.Visibility = Visibility.Visible;
        }
        else
        {
            StatusGit.Visibility = Visibility.Collapsed;
        }

        StatusExitCode.Text = $"exit: {Context.LastExitCode}";
        StatusExitCode.Foreground = Context.LastExitCode == 0
            ? new SolidColorBrush(Color.FromRgb(78, 201, 176))
            : new SolidColorBrush(Color.FromRgb(244, 71, 71));

        TitleChanged?.Invoke(Context.PosixCurrentDirectory);
    }

    public void AppendAnsi(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        Dispatcher.Invoke(() =>
        {
            // Handle clear screen ANSI code \x1b[2J\x1b[H
            if (text.Contains("\x1b[2J"))
            {
                TerminalDoc.Blocks.Clear();
                _currentParagraph = null;
                text = text.Replace("\x1b[2J", "").Replace("\x1b[H", "");
                if (string.IsNullOrEmpty(text)) return;
            }

            if (_currentParagraph == null)
            {
                _currentParagraph = new Paragraph { Margin = new Thickness(0), LineHeight = 1.3 };
                TerminalDoc.Blocks.Add(_currentParagraph);
            }

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var runs = AnsiText.Parse(line);

                foreach (var run in runs)
                {
                    var wpfRun = new Run(run.Text);

                    if (run.Foreground != null)
                    {
                        wpfRun.Foreground = new SolidColorBrush(Color.FromRgb(run.Foreground.R, run.Foreground.G, run.Foreground.B));
                    }
                    else
                    {
                        wpfRun.Foreground = _currentTheme.ForegroundBrush;
                    }

                    if (run.Background != null)
                    {
                        wpfRun.Background = new SolidColorBrush(Color.FromRgb(run.Background.R, run.Background.G, run.Background.B));
                    }

                    if (run.Bold) wpfRun.FontWeight = FontWeights.Bold;
                    if (run.Underline) wpfRun.TextDecorations.Add(TextDecorations.Underline);

                    _currentParagraph.Inlines.Add(wpfRun);
                }

                if (i < lines.Length - 1)
                {
                    _currentParagraph = new Paragraph { Margin = new Thickness(0), LineHeight = 1.3 };
                    TerminalDoc.Blocks.Add(_currentParagraph);
                }
            }

            OutputBox.ScrollToEnd();
        });
    }

    private async void CommandInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Enter: execute command
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            HideSuggestions();
            var commandLine = CommandInput.Text;
            CommandInput.Text = string.Empty;
            await ExecuteCommandLineAsync(commandLine);
            return;
        }

        // Up Arrow: history previous
        if (e.Key == Key.Up)
        {
            e.Handled = true;
            if (Context.History.Count == 0 || _historyIndex <= 0)
                return;

            if (_historyIndex == Context.History.Count)
                _historyStash = CommandInput.Text;

            _historyIndex--;
            CommandInput.Text = Context.History[_historyIndex];
            CommandInput.CaretIndex = CommandInput.Text.Length;
            return;
        }

        // Down Arrow: history next
        if (e.Key == Key.Down)
        {
            e.Handled = true;
            if (_historyIndex >= Context.History.Count)
                return;

            _historyIndex++;
            if (_historyIndex == Context.History.Count)
            {
                CommandInput.Text = _historyStash ?? string.Empty;
            }
            else
            {
                CommandInput.Text = Context.History[_historyIndex];
            }
            CommandInput.CaretIndex = CommandInput.Text.Length;
            return;
        }

        // Tab: autocomplete
        if (e.Key == Key.Tab)
        {
            e.Handled = true;
            HandleTabCompletion();
            return;
        }

        // Ctrl+C: Cancel
        if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            if (_isRunning && _currentCts != null)
            {
                _currentCts.Cancel();
                AppendAnsi("^C\n");
            }
            else
            {
                AppendAnsi($"{PromptBlock.Text}{CommandInput.Text}^C\n");
                CommandInput.Text = string.Empty;
            }
            return;
        }

        // Ctrl+L: Clear screen
        if (e.Key == Key.L && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            TerminalDoc.Blocks.Clear();
            _currentParagraph = null;
            return;
        }

        // Zoom font with Ctrl + OemPlus / OemMinus
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key is Key.OemPlus or Key.Add)
            {
                SetFontSize(OutputBox.FontSize + 1);
                e.Handled = true;
            }
            else if (e.Key is Key.OemMinus or Key.Subtract)
            {
                SetFontSize(OutputBox.FontSize - 1);
                e.Handled = true;
            }
        }
    }

    private void OutputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // If user presses Ctrl+C and no text is selected, cancel running command or forward to input
        if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (string.IsNullOrEmpty(OutputBox.Selection.Text))
            {
                if (_isRunning && _currentCts != null)
                {
                    _currentCts.Cancel();
                    AppendAnsi("^C\n");
                }
                CommandInput.Focus();
                e.Handled = true;
            }
            return;
        }

        // Focus input on typing
        if (!char.IsControl((char)KeyInterop.VirtualKeyFromKey(e.Key)))
        {
            CommandInput.Focus();
        }
    }

    private void OutputBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Clicking in output area allows selection or quick refocusing of input
        if (e.ClickCount == 1 && string.IsNullOrEmpty(OutputBox.Selection.Text))
        {
            CommandInput.Focus();
        }
    }

    private async Task ExecuteCommandLineAsync(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            AppendAnsi($"{PromptBlock.Text}\n");
            return;
        }

        // Echo prompt and executed command in terminal buffer
        AppendAnsi($"{PromptBlock.Text}{commandLine}\n");

        _historyIndex = Context.History.Count + 1; // Will be incremented inside ShellEngine
        _isRunning = true;
        RunningIndicator.Visibility = Visibility.Visible;
        CommandInput.IsEnabled = false;

        _currentCts = new CancellationTokenSource();

        var streamWriter = new WpfAnsiWriter(this);

        try
        {
            await Task.Run(async () =>
            {
                await Engine.ExecuteAsync(
                    commandLine,
                    Context,
                    TextReader.Null,
                    streamWriter,
                    streamWriter,
                    _currentCts.Token);
            }, _currentCts.Token);
        }
        catch (OperationCanceledException)
        {
            AppendAnsi("^C\n");
        }
        catch (Exception ex)
        {
            AppendAnsi($"{AnsiText.BrightRed}Error: {ex.Message}{AnsiText.Reset}\n");
        }
        finally
        {
            _isRunning = false;
            RunningIndicator.Visibility = Visibility.Collapsed;
            CommandInput.IsEnabled = true;
            _historyIndex = Context.History.Count;

            UpdatePrompt();
            UpdateStatusBar();
            CommandInput.Focus();
        }
    }

    private void HandleTabCompletion()
    {
        var text = CommandInput.Text;
        int caret = CommandInput.CaretIndex;
        var prefixToCaret = text[..caret];
        int lastSpace = prefixToCaret.LastIndexOfAny(new[] { ' ', '|', ';', '&' });
        string wordPrefix = lastSpace == -1 ? prefixToCaret : prefixToCaret[(lastSpace + 1)..];

        if (string.IsNullOrEmpty(wordPrefix))
            return;

        var matches = new List<string>();

        if (lastSpace == -1)
        {
            // Built-in commands and aliases
            matches.AddRange(Engine.Registry.GetCommandNames().Where(c => c.StartsWith(wordPrefix, StringComparison.OrdinalIgnoreCase)));
            matches.AddRange(Context.Aliases.Keys.Where(a => a.StartsWith(wordPrefix, StringComparison.OrdinalIgnoreCase)));
        }

        // File and directory completion
        try
        {
            string searchDir = Context.CurrentDirectory;
            string filePrefix = wordPrefix;

            if (wordPrefix.Contains('/') || wordPrefix.Contains('\\'))
            {
                var dirPart = Path.GetDirectoryName(wordPrefix.Replace('/', Path.DirectorySeparatorChar));
                if (!string.IsNullOrEmpty(dirPart))
                {
                    searchDir = PosixPathMapper.ToWindows(dirPart, Context.CurrentDirectory);
                }
                filePrefix = Path.GetFileName(wordPrefix);
            }

            if (Directory.Exists(searchDir))
            {
                var dirInfo = new DirectoryInfo(searchDir);
                foreach (var entry in dirInfo.GetFileSystemInfos())
                {
                    if (entry.Name.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var completed = entry is DirectoryInfo ? entry.Name + "/" : entry.Name;
                        if (wordPrefix.Contains('/'))
                        {
                            var dirPart = wordPrefix[..wordPrefix.LastIndexOf('/')] + "/";
                            matches.Add(dirPart + completed);
                        }
                        else
                        {
                            matches.Add(completed);
                        }
                    }
                }
            }
        }
        catch { }

        if (matches.Count == 1)
        {
            var match = matches[0];
            var addition = match[wordPrefix.Length..];
            CommandInput.Text = text.Insert(caret, addition);
            CommandInput.CaretIndex = caret + addition.Length;
            HideSuggestions();
        }
        else if (matches.Count > 1)
        {
            ShowSuggestions(matches.Take(12).ToList());
        }
    }

    private void ShowSuggestions(List<string> suggestions)
    {
        SuggestionsPanel.Children.Clear();
        foreach (var s in suggestions)
        {
            var btn = new Button
            {
                Content = s,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                Foreground = _currentTheme.ForegroundBrush,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(3),
                Padding = new Thickness(6, 2, 6, 2),
                FontFamily = OutputBox.FontFamily,
                FontSize = 11.5,
                Cursor = Cursors.Hand
            };
            btn.Click += (sender, e) =>
            {
                ApplyCompletion(s);
                HideSuggestions();
                CommandInput.Focus();
            };
            SuggestionsPanel.Children.Add(btn);
        }
        SuggestionsBorder.Visibility = Visibility.Visible;
    }

    private void ApplyCompletion(string completion)
    {
        var text = CommandInput.Text;
        int caret = CommandInput.CaretIndex;
        var prefixToCaret = text[..caret];
        int lastSpace = prefixToCaret.LastIndexOfAny(new[] { ' ', '|', ';', '&' });
        int wordStart = lastSpace == -1 ? 0 : lastSpace + 1;

        CommandInput.Text = text[..wordStart] + completion + text[caret..];
        CommandInput.CaretIndex = wordStart + completion.Length;
    }

    private void HideSuggestions()
    {
        SuggestionsBorder.Visibility = Visibility.Collapsed;
    }

    private void CommandInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SuggestionsBorder.Visibility == Visibility.Visible && string.IsNullOrWhiteSpace(CommandInput.Text))
        {
            HideSuggestions();
        }
    }

    private class WpfAnsiWriter : TextWriter
    {
        private readonly TerminalControl _control;
        public override Encoding Encoding => Encoding.UTF8;

        public WpfAnsiWriter(TerminalControl control)
        {
            _control = control;
        }

        public override void Write(char value)
        {
            _control.AppendAnsi(value.ToString());
        }

        public override void Write(string? value)
        {
            if (value != null)
                _control.AppendAnsi(value);
        }

        public override void WriteLine(string? value)
        {
            _control.AppendAnsi((value ?? string.Empty) + "\n");
        }

        public override void WriteLine()
        {
            _control.AppendAnsi("\n");
        }
    }
}
