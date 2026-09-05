using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Common;

namespace LinuxTerm.Core.Commands;

public class FileCommand : ICommand
{
    public string Name => "file";
    public string Description => "Determine file type";
    public string Synopsis => "file [-b] [-i|--mime-type] FILE...";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        if (args.Length == 0)
        {
            await stderr.WriteLineAsync("Usage: file [-b] [-i|--mime-type] FILE...");
            return 1;
        }

        bool brief = false;
        bool mime = false;
        var files = new List<string>();

        foreach (var arg in args)
        {
            if (arg == "-b" || arg == "--brief")
            {
                brief = true;
            }
            else if (arg == "-i" || arg == "--mime-type" || arg == "--mime")
            {
                mime = true;
            }
            else if (!arg.StartsWith('-') || arg == "-")
            {
                files.Add(arg);
            }
        }

        if (files.Count == 0)
        {
            await stderr.WriteLineAsync("file: missing operand");
            return 1;
        }

        int exitCode = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            if (file == "-")
            {
                using var ms = new MemoryStream();
                var buffer = new byte[4096];
                int read = await stdin.BaseStreamOrReadAsync(buffer, ct);
                var (desc, mimeType) = InspectBytes(buffer.AsSpan(0, read));
                var result = mime ? mimeType : desc;
                await stdout.WriteLineAsync(brief ? result : $"/dev/stdin: {result}");
                continue;
            }

            var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
            if (Directory.Exists(winPath))
            {
                var desc = mime ? "inode/directory; charset=binary" : "directory";
                await stdout.WriteLineAsync(brief ? desc : $"{file}: {desc}");
                continue;
            }

            if (!File.Exists(winPath))
            {
                await stderr.WriteLineAsync($"file: cannot open '{file}' (No such file or directory)");
                exitCode = 1;
                continue;
            }

            try
            {
                var fi = new FileInfo(winPath);
                if (fi.LinkTarget != null)
                {
                    var desc = mime ? "inode/symlink; charset=binary" : $"symbolic link to {fi.LinkTarget}";
                    await stdout.WriteLineAsync(brief ? desc : $"{file}: {desc}");
                    continue;
                }

                if (fi.Length == 0)
                {
                    var desc = mime ? "application/x-empty" : "empty";
                    await stdout.WriteLineAsync(brief ? desc : $"{file}: {desc}");
                    continue;
                }

                byte[] header = new byte[(int)Math.Min(4096, fi.Length)];
                using (var fs = File.OpenRead(winPath))
                {
                    int totalRead = 0;
                    while (totalRead < header.Length)
                    {
                        int r = await fs.ReadAsync(header.AsMemory(totalRead, header.Length - totalRead), ct);
                        if (r == 0) break;
                        totalRead += r;
                    }
                }

                var (description, mimeType) = InspectBytes(header);
                var outputText = mime ? mimeType : description;
                await stdout.WriteLineAsync(brief ? outputText : $"{file}: {outputText}");
            }
            catch (Exception ex)
            {
                await stderr.WriteLineAsync($"file: {file}: {ex.Message}");
                exitCode = 1;
            }
        }

        return exitCode;
    }

    private static (string Description, string MimeType) InspectBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
            return ("empty", "application/x-empty");

        // Magic bytes checks
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return ("PNG image data", "image/png");

        // JPEG: FF D8 FF
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ("JPEG image data", "image/jpeg");

        // GIF: GIF87a or GIF89a
        if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
            return ("GIF image data", "image/gif");

        // PDF: %PDF
        if (bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
            return ("PDF document", "application/pdf");

        // ZIP: PK\x03\x04
        if (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04)
            return ("Zip archive data", "application/zip");

        // GZIP: 1F 8B
        if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
            return ("gzip compressed data", "application/gzip");

        // BZIP2: BZh
        if (bytes.Length >= 3 && bytes[0] == 0x42 && bytes[1] == 0x5A && bytes[2] == 0x68)
            return ("bzip2 compressed data", "application/x-bzip2");

        // TAR check (offset 257 "ustar")
        if (bytes.Length >= 262 &&
            bytes[257] == 'u' && bytes[258] == 's' && bytes[259] == 't' && bytes[260] == 'a' && bytes[261] == 'r')
            return ("POSIX tar archive", "application/x-tar");

        // Windows PE executable: MZ
        if (bytes.Length >= 2 && bytes[0] == 0x4D && bytes[1] == 0x5A)
            return ("PE32/PE32+ executable (Windows)", "application/x-dosexec");

        // Linux ELF executable: \x7fELF
        if (bytes.Length >= 4 && bytes[0] == 0x7F && bytes[1] == 0x45 && bytes[2] == 0x4C && bytes[3] == 0x4F)
            return ("ELF executable", "application/x-executable");

        // BMP: BM
        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
            return ("PC bitmap, Windows 3.x format", "image/bmp");

        // Shebang script: #!
        if (bytes.Length >= 2 && bytes[0] == 0x23 && bytes[1] == 0x21)
        {
            var firstLine = Encoding.ASCII.GetString(bytes[..Math.Min(bytes.Length, 128)]).Split('\n')[0].Trim();
            if (firstLine.Contains("python"))
                return ("Python script, ASCII text executable", "text/x-python");
            if (firstLine.Contains("bash") || firstLine.Contains("sh"))
                return ("Bourne-Again shell script, ASCII text executable", "text/x-shellscript");
            return ("Script text executable", "text/plain");
        }

        // Try decoding as UTF-8 or ASCII text
        try
        {
            var text = Encoding.UTF8.GetString(bytes);
            // Check if it's text (control chars except \r, \n, \t, \b, \f are rare)
            bool isBinary = false;
            foreach (var b in bytes)
            {
                if (b == 0 || (b < 9 && b != 0) || (b > 13 && b < 32))
                {
                    isBinary = true;
                    break;
                }
            }

            if (!isBinary)
            {
                var trimmed = text.TrimStart();
                if ((trimmed.StartsWith('{') && trimmed.Contains('}')) || (trimmed.StartsWith('[') && trimmed.Contains(']')))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(text);
                        return ("JSON text data", "application/json");
                    }
                    catch { }
                }

                if (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
                {
                    return ("HTML/XML document text", "text/html");
                }

                bool hasNonAscii = bytes.ToArray().Any(b => b >= 128);
                if (hasNonAscii)
                    return ("UTF-8 Unicode text", "text/plain; charset=utf-8");
                return ("ASCII text", "text/plain; charset=us-ascii");
            }
        }
        catch { }

        return ("data", "application/octet-stream");
    }
}

