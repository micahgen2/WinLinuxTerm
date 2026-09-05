using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Common;

namespace LinuxTerm.Core.Commands;

public class TopCommand : ICommand
{
    public string Name => "top";
    public string Description => "Display system tasks and resource usage";
    public string Synopsis => "top [-b] [-n NUM] [-d SEC]";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        bool batch = false;
        int iterations = 1;
        int delayMs = 2000;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "-b") batch = true;
            else if (arg == "-n" && i + 1 < args.Length && int.TryParse(args[++i], out var n)) iterations = n;
            else if (arg == "-d" && i + 1 < args.Length && double.TryParse(args[++i], CultureInfo.InvariantCulture, out var d)) delayMs = (int)(d * 1000);
        }

        for (int it = 0; it < iterations; it++)
        {
            ct.ThrowIfCancellationRequested();

            if (!batch && it > 0)
            {
                await stdout.WriteAsync("\x1b[H\x1b[J");
            }

            var procs = Process.GetProcesses();
            int total = procs.Length;
            long totalMemBytes = 0;

            var procList = new List<(int Id, string Name, long WorkingSet, TimeSpan TotalCpu, int Threads)>();
            foreach (var p in procs)
            {
                try
                {
                    totalMemBytes += p.WorkingSet64;
                    procList.Add((p.Id, p.ProcessName, p.WorkingSet64, p.TotalProcessorTime, p.Threads.Count));
                }
                catch
                {
                    // Access denied for some system processes
                }
            }

            var now = DateTime.Now;
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            var uptimeStr = $"{(int)uptime.TotalDays} days, {uptime.Hours:D2}:{uptime.Minutes:D2}";

            await stdout.WriteLineAsync($"top - {now:HH:mm:ss} up {uptimeStr},  1 user,  load average: 0.15, 0.20, 0.18");
            await stdout.WriteLineAsync($"Tasks: {total,3} total,   1 running, {total - 1,3} sleeping,   0 stopped,   0 zombie");
            await stdout.WriteLineAsync($"%Cpu(s):  2.3 us,  1.2 sy,  0.0 ni, 96.1 id,  0.4 wa,  0.0 hi,  0.0 si,  0.0 st");
            long totalMemMb = (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024));
            if (totalMemMb == 0) totalMemMb = 16384;
            long usedMemMb = totalMemBytes / (1024 * 1024);
            long freeMemMb = Math.Max(0, totalMemMb - usedMemMb);
            await stdout.WriteLineAsync($"MiB Mem :  {totalMemMb,7} total,  {freeMemMb,7} free,  {usedMemMb,7} used,   2048 buff/cache");
            await stdout.WriteLineAsync();

            await stdout.WriteLineAsync($"{"PID",-7} {"USER",-9} {"PR",-3} {"NI",-3} {"VIRT",-7} {"RES",-7} {"SHR",-6} {"S",-2} {"%CPU",-5} {"%MEM",-5} {"TIME+",-9} COMMAND");
            
            // Sort by Memory / CPU
            var sorted = procList.OrderByDescending(p => p.WorkingSet).Take(20);
            foreach (var p in sorted)
            {
                ct.ThrowIfCancellationRequested();
                var memMb = $"{p.WorkingSet / (1024 * 1024)}M";
                var memPct = totalMemMb > 0 ? (p.WorkingSet / (1024.0 * 1024.0 * totalMemMb) * 100).ToString("F1", CultureInfo.InvariantCulture) : "0.0";
                var timeStr = $"{p.TotalCpu.Minutes:D2}:{p.TotalCpu.Seconds:D2}.{p.TotalCpu.Milliseconds / 10:D2}";
                await stdout.WriteLineAsync($"{p.Id,-7} {context.UserName,-9} {"20",-3} {"0",-3} {memMb,-7} {memMb,-7} {"12M",-6} {"S",-2} {"0.0",-5} {memPct,-5} {timeStr,-9} {p.Name}");
            }

            if (it < iterations - 1)
            {
                await Task.Delay(delayMs, ct);
            }
        }

        return 0;
    }
}

public class IdCommand : ICommand
{
    public string Name => "id";
    public string Description => "Print real and effective user and group IDs";
    public string Synopsis => "id [-u] [-g] [-G] [-n] [USER]";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        bool onlyUid = false;
        bool onlyGid = false;
        bool allGroups = false;
        bool nameOnly = false;
        string? targetUser = null;

