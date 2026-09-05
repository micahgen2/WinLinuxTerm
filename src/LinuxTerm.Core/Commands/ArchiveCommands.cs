using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Common;

namespace LinuxTerm.Core.Commands;

public class TarCommand : ICommand
{
    public string Name => "tar";
    public string Description => "Tape archive tool to create, extract, and list tar archives";
    public string Synopsis => "tar [-c|-x|-t] [-z] [-v] [-f ARCHIVE] [-C DIR] [FILE...]";

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
            await stderr.WriteLineAsync("tar: You must specify one of the '-c', '-x', or '-t' options");
            return 1;
        }

        bool create = false;
        bool extract = false;
        bool list = false;
        bool gzip = false;
        bool verbose = false;
        string? archiveFile = null;
        string? targetDir = null;
        var files = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "-f" && i + 1 < args.Length)
            {
                archiveFile = args[++i];
            }
            else if (arg == "-C" && i + 1 < args.Length)
            {
                targetDir = args[++i];
            }
            else if (arg.StartsWith('-') && arg.Length > 1)
            {
                for (int j = 1; j < arg.Length; j++)
                {
                    char flag = arg[j];
                    switch (flag)
                    {
                        case 'c': create = true; break;
                        case 'x': extract = true; break;
                        case 't': list = true; break;
                        case 'z': gzip = true; break;
                        case 'v': verbose = true; break;
                        case 'f':
                            if (j + 1 < arg.Length)
                            {
                                archiveFile = arg[(j + 1)..];
                                j = arg.Length; // consumed rest
                            }
                            else if (i + 1 < args.Length)
                            {
                                archiveFile = args[++i];
                            }
                            break;
                        case 'C':
                            if (i + 1 < args.Length) targetDir = args[++i];
                            break;
                    }
                }
            }
            else
            {
                files.Add(arg);
            }
        }

        if (!create && !extract && !list)
        {
            await stderr.WriteLineAsync("tar: You must specify one of the '-c', '-x', or '-t' options");
            return 1;
        }

        if (string.IsNullOrEmpty(archiveFile))
        {
            await stderr.WriteLineAsync("tar: Archive file not specified (-f)");
            return 1;
        }

        string fullArchive = PosixPathMapper.ToWindows(archiveFile, context.CurrentDirectory);
        string destDir = string.IsNullOrEmpty(targetDir)
            ? context.CurrentDirectory
            : PosixPathMapper.ToWindows(targetDir, context.CurrentDirectory);

        if (archiveFile.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            archiveFile.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            gzip = true;
        }

        try
        {
            if (create)
            {
                var dir = Path.GetDirectoryName(fullArchive);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using var fileStream = File.Create(fullArchive);
                Stream targetStream = gzip
                    ? new GZipStream(fileStream, CompressionMode.Compress)
                    : fileStream;

                try
                {
                    using var writer = new TarWriter(targetStream, TarEntryFormat.Pax, leaveOpen: true);
                    foreach (var f in files)
                    {
                        if (ct.IsCancellationRequested) return 130;
                        string winPath = PosixPathMapper.ToWindows(f, context.CurrentDirectory);
                        if (File.Exists(winPath))
                        {
                            string entryName = Path.GetFileName(winPath);
                            writer.WriteEntry(winPath, entryName);
                            if (verbose) await stdout.WriteLineAsync(entryName);
                        }
                        else if (Directory.Exists(winPath))
                        {
                            string baseDirName = Path.GetFileName(winPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                            var allFiles = Directory.GetFiles(winPath, "*", SearchOption.AllDirectories);
                            foreach (var subFile in allFiles)
                            {
                                if (ct.IsCancellationRequested) return 130;
                                string relative = Path.GetRelativePath(winPath, subFile);
                                string entryName = string.IsNullOrEmpty(baseDirName) ? relative : $"{baseDirName}/{relative.Replace('\\', '/')}";
                                writer.WriteEntry(subFile, entryName);
                                if (verbose) await stdout.WriteLineAsync(entryName);
                            }
                        }
                        else
                        {
                            await stderr.WriteLineAsync($"tar: {f}: Cannot stat: No such file or directory");
                        }
                    }
                }
                finally
                {
                    if (gzip && targetStream is GZipStream gz)
                    {
                        await gz.FlushAsync(ct);
                        gz.Dispose();
                    }
                }

                return 0;
            }

            if (!File.Exists(fullArchive))
            {
                await stderr.WriteLineAsync($"tar: {archiveFile}: Cannot open: No such file or directory");
                return 2;
            }

            using var inStream = File.OpenRead(fullArchive);
            Stream readStream = gzip
                ? new GZipStream(inStream, CompressionMode.Decompress)
                : inStream;

            using var reader = new TarReader(readStream);

            if (list)
            {
                while (reader.GetNextEntry() is { } entry)
                {
                    if (ct.IsCancellationRequested) return 130;
                    await stdout.WriteLineAsync(entry.Name);
                }
                return 0;
            }

            if (extract)
            {
                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                while (reader.GetNextEntry() is { } entry)
                {
                    if (ct.IsCancellationRequested) return 130;
                    string targetPath = Path.Combine(destDir, entry.Name.Replace('/', Path.DirectorySeparatorChar));
                    var parent = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                        Directory.CreateDirectory(parent);

                    if (entry.EntryType == TarEntryType.Directory)
                    {
                        if (!Directory.Exists(targetPath))
                            Directory.CreateDirectory(targetPath);
                    }
                    else
                    {
                        entry.ExtractToFile(targetPath, overwrite: true);
                    }

                    if (verbose) await stdout.WriteLineAsync(entry.Name);
                }
                return 0;
            }
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"tar: Error: {ex.Message}");
            return 2;
        }

        return 0;
    }
}

