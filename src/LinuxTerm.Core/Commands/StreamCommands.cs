using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Common;
using LinuxTerm.Core.Execution;

namespace LinuxTerm.Core.Commands;

public class SedCommand : ICommand
{
    public string Name => "sed";
    public string Description => "Stream editor for filtering and transforming text";
    public string Synopsis => "sed 's/pattern/replacement/[g][i]' [FILE]...";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            await stderr.WriteLineAsync("sed: missing script");
            return 1;
        }

        string script = args[0];
        var files = args.Skip(1).ToList();
        if (files.Count == 0) files.Add("-");

        // Parse s/pattern/replacement/flags
        var sMatch = Regex.Match(script, @"^s([/|#])(?<pattern>.*?)\1(?<replacement>.*?)\1(?<flags>[a-zA-Z]*)$");
        if (!sMatch.Success)
        {
            await stderr.WriteLineAsync($"sed: unsupported script syntax '{script}' (expected s/find/replace/[flags])");
            return 1;
        }

        var pattern = sMatch.Groups["pattern"].Value;
        var replacement = sMatch.Groups["replacement"].Value;
        var flags = sMatch.Groups["flags"].Value;

        bool global = flags.Contains('g');
        bool ignoreCase = flags.Contains('i');

        var options = RegexOptions.Compiled;
        if (ignoreCase) options |= RegexOptions.IgnoreCase;
        var regex = new Regex(pattern, options);

        foreach (var file in files)
        {
            if (file == "-")
            {
                string? line;
                while ((line = await stdin.ReadLineAsync(ct)) != null)
                {
                    stdout.WriteLine(ApplySed(line, regex, replacement, global));
                }
            }
            else
            {
                var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
                if (!File.Exists(winPath))
                {
                    await stderr.WriteLineAsync($"sed: can't read '{file}': No such file or directory");
                    continue;
                }

                using var reader = new StreamReader(winPath);
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                {
                    stdout.WriteLine(ApplySed(line, regex, replacement, global));
                }
            }
        }

        return 0;
    }

    private static string ApplySed(string line, Regex regex, string replacement, bool global)
    {
        return global ? regex.Replace(line, replacement) : regex.Replace(line, replacement, 1);
    }
}

public class AwkCommand : ICommand
{
    public string Name => "awk";
    public string Description => "Pattern scanning and processing language";
    public string Synopsis => "awk [-F delim] '{print $1, $2...}' [FILE]...";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        string? delim = null;
        string? script = null;
        var files = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-F" && i + 1 < args.Length)
            {
                delim = args[++i];
            }
            else if (args[i].StartsWith("-F") && args[i].Length > 2)
            {
                delim = args[i][2..];
            }
            else if (script == null)
            {
                script = args[i];
            }
            else
            {
                files.Add(args[i]);
            }
        }

        if (script == null)
        {
            await stderr.WriteLineAsync("awk: missing program");
            return 1;
        }

        if (files.Count == 0) files.Add("-");

        // Simple awk '{print ...}' evaluator
        var printMatch = Regex.Match(script, @"\{\s*print\s+(?<fields>.*?)\s*\}");
        var fieldTokens = printMatch.Success ? printMatch.Groups["fields"].Value.Split(',', StringSplitOptions.TrimEntries) : new[] { "$0" };

        foreach (var file in files)
        {
            if (file == "-")
            {
                string? line;
                while ((line = await stdin.ReadLineAsync(ct)) != null)
                {
                    stdout.WriteLine(ProcessLine(line, delim, fieldTokens));
                }
            }
            else
            {
                var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
                if (!File.Exists(winPath))
                {
                    await stderr.WriteLineAsync($"awk: fatal: cannot open file '{file}' for reading: No such file or directory");
                    continue;
                }

                using var reader = new StreamReader(winPath);
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                {
                    stdout.WriteLine(ProcessLine(line, delim, fieldTokens));
                }
            }
        }

        return 0;
    }

    private static string ProcessLine(string line, string? delim, string[] fieldTokens)
    {
        string[] parts;
        if (delim != null)
        {
            parts = line.Split(delim);
        }
        else
        {
            parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        }

        var outputItems = new List<string>();
        foreach (var token in fieldTokens)
        {
            if (token == "$0")
            {
                outputItems.Add(line);
            }
            else if (token == "$NF")
            {
                outputItems.Add(parts.Length > 0 ? parts[^1] : string.Empty);
            }
            else if (token.StartsWith('$') && int.TryParse(token[1..], out int idx))
            {
                outputItems.Add(idx > 0 && idx <= parts.Length ? parts[idx - 1] : string.Empty);
            }
            else
            {
                outputItems.Add(token.Trim('"', '\''));
            }
        }

        return string.Join(" ", outputItems);
    }
}

