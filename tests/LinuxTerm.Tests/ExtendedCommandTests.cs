using System;
using System.IO;
using System.Threading.Tasks;
using LinuxTerm.Core.Common;
using LinuxTerm.Core.Execution;
using Xunit;

namespace LinuxTerm.Tests;

public class ExtendedCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ShellContext _context;
    private readonly ShellEngine _engine;

    public ExtendedCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WinLinuxTerm_ExtTests_" + Guid.NewGuid().ToString("N"));
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

    // ==========================================
    // Archive & Compression Commands Tests
    // ==========================================

    [Fact]
    public async Task Tar_CreateListExtract_WorksCorrectly()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "f1.txt"), "hello tar 1");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "f2.txt"), "hello tar 2");

        // 1. Create tar
        var (c1, _, _) = await RunAsync("tar -cf test.tar f1.txt f2.txt");
        Assert.Equal(0, c1);
        Assert.True(File.Exists(Path.Combine(_tempDir, "test.tar")));

        // 2. List tar
        var (c2, out2, _) = await RunAsync("tar -tf test.tar");
        Assert.Equal(0, c2);
        Assert.Contains("f1.txt", out2);
        Assert.Contains("f2.txt", out2);

        // 3. Extract tar to subfolder
        var outDir = Path.Combine(_tempDir, "extracted");
        Directory.CreateDirectory(outDir);
        var (c3, _, _) = await RunAsync("tar -xf test.tar -C extracted");
        Assert.Equal(0, c3);
        Assert.True(File.Exists(Path.Combine(outDir, "f1.txt")));
        Assert.Equal("hello tar 1", await File.ReadAllTextAsync(Path.Combine(outDir, "f1.txt")));
    }

    [Fact]
    public async Task Gzip_Gunzip_Roundtrip_WorksCorrectly()
    {
        var filePath = Path.Combine(_tempDir, "data.txt");
        await File.WriteAllTextAsync(filePath, "Gzip test compression content 1234567890");

        // gzip data.txt
        var (c1, _, _) = await RunAsync("gzip -k data.txt");
        Assert.Equal(0, c1);
        Assert.True(File.Exists(Path.Combine(_tempDir, "data.txt.gz")));

        // remove original and gunzip
        File.Delete(filePath);
        var (c2, _, _) = await RunAsync("gunzip data.txt.gz");
        Assert.Equal(0, c2);
        Assert.True(File.Exists(filePath));
        Assert.Equal("Gzip test compression content 1234567890", await File.ReadAllTextAsync(filePath));
    }

    [Fact]
    public async Task Zip_Unzip_Roundtrip_WorksCorrectly()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "zip1.txt"), "file 1 inside zip");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "zip2.txt"), "file 2 inside zip");

        // zip
        var (c1, out1, err1) = await RunAsync("zip test.zip zip1.txt zip2.txt");
        Assert.True(c1 == 0, $"Zip failed with code {c1}, stdout: '{out1}', stderr: '{err1}'");
        Assert.True(File.Exists(Path.Combine(_tempDir, "test.zip")));

        // unzip to target directory
        var (c2, out2, err2) = await RunAsync("unzip test.zip -d zip_out");
        Assert.True(c2 == 0, $"Unzip failed with code {c2}, stdout: '{out2}', stderr: '{err2}'");
        var outPath = Path.Combine(_tempDir, "zip_out", "zip1.txt");
        Assert.True(File.Exists(outPath));
        Assert.Equal("file 1 inside zip", await File.ReadAllTextAsync(outPath));
    }

    // ==========================================
    // Path & File Metadata Commands Tests
    // ==========================================

    [Fact]
    public async Task Basename_StripsDirectoryAndSuffix()
    {
        var (c1, out1, _) = await RunAsync("basename /usr/include/stdio.h .h");
        Assert.Equal(0, c1);
        Assert.Equal("stdio" + Environment.NewLine, out1);

        var (c2, out2, _) = await RunAsync("basename -a /dir1/alpha.txt /dir2/beta.txt");
        Assert.Equal(0, c2);
        Assert.Contains("alpha.txt", out2);
        Assert.Contains("beta.txt", out2);
    }

    [Fact]
    public async Task Dirname_StripsTrailingComponent()
    {
        var (c1, out1, _) = await RunAsync("dirname /usr/local/bin/sort");
        Assert.Equal(0, c1);
        Assert.Equal("/usr/local/bin" + Environment.NewLine, out1);

        var (c2, out2, _) = await RunAsync("dirname simple.txt");
        Assert.Equal(0, c2);
        Assert.Equal("." + Environment.NewLine, out2);
    }

    [Fact]
    public async Task Realpath_ResolvesCanonicalPath()
    {
        var file = Path.Combine(_tempDir, "real.txt");
        await File.WriteAllTextAsync(file, "content");

        var (c1, out1, _) = await RunAsync("realpath real.txt");
        Assert.Equal(0, c1);
        Assert.Contains("real.txt", out1);
    }

    [Fact]
    public async Task Readlink_CanonicalizesPath()
    {
        var file = Path.Combine(_tempDir, "link.txt");
        await File.WriteAllTextAsync(file, "content");

        var (c1, out1, _) = await RunAsync("readlink -f link.txt");
        Assert.Equal(0, c1);
        Assert.Contains("link.txt", out1);
    }

    [Fact]
    public async Task Stat_DisplaysMetadataAndFormats()
    {
        var file = Path.Combine(_tempDir, "stat_test.txt");
        await File.WriteAllTextAsync(file, "12345678");

        var (c1, out1, _) = await RunAsync("stat stat_test.txt");
        Assert.Equal(0, c1);
        Assert.Contains("Size: 8", out1);
        Assert.Contains("stat_test.txt", out1);

        var (c2, out2, _) = await RunAsync("stat -c %s stat_test.txt");
        Assert.Equal(0, c2);
        Assert.Equal("8" + Environment.NewLine, out2);
    }

    [Fact]
    public async Task File_DetectsTypes()
    {
        var textFile = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(textFile, "This is plain ASCII text file.\n");

        var emptyFile = Path.Combine(_tempDir, "empty.dat");
        await File.WriteAllBytesAsync(emptyFile, Array.Empty<byte>());

        var (c1, out1, _) = await RunAsync("file test.txt");
        Assert.Equal(0, c1);
        Assert.Contains("ASCII text", out1);

        var (c2, out2, _) = await RunAsync("file empty.dat");
        Assert.Equal(0, c2);
        Assert.Contains("empty", out2);
    }

    // ==========================================
    // Additional Text Processing Commands Tests
    // ==========================================

    [Fact]
    public async Task Tac_ReversesLineOrder()
    {
        var file = Path.Combine(_tempDir, "lines.txt");
        await File.WriteAllTextAsync(file, "line1\nline2\nline3\n");

        var (c1, out1, _) = await RunAsync("tac lines.txt");
        Assert.Equal(0, c1);
        var expected = "line3\nline2\nline1\n";
        Assert.Equal(expected.Replace("\n", Environment.NewLine), out1.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
    }

    [Fact]
    public async Task Rev_ReversesCharacters()
    {
        var file = Path.Combine(_tempDir, "rev_lines.txt");
        await File.WriteAllTextAsync(file, "hello\nworld\n");

        var (c1, out1, _) = await RunAsync("rev rev_lines.txt");
        Assert.Equal(0, c1);
        Assert.Contains("olleh", out1);
        Assert.Contains("dlrow", out1);
    }

    [Fact]
    public async Task Nl_NumbersLines()
    {
        var file = Path.Combine(_tempDir, "nl_test.txt");
        await File.WriteAllTextAsync(file, "first\nsecond\nthird\n");

        var (c1, out1, _) = await RunAsync("nl -b a nl_test.txt");
        Assert.Equal(0, c1);
        Assert.Contains("1\tfirst", out1);
        Assert.Contains("2\tsecond", out1);
        Assert.Contains("3\tthird", out1);
    }

    [Fact]
    public async Task Fold_WrapsAtSpecifiedWidth()
    {
        var file = Path.Combine(_tempDir, "fold_test.txt");
        await File.WriteAllTextAsync(file, "12345678901234567890\n");

        var (c1, out1, _) = await RunAsync("fold -w 5 fold_test.txt");
        Assert.Equal(0, c1);
        Assert.Contains("12345", out1);
        Assert.Contains("67890", out1);
    }

    [Fact]
    public async Task Paste_MergesColumns()
    {
        var file1 = Path.Combine(_tempDir, "p1.txt");
        var file2 = Path.Combine(_tempDir, "p2.txt");
        await File.WriteAllTextAsync(file1, "A\nB\n");
        await File.WriteAllTextAsync(file2, "1\n2\n");

        var (c1, out1, _) = await RunAsync("paste -d : p1.txt p2.txt");
        Assert.Equal(0, c1);
        Assert.Contains("A:1", out1);
        Assert.Contains("B:2", out1);
    }

    [Fact]
    public async Task Comm_ComparesSortedFiles()
    {
        var file1 = Path.Combine(_tempDir, "comm1.txt");
        var file2 = Path.Combine(_tempDir, "comm2.txt");
        await File.WriteAllTextAsync(file1, "apple\nbanana\ncherry\n");
        await File.WriteAllTextAsync(file2, "banana\ncherry\ndate\n");

        // Only lines common to both (-12 suppresses col 1 and 2)
        var (c1, out1, _) = await RunAsync("comm -12 comm1.txt comm2.txt");
        Assert.Equal(0, c1);
        Assert.Contains("banana", out1);
        Assert.Contains("cherry", out1);
        Assert.DoesNotContain("apple", out1);
        Assert.DoesNotContain("date", out1);
    }

    [Fact]
    public async Task Cmp_ComparesBytes()
    {
        var f1 = Path.Combine(_tempDir, "cmp1.bin");
        var f2 = Path.Combine(_tempDir, "cmp2.bin");
        var f3 = Path.Combine(_tempDir, "cmp3.bin");

        await File.WriteAllBytesAsync(f1, new byte[] { 1, 2, 3 });
        await File.WriteAllBytesAsync(f2, new byte[] { 1, 2, 3 });
        await File.WriteAllBytesAsync(f3, new byte[] { 1, 9, 3 });

        var (c1, _, _) = await RunAsync("cmp cmp1.bin cmp2.bin");
        Assert.Equal(0, c1);

        var (c2, out2, _) = await RunAsync("cmp cmp1.bin cmp3.bin");
        Assert.Equal(1, c2);
        Assert.Contains("differ", out2);
    }

    [Fact]
    public async Task Expand_Unexpand_ConvertsTabsAndSpaces()
    {
        var file = Path.Combine(_tempDir, "tabs.txt");
        await File.WriteAllTextAsync(file, "a\tb\n");

        var (c1, out1, _) = await RunAsync("expand -t 4 tabs.txt");
        Assert.Equal(0, c1);
        Assert.Equal("a   b" + Environment.NewLine, out1);

        var spaceFile = Path.Combine(_tempDir, "spaces.txt");
        await File.WriteAllTextAsync(spaceFile, "    indented\n");

        var (c2, out2, _) = await RunAsync("unexpand -t 4 spaces.txt");
        Assert.Equal(0, c2);
        Assert.Equal("\tindented" + Environment.NewLine, out2);
    }

    // ==========================================
    // System & Generator Commands Tests
    // ==========================================

    [Fact]
    public async Task Arch_PrintsArchitecture()
    {
        var (c1, out1, _) = await RunAsync("arch");
        Assert.Equal(0, c1);
        Assert.True(!string.IsNullOrWhiteSpace(out1));
    }

    [Fact]
    public async Task Id_PrintsUserAndGroups()
    {
        var (c1, out1, _) = await RunAsync("id");
        Assert.Equal(0, c1);
        Assert.Contains("uid=1000", out1);
        Assert.Contains("gid=1000", out1);

        var (c2, out2, _) = await RunAsync("id -u");
        Assert.Equal(0, c2);
        Assert.Equal("1000" + Environment.NewLine, out2);
    }

    [Fact]
    public async Task Groups_PrintsGroups()
    {
        var (c1, out1, _) = await RunAsync("groups");
        Assert.Equal(0, c1);
        Assert.True(!string.IsNullOrWhiteSpace(out1));
    }

    [Fact]
    public async Task Printenv_PrintsVariables()
    {
        _context.EnvironmentVariables["MY_CUSTOM_VAR"] = "linuxterm_rocks";

        var (c1, out1, _) = await RunAsync("printenv MY_CUSTOM_VAR");
        Assert.Equal(0, c1);
        Assert.Equal("linuxterm_rocks" + Environment.NewLine, out1);
    }

    [Fact]
    public async Task Seq_GeneratesSequence()
    {
        var (c1, out1, _) = await RunAsync("seq 3");
        Assert.Equal(0, c1);
        Assert.Equal($"1{Environment.NewLine}2{Environment.NewLine}3{Environment.NewLine}", out1);

        var (c2, out2, _) = await RunAsync("seq 1 2 5");
        Assert.Equal(0, c2);
        Assert.Equal($"1{Environment.NewLine}3{Environment.NewLine}5{Environment.NewLine}", out2);
    }

    [Fact]
    public async Task True_And_False_ExitCodes()
    {
        var (c1, _, _) = await RunAsync("true");
        Assert.Equal(0, c1);

        var (c2, _, _) = await RunAsync("false");
        Assert.Equal(1, c2);
    }

    [Fact]
    public async Task Cal_PrintsCalendar()
    {
        var (c1, out1, _) = await RunAsync("cal 9 2026");
        Assert.Equal(0, c1);
        Assert.Contains("September 2026", out1);
        Assert.Contains("Su Mo Tu We Th Fr Sa", out1);
    }

    [Fact]
    public async Task Shuf_GeneratesRandomOutput()
    {
        var (c1, out1, _) = await RunAsync("shuf -i 1-10 -n 3");
        Assert.Equal(0, c1);
        var lines = out1.Trim().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public async Task Top_BatchMode_ExecutesOnce()
    {
        var (c1, out1, _) = await RunAsync("top -b -n 1");
        Assert.Equal(0, c1);
        Assert.Contains("Tasks:", out1);
        Assert.Contains("MiB Mem :", out1);
    }

    // ==========================================
    // Network Diagnostics Commands Tests
    // ==========================================

    [Fact]
    public async Task Ifconfig_And_Ip_ExecuteSuccessfully()
    {
        var (c1, out1, _) = await RunAsync("ifconfig");
        Assert.Equal(0, c1);
        Assert.True(!string.IsNullOrWhiteSpace(out1));

        var (c2, out2, _) = await RunAsync("ip addr");
        Assert.Equal(0, c2);
        Assert.True(!string.IsNullOrWhiteSpace(out2));
    }

    [Fact]
    public async Task Nslookup_ResolvesLocalhost()
    {
        var (c1, out1, _) = await RunAsync("nslookup localhost");
        Assert.Equal(0, c1);
        Assert.Contains("Non-authoritative answer:", out1);
        Assert.Contains("127.0.0.1", out1);
    }

    [Fact]
    public async Task Ping_Localhost_SendsPackets()
    {
        var (c1, out1, _) = await RunAsync("ping -c 1 -W 2 127.0.0.1");
        Assert.Equal(0, c1);
        Assert.Contains("64 bytes from 127.0.0.1", out1);
        Assert.Contains("ping statistics", out1);
    }

    [Fact]
    public async Task Tar_Gzip_CreateAndExtract_WorksCorrectly()
    {
        var src = Path.Combine(_tempDir, "targz_src.txt");
        await File.WriteAllTextAsync(src, "tar gzip content test");

        var (c1, _, _) = await RunAsync("tar -czf archive.tar.gz targz_src.txt");
        Assert.Equal(0, c1);
        Assert.True(File.Exists(Path.Combine(_tempDir, "archive.tar.gz")));

        var outDir = Path.Combine(_tempDir, "targz_out");
        Directory.CreateDirectory(outDir);
        var (c2, _, _) = await RunAsync("tar -xzf archive.tar.gz -C targz_out");
        Assert.Equal(0, c2);
        Assert.True(File.Exists(Path.Combine(outDir, "targz_src.txt")));
        Assert.Equal("tar gzip content test", await File.ReadAllTextAsync(Path.Combine(outDir, "targz_src.txt")));
    }

    [Fact]
    public async Task Yes_PipedToHead_TerminatesCleanly()
    {
        var (c1, out1, _) = await RunAsync("yes hello | head -n 3");
        Assert.Equal(0, c1);
        var lines = out1.Trim().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.All(lines, l => Assert.Equal("hello", l));
    }

    [Fact]
    public async Task Htop_BatchMode_RendersFrame()
    {
        var (c1, out1, _) = await RunAsync("htop -b");
        Assert.Equal(0, c1);
        Assert.Contains("Mem[", out1);
        Assert.Contains("Tasks:", out1);
        Assert.Contains("Load average:", out1);
        Assert.Contains("Uptime:", out1);
        Assert.Contains("PID", out1);
        Assert.Contains("Command", out1);
        Assert.Contains("F10", out1);
        Assert.Contains("Quit", out1);
    }

    [Fact]
    public async Task Htop_Help_PrintsUsage()
    {
        var (c1, out1, _) = await RunAsync("htop --help");
        Assert.Equal(0, c1);
        Assert.Contains("htop - interactive process viewer", out1);
        Assert.Contains("Usage: htop", out1);
        Assert.Contains("--delay", out1);
        Assert.Contains("--sort-key", out1);
    }
}
