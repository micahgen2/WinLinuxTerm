using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Common;

namespace LinuxTerm.Core.Commands;

public class PingCommand : ICommand
{
    public string Name => "ping";
    public string Description => "Send ICMP ECHO_REQUEST to network hosts";
    public string Synopsis => "ping [-c COUNT] [-i INTERVAL] [-W TIMEOUT] HOST";

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
            await stderr.WriteLineAsync("ping: usage error: Destination address required");
            return 1;
        }

        int count = 4;
        int intervalMs = 1000;
        int timeoutMs = 4000;
        string? targetHost = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "-c" && i + 1 < args.Length && int.TryParse(args[++i], out var c)) count = c;
            else if (arg == "-i" && i + 1 < args.Length && double.TryParse(args[++i], CultureInfo.InvariantCulture, out var inv)) intervalMs = (int)(inv * 1000);
            else if (arg == "-W" && i + 1 < args.Length && double.TryParse(args[++i], CultureInfo.InvariantCulture, out var t)) timeoutMs = (int)(t * 1000);
            else if (!arg.StartsWith('-')) targetHost = arg;
        }

        if (string.IsNullOrEmpty(targetHost))
        {
            await stderr.WriteLineAsync("ping: usage error: Destination address required");
            return 1;
        }

        IPAddress targetIp;
        try
        {
            if (!IPAddress.TryParse(targetHost, out targetIp!))
            {
                var hostEntry = await Dns.GetHostEntryAsync(targetHost, ct);
                targetIp = hostEntry.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                           ?? hostEntry.AddressList.First();
            }
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"ping: {targetHost}: Name or service not known ({ex.Message})");
            return 2;
        }

        await stdout.WriteLineAsync($"PING {targetHost} ({targetIp}) 56(84) bytes of data.");

        using var pingSender = new Ping();
        var roundtrips = new List<long>();
        int transmitted = 0;
        int received = 0;
        var stopwatch = Stopwatch.StartNew();

        for (int seq = 1; seq <= count; seq++)
        {
            if (ct.IsCancellationRequested) break;

            transmitted++;
            try
            {
                var reply = await pingSender.SendPingAsync(targetIp, timeoutMs);
                if (reply.Status == IPStatus.Success)
                {
                    received++;
                    roundtrips.Add(reply.RoundtripTime);
                    int ttl = reply.Options?.Ttl ?? 64;
                    await stdout.WriteLineAsync($"64 bytes from {reply.Address}: icmp_seq={seq} ttl={ttl} time={reply.RoundtripTime:F1} ms");
                }
                else
                {
                    await stdout.WriteLineAsync($"From {targetIp} icmp_seq={seq} Destination Host Unreachable");
                }
            }
            catch (Exception ex)
            {
                await stderr.WriteLineAsync($"ping: sendto: {ex.Message}");
            }

            if (seq < count)
            {
                try
                {
                    await Task.Delay(intervalMs, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        stopwatch.Stop();
        int lossPct = transmitted > 0 ? (int)((transmitted - received) / (double)transmitted * 100) : 0;

        await stdout.WriteLineAsync();
        await stdout.WriteLineAsync($"--- {targetHost} ping statistics ---");
        await stdout.WriteLineAsync($"{transmitted} packets transmitted, {received} received, {lossPct}% packet loss, time {stopwatch.ElapsedMilliseconds}ms");
        if (roundtrips.Count > 0)
        {
            double min = roundtrips.Min();
            double avg = roundtrips.Average();
            double max = roundtrips.Max();
            await stdout.WriteLineAsync($"rtt min/avg/max = {min:F3}/{avg:F3}/{max:F3} ms");
        }

        return received > 0 ? 0 : 1;
    }
}

public class IfconfigCommand : ICommand
{
    public string Name => "ifconfig";
    public string Description => "Configure or view network interface parameters";
    public string Synopsis => "ifconfig [-a] [INTERFACE]";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        string? targetIface = null;
        foreach (var a in args)
        {
            if (!a.StartsWith('-')) targetIface = a;
        }

        var interfaces = NetworkInterface.GetAllNetworkInterfaces();
        int ifaceIndex = 0;

        foreach (var iface in interfaces)
        {
            ct.ThrowIfCancellationRequested();

            var linuxName = GetLinuxInterfaceName(iface, ifaceIndex++);
            if (targetIface != null &&
                !string.Equals(targetIface, linuxName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(targetIface, iface.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var ipProps = iface.GetIPProperties();
            var ipv4 = ipProps.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
            var ipv6 = ipProps.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetworkV6);
            var mac = iface.GetPhysicalAddress().ToString();
            var formattedMac = string.Join(":", Enumerable.Range(0, mac.Length / 2).Select(i => mac.Substring(i * 2, 2).ToLowerInvariant()));
            if (string.IsNullOrEmpty(formattedMac)) formattedMac = "00:00:00:00:00:00";

            IPv4InterfaceStatistics? stats = null;
            try
            {
                if (iface.Supports(NetworkInterfaceComponent.IPv4))
                {
                    stats = iface.GetIPv4Statistics();
                }
            }
            catch { }

            string statusFlags = iface.OperationalStatus == OperationalStatus.Up ? "UP,BROADCAST,RUNNING,MULTICAST" : "BROADCAST,MULTICAST";

            await stdout.WriteLineAsync($"{linuxName}: flags=4163<{statusFlags}>  mtu 1500");
            if (ipv4 != null)
            {
                var mask = ipv4.IPv4Mask?.ToString() ?? "255.255.255.0";
                await stdout.WriteLineAsync($"        inet {ipv4.Address}  netmask {mask}  broadcast 255.255.255.255");
            }
            if (ipv6 != null)
            {
                await stdout.WriteLineAsync($"        inet6 {ipv6.Address}  prefixlen 64  scopeid 0x20<link>");
            }
            await stdout.WriteLineAsync($"        ether {formattedMac}  txqueuelen 1000  ({iface.NetworkInterfaceType})");
#pragma warning disable CA1416
            long rxPackets = stats?.UnicastPacketsReceived ?? 0;
            long rxBytes = stats?.BytesReceived ?? 0;
            long rxErrors = stats?.IncomingPacketsWithErrors ?? 0;
            long rxDropped = stats?.IncomingPacketsDiscarded ?? 0;
            long txPackets = stats?.UnicastPacketsSent ?? 0;
            long txBytes = stats?.BytesSent ?? 0;
            long txErrors = stats?.OutgoingPacketsWithErrors ?? 0;
            long txDropped = stats?.OutgoingPacketsDiscarded ?? 0;
#pragma warning restore CA1416
            await stdout.WriteLineAsync($"        RX packets {rxPackets}  bytes {rxBytes} ({FormatBytes(rxBytes)})");
            await stdout.WriteLineAsync($"        RX errors {rxErrors}  dropped {rxDropped}  overruns 0  frame 0");
            await stdout.WriteLineAsync($"        TX packets {txPackets}  bytes {txBytes} ({FormatBytes(txBytes)})");
            await stdout.WriteLineAsync($"        TX errors {txErrors}  dropped {txDropped} overruns 0  carrier 0  collisions 0");
            await stdout.WriteLineAsync();
        }

        return 0;
    }

    internal static string GetLinuxInterfaceName(NetworkInterface iface, int index)
    {
        if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback) return "lo";
        if (iface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) return $"wlan{index}";
        return $"eth{index}";
    }

    internal static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024 * 1024)):F1} GiB";
        if (bytes >= 1024 * 1024) return $"{(bytes / (1024.0 * 1024)):F1} MiB";
        if (bytes >= 1024) return $"{(bytes / 1024.0):F1} KiB";
        return $"{bytes} B";
    }
}