internal static class StreamExtensions
{
    public static async Task<int> BaseStreamOrReadAsync(this TextReader reader, byte[] buffer, CancellationToken ct)
    {
        var chars = new char[buffer.Length];
        int charsRead = await reader.ReadAsync(chars, 0, chars.Length);
        if (charsRead <= 0) return 0;
        return Encoding.UTF8.GetBytes(chars, 0, charsRead, buffer, 0);
    }
}

public class StatCommand : ICommand
{
    public string Name => "stat";
    public string Description => "Display file or file system status";
    public string Synopsis => "stat [-c FORMAT | --format=FORMAT] [-t] FILE...";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        if (args.Length == 0)
        {
            await stderr.WriteLineAsync("stat: missing operand");
            return 1;
        }

        string? customFormat = null;
        bool terse = false;
        var files = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if ((arg == "-c" || arg == "--format") && i + 1 < args.Length)
            {
                customFormat = args[++i];
            }
            else if (arg.StartsWith("--format="))
            {
                customFormat = arg["--format=".Length..];
            }
            else if (arg == "-t" || arg == "--terse")
            {
                terse = true;
            }
            else if (!arg.StartsWith('-'))
            {
                files.Add(arg);
            }
        }

        if (files.Count == 0)
        {
            await stderr.WriteLineAsync("stat: missing operand");
            return 1;
        }

        int exitCode = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
            bool isDir = Directory.Exists(winPath);
            bool isFile = File.Exists(winPath);

            if (!isDir && !isFile)
            {
                await stderr.WriteLineAsync($"stat: cannot statx '{file}': No such file or directory");
                exitCode = 1;
                continue;
            }

            FileSystemInfo info = isDir ? new DirectoryInfo(winPath) : new FileInfo(winPath);
            long size = info is FileInfo fi ? fi.Length : 4096;
            long blocks = (size + 511) / 512;
            int ioBlock = 4096;
            string fileType = isDir ? "directory" : "regular file";
            string accessOctal = isDir ? "0755" : (info.Attributes.HasFlag(FileAttributes.ReadOnly) ? "0444" : "0644");
            string accessStr = isDir ? "drwxr-xr-x" : (info.Attributes.HasFlag(FileAttributes.ReadOnly) ? "-r--r--r--" : "-rw-r--r--");
            string user = context.UserName;
            string group = context.UserName;
            int uid = 1000;
            int gid = 1000;
            string accessTime = info.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss.000000000 zzz");
            string modifyTime = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss.000000000 zzz");
            string changeTime = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss.000000000 zzz");
            string birthTime = info.CreationTime.ToString("yyyy-MM-dd HH:mm:ss.000000000 zzz");

            if (customFormat != null)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < customFormat.Length; i++)
                {
                    if (customFormat[i] == '%' && i + 1 < customFormat.Length)
                    {
                        char spec = customFormat[++i];
                        switch (spec)
                        {
                            case 'n': sb.Append(file); break;
                            case 'N': sb.Append($"'{file}'"); break;
                            case 's': sb.Append(size); break;
                            case 'b': sb.Append(blocks); break;
                            case 'B': sb.Append(512); break;
                            case 'o': sb.Append(ioBlock); break;
                            case 'F': sb.Append(fileType); break;
                            case 'a': sb.Append(accessOctal); break;
                            case 'A': sb.Append(accessStr); break;
                            case 'u': sb.Append(uid); break;
                            case 'U': sb.Append(user); break;
                            case 'g': sb.Append(gid); break;
                            case 'G': sb.Append(group); break;
                            case 'x': sb.Append(accessTime); break;
                            case 'y': sb.Append(modifyTime); break;
                            case 'z': sb.Append(changeTime); break;
                            case 'w': sb.Append(birthTime); break;
                            case '%': sb.Append('%'); break;
                            default: sb.Append('%').Append(spec); break;
                        }
                    }
                    else
                    {
                        sb.Append(customFormat[i]);
                    }
                }
                await stdout.WriteLineAsync(sb.ToString());
            }
            else if (terse)
            {
                await stdout.WriteLineAsync($"{file} {size} {blocks} {accessOctal} {uid} {gid} 0 0 {ioBlock} 1 0 0 {new DateTimeOffset(info.LastAccessTimeUtc).ToUnixTimeSeconds()} {new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds()} {new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds()} {new DateTimeOffset(info.CreationTimeUtc).ToUnixTimeSeconds()}");
            }
            else
            {
                await stdout.WriteLineAsync($"  File: {file}");
                await stdout.WriteLineAsync($"  Size: {size,-10} Blocks: {blocks,-10} IO Block: {ioBlock,-6} {fileType}");
                await stdout.WriteLineAsync($"Device: 0,0        Inode: 100000      Links: 1");
                await stdout.WriteLineAsync($"Access: ({accessOctal}/{accessStr})  Uid: ({uid,5}/{user,8})   Gid: ({gid,5}/{group,8})");
                await stdout.WriteLineAsync($"Access: {accessTime}");
                await stdout.WriteLineAsync($"Modify: {modifyTime}");
                await stdout.WriteLineAsync($"Change: {changeTime}");
                await stdout.WriteLineAsync($" Birth: {birthTime}");
            }
        }

        return exitCode;
    }
}

