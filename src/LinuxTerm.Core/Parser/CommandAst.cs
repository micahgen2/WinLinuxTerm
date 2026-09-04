using System.Collections.Generic;
using System.Linq;

namespace LinuxTerm.Core.Parser;

public interface IShellNode { }

public enum SequenceOperator
{
    Semicolon,
    And, // &&
    Or   // ||
}

public class SequenceNode : IShellNode
{
    public IShellNode Left { get; }
    public IShellNode Right { get; }
    public SequenceOperator Operator { get; }

    public SequenceNode(IShellNode left, IShellNode right, SequenceOperator op)
    {
        Left = left;
        Right = right;
        Operator = op;
    }
}

public class PipelineNode : IShellNode
{
    public List<CommandNode> Commands { get; } = new();

    public PipelineNode(IEnumerable<CommandNode> commands)
    {
        Commands.AddRange(commands);
    }
}

public enum RedirectType
{
    OutputOverwrite, // >
    OutputAppend,    // >>
    Input,           // <
    StderrToStdout   // 2>&1
}

public record RedirectNode(RedirectType Type, string Target);

public class CommandNode : IShellNode
{
    public List<string> Arguments { get; } = new();
    public List<RedirectNode> Redirects { get; } = new();

    public string CommandName => Arguments.FirstOrDefault() ?? string.Empty;

    public CommandNode(IEnumerable<string> args, IEnumerable<RedirectNode>? redirects = null)
    {
        Arguments.AddRange(args);
        if (redirects != null)
        {
            Redirects.AddRange(redirects);
        }
    }
}
