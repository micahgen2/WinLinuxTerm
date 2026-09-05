using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Common;

namespace LinuxTerm.Core.Commands;

public class TacCommand : ICommand
{
    public string Name => "tac";
    public string Description => "Concatenate and print files in reverse line order";
    public string Synopsis => "tac [-s SEP] [FILE...]";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        string separator = "\n";
        var files = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if ((arg == "-s" || arg == "--separator") && i + 1 < args.Length)
            {
                separator = args[++i];
            }
            else if (arg.StartsWith("--separator="))
            {
                separator = arg["--separator=".Length..];
            }
            else if (!arg.StartsWith('-') || arg == "-")
            {
                files.Add(arg);
            }
        }

        if (files.Count == 0)
        {
            files.Add("-");
        }

        int exitCode = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
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
                    await stderr.WriteLineAsync($"tac: {file}: No such file or directory");
                    exitCode = 1;
                    continue;
                }
                content = await File.ReadAllTextAsync(winPath, ct);
            }

            if (string.IsNullOrEmpty(content))
                continue;

            var parts = content.Split(separator);
            if (content.EndsWith(separator) && parts.Length > 0 && parts[^1] == "")
            {
                parts = parts[..^1];
            }

            for (int i = parts.Length - 1; i >= 0; i--)
            {
                await stdout.WriteAsync(parts[i]);
                await stdout.WriteAsync(separator);
            }
        }

        return exitCode;
    }
}

public class RevCommand : ICommand
{
    public string Name => "rev";
    public string Description => "Reverse lines characterwise";
    public string Synopsis => "rev [FILE...]";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        var files = args.Where(a => !a.StartsWith('-') || a == "-").ToList();
        if (files.Count == 0)
        {
            files.Add("-");
        }

        int exitCode = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            TextReader reader;
            bool shouldDispose = false;

            if (file == "-")
            {
                reader = stdin;
            }
            else
            {
                var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
                if (!File.Exists(winPath))
                {
                    await stderr.WriteLineAsync($"rev: cannot open {file}: No such file or directory");
                    exitCode = 1;
                    continue;
                }
                reader = new StreamReader(winPath);
                shouldDispose = true;
            }

            try
            {
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                {
                    char[] chars = line.ToCharArray();
                    Array.Reverse(chars);
                    await stdout.WriteLineAsync(new string(chars));
                }
            }
            finally
            {
                if (shouldDispose) reader.Dispose();
            }
        }

        return exitCode;
    }
}

public class NlCommand : ICommand
{
    public string Name => "nl";
    public string Description => "Number lines of files";
    public string Synopsis => "nl [-b STYLE] [-s SEP] [-w WIDTH] [-v START] [FILE...]";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        string style = "t"; // t: non-empty, a: all, n: none
        string separator = "\t";
        int width = 6;
        long lineNumber = 1;
        var files = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if ((arg == "-b" || arg == "--body-numbering") && i + 1 < args.Length)
            {
                style = args[++i];
            }
            else if (arg.StartsWith("-b"))
            {
                style = arg[2..];
            }
            else if ((arg == "-s" || arg == "--number-separator") && i + 1 < args.Length)
            {
                separator = args[++i];
            }
            else if (arg.StartsWith("-s"))
            {
                separator = arg[2..];
            }
            else if ((arg == "-w" || arg == "--number-width") && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var w)) width = w;
            }
            else if ((arg == "-v" || arg == "--starting-line-number") && i + 1 < args.Length)
            {
                if (long.TryParse(args[++i], out var v)) lineNumber = v;
            }
            else if (!arg.StartsWith('-') || arg == "-")
            {
                files.Add(arg);
            }
        }

        if (files.Count == 0)
        {
            files.Add("-");
        }

        int exitCode = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            TextReader reader;
            bool shouldDispose = false;

            if (file == "-")
            {
                reader = stdin;
            }
            else
            {
                var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
                if (!File.Exists(winPath))
                {
                    await stderr.WriteLineAsync($"nl: {file}: No such file or directory");
                    exitCode = 1;
                    continue;
                }
                reader = new StreamReader(winPath);
                shouldDispose = true;
            }

            try
            {
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                {
                    bool shouldNumber = style switch
                    {
                        "a" => true,
                        "n" => false,
                        _ => !string.IsNullOrEmpty(line.Trim())
                    };

                    if (shouldNumber)
                    {
                        var numStr = lineNumber.ToString().PadLeft(width);
                        await stdout.WriteLineAsync($"{numStr}{separator}{line}");
                        lineNumber++;
                    }
                    else
                    {
                        var padding = new string(' ', width);
                        await stdout.WriteLineAsync($"{padding}{separator}{line}");
                    }
                }
            }
            finally
            {
                if (shouldDispose) reader.Dispose();
            }
        }

        return exitCode;
    }
}