        foreach (var arg in args)
        {
            if (arg == "-u") onlyUid = true;
            else if (arg == "-g") onlyGid = true;
            else if (arg == "-G") allGroups = true;
            else if (arg == "-n") nameOnly = true;
            else if (!arg.StartsWith('-')) targetUser = arg;
        }

        string user = targetUser ?? context.UserName;
        int uid = 1000;
        int gid = 1000;
        var groupList = new List<(int Id, string Name)>
        {
            (1000, user),
            (4, "adm"),
            (24, "cdrom"),
            (27, "sudo"),
            (30, "dip"),
            (46, "plugdev")
        };

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                if (identity.Groups != null)
                {
                    int autoGid = 1001;
                    foreach (var group in identity.Groups)
                    {
                        try
                        {
                            var name = group.Translate(typeof(NTAccount)).Value;
                            var slash = name.LastIndexOf('\\');
                            var cleanName = slash >= 0 ? name[(slash + 1)..] : name;
                            if (!groupList.Any(g => g.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase)))
                            {
                                groupList.Add((autoGid++, cleanName.ToLowerInvariant()));
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        if (onlyUid)
        {
            await stdout.WriteLineAsync(nameOnly ? user : uid.ToString());
            return 0;
        }

        if (onlyGid)
        {
            await stdout.WriteLineAsync(nameOnly ? user : gid.ToString());
            return 0;
        }

        if (allGroups)
        {
            if (nameOnly)
            {
                await stdout.WriteLineAsync(string.Join(" ", groupList.Select(g => g.Name)));
            }
            else
            {
                await stdout.WriteLineAsync(string.Join(" ", groupList.Select(g => g.Id.ToString())));
            }
            return 0;
        }

        // Default full output
        var groupsStr = string.Join(",", groupList.Select(g => $"{g.Id}({g.Name})"));
        await stdout.WriteLineAsync($"uid={uid}({user}) gid={gid}({user}) groups={groupsStr}");
        return 0;
    }
}

public class GroupsCommand : ICommand
{
    public string Name => "groups";
    public string Description => "Print group memberships for user";
    public string Synopsis => "groups [USER...]";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        var users = args.Where(a => !a.StartsWith('-')).ToList();
        if (users.Count == 0)
        {
            users.Add(context.UserName);
        }

        foreach (var u in users)
        {
            var grps = new List<string> { u, "adm", "dialout", "cdrom", "floppy", "sudo", "audio", "dip", "video", "plugdev", "netdev" };
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using var identity = WindowsIdentity.GetCurrent();
                    if (identity.Groups != null)
                    {
                        foreach (var group in identity.Groups)
                        {
                            try
                            {
                                var name = group.Translate(typeof(NTAccount)).Value;
                                var slash = name.LastIndexOf('\\');
                                var clean = slash >= 0 ? name[(slash + 1)..] : name;
                                if (!grps.Contains(clean.ToLowerInvariant()))
                                {
                                    grps.Add(clean.ToLowerInvariant());
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            if (users.Count == 1 && args.Length == 0)
            {
                await stdout.WriteLineAsync(string.Join(" ", grps));
            }
            else
            {
                await stdout.WriteLineAsync($"{u} : {string.Join(" ", grps)}");
            }
        }

        return 0;
    }
}

public class ArchCommand : ICommand
{
    public string Name => "arch";
    public string Description => "Print machine architecture";
    public string Synopsis => "arch";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x86_64",
            Architecture.Arm64 => "aarch64",
            Architecture.X86 => "i686",
            Architecture.Arm => "armv7l",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        };

        await stdout.WriteLineAsync(arch);
        return 0;
    }
}

public class PrintenvCommand : ICommand
{
    public string Name => "printenv";
    public string Description => "Print all or part of environment";
    public string Synopsis => "printenv [-0|--null] [VARIABLE...]";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        bool nullTerminated = false;
        var vars = new List<string>();

        foreach (var arg in args)
        {
            if (arg == "-0" || arg == "--null") nullTerminated = true;
            else if (!arg.StartsWith('-')) vars.Add(arg);
        }

        if (vars.Count == 0)
        {
            foreach (var kvp in context.EnvironmentVariables.OrderBy(k => k.Key))
            {
                if (nullTerminated)
                    await stdout.WriteAsync($"{kvp.Key}={kvp.Value}\0");
                else
                    await stdout.WriteLineAsync($"{kvp.Key}={kvp.Value}");
            }
            return 0;
        }

        int exitCode = 0;
        foreach (var v in vars)
        {
            if (context.EnvironmentVariables.TryGetValue(v, out var val))
            {
                if (nullTerminated)
                    await stdout.WriteAsync($"{val}\0");
                else
                    await stdout.WriteLineAsync(val);
            }
            else
            {
                exitCode = 1;
            }
        }

        return exitCode;
    }
}

public class SeqCommand : ICommand
{
    public string Name => "seq";
    public string Description => "Print sequences of numbers";
    public string Synopsis => "seq [-w] [-s STRING] [-f FORMAT] FIRST [INCREMENT] LAST | seq LAST";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        if (args.Length == 0)
        {
            await stderr.WriteLineAsync("seq: missing operand");
            return 1;
        }

        bool equalizeWidth = false;
        string separator = "\n";
        string? format = null;
        var operands = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "-w" || arg == "--equal-width") equalizeWidth = true;
            else if ((arg == "-s" || arg == "--separator") && i + 1 < args.Length) separator = args[++i];
            else if (arg.StartsWith("-s")) separator = arg[2..];
            else if ((arg == "-f" || arg == "--format") && i + 1 < args.Length) format = args[++i];
            else if (arg.StartsWith("-f")) format = arg[2..];
            else if (!arg.StartsWith('-')) operands.Add(arg);
        }

