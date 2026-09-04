using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Common;

namespace LinuxTerm.Core.Commands;

public class NeofetchCommand : ICommand
{
    public string Name => "neofetch";
    public string Description => "Display system information and ASCII logo";
    public string Synopsis => "neofetch";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var mem = GC.GetGCMemoryInfo();
        long totalRamMb = mem.TotalAvailableMemoryBytes / (1024 * 1024);
        long usedRamMb = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);

        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        string uptimeStr = uptime.Days > 0 ? $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m" : $"{uptime.Hours}h {uptime.Minutes}m";

        string c = AnsiText.BrightCyan;
        string g = AnsiText.BrightGreen;
        string b = AnsiText.Bold;
        string r = AnsiText.Reset;
        string y = AnsiText.BrightYellow;

        var logo = new[]
        {
            $"{c}        #####         {r}",
            $"{c}       #######        {r}",
            $"{c}       ##{r}O{c}#{r}O{c}##        {r}",
            $"{c}       #<{y}###{c}>#        {r}",
            $"{c}      ######{y}###{c}       {r}",
            $"{c}     ####{y}#######{c}#     {r}",
            $"{c}    ####{y}#########{c}#    {r}",
            $"{c}    #{y}#{c}#{y}###########{c}##   {r}",
            $"{c}   #{y}#{c}#{y}#############{c}##  {r}",
            $"{c}   {y}#################  {r}",
            $"{c}    {y}###          ###  {r}"
        };

        var info = new[]
        {
            $"{b}{g}{context.UserName}{r}@{b}{g}{context.HostName}{r}",
            $"{b}------------------------{r}",
            $"{b}{y}OS:{r} WinLinuxTerm 1.0 (Windows / POSIX Hybrid)",
            $"{b}{y}Host:{r} {Environment.MachineName}",
            $"{b}{y}Kernel:{r} 6.6.0-win-posix #1 SMP",
            $"{b}{y}Uptime:{r} {uptimeStr}",
            $"{b}{y}Shell:{r} bash (LinuxTerm.Core .NET 10)",
            $"{b}{y}CPU:{r} {Environment.ProcessorCount} Cores ({RuntimeInformation.ProcessArchitecture})",
            $"{b}{y}Memory:{r} {usedRamMb}MiB / {totalRamMb}MiB",
            $"{b}{y}DotNet:{r} {Environment.Version}",
            $"\x1b[40m   \x1b[41m   \x1b[42m   \x1b[43m   \x1b[44m   \x1b[45m   \x1b[46m   \x1b[47m   {r}"
        };

        int max = Math.Max(logo.Length, info.Length);
        for (int i = 0; i < max; i++)
        {
            string l = i < logo.Length ? logo[i] : new string(' ', 22);
            string inf = i < info.Length ? info[i] : string.Empty;
            stdout.WriteLine($"{l}  {inf}");
        }

        return Task.FromResult(0);
    }
}

public class DuCommand : ICommand
{
    public string Name => "du";
    public string Description => "Estimate file space usage";
    public string Synopsis => "du [-h] [-s] [FILE/DIR]...";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool human = false;
        bool summarize = false;
        var targets = new List<string>();

        foreach (var arg in args)
        {
            if (arg.StartsWith('-') && arg.Length > 1)
            {
                if (arg.Contains('h')) human = true;
                if (arg.Contains('s')) summarize = true;
            }
            else
            {
                targets.Add(arg);
            }
        }

        if (targets.Count == 0) targets.Add(".");

        foreach (var target in targets)
        {
            var winPath = PosixPathMapper.ToWindows(target, context.CurrentDirectory);
            if (File.Exists(winPath))
            {
                var fi = new FileInfo(winPath);
                stdout.WriteLine($"{(human ? FormatSize(fi.Length) : (fi.Length / 1024).ToString()),8}\t{target}");
            }
            else if (Directory.Exists(winPath))
            {
                long totalBytes = CalculateDirSize(winPath, target, human, summarize, stdout, ct);
                if (summarize)
                {
                    stdout.WriteLine($"{(human ? FormatSize(totalBytes) : (totalBytes / 1024).ToString()),8}\t{target}");
                }
            }
            else
            {
                await stderr.WriteLineAsync($"du: cannot access '{target}': No such file or directory");
            }
        }

        return 0;
    }

    private static long CalculateDirSize(string dirPath, string displayPath, bool human, bool summarize, TextWriter stdout, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return 0;
        long size = 0;

        try
        {
            foreach (var file in Directory.GetFiles(dirPath))
            {
                try { size += new FileInfo(file).Length; } catch { }
            }

            foreach (var sub in Directory.GetDirectories(dirPath))
            {
                var name = Path.GetFileName(sub);
                var subDisplay = $"{displayPath}/{name}";
                long subSize = CalculateDirSize(sub, subDisplay, human, summarize, stdout, ct);
                size += subSize;
            }

            if (!summarize)
            {
                stdout.WriteLine($"{(human ? FormatSize(size) : (size / 1024).ToString()),8}\t{displayPath}");
            }
        }
        catch { }

        return size;
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

public class NetstatCommand : ICommand
{
    public string Name => "netstat";
    public string Description => "Print network connections, routing tables, and interface statistics";
    public string Synopsis => "netstat [-a] [-t] [-u]";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool tcp = !args.Contains("-u") || args.Contains("-t");
        bool udp = !args.Contains("-t") || args.Contains("-u");

        stdout.WriteLine($"{"Proto",-7} {"Local Address",-26} {"Foreign Address",-26} {"State"}");

        var ipProps = IPGlobalProperties.GetIPGlobalProperties();

        if (tcp)
        {
            try
            {
                var tcpListeners = ipProps.GetActiveTcpListeners();
                foreach (var ep in tcpListeners)
                {
                    stdout.WriteLine($"{"tcp",-7} {ep.ToString(),-26} {"0.0.0.0:*",-26} {"LISTEN"}");
                }

                var tcpConnections = ipProps.GetActiveTcpConnections();
                foreach (var c in tcpConnections)
                {
                    stdout.WriteLine($"{"tcp",-7} {c.LocalEndPoint.ToString(),-26} {c.RemoteEndPoint.ToString(),-26} {c.State}");
                }
            }
            catch { }
        }

        if (udp)
        {
            try
            {
                var udpListeners = ipProps.GetActiveUdpListeners();
                foreach (var ep in udpListeners)
                {
                    stdout.WriteLine($"{"udp",-7} {ep.ToString(),-26} {"*:*",-26} {""}");
                }
            }
            catch { }
        }

        return Task.FromResult(0);
    }
}

public class SleepCommand : ICommand
{
    public string Name => "sleep";
    public string Description => "Delay for a specified amount of time";
    public string Synopsis => "sleep NUMBER[s|m|h]";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            await stderr.WriteLineAsync("sleep: missing operand");
            return 1;
        }

        string arg = args[0];
        double seconds = 1;
        if (arg.EndsWith('s')) double.TryParse(arg[..^1], out seconds);
        else if (arg.EndsWith('m')) { double.TryParse(arg[..^1], out double m); seconds = m * 60; }
        else if (arg.EndsWith('h')) { double.TryParse(arg[..^1], out double h); seconds = h * 3600; }
        else double.TryParse(arg, out seconds);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
    }
}
