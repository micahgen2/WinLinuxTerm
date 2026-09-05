using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Common;
using LinuxTerm.Core.Editors.Nano;
using LinuxTerm.Core.Editors.Vim;

namespace LinuxTerm.Core.Commands;

public class NanoCommand : ICommand
{
    public string Name => "nano";
    public string Description => "GNU nano - Nano's ANOther editor, an enhanced free Pico clone";
    public string Synopsis => "nano [FILE]";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        string? targetPath = null;
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            targetPath = PosixPathMapper.ToWindows(args[0], context.CurrentDirectory);
        }

        var session = new NanoSession(targetPath)
        {
            WorkingDirectory = context.CurrentDirectory
        };

        // GUI handler priority
        if (context.NanoGuiHandler != null)
        {
            return await context.NanoGuiHandler(session, ct);
        }

        // CLI interactive mode
        if (!Console.IsOutputRedirected && !Console.IsInputRedirected)
        {
            session.RunInteractiveConsole(ct);
            return 0;
        }

        await stderr.WriteLineAsync("nano: terminal is not a tty");
        return 1;
    }
}

public class VimCommand : ICommand
{
    public string Name => "vim";
    public string Description => "Vi IMproved - a programmer's text editor";
    public string Synopsis => "vim [FILE]";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        string? targetPath = null;
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            targetPath = PosixPathMapper.ToWindows(args[0], context.CurrentDirectory);
        }

        var session = new VimSession(targetPath)
        {
            WorkingDirectory = context.CurrentDirectory
        };

        // GUI handler priority
        if (context.VimGuiHandler != null)
        {
            return await context.VimGuiHandler(session, ct);
        }

        // CLI interactive mode
        if (!Console.IsOutputRedirected && !Console.IsInputRedirected)
        {
            session.RunInteractiveConsole(ct);
            return 0;
        }

        await stderr.WriteLineAsync("Vim: Warning: Input is not from a terminal");
        return 1;
    }
}

public class ViCommand : ICommand
{
    private readonly VimCommand _vim = new();

    public string Name => "vi";
    public string Description => "Vi clone - standard Unix visual text editor";
    public string Synopsis => "vi [FILE]";

    public Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        return _vim.ExecuteAsync(args, context, stdin, stdout, stderr, ct);
    }
}