        if (operands.Count == 0)
        {
            await stderr.WriteLineAsync("seq: missing operand");
            return 1;
        }

        double first = 1;
        double increment = 1;
        double last;

        if (operands.Count == 1)
        {
            if (!double.TryParse(operands[0], CultureInfo.InvariantCulture, out last))
            {
                await stderr.WriteLineAsync($"seq: invalid floating point argument: '{operands[0]}'");
                return 1;
            }
        }
        else if (operands.Count == 2)
        {
            if (!double.TryParse(operands[0], CultureInfo.InvariantCulture, out first) ||
                !double.TryParse(operands[1], CultureInfo.InvariantCulture, out last))
            {
                await stderr.WriteLineAsync("seq: invalid argument");
                return 1;
            }
        }
        else
        {
            if (!double.TryParse(operands[0], CultureInfo.InvariantCulture, out first) ||
                !double.TryParse(operands[1], CultureInfo.InvariantCulture, out increment) ||
                !double.TryParse(operands[2], CultureInfo.InvariantCulture, out last))
            {
                await stderr.WriteLineAsync("seq: invalid argument");
                return 1;
            }
        }

        if (increment == 0)
        {
            await stderr.WriteLineAsync("seq: zero %s increment");
            return 1;
        }

        // Determine number of decimal places
        int decimals = 0;
        foreach (var op in operands)
        {
            int dot = op.IndexOf('.');
            if (dot >= 0) decimals = Math.Max(decimals, op.Length - dot - 1);
        }

        int width = 0;
        if (equalizeWidth)
        {
            width = Math.Max(first.ToString("F" + decimals, CultureInfo.InvariantCulture).Length,
                             last.ToString("F" + decimals, CultureInfo.InvariantCulture).Length);
        }

        bool isDefaultNewline = separator == "\n";
        bool firstOutput = true;
        for (double val = first; increment > 0 ? val <= last + 1e-9 : val >= last - 1e-9; val += increment)
        {
            ct.ThrowIfCancellationRequested();

            string formatted;
            if (format != null)
            {
                // Simple %g or %f format support
                formatted = val.ToString(CultureInfo.InvariantCulture);
            }
            else if (decimals > 0)
            {
                formatted = val.ToString("F" + decimals, CultureInfo.InvariantCulture);
                if (equalizeWidth) formatted = formatted.PadLeft(width, '0');
            }
            else
            {
                formatted = ((long)Math.Round(val)).ToString(CultureInfo.InvariantCulture);
                if (equalizeWidth) formatted = formatted.PadLeft(width, '0');
            }

            if (isDefaultNewline)
            {
                await stdout.WriteLineAsync(formatted);
            }
            else
            {
                if (!firstOutput)
                {
                    await stdout.WriteAsync(separator);
                }
                await stdout.WriteAsync(formatted);
            }
            firstOutput = false;
        }

        if (!firstOutput && !isDefaultNewline)
        {
            await stdout.WriteLineAsync();
        }

        return 0;
    }
}

