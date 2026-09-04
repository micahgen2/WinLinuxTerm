using System;
using System.Collections.Generic;

namespace LinuxTerm.Core.Commands;

public class CommandRegistry
{
    private readonly Dictionary<string, ICommand> _commands = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ICommand command)
    {
        _commands[command.Name] = command;
    }

    public ICommand? GetCommand(string name)
    {
        return _commands.GetValueOrDefault(name);
    }

    public bool HasCommand(string name)
    {
        return _commands.ContainsKey(name);
    }

    public IEnumerable<ICommand> GetAllCommands()
    {
        return _commands.Values;
    }

    public IEnumerable<string> GetCommandNames()
    {
        return _commands.Keys;
    }

    public static CommandRegistry CreateDefault()
    {
        var registry = new CommandRegistry();

        // File & Directory commands
        registry.Register(new LsCommand());
        registry.Register(new CdCommand());
        registry.Register(new PwdCommand());
        registry.Register(new MkdirCommand());
        registry.Register(new RmCommand());
        registry.Register(new CpCommand());
        registry.Register(new MvCommand());
        registry.Register(new TouchCommand());
        registry.Register(new FindCommand());
        registry.Register(new TreeCommand());

        // Text commands
        registry.Register(new CatCommand());
        registry.Register(new GrepCommand());
        registry.Register(new HeadCommand());
        registry.Register(new TailCommand());
        registry.Register(new WcCommand());
        registry.Register(new EchoCommand());
        registry.Register(new Base64Command());

        // System & Info commands
        registry.Register(new UnameCommand());
        registry.Register(new WhoamiCommand());
        registry.Register(new HostnameCommand());
        registry.Register(new DateCommand());
        registry.Register(new UptimeCommand());
        registry.Register(new DfCommand());
        registry.Register(new FreeCommand());
        registry.Register(new PsCommand());
        registry.Register(new KillCommand());
        registry.Register(new WhichCommand(registry));
        registry.Register(new ClearCommand());

        // Shell builtins & Utilities
        registry.Register(new ExportCommand());
        registry.Register(new EnvCommand());
        registry.Register(new AliasCommand());
        registry.Register(new UnaliasCommand());
        registry.Register(new HistoryCommand());
        registry.Register(new HelpCommand(registry));
        registry.Register(new ExitCommand());

        // Network commands
        registry.Register(new CurlCommand());
        registry.Register(new WgetCommand());

        return registry;
    }
}
