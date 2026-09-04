using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Commands;
using LinuxTerm.Core.Common;

namespace LinuxTerm.Core.Execution;

/// <summary>
/// Executes external Windows commands and binaries when no built-in Linux command matches.
/// </summary>
public static class ProcessRunner
{
    public static async Task<int> RunAsync(
        string commandName,
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        var executablePath = WhichCommand.FindInPath(commandName, context);
        string fileName;
        string[] finalArgs;

        if (executablePath != null)
        {
            fileName = executablePath;
            finalArgs = args.Select(a => TranslateArg(a, context.CurrentDirectory)).ToArray();
        }
        else
        {
            // Fall back to executing via cmd.exe /c
            fileName = "cmd.exe";
            var rawCommand = commandName + (args.Length > 0 ? " " + string.Join(" ", args.Select(EscapeForCmd)) : "");
            finalArgs = new[] { "/c", rawCommand };
        }

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = context.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        foreach (var arg in finalArgs)
        {
            if (fileName != "cmd.exe")
                psi.ArgumentList.Add(arg);
        }

        // Copy environment variables from context
        foreach (var kvp in context.EnvironmentVariables)
        {
            psi.Environment[kvp.Key] = kvp.Value;
        }

        try
        {
            using var process = new Process { StartInfo = psi };
            process.Start();

            var inputTask = Task.Run(async () =>
            {
                try
                {
                    char[] buffer = new char[4096];
                    int read;
                    while ((read = await stdin.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await process.StandardInput.WriteAsync(buffer, 0, read);
                        await process.StandardInput.FlushAsync();
                    }
                    process.StandardInput.Close();
                }
                catch { }
            }, ct);

            var outputTask = Task.Run(async () =>
            {
                try
                {
                    char[] buffer = new char[4096];
                    int read;
                    while ((read = await process.StandardOutput.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await stdout.WriteAsync(buffer, 0, read);
                        await stdout.FlushAsync();
                    }
                }
                catch { }
            }, ct);

            var errorTask = Task.Run(async () =>
            {
                try
                {
                    char[] buffer = new char[4096];
                    int read;
                    while ((read = await process.StandardError.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await stderr.WriteAsync(buffer, 0, read);
                        await stderr.FlushAsync();
                    }
                }
                catch { }
            }, ct);

            await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(ct));
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"bash: {commandName}: {ex.Message}");
            return 127;
        }
    }

    private static string TranslateArg(string arg, string currentDir)
    {
        // If argument looks like a POSIX path, translate to Windows path
        if (arg.StartsWith('/') || arg.StartsWith("~/") || arg == "~")
        {
            return PosixPathMapper.ToWindows(arg, currentDir);
        }
        return arg;
    }

    private static string EscapeForCmd(string s)
    {
        if (s.Contains(' ') || s.Contains('\t'))
            return $"\"{s}\"";
        return s;
    }
}
