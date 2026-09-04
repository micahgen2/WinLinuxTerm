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

public class LsCommand : ICommand
{
    public string Name => "ls";
    public string Description => "List directory contents";
    public string Synopsis => "ls [-l] [-a] [-A] [-h] [-t] [-S] [-r] [-1] [FILE/DIR]...";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool longFormat = false;
        bool all = false;
        bool almostAll = false;
        bool humanReadable = false;
        bool sortByTime = false;
        bool sortBySize = false;
        bool reverse = false;
        bool oneColumn = false;
        var targets = new List<string>();

        foreach (var arg in args)
        {
            if (arg.StartsWith('-') && arg.Length > 1 && !arg.StartsWith("--"))
            {
                foreach (char c in arg[1..])
                {
                    switch (c)
                    {
                        case 'l': longFormat = true; break;
                        case 'a': all = true; break;
                        case 'A': almostAll = true; break;
                        case 'h': humanReadable = true; break;
                        case 't': sortByTime = true; break;
                        case 'S': sortBySize = true; break;
                        case 'r': reverse = true; break;
                        case '1': oneColumn = true; break;
                    }
                }
            }
            else if (arg == "--help")
            {
                stdout.WriteLine(Synopsis);
                return Task.FromResult(0);
            }
            else
            {
                targets.Add(arg);
            }
        }

        if (targets.Count == 0)
            targets.Add(".");

        int exitCode = 0;
        bool multiple = targets.Count > 1;

        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var winPath = PosixPathMapper.ToWindows(target, context.CurrentDirectory);

            if (multiple)
            {
                if (i > 0) stdout.WriteLine();
                stdout.WriteLine($"{target}:");
            }

            if (File.Exists(winPath))
            {
                var fi = new FileInfo(winPath);
                if (longFormat)
                    stdout.WriteLine(FormatLongFile(fi, humanReadable, context.UserName));
                else
                    stdout.WriteLine(ColorizeEntry(fi.Name, false, fi.Extension));
                continue;
            }

            if (!Directory.Exists(winPath))
            {
                stderr.WriteLine($"ls: cannot access '{target}': No such file or directory");
                exitCode = 2;
                continue;
            }