public class BasenameCommand : ICommand
{
    public string Name => "basename";
    public string Description => "Strip directory and suffix from filenames";
    public string Synopsis => "basename NAME [SUFFIX] | basename -a [-s SUFFIX] NAME...";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        if (args.Length == 0)
        {
            await stderr.WriteLineAsync("basename: missing operand");
            return 1;
        }

        bool multiple = false;
        string? suffix = null;
        var names = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "-a" || arg == "--multiple")
            {
                multiple = true;
            }
            else if ((arg == "-s" || arg == "--suffix") && i + 1 < args.Length)
            {
                suffix = args[++i];
                multiple = true;
            }
            else if (arg.StartsWith("-s"))
            {
                suffix = arg[2..];
                multiple = true;
            }
            else if (!arg.StartsWith('-'))
            {
                names.Add(arg);
            }
        }

        if (names.Count == 0)
        {
            await stderr.WriteLineAsync("basename: missing operand");
            return 1;
        }

        if (!multiple && names.Count >= 2 && suffix == null)
        {
            suffix = names[1];
            names = new List<string> { names[0] };
        }

        foreach (var name in names)
        {
            var clean = name.TrimEnd('/', '\\');
            if (string.IsNullOrEmpty(clean))
            {
                await stdout.WriteLineAsync(name.Contains('/') || name.Contains('\\') ? "/" : "");
                continue;
            }

            var lastSlash = Math.Max(clean.LastIndexOf('/'), clean.LastIndexOf('\\'));
            var baseName = lastSlash >= 0 ? clean[(lastSlash + 1)..] : clean;

            if (!string.IsNullOrEmpty(suffix) && baseName.EndsWith(suffix) && baseName != suffix)
            {
                baseName = baseName[..^suffix.Length];
            }

            await stdout.WriteLineAsync(baseName);
        }

        return 0;
    }
}

public class DirnameCommand : ICommand
{
    public string Name => "dirname";
    public string Description => "Strip last component from file name";
    public string Synopsis => "dirname [OPTION] NAME...";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        if (args.Length == 0)
        {
            await stderr.WriteLineAsync("dirname: missing operand");
            return 1;
        }

        bool zeroTerminated = false;
        var names = new List<string>();

        foreach (var arg in args)
        {
            if (arg == "-z" || arg == "--zero")
            {
                zeroTerminated = true;
            }
            else if (!arg.StartsWith('-'))
            {
                names.Add(arg);
            }
        }

        if (names.Count == 0)
        {
            await stderr.WriteLineAsync("dirname: missing operand");
            return 1;
        }