public class YesCommand : ICommand
{
    public string Name => "yes";
    public string Description => "Output a string repeatedly until killed";
    public string Synopsis => "yes [STRING...]";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        string text = args.Length > 0 ? string.Join(" ", args) : "y";
        var chunk = new StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            chunk.AppendLine(text);
        }
        string block = chunk.ToString();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await stdout.WriteAsync(block);
                await stdout.FlushAsync(ct);

                // Prevent memory exhaustion if piped into an in-memory StringWriter
                if (stdout is StringWriter sw && sw.GetStringBuilder().Length >= 64 * 1024)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Clean exit when piped or cancelled
        }

        return 0;
    }
}

public class TrueCommand : ICommand
{
    public string Name => "true";
    public string Description => "Do nothing, successfully";
    public string Synopsis => "true";

    public Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        return Task.FromResult(0);
    }
}

public class FalseCommand : ICommand
{
    public string Name => "false";
    public string Description => "Do nothing, unsuccessfully";
    public string Synopsis => "false";

    public Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        return Task.FromResult(1);
    }
}

public class CalCommand : ICommand
{
    public string Name => "cal";
    public string Description => "Display a calendar";
    public string Synopsis => "cal [[MONTH] YEAR] [-3] [-y]";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        bool show3 = false;
        bool showYear = false;
        var nums = new List<int>();

        foreach (var arg in args)
        {
            if (arg == "-3") show3 = true;
            else if (arg == "-y" || arg == "--year") showYear = true;
            else if (int.TryParse(arg, out var n)) nums.Add(n);
        }

        var today = DateTime.Today;
        int targetMonth = today.Month;
        int targetYear = today.Year;

        if (nums.Count == 1)
        {
            if (nums[0] > 12)
            {
                targetYear = nums[0];
                showYear = true;
            }
            else
            {
                targetMonth = nums[0];
            }
        }
        else if (nums.Count >= 2)
        {
            targetMonth = nums[0];
            targetYear = nums[1];
        }

        if (showYear)
        {
            await stdout.WriteLineAsync($"{targetYear}".PadLeft(33));
            await stdout.WriteLineAsync();
            for (int q = 1; q <= 12; q += 3)
            {
                await PrintMonthRowAsync(targetYear, q, stdout);
            }
            return 0;
        }

        if (show3)
        {
            var prevDate = new DateTime(targetYear, targetMonth, 1).AddMonths(-1);
            var nextDate = new DateTime(targetYear, targetMonth, 1).AddMonths(1);

            await stdout.WriteLineAsync(FormatMonth(prevDate.Year, prevDate.Month, false));
            await stdout.WriteLineAsync();
            await stdout.WriteLineAsync(FormatMonth(targetYear, targetMonth, true));
            await stdout.WriteLineAsync();
            await stdout.WriteLineAsync(FormatMonth(nextDate.Year, nextDate.Month, false));
            return 0;
        }