public class FoldCommand : ICommand
{
    public string Name => "fold";
    public string Description => "Wrap each input line to fit in specified width";
    public string Synopsis => "fold [-w WIDTH] [-s] [FILE...]";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        int width = 80;
        bool breakAtSpaces = false;
        var files = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if ((arg == "-w" || arg == "--width") && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var w) && w > 0) width = w;
            }
            else if (arg.StartsWith("-w") && int.TryParse(arg[2..], out var w2) && w2 > 0)
            {
                width = w2;
            }
            else if (arg == "-s" || arg == "--spaces")
            {
                breakAtSpaces = true;
            }
            else if (!arg.StartsWith('-') || arg == "-")
            {
                files.Add(arg);
            }
        }

        if (files.Count == 0)
        {
            files.Add("-");
        }

        int exitCode = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            TextReader reader;
            bool shouldDispose = false;

            if (file == "-")
            {
                reader = stdin;
            }
            else
            {
                var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
                if (!File.Exists(winPath))
                {
                    await stderr.WriteLineAsync($"fold: {file}: No such file or directory");
                    exitCode = 1;
                    continue;
                }
                reader = new StreamReader(winPath);
                shouldDispose = true;
            }

            try
            {
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                {
                    if (line.Length <= width)
                    {
                        await stdout.WriteLineAsync(line);
                        continue;
                    }

                    int pos = 0;
                    while (pos < line.Length)
                    {
                        int remaining = line.Length - pos;
                        if (remaining <= width)
                        {
                            await stdout.WriteLineAsync(line[pos..]);
                            break;
                        }

                        int take = width;
                        if (breakAtSpaces)
                        {
                            int spaceIdx = line.LastIndexOf(' ', pos + width, width);
                            if (spaceIdx > pos)
                            {
                                take = spaceIdx - pos + 1;
                            }
                        }

                        await stdout.WriteLineAsync(line.Substring(pos, take).TrimEnd());
                        pos += take;
                    }
                }
            }
            finally
            {
                if (shouldDispose) reader.Dispose();
            }
        }

        return exitCode;
    }
}

