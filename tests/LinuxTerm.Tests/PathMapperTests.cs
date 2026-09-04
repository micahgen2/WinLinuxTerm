using System;
using System.IO;
using LinuxTerm.Core.Common;
using Xunit;

namespace LinuxTerm.Tests;

public class PathMapperTests
{
    [Fact]
    public void ToWindows_Tilde_ExpandsToUserProfile()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = PosixPathMapper.ToWindows("~", "C:\\");
        Assert.Equal(userProfile, result);
    }

    [Fact]
    public void ToWindows_TildeSubdirectory_ExpandsCorrectly()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expected = Path.Combine(userProfile, "documents", "test.txt");
        var result = PosixPathMapper.ToWindows("~/documents/test.txt", "C:\\");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToWindows_MntDrivePath_ExpandsToDriveLetter()
    {
        var result = PosixPathMapper.ToWindows("/mnt/c/Windows/System32", "C:\\");
        Assert.Equal("C:\\Windows\\System32", result);
    }

    [Fact]
    public void ToWindows_CygwinDrivePath_ExpandsToDriveLetter()
    {
        var result = PosixPathMapper.ToWindows("/c/Users/Test", "C:\\");
        Assert.Equal("C:\\Users\\Test", result);
    }

    [Fact]
    public void ToWindows_RelativePath_ResolvesAgainstWorkingDirectory()
    {
        var workingDir = "C:\\projects\\app";
        var result = PosixPathMapper.ToWindows("src/file.cs", workingDir);
        Assert.Equal("C:\\projects\\app\\src\\file.cs", result);
    }

    [Fact]
    public void ToWindows_ParentDirectory_ResolvesCorrectly()
    {
        var workingDir = "C:\\projects\\app";
        var result = PosixPathMapper.ToWindows("../other", workingDir);
        Assert.Equal("C:\\projects\\other", result);
    }

    [Fact]
    public void ToPosix_DrivePath_ConvertsToPosixFormat()
    {
        var result = PosixPathMapper.ToPosix("C:\\tools\\bin", useTildeForHome: false);
        Assert.Equal("/c/tools/bin", result);
    }

    [Fact]
    public void ToPosix_HomePath_ConvertsToTilde()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = PosixPathMapper.ToPosix(userProfile, useTildeForHome: true);
        Assert.Equal("~", result);

        var sub = Path.Combine(userProfile, "projects");
        var subResult = PosixPathMapper.ToPosix(sub, useTildeForHome: true);
        Assert.Equal("~/projects", subResult);
    }
}
