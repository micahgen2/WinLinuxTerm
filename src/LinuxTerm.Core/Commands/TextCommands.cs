using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Common;

namespace LinuxTerm.Core.Commands;

public class CatCommand : ICommand
{
    public string Name => "cat";
    public string Description => "Concatenate FILE(s) to standard output";
    public string Synopsis => "cat [-n] [-b] [FILE]...";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool numberAll = false;
        bool numberNonBlank = false;
        var files = new List<string>();

        foreach (var arg in args)
        {
            if (arg == "-n") numberAll = true;
            else if (arg == "-b") numberNonBlank = true;
            else if (!arg.StartsWith('-') || arg == "-") files.Add(arg);
        }

        if (files.Count == 0)
            files.Add("-");

        int lineCounter = 1;
        int exitCode = 0;

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;

            if (file == "-")
            {
                string? line;
                while ((line = await stdin.ReadLineAsync(ct)) != null)
                {
                    OutputLine(line, ref lineCounter, numberAll, numberNonBlank, stdout);
                }
            }
            else
            {
                var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
                if (!File.Exists(winPath))
                {
                    await stderr.WriteLineAsync($"cat: {file}: No such file or directory");
                    exitCode = 1;
                    continue;
                }

                try
                {
                    using var reader = new StreamReader(winPath);
                    string? line;
                    while ((line = await reader.ReadLineAsync(ct)) != null)
                    {
                        OutputLine(line, ref lineCounter, numberAll, numberNonBlank, stdout);
                    }
                }
                catch (Exception ex)
                {
                    await stderr.WriteLineAsync($"cat: {file}: {ex.Message}");
                    exitCode = 1;
                }
            }
        }

        return exitCode;
    }

    private static void OutputLine(string line, ref int counter, bool numberAll, bool numberNonBlank, TextWriter stdout)
    {
        if (numberNonBlank)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                stdout.WriteLine($"{counter++,6}\t{line}");
            }
            else
            {
                stdout.WriteLine(line);
            }
        }
        else if (numberAll)
        {
            stdout.WriteLine($"{counter++,6}\t{line}");
        }
        else
        {
            stdout.WriteLine(line);
        }
    }
}

