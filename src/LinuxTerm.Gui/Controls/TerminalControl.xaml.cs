using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using LinuxTerm.Core.Editors;
using LinuxTerm.Core.Editors.Nano;
using LinuxTerm.Core.Editors.Vim;
using LinuxTerm.Core.Execution;
using LinuxTerm.Gui.Themes;

namespace LinuxTerm.Gui.Controls;

public partial class TerminalControl : UserControl
{
    public ShellContext Context { get; }
    public ShellEngine Engine { get; }

    public event Action<string>? TitleChanged;
    public event Action? SplitVerticalRequested;
    public event Action? SplitHorizontalRequested;

    private int _historyIndex;
    private string? _historyStash;
    private CancellationTokenSource? _currentCts;
    private bool _isRunning;
    private TerminalTheme _currentTheme = TerminalTheme.VsCodeDark;
    private Paragraph? _currentParagraph;

    // Search state
    private readonly List<TextRange> _searchResults = new();
    private int _currentSearchIndex = -1;

    // Active full-screen editor session state
    private NanoSession? _activeNanoSession;
    private VimSession? _activeVimSession;
    private TaskCompletionSource<int>? _activeEditorTcs;

    public TerminalControl(string? initialDirectory = null)
    {
        InitializeComponent();
        Context = new ShellContext(initialDirectory);
        Engine = new ShellEngine();
        _historyIndex = 0;

        // Hook up GUI interactive text editors
        Context.NanoGuiHandler = LaunchNanoGuiAsync;
        Context.VimGuiHandler = LaunchVimGuiAsync;
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

        // Editor overlay theme styling
        EditorOverlay.Background = theme.BackgroundBrush;
        EditorHeader.Background = theme.AccentBrush;
        EditorLineNumbersBorder.Background = theme.HeaderBackgroundBrush;
        EditorStatusBar.Background = theme.HeaderBackgroundBrush;
        EditorShortcutBar.Background = theme.HeaderBackgroundBrush;
        EditorStatusText.Foreground = theme.ForegroundBrush;

        UpdatePrompt();
    }

