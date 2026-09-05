using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Editors.Nano;
using LinuxTerm.Core.Editors.Vim;

namespace LinuxTerm.Core.Common;

/// <summary>
/// Maintains active session state for the Linux shell environment.
/// </summary>
public class ShellContext
{
    private string _currentDirectory;

    public string CurrentDirectory
    {
        get => _currentDirectory;
        set
        {
            if (Directory.Exists(value))
            {
                _previousDirectory = _currentDirectory;
                _currentDirectory = Path.GetFullPath(value);
                EnvironmentVariables["PWD"] = PosixCurrentDirectory;
                EnvironmentVariables["OLDPWD"] = PosixPathMapper.ToPosix(_previousDirectory);
            }
            else
            {
                throw new DirectoryNotFoundException($"Directory not found: {value}");
            }
        }
    }

    private string _previousDirectory;
    public string PreviousDirectory => _previousDirectory;

    public string PosixCurrentDirectory => PosixPathMapper.ToPosix(_currentDirectory);

    public Dictionary<string, string> EnvironmentVariables { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> History { get; } = new();

    public int LastExitCode { get; set; } = 0;

    public bool IsRunning { get; set; } = true;

    public bool IsAdmin { get; }

    public string UserName => EnvironmentVariables.GetValueOrDefault("USER", Environment.UserName.ToLowerInvariant());
    public string HostName => EnvironmentVariables.GetValueOrDefault("HOSTNAME", Environment.MachineName.ToLowerInvariant());

    /// <summary>
    /// Optional GUI callback for interactive Nano editing sessions.
    /// </summary>
    public Func<NanoSession, CancellationToken, Task<int>>? NanoGuiHandler { get; set; }

    /// <summary>
    /// Optional GUI callback for interactive Vim editing sessions.
    /// </summary>
    public Func<VimSession, CancellationToken, Task<int>>? VimGuiHandler { get; set; }

    public ShellContext(string? initialDirectory = null)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _currentDirectory = Directory.Exists(initialDirectory) 
            ? Path.GetFullPath(initialDirectory) 
            : Directory.Exists(userProfile) ? userProfile : Directory.GetCurrentDirectory();
        
        _previousDirectory = _currentDirectory;

        // Check Windows Administrator status
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                IsAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            else
            {
                IsAdmin = false;
            }
        }
        catch
        {
            IsAdmin = false;
        }

        // Initialize environment variables from process
        foreach (DictionaryEntry de in Environment.GetEnvironmentVariables())
        {
            if (de.Key is string key && de.Value is string val)
            {
                EnvironmentVariables[key] = val;
            }
        }

        // Standard POSIX variables
        EnvironmentVariables["USER"] = Environment.UserName.ToLowerInvariant();
        EnvironmentVariables["LOGNAME"] = EnvironmentVariables["USER"];
        EnvironmentVariables["HOME"] = PosixPathMapper.ToPosix(userProfile);
        EnvironmentVariables["PWD"] = PosixCurrentDirectory;
        EnvironmentVariables["OLDPWD"] = PosixCurrentDirectory;
        EnvironmentVariables["SHELL"] = "/bin/bash";
        EnvironmentVariables["TERM"] = "xterm-256color";
        EnvironmentVariables["HOSTNAME"] = Environment.MachineName.ToLowerInvariant();
        EnvironmentVariables["SHLVL"] = "1";

        // Sensible default aliases
        Aliases["ll"] = "ls -la";
        Aliases["la"] = "ls -A";
        Aliases["l"] = "ls -CF";
        Aliases["cls"] = "clear";
        Aliases[".."] = "cd ..";
        Aliases["..."] = "cd ../..";
    }

    /// <summary>
    /// Expands variable references in a string ($VAR or ${VAR} or $?).
    /// </summary>
    public string ExpandVariables(string input)
    {
        if (string.IsNullOrEmpty(input) || !input.Contains('$'))
            return input;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '$' && i + 1 < input.Length)
            {
                i++;
                if (input[i] == '?')
                {
                    sb.Append(LastExitCode);
                    continue;
                }

                if (input[i] == '{')
                {
                    int closeIdx = input.IndexOf('}', i + 1);
                    if (closeIdx != -1)
                    {
                        var varName = input.Substring(i + 1, closeIdx - (i + 1));
                        sb.Append(EnvironmentVariables.GetValueOrDefault(varName, string.Empty));
                        i = closeIdx;
                        continue;
                    }
                }

                int start = i;
                while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_'))
                {
                    i++;
                }

                if (i > start)
                {
                    var varName = input[start..i];
                    sb.Append(EnvironmentVariables.GetValueOrDefault(varName, string.Empty));
                    i--; // Step back for outer loop
                }
                else
                {
                    sb.Append('$');
                    i--;
                }
            }
            else
            {
                sb.Append(input[i]);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets the current Git branch name if inside a git repository, or null.
    /// </summary>
    public string? GetGitBranch()
    {
        try
        {
            var dir = new DirectoryInfo(_currentDirectory);
            while (dir != null)
            {
                var gitDir = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(gitDir))
                {
                    var headFile = Path.Combine(gitDir, "HEAD");
                    if (File.Exists(headFile))
                    {
                        var headContent = File.ReadAllText(headFile).Trim();
                        if (headContent.StartsWith("ref: refs/heads/"))
                        {
                            return headContent["ref: refs/heads/".Length..];
                        }
                        if (headContent.Length >= 7)
                        {
                            return headContent[..7]; // Detached HEAD SHA
                        }
                    }
                    return "git";
                }
                dir = dir.Parent;
            }
        }
        catch
        {
            // Ignore filesystem access errors
        }

        return null;
    }

    /// <summary>
    /// Generates standard colored prompt text: user@hostname:path (branch)$
    /// </summary>
    public string GetPrompt()
    {
        var user = UserName;
        var host = HostName;
        var posixPath = PosixCurrentDirectory;
        var gitBranch = GetGitBranch();
        var symbol = IsAdmin ? "#" : "$";

        var userColor = IsAdmin ? AnsiText.BrightRed : AnsiText.BrightGreen;
        var hostColor = AnsiText.BrightGreen;
        var pathColor = AnsiText.BrightBlue;
        var gitColor = AnsiText.BrightYellow;

        var prompt = $"{userColor}{user}{AnsiText.Reset}@{hostColor}{host}{AnsiText.Reset}:{pathColor}{posixPath}{AnsiText.Reset}";
        if (!string.IsNullOrEmpty(gitBranch))
        {
            prompt += $" {gitColor}({gitBranch}){AnsiText.Reset}";
        }

        prompt += $"{symbol} ";
        return prompt;
    }
}
