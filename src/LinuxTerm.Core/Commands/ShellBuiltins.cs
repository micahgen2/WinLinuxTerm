using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Common;

namespace LinuxTerm.Core.Commands;

public class ExportCommand : ICommand
{
    public string Name => "export";
    public string Description => "Set export attribute for shell variables";
    public string Synopsis => "export [NAME[=VALUE]...]";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            foreach (var kvp in context.EnvironmentVariables.OrderBy(k => k.Key))
            {
                stdout.WriteLine($"declare -x {kvp.Key}=\"{kvp.Value}\"");
            }
            return Task.FromResult(0);
        }

        foreach (var arg in args)
        {
            int eqIdx = arg.IndexOf('=');
            if (eqIdx != -1)
            {
                var key = arg[..eqIdx];
                var val = arg[(eqIdx + 1)..];
                context.EnvironmentVariables[key] = val;
            }
            else
            {
                // Just exported without changing value
                if (!context.EnvironmentVariables.ContainsKey(arg))
                {
                    context.EnvironmentVariables[arg] = string.Empty;
                }
            }
        }

        return Task.FromResult(0);
    }
}

public class EnvCommand : ICommand
{
    public string Name => "env";
    public string Description => "Run a program in a modified environment or print environment";
    public string Synopsis => "env";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        foreach (var kvp in context.EnvironmentVariables.OrderBy(k => k.Key))
        {
            stdout.WriteLine($"{kvp.Key}={kvp.Value}");
        }
        return Task.FromResult(0);
    }
}

public class AliasCommand : ICommand
{
    public string Name => "alias";
    public string Description => "Define or display aliases";
    public string Synopsis => "alias [NAME[='VALUE']...]";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            foreach (var kvp in context.Aliases.OrderBy(a => a.Key))
            {
                stdout.WriteLine($"alias {kvp.Key}='{kvp.Value}'");
            }
            return Task.FromResult(0);
        }

        foreach (var arg in args)
        {
            int eq = arg.IndexOf('=');
            if (eq != -1)
            {
                var name = arg[..eq].Trim();
                var val = arg[(eq + 1)..].Trim('\'', '"');
                context.Aliases[name] = val;
            }
            else
            {
                if (context.Aliases.TryGetValue(arg, out var val))
                {
                    stdout.WriteLine($"alias {arg}='{val}'");
                }
                else
                {
                    stderr.WriteLine($"alias: {arg}: not found");
                }
            }
        }

        return Task.FromResult(0);
    }
}

public class UnaliasCommand : ICommand
{
    public string Name => "unalias";
    public string Description => "Remove names from the list of defined aliases";
    public string Synopsis => "unalias [-a] NAME...";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        if (args.Contains("-a"))
        {
            context.Aliases.Clear();
            return Task.FromResult(0);
        }

        foreach (var name in args)
        {
            if (!context.Aliases.Remove(name))
            {
                stderr.WriteLine($"unalias: {name}: not found");
            }
        }

        return Task.FromResult(0);
    }
}

public class HistoryCommand : ICommand
{
    public string Name => "history";
    public string Description => "Display or manipulate the history list";
    public string Synopsis => "history [-c]";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        if (args.Contains("-c"))
        {
            context.History.Clear();
            return Task.FromResult(0);
        }

        for (int i = 0; i < context.History.Count; i++)
        {
            stdout.WriteLine($"{i + 1,5}  {context.History[i]}");
        }

        return Task.FromResult(0);
    }
}

public class HelpCommand : ICommand
{
    private readonly CommandRegistry _registry;

    public HelpCommand(CommandRegistry registry)
    {
        _registry = registry;
    }

    public string Name => "help";
    public string Description => "Display information about builtin commands";
    public string Synopsis => "help [PATTERN|COMMAND]";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            stdout.WriteLine($"{AnsiText.Bold}WinLinuxTerm Built-in Commands:{AnsiText.Reset}");
            stdout.WriteLine();

            var cmds = _registry.GetAllCommands().OrderBy(c => c.Name);
            foreach (var cmd in cmds)
            {
                stdout.WriteLine($"  {AnsiText.Bold}{AnsiText.BrightGreen}{cmd.Name,-12}{AnsiText.Reset} {cmd.Description}");
            }

            stdout.WriteLine();
            stdout.WriteLine($"Type '{AnsiText.BrightYellow}help <command>{AnsiText.Reset}' or '{AnsiText.BrightYellow}man <command>{AnsiText.Reset}' for detailed synopsis.");
            return Task.FromResult(0);
        }

        var target = args[0];
        var found = _registry.GetCommand(target);
        if (found != null)
        {
            stdout.WriteLine($"{AnsiText.Bold}NAME{AnsiText.Reset}");
            stdout.WriteLine($"    {found.Name} - {found.Description}");
            stdout.WriteLine();
            stdout.WriteLine($"{AnsiText.Bold}SYNOPSIS{AnsiText.Reset}");
            stdout.WriteLine($"    {found.Synopsis}");
            stdout.WriteLine();
            stdout.WriteLine($"{AnsiText.Bold}DESCRIPTION{AnsiText.Reset}");
            stdout.WriteLine($"    {found.Description}");
            return Task.FromResult(0);
        }

        stderr.WriteLine($"help: no help topics match '{target}'. Try 'help'.");
        return Task.FromResult(1);
    }
}

public class ExitCommand : ICommand
{
    public string Name => "exit";
    public string Description => "Exit the shell";
    public string Synopsis => "exit [n]";

    public Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        int code = 0;
        if (args.Length > 0 && int.TryParse(args[0], out int c))
            code = c;

        context.LastExitCode = code;
        context.IsRunning = false;
        return Task.FromResult(code);
    }
}