public class GzipCommand : ICommand
{
    public string Name => "gzip";
    public string Description => "Compress or decompress files using Lempel-Ziv coding (LZ77)";
    public string Synopsis => "gzip [-d] [-k] [-c] [FILE...]";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        bool decompress = false;
        bool keep = false;
        bool toStdout = false;
        var files = new List<string>();

        foreach (var arg in args)
        {
            if (arg is "-d" or "--decompress" or "--uncompress") decompress = true;
            else if (arg is "-k" or "--keep") keep = true;
            else if (arg is "-c" or "--stdout" or "--to-stdout") toStdout = true;
            else if (arg.StartsWith('-') && arg.Length > 1)
            {
                if (arg.Contains('d')) decompress = true;
                if (arg.Contains('k')) keep = true;
                if (arg.Contains('c')) toStdout = true;
            }
            else files.Add(arg);
        }

        if (files.Count == 0 || (files.Count == 1 && files[0] == "-"))
        {
            // Stream processing via stdin/stdout
            try
            {
                if (decompress)
                {
                    var text = await stdin.ReadToEndAsync(ct);
                    var bytes = Convert.FromBase64String(text.Trim());
                    using var msIn = new MemoryStream(bytes);
                    using var gz = new GZipStream(msIn, CompressionMode.Decompress);
                    using var reader = new StreamReader(gz);
                    await stdout.WriteAsync(await reader.ReadToEndAsync(ct));
                }
                else
                {
                    var text = await stdin.ReadToEndAsync(ct);
                    using var msOut = new MemoryStream();
                    using (var gz = new GZipStream(msOut, CompressionMode.Compress, leaveOpen: true))
                    using (var writer = new StreamWriter(gz))
                    {
                        await writer.WriteAsync(text);
                    }
                    await stdout.WriteAsync(Convert.ToBase64String(msOut.ToArray()));
                }
                return 0;
            }
            catch (Exception ex)
            {
                await stderr.WriteLineAsync($"gzip: {ex.Message}");
                return 1;
            }
        }