    public void SetFontSize(double size)
    {
        if (size is >= 9 and <= 32)
        {
            OutputBox.FontSize = size;
            CommandInput.FontSize = size;
            PromptBlock.FontSize = size;
            EditorRichTextBox.FontSize = size;
            EditorLineNumbers.FontSize = size;
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
        // Ctrl+F: Open Search Bar
        if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            OpenSearchBar();
            return;
        }

        // Ctrl+Shift+E: Split Vertically
        if (e.Key == Key.E && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            SplitVerticalRequested?.Invoke();
            return;
        }

        // Ctrl+Shift+O: Split Horizontally
        if (e.Key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            SplitHorizontalRequested?.Invoke();
            return;
        }

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
        // Ctrl+F: Open Search Bar
        if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            OpenSearchBar();
            return;
        }

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
        if (!char.IsControl((char)KeyInterop.VirtualKeyFromKey(e.Key)) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            CommandInput.Focus();
        }
    }

    private void OutputBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
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

        AppendAnsi($"{PromptBlock.Text}{commandLine}\n");

        _historyIndex = Context.History.Count + 1;
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
            matches.AddRange(Engine.Registry.GetCommandNames().Where(c => c.StartsWith(wordPrefix, StringComparison.OrdinalIgnoreCase)));
            matches.AddRange(Context.Aliases.Keys.Where(a => a.StartsWith(wordPrefix, StringComparison.OrdinalIgnoreCase)));
        }

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
                            candidatesAdd(matches, completed);
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

    private static void candidatesAdd(List<string> list, string item)
    {
        list.Add(item);
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

    // --- Search Bar Implementation ---
    public void OpenSearchBar()
    {
        SearchBar.Visibility = Visibility.Visible;
        SearchInput.Focus();
        SearchInput.SelectAll();
    }

    private void BtnCloseSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBar.Visibility = Visibility.Collapsed;
        CommandInput.Focus();
    }

    private void SearchInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            BtnCloseSearch_Click(sender, e);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            bool reverse = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            FindNext(!reverse);
        }
    }

    private void SearchInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshSearchResults();
    }

    private void BtnFindNext_Click(object sender, RoutedEventArgs e) => FindNext(true);
    private void BtnFindPrev_Click(object sender, RoutedEventArgs e) => FindNext(false);

    private void RefreshSearchResults()
    {
        _searchResults.Clear();
        _currentSearchIndex = -1;

        string query = SearchInput.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchCountBlock.Text = "No matches";
            return;
        }

        var start = TerminalDoc.ContentStart;
        while (start != null && start.CompareTo(TerminalDoc.ContentEnd) < 0)
        {
            if (start.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                string text = start.GetTextInRun(LogicalDirection.Forward);
                int idx = 0;
                while ((idx = text.IndexOf(query, idx, StringComparison.OrdinalIgnoreCase)) != -1)
                {
                    var p1 = start.GetPositionAtOffset(idx);
                    var p2 = start.GetPositionAtOffset(idx + query.Length);
                    if (p1 != null && p2 != null)
                    {
                        _searchResults.Add(new TextRange(p1, p2));
                    }
                    idx += query.Length;
                }
            }
            start = start.GetNextContextPosition(LogicalDirection.Forward);
        }

        if (_searchResults.Count > 0)
        {
            _currentSearchIndex = 0;
            HighlightMatch();
        }
        else
        {
            SearchCountBlock.Text = "0 of 0";
        }
    }

    private void FindNext(bool forward)
    {
        if (_searchResults.Count == 0) return;

        if (forward)
        {
            _currentSearchIndex = (_currentSearchIndex + 1) % _searchResults.Count;
        }
        else
        {
            _currentSearchIndex = (_currentSearchIndex - 1 + _searchResults.Count) % _searchResults.Count;
        }

        HighlightMatch();
    }

    private void HighlightMatch()
    {
        if (_currentSearchIndex < 0 || _currentSearchIndex >= _searchResults.Count) return;

        var targetRange = _searchResults[_currentSearchIndex];
        OutputBox.Selection.Select(targetRange.Start, targetRange.End);
        var rect = targetRange.Start.GetCharacterRect(LogicalDirection.Forward);
        OutputBox.ScrollToVerticalOffset(OutputBox.VerticalOffset + rect.Top - 50);

        SearchCountBlock.Text = $"{_currentSearchIndex + 1} of {_searchResults.Count}";
    }

    // --- Context Menu Actions ---
    private void MenuCopy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(OutputBox.Selection.Text))
        {
            Clipboard.SetText(OutputBox.Selection.Text);
        }
    }

    private void MenuPaste_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText();
            CommandInput.SelectedText = text;
            CommandInput.CaretIndex += text.Length;
        }
        CommandInput.Focus();
    }

    private void MenuFind_Click(object sender, RoutedEventArgs e)
    {
        OpenSearchBar();
    }

    private void MenuClear_Click(object sender, RoutedEventArgs e)
    {
        TerminalDoc.Blocks.Clear();
        _currentParagraph = null;
        CommandInput.Focus();
    }

    private void MenuSplitVertical_Click(object sender, RoutedEventArgs e)
    {
        SplitVerticalRequested?.Invoke();
    }

    private void MenuSplitHorizontal_Click(object sender, RoutedEventArgs e)
    {
        SplitHorizontalRequested?.Invoke();
    }

    private void MenuOpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{Context.CurrentDirectory}\"",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private class WpfAnsiWriter : TextWriter
    {
        private readonly TerminalControl _control;
        public override Encoding Encoding => Encoding.UTF8;

        public WpfAnsiWriter(TerminalControl control)
        {
            _control = control;
        }

        public override void Write(char value) => _control.AppendAnsi(value.ToString());
        public override void Write(string? value) { if (value != null) _control.AppendAnsi(value); }
        public override void WriteLine(string? value) => _control.AppendAnsi((value ?? string.Empty) + "\n");
        public override void WriteLine() => _control.AppendAnsi("\n");
    }

    #region Interactive Text Editor Overlay (Nano & Vim)

    private Task<int> LaunchNanoGuiAsync(NanoSession session, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<int>();
        _activeEditorTcs = tcs;
        _activeNanoSession = session;
        _activeVimSession = null;

        ct.Register(() =>
        {
            Dispatcher.Invoke(() =>
            {
                CloseEditorOverlay();
                tcs.TrySetCanceled();
            });
        });

        Dispatcher.Invoke(() =>
        {
            OpenNanoGui(session);
        });

        return tcs.Task;
    }

    private Task<int> LaunchVimGuiAsync(VimSession session, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<int>();
        _activeEditorTcs = tcs;
        _activeVimSession = session;
        _activeNanoSession = null;

        ct.Register(() =>
        {
            Dispatcher.Invoke(() =>
            {
                CloseEditorOverlay();
                tcs.TrySetCanceled();
            });
        });

        Dispatcher.Invoke(() =>
        {
            OpenVimGui(session);
        });

        return tcs.Task;
    }

    private void OpenNanoGui(NanoSession session)
    {
        EditorOverlay.Visibility = Visibility.Visible;
        EditorHeader.Visibility = Visibility.Visible;
        EditorLineNumbersBorder.Visibility = Visibility.Collapsed;
        EditorShortcutBar.Visibility = Visibility.Visible;

        RenderNanoView();
        EditorOverlay.Focus();
    }

    private void OpenVimGui(VimSession session)
    {
        EditorOverlay.Visibility = Visibility.Visible;
        EditorHeader.Visibility = Visibility.Collapsed;
        EditorLineNumbersBorder.Visibility = session.ShowLineNumbers ? Visibility.Visible : Visibility.Collapsed;
        EditorShortcutBar.Visibility = Visibility.Collapsed;

        RenderVimView();
        EditorOverlay.Focus();
    }

    private void RenderNanoView()
    {
        if (_activeNanoSession == null) return;

        var buf = _activeNanoSession.Buffer;
        EditorHeaderTitle.Text = "GNU nano 8.0";
        EditorHeaderFile.Text = $"File: {Path.GetFileName(buf.FilePath ?? "New Buffer")}";
        EditorHeaderModified.Visibility = buf.IsModified ? Visibility.Visible : Visibility.Collapsed;

        if (_activeNanoSession.IsInPrompt)
        {
            EditorStatusText.Text = $"{_activeNanoSession.PromptLabel}{_activeNanoSession.PromptInput}";
            EditorStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 204, 0));
        }
        else if (!string.IsNullOrEmpty(_activeNanoSession.StatusMessage))
        {
            EditorStatusText.Text = $"[ {_activeNanoSession.StatusMessage} ]";
            EditorStatusText.Foreground = _currentTheme.ForegroundBrush;
        }
        else
        {
            EditorStatusText.Text = string.Empty;
        }

        int curLineLen = buf.GetLineText(buf.CursorRow).Length;
        int pct = buf.LineCount > 0 ? (int)((buf.CursorRow + 1.0) / buf.LineCount * 100) : 100;
        EditorPositionText.Text = $"line {buf.CursorRow + 1}/{buf.LineCount} ({pct}%), col {buf.CursorCol + 1}/{curLineLen + 1}";

        RenderDocBuffer(buf);
    }

    private void RenderVimView()
    {
        if (_activeVimSession == null) return;

        var buf = _activeVimSession.Buffer;
        EditorLineNumbersBorder.Visibility = _activeVimSession.ShowLineNumbers ? Visibility.Visible : Visibility.Collapsed;

        if (_activeVimSession.ShowLineNumbers)
        {
            var sbNums = new StringBuilder();
            for (int i = 1; i <= buf.LineCount; i++)
            {
                sbNums.AppendLine(i.ToString().PadLeft(4));
            }
            for (int i = 0; i < 15; i++)
            {
                sbNums.AppendLine("   ~");
            }
            EditorLineNumbers.Text = sbNums.ToString();
        }

        if (_activeVimSession.Mode == VimMode.Insert)
        {
            EditorStatusText.Text = "-- INSERT --";
            EditorStatusText.Foreground = new SolidColorBrush(Color.FromRgb(78, 201, 176));
        }
        else if (_activeVimSession.Mode == VimMode.CommandLine)
        {
            EditorStatusText.Text = $":{_activeVimSession.CommandBuffer}";
            EditorStatusText.Foreground = _currentTheme.ForegroundBrush;
        }
        else if (_activeVimSession.Mode == VimMode.Search)
        {
            EditorStatusText.Text = $"/{_activeVimSession.CommandBuffer}";
            EditorStatusText.Foreground = _currentTheme.ForegroundBrush;
        }
        else if (!string.IsNullOrEmpty(_activeVimSession.StatusMessage))
        {
            EditorStatusText.Text = _activeVimSession.StatusMessage;
            EditorStatusText.Foreground = _currentTheme.ForegroundBrush;
        }
        else
        {
            EditorStatusText.Text = string.Empty;
        }

        int pct = buf.LineCount > 0 ? (int)((buf.CursorRow + 1.0) / buf.LineCount * 100) : 100;
        EditorPositionText.Text = $"{buf.CursorRow + 1},{buf.CursorCol + 1}        {pct}%";

        RenderDocBuffer(buf);
    }

    private void RenderDocBuffer(TextBuffer buf)
    {
        EditorDoc.Blocks.Clear();
        for (int r = 0; r < buf.LineCount; r++)
        {
            var p = new Paragraph { Margin = new Thickness(0), LineHeight = 1.3 };
            var line = buf.GetLineText(r);

            if (r == buf.CursorRow)
            {
                int col = Math.Clamp(buf.CursorCol, 0, line.Length);
                if (col > 0)
                {
                    p.Inlines.Add(new Run(line[..col]) { Foreground = _currentTheme.ForegroundBrush });
                }

                char curChar = col < line.Length ? line[col] : ' ';
                p.Inlines.Add(new Run(curChar.ToString())
                {
                    Background = _currentTheme.AccentBrush,
                    Foreground = Brushes.Black,
                    FontWeight = FontWeights.Bold
                });

                if (col + 1 < line.Length)
                {
                    p.Inlines.Add(new Run(line[(col + 1)..]) { Foreground = _currentTheme.ForegroundBrush });
                }
            }
            else
            {
                p.Inlines.Add(new Run(string.IsNullOrEmpty(line) ? " " : line) { Foreground = _currentTheme.ForegroundBrush });
            }

            EditorDoc.Blocks.Add(p);
        }

        if (buf.CursorRow < EditorDoc.Blocks.Count)
        {
            EditorDoc.Blocks.ElementAt(buf.CursorRow).BringIntoView();
        }
    }

    private void CloseEditorOverlay()
    {
        EditorOverlay.Visibility = Visibility.Collapsed;
        _activeNanoSession = null;
        _activeVimSession = null;
        CommandInput.Focus();
        _activeEditorTcs?.TrySetResult(0);
    }

    private void EditorOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        ConsoleModifiers mods = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= ConsoleModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= ConsoleModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= ConsoleModifiers.Alt;

        // Control key combinations
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            var cKey = ConvertWpfKeyToConsoleKey(e.Key);
            if (cKey != ConsoleKey.None)
            {
                e.Handled = true;
                if (_activeNanoSession != null)
                {
                    _activeNanoSession.ProcessKey(cKey, '\0', mods);
                    if (_activeNanoSession.IsExited) CloseEditorOverlay();
                    else RenderNanoView();
                }
                else if (_activeVimSession != null)
                {
                    _activeVimSession.ProcessKey(cKey, '\0', mods);
                    if (_activeVimSession.IsExited) CloseEditorOverlay();
                    else RenderVimView();
                }
            }
            return;
        }

        // Special keys (Enter, Back, Escape, Delete, Tab, Arrows, Navigation)
        var specialKey = ConvertWpfKeyToConsoleKey(e.Key);
        if (specialKey is ConsoleKey.Enter or ConsoleKey.Backspace or ConsoleKey.Delete or ConsoleKey.Escape or ConsoleKey.Tab
            or ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.LeftArrow or ConsoleKey.RightArrow
            or ConsoleKey.Home or ConsoleKey.End or ConsoleKey.PageUp or ConsoleKey.PageDown)
        {
            e.Handled = true;
            char keyChar = specialKey switch
            {
                ConsoleKey.Enter => '\r',
                ConsoleKey.Backspace => '\b',
                ConsoleKey.Tab => '\t',
                _ => '\0'
            };

            if (_activeNanoSession != null)
            {
                _activeNanoSession.ProcessKey(specialKey, keyChar, mods);
                if (_activeNanoSession.IsExited) CloseEditorOverlay();
                else RenderNanoView();
            }
            else if (_activeVimSession != null)
            {
                _activeVimSession.ProcessKey(specialKey, keyChar, mods);
                if (_activeVimSession.IsExited) CloseEditorOverlay();
                else RenderVimView();
            }
        }
    }

    private void EditorOverlay_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;

        e.Handled = true;
        foreach (char c in e.Text)
        {
            if (char.IsControl(c)) continue;

            ConsoleKey consoleKey = c switch
            {
                >= 'a' and <= 'z' => (ConsoleKey)(c - 'a' + (int)ConsoleKey.A),
                >= 'A' and <= 'Z' => (ConsoleKey)(c - 'A' + (int)ConsoleKey.A),
                >= '0' and <= '9' => (ConsoleKey)(c - '0' + (int)ConsoleKey.D0),
                _ => ConsoleKey.None
            };

            ConsoleModifiers mods = 0;
            if (char.IsUpper(c)) mods |= ConsoleModifiers.Shift;

            if (_activeNanoSession != null)
            {
                _activeNanoSession.ProcessKey(consoleKey, c, mods);
                if (_activeNanoSession.IsExited)
                {
                    CloseEditorOverlay();
                    return;
                }
            }
            else if (_activeVimSession != null)
            {
                _activeVimSession.ProcessKey(consoleKey, c, mods);
                if (_activeVimSession.IsExited)
                {
                    CloseEditorOverlay();
                    return;
                }
            }
        }

        if (_activeNanoSession != null) RenderNanoView();
        else if (_activeVimSession != null) RenderVimView();
    }

    private void EditorOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        EditorOverlay.Focus();
    }

    private static ConsoleKey ConvertWpfKeyToConsoleKey(Key key)
    {
        return key switch
        {
            Key.Return => ConsoleKey.Enter,
            Key.Back => ConsoleKey.Backspace,
            Key.Escape => ConsoleKey.Escape,
            Key.Tab => ConsoleKey.Tab,
            Key.Space => ConsoleKey.Spacebar,
            Key.Delete => ConsoleKey.Delete,
            Key.Insert => ConsoleKey.Insert,
            Key.Home => ConsoleKey.Home,
            Key.End => ConsoleKey.End,
            Key.PageUp => ConsoleKey.PageUp,
            Key.PageDown => ConsoleKey.PageDown,
            Key.Up => ConsoleKey.UpArrow,
            Key.Down => ConsoleKey.DownArrow,
            Key.Left => ConsoleKey.LeftArrow,
            Key.Right => ConsoleKey.RightArrow,
            >= Key.A and <= Key.Z => (ConsoleKey)((int)ConsoleKey.A + (key - Key.A)),
            >= Key.D0 and <= Key.D9 => (ConsoleKey)((int)ConsoleKey.D0 + (key - Key.D0)),
            >= Key.NumPad0 and <= Key.NumPad9 => (ConsoleKey)((int)ConsoleKey.NumPad0 + (key - Key.NumPad0)),
            Key.F1 => ConsoleKey.F1,
            Key.F2 => ConsoleKey.F2,
            Key.F3 => ConsoleKey.F3,
            Key.F4 => ConsoleKey.F4,
            Key.F5 => ConsoleKey.F5,
            Key.F6 => ConsoleKey.F6,
            Key.F7 => ConsoleKey.F7,
            Key.F8 => ConsoleKey.F8,
            Key.F9 => ConsoleKey.F9,
            Key.F10 => ConsoleKey.F10,
            Key.F11 => ConsoleKey.F11,
            Key.F12 => ConsoleKey.F12,
            _ => ConsoleKey.None
        };
    }

    #endregion
}
