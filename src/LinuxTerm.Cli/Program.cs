using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Commands;
using LinuxTerm.Core.Common;
using LinuxTerm.Core.Execution;

namespace LinuxTerm.Cli;

public class Program
{
    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        // Enable ANSI virtual terminal processing on Windows console
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var handle = GetStdHandle(STD_OUTPUT_HANDLE);
                if (GetConsoleMode(handle, out uint mode))
                {
                    SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
                }
            }
            catch { }
        }

        var context = new ShellContext(Directory.GetCurrentDirectory());
        var engine = new ShellEngine();

        // Support -c "command" non-interactive mode
        if (args.Length >= 2 && args[0] == "-c")
        {
            var commandLine = string.Join(" ", args.Skip(1));
            return await engine.ExecuteAsync(commandLine, context, Console.In, Console.Out, Console.Error);
        }

        PrintWelcomeBanner();

        var cancelSource = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true; // Prevent app exit on Ctrl+C
            cancelSource.Cancel();
        };

        while (context.IsRunning)
        {
            Console.Write(context.GetPrompt());

            string? input = ReadLineWithHistoryAndCompletion(context, engine.Registry);
            if (input == null) // EOF / Ctrl+D
                break;

            if (string.IsNullOrWhiteSpace(input))
                continue;

            cancelSource = new CancellationTokenSource();

            try
            {
                await engine.ExecuteAsync(input, context, Console.In, Console.Out, Console.Error, cancelSource.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();
            }
        }

        return context.LastExitCode;
    }

    private static void PrintWelcomeBanner()
    {
        Console.WriteLine($"{AnsiText.BrightCyan}======================================================================{AnsiText.Reset}");
        Console.WriteLine($"{AnsiText.Bold}{AnsiText.BrightGreen}  WinLinuxTerm - Windows POSIX Terminal Emulator (C# .NET 10){AnsiText.Reset}");
        Console.WriteLine($"{AnsiText.BrightCyan}======================================================================{AnsiText.Reset}");
        Console.WriteLine($" Type '{AnsiText.BrightYellow}help{AnsiText.Reset}' for built-in Linux commands, '{AnsiText.BrightYellow}exit{AnsiText.Reset}' to quit.");
        Console.WriteLine($" Supports pipes ({AnsiText.BrightMagenta}|{AnsiText.Reset}), redirects ({AnsiText.BrightMagenta}>{AnsiText.Reset}, {AnsiText.BrightMagenta}>>{AnsiText.Reset}), aliases, and native Windows binaries.");
        Console.WriteLine();
    }

    /// <summary>
    /// Interactive line reader with Up/Down history recall and Tab auto-completion.
    /// </summary>
    private static string? ReadLineWithHistoryAndCompletion(ShellContext context, CommandRegistry registry)
    {
        var buffer = new StringBuilder();
        int cursor = 0;
        int historyIndex = context.History.Count;
        string? stash = null;

        while (true)
        {
            var keyInfo = Console.ReadKey(intercept: true);

            if (keyInfo.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            if (keyInfo.Key == ConsoleKey.Escape)
            {
                continue;
            }

            // Ctrl+C
            if (keyInfo.Key == ConsoleKey.C && keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                Console.WriteLine("^C");
                return string.Empty;
            }

            // Ctrl+D on empty line
            if (keyInfo.Key == ConsoleKey.D && keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (buffer.Length == 0)
                {
                    Console.WriteLine("exit");
                    return null;
                }
            }

            // Ctrl+L (clear screen)
            if (keyInfo.Key == ConsoleKey.L && keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                Console.Clear();
                Console.Write(context.GetPrompt());
                Console.Write(buffer.ToString());
                cursor = buffer.Length;
                continue;
            }

            // History navigation: Up Arrow
            if (keyInfo.Key == ConsoleKey.UpArrow)
            {
                if (context.History.Count == 0 || historyIndex <= 0)
                    continue;

                if (historyIndex == context.History.Count)
                    stash = buffer.ToString();

                historyIndex--;
                ReplaceBuffer(buffer, context.History[historyIndex], ref cursor);
                continue;
            }

            // History navigation: Down Arrow
            if (keyInfo.Key == ConsoleKey.DownArrow)
            {
                if (historyIndex >= context.History.Count)
                    continue;

                historyIndex++;
                if (historyIndex == context.History.Count)
                {
                    ReplaceBuffer(buffer, stash ?? string.Empty, ref cursor);
                }
                else
                {
                    ReplaceBuffer(buffer, context.History[historyIndex], ref cursor);
                }
                continue;
            }

            // Left / Right Arrow
            if (keyInfo.Key == ConsoleKey.LeftArrow)
            {
                if (cursor > 0)
                {
                    cursor--;
                    Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
                }
                continue;
            }

            if (keyInfo.Key == ConsoleKey.RightArrow)
            {
                if (cursor < buffer.Length)
                {
                    cursor++;
                    Console.SetCursorPosition(Console.CursorLeft + 1, Console.CursorTop);
                }
                continue;
            }

            // Home / End
            if (keyInfo.Key == ConsoleKey.Home)
            {
                Console.SetCursorPosition(Console.CursorLeft - cursor, Console.CursorTop);
                cursor = 0;
                continue;
            }

            if (keyInfo.Key == ConsoleKey.End)
            {
                Console.SetCursorPosition(Console.CursorLeft + (buffer.Length - cursor), Console.CursorTop);
                cursor = buffer.Length;
                continue;
            }

            // Backspace
            if (keyInfo.Key == ConsoleKey.Backspace)
            {
                if (cursor > 0)
                {
                    cursor--;
                    buffer.Remove(cursor, 1);
                    RedrawFromCursor(buffer, cursor);
                }
                continue;
            }

            // Delete
            if (keyInfo.Key == ConsoleKey.Delete)
            {
                if (cursor < buffer.Length)
                {
                    buffer.Remove(cursor, 1);
                    RedrawFromCursor(buffer, cursor);
                }
                continue;
            }

            // Tab completion
            if (keyInfo.Key == ConsoleKey.Tab)
            {
                PerformCompletion(buffer, ref cursor, context, registry);
                continue;
            }

            // Regular printable character
            if (!char.IsControl(keyInfo.KeyChar))
            {
                buffer.Insert(cursor, keyInfo.KeyChar);
                cursor++;
                RedrawFromCursor(buffer, cursor - 1);
                Console.SetCursorPosition(Console.CursorLeft - (buffer.Length - cursor), Console.CursorTop);
            }
        }
    }

    private static void ReplaceBuffer(StringBuilder buffer, string newText, ref int cursor)
    {
        // Clear old line
        int currentLen = buffer.Length;
        Console.SetCursorPosition(Console.CursorLeft - cursor, Console.CursorTop);
        Console.Write(new string(' ', currentLen));
        Console.SetCursorPosition(Console.CursorLeft - currentLen, Console.CursorTop);

        // Write new line
        buffer.Clear();
        buffer.Append(newText);
        Console.Write(newText);
        cursor = newText.Length;
    }

    private static void RedrawFromCursor(StringBuilder buffer, int fromIdx)
    {
        int originalLeft = Console.CursorLeft;
        int originalTop = Console.CursorTop;

        var tail = buffer.ToString()[fromIdx..];
        Console.Write(tail + " ");
        Console.SetCursorPosition(originalLeft + 1, originalTop);
    }

    private static void PerformCompletion(StringBuilder buffer, ref int cursor, ShellContext context, CommandRegistry registry)
    {
        var text = buffer.ToString()[..cursor];
        int lastSpace = text.LastIndexOfAny(new[] { ' ', '|', ';', '&' });
        string prefix = lastSpace == -1 ? text : text[(lastSpace + 1)..];

        if (string.IsNullOrEmpty(prefix))
            return;

        var candidates = new List<string>();

        if (lastSpace == -1)
        {
            // Command name completion
            candidates.AddRange(registry.GetCommandNames().Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
            candidates.AddRange(context.Aliases.Keys.Where(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
        }

        // File / Directory path completion
        try
        {
            string searchDir = context.CurrentDirectory;
            string filePrefix = prefix;

            if (prefix.Contains('/') || prefix.Contains('\\'))
            {
                var dirPart = Path.GetDirectoryName(prefix.Replace('/', Path.DirectorySeparatorChar));
                if (!string.IsNullOrEmpty(dirPart))
                {
                    searchDir = PosixPathMapper.ToWindows(dirPart, context.CurrentDirectory);
                }
                filePrefix = Path.GetFileName(prefix);
            }

            if (Directory.Exists(searchDir))
            {
                var dirInfo = new DirectoryInfo(searchDir);
                foreach (var entry in dirInfo.GetFileSystemInfos())
                {
                    if (entry.Name.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var completed = entry is DirectoryInfo ? entry.Name + "/" : entry.Name;
                        if (prefix.Contains('/'))
                        {
                            var dirPart = prefix[..prefix.LastIndexOf('/')] + "/";
                            candidates.Add(dirPart + completed);
                        }
                        else
                        {
                            candidates.Add(completed);
                        }
                    }
                }
            }
        }
        catch { }

        if (candidates.Count == 1)
        {
            var match = candidates[0];
            var addition = match[prefix.Length..];
            buffer.Insert(cursor, addition);
            cursor += addition.Length;
            Console.Write(addition);
        }
        else if (candidates.Count > 1)
        {
            Console.WriteLine();
            Console.WriteLine(string.Join("  ", candidates));
            Console.Write(context.GetPrompt());
            Console.Write(buffer.ToString());
            cursor = buffer.Length;
        }
    }
}
