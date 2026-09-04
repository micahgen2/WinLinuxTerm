using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Common;

namespace LinuxTerm.Core.Commands;

public class Md5SumCommand : ICommand
{
    public string Name => "md5sum";
    public string Description => "Compute and check MD5 message digest";
    public string Synopsis => "md5sum [FILE]...";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var files = new List<string>(args);
        if (files.Count == 0) files.Add("-");

        foreach (var file in files)
        {
            if (file == "-")
            {
                var text = await stdin.ReadToEndAsync(ct);
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                var hash = MD5.HashData(bytes);
                stdout.WriteLine($"{Convert.ToHexStringLower(hash)}  -");
            }
            else
            {
                var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
                if (!File.Exists(winPath))
                {
                    await stderr.WriteLineAsync($"md5sum: {file}: No such file or directory");
                    continue;
                }

                await using var stream = File.OpenRead(winPath);
                var hash = await MD5.HashDataAsync(stream, ct);
                stdout.WriteLine($"{Convert.ToHexStringLower(hash)}  {file}");
            }
        }

        return 0;
    }
}

public class Sha256SumCommand : ICommand
{
    public string Name => "sha256sum";
    public string Description => "Compute and check SHA256 message digest";
    public string Synopsis => "sha256sum [FILE]...";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var files = new List<string>(args);
        if (files.Count == 0) files.Add("-");

        foreach (var file in files)
        {
            if (file == "-")
            {
                var text = await stdin.ReadToEndAsync(ct);
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                var hash = SHA256.HashData(bytes);
                stdout.WriteLine($"{Convert.ToHexStringLower(hash)}  -");
            }
            else
            {
                var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
                if (!File.Exists(winPath))
                {
                    await stderr.WriteLineAsync($"sha256sum: {file}: No such file or directory");
                    continue;
                }

                await using var stream = File.OpenRead(winPath);
                var hash = await SHA256.HashDataAsync(stream, ct);
                stdout.WriteLine($"{Convert.ToHexStringLower(hash)}  {file}");
            }
        }

        return 0;
    }
}