public class IpCommand : ICommand
{
    public string Name => "ip";
    public string Description => "Show and manipulate routing, network devices, and interfaces";
    public string Synopsis => "ip [OPTIONS] OBJECT { COMMAND | help }";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        if (args.Length == 0 || args[0] == "help")
        {
            await stdout.WriteLineAsync("Usage: ip [ OPTIONS ] OBJECT { COMMAND | help }");
            await stdout.WriteLineAsync("where  OBJECT := { link | address | addr | a | route | r }");
            return 0;
        }

        string obj = args[0].ToLowerInvariant();
        string subcmd = args.Length > 1 ? args[1].ToLowerInvariant() : "show";

        var interfaces = NetworkInterface.GetAllNetworkInterfaces();

        if (obj == "a" || obj == "addr" || obj == "address")
        {
            int index = 1;
            foreach (var iface in interfaces)
            {
                ct.ThrowIfCancellationRequested();
                var name = IfconfigCommand.GetLinuxInterfaceName(iface, index - 1);
                var mac = iface.GetPhysicalAddress().ToString();
                var formattedMac = string.Join(":", Enumerable.Range(0, mac.Length / 2).Select(i => mac.Substring(i * 2, 2).ToLowerInvariant()));
                if (string.IsNullOrEmpty(formattedMac)) formattedMac = "00:00:00:00:00:00";
                var status = iface.OperationalStatus == OperationalStatus.Up ? "UP" : "DOWN";

                await stdout.WriteLineAsync($"{index}: {name}: <BROADCAST,MULTICAST,{status},LOWER_UP> mtu 1500 qdisc fq_codel state {status} group default qlen 1000");
                await stdout.WriteLineAsync($"    link/ether {formattedMac} brd ff:ff:ff:ff:ff:ff");

                var ipProps = iface.GetIPProperties();
                foreach (var uni in ipProps.UnicastAddresses)
                {
                    if (uni.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        await stdout.WriteLineAsync($"    inet {uni.Address}/24 brd 255.255.255.255 scope global dynamic {name}");
                        await stdout.WriteLineAsync($"       valid_lft forever preferred_lft forever");
                    }
                    else if (uni.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        await stdout.WriteLineAsync($"    inet6 {uni.Address}/64 scope link");
                        await stdout.WriteLineAsync($"       valid_lft forever preferred_lft forever");
                    }
                }
                index++;
            }
            return 0;
        }
        else if (obj == "link" || obj == "l")
        {
            int index = 1;
            foreach (var iface in interfaces)
            {
                var name = IfconfigCommand.GetLinuxInterfaceName(iface, index - 1);
                var mac = iface.GetPhysicalAddress().ToString();
                var formattedMac = string.Join(":", Enumerable.Range(0, mac.Length / 2).Select(i => mac.Substring(i * 2, 2).ToLowerInvariant()));
                if (string.IsNullOrEmpty(formattedMac)) formattedMac = "00:00:00:00:00:00";
                var status = iface.OperationalStatus == OperationalStatus.Up ? "UP" : "DOWN";

                await stdout.WriteLineAsync($"{index}: {name}: <BROADCAST,MULTICAST,{status},LOWER_UP> mtu 1500 qdisc mq state {status} mode DEFAULT group default qlen 1000");
                await stdout.WriteLineAsync($"    link/ether {formattedMac} brd ff:ff:ff:ff:ff:ff");
                index++;
            }
            return 0;
        }
        else if (obj == "route" || obj == "r")
        {
            int index = 0;
            foreach (var iface in interfaces)
            {
                var name = IfconfigCommand.GetLinuxInterfaceName(iface, index++);
                var props = iface.GetIPProperties();
                foreach (var gw in props.GatewayAddresses)
                {
                    await stdout.WriteLineAsync($"default via {gw.Address} dev {name} proto dhcp metric 100");
                }
            }
            return 0;
        }