public class PasteCommand : ICommand
{
    public string Name => "paste";
    public string Description => "Merge lines of files";
    public string Synopsis => "paste [-d DELIM] [-s] FILE...";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        string delimiters = "\t";
        bool serial = false;
        var files = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if ((arg == "-d" || arg == "--delimiters") && i + 1 < args.Length)
            {
                delimiters = args[++i];
                if (string.IsNullOrEmpty(delimiters)) delimiters = "\t";
            }
            else if (arg.StartsWith("-d"))
            {
                delimiters = arg[2..];
            }
            else if (arg == "-s" || arg == "--serial")
            {
                serial = true;
            }
            else if (!arg.StartsWith('-') || arg == "-")
            {
                files.Add(arg);
            }
        }

        if (files.Count == 0)
        {
            files.Add("-");
        }

        // Load all lines per file
        var fileLines = new List<List<string>>();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var lines = new List<string>();
            if (file == "-")
            {
                string? l;
                while ((l = await stdin.ReadLineAsync(ct)) != null)
                {
                    lines.Add(l);
                }
            }
            else
            {
                var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
                if (!File.Exists(winPath))
                {
                    await stderr.WriteLineAsync($"paste: {file}: No such file or directory");
                    return 1;
                }
                lines.AddRange(await File.ReadAllLinesAsync(winPath, ct));
            }
            fileLines.Add(lines);
        }

        if (serial)
        {
            foreach (var lines in fileLines)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < lines.Count; i++)
                {
                    sb.Append(lines[i]);
                    if (i < lines.Count - 1)
                    {
                        sb.Append(delimiters[i % delimiters.Length]);
                    }
                }
                await stdout.WriteLineAsync(sb.ToString());
            }
        }
        else
        {
            int maxLines = fileLines.Count > 0 ? fileLines.Max(f => f.Count) : 0;
            for (int row = 0; row < maxLines; row++)
            {
                var sb = new StringBuilder();
                for (int f = 0; f < fileLines.Count; f++)
                {
                    if (f > 0)
                    {
                        sb.Append(delimiters[(f - 1) % delimiters.Length]);
                    }

                    if (row < fileLines[f].Count)
                    {
                        sb.Append(fileLines[f][row]);
                    }
                }
                await stdout.WriteLineAsync(sb.ToString());
            }
        }

        return 0;
    }
}

public class CommCommand : ICommand
{
    public string Name => "comm";
    public string Description => "Compare two sorted files line by line";
    public string Synopsis => "comm [-1] [-2] [-3] FILE1 FILE2";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        bool show1 = true;
        bool show2 = true;
        bool show3 = true;
        var files = new List<string>();

        foreach (var arg in args)
        {
            if (arg == "-1") show1 = false;
            else if (arg == "-2") show2 = false;
            else if (arg == "-3") show3 = false;
            else if (arg == "-12") { show1 = false; show2 = false; }
            else if (arg == "-13") { show1 = false; show3 = false; }
            else if (arg == "-23") { show2 = false; show3 = false; }
            else if (arg == "-123") { show1 = false; show2 = false; show3 = false; }
            else if (!arg.StartsWith('-') || arg == "-") files.Add(arg);
        }

        if (files.Count != 2)
        {
            await stderr.WriteLineAsync("comm: missing operand after 'comm'");
            return 1;
        }

        var lines1 = await ReadLinesAsync(files[0], context, stdin, ct);
        var lines2 = await ReadLinesAsync(files[1], context, stdin, ct);

        int i = 0, j = 0;
        while (i < lines1.Length || j < lines2.Length)
        {
            ct.ThrowIfCancellationRequested();
            if (i >= lines1.Length)
            {
                // Only lines2 remaining (col 2)
                if (show2)
                {
                    var prefix = show1 ? "\t" : "";
                    await stdout.WriteLineAsync($"{prefix}{lines2[j]}");
                }
                j++;
            }
            else if (j >= lines2.Length)
            {
                // Only lines1 remaining (col 1)
                if (show1)
                {
                    await stdout.WriteLineAsync(lines1[i]);
                }
                i++;
            }
            else
            {
                int cmp = string.CompareOrdinal(lines1[i], lines2[j]);
                if (cmp < 0)
                {
                    if (show1)
                    {
                        await stdout.WriteLineAsync(lines1[i]);
                    }
                    i++;
                }
                else if (cmp > 0)
                {
                    if (show2)
                    {
                        var prefix = show1 ? "\t" : "";
                        await stdout.WriteLineAsync($"{prefix}{lines2[j]}");
                    }
                    j++;
                }
                else
                {
                    // Common (col 3)
                    if (show3)
                    {
                        var prefix = (show1 ? "\t" : "") + (show2 ? "\t" : "");
                        await stdout.WriteLineAsync($"{prefix}{lines1[i]}");
                    }
                    i++;
                    j++;
                }
            }
        }