public class GrepCommand : ICommand
{
    public string Name => "grep";
    public string Description => "Print lines matching a pattern";
    public string Synopsis => "grep [-i] [-v] [-n] [-r|-R] [-c] [-E] PATTERN [FILE]...";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool ignoreCase = false;
        bool invertMatch = false;
        bool lineNumbers = false;
        bool recursive = false;
        bool countOnly = false;
        string? pattern = null;
        var targets = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith('-') && arg.Length > 1 && !arg.StartsWith("--"))
            {
                foreach (char c in arg[1..])
                {
                    switch (c)
                    {
                        case 'i': ignoreCase = true; break;
                        case 'v': invertMatch = true; break;
                        case 'n': lineNumbers = true; break;
                        case 'r':
                        case 'R': recursive = true; break;
                        case 'c': countOnly = true; break;
                        case 'E': break; // Default uses regex
                    }
                }
            }
            else if (pattern == null)
            {
                pattern = arg;
            }
            else
            {
                targets.Add(arg);
            }
        }

        if (pattern == null)
        {
            await stderr.WriteLineAsync("grep: missing pattern");
            return 2;
        }

        Regex regex;
        try
        {
            var options = RegexOptions.Compiled;
            if (ignoreCase) options |= RegexOptions.IgnoreCase;
            regex = new Regex(pattern, options);
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"grep: invalid regular expression '{pattern}': {ex.Message}");
            return 2;
        }

        int totalMatches = 0;

        if (targets.Count == 0)
        {
            // Read from standard input
            int lineNum = 1;
            int matches = 0;
            string? line;
            while ((line = await stdin.ReadLineAsync(ct)) != null)
            {
                bool isMatch = regex.IsMatch(line);
                if (invertMatch) isMatch = !isMatch;

                if (isMatch)
                {
                    matches++;
                    if (!countOnly)
                    {
                        string outputLine = invertMatch ? line : regex.Replace(line, m => $"{AnsiText.Bold}{AnsiText.Red}{m.Value}{AnsiText.Reset}");
                        if (lineNumbers)
                            stdout.WriteLine($"{AnsiText.Green}{lineNum}{AnsiText.Reset}:{outputLine}");
                        else
                            stdout.WriteLine(outputLine);
                    }
                }
                lineNum++;
            }

            if (countOnly) stdout.WriteLine(matches);
            totalMatches += matches;
        }
        else
        {
            bool showFilename = targets.Count > 1 || recursive;

            foreach (var target in targets)
            {
                var winPath = PosixPathMapper.ToWindows(target, context.CurrentDirectory);
                if (Directory.Exists(winPath))
                {
                    if (!recursive)
                    {
                        await stderr.WriteLineAsync($"grep: {target}: Is a directory");
                        continue;
                    }

                    totalMatches += await SearchDirectoryAsync(winPath, target, regex, invertMatch, lineNumbers, countOnly, stdout, stderr, ct);
                }
                else if (File.Exists(winPath))
                {
                    totalMatches += await SearchFileAsync(winPath, target, showFilename, regex, invertMatch, lineNumbers, countOnly, stdout, ct);
                }
                else
                {
                    await stderr.WriteLineAsync($"grep: {target}: No such file or directory");
                }
            }
        }

        return totalMatches > 0 ? 0 : 1;
    }

    private static async Task<int> SearchFileAsync(string winPath, string displayPath, bool showFilename, Regex regex, bool invert, bool lineNumbers, bool countOnly, TextWriter stdout, CancellationToken ct)
    {
        int matches = 0;
        int lineNum = 1;

        try
        {
            using var reader = new StreamReader(winPath);
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                bool isMatch = regex.IsMatch(line);
                if (invert) isMatch = !isMatch;

                if (isMatch)
                {
                    matches++;
                    if (!countOnly)
                    {
                        string outputLine = invert ? line : regex.Replace(line, m => $"{AnsiText.Bold}{AnsiText.Red}{m.Value}{AnsiText.Reset}");
                        var prefix = new StringBuilder();
                        if (showFilename) prefix.Append($"{AnsiText.Magenta}{displayPath}{AnsiText.Reset}:");
                        if (lineNumbers) prefix.Append($"{AnsiText.Green}{lineNum}{AnsiText.Reset}:");

                        stdout.WriteLine($"{prefix}{outputLine}");
                    }
                }
                lineNum++;
            }

            if (countOnly)
            {
                if (showFilename) stdout.WriteLine($"{displayPath}:{matches}");
                else stdout.WriteLine(matches);
            }
        }
        catch { }

        return matches;
    }

    private static async Task<int> SearchDirectoryAsync(string dirWinPath, string displayDir, Regex regex, bool invert, bool lineNumbers, bool countOnly, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        int total = 0;
        try
        {
            foreach (var file in Directory.GetFiles(dirWinPath, "*", SearchOption.AllDirectories))
            {
                if (ct.IsCancellationRequested) break;
                var relative = Path.GetRelativePath(dirWinPath, file).Replace('\\', '/');
                var displayPath = $"{displayDir}/{relative}".Replace("//", "/");
                total += await SearchFileAsync(file, displayPath, true, regex, invert, lineNumbers, countOnly, stdout, ct);
            }
        }
        catch { }

        return total;
    }
}