public class CutCommand : ICommand
{
    public string Name => "cut";
    public string Description => "Remove sections from each line of files";
    public string Synopsis => "cut [-d DELIM] -f FIELDS [FILE]...";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        string delim = "\t";
        string? fieldsArg = null;
        var files = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-d" && i + 1 < args.Length)
            {
                delim = args[++i];
            }
            else if (args[i].StartsWith("-d") && args[i].Length > 2)
            {
                delim = args[i][2..];
            }
            else if (args[i] == "-f" && i + 1 < args.Length)
            {
                fieldsArg = args[++i];
            }
            else if (args[i].StartsWith("-f") && args[i].Length > 2)
            {
                fieldsArg = args[i][2..];
            }
            else if (!args[i].StartsWith('-'))
            {
                files.Add(args[i]);
            }
        }

        if (fieldsArg == null)
        {
            await stderr.WriteLineAsync("cut: you must specify a list of fields (-f)");
            return 1;
        }

        var targetFields = ParseFields(fieldsArg);
        if (files.Count == 0) files.Add("-");

        foreach (var file in files)
        {
            if (file == "-")
            {
                string? line;
                while ((line = await stdin.ReadLineAsync(ct)) != null)
                {
                    stdout.WriteLine(CutLine(line, delim, targetFields));
                }
            }
            else
            {
                var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
                if (!File.Exists(winPath))
                {
                    await stderr.WriteLineAsync($"cut: {file}: No such file or directory");
                    continue;
                }

                using var reader = new StreamReader(winPath);
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                {
                    stdout.WriteLine(CutLine(line, delim, targetFields));
                }
            }
        }

        return 0;
    }

    private static HashSet<int> ParseFields(string fieldStr)
    {
        var result = new HashSet<int>();
        var parts = fieldStr.Split(',');

        foreach (var p in parts)
        {
            if (p.Contains('-'))
            {
                var range = p.Split('-');
                if (int.TryParse(range[0], out int start) && int.TryParse(range[1], out int end))
                {
                    for (int i = start; i <= end; i++) result.Add(i);
                }
            }
            else if (int.TryParse(p, out int single))
            {
                result.Add(single);
            }
        }

        return result;
    }

    private static string CutLine(string line, string delim, HashSet<int> fields)
    {
        var parts = line.Split(delim);
        var selected = new List<string>();

        for (int i = 0; i < parts.Length; i++)
        {
            if (fields.Contains(i + 1))
            {
                selected.Add(parts[i]);
            }
        }

        return string.Join(delim, selected);
    }
}