        int exitCode = 0;
        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) return 130;
            string winPath = PosixPathMapper.ToWindows(file, context.CurrentDirectory);
            if (!File.Exists(winPath))
            {
                await stderr.WriteLineAsync($"gzip: {file}: No such file or directory");
                exitCode = 1;
                continue;
            }

            try
            {
                if (decompress)
                {
                    string outPath = winPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                        ? winPath[..^3]
                        : winPath + ".out";

                    using (var inStream = File.OpenRead(winPath))
                    using (var gz = new GZipStream(inStream, CompressionMode.Decompress))
                    {
                        if (toStdout)
                        {
                            using var r = new StreamReader(gz);
                            await stdout.WriteAsync(await r.ReadToEndAsync(ct));
                        }
                        else
                        {
                            using var outStream = File.Create(outPath);
                            await gz.CopyToAsync(outStream, ct);
                        }
                    }

                    if (!keep && !toStdout)
                        File.Delete(winPath);
                }
                else
                {
                    string outPath = winPath + ".gz";
                    if (toStdout)
                    {
                        using var inStream = File.OpenRead(winPath);
                        using var gz = new GZipStream(new MemoryStream(), CompressionMode.Compress);
                        await inStream.CopyToAsync(gz, ct);
                    }
                    else
                    {
                        using var inStream = File.OpenRead(winPath);
                        using var outStream = File.Create(outPath);
                        using var gz = new GZipStream(outStream, CompressionMode.Compress);
                        await inStream.CopyToAsync(gz, ct);
                    }

                    if (!keep && !toStdout)
                        File.Delete(winPath);
                }
            }
            catch (Exception ex)
            {
                await stderr.WriteLineAsync($"gzip: {file}: {ex.Message}");
                exitCode = 1;
            }
        }

        return exitCode;
    }
}

public class GunzipCommand : ICommand
{
    private readonly GzipCommand _gzip = new();

    public string Name => "gunzip";
    public string Description => "Decompress files created by gzip";
    public string Synopsis => "gunzip [-k] [-c] [FILE...]";

    public Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        var newArgs = new List<string> { "-d" };
        newArgs.AddRange(args);
        return _gzip.ExecuteAsync(newArgs.ToArray(), context, stdin, stdout, stderr, ct);
    }
}

public class ZipCommand : ICommand
{
    public string Name => "zip";
    public string Description => "Package and compress files into a zip archive";
    public string Synopsis => "zip [-r] [-q] ARCHIVE [FILE...]";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        bool recursive = false;
        bool quiet = false;
        string? zipFile = null;
        var files = new List<string>();

        foreach (var arg in args)
        {
            if (arg is "-r" or "-r9" or "-rq") recursive = true;
            else if (arg is "-q") quiet = true;
            else if (zipFile == null) zipFile = arg;
            else files.Add(arg);
        }

        if (zipFile == null || files.Count == 0)
        {
            await stderr.WriteLineAsync("zip: Usage: zip [-r] [-q] ARCHIVE [FILE...]");
            return 1;
        }