public class HeadCommand : ICommand
{
    public string Name => "head";
    public string Description => "Output the first part of files";
    public string Synopsis => "head [-n count] [FILE]...";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        int count = 10;
        var files = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-n" && i + 1 < args.Length)
            {
                int.TryParse(args[++i], out count);
            }
            else if (args[i].StartsWith('-') && int.TryParse(args[i][1..], out int n))
            {
                count = n;
            }
            else if (!args[i].StartsWith('-') || args[i] == "-")
            {
                files.Add(args[i]);
            }
        }

        if (files.Count == 0) files.Add("-");

        int exitCode = 0;
        bool multiple = files.Count > 1;

        for (int f = 0; f < files.Count; f++)
        {
            var file = files[f];
            if (multiple)
            {
                if (f > 0) stdout.WriteLine();
                stdout.WriteLine($"==> {file} <==");
            }

            if (file == "-")
            {
                for (int i = 0; i < count; i++)
                {
                    var line = await stdin.ReadLineAsync(ct);
                    if (line == null) break;
                    stdout.WriteLine(line);
                }
            }
            else
            {
                var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
                if (!File.Exists(winPath))
                {
                    await stderr.WriteLineAsync($"head: cannot open '{file}' for reading: No such file or directory");
                    exitCode = 1;
                    continue;
                }

                try
                {
                    using var reader = new StreamReader(winPath);
                    for (int i = 0; i < count; i++)
                    {
                        var line = await reader.ReadLineAsync(ct);
                        if (line == null) break;
                        stdout.WriteLine(line);
                    }
                }
                catch (Exception ex)
                {
                    await stderr.WriteLineAsync($"head: error reading '{file}': {ex.Message}");
                    exitCode = 1;
                }
            }
        }

        return exitCode;
    }
}

public class TailCommand : ICommand
{
    public string Name => "tail";
    public string Description => "Output the last part of files";
    public string Synopsis => "tail [-n count] [FILE]...";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        int count = 10;
        var files = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-n" && i + 1 < args.Length)
            {
                int.TryParse(args[++i], out count);
            }
            else if (args[i].StartsWith('-') && int.TryParse(args[i][1..], out int n))
            {
                count = n;
            }
            else if (!args[i].StartsWith('-') || args[i] == "-")
            {
                files.Add(args[i]);
            }
        }

        if (files.Count == 0) files.Add("-");

        int exitCode = 0;
        bool multiple = files.Count > 1;

        for (int f = 0; f < files.Count; f++)
        {
            var file = files[f];
            if (multiple)
            {
                if (f > 0) stdout.WriteLine();
                stdout.WriteLine($"==> {file} <==");
            }

            var lines = new Queue<string>();

            if (file == "-")
            {
                string? line;
                while ((line = await stdin.ReadLineAsync(ct)) != null)
                {
                    lines.Enqueue(line);
                    if (lines.Count > count) lines.Dequeue();
                }
            }
            else
            {
                var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
                if (!File.Exists(winPath))
                {
                    await stderr.WriteLineAsync($"tail: cannot open '{file}' for reading: No such file or directory");
                    exitCode = 1;
                    continue;
                }

                try
                {
                    using var reader = new StreamReader(winPath);
                    string? line;
                    while ((line = await reader.ReadLineAsync(ct)) != null)
                    {
                        lines.Enqueue(line);
                        if (lines.Count > count) lines.Dequeue();
                    }
                }
                catch (Exception ex)
                {
                    await stderr.WriteLineAsync($"tail: error reading '{file}': {ex.Message}");
                    exitCode = 1;
                    continue;
                }
            }

            foreach (var line in lines)
            {
                stdout.WriteLine(line);
            }
        }

        return exitCode;
    }
}

public class WcCommand : ICommand
{
    public string Name => "wc";
    public string Description => "Print newline, word, and byte counts for each FILE";
    public string Synopsis => "wc [-l] [-w] [-c] [-m] [FILE]...";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool lines = false;
        bool words = false;
        bool bytes = false;
        bool chars = false;
        var files = new List<string>();

        foreach (var arg in args)
        {
            if (arg.StartsWith('-') && arg.Length > 1)
            {
                if (arg.Contains('l')) lines = true;
                if (arg.Contains('w')) words = true;
                if (arg.Contains('c')) bytes = true;
                if (arg.Contains('m')) chars = true;
            }
            else
            {
                files.Add(arg);
            }
        }

        if (!lines && !words && !bytes && !chars)
        {
            lines = true;
            words = true;
            bytes = true;
        }

        if (files.Count == 0) files.Add("-");

