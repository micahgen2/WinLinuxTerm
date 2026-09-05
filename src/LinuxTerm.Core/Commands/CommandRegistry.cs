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

        // Stream & Text processing commands
        registry.Register(new SedCommand());
        registry.Register(new AwkCommand());
        registry.Register(new CutCommand());
        registry.Register(new SortCommand());
        registry.Register(new UniqCommand());
        registry.Register(new TrCommand());
        registry.Register(new TeeCommand());
        registry.Register(new XargsCommand());
        registry.Register(new DiffCommand());

        // Checksum & Cryptography commands
        registry.Register(new Md5SumCommand());
        registry.Register(new Sha256SumCommand());

        // Advanced Diagnostic & System commands
        registry.Register(new NeofetchCommand());
        registry.Register(new DuCommand());
        registry.Register(new NetstatCommand());
        registry.Register(new SleepCommand());

        // Text Editors
        registry.Register(new NanoCommand());
        registry.Register(new VimCommand());
        registry.Register(new ViCommand());

        // Archive & Compression commands
        registry.Register(new TarCommand());
        registry.Register(new GzipCommand());
        registry.Register(new GunzipCommand());
        registry.Register(new ZipCommand());
        registry.Register(new UnzipCommand());

        // Path & File Metadata commands
        registry.Register(new FileCommand());
        registry.Register(new StatCommand());
        registry.Register(new BasenameCommand());
        registry.Register(new DirnameCommand());
        registry.Register(new RealpathCommand());
        registry.Register(new ReadlinkCommand());

        // Additional Text Processing commands
        registry.Register(new TacCommand());
        registry.Register(new RevCommand());
        registry.Register(new NlCommand());
        registry.Register(new FoldCommand());
        registry.Register(new PasteCommand());
        registry.Register(new CommCommand());
        registry.Register(new CmpCommand());
        registry.Register(new ExpandCommand());
        registry.Register(new UnexpandCommand());

        // System & Generator commands
        registry.Register(new TopCommand());
        registry.Register(new IdCommand());
        registry.Register(new GroupsCommand());
        registry.Register(new ArchCommand());
        registry.Register(new PrintenvCommand());
        registry.Register(new SeqCommand());
        registry.Register(new YesCommand());
        registry.Register(new TrueCommand());
        registry.Register(new FalseCommand());
        registry.Register(new CalCommand());
        registry.Register(new ShufCommand());

        // Network Diagnostics commands
        registry.Register(new PingCommand());
        registry.Register(new IfconfigCommand());
        registry.Register(new IpCommand());
        registry.Register(new NslookupCommand());

        return registry;
    }
}