        await stderr.WriteLineAsync($"ip: Object \"{obj}\" is unknown, try \"ip help\".");
        return 1;
    }
}

public class NslookupCommand : ICommand
{
    public string Name => "nslookup";
    public string Description => "Query Internet name servers interactively or for a specific host";
    public string Synopsis => "nslookup [HOST] [SERVER]";

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
            await stderr.WriteLineAsync("nslookup: interactive mode not supported, specify host operand");
            return 1;
        }

        string host = args[0];
        string server = args.Length > 1 ? args[1] : "127.0.0.53";

        await stdout.WriteLineAsync($"Server:         {server}");
        await stdout.WriteLineAsync($"Address:        {server}#53");
        await stdout.WriteLineAsync();

        try
        {
            var entry = await Dns.GetHostEntryAsync(host, ct);
            await stdout.WriteLineAsync("Non-authoritative answer:");
            await stdout.WriteLineAsync($"Name:   {entry.HostName}");
            foreach (var ip in entry.AddressList)
            {
                await stdout.WriteLineAsync($"Address: {ip}");
            }
            if (entry.Aliases.Length > 0)
            {
                foreach (var alias in entry.Aliases)
                {
                    await stdout.WriteLineAsync($"Aliases: {alias}");
                }
            }
            return 0;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"** server can't find {host}: {ex.Message}");
            return 1;
        }
    }
}