            try
            {
                var dirInfo = new DirectoryInfo(winPath);
                var entries = new List<FileSystemInfo>();

                if (all)
                {
                    entries.Add(dirInfo); // represent "."
                    if (dirInfo.Parent != null)
                        entries.Add(dirInfo.Parent); // represent ".."
                }

                entries.AddRange(dirInfo.GetFileSystemInfos());

                if (!all && !almostAll)
                {
                    entries = entries.Where(e => !e.Name.StartsWith('.')).ToList();
                }

                // Sorting
                if (sortByTime)
                    entries = entries.OrderByDescending(e => e.LastWriteTime).ToList();
                else if (sortBySize)
                    entries = entries.OrderByDescending(e => e is FileInfo f ? f.Length : 0).ToList();
                else
                    entries = entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

                if (reverse)
                    entries.Reverse();

                if (longFormat)
                {
                    // Calculate total blocks (approximate 1KB blocks)
                    long totalBytes = entries.OfType<FileInfo>().Sum(f => f.Length);
                    stdout.WriteLine($"total {Math.Max(1, totalBytes / 1024)}");

                    foreach (var entry in entries)
                    {
                        string name = entry.FullName == dirInfo.FullName ? "." :
                                      (dirInfo.Parent != null && entry.FullName == dirInfo.Parent.FullName ? ".." : entry.Name);

                        if (entry is DirectoryInfo di)
                        {
                            stdout.WriteLine(FormatLongDirectory(di, name, context.UserName));
                        }
                        else if (entry is FileInfo fi)
                        {
                            stdout.WriteLine(FormatLongFile(fi, humanReadable, context.UserName, name));
                        }
                    }
                }
                else if (oneColumn)
                {
                    foreach (var entry in entries)
                    {
                        string name = entry.FullName == dirInfo.FullName ? "." :
                                      (dirInfo.Parent != null && entry.FullName == dirInfo.Parent.FullName ? ".." : entry.Name);
                        stdout.WriteLine(ColorizeEntry(name, entry is DirectoryInfo, entry.Extension));
                    }
                }
                else
                {
                    // Multi-column or flow layout
                    var coloredNames = entries.Select(entry =>
                    {
                        string name = entry.FullName == dirInfo.FullName ? "." :
                                      (dirInfo.Parent != null && entry.FullName == dirInfo.Parent.FullName ? ".." : entry.Name);
                        return ColorizeEntry(name, entry is DirectoryInfo, entry.Extension);
                    }).ToList();

                    stdout.WriteLine(string.Join("  ", coloredNames));
                }
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"ls: cannot open directory '{target}': {ex.Message}");
                exitCode = 1;
            }
        }

        return Task.FromResult(exitCode);
    }

    private static string ColorizeEntry(string name, bool isDir, string ext)
    {
        if (isDir)
            return $"{AnsiText.Bold}{AnsiText.BrightBlue}{name}{AnsiText.Reset}";

        var lowerExt = ext.ToLowerInvariant();
        if (lowerExt is ".exe" or ".bat" or ".cmd" or ".ps1" or ".sh")
            return $"{AnsiText.Bold}{AnsiText.BrightGreen}{name}{AnsiText.Reset}";

        if (lowerExt is ".zip" or ".tar" or ".gz" or ".7z" or ".rar")
            return $"{AnsiText.Bold}{AnsiText.BrightMagenta}{name}{AnsiText.Reset}";

        if (lowerExt is ".png" or ".jpg" or ".jpeg" or ".gif" or ".mp4" or ".svg")
            return $"{AnsiText.BrightMagenta}{name}{AnsiText.Reset}";

        if (lowerExt is ".lnk")
            return $"{AnsiText.BrightCyan}{name}{AnsiText.Reset}";

        return name;
    }

    private static string FormatLongDirectory(DirectoryInfo di, string name, string user)
    {
        var dateStr = di.LastWriteTime.ToString("MMM dd HH:mm");
        var coloredName = $"{AnsiText.Bold}{AnsiText.BrightBlue}{name}{AnsiText.Reset}";
        return $"drwxr-xr-x 1 {user} {user} 4096 {dateStr,12} {coloredName}";
    }

    private static string FormatLongFile(FileInfo fi, bool humanReadable, string user, string? customName = null)
    {
        var dateStr = fi.LastWriteTime.ToString("MMM dd HH:mm");
        var sizeStr = humanReadable ? FormatSize(fi.Length) : fi.Length.ToString();
        var name = customName ?? fi.Name;
        var coloredName = ColorizeEntry(name, false, fi.Extension);
        var perm = fi.IsReadOnly ? "-r--r--r--" : "-rw-r--r--";
        if (fi.Extension.ToLowerInvariant() is ".exe" or ".bat" or ".cmd")
            perm = "-rwxr-xr-x";

        return $"{perm} 1 {user} {user} {sizeStr,8} {dateStr,12} {coloredName}";
    }

    private static string FormatSize(long bytes)
    {
        string[] suffixes = { "B", "K", "M", "G", "T" };
        int idx = 0;
        double d = bytes;
        while (d >= 1024 && idx < suffixes.Length - 1)
        {
            d /= 1024;
            idx++;
        }
        return idx == 0 ? $"{bytes}B" : $"{d:0.#}{suffixes[idx]}";
    }
}

public class CdCommand : ICommand
{
    public string Name => "cd";
    public string Description => "Change the shell working directory";
    public string Synopsis => "cd [-] [DIR]";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        string target;
        if (args.Length == 0)
        {
            target = "~";
        }
        else if (args[0] == "-")
        {
            target = context.PreviousDirectory;
            var winTarget = PosixPathMapper.ToWindows(target, context.CurrentDirectory);
            if (Directory.Exists(winTarget))
            {
                context.CurrentDirectory = winTarget;
                stdout.WriteLine(context.PosixCurrentDirectory);
                return Task.FromResult(0);
            }
            stderr.WriteLine("bash: cd: OLDPWD not set");
            return Task.FromResult(1);
        }
        else
        {
            target = args[0];
        }

        var winPath = PosixPathMapper.ToWindows(target, context.CurrentDirectory);
        if (Directory.Exists(winPath))
        {
            context.CurrentDirectory = winPath;
            return Task.FromResult(0);
        }

        stderr.WriteLine($"bash: cd: {target}: No such file or directory");
        return Task.FromResult(1);
    }
}

public class PwdCommand : ICommand
{
    public string Name => "pwd";
    public string Description => "Print name of current/working directory";
    public string Synopsis => "pwd [-P]";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool physical = args.Contains("-P");
        stdout.WriteLine(physical ? context.CurrentDirectory : context.PosixCurrentDirectory);
        return Task.FromResult(0);
    }
}

public class MkdirCommand : ICommand
{
    public string Name => "mkdir";
    public string Description => "Create the DIRECTORY(ies), if they do not already exist";
    public string Synopsis => "mkdir [-p] DIRECTORY...";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool parents = false;
        var dirs = new List<string>();