public class SortCommand : ICommand
{
    public string Name => "sort";
    public string Description => "Sort lines of text files";
    public string Synopsis => "sort [-r] [-n] [-u] [-k KEY] [FILE]...";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool reverse = false;
        bool numeric = false;
        bool unique = false;
        int keyCol = 0;
        var files = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith('-') && arg.Length > 1)
            {
                if (arg.Contains('r')) reverse = true;
                if (arg.Contains('n')) numeric = true;
                if (arg.Contains('u')) unique = true;
                if (arg == "-k" && i + 1 < args.Length)
                {
                    int.TryParse(args[++i], out keyCol);
                }
            }
            else
            {
                files.Add(arg);
            }
        }

        if (files.Count == 0) files.Add("-");

        var lines = new List<string>();

        foreach (var file in files)
        {
            if (file == "-")
            {
                string? line;
                while ((line = await stdin.ReadLineAsync(ct)) != null)
                    lines.Add(line);
            }
            else
            {
                var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
                if (!File.Exists(winPath))
                {
                    await stderr.WriteLineAsync($"sort: cannot read '{file}': No such file or directory");
                    continue;
                }
                lines.AddRange(await File.ReadAllLinesAsync(winPath, ct));
            }
        }

        IEnumerable<string> sorted;

        if (numeric)
        {
            sorted = lines.OrderBy(l => ExtractNumericKey(l, keyCol));
        }
        else
        {
            sorted = lines.OrderBy(l => ExtractKey(l, keyCol), StringComparer.Ordinal);
        }

        if (reverse) sorted = sorted.Reverse();
        if (unique) sorted = sorted.Distinct();

        foreach (var l in sorted)
        {
            stdout.WriteLine(l);
        }

        return 0;
    }

    private static string ExtractKey(string line, int col)
    {
        if (col <= 0) return line;
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return col <= parts.Length ? parts[col - 1] : line;
    }

    private static double ExtractNumericKey(string line, int col)
    {
        var key = ExtractKey(line, col);
        var match = Regex.Match(key, @"-?\d+(\.\d+)?");
        return match.Success && double.TryParse(match.Value, out double val) ? val : 0;
    }
}

public class UniqCommand : ICommand
{
    public string Name => "uniq";
    public string Description => "Report or omit repeated lines";
    public string Synopsis => "uniq [-c] [-d] [-i] [FILE]";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool count = false;
        bool duplicatesOnly = false;
        bool ignoreCase = false;
        string? file = null;

        foreach (var arg in args)
        {
            if (arg.StartsWith('-') && arg.Length > 1)
            {
                if (arg.Contains('c')) count = true;
                if (arg.Contains('d')) duplicatesOnly = true;
                if (arg.Contains('i')) ignoreCase = true;
            }
            else
            {
                file = arg;
            }
        }

        var lines = new List<string>();
        if (file == null || file == "-")
        {
            string? l;
            while ((l = await stdin.ReadLineAsync(ct)) != null) lines.Add(l);
        }
        else
        {
            var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
            if (!File.Exists(winPath))
            {
                await stderr.WriteLineAsync($"uniq: '{file}': No such file or directory");
                return 1;
            }
            lines.AddRange(await File.ReadAllLinesAsync(winPath, ct));
        }

        var comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        int i = 0;
        while (i < lines.Count)
        {
            var current = lines[i];
            int runLength = 1;
            while (i + 1 < lines.Count && comparer.Equals(lines[i + 1], current))
            {
                runLength++;
                i++;
            }

            if (!duplicatesOnly || runLength > 1)
            {
                if (count)
                {
                    stdout.WriteLine($"{runLength,7} {current}");
                }
                else
                {
                    stdout.WriteLine(current);
                }
            }

            i++;
        }

        return 0;
    }
}

public class TrCommand : ICommand
{
    public string Name => "tr";
    public string Description => "Translate or delete characters";
    public string Synopsis => "tr [-d] SET1 [SET2]";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool delete = false;
        string? set1 = null;
        string? set2 = null;

        foreach (var arg in args)
        {
            if (arg == "-d") delete = true;
            else if (set1 == null) set1 = arg;
            else if (set2 == null) set2 = arg;
        }

        if (set1 == null)
        {
            await stderr.WriteLineAsync("tr: missing operand");
            return 1;
        }

        set1 = ExpandSet(Unescape(set1));
        if (set2 != null) set2 = ExpandSet(Unescape(set2));

        var input = await stdin.ReadToEndAsync(ct);
        var sb = new StringBuilder();

        if (delete)
        {
            var delSet = new HashSet<char>(set1);
            foreach (char c in input)
            {
                if (!delSet.Contains(c)) sb.Append(c);
            }
        }
        else if (set2 != null)
        {
            var map = new Dictionary<char, char>();
            for (int i = 0; i < set1.Length; i++)
            {
                char target = i < set2.Length ? set2[i] : set2[^1];
                map[set1[i]] = target;
            }

            foreach (char c in input)
            {
                sb.Append(map.TryGetValue(c, out char repl) ? repl : c);
            }
        }
        else
        {
            sb.Append(input);
        }

