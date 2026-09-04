using System;
using System.IO;
using System.Threading.Tasks;
using LinuxTerm.Core.Common;
using LinuxTerm.Core.Execution;
using Xunit;

namespace LinuxTerm.Tests;

public class StreamCommandsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ShellContext _context;
    private readonly ShellEngine _engine;

    public StreamCommandsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WinLinuxTerm_StreamTest_" + Guid.NewGuid().ToString("N"));
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
    public async Task Sed_ReplacesPatternGlobally()
    {
        var (code, output, _) = await RunAsync("echo 'foo bar foo' | sed 's/foo/baz/g'");
        Assert.Equal(0, code);
        Assert.Equal("baz bar baz" + Environment.NewLine, output);
    }

    [Fact]
    public async Task Awk_PrintsSpecificColumns()
    {
        var (code, output, _) = await RunAsync("echo 'alpha beta gamma delta' | awk '{print $2, $4}'");
        Assert.Equal(0, code);
        Assert.Equal("beta delta" + Environment.NewLine, output);
    }

    [Fact]
    public async Task Cut_ExtractsDelimitedFields()
    {
        var (code, output, _) = await RunAsync("echo 'user:x:1000:1000:Micah:/home/micah:/bin/bash' | cut -d: -f1,5");
        Assert.Equal(0, code);
        Assert.Equal("user:Micah" + Environment.NewLine, output);
    }

    [Fact]
    public async Task Sort_SortsNumericallyAndReverses()
    {
        var (code, output, _) = await RunAsync("echo -e '10\\n2\\n35\\n1' | sort -n -r");
        Assert.Equal(0, code);
        var lines = output.Trim().Split(Environment.NewLine);
        Assert.Equal(new[] { "35", "10", "2", "1" }, lines);
    }

    [Fact]
    public async Task Uniq_CountsDuplicateLines()
    {
        var (code, output, _) = await RunAsync("echo -e 'apple\\napple\\nbanana\\napple' | uniq -c");
        Assert.Equal(0, code);
        Assert.Contains("2 apple", output);
        Assert.Contains("1 banana", output);
    }

    [Fact]
    public async Task Tr_TranslatesCaseAndDeletes()
    {
        var (code1, output1, _) = await RunAsync("echo 'hello world' | tr 'a-z' 'A-Z'");
        Assert.Equal(0, code1);
        Assert.Equal("HELLO WORLD" + Environment.NewLine, output1);

        var (code2, output2, _) = await RunAsync("echo 'hello world' | tr -d 'lo'");
        Assert.Equal(0, code2);
        Assert.Equal("he wrd" + Environment.NewLine, output2);
    }

    [Fact]
    public async Task Tee_WritesToBothFileAndStdout()
    {
        var (code, output, _) = await RunAsync("echo 'tee test data' | tee test_tee.txt");
        Assert.Equal(0, code);
        Assert.Equal("tee test data" + Environment.NewLine, output);

        var filePath = Path.Combine(_tempDir, "test_tee.txt");
        Assert.True(File.Exists(filePath));
        var fileContent = await File.ReadAllTextAsync(filePath);
        Assert.Contains("tee test data", fileContent);
    }

    [Fact]
    public async Task Xargs_BuildsAndExecutesCommand()
    {
        var (code, output, _) = await RunAsync("echo 'file1 file2 file3' | xargs echo prefix");
        Assert.Equal(0, code);
        Assert.Equal("prefix file1 file2 file3" + Environment.NewLine, output);
    }

    [Fact]
    public async Task Sha256Sum_CalculatesDigest()
    {
        var (code, output, _) = await RunAsync("echo -n 'antigravity' | sha256sum");
        Assert.Equal(0, code);
        Assert.Contains(" -", output);
        // SHA-256 of "antigravity"
        Assert.StartsWith("ac0a3dfd6dddb20962cecff6ee5fe65e19d3923be", output);
    }

    [Fact]
    public async Task CommandSubstitution_EvaluatesNestedCommand()
    {
        var (code, output, _) = await RunAsync("echo \"Release: $(uname -r)\"");
        Assert.Equal(0, code);
        Assert.Equal("Release: 6.6.0-win-posix" + Environment.NewLine, output);
    }

    [Fact]
    public async Task Neofetch_ExecutesSuccessfully()
    {
        var (code, output, _) = await RunAsync("neofetch");
        Assert.Equal(0, code);
        var plain = AnsiText.StripAnsi(output);
        Assert.Contains("OS: WinLinuxTerm", plain);
        Assert.Contains("Kernel: 6.6.0-win-posix", plain);
    }
}
