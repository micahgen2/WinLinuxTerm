using System;
using System.IO;
using System.Text.RegularExpressions;

namespace LinuxTerm.Core.Common;

/// <summary>
/// Handles seamless translation between POSIX-style paths (/c/Users/..., ~/..., /mnt/c/...) 
/// and native Windows paths (C:\Users\..., etc.).
/// </summary>
public static class PosixPathMapper
{
    private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly Regex DrivePathRegex = new(@"^/([a-zA-Z])(/.*)?$", RegexOptions.Compiled);
    private static readonly Regex MntDrivePathRegex = new(@"^/mnt/([a-zA-Z])(/.*)?$", RegexOptions.Compiled);
    private static readonly Regex WindowsDriveRegex = new(@"^([a-zA-Z]):[\\/](.*)?$", RegexOptions.Compiled);

    /// <summary>
    /// Converts a POSIX or Windows path into a canonical Windows filesystem path.
    /// </summary>
    public static string ToWindows(string path, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return workingDirectory;

        path = path.Trim();

        // 1. Home directory expansion
        if (path == "~")
            return UserProfile;

        if (path.StartsWith("~/") || path.StartsWith("~\\"))
            return Path.Combine(UserProfile, path[2..].Replace('/', Path.DirectorySeparatorChar));

        // 2. Already a full Windows path (e.g. C:\... or C:/...)
        if (WindowsDriveRegex.IsMatch(path))
            return Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));

        // 3. /mnt/c/... or /mnt/c
        var mntMatch = MntDrivePathRegex.Match(path);
        if (mntMatch.Success)
        {
            var drive = mntMatch.Groups[1].Value.ToUpperInvariant();
            var rest = mntMatch.Groups[2].Success ? mntMatch.Groups[2].Value.TrimStart('/') : string.Empty;
            var winPath = $"{drive}:\\{rest.Replace('/', Path.DirectorySeparatorChar)}";
            return Path.GetFullPath(winPath);
        }

        // 4. /c/... or /c (git-bash / cygwin style)
        var driveMatch = DrivePathRegex.Match(path);
        if (driveMatch.Success)
        {
            var drive = driveMatch.Groups[1].Value.ToUpperInvariant();
            var rest = driveMatch.Groups[2].Success ? driveMatch.Groups[2].Value.TrimStart('/') : string.Empty;
            var winPath = $"{drive}:\\{rest.Replace('/', Path.DirectorySeparatorChar)}";
            return Path.GetFullPath(winPath);
        }

        // 5. Root "/" -> Current working drive root (e.g. C:\)
        if (path == "/")
        {
            var driveRoot = Path.GetPathRoot(workingDirectory);
            return string.IsNullOrEmpty(driveRoot) ? "C:\\" : driveRoot;
        }

        // 6. Absolute posix path without drive: /Users/... -> C:\Users\...
        if (path.StartsWith('/'))
        {
            var driveRoot = Path.GetPathRoot(workingDirectory) ?? "C:\\";
            var relative = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(driveRoot, relative));
        }

        // 7. Relative path: resolve against working directory
        var combined = Path.Combine(workingDirectory, path.Replace('/', Path.DirectorySeparatorChar));
        return Path.GetFullPath(combined);
    }

    /// <summary>
    /// Converts a Windows filesystem path into a friendly POSIX-style path (e.g. ~/projects or /c/Users/...).
    /// </summary>
    public static string ToPosix(string windowsPath, bool useTildeForHome = true)
    {
        if (string.IsNullOrWhiteSpace(windowsPath))
            return "/";

        windowsPath = Path.GetFullPath(windowsPath);

        if (useTildeForHome && windowsPath.Equals(UserProfile, StringComparison.OrdinalIgnoreCase))
            return "~";

        if (useTildeForHome && windowsPath.StartsWith(UserProfile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            var sub = windowsPath[UserProfile.Length..].Replace('\\', '/');
            return $"~{sub}";
        }

        var match = WindowsDriveRegex.Match(windowsPath);
        if (match.Success)
        {
            var drive = match.Groups[1].Value.ToLowerInvariant();
            var rest = match.Groups[2].Value.Replace('\\', '/');
            return string.IsNullOrEmpty(rest) ? $"/{drive}" : $"/{drive}/{rest}";
        }

        return windowsPath.Replace('\\', '/');
    }
}
