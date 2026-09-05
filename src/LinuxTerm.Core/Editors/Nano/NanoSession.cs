using System;
using System.IO;
using System.Text;
using System.Threading;
using LinuxTerm.Core.Common;

namespace LinuxTerm.Core.Editors.Nano;

public class NanoSession
{
    public TextBuffer Buffer { get; }
    public string? StatusMessage { get; set; }
    public bool IsExited { get; set; }

    // Prompt state
    public bool IsInPrompt { get; private set; }
    public string PromptLabel { get; private set; } = string.Empty;
    public StringBuilder PromptInput { get; } = new();
    private Action<string>? _promptAction;

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

    public NanoSession(string? filePath = null)
    {
        Buffer = new TextBuffer(filePath);
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            StatusMessage = $"[ Read {Buffer.LineCount} lines ]";
        }
        else if (!string.IsNullOrEmpty(filePath))
        {
            StatusMessage = "[ New File ]";
        }
    }

    public void AskPrompt(string label, string initialValue, Action<string> onConfirm)
    {
        IsInPrompt = true;
        PromptLabel = label;
        PromptInput.Clear();
        PromptInput.Append(initialValue);
        _promptAction = onConfirm;
    }

    public void CancelPrompt()
    {
        IsInPrompt = false;
        PromptLabel = string.Empty;
        PromptInput.Clear();
        _promptAction = null;
        StatusMessage = "[ Cancelled ]";
    }

    public void ProcessKey(ConsoleKey key, char keyChar, ConsoleModifiers modifiers)
    {
        if (IsInPrompt)
        {
            HandlePromptKey(key, keyChar, modifiers);
            return;
        }

        // Ctrl Shortcuts
        if (modifiers.HasFlag(ConsoleModifiers.Control))
        {
            switch (key)
            {
                case ConsoleKey.G: // Help
                    StatusMessage = "[ nano: ^O Write, ^X Exit, ^K Cut, ^U Paste, ^W WhereIs, ^C Cursor ]";
                    return;

                case ConsoleKey.O: // Write Out
                    AskPrompt("File Name to Write: ", Buffer.FilePath ?? "untitled.txt", targetPath =>
                    {
                        try
                        {
                            var resolved = Path.IsPathRooted(targetPath) ? targetPath : Path.Combine(WorkingDirectory, targetPath);
                            Buffer.SaveToFile(resolved);
                            StatusMessage = $"[ Wrote {Buffer.LineCount} lines ]";
                        }
                        catch (Exception ex)
                        {
                            StatusMessage = $"[ Error writing {targetPath}: {ex.Message} ]";
                        }
                    });
                    return;

                case ConsoleKey.X: // Exit
                    if (Buffer.IsModified)
                    {
                        AskPrompt("Save modified buffer? (Y/N/C) ", "Y", answer =>
                        {
                            var a = answer.Trim().ToUpperInvariant();
                            if (a == "Y")
                            {
                                try
                                {
                                    Buffer.SaveToFile();
                                    IsExited = true;
                                }
                                catch (Exception ex)
                                {
                                    StatusMessage = $"[ Error: {ex.Message} ]";
                                }
                            }
                            else if (a == "N")
                            {
                                IsExited = true;
                            }
                        });
                    }
                    else
                    {
                        IsExited = true;
                    }
                    return;

                case ConsoleKey.K: // Cut Line
                    Buffer.DeleteLine(Buffer.CursorRow);
                    StatusMessage = "[ Cut 1 line ]";
                    return;

                case ConsoleKey.U: // Uncut / Paste Line
                    Buffer.PasteLine(below: false);
                    StatusMessage = "[ Pasted 1 line ]";
                    return;

                case ConsoleKey.W: // Where Is
                    AskPrompt("Search: ", "", query =>
                    {
                        if (Buffer.Search(query, forward: true))
                        {
                            StatusMessage = $"[ Found '{query}' ]";
                        }
                        else
                        {
                            StatusMessage = $"[ '{query}' not found ]";
                        }
                    });
                    return;

                case ConsoleKey.C: // Cursor Position
                    int curLineLen = Buffer.GetLineText(Buffer.CursorRow).Length;
                    StatusMessage = $"[ line {Buffer.CursorRow + 1}/{Buffer.LineCount} ({(int)((Buffer.CursorRow + 1.0) / Math.Max(1, Buffer.LineCount) * 100)}%), col {Buffer.CursorCol + 1}/{curLineLen + 1} ]";
                    return;
            }
            return;
        }

        // Regular Navigation & Editing
        switch (key)
        {
            case ConsoleKey.UpArrow: Buffer.MoveUp(); break;
            case ConsoleKey.DownArrow: Buffer.MoveDown(); break;
            case ConsoleKey.LeftArrow: Buffer.MoveLeft(); break;
            case ConsoleKey.RightArrow: Buffer.MoveRight(); break;
            case ConsoleKey.Home: Buffer.MoveLineStart(); break;
            case ConsoleKey.End: Buffer.MoveLineEnd(); break;
            case ConsoleKey.PageUp:
                for (int i = 0; i < 15; i++) Buffer.MoveUp();
                break;
            case ConsoleKey.PageDown:
                for (int i = 0; i < 15; i++) Buffer.MoveDown();
                break;
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
            default:
                if (!char.IsControl(keyChar))
                {
                    Buffer.InsertChar(keyChar);
                }
                break;
        }

        var (h, w) = GetConsoleSize();
        AdjustViewport(h, w);
    }

    private void HandlePromptKey(ConsoleKey key, char keyChar, ConsoleModifiers modifiers)
    {
        if (key == ConsoleKey.Escape || (key == ConsoleKey.C && modifiers.HasFlag(ConsoleModifiers.Control)))
        {
            CancelPrompt();
            return;
        }

        if (key == ConsoleKey.Enter)
        {
            var action = _promptAction;
            var text = PromptInput.ToString();
            IsInPrompt = false;
            _promptAction = null;
            action?.Invoke(text);
            return;
        }

        if (key == ConsoleKey.Backspace && PromptInput.Length > 0)
        {
            PromptInput.Remove(PromptInput.Length - 1, 1);
            return;
        }

        if (!char.IsControl(keyChar))
        {
            PromptInput.Append(keyChar);
        }
    }

    public void AdjustViewport(int height, int width)
    {
        int editHeight = Math.Max(1, height - 4);
        int editWidth = Math.Max(1, width);

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
        // Switch to alternate buffer and hide cursor blink delay
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
            // Restore main screen buffer
            Console.Write("\x1b[?1049l\x1b[2J\x1b[H");
        }
    }

    private void RenderConsole()
    {
        var (height, width) = GetConsoleSize();
        AdjustViewport(height, width);

        var sb = new StringBuilder();
        sb.Append("\x1b[H"); // Move cursor to top-left

        // 1. Header Line (Row 0)
        string title = " GNU nano 8.0 ";
        string fileName = Path.GetFileName(Buffer.FilePath ?? "New Buffer");
        string modified = Buffer.IsModified ? "Modified" : "";
        string fileStr = $"File: {fileName}";

        int leftPadding = Math.Max(0, (width - title.Length - fileStr.Length - modified.Length - 4) / 2);
        string headerText = $"{title}{new string(' ', leftPadding)}{fileStr}{new string(' ', leftPadding)}{modified}";
        if (headerText.Length < width) headerText = headerText.PadRight(width);
        else headerText = headerText[..width];

        sb.Append($"\x1b[7m{headerText}\x1b[0m\r\n");

        // 2. Text Canvas (Rows 1 to height - 3)
        int editHeight = Math.Max(1, height - 4);
        for (int r = 0; r < editHeight; r++)
        {
            int lineIdx = TopRow + r;
            string lineContent = "";
            if (lineIdx < Buffer.LineCount)
            {
                var raw = Buffer.GetLineText(lineIdx);
                if (LeftCol < raw.Length)
                {
                    lineContent = raw[LeftCol..];
                    if (lineContent.Length > width) lineContent = lineContent[..width];
                }
            }
            sb.Append($"{lineContent.PadRight(width)}\r\n");
        }

        // 3. Status/Message Bar (Row height - 3)
        string statusText = "";
        if (IsInPrompt)
        {
            statusText = $"{PromptLabel}{PromptInput}";
        }
        else if (!string.IsNullOrEmpty(StatusMessage))
        {
            statusText = $"[ {StatusMessage} ]";
        }
        sb.Append($"{statusText.PadRight(width)}\r\n");

        // 4. Shortcut Help Bar (Rows height - 2 and height - 1)
        string bar1 = "^G Help     ^O WriteOut     ^W Where Is     ^K Cut Text".PadRight(width);
        string bar2 = "^X Exit     ^R Read File    ^\\ Replace      ^U Paste Text".PadRight(width);
        sb.Append($"\x1b[7m{bar1[..Math.Min(width, bar1.Length)]}\x1b[0m\r\n");
        sb.Append($"\x1b[7m{bar2[..Math.Min(width, bar2.Length)]}\x1b[0m");

        Console.Write(sb.ToString());

        // Position terminal cursor
        int targetScreenRow = IsInPrompt ? height - 3 : Math.Clamp(Buffer.CursorRow - TopRow + 1, 1, height - 3);
        int targetScreenCol = IsInPrompt ? Math.Clamp(PromptLabel.Length + PromptInput.Length, 0, width - 1) : Math.Clamp(Buffer.CursorCol - LeftCol, 0, width - 1);

        Console.SetCursorPosition(targetScreenCol, targetScreenRow);
    }
}