        char terminator = zeroTerminated ? '\0' : '\n';
        foreach (var name in names)
        {
            var normalized = name.Replace('\\', '/');
            var trimmed = normalized.TrimEnd('/');

            string res;
            if (string.IsNullOrEmpty(trimmed))
            {
                res = "/";
            }
            else
            {
                var lastSlash = trimmed.LastIndexOf('/');
                if (lastSlash < 0)
                    res = ".";
                else if (lastSlash == 0)
                    res = "/";
                else
                    res = trimmed[..lastSlash];
            }

            if (zeroTerminated)
                await stdout.WriteAsync($"{res}\0");
            else
                await stdout.WriteLineAsync(res);
        }

        return 0;
    }
}

public class RealpathCommand : ICommand
{
    public string Name => "realpath";
    public string Description => "Print the resolved path";
    public string Synopsis => "realpath [-q] [-m] [-e] [--relative-to=DIR] FILE...";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        if (args.Length == 0)
        {
            await stderr.WriteLineAsync("realpath: missing operand");
            return 1;
        }

        bool quiet = false;
        bool mustExist = false;
        string? relativeTo = null;
        var files = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "-q" || arg == "--quiet")
            {
                quiet = true;
            }
            else if (arg == "-e" || arg == "--canonicalize-existing")
            {
                mustExist = true;
            }
            else if (arg == "-m" || arg == "--canonicalize-missing")
            {
                mustExist = false;
            }
            else if (arg.StartsWith("--relative-to="))
            {
                relativeTo = arg["--relative-to=".Length..];
            }
            else if (!arg.StartsWith('-'))
            {
                files.Add(arg);
            }
        }

        if (files.Count == 0)
        {
            await stderr.WriteLineAsync("realpath: missing operand");
            return 1;
        }

        int exitCode = 0;
        foreach (var file in files)
        {
            var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
            if (mustExist && !File.Exists(winPath) && !Directory.Exists(winPath))
            {
                if (!quiet)
                {
                    await stderr.WriteLineAsync($"realpath: '{file}': No such file or directory");
                }
                exitCode = 1;
                continue;
            }

            var fullPath = Path.GetFullPath(winPath);
            if (!string.IsNullOrEmpty(relativeTo))
            {
                var relWin = Path.GetFullPath(PosixPathMapper.ToWindows(relativeTo, context.CurrentDirectory));
                var rel = Path.GetRelativePath(relWin, fullPath).Replace('\\', '/');
                await stdout.WriteLineAsync(rel);
            }
            else
            {
                var posix = PosixPathMapper.ToPosix(fullPath, useTildeForHome: false);
                await stdout.WriteLineAsync(posix);
            }
        }

        return exitCode;
    }
}

public class ReadlinkCommand : ICommand
{
    public string Name => "readlink";
    public string Description => "Print resolved symbolic links or canonical file names";
    public string Synopsis => "readlink [-f] [-e] [-m] [-n] [-q] FILE...";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        if (args.Length == 0)
        {
            return 1;
        }

        bool canonicalize = false;
        bool noNewline = false;
        bool quiet = false;
        var files = new List<string>();

        foreach (var arg in args)
        {
            if (arg == "-f" || arg == "-e" || arg == "-m" || arg == "--canonicalize")
            {
                canonicalize = true;
            }
            else if (arg == "-n")
            {
                noNewline = true;
            }
            else if (arg == "-q" || arg == "-s" || arg == "--silent" || arg == "--quiet")
            {
                quiet = true;
            }
            else if (!arg.StartsWith('-'))
            {
                files.Add(arg);
            }
        }

        if (files.Count == 0)
            return 1;

        int exitCode = 0;
        foreach (var file in files)
        {
            var winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
            if (!File.Exists(winPath) && !Directory.Exists(winPath) && !canonicalize)
            {
                if (!quiet)
                {
                    await stderr.WriteLineAsync($"readlink: {file}: No such file or directory");
                }
                exitCode = 1;
                continue;
            }

            if (canonicalize)
            {
                var full = Path.GetFullPath(winPath);
                var posix = PosixPathMapper.ToPosix(full, useTildeForHome: false);
                if (noNewline)
                    await stdout.WriteAsync(posix);
                else
                    await stdout.WriteLineAsync(posix);
            }
            else
            {
                FileSystemInfo? info = null;
                if (File.Exists(winPath)) info = new FileInfo(winPath);
                else if (Directory.Exists(winPath)) info = new DirectoryInfo(winPath);

                if (info != null && info.LinkTarget != null)
                {
                    var target = PosixPathMapper.ToPosix(info.LinkTarget, useTildeForHome: false);
                    if (noNewline)
                        await stdout.WriteAsync(target);
                    else
                        await stdout.WriteLineAsync(target);
                }
                else
                {
                    exitCode = 1;
                }
            }
        }

        return exitCode;
    }
}
