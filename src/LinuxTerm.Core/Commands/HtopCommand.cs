using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Common;

namespace LinuxTerm.Core.Commands;

public class HtopCommand : ICommand
{
    public string Name => "htop";
    public string Description => "Interactive process viewer and system monitor";
    public string Synopsis => "htop [-d DELAY] [-u USER] [-p PID,...] [-s SORT_KEY] [-n NUM] [-b]";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        int delayMs = 1500;
        string? filterUser = null;
        var filterPids = new HashSet<int>();
        string sortKey = "CPU";
        bool batch = false;
        int maxIterations = -1;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if ((arg == "-d" || arg == "--delay") && i + 1 < args.Length)
            {
                if (double.TryParse(args[++i], CultureInfo.InvariantCulture, out var d) && d > 0)
                    delayMs = (int)(d * 100);
            }
            else if (arg.StartsWith("-d") && double.TryParse(arg[2..], CultureInfo.InvariantCulture, out var d2) && d2 > 0)
            {
                delayMs = (int)(d2 * 100);
            }
            else if ((arg == "-u" || arg == "--user") && i + 1 < args.Length)
            {
                filterUser = args[++i];
            }
            else if (arg.StartsWith("--user="))
            {
                filterUser = arg["--user=".Length..];
            }
            else if ((arg == "-p" || arg == "--pid") && i + 1 < args.Length)
            {
                ParsePids(args[++i], filterPids);
            }
            else if (arg.StartsWith("-p"))
            {
                ParsePids(arg[2..], filterPids);
            }
            else if ((arg == "-s" || arg == "--sort-key") && i + 1 < args.Length)
            {
                sortKey = args[++i].ToUpperInvariant();
            }
            else if (arg.StartsWith("--sort-key="))
            {
                sortKey = arg["--sort-key=".Length..].ToUpperInvariant();
            }
            else if (arg == "-b" || arg == "--batch")
            {
                batch = true;
            }
            else if ((arg == "-n" || arg == "--iterations") && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var n)) maxIterations = n;
            }
            else if (arg == "-h" || arg == "--help")
            {
                await stdout.WriteLineAsync("htop - interactive process viewer");
                await stdout.WriteLineAsync("Usage: htop [options]");
                await stdout.WriteLineAsync("  -d, --delay=DELAY    Delay between updates, in tenths of a second (default 15)");
                await stdout.WriteLineAsync("  -u, --user=USERNAME  Show only processes of a given user");
                await stdout.WriteLineAsync("  -p, --pid=PID,...    Show only the given PIDs");
                await stdout.WriteLineAsync("  -s, --sort-key=KEY   Sort by column (PID, USER, PRI, NI, VIRT, RES, CPU, MEM, TIME, COMMAND)");
                await stdout.WriteLineAsync("  -n, --iterations=N   Exit after N iterations");
                await stdout.WriteLineAsync("  -b, --batch          Output in batch mode (snapshot without screen redraw)");
                await stdout.WriteLineAsync("  -h, --help           Print this help screen");
                return 0;
            }
        }

        // If stdout is an in-memory buffer (e.g. piped in ShellEngine or redirected), default to single snapshot
        if (stdout is StringWriter)
        {
            batch = true;
            if (maxIterations <= 0) maxIterations = 1;
        }

        int iteration = 0;
        var rnd = new Random();

        while (!ct.IsCancellationRequested)
        {
            iteration++;

            var frame = RenderFrame(context, filterUser, filterPids, sortKey, rnd);
            if (!batch && iteration > 1)
            {
                await stdout.WriteAsync("\x1b[H\x1b[J");
            }
            await stdout.WriteAsync(frame);
            await stdout.FlushAsync(ct);

            if (batch && (maxIterations <= 0 || iteration >= maxIterations))
            {
                break;
            }

            if (maxIterations > 0 && iteration >= maxIterations)
            {
                break;
            }

            // In interactive console, check if user pressed 'q' or 'Q' to quit
            if (!Console.IsInputRedirected && Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape)
                {
                    break;
                }
            }

            try
            {
                await Task.Delay(delayMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return 0;
    }

    private static void ParsePids(string pidStr, HashSet<int> set)
    {
        var parts = pidStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (int.TryParse(p.Trim(), out var pid))
            {
                set.Add(pid);
            }
        }
    }

    private static string RenderFrame(
        ShellContext context,
        string? filterUser,
        HashSet<int> filterPids,
        string sortKey,
        Random rnd)
    {
        var sb = new StringBuilder();

        string reset = AnsiText.Reset;
        string bold = AnsiText.Bold;
        string green = AnsiText.BrightGreen;
        string cyan = AnsiText.BrightCyan;
        string yellow = AnsiText.BrightYellow;
        string red = AnsiText.BrightRed;
        string white = AnsiText.BrightWhite;
        string headerBg = "\x1b[46;30m"; // Cyan background, black text
        string fnNumBg = "\x1b[46;30m";  // Cyan background, black text

        // 1. Gather System Metrics
        int coreCount = Environment.ProcessorCount;
        int displayCores = Math.Min(coreCount, 8); // Display up to 8 core meters side-by-side or stacked

        var memInfo = GC.GetGCMemoryInfo();
        long totalRamBytes = memInfo.TotalAvailableMemoryBytes;
        if (totalRamBytes <= 0) totalRamBytes = 16L * 1024 * 1024 * 1024;

        var allProcs = Process.GetProcesses();
        int totalTasks = allProcs.Length;
        int totalThreads = 0;
        long totalWorkingSet = 0;

        var procEntries = new List<HtopProcessEntry>();
        foreach (var p in allProcs)
        {
            try
            {
                totalWorkingSet += p.WorkingSet64;
                totalThreads += p.Threads.Count;

                if (filterPids.Count > 0 && !filterPids.Contains(p.Id))
                    continue;

                string procUser = context.UserName;
                if (filterUser != null && !procUser.Equals(filterUser, StringComparison.OrdinalIgnoreCase))
                    continue;

                procEntries.Add(new HtopProcessEntry
                {
                    Pid = p.Id,
                    User = procUser,
                    Priority = 20,
                    Nice = 0,
                    VirtualBytes = p.VirtualMemorySize64,
                    ResidentBytes = p.WorkingSet64,
                    SharedBytes = Math.Min(p.WorkingSet64, 32L * 1024 * 1024),
                    State = 'S',
                    CpuPercent = Math.Round(rnd.NextDouble() * 3.5, 1),
                    MemPercent = totalRamBytes > 0 ? Math.Round((double)p.WorkingSet64 / totalRamBytes * 100, 1) : 0.0,
                    CpuTime = p.TotalProcessorTime,
                    Command = p.ProcessName
                });
            }
            catch
            {
                // Access denied for protected system processes
            }
        }

        long usedRamBytes = totalWorkingSet;
        double memPercent = Math.Clamp((double)usedRamBytes / totalRamBytes * 100, 0, 100);

        // Uptime & Load
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        string uptimeStr = $"{(int)uptime.TotalDays} days, {uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
        string loadStr = "0.28, 0.35, 0.31";

        // 2. Render Header Meters (Left & Right columns)
        int meterWidth = 24;

        // Render Cores in pairs: core 1 & core (displayCores/2 + 1)
        int halfCores = (displayCores + 1) / 2;
        for (int c = 0; c < halfCores; c++)
        {
            int core1 = c + 1;
            int core2 = c + 1 + halfCores;

            double pct1 = 15.0 + (c * 7.3) % 45.0 + rnd.NextDouble() * 8.0;
            string bar1 = FormatBar(pct1, meterWidth, green, cyan, red, reset);
            string leftCol = $"{cyan}{core1,-2}{reset}[{bar1}{cyan}{pct1,5:F1}%{reset}]";

            string rightCol = "";
            if (core2 <= displayCores)
            {
                double pct2 = 12.0 + (core2 * 6.7) % 50.0 + rnd.NextDouble() * 8.0;
                string bar2 = FormatBar(pct2, meterWidth, green, cyan, red, reset);
                rightCol = $"{cyan}{core2,-2}{reset}[{bar2}{cyan}{pct2,5:F1}%{reset}]";
            }
            else if (c == 0)
            {
                rightCol = $"{bold}Tasks:{reset} {totalTasks}, {totalThreads} thr; {green}1 running{reset}";
            }
            else if (c == 1)
            {
                rightCol = $"{bold}Load average:{reset} {loadStr}";
            }
            else if (c == 2)
            {
                rightCol = $"{bold}Uptime:{reset} {uptimeStr}";
            }

            sb.AppendLine($"{leftCol}   {rightCol}");
        }

        // Memory and Swap Meters
        string memStr = $"{FormatSize(usedRamBytes)}/{FormatSize(totalRamBytes)}";
        string memBar = FormatBar(memPercent, meterWidth, green, cyan, yellow, reset);
        string memLine = $"{cyan}Mem{reset}[{memBar}{cyan}{memStr,11}{reset}]";
        string taskStat = $"{bold}Tasks:{reset} {totalTasks}, {totalThreads} thr; {green}1 running{reset}";
        sb.AppendLine($"{memLine}   {taskStat}");

        string swpStr = "0K/0K";
        string swpBar = FormatBar(0, meterWidth, green, cyan, red, reset);
        string swpLine = $"{cyan}Swp{reset}[{swpBar}{cyan}{swpStr,11}{reset}]";
        string loadStat = $"{bold}Load average:{reset} {loadStr}";
        sb.AppendLine($"{swpLine}   {loadStat}");

        string uptimeStat = $"{bold}Uptime:{reset} {uptimeStr}";
        sb.AppendLine($"{new string(' ', 40)}   {uptimeStat}");

        // 3. Process Table Header
        sb.AppendLine();
        sb.AppendLine($"{headerBg}{"  PID",-6} {"USER",-9} {"PRI",-4} {"NI",-3} {"VIRT",-6} {"RES",-6} {"SHR",-6} {"S",-2} {"CPU%",-6} {"MEM%",-6} {"TIME+",-9} {"Command",-24}{reset}");

        // 4. Sort Processes
        IEnumerable<HtopProcessEntry> sorted = sortKey switch
        {
            "PID" => procEntries.OrderBy(p => p.Pid),
            "USER" => procEntries.OrderBy(p => p.User),
            "PRI" => procEntries.OrderByDescending(p => p.Priority),
            "NI" => procEntries.OrderBy(p => p.Nice),
            "VIRT" => procEntries.OrderByDescending(p => p.VirtualBytes),
            "RES" => procEntries.OrderByDescending(p => p.ResidentBytes),
            "SHR" => procEntries.OrderByDescending(p => p.SharedBytes),
            "MEM" => procEntries.OrderByDescending(p => p.MemPercent),
            "TIME" => procEntries.OrderByDescending(p => p.CpuTime),
            "COMMAND" => procEntries.OrderBy(p => p.Command),
            _ => procEntries.OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.ResidentBytes)
        };

        // Render up to 18 processes
        foreach (var p in sorted.Take(18))
        {
            string timeFormatted = $"{p.CpuTime.Minutes + p.CpuTime.Hours * 60:D2}:{p.CpuTime.Seconds:D2}.{p.CpuTime.Milliseconds / 10:D2}";
            string cpuFormatted = p.CpuPercent.ToString("F1", CultureInfo.InvariantCulture);
            string memFormatted = p.MemPercent.ToString("F1", CultureInfo.InvariantCulture);

            string pidColor = cyan;
            string userColor = green;
            string cmdColor = p.CpuPercent > 2.0 ? $"{bold}{white}" : white;

            sb.Append($"{pidColor}{p.Pid,5}{reset} ");
            sb.Append($"{userColor}{p.User,-9}{reset} ");
            sb.Append($"{p.Priority,3} ");
            sb.Append($"{p.Nice,3} ");
            sb.Append($"{FormatSize(p.VirtualBytes),6} ");
            sb.Append($"{FormatSize(p.ResidentBytes),6} ");
            sb.Append($"{FormatSize(p.SharedBytes),6} ");
            sb.Append($"{green}{p.State}{reset}  ");
            sb.Append($"{green}{cpuFormatted,5}{reset} ");
            sb.Append($"{green}{memFormatted,5}{reset} ");
            sb.Append($"{yellow}{timeFormatted,9}{reset} ");
            sb.Append($"{cmdColor}{p.Command}{reset}");
            sb.AppendLine();
        }

        // 5. Bottom Footer (Iconic htop F1-F10 bar)
        sb.AppendLine();
        sb.Append($"{fnNumBg}F1{reset}Help  ");
        sb.Append($"{fnNumBg}F2{reset}Setup ");
        sb.Append($"{fnNumBg}F3{reset}Search");
        sb.Append($"{fnNumBg}F4{reset}Filter");
        sb.Append($"{fnNumBg}F5{reset}Tree  ");
        sb.Append($"{fnNumBg}F6{reset}SortBy");
        sb.Append($"{fnNumBg}F7{reset}Nice -");
        sb.Append($"{fnNumBg}F8{reset}Nice +");
        sb.Append($"{fnNumBg}F9{reset}Kill  ");
        sb.Append($"{fnNumBg}F10{reset}Quit");
        sb.AppendLine();

        return sb.ToString();
    }

    private static string FormatBar(double percent, int width, string color1, string color2, string color3, string reset)
    {
        percent = Math.Clamp(percent, 0, 100);
        int filled = (int)Math.Round(percent / 100.0 * width);

        var sb = new StringBuilder();
        for (int i = 0; i < width; i++)
        {
            if (i < filled)
            {
                double fraction = (double)i / width;
                if (fraction < 0.5) sb.Append(color1);
                else if (fraction < 0.8) sb.Append(color2);
                else sb.Append(color3);
                sb.Append('|');
            }
            else
            {
                sb.Append(' ');
            }
        }
        sb.Append(reset);
        return sb.ToString();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{(bytes / (1024.0 * 1024 * 1024)):F1}G";
        if (bytes >= 1024L * 1024)
            return $"{bytes / (1024 * 1024)}M";
        if (bytes >= 1024L)
            return $"{bytes / 1024}K";
        return $"{bytes}B";
    }

    private class HtopProcessEntry
    {
        public int Pid { get; set; }
        public string User { get; set; } = string.Empty;
        public int Priority { get; set; }
        public int Nice { get; set; }
        public long VirtualBytes { get; set; }
        public long ResidentBytes { get; set; }
        public long SharedBytes { get; set; }
        public char State { get; set; }
        public double CpuPercent { get; set; }
        public double MemPercent { get; set; }
        public TimeSpan CpuTime { get; set; }
        public string Command { get; set; } = string.Empty;
    }
}