        if (!zipFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            zipFile += ".zip";

        string fullZipPath = PosixPathMapper.ToWindows(zipFile, context.CurrentDirectory);
        var dir = Path.GetDirectoryName(fullZipPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        try
        {
            bool isExisting = File.Exists(fullZipPath);
            using var archive = ZipFile.Open(fullZipPath, isExisting ? ZipArchiveMode.Update : ZipArchiveMode.Create);

            foreach (var f in files)
            {
                if (ct.IsCancellationRequested) return 130;
                string winPath = PosixPathMapper.ToWindows(f, context.CurrentDirectory);

                if (File.Exists(winPath))
                {
                    string entryName = Path.GetFileName(winPath);
                    // Remove existing if updating
                    if (isExisting)
                    {
                        archive.GetEntry(entryName)?.Delete();
                    }
                    archive.CreateEntryFromFile(winPath, entryName, CompressionLevel.Optimal);
                    if (!quiet) await stdout.WriteLineAsync($"  adding: {entryName} (deflated)");
                }
                else if (Directory.Exists(winPath))
                {
                    string baseDirName = Path.GetFileName(winPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    var searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    var allFiles = Directory.GetFiles(winPath, "*", searchOpt);

                    foreach (var subFile in allFiles)
                    {
                        if (ct.IsCancellationRequested) return 130;
                        string relative = Path.GetRelativePath(winPath, subFile);
                        string entryName = string.IsNullOrEmpty(baseDirName) ? relative : $"{baseDirName}/{relative.Replace('\\', '/')}";
                        if (isExisting)
                        {
                            archive.GetEntry(entryName)?.Delete();
                        }
                        archive.CreateEntryFromFile(subFile, entryName, CompressionLevel.Optimal);
                        if (!quiet) await stdout.WriteLineAsync($"  adding: {entryName} (deflated)");
                    }
                }
                else
                {
                    await stderr.WriteLineAsync($"zip warning: name not matched: {f}");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"zip error: {ex.Message}");
            return 1;
        }
    }
}

public class UnzipCommand : ICommand
{
    public string Name => "unzip";
    public string Description => "List, test and extract compressed files in a ZIP archive";
    public string Synopsis => "unzip [-l] [-o] [-d DIR] ARCHIVE";

    public async Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        bool list = false;
        bool overwrite = false;
        string? targetDir = null;
        string? zipFile = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-l") list = true;
            else if (arg is "-o") overwrite = true;
            else if (arg is "-d" && i + 1 < args.Length) targetDir = args[++i];
            else if (!arg.StartsWith('-') && zipFile == null) zipFile = arg;
        }

        if (string.IsNullOrEmpty(zipFile))
        {
            await stderr.WriteLineAsync("unzip: Usage: unzip [-l] [-o] [-d DIR] ARCHIVE");
            return 1;
        }

        string fullZipPath = PosixPathMapper.ToWindows(zipFile, context.CurrentDirectory);
        if (!File.Exists(fullZipPath) && !fullZipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            fullZipPath += ".zip";
        }

        if (!File.Exists(fullZipPath))
        {
            await stderr.WriteLineAsync($"unzip: cannot find or open {zipFile}");
            return 9;
        }

        string extractDir = string.IsNullOrEmpty(targetDir)
            ? context.CurrentDirectory
            : PosixPathMapper.ToWindows(targetDir, context.CurrentDirectory);

        try
        {
            using var archive = ZipFile.OpenRead(fullZipPath);

            if (list)
            {
                await stdout.WriteLineAsync("Archive:  " + Path.GetFileName(fullZipPath));
                await stdout.WriteLineAsync("  Length      Date    Time    Name");
                await stdout.WriteLineAsync("---------  ---------- -----   ----");

                long totalLength = 0;
                int count = 0;
                foreach (var entry in archive.Entries)
                {
                    totalLength += entry.Length;
                    count++;
                    string dateStr = entry.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                    await stdout.WriteLineAsync($"{entry.Length,9}  {dateStr}   {entry.FullName}");
                }
                await stdout.WriteLineAsync("---------                     -------");
                await stdout.WriteLineAsync($"{totalLength,9}                     {count} files");
                return 0;
            }

            if (!Directory.Exists(extractDir))
                Directory.CreateDirectory(extractDir);

            await stdout.WriteLineAsync($"Archive:  {Path.GetFileName(fullZipPath)}");
            foreach (var entry in archive.Entries)
            {
                if (ct.IsCancellationRequested) return 130;
                string destinationPath = Path.GetFullPath(Path.Combine(extractDir, entry.FullName));
                if (!destinationPath.StartsWith(extractDir, StringComparison.OrdinalIgnoreCase))
                {
                    // Zip slip protection
                    continue;
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destinationPath);
                }
                else
                {
                    var parent = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                        Directory.CreateDirectory(parent);

                    entry.ExtractToFile(destinationPath, overwrite);
                    await stdout.WriteLineAsync($"  inflating: {entry.FullName}");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"unzip error: {ex.Message}");
            return 1;
        }
    }
}