        foreach (var arg in args)
        {
            if (arg is "-p" or "--parents")
                parents = true;
            else if (!arg.StartsWith('-'))
                dirs.Add(arg);
        }

        if (dirs.Count == 0)
        {
            stderr.WriteLine("mkdir: missing operand");
            return Task.FromResult(1);
        }

        int exitCode = 0;
        foreach (var dir in dirs)
        {
            try
            {
                var winPath = PosixPathMapper.ToWindows(dir, context.CurrentDirectory);
                if (Directory.Exists(winPath) && !parents)
                {
                    stderr.WriteLine($"mkdir: cannot create directory '{dir}': File exists");
                    exitCode = 1;
                    continue;
                }
                Directory.CreateDirectory(winPath);
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"mkdir: cannot create directory '{dir}': {ex.Message}");
                exitCode = 1;
            }
        }

        return Task.FromResult(exitCode);
    }
}

public class RmCommand : ICommand
{
    public string Name => "rm";
    public string Description => "Remove files or directories";
    public string Synopsis => "rm [-r|-R] [-f] FILE...";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool recursive = false;
        bool force = false;
        var targets = new List<string>();

        foreach (var arg in args)
        {
            if (arg.StartsWith('-') && arg.Length > 1)
            {
                if (arg.Contains('r') || arg.Contains('R')) recursive = true;
                if (arg.Contains('f')) force = true;
            }
            else
            {
                targets.Add(arg);
            }
        }

        if (targets.Count == 0)
        {
            if (!force) stderr.WriteLine("rm: missing operand");
            return Task.FromResult(force ? 0 : 1);
        }

        int exitCode = 0;
        foreach (var target in targets)
        {
            var winPath = PosixPathMapper.ToWindows(target, context.CurrentDirectory);

            if (File.Exists(winPath))
            {
                try
                {
                    File.Delete(winPath);
                }
                catch (Exception ex)
                {
                    if (!force)
                    {
                        stderr.WriteLine($"rm: cannot remove '{target}': {ex.Message}");
                        exitCode = 1;
                    }
                }
            }
            else if (Directory.Exists(winPath))
            {
                if (!recursive)
                {
                    stderr.WriteLine($"rm: cannot remove '{target}': Is a directory");
                    exitCode = 1;
                    continue;
                }
                try
                {
                    Directory.Delete(winPath, true);
                }
                catch (Exception ex)
                {
                    if (!force)
                    {
                        stderr.WriteLine($"rm: cannot remove '{target}': {ex.Message}");
                        exitCode = 1;
                    }
                }
            }
            else if (!force)
            {
                stderr.WriteLine($"rm: cannot remove '{target}': No such file or directory");
                exitCode = 1;
            }
        }

        return Task.FromResult(exitCode);
    }
}

public class CpCommand : ICommand
{
    public string Name => "cp";
    public string Description => "Copy SOURCE to DEST, or multiple SOURCE(s) to DIRECTORY";
    public string Synopsis => "cp [-r|-R] SOURCE... DEST";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool recursive = false;
        var paths = new List<string>();

        foreach (var arg in args)
        {
            if (arg.StartsWith('-') && (arg.Contains('r') || arg.Contains('R')))
                recursive = true;
            else if (!arg.StartsWith('-'))
                paths.Add(arg);
        }

        if (paths.Count < 2)
        {
            stderr.WriteLine("cp: missing destination file operand after source");
            return Task.FromResult(1);
        }

        var dest = paths[^1];
        var sources = paths.Take(paths.Count - 1).ToList();
        var winDest = PosixPathMapper.ToWindows(dest, context.CurrentDirectory);
        bool destIsDir = Directory.Exists(winDest);

        if (sources.Count > 1 && !destIsDir)
        {
            stderr.WriteLine($"cp: target '{dest}' is not a directory");
            return Task.FromResult(1);
        }

        int exitCode = 0;
        foreach (var src in sources)
        {
            var winSrc = PosixPathMapper.ToWindows(src, context.CurrentDirectory);

            if (File.Exists(winSrc))
            {
                try
                {
                    var finalDest = destIsDir ? Path.Combine(winDest, Path.GetFileName(winSrc)) : winDest;
                    File.Copy(winSrc, finalDest, true);
                }
                catch (Exception ex)
                {
                    stderr.WriteLine($"cp: cannot copy '{src}': {ex.Message}");
                    exitCode = 1;
                }
            }
            else if (Directory.Exists(winSrc))
            {
                if (!recursive)
                {
                    stderr.WriteLine($"cp: -r not specified; omitting directory '{src}'");
                    exitCode = 1;
                    continue;
                }
                try
                {
                    var targetDir = destIsDir ? Path.Combine(winDest, Path.GetFileName(winSrc)) : winDest;
                    CopyDirectoryRecursively(winSrc, targetDir);
                }
                catch (Exception ex)
                {
                    stderr.WriteLine($"cp: cannot copy directory '{src}': {ex.Message}");
                    exitCode = 1;
                }
            }
            else
            {
                stderr.WriteLine($"cp: cannot stat '{src}': No such file or directory");
                exitCode = 1;
            }
        }

