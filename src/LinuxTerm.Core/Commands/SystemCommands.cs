using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Common;

namespace LinuxTerm.Core.Commands;

public class UnameCommand : ICommand
{
    public string Name => "uname";
    public string Description => "Print system information";
    public string Synopsis => "uname [-a] [-s] [-n] [-r] [-v] [-m] [-o]";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool all = false;
        bool kernelName = false;
        bool nodename = false;
        bool kernelRelease = false;
        bool kernelVersion = false;
        bool machine = false;
        bool operatingSystem = false;

        foreach (var arg in args)
        {
            if (arg.StartsWith('-') && arg.Length > 1)
            {
                foreach (char c in arg[1..])
                {
                    switch (c)
                    {
                        case 'a': all = true; break;
                        case 's': kernelName = true; break;
                        case 'n': nodename = true; break;
                        case 'r': kernelRelease = true; break;
                        case 'v': kernelVersion = true; break;
                        case 'm': machine = true; break;
                        case 'o': operatingSystem = true; break;
                    }
                }
            }
        }

        if (!all && !kernelName && !nodename && !kernelRelease && !kernelVersion && !machine && !operatingSystem)
        {
            kernelName = true; // default is -s
        }

        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x86_64",
            Architecture.Arm64 => "aarch64",
            Architecture.X86 => "i686",
            Architecture.Arm => "armv7l",
            _ => "unknown"
        };

        var parts = new System.Collections.Generic.List<string>();

        if (all || kernelName) parts.Add("Linux");
        if (all || nodename) parts.Add(context.HostName);
        if (all || kernelRelease) parts.Add("6.6.0-win-posix");
        if (all || kernelVersion) parts.Add("#1 SMP PREEMPT_DYNAMIC");
        if (all || machine) parts.Add(arch);
        if (all || operatingSystem) parts.Add("GNU/Linux");

        stdout.WriteLine(string.Join(" ", parts));
        return Task.FromResult(0);
    }
}

public class WhoamiCommand : ICommand
{
    public string Name => "whoami";
    public string Description => "Print effective userid";
    public string Synopsis => "whoami";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        stdout.WriteLine(context.UserName);
        return Task.FromResult(0);
    }
}

public class HostnameCommand : ICommand
{
    public string Name => "hostname";
    public string Description => "Show or set system host name";
    public string Synopsis => "hostname";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        stdout.WriteLine(context.HostName);
        return Task.FromResult(0);
    }
}

public class DateCommand : ICommand
{
    public string Name => "date";
    public string Description => "Display the current time in the given FORMAT";
    public string Synopsis => "date [+FORMAT]";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var now = DateTimeOffset.Now;
        var formatArg = args.FirstOrDefault(a => a.StartsWith('+'));

        if (formatArg != null)
        {
            string fmt = formatArg[1..]
                .Replace("%Y", now.ToString("yyyy"))
                .Replace("%m", now.ToString("MM"))
                .Replace("%d", now.ToString("dd"))
                .Replace("%H", now.ToString("HH"))
                .Replace("%M", now.ToString("mm"))
                .Replace("%S", now.ToString("ss"))
                .Replace("%s", now.ToUnixTimeSeconds().ToString())
                .Replace("%F", now.ToString("yyyy-MM-dd"))
                .Replace("%T", now.ToString("HH:mm:ss"));
            stdout.WriteLine(fmt);
        }
        else
        {
            // Standard POSIX date output: "Fri Sep  4 18:54:23 EDT 2026"
            stdout.WriteLine(now.ToString("ddd MMM d HH:mm:ss zzz yyyy"));
        }

        return Task.FromResult(0);
    }
}

public class UptimeCommand : ICommand
{
    public string Name => "uptime";
    public string Description => "Tell how long the system has been running";
    public string Synopsis => "uptime";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        var now = DateTime.Now.ToString("HH:mm:ss");
        string upFormatted = uptime.Days > 0
            ? $"{uptime.Days} days, {uptime.Hours:D2}:{uptime.Minutes:D2}"
            : $"{uptime.Hours:D2}:{uptime.Minutes:D2}";

        stdout.WriteLine($" {now} up {upFormatted},  1 user,  load average: 0.08, 0.04, 0.01");
        return Task.FromResult(0);
    }
}

public class DfCommand : ICommand
{
    public string Name => "df";
    public string Description => "Report file system disk space usage";
    public string Synopsis => "df [-h]";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool human = args.Contains("-h");

        stdout.WriteLine($"{"Filesystem",-15} {(human ? "Size" : "1K-blocks"),10} {"Used",10} {"Avail",10} {"Use%",6} {"Mounted on"}");

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;

            try
            {
                var letter = drive.Name.TrimEnd('\\', ':').ToLowerInvariant();
                var mount = $"/{letter}";
                long total = drive.TotalSize;
                long free = drive.AvailableFreeSpace;
                long used = total - free;
                double pct = total > 0 ? (double)used / total * 100 : 0;

                string totalStr = human ? FormatSize(total) : (total / 1024).ToString();
                string usedStr = human ? FormatSize(used) : (used / 1024).ToString();
                string freeStr = human ? FormatSize(free) : (free / 1024).ToString();

                stdout.WriteLine($"{drive.Name,-15} {totalStr,10} {usedStr,10} {freeStr,10} {$"{pct:0}%",6} {mount}");
            }
            catch { }
        }

        return Task.FromResult(0);
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

public class FreeCommand : ICommand
{
    public string Name => "free";
    public string Description => "Display amount of free and used memory in the system";
    public string Synopsis => "free [-h] [-m] [-g]";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool human = args.Contains("-h");
        bool mega = args.Contains("-m");
        bool giga = args.Contains("-g");

