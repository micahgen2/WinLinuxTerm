using System.Linq;
using LinuxTerm.Core.Common;
using LinuxTerm.Core.Parser;
using Xunit;

namespace LinuxTerm.Tests;

public class TokenizerTests
{
    private readonly ShellContext _context = new();

    [Fact]
    public void Parse_SimpleCommand_ReturnsCommandNode()
    {
        var node = Tokenizer.Parse("ls -la /c/temp", _context);
        var cmd = Assert.IsType<CommandNode>(node);

        Assert.Equal("ls", cmd.CommandName);
        Assert.Equal(new[] { "ls", "-la", "/c/temp" }, cmd.Arguments);
    }

    [Fact]
    public void Parse_SingleQuotes_PreservesLiteralContent()
    {
        var node = Tokenizer.Parse("echo 'hello $USER world'", _context);
        var cmd = Assert.IsType<CommandNode>(node);

        Assert.Equal(new[] { "echo", "hello $USER world" }, cmd.Arguments);
    }

    [Fact]
    public void Parse_DoubleQuotes_ExpandsVariables()
    {
        _context.EnvironmentVariables["MY_VAR"] = "Rocket";
        var node = Tokenizer.Parse("echo \"Launch: $MY_VAR\"", _context);
        var cmd = Assert.IsType<CommandNode>(node);

        Assert.Equal(new[] { "echo", "Launch: Rocket" }, cmd.Arguments);
    }

    [Fact]
    public void Parse_Pipeline_ReturnsPipelineNode()
    {
        var node = Tokenizer.Parse("cat file.txt | grep error | wc -l", _context);
        var pipeline = Assert.IsType<PipelineNode>(node);

        Assert.Equal(3, pipeline.Commands.Count);
        Assert.Equal("cat", pipeline.Commands[0].CommandName);
        Assert.Equal("grep", pipeline.Commands[1].CommandName);
        Assert.Equal("wc", pipeline.Commands[2].CommandName);
    }

    [Fact]
    public void Parse_Redirection_ParsesRedirectNodes()
    {
        var node = Tokenizer.Parse("echo text > out.txt", _context);
        var cmd = Assert.IsType<CommandNode>(node);

        Assert.Single(cmd.Redirects);
        Assert.Equal(RedirectType.OutputOverwrite, cmd.Redirects[0].Type);
        Assert.Equal("out.txt", cmd.Redirects[0].Target);
    }

    [Fact]
    public void Parse_AppendRedirection_ParsesAppendNode()
    {
        var node = Tokenizer.Parse("echo line >> log.txt", _context);
        var cmd = Assert.IsType<CommandNode>(node);

        Assert.Single(cmd.Redirects);
        Assert.Equal(RedirectType.OutputAppend, cmd.Redirects[0].Type);
        Assert.Equal("log.txt", cmd.Redirects[0].Target);
    }

    [Fact]
    public void Parse_AndOrSequence_ReturnsSequenceNode()
    {
        var node = Tokenizer.Parse("mkdir test && cd test || echo failed", _context);
        var seq1 = Assert.IsType<SequenceNode>(node);
        Assert.Equal(SequenceOperator.Or, seq1.Operator);

        var seq2 = Assert.IsType<SequenceNode>(seq1.Left);
        Assert.Equal(SequenceOperator.And, seq2.Operator);
    }

    [Fact]
    public void Parse_Alias_ExpandsAliasAtCommandBoundary()
    {
        _context.Aliases["ll"] = "ls -la";
        var node = Tokenizer.Parse("ll /c/tmp", _context);
        var cmd = Assert.IsType<CommandNode>(node);

        Assert.Equal("ls", cmd.CommandName);
        Assert.Equal(new[] { "ls", "-la", "/c/tmp" }, cmd.Arguments);
    }
}
