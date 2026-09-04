using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Commands;
using LinuxTerm.Core.Common;
using LinuxTerm.Core.Parser;

namespace LinuxTerm.Core.Execution;

/// <summary>
/// Orchestrates the parsing, pipeline coordination, redirection, and execution of shell commands.
/// </summary>
public class ShellEngine
{
    public CommandRegistry Registry { get; }

    public ShellEngine(CommandRegistry? registry = null)
    {
        Registry = registry ?? CommandRegistry.CreateDefault();
    }

    public async Task<int> ExecuteAsync(
        string commandLine,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return 0;

        commandLine = commandLine.Trim();

        // Record history
        if (context.History.Count == 0 || context.History[^1] != commandLine)
        {
            context.History.Add(commandLine);
        }

        IShellNode? ast;
        try
        {
            ast = Tokenizer.Parse(commandLine, context);
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"bash: syntax error: {ex.Message}");
            context.LastExitCode = 2;
            return 2;
        }

        if (ast == null)
            return 0;

        int exitCode = await ExecuteNodeAsync(ast, context, stdin, stdout, stderr, ct);
        context.LastExitCode = exitCode;
        return exitCode;
    }

    private async Task<int> ExecuteNodeAsync(
        IShellNode node,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return 130;

        if (node is SequenceNode seq)
        {
            int leftCode = await ExecuteNodeAsync(seq.Left, context, stdin, stdout, stderr, ct);

            return seq.Operator switch
            {
                SequenceOperator.Semicolon => await ExecuteNodeAsync(seq.Right, context, stdin, stdout, stderr, ct),
                SequenceOperator.And => leftCode == 0 ? await ExecuteNodeAsync(seq.Right, context, stdin, stdout, stderr, ct) : leftCode,
                SequenceOperator.Or => leftCode != 0 ? await ExecuteNodeAsync(seq.Right, context, stdin, stdout, stderr, ct) : leftCode,
                _ => leftCode
            };
        }

        if (node is PipelineNode pipeline)
        {
            return await ExecutePipelineAsync(pipeline, context, stdin, stdout, stderr, ct);
        }

        if (node is CommandNode cmd)
        {
            return await ExecuteCommandNodeAsync(cmd, context, stdin, stdout, stderr, ct);
        }

        return 0;
    }

    private async Task<int> ExecutePipelineAsync(
        PipelineNode pipeline,
        ShellContext context,
        TextReader initialStdin,
        TextWriter finalStdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        TextReader currentStdin = initialStdin;
        int lastExitCode = 0;

        for (int i = 0; i < pipeline.Commands.Count; i++)
        {
            var cmd = pipeline.Commands[i];
            bool isLast = i == pipeline.Commands.Count - 1;

            if (isLast)
            {
                lastExitCode = await ExecuteCommandNodeAsync(cmd, context, currentStdin, finalStdout, stderr, ct);
            }
            else
            {
                var intermediateOut = new StringWriter();
                lastExitCode = await ExecuteCommandNodeAsync(cmd, context, currentStdin, intermediateOut, stderr, ct);
                currentStdin = new StringReader(intermediateOut.ToString());
            }

            if (ct.IsCancellationRequested)
                return 130;
        }

        return lastExitCode;
    }

    private async Task<int> ExecuteCommandNodeAsync(
        CommandNode cmd,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        if (cmd.Arguments.Count == 0 && cmd.Redirects.Count == 0)
            return 0;

        TextReader actualStdin = stdin;
        TextWriter actualStdout = stdout;
        TextWriter actualStderr = stderr;
        FileStream? inStream = null;
        FileStream? outStream = null;
        StreamWriter? outWriter = null;

        try
        {
            // Apply Redirections
            foreach (var redir in cmd.Redirects)
            {
                var targetWinPath = PosixPathMapper.ToWindows(redir.Target, context.CurrentDirectory);

                switch (redir.Type)
                {
                    case RedirectType.OutputOverwrite:
                        var parentDir = Path.GetDirectoryName(targetWinPath);
                        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                            Directory.CreateDirectory(parentDir);
                        outStream = new FileStream(targetWinPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                        outWriter = new StreamWriter(outStream) { AutoFlush = true };
                        actualStdout = outWriter;
                        break;

                    case RedirectType.OutputAppend:
                        var appendDir = Path.GetDirectoryName(targetWinPath);
                        if (!string.IsNullOrEmpty(appendDir) && !Directory.Exists(appendDir))
                            Directory.CreateDirectory(appendDir);
                        outStream = new FileStream(targetWinPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                        outWriter = new StreamWriter(outStream) { AutoFlush = true };
                        actualStdout = outWriter;
                        break;

                    case RedirectType.Input:
                        if (!File.Exists(targetWinPath))
                        {
                            await stderr.WriteLineAsync($"bash: {redir.Target}: No such file or directory");
                            return 1;
                        }
                        inStream = new FileStream(targetWinPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        actualStdin = new StreamReader(inStream);
                        break;

                    case RedirectType.StderrToStdout:
                        actualStderr = actualStdout;
                        break;
                }
            }

            if (cmd.Arguments.Count == 0)
                return 0;

            var commandName = cmd.Arguments[0];
            var args = cmd.Arguments.Skip(1).ToArray();

            // Built-in command lookup
            var builtIn = Registry.GetCommand(commandName);
            if (builtIn != null)
            {
                return await builtIn.ExecuteAsync(args, context, actualStdin, actualStdout, actualStderr, ct);
            }

            // External process execution
            return await ProcessRunner.RunAsync(commandName, args, context, actualStdin, actualStdout, actualStderr, ct);
        }
        finally
        {
            if (outWriter != null)
            {
                await outWriter.FlushAsync();
                outWriter.Dispose();
            }
            outStream?.Dispose();
            inStream?.Dispose();
        }
    }
}