        stdout.Write(sb.ToString());
        return 0;
    }

    private static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\r", "\r");

    private static string ExpandSet(string s)
    {
        if (s == "a-z") return "abcdefghijklmnopqrstuvwxyz";
        if (s == "A-Z") return "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        if (s == "0-9") return "0123456789";
        return s;
    }
}

public class TeeCommand : ICommand
{
    public string Name => "tee";
    public string Description => "Read from standard input and write to standard output and files";
    public string Synopsis => "tee [-a] [FILE]...";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool append = false;
        var files = new List<string>();

        foreach (var arg in args)
        {
            if (arg is "-a" or "--append") append = true;
            else if (!arg.StartsWith('-')) files.Add(arg);
        }

        var writers = new List<StreamWriter>();
        try
        {
            foreach (var f in files)
            {
                var winPath = PosixPathMapper.ToWindows(f, context.CurrentDirectory);
                var mode = append ? FileMode.Append : FileMode.Create;
                var stream = new FileStream(winPath, mode, FileAccess.Write, FileShare.Read);
                writers.Add(new StreamWriter(stream) { AutoFlush = true });
            }

            string? line;
            while ((line = await stdin.ReadLineAsync(ct)) != null)
            {
                stdout.WriteLine(line);
                foreach (var w in writers)
                {
                    await w.WriteLineAsync(line);
                }
            }
        }
        finally
        {
            foreach (var w in writers)
            {
                w.Dispose();
            }
        }

        return 0;
    }
}

public class XargsCommand : ICommand
{
    public string Name => "xargs";
    public string Description => "Build and execute command lines from standard input";
    public string Synopsis => "xargs [COMMAND] [INITIAL-ARGS...]";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var cmdName = args.Length > 0 ? args[0] : "echo";
        var initialArgs = args.Skip(1).ToList();

        var input = await stdin.ReadToEndAsync(ct);
        var items = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (items.Length == 0) return 0;

        var fullArgs = new List<string>(initialArgs);
        fullArgs.AddRange(items);

        var engine = new ShellEngine();
        var commandLine = $"{cmdName} {string.Join(" ", fullArgs.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}";
        return await engine.ExecuteAsync(commandLine, context, TextReader.Null, stdout, stderr, ct);
    }
}

public class DiffCommand : ICommand
{
    public string Name => "diff";
    public string Description => "Compare files line by line";
    public string Synopsis => "diff FILE1 FILE2";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var files = args.Where(a => !a.StartsWith('-')).ToList();
        if (files.Count < 2)
        {
            await stderr.WriteLineAsync("diff: missing operand");
            return 2;
        }

        var path1 = PosixPathMapper.ToWindows(files[0], context.CurrentDirectory);
        var path2 = PosixPathMapper.ToWindows(files[1], context.CurrentDirectory);

        if (!File.Exists(path1) || !File.Exists(path2))
        {
            await stderr.WriteLineAsync("diff: cannot open file");
            return 2;
        }

        var lines1 = await File.ReadAllLinesAsync(path1, ct);
        var lines2 = await File.ReadAllLinesAsync(path2, ct);

        int max = Math.Max(lines1.Length, lines2.Length);
        bool hasDiff = false;

        for (int i = 0; i < max; i++)
        {
            string? l1 = i < lines1.Length ? lines1[i] : null;
            string? l2 = i < lines2.Length ? lines2[i] : null;

            if (l1 != l2)
            {
                hasDiff = true;
                if (l1 != null && l2 != null)
                {
                    stdout.WriteLine($"{AnsiText.Red}< {l1}{AnsiText.Reset}");
                    stdout.WriteLine("---");
                    stdout.WriteLine($"{AnsiText.Green}> {l2}{AnsiText.Reset}");
                }
                else if (l1 != null)
                {
                    stdout.WriteLine($"{AnsiText.Red}< {l1}{AnsiText.Reset}");
                }
                else if (l2 != null)
                {
                    stdout.WriteLine($"{AnsiText.Green}> {l2}{AnsiText.Reset}");
                }
            }
        }

        return hasDiff ? 1 : 0;
    }
}