        return 0;
    }

    private static async Task<string[]> ReadLinesAsync(string file, ShellContext context, TextReader stdin, CancellationToken ct)
    {
        if (file == "-")
        {
            var lines = new List<string>();
            string? l;
            while ((l = await stdin.ReadLineAsync(ct)) != null) lines.Add(l);
            return lines.ToArray();
        }
        var win = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
        return File.Exists(win) ? await File.ReadAllLinesAsync(win, ct) : Array.Empty<string>();
    }
}

public class CmpCommand : ICommand
{
    public string Name => "cmp";
    public string Description => "Compare two files byte by byte";
    public string Synopsis => "cmp [-l] [-s] FILE1 FILE2";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        bool verbose = false;
        bool silent = false;
        var files = new List<string>();

        foreach (var arg in args)
        {
            if (arg == "-l" || arg == "--verbose") verbose = true;
            else if (arg == "-s" || arg == "--silent" || arg == "--quiet") silent = true;
            else if (!arg.StartsWith('-') || arg == "-") files.Add(arg);
        }

        if (files.Count < 2)
        {
            if (!silent) await stderr.WriteLineAsync("cmp: missing operand after 'cmp'");
            return 2;
        }

        string file1 = files[0];
        string file2 = files[1];

        var win1 = PosixPathMapper.ToWindows(file1, context.CurrentDirectory);
        var win2 = PosixPathMapper.ToWindows(file2, context.CurrentDirectory);

        if (!File.Exists(win1))
        {
            if (!silent) await stderr.WriteLineAsync($"cmp: {file1}: No such file or directory");
            return 2;
        }
        if (!File.Exists(win2))
        {
            if (!silent) await stderr.WriteLineAsync($"cmp: {file2}: No such file or directory");
            return 2;
        }

        using var fs1 = File.OpenRead(win1);
        using var fs2 = File.OpenRead(win2);

        long byteOffset = 1;
        long lineOffset = 1;
        bool differ = false;

        byte[] buf1 = new byte[8192];
        byte[] buf2 = new byte[8192];

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int r1 = await fs1.ReadAsync(buf1.AsMemory(0, buf1.Length), ct);
            int r2 = await fs2.ReadAsync(buf2.AsMemory(0, buf2.Length), ct);

            int min = Math.Min(r1, r2);
            for (int k = 0; k < min; k++)
            {
                if (buf1[k] != buf2[k])
                {
                    differ = true;
                    if (verbose)
                    {
                        await stdout.WriteLineAsync($"{byteOffset,8} {Convert.ToString(buf1[k], 8).PadLeft(3, '0')} {Convert.ToString(buf2[k], 8).PadLeft(3, '0')}");
                    }
                    else
                    {
                        if (!silent)
                        {
                            await stdout.WriteLineAsync($"{file1} {file2} differ: byte {byteOffset}, line {lineOffset}");
                        }
                        return 1;
                    }
                }

                if (buf1[k] == '\n') lineOffset++;
                byteOffset++;
            }

            if (r1 == 0 && r2 == 0)
            {
                break;
            }

            if (r1 == 0)
            {
                if (!silent) await stderr.WriteLineAsync($"cmp: EOF on {file1}");
                return 1;
            }

            if (r2 == 0)
            {
                if (!silent) await stderr.WriteLineAsync($"cmp: EOF on {file2}");
                return 1;
            }
        }

        return differ ? 1 : 0;
    }
}