        var memInfo = GC.GetGCMemoryInfo();
        long totalRam = memInfo.TotalAvailableMemoryBytes;
        long procUsed = Process.GetCurrentProcess().WorkingSet64;
        long freeRam = Math.Max(0, totalRam - procUsed);
        long buffCache = totalRam / 8; // Emulated buffer/cache
        long available = freeRam + buffCache;

        Func<long, string> formatter = b =>
        {
            if (human) return FormatSize(b);
            if (giga) return (b / (1024 * 1024 * 1024)).ToString();
            if (mega) return (b / (1024 * 1024)).ToString();
            return (b / 1024).ToString(); // default KB
        };

        string unitHeader = human ? "" : (mega ? " (MiB)" : (giga ? " (GiB)" : " (KiB)"));

        stdout.WriteLine($"{"",-10} {$"total{unitHeader}",12} {"used",12} {"free",12} {"shared",12} {"buff/cache",12} {"available",12}");
        stdout.WriteLine($"{"Mem:",-10} {formatter(totalRam),12} {formatter(procUsed),12} {formatter(freeRam),12} {"0",12} {formatter(buffCache),12} {formatter(available),12}");
        stdout.WriteLine($"{"Swap:",-10} {formatter(totalRam / 2),12} {"0",12} {formatter(totalRam / 2),12}");

        return Task.FromResult(0);
    }

    private static string FormatSize(long bytes)
    {
        string[] suffixes = { "B", "Ki", "Mi", "Gi", "Ti" };
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

public class PsCommand : ICommand
{
    public string Name => "ps";
    public string Description => "Report a snapshot of the current processes";
    public string Synopsis => "ps [-a] [-u]";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool detailed = args.Contains("-u") || args.Contains("-l");

        if (detailed)
        {
            stdout.WriteLine($"{"USER",-12} {"PID",8} {"%CPU",6} {"%MEM",6} {"VSZ(MB)",10} {"RSS(MB)",10} {"COMMAND"}");
        }
        else
        {
            stdout.WriteLine($"{"PID",8} {"TTY",8} {"TIME",10} {"CMD"}");
        }

        var processes = Process.GetProcesses().OrderBy(p => p.Id).ToList();

        foreach (var p in processes)
        {
            try
            {
                if (detailed)
                {
                    double memMb = p.WorkingSet64 / (1024.0 * 1024.0);
                    stdout.WriteLine($"{context.UserName,-12} {p.Id,8} {"0.0",6} {"0.1",6} {memMb,10:0.0} {memMb,10:0.0} {p.ProcessName}");
                }
                else
                {
                    stdout.WriteLine($"{p.Id,8} {"pts/0",8} {"00:00:00",10} {p.ProcessName}");
                }
            }
            catch { }
        }

        return Task.FromResult(0);
    }
}

public class KillCommand : ICommand
{
    public string Name => "kill";
    public string Description => "Send a signal to a process";
    public string Synopsis => "kill [-9] PID...";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var pids = new System.Collections.Generic.List<int>();

        foreach (var arg in args)
        {
            if (arg is "-9" or "-SIGKILL" or "-KILL") continue;
            if (int.TryParse(arg, out int pid)) pids.Add(pid);
            else stderr.WriteLine($"kill: invalid PID: '{arg}'");
        }

        if (pids.Count == 0)
        {
            stderr.WriteLine("kill: usage: kill [-9] PID...");
            return Task.FromResult(1);
        }

        int exitCode = 0;
        foreach (var pid in pids)
        {
            try
            {
                var proc = Process.GetProcessById(pid);
                proc.Kill();
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"kill: ({pid}) - {ex.Message}");
                exitCode = 1;
            }
        }

        return Task.FromResult(exitCode);
    }
}

public class WhichCommand : ICommand
{
    private readonly CommandRegistry _registry;

    public WhichCommand(CommandRegistry registry)
    {
        _registry = registry;
    }

    public string Name => "which";
    public string Description => "Locate a command";
    public string Synopsis => "which COMMAND...";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine("which: missing argument");
            return Task.FromResult(1);
        }

        int exitCode = 0;
        foreach (var cmd in args)
        {
            if (_registry.HasCommand(cmd))
            {
                stdout.WriteLine($"{cmd}: shell built-in command");
                continue;
            }

            var path = FindInPath(cmd, context);
            if (path != null)
            {
                stdout.WriteLine(PosixPathMapper.ToPosix(path));
            }
            else
            {
                stderr.WriteLine($"which: no {cmd} in ({context.EnvironmentVariables.GetValueOrDefault("PATH", "")})");
                exitCode = 1;
            }
        }

        return Task.FromResult(exitCode);
    }

    public static string? FindInPath(string binaryName, ShellContext context)
    {
        var pathVar = context.EnvironmentVariables.GetValueOrDefault("PATH", Environment.GetEnvironmentVariable("PATH") ?? "");
        var dirs = pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        var exts = new[] { "", ".exe", ".cmd", ".bat", ".ps1", ".com" };

        foreach (var dir in dirs)
        {
            foreach (var ext in exts)
            {
                var full = Path.Combine(dir, binaryName + ext);
                if (File.Exists(full))
                    return full;
            }
        }

        return null;
    }
}

public class ClearCommand : ICommand
{
    public string Name => "clear";
    public string Description => "Clear the terminal screen";
    public string Synopsis => "clear";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        // Emit ANSI clear screen and move cursor home
        stdout.Write("\x1b[2J\x1b[H");
        return Task.FromResult(0);
    }
}