        await stdout.WriteLineAsync(FormatMonth(targetYear, targetMonth, true));
        return 0;
    }

    private static string FormatMonth(int year, int month, bool isCurrentMonth)
    {
        var sb = new StringBuilder();
        var dt = new DateTime(year, month, 1);
        string header = $"{dt:MMMM yyyy}";
        sb.AppendLine(header.PadLeft(10 + header.Length / 2));
        sb.AppendLine("Su Mo Tu We Th Fr Sa");

        int dayOfWeek = (int)dt.DayOfWeek;
        sb.Append(new string(' ', dayOfWeek * 3));

        int daysInMonth = DateTime.DaysInMonth(year, month);
        for (int day = 1; day <= daysInMonth; day++)
        {
            sb.Append($"{day,2} ");
            dayOfWeek = (dayOfWeek + 1) % 7;
            if (dayOfWeek == 0 && day != daysInMonth)
            {
                sb.AppendLine();
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static async Task PrintMonthRowAsync(int year, int startMonth, TextWriter stdout)
    {
        // 3 months side by side
        var m1 = new DateTime(year, startMonth, 1);
        var m2 = new DateTime(year, startMonth + 1, 1);
        var m3 = new DateTime(year, startMonth + 2, 1);

        var h1 = m1.ToString("MMMM").PadLeft(10 + m1.ToString("MMMM").Length / 2).PadRight(20);
        var h2 = m2.ToString("MMMM").PadLeft(10 + m2.ToString("MMMM").Length / 2).PadRight(20);
        var h3 = m3.ToString("MMMM").PadLeft(10 + m3.ToString("MMMM").Length / 2).PadRight(20);
        await stdout.WriteLineAsync($"{h1}  {h2}  {h3}");

        await stdout.WriteLineAsync("Su Mo Tu We Th Fr Sa  Su Mo Tu We Th Fr Sa  Su Mo Tu We Th Fr Sa");

        var g1 = GetMonthGrid(year, startMonth);
        var g2 = GetMonthGrid(year, startMonth + 1);
        var g3 = GetMonthGrid(year, startMonth + 2);

        for (int r = 0; r < 6; r++)
        {
            var line1 = r < g1.Count ? g1[r].PadRight(20) : new string(' ', 20);
            var line2 = r < g2.Count ? g2[r].PadRight(20) : new string(' ', 20);
            var line3 = r < g3.Count ? g3[r].PadRight(20) : new string(' ', 20);
            await stdout.WriteLineAsync($"{line1}  {line2}  {line3}".TrimEnd());
        }
    }

    private static List<string> GetMonthGrid(int year, int month)
    {
        var lines = new List<string>();
        var dt = new DateTime(year, month, 1);
        int dow = (int)dt.DayOfWeek;
        var sb = new StringBuilder();
        sb.Append(new string(' ', dow * 3));

        int days = DateTime.DaysInMonth(year, month);
        for (int d = 1; d <= days; d++)
        {
            sb.Append($"{d,2} ");
            dow = (dow + 1) % 7;
            if (dow == 0 || d == days)
            {
                lines.Add(sb.ToString().TrimEnd());
                sb.Clear();
            }
        }
        return lines;
    }
}

public class ShufCommand : ICommand
{
    public string Name => "shuf";
    public string Description => "Generate random permutations";
    public string Synopsis => "shuf [-e] [-i LO-HI] [-n COUNT] [-o FILE] [FILE]";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        bool echo = false;
        string? rangeStr = null;
        int? count = null;
        string? outputFile = null;
        var items = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "-e" || arg == "--echo") echo = true;
            else if ((arg == "-i" || arg == "--input-range") && i + 1 < args.Length) rangeStr = args[++i];
            else if (arg.StartsWith("-i")) rangeStr = arg[2..];
            else if ((arg == "-n" || arg == "--head-count") && i + 1 < args.Length && int.TryParse(args[++i], out var c)) count = c;
            else if (arg.StartsWith("-n") && int.TryParse(arg[2..], out var c2)) count = c2;
            else if ((arg == "-o" || arg == "--output") && i + 1 < args.Length) outputFile = args[++i];
            else if (arg.StartsWith("-o")) outputFile = arg[2..];
            else if (!arg.StartsWith('-') || arg == "-") items.Add(arg);
        }

        var lines = new List<string>();

        if (rangeStr != null)
        {
            var parts = rangeStr.Split('-');
            if (parts.Length == 2 && long.TryParse(parts[0], out var lo) && long.TryParse(parts[1], out var hi) && lo <= hi)
            {
                for (long v = lo; v <= hi; v++)
                {
                    lines.Add(v.ToString());
                }
            }
            else
            {
                await stderr.WriteLineAsync($"shuf: invalid input range: '{rangeStr}'");
                return 1;
            }
        }
        else if (echo)
        {
            lines.AddRange(items);
        }
        else
        {
            string sourceFile = items.Count > 0 ? items[0] : "-";
            if (sourceFile == "-")
            {
                string? l;
                while ((l = await stdin.ReadLineAsync(ct)) != null)
                {
                    lines.Add(l);
                }
            }
            else
            {
                var win = PosixPathMapper.ToWindows(sourceFile, context.CurrentDirectory);
                if (!File.Exists(win))
                {
                    await stderr.WriteLineAsync($"shuf: {sourceFile}: No such file or directory");
                    return 1;
                }
                lines.AddRange(await File.ReadAllLinesAsync(win, ct));
            }
        }

        // Fisher-Yates shuffle
        for (int i = lines.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (lines[i], lines[j]) = (lines[j], lines[i]);
        }

        var take = count.HasValue ? lines.Take(count.Value).ToList() : lines;

        if (outputFile != null)
        {
            var winOut = PosixPathMapper.ToWindows(outputFile, context.CurrentDirectory);
            await File.WriteAllLinesAsync(winOut, take, ct);
        }
        else
        {
            foreach (var l in take)
            {
                await stdout.WriteLineAsync(l);
            }
        }

        return 0;
    }
}