public class ExpandCommand : ICommand
{
    public string Name => "expand";
    public string Description => "Convert tabs to spaces";
    public string Synopsis => "expand [-t N] [-i] [FILE...]";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        int tabSize = 8;
        bool initialOnly = false;
        var files = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if ((arg == "-t" || arg == "--tabs") && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var t) && t > 0) tabSize = t;
            }
            else if (arg.StartsWith("-t") && int.TryParse(arg[2..], out var t2) && t2 > 0)
            {
                tabSize = t2;
            }
            else if (arg == "-i" || arg == "--initial")
            {
                initialOnly = true;
            }
            else if (!arg.StartsWith('-') || arg == "-")
            {
                files.Add(arg);
            }
        }

        if (files.Count == 0) files.Add("-");

        int exitCode = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            TextReader reader;
            bool shouldDispose = false;

            if (file == "-")
            {
                reader = stdin;
            }
            else
            {
                var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
                if (!File.Exists(winPath))
                {
                    await stderr.WriteLineAsync($"expand: {file}: No such file or directory");
                    exitCode = 1;
                    continue;
                }
                reader = new StreamReader(winPath);
                shouldDispose = true;
            }

            try
            {
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                {
                    var sb = new StringBuilder();
                    int col = 0;
                    bool pastInitial = false;

                    foreach (char c in line)
                    {
                        if (c == '\t')
                        {
                            if (initialOnly && pastInitial)
                            {
                                sb.Append(c);
                                col++;
                            }
                            else
                            {
                                int spaces = tabSize - (col % tabSize);
                                sb.Append(' ', spaces);
                                col += spaces;
                            }
                        }
                        else
                        {
                            if (c != ' ') pastInitial = true;
                            sb.Append(c);
                            col++;
                        }
                    }
                    await stdout.WriteLineAsync(sb.ToString());
                }
            }
            finally
            {
                if (shouldDispose) reader.Dispose();
            }
        }

        return exitCode;
    }
}

public class UnexpandCommand : ICommand
{
    public string Name => "unexpand";
    public string Description => "Convert spaces to tabs";
    public string Synopsis => "unexpand [-t N] [-a] [FILE...]";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        int tabSize = 8;
        bool allSpaces = false;
        var files = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if ((arg == "-t" || arg == "--tabs") && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var t) && t > 0) tabSize = t;
            }
            else if (arg.StartsWith("-t") && int.TryParse(arg[2..], out var t2) && t2 > 0)
            {
                tabSize = t2;
            }
            else if (arg == "-a" || arg == "--all")
            {
                allSpaces = true;
            }
            else if (!arg.StartsWith('-') || arg == "-")
            {
                files.Add(arg);
            }
        }

        if (files.Count == 0) files.Add("-");

        int exitCode = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            TextReader reader;
            bool shouldDispose = false;

            if (file == "-")
            {
                reader = stdin;
            }
            else
            {
                var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
                if (!File.Exists(winPath))
                {
                    await stderr.WriteLineAsync($"unexpand: {file}: No such file or directory");
                    exitCode = 1;
                    continue;
                }
                reader = new StreamReader(winPath);
                shouldDispose = true;
            }

            try
            {
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                {
                    var sb = new StringBuilder();
                    int spaceCount = 0;
                    bool nonSpaceSeen = false;

                    for (int i = 0; i < line.Length; i++)
                    {
                        char c = line[i];
                        if (c == ' ' && (!nonSpaceSeen || allSpaces))
                        {
                            spaceCount++;
                            if (spaceCount == tabSize)
                            {
                                sb.Append('\t');
                                spaceCount = 0;
                            }
                        }
                        else
                        {
                            if (spaceCount > 0)
                            {
                                sb.Append(' ', spaceCount);
                                spaceCount = 0;
                            }
                            nonSpaceSeen = true;
                            sb.Append(c);
                        }
                    }

                    if (spaceCount > 0)
                    {
                        sb.Append(' ', spaceCount);
                    }

                    await stdout.WriteLineAsync(sb.ToString());
                }
            }
            finally
            {
                if (shouldDispose) reader.Dispose();
            }
        }

        return exitCode;
    }
}
