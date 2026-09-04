using System;
using System.IO;
using System.Threading.Tasks;
using LinuxTerm.Core.Common;
using LinuxTerm.Core.Execution;
using Xunit;

namespace LinuxTerm.Tests;

public class CommandExecutionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ShellContext _context;
    private readonly ShellEngine _engine;

    public CommandExecutionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WinLinuxTerm_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _context = new ShellContext(_tempDir);
        _engine = new ShellEngine();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string commandLine)
    {
        var stdoutWriter = new StringWriter();
        var stderrWriter = new StringWriter();
        int code = await _engine.ExecuteAsync(commandLine, _context, TextReader.Null, stdoutWriter, stderrWriter);
        return (code, stdoutWriter.ToString(), stderrWriter.ToString());
    }

    [Fact]
    public async Task Echo_PrintsExpectedOutput()
    {
        var (code, output, _) = await RunAsync("echo Hello World");
        Assert.Equal(0, code);
        Assert.Equal("Hello World" + Environment.NewLine, output);
    }

    [Fact]
    public async Task Echo_EscapeSequences_InterpretsNewlines()
    {
        var (code, output, _) = await RunAsync("echo -e 'line1\\nline2'");
        Assert.Equal(0, code);
        Assert.Contains("line1\nline2", output);
    }

    [Fact]
    public async Task Redirection_WritesToFileAndCatReadsIt()
    {
        var (c1, _, _) = await RunAsync("echo 'Terminal Test Redirection' > test.txt");
        Assert.Equal(0, c1);

        var (c2, output, _) = await RunAsync("cat test.txt");
        Assert.Equal(0, c2);
        Assert.Contains("Terminal Test Redirection", output);
    }

    [Fact]
    public async Task Pipe_EchoIntoGrepIntoWc_ReturnsCorrectCount()
    {
        var (code, output, _) = await RunAsync("echo -e 'apple\\nbanana\\napricot' | grep ap | wc -l");
        Assert.Equal(0, code);
        Assert.Equal("2", output.Trim());
    }

    [Fact]
    public async Task Grep_InvertMatch_FiltersOutPattern()
    {
        var (code, output, _) = await RunAsync("echo -e 'match1\\nskip\\nmatch2' | grep -v skip");
        Assert.Equal(0, code);
        Assert.Contains("match1", output);
        Assert.Contains("match2", output);
        Assert.DoesNotContain("skip", output);
    }

    [Fact]
    public async Task MkdirAndRm_CreatesAndDeletesDirectories()
    {
        var (c1, _, _) = await RunAsync("mkdir -p nested/sub/folder");
        Assert.Equal(0, c1);
        Assert.True(Directory.Exists(Path.Combine(_tempDir, "nested", "sub", "folder")));

        var (c2, _, _) = await RunAsync("rm -rf nested");
        Assert.Equal(0, c2);
        Assert.False(Directory.Exists(Path.Combine(_tempDir, "nested")));
    }

    [Fact]
    public async Task Uname_ReturnsLinuxPosixKernel()
    {
        var (code, output, _) = await RunAsync("uname -a");
        Assert.Equal(0, code);
        Assert.Contains("Linux", output);
        Assert.Contains("6.6.0-win-posix", output);
        Assert.Contains("GNU/Linux", output);
    }

    [Fact]
    public async Task Export_SetsEnvironmentVariable()
    {
        var (c1, _, _) = await RunAsync("export APP_ENV=Production");
        Assert.Equal(0, c1);
        Assert.Equal("Production", _context.EnvironmentVariables["APP_ENV"]);

        var (c2, output, _) = await RunAsync("echo $APP_ENV");
        Assert.Equal(0, c2);
        Assert.Equal("Production" + Environment.NewLine, output);
    }

    [Fact]
    public async Task LogicalAnd_ShortCircuitsOnFailure()
    {
        // Nonexistent command or file should fail
        var (code, output, _) = await RunAsync("cat nonexistent_file_12345.txt && echo should_not_run");
        Assert.NotEqual(0, code);
        Assert.DoesNotContain("should_not_run", output);
    }

    [Fact]
    public async Task LogicalOr_RunsOnFailure()
    {
        var (code, output, _) = await RunAsync("cat nonexistent_file_12345.txt || echo fallback_executed");
        Assert.Equal(0, code);
        Assert.Contains("fallback_executed", output);
    }

    [Fact]
    public async Task Which_FindsBuiltinCommands()
    {
        var (code, output, _) = await RunAsync("which ls");
        Assert.Equal(0, code);
        Assert.Contains("shell built-in command", output);
    }
}