        return Task.FromResult(exitCode);
    }

    private static void CopyDirectoryRecursively(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectoryRecursively(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
        }
    }
}

public class MvCommand : ICommand
{
    public string Name => "mv";
    public string Description => "Rename SOURCE to DEST, or move SOURCE(s) to DIRECTORY";
    public string Synopsis => "mv SOURCE... DEST";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var paths = args.Where(a => !a.StartsWith('-')).ToList();
        if (paths.Count < 2)
        {
            stderr.WriteLine("mv: missing destination file operand");
            return Task.FromResult(1);
        }

        var dest = paths[^1];
        var sources = paths.Take(paths.Count - 1).ToList();
        var winDest = PosixPathMapper.ToWindows(dest, context.CurrentDirectory);
        bool destIsDir = Directory.Exists(winDest);

        if (sources.Count > 1 && !destIsDir)
        {
            stderr.WriteLine($"mv: target '{dest}' is not a directory");
            return Task.FromResult(1);
        }

        int exitCode = 0;
        foreach (var src in sources)
        {
            var winSrc = PosixPathMapper.ToWindows(src, context.CurrentDirectory);

            if (File.Exists(winSrc))
            {
                try
                {
                    var finalDest = destIsDir ? Path.Combine(winDest, Path.GetFileName(winSrc)) : winDest;
                    if (File.Exists(finalDest)) File.Delete(finalDest);
                    File.Move(winSrc, finalDest);
                }
                catch (Exception ex)
                {
                    stderr.WriteLine($"mv: cannot move '{src}': {ex.Message}");
                    exitCode = 1;
                }
            }
            else if (Directory.Exists(winSrc))
            {
                try
                {
                    var finalDest = destIsDir ? Path.Combine(winDest, Path.GetFileName(winSrc)) : winDest;
                    if (Directory.Exists(finalDest)) Directory.Delete(finalDest, true);
                    Directory.Move(winSrc, finalDest);
                }
                catch (Exception ex)
                {
                    stderr.WriteLine($"mv: cannot move '{src}': {ex.Message}");
                    exitCode = 1;
                }
            }
            else
            {
                stderr.WriteLine($"mv: cannot stat '{src}': No such file or directory");
                exitCode = 1;
            }
        }

        return Task.FromResult(exitCode);
    }
}

public class TouchCommand : ICommand
{
    public string Name => "touch";
    public string Description => "Update the access and modification times of each FILE to the current time";
    public string Synopsis => "touch FILE...";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var targets = args.Where(a => !a.StartsWith('-')).ToList();
        if (targets.Count == 0)
        {
            stderr.WriteLine("touch: missing file operand");
            return Task.FromResult(1);
        }

        int exitCode = 0;
        foreach (var target in targets)
        {
            try
            {
                var winPath = PosixPathMapper.ToWindows(target, context.CurrentDirectory);
                if (File.Exists(winPath))
                {
                    File.SetLastWriteTime(winPath, DateTime.Now);
                }
                else
                {
                    var dir = Path.GetDirectoryName(winPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllBytes(winPath, Array.Empty<byte>());
                }
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"touch: cannot touch '{target}': {ex.Message}");
                exitCode = 1;
            }
        }

        return Task.FromResult(exitCode);
    }
}

