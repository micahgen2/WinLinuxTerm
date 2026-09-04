using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Common;

namespace LinuxTerm.Core.Commands;

public class CurlCommand : ICommand
{
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true
    });

    public string Name => "curl";
    public string Description => "Transfer data from or to a server";
    public string Synopsis => "curl [-I] [-o FILE] [-s] [-X METHOD] [-d DATA] URL";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool headersOnly = false;
        bool silent = false;
        string? outputFile = null;
        string method = "GET";
        string? data = null;
        string? url = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-I" or "--head") headersOnly = true;
            else if (arg is "-s" or "--silent") silent = true;
            else if (arg is "-o" or "--output" && i + 1 < args.Length) outputFile = args[++i];
            else if (arg is "-X" or "--request" && i + 1 < args.Length) method = args[++i].ToUpperInvariant();
            else if (arg is "-d" or "--data" && i + 1 < args.Length)
            {
                data = args[++i];
                if (method == "GET") method = "POST";
            }
            else if (!arg.StartsWith('-')) url = arg;
        }

        if (string.IsNullOrEmpty(url))
        {
            await stderr.WriteLineAsync("curl: try 'curl --help' for more information");
            return 2;
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        try
        {
            using var req = new HttpRequestMessage(new HttpMethod(method), url);
            if (data != null)
            {
                req.Content = new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded");
            }

            var resp = await HttpClient.SendAsync(req, headersOnly ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, ct);

            if (headersOnly)
            {
                stdout.WriteLine($"HTTP/{resp.Version} {(int)resp.StatusCode} {resp.ReasonPhrase}");
                foreach (var h in resp.Headers)
                {
                    stdout.WriteLine($"{h.Key}: {string.Join(", ", h.Value)}");
                }
                foreach (var h in resp.Content.Headers)
                {
                    stdout.WriteLine($"{h.Key}: {string.Join(", ", h.Value)}");
                }
                return 0;
            }

            var contentBytes = await resp.Content.ReadAsByteArrayAsync(ct);

            if (!string.IsNullOrEmpty(outputFile))
            {
                var winPath = PosixPathMapper.ToWindows(outputFile, context.CurrentDirectory);
                await File.WriteAllBytesAsync(winPath, contentBytes, ct);
                if (!silent)
                {
                    stdout.WriteLine($"Saved {contentBytes.Length} bytes to {outputFile}");
                }
            }
            else
            {
                var text = Encoding.UTF8.GetString(contentBytes);
                stdout.Write(text);
                if (!text.EndsWith('\n')) stdout.WriteLine();
            }

            return resp.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"curl: (6) Could not resolve or connect to '{url}': {ex.Message}");
            return 6;
        }
    }
}

public class WgetCommand : ICommand
{
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true
    });

    public string Name => "wget";
    public string Description => "The non-interactive network downloader";
    public string Synopsis => "wget [-O FILE] [-q] URL";

    public async Task<int> ExecuteAsync(string[] args, ShellContext context, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        bool quiet = false;
        string? outputFile = null;
        string? url = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-q" or "--quiet") quiet = true;
            else if (arg is "-O" or "--output-document" && i + 1 < args.Length) outputFile = args[++i];
            else if (!arg.StartsWith('-')) url = arg;
        }

        if (string.IsNullOrEmpty(url))
        {
            await stderr.WriteLineAsync("wget: missing URL");
            return 1;
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        if (string.IsNullOrEmpty(outputFile))
        {
            try
            {
                var uri = new Uri(url);
                outputFile = Path.GetFileName(uri.LocalPath);
                if (string.IsNullOrEmpty(outputFile)) outputFile = "index.html";
            }
            catch
            {
                outputFile = "download";
            }
        }

        try
        {
            if (!quiet) stdout.WriteLine($"--  Connecting to {url}...");
            var resp = await HttpClient.GetAsync(url, ct);
            if (!quiet) stdout.WriteLine($"HTTP request sent, awaiting response... {(int)resp.StatusCode} {resp.ReasonPhrase}");

            resp.EnsureSuccessStatusCode();

            var data = await resp.Content.ReadAsByteArrayAsync(ct);
            var winPath = PosixPathMapper.ToWindows(outputFile, context.CurrentDirectory);
            await File.WriteAllBytesAsync(winPath, data, ct);

            if (!quiet)
            {
                stdout.WriteLine($"'{outputFile}' saved [{data.Length}/{data.Length}]");
            }
            return 0;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"wget: error: {ex.Message}");
            return 1;
        }
    }
}