        long totalLines = 0, totalWords = 0, totalBytes = 0, totalChars = 0;

        foreach (var file in files)
        {
            string content;
            if (file == "-")
            {
                content = await stdin.ReadToEndAsync(ct);
            }
            else
            {
                var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
                if (!File.Exists(winPath))
                {
                    await stderr.WriteLineAsync($"wc: {file}: No such file or directory");
                    continue;
                }
                content = await File.ReadAllTextAsync(winPath, ct);
            }

            long l = content.Count(c => c == '\n');
            long w = Regex.Matches(content, @"\S+").Count;
            long b = Encoding.UTF8.GetByteCount(content);
            long cCount = content.Length;

            totalLines += l;
            totalWords += w;
            totalBytes += b;
            totalChars += cCount;

            var sb = new StringBuilder();
            if (lines) sb.Append($"{l,8} ");
            if (words) sb.Append($"{w,8} ");
            if (chars) sb.Append($"{cCount,8} ");
            else if (bytes) sb.Append($"{b,8} ");
            if (file != "-") sb.Append(file);

            stdout.WriteLine(sb.ToString().TrimEnd());
        }

        if (files.Count > 1)
        {
            var sb = new StringBuilder();
            if (lines) sb.Append($"{totalLines,8} ");
            if (words) sb.Append($"{totalWords,8} ");
            if (chars) sb.Append($"{totalChars,8} ");
            else if (bytes) sb.Append($"{totalBytes,8} ");
            sb.Append("total");
            stdout.WriteLine(sb.ToString().TrimEnd());
        }

        return 0;
    }
}

public class EchoCommand : ICommand
{
    public string Name => "echo";
    public string Description => "Write arguments to the standard output";
    public string Synopsis => "echo [-n] [-e] [STRING]...";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool newline = true;
        bool escapes = false;
        var words = new List<string>();

        foreach (var arg in args)
        {
            if (arg == "-n") newline = false;
            else if (arg == "-e") escapes = true;
            else words.Add(arg);
        }

        var text = string.Join(" ", words);

        if (escapes)
        {
            text = Regex.Unescape(text.Replace("\\e", "\x1b"));
        }

        if (newline)
            stdout.WriteLine(text);
        else
            stdout.Write(text);

        return Task.FromResult(0);
    }
}

public class Base64Command : ICommand
{
    public string Name => "base64";
    public string Description => "Base64 encode or decode data and print to standard output";
    public string Synopsis => "base64 [-d|--decode] [FILE]";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool decode = false;
        string? file = null;

        foreach (var arg in args)
        {
            if (arg is "-d" or "--decode") decode = true;
            else if (!arg.StartsWith('-')) file = arg;
        }

        byte[] data;
        if (file == null || file == "-")
        {
            var text = await stdin.ReadToEndAsync(ct);
            if (decode)
            {
                try
                {
                    data = Convert.FromBase64String(text.Trim());
                    stdout.Write(Encoding.UTF8.GetString(data));
                    return 0;
                }
                catch (Exception ex)
                {
                    stderr.WriteLine($"base64: invalid input: {ex.Message}");
                    return 1;
                }
            }
            else
            {
                data = Encoding.UTF8.GetBytes(text);
                stdout.WriteLine(Convert.ToBase64String(data));
                return 0;
            }
        }
        else
        {
            var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
            if (!File.Exists(winPath))
            {
                stderr.WriteLine($"base64: {file}: No such file or directory");
                return 1;
            }

            if (decode)
            {
                try
                {
                    var text = await File.ReadAllTextAsync(winPath, ct);
                    data = Convert.FromBase64String(text.Trim());
                    stdout.Write(Encoding.UTF8.GetString(data));
                    return 0;
                }
                catch (Exception ex)
                {
                    stderr.WriteLine($"base64: invalid input: {ex.Message}");
                    return 1;
                }
            }
            else
            {
                data = await File.ReadAllBytesAsync(winPath, ct);
                stdout.WriteLine(Convert.ToBase64String(data));
                return 0;
            }
        }
    }
}