public class FindCommand : ICommand
{
    public string Name => "find";
    public string Description => "Search for files in a directory hierarchy";
    public string Synopsis => "find [PATH] [-name PATTERN] [-type f|d] [-maxdepth N]";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        string rootPath = ".";
        string? namePattern = null;
        string? typeFilter = null; // "f" or "d"
        int maxDepth = int.MaxValue;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "-name" && i + 1 < args.Length)
            {
                namePattern = args[++i];
            }
            else if (arg == "-type" && i + 1 < args.Length)
            {
                typeFilter = args[++i];
            }
            else if (arg == "-maxdepth" && i + 1 < args.Length)
            {
                int.TryParse(args[++i], out maxDepth);
            }
            else if (!arg.StartsWith('-'))
            {
                rootPath = arg;
            }
        }

        var winRoot = PosixPathMapper.ToWindows(rootPath, context.CurrentDirectory);
        if (!Directory.Exists(winRoot) && !File.Exists(winRoot))
        {
            stderr.WriteLine($"find: '{rootPath}': No such file or directory");
            return Task.FromResult(1);
        }

        Regex? regex = null;
        if (!string.IsNullOrEmpty(namePattern))
        {
            var pattern = "^" + Regex.Escape(namePattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            regex = new Regex(pattern, RegexOptions.IgnoreCase);
        }

        Scan(winRoot, rootPath, 0, maxDepth, regex, typeFilter, stdout, ct);
        return Task.FromResult(0);
    }

    private static void Scan(string currentWinPath, string currentDisplayPath, int currentDepth, int maxDepth, Regex? regex, string? typeFilter, TextWriter stdout, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || currentDepth > maxDepth)
            return;

        if (Directory.Exists(currentWinPath))
        {
            if (typeFilter != "f" && (regex == null || regex.IsMatch(Path.GetFileName(currentWinPath) ?? string.Empty)))
            {
                stdout.WriteLine(currentDisplayPath.Replace('\\', '/'));
            }

            if (currentDepth == maxDepth)
                return;

            try
            {
                foreach (var dir in Directory.GetDirectories(currentWinPath))
                {
                    var name = Path.GetFileName(dir);
                    Scan(dir, $"{currentDisplayPath}/{name}", currentDepth + 1, maxDepth, regex, typeFilter, stdout, ct);
                }

                foreach (var file in Directory.GetFiles(currentWinPath))
                {
                    var name = Path.GetFileName(file);
                    if (typeFilter != "d" && (regex == null || regex.IsMatch(name)))
                    {
                        stdout.WriteLine($"{currentDisplayPath}/{name}".Replace('\\', '/'));
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Permission denied, skip
            }
        }
        else if (File.Exists(currentWinPath))
        {
            var name = Path.GetFileName(currentWinPath);
            if (typeFilter != "d" && (regex == null || regex.IsMatch(name)))
            {
                stdout.WriteLine(currentDisplayPath.Replace('\\', '/'));
            }
        }
    }
}

public class TreeCommand : ICommand
{
    public string Name => "tree";
    public string Description => "List contents of directories in a tree-like format";
    public string Synopsis => "tree [-L level] [-a] [DIRECTORY]";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        int maxDepth = 4;
        bool all = false;
        string rootPath = ".";

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-L" && i + 1 < args.Length)
            {
                int.TryParse(args[++i], out maxDepth);
            }
            else if (args[i] == "-a")
            {
                all = true;
            }
            else if (!args[i].StartsWith('-'))
            {
                rootPath = args[i];
            }
        }

        var winRoot = PosixPathMapper.ToWindows(rootPath, context.CurrentDirectory);
        if (!Directory.Exists(winRoot))
        {
            stderr.WriteLine($"tree: '{rootPath}': No such file or directory");
            return Task.FromResult(1);
        }

        stdout.WriteLine(rootPath);
        int dirCount = 0;
        int fileCount = 0;

        PrintBranch(winRoot, string.Empty, 1, maxDepth, all, stdout, ref dirCount, ref fileCount, ct);
        stdout.WriteLine();
        stdout.WriteLine($"{dirCount} directories, {fileCount} files");

        return Task.FromResult(0);
    }

    private static void PrintBranch(string path, string prefix, int depth, int maxDepth, bool all, TextWriter stdout, ref int dirCount, ref int fileCount, CancellationToken ct)
    {
        if (depth > maxDepth || ct.IsCancellationRequested) return;

        try
        {
            var di = new DirectoryInfo(path);
            var dirs = di.GetDirectories().Where(d => all || !d.Name.StartsWith('.')).OrderBy(d => d.Name).ToList();
            var files = di.GetFiles().Where(f => all || !f.Name.StartsWith('.')).OrderBy(f => f.Name).ToList();

            int total = dirs.Count + files.Count;
            int current = 0;

            foreach (var dir in dirs)
            {
                current++;
                bool isLast = current == total;
                stdout.WriteLine($"{prefix}{(isLast ? "└── " : "├── ")}{AnsiText.Bold}{AnsiText.BrightBlue}{dir.Name}{AnsiText.Reset}");
                dirCount++;
                PrintBranch(dir.FullName, prefix + (isLast ? "    " : "│   "), depth + 1, maxDepth, all, stdout, ref dirCount, ref fileCount, ct);
            }

            foreach (var file in files)
            {
                current++;
                bool isLast = current == total;
                stdout.WriteLine($"{prefix}{(isLast ? "└── " : "├── ")}{file.Name}");
                fileCount++;
            }
        }
        catch (UnauthorizedAccessException) { }
    }
}
