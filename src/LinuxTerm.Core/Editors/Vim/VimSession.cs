using System;
using System.IO;
using System.Text;
using System.Threading;
using LinuxTerm.Core.Common;

namespace LinuxTerm.Core.Editors.Vim;

public enum VimMode
{
    Normal,
    Insert,
    CommandLine,
    Search
}

public class VimSession
{
    public TextBuffer Buffer { get; }
    public VimMode Mode { get; set; } = VimMode.Normal;
    public string? StatusMessage { get; set; }
    public bool IsExited { get; set; }
    public bool ShowLineNumbers { get; set; } = true;

    public StringBuilder CommandBuffer { get; } = new();

    private char _pendingNormalKey = '\0';
    private string? _lastSearchQuery;

    public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();

    // Viewport
    public int TopRow { get; set; }
    public int LeftCol { get; set; }

    public static (int Height, int Width) GetConsoleSize()
    {
        try
        {
            if (!Console.IsOutputRedirected && Console.WindowHeight > 0 && Console.WindowWidth > 0)
                return (Console.WindowHeight, Console.WindowWidth);
        }
        catch { }
        return (24, 80);
    }

    public VimSession(string? filePath = null)
    {
        Buffer = new TextBuffer(filePath);
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            StatusMessage = $"\"{Path.GetFileName(filePath)}\" {Buffer.LineCount}L";
        }
        else if (!string.IsNullOrEmpty(filePath))
        {
            StatusMessage = $"\"{Path.GetFileName(filePath)}\" [New File]";
        }
    }

    public void ProcessKey(ConsoleKey key, char keyChar, ConsoleModifiers modifiers)
    {
        switch (Mode)
        {
            case VimMode.Insert:
                HandleInsertKey(key, keyChar, modifiers);
                break;
            case VimMode.CommandLine:
                HandleCommandLineKey(key, keyChar, modifiers);
                break;
            case VimMode.Search:
                HandleSearchKey(key, keyChar, modifiers);
                break;
            case VimMode.Normal:
            default:
                HandleNormalKey(key, keyChar, modifiers);
                break;
        }

        var (h, w) = GetConsoleSize();
        AdjustViewport(h, w);
    }

    private void HandleNormalKey(ConsoleKey key, char keyChar, ConsoleModifiers modifiers)
    {
        StatusMessage = null;

        // Ctrl+R Redo
        if (modifiers.HasFlag(ConsoleModifiers.Control) && key == ConsoleKey.R)
        {
            if (Buffer.Redo()) StatusMessage = "1 change; after #1";
            else StatusMessage = "Already at newest change";
            return;
        }

        // Pending 2-key sequence handling (dd, yy, gg, ZZ)
        if (_pendingNormalKey != '\0')
        {
            char first = _pendingNormalKey;
            _pendingNormalKey = '\0';

            if (first == 'd' && keyChar == 'd')
            {
                Buffer.DeleteLine(Buffer.CursorRow);
                StatusMessage = "1 line less";
                return;
            }
            if (first == 'y' && keyChar == 'y')
            {
                Buffer.YankLine(Buffer.CursorRow);
                StatusMessage = "1 line yanked";
                return;
            }
            if (first == 'g' && keyChar == 'g')
            {
                Buffer.MoveFileStart();
                return;
            }
            if (first == 'Z' && keyChar == 'Z')
            {
                try { Buffer.SaveToFile(); } catch { }
                IsExited = true;
                return;
            }
        }

        // Check for 2-key sequence starts
        if (keyChar is 'd' or 'y' or 'g' or 'Z')
        {
            _pendingNormalKey = keyChar;
            return;
        }

        switch (key)
        {
            // Motions
            case ConsoleKey.LeftArrow:
            case ConsoleKey.H when keyChar == 'h':
                Buffer.MoveLeft();
                break;

            case ConsoleKey.DownArrow:
            case ConsoleKey.J when keyChar == 'j':
                Buffer.MoveDown();
                break;

            case ConsoleKey.UpArrow:
            case ConsoleKey.K when keyChar == 'k':
                Buffer.MoveUp();
                break;

            case ConsoleKey.RightArrow:
            case ConsoleKey.L when keyChar == 'l':
                Buffer.MoveRight();
                break;

            case ConsoleKey.Home:
            case ConsoleKey.D0 when keyChar == '0':
                Buffer.MoveLineStart();
                break;

            case ConsoleKey.D6 when keyChar == '^':
                Buffer.MoveFirstNonWhitespace();
                break;

            case ConsoleKey.End:
            case ConsoleKey.D4 when keyChar == '$':
                Buffer.MoveLineEnd();
                break;

            case ConsoleKey.W when keyChar == 'w':
                Buffer.MoveWordForward();
                break;

            case ConsoleKey.B when keyChar == 'b':
                Buffer.MoveWordBackward();
                break;

            case ConsoleKey.G when keyChar == 'G':
                Buffer.MoveFileEnd();
                break;

            // Entering Insert Mode
            case ConsoleKey.I when keyChar == 'i':
                Mode = VimMode.Insert;
                break;

            case ConsoleKey.A when keyChar == 'a':
                Buffer.MoveRight();
                Mode = VimMode.Insert;
                break;

            case ConsoleKey.I when keyChar == 'I':
                Buffer.MoveLineStart();
                Mode = VimMode.Insert;
                break;

            case ConsoleKey.A when keyChar == 'A':
                Buffer.MoveLineEnd();
                Mode = VimMode.Insert;
                break;

            case ConsoleKey.O when keyChar == 'o':
                Buffer.MoveLineEnd();
                Buffer.InsertNewline();
                Mode = VimMode.Insert;
                break;

            case ConsoleKey.O when keyChar == 'O':
                Buffer.MoveLineStart();
                Buffer.InsertNewline();
                Buffer.MoveUp();
                Mode = VimMode.Insert;
                break;

            // Quick Edits
            case ConsoleKey.X when keyChar == 'x':
                Buffer.DeleteForwards();
                break;

            case ConsoleKey.D when keyChar == 'D':
                Buffer.DeleteToLineEnd();
                break;

            case ConsoleKey.P when keyChar == 'p':
                Buffer.PasteLine(below: true);
                break;

            case ConsoleKey.P when keyChar == 'P':
                Buffer.PasteLine(below: false);
                break;

            case ConsoleKey.U when keyChar == 'u':
                if (Buffer.Undo()) StatusMessage = "1 change; before #1";
                else StatusMessage = "Already at oldest change";
                break;

            // Search next
            case ConsoleKey.N when keyChar == 'n':
                if (!string.IsNullOrEmpty(_lastSearchQuery))
                    Buffer.Search(_lastSearchQuery, forward: true);
                break;

            case ConsoleKey.N when keyChar == 'N':
                if (!string.IsNullOrEmpty(_lastSearchQuery))
                    Buffer.Search(_lastSearchQuery, forward: false);
                break;

            // Transitions to Command/Search Mode
            default:
                if (keyChar == ':')
                {
                    Mode = VimMode.CommandLine;
                    CommandBuffer.Clear();
                }
                else if (keyChar == '/')
                {
                    Mode = VimMode.Search;
                    CommandBuffer.Clear();
                }
                break;
        }
    }

    private void HandleInsertKey(ConsoleKey key, char keyChar, ConsoleModifiers modifiers)
    {
        if (key == ConsoleKey.Escape)
        {
            Mode = VimMode.Normal;
            Buffer.MoveLeft();
            return;
        }

        switch (key)
        {
            case ConsoleKey.Enter:
                Buffer.InsertNewline();
                break;
            case ConsoleKey.Backspace:
                Buffer.DeleteBackwards();
                break;
            case ConsoleKey.Delete:
                Buffer.DeleteForwards();
                break;
            case ConsoleKey.Tab:
                Buffer.InsertText("    ");
                break;
            case ConsoleKey.UpArrow: Buffer.MoveUp(); break;
            case ConsoleKey.DownArrow: Buffer.MoveDown(); break;
            case ConsoleKey.LeftArrow: Buffer.MoveLeft(); break;
            case ConsoleKey.RightArrow: Buffer.MoveRight(); break;
            default:
                if (!char.IsControl(keyChar))
                {
                    Buffer.InsertChar(keyChar);
                }
                break;
        }
    }

    private void HandleCommandLineKey(ConsoleKey key, char keyChar, ConsoleModifiers modifiers)
    {
        if (key == ConsoleKey.Escape)
        {
            Mode = VimMode.Normal;
            CommandBuffer.Clear();
            return;
        }

        if (key == ConsoleKey.Enter)
        {
            var cmd = CommandBuffer.ToString().Trim();
            Mode = VimMode.Normal;
            CommandBuffer.Clear();
            ExecuteColonCommand(cmd);
            return;
        }

        if (key == ConsoleKey.Backspace)
        {
            if (CommandBuffer.Length > 0)
            {
                CommandBuffer.Remove(CommandBuffer.Length - 1, 1);
            }
            else
            {
                Mode = VimMode.Normal;
            }
            return;
        }

        if (!char.IsControl(keyChar))
        {
            CommandBuffer.Append(keyChar);
        }
    }

    private void HandleSearchKey(ConsoleKey key, char keyChar, ConsoleModifiers modifiers)
    {
        if (key == ConsoleKey.Escape)
        {
            Mode = VimMode.Normal;
            CommandBuffer.Clear();
            return;
        }

        if (key == ConsoleKey.Enter)
        {
            _lastSearchQuery = CommandBuffer.ToString();
            Mode = VimMode.Normal;
            CommandBuffer.Clear();
            if (!string.IsNullOrEmpty(_lastSearchQuery))
            {
                if (!Buffer.Search(_lastSearchQuery, forward: true))
                {
                    StatusMessage = $"E486: Pattern not found: {_lastSearchQuery}";
                }
            }
            return;
        }

        if (key == ConsoleKey.Backspace)
        {
            if (CommandBuffer.Length > 0) CommandBuffer.Remove(CommandBuffer.Length - 1, 1);
            else Mode = VimMode.Normal;
            return;
        }

        if (!char.IsControl(keyChar))
        {
            CommandBuffer.Append(keyChar);
        }
    }

    public void ExecuteColonCommand(string cmd)
    {
        if (string.IsNullOrEmpty(cmd)) return;

        if (cmd == "q")
        {
            if (Buffer.IsModified)
            {
                StatusMessage = "E37: No write since last change (add ! to override)";
            }
            else
            {
                IsExited = true;
            }
        }
        else if (cmd == "q!")
        {
            IsExited = true;
        }
        else if (int.TryParse(cmd, out int targetLine))
        {
            Buffer.CursorRow = Math.Clamp(targetLine - 1, 0, Math.Max(0, Buffer.LineCount - 1));
            Buffer.CursorCol = 0;
        }
        else if (cmd is "help" or "h")
        {
            StatusMessage = "Vim: i:Insert, Esc:Normal, :w Write, :q Quit, :wq Save&Quit, /find";
        }
        else if (cmd is "w" or "write")
        {
            if (string.IsNullOrEmpty(Buffer.FilePath))
            {
                StatusMessage = "E32: No file name";
            }
            else
            {
                try
                {
                    var resolved = Path.IsPathRooted(Buffer.FilePath) ? Buffer.FilePath : Path.Combine(WorkingDirectory, Buffer.FilePath);
                    Buffer.SaveToFile(resolved);
                    StatusMessage = $"\"{Path.GetFileName(Buffer.FilePath)}\" {Buffer.LineCount}L written";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"E212: Can't open file for writing: {ex.Message}";
                }
            }
        }
        else if (cmd.StartsWith("w ") || cmd.StartsWith("write "))
        {
            var target = cmd.Substring(cmd.IndexOf(' ') + 1).Trim();
            try
            {
                var resolved = Path.IsPathRooted(target) ? target : Path.Combine(WorkingDirectory, target);
                Buffer.SaveToFile(resolved);
                StatusMessage = $"\"{target}\" [New] {Buffer.LineCount}L written";
            }
            catch (Exception ex)
            {
                StatusMessage = $"E212: Can't open file for writing: {ex.Message}";
            }
        }
        else if (cmd is "wq" or "x")
        {
            try
            {
                var target = Buffer.FilePath ?? "untitled.txt";
                var resolved = Path.IsPathRooted(target) ? target : Path.Combine(WorkingDirectory, target);
                Buffer.SaveToFile(resolved);
                IsExited = true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"E212: Can't open file for writing: {ex.Message}";
            }
        }
        else if (cmd is "set nu" or "set number")
        {
            ShowLineNumbers = true;
        }
        else if (cmd is "set nonu" or "set nonumber")
        {
            ShowLineNumbers = false;
        }
        else
        {
            StatusMessage = $"E492: Not an editor command: {cmd}";
        }
    }

    public void AdjustViewport(int height, int width)
    {
        int editHeight = Math.Max(1, height - 1);
        int lineNumWidth = ShowLineNumbers ? 5 : 0;
        int editWidth = Math.Max(1, width - lineNumWidth);

        if (Buffer.CursorRow < TopRow)
            TopRow = Buffer.CursorRow;
        else if (Buffer.CursorRow >= TopRow + editHeight)
            TopRow = Buffer.CursorRow - editHeight + 1;

        if (Buffer.CursorCol < LeftCol)
            LeftCol = Buffer.CursorCol;
        else if (Buffer.CursorCol >= LeftCol + editWidth)
            LeftCol = Buffer.CursorCol - editWidth + 1;
    }

    public void RunInteractiveConsole(CancellationToken ct = default)
    {
        Console.Write("\x1b[?1049h\x1b[2J");
        Console.CursorVisible = true;

        try
        {
            while (!IsExited && !ct.IsCancellationRequested)
            {
                RenderConsole();
                var keyInfo = Console.ReadKey(intercept: true);
                ProcessKey(keyInfo.Key, keyInfo.KeyChar, keyInfo.Modifiers);
            }
        }
        finally
        {
            Console.Write("\x1b[?1049l\x1b[2J\x1b[H");
        }
    }

    private void RenderConsole()
    {
        var (height, width) = GetConsoleSize();
        AdjustViewport(height, width);

        var sb = new StringBuilder();
        sb.Append("\x1b[H");

        int lineNumWidth = ShowLineNumbers ? 5 : 0;
        int editHeight = Math.Max(1, height - 1);

        for (int r = 0; r < editHeight; r++)
        {
            int lineIdx = TopRow + r;
            if (lineIdx < Buffer.LineCount)
            {
                if (ShowLineNumbers)
                {
                    sb.Append($"\x1b[33m{lineIdx + 1,4} \x1b[0m");
                }
                var raw = Buffer.GetLineText(lineIdx);
                string content = "";
                if (LeftCol < raw.Length)
                {
                    content = raw[LeftCol..];
                    int available = width - lineNumWidth;
                    if (content.Length > available) content = content[..available];
                }
                sb.Append($"{content.PadRight(width - lineNumWidth)}\r\n");
            }
            else
            {
                sb.Append($"\x1b[34m~    \x1b[0m{"".PadRight(width - lineNumWidth)}\r\n");
            }
        }

        // Bottom status line
        string bottomText = "";
        if (Mode == VimMode.Insert)
        {
            bottomText = "\x1b[1m-- INSERT --\x1b[0m";
        }
        else if (Mode == VimMode.CommandLine)
        {
            bottomText = $":{CommandBuffer}";
        }
        else if (Mode == VimMode.Search)
        {
            bottomText = $"/{CommandBuffer}";
        }
        else if (!string.IsNullOrEmpty(StatusMessage))
        {
            bottomText = StatusMessage;
        }

        int colDisplay = Buffer.CursorCol + 1;
        int rowDisplay = Buffer.CursorRow + 1;
        int pct = Buffer.LineCount > 0 ? (int)((double)rowDisplay / Buffer.LineCount * 100) : 100;
        string rightStatus = $"{rowDisplay},{colDisplay}        {pct}%";

        int plainBottomLen = AnsiText.StripAnsi(bottomText).Length;
        int padding = Math.Max(1, width - plainBottomLen - rightStatus.Length - 1);
        sb.Append($"{bottomText}{new string(' ', padding)}{rightStatus}");

        Console.Write(sb.ToString());

        // Position terminal cursor
        int targetRow = (Mode is VimMode.CommandLine or VimMode.Search) ? height - 1 : Math.Clamp(Buffer.CursorRow - TopRow, 0, height - 2);
        int targetCol;
        if (Mode == VimMode.CommandLine)
        {
            targetCol = Math.Clamp(1 + CommandBuffer.Length, 0, width - 1);
        }
        else if (Mode == VimMode.Search)
        {
            targetCol = Math.Clamp(1 + CommandBuffer.Length, 0, width - 1);
        }
        else
        {
            targetCol = Math.Clamp(lineNumWidth + (Buffer.CursorCol - LeftCol), 0, width - 1);
        }

        Console.SetCursorPosition(targetCol, targetRow);
    }
}
