using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Commands;
using LinuxTerm.Core.Common;
using LinuxTerm.Core.Editors;
using LinuxTerm.Core.Editors.Nano;
using LinuxTerm.Core.Editors.Vim;
using Xunit;

namespace LinuxTerm.Tests;

public class EditorTests : IDisposable
{
    private readonly string _tempDir;

    public EditorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WinLinuxTerm_EditorTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
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

    [Fact]
    public void TextBuffer_BasicTypingAndNewlines()
    {
        var buffer = new TextBuffer();
        Assert.Equal(1, buffer.LineCount);

        buffer.InsertText("Hello");
        Assert.Equal("Hello", buffer.GetLineText(0));
        Assert.Equal(5, buffer.CursorCol);
        Assert.True(buffer.IsModified);

        buffer.InsertNewline();
        Assert.Equal(2, buffer.LineCount);
        Assert.Equal(1, buffer.CursorRow);
        Assert.Equal(0, buffer.CursorCol);

        buffer.InsertText("World");
        Assert.Equal("World", buffer.GetLineText(1));
    }

    [Fact]
    public void TextBuffer_BackspaceAndDelete_MergeLinesCorrectly()
    {
        var buffer = new TextBuffer();
        buffer.InsertText("First");
        buffer.InsertNewline();
        buffer.InsertText("Second");

        Assert.Equal(2, buffer.LineCount);
        Assert.Equal(1, buffer.CursorRow);
        Assert.Equal(6, buffer.CursorCol);

        // Move to start of second line and backspace to merge
        buffer.MoveLineStart();
        Assert.Equal(0, buffer.CursorCol);

        buffer.DeleteBackwards();
        Assert.Equal(1, buffer.LineCount);
        Assert.Equal("FirstSecond", buffer.GetLineText(0));
        Assert.Equal(5, buffer.CursorCol);

        // Move cursor to middle and delete forward
        buffer.CursorCol = 5;
        buffer.DeleteForwards(); // deletes 'S'
        Assert.Equal("Firstecond", buffer.GetLineText(0));
    }

    [Fact]
    public void TextBuffer_UndoAndRedo()
    {
        var buffer = new TextBuffer();
        buffer.InsertText("Alpha");
        Assert.Equal("Alpha", buffer.GetLineText(0));

        buffer.InsertText(" Beta");
        Assert.Equal("Alpha Beta", buffer.GetLineText(0));

        // Undo
        Assert.True(buffer.Undo());
        Assert.Equal("Alpha", buffer.GetLineText(0));

        // Redo
        Assert.True(buffer.Redo());
        Assert.Equal("Alpha Beta", buffer.GetLineText(0));
    }

    [Fact]
    public void TextBuffer_CutYankPaste()
    {
        var buffer = new TextBuffer();
        buffer.InsertText("Line 1");
        buffer.InsertNewline();
        buffer.InsertText("Line 2");
        buffer.InsertNewline();
        buffer.InsertText("Line 3");

        Assert.Equal(3, buffer.LineCount);

        // Cut line 1 ("Line 2")
        buffer.CursorRow = 1;
        buffer.DeleteLine(1);
        Assert.Equal(2, buffer.LineCount);
        Assert.Equal("Line 2", buffer.ClipboardLine);
        Assert.Equal("Line 1", buffer.GetLineText(0));
        Assert.Equal("Line 3", buffer.GetLineText(1));

        // Paste below
        buffer.CursorRow = 1;
        buffer.PasteLine(below: true);
        Assert.Equal(3, buffer.LineCount);
        Assert.Equal("Line 2", buffer.GetLineText(2));

        // Yank line 0
        buffer.YankLine(0);
        Assert.Equal("Line 1", buffer.ClipboardLine);

        // Delete to end of line
        buffer.CursorRow = 0;
        buffer.CursorCol = 4;
        buffer.DeleteToLineEnd();
        Assert.Equal("Line", buffer.GetLineText(0));
    }

    [Fact]
    public void TextBuffer_Search_FindsForwardAndBackward()
    {
        var buffer = new TextBuffer();
        buffer.InsertText("apple banana cherry");
        buffer.InsertNewline();
        buffer.InsertText("date banana elderberry");

        buffer.CursorRow = 0;
        buffer.CursorCol = 0;

        // Search forward
        bool found = buffer.Search("banana", forward: true);
        Assert.True(found);
        Assert.Equal(0, buffer.CursorRow);
        Assert.Equal(6, buffer.CursorCol);

        // Search next forward
        bool foundNext = buffer.Search("banana", forward: true);
        Assert.True(foundNext);
        Assert.Equal(1, buffer.CursorRow);
        Assert.Equal(5, buffer.CursorCol);

        // Search backward
        bool foundBack = buffer.Search("banana", forward: false);
        Assert.True(foundBack);
        Assert.Equal(0, buffer.CursorRow);
        Assert.Equal(6, buffer.CursorCol);

        // Search not found
        Assert.False(buffer.Search("pineapple"));
    }

    [Fact]
    public void NanoSession_InteractiveWorkflow_SaveAndExit()
    {
        var testFile = Path.Combine(_tempDir, "nano_test.txt");
        var session = new NanoSession(testFile)
        {
            WorkingDirectory = _tempDir
        };

        // Type "Hello Nano"
        foreach (char c in "Hello Nano")
        {
            session.ProcessKey(ConsoleKey.None, c, ConsoleModifiers.None);
        }
        Assert.Equal("Hello Nano", session.Buffer.GetLineText(0));
        Assert.True(session.Buffer.IsModified);

        // Save with ^O
        session.ProcessKey(ConsoleKey.O, '\0', ConsoleModifiers.Control);
        Assert.True(session.IsInPrompt);
        Assert.Contains("File Name to Write", session.PromptLabel);

        // Confirm filename with Enter
        session.ProcessKey(ConsoleKey.Enter, '\r', ConsoleModifiers.None);
        Assert.False(session.IsInPrompt);
        Assert.True(File.Exists(testFile));
        Assert.Equal("Hello Nano", File.ReadAllText(testFile).Trim());

        // Exit with ^X
        session.ProcessKey(ConsoleKey.X, '\0', ConsoleModifiers.Control);
        Assert.True(session.IsExited);
    }

    [Fact]
    public void VimSession_ModalEditingAndMotions()
    {
        var testFile = Path.Combine(_tempDir, "vim_test.txt");
        var session = new VimSession(testFile)
        {
            WorkingDirectory = _tempDir
        };

        Assert.Equal(VimMode.Normal, session.Mode);

        // Enter insert mode with 'i'
        session.ProcessKey(ConsoleKey.I, 'i', ConsoleModifiers.None);
        Assert.Equal(VimMode.Insert, session.Mode);

        // Type "First Line"
        foreach (char c in "First Line")
        {
            session.ProcessKey(ConsoleKey.None, c, ConsoleModifiers.None);
        }
        session.ProcessKey(ConsoleKey.Enter, '\r', ConsoleModifiers.None);
        foreach (char c in "Second Line")
        {
            session.ProcessKey(ConsoleKey.None, c, ConsoleModifiers.None);
        }

        Assert.Equal(2, session.Buffer.LineCount);
        Assert.Equal("First Line", session.Buffer.GetLineText(0));
        Assert.Equal("Second Line", session.Buffer.GetLineText(1));

        // Return to Normal mode with Escape
        session.ProcessKey(ConsoleKey.Escape, '\0', ConsoleModifiers.None);
        Assert.Equal(VimMode.Normal, session.Mode);

        // Motion 'k' moves up to line 0
        session.ProcessKey(ConsoleKey.K, 'k', ConsoleModifiers.None);
        Assert.Equal(0, session.Buffer.CursorRow);

        // Motion '$' moves to line end
        session.ProcessKey(ConsoleKey.D4, '$', ConsoleModifiers.None);
        Assert.Equal("First Line".Length, session.Buffer.CursorCol);

        // Motion '0' moves to line start
        session.ProcessKey(ConsoleKey.D0, '0', ConsoleModifiers.None);
        Assert.Equal(0, session.Buffer.CursorCol);

        // 'x' deletes character 'F'
        session.ProcessKey(ConsoleKey.X, 'x', ConsoleModifiers.None);
        Assert.Equal("irst Line", session.Buffer.GetLineText(0));

        // 'u' undoes delete
        session.ProcessKey(ConsoleKey.U, 'u', ConsoleModifiers.None);
        Assert.Equal("First Line", session.Buffer.GetLineText(0));

        // 'Ctrl+R' redoes delete
        session.ProcessKey(ConsoleKey.R, '\0', ConsoleModifiers.Control);
        Assert.Equal("irst Line", session.Buffer.GetLineText(0));

        // Save with :w
        session.ExecuteColonCommand("w");
        Assert.True(File.Exists(testFile));
        Assert.False(session.Buffer.IsModified);

        // Jump to line 2 with :2
        session.ExecuteColonCommand("2");
        Assert.Equal(1, session.Buffer.CursorRow);

        // Line number display toggle
        session.ExecuteColonCommand("set nu");
        Assert.True(session.ShowLineNumbers);
        session.ExecuteColonCommand("set nonu");
        Assert.False(session.ShowLineNumbers);

        // Quit with :q
        session.ExecuteColonCommand("q");
        Assert.True(session.IsExited);
    }

    [Fact]
    public void VimSession_ColonWq_SavesAndQuits()
    {
        var testFile = Path.Combine(_tempDir, "vim_wq_test.txt");
        var session = new VimSession(testFile)
        {
            WorkingDirectory = _tempDir
        };

        // Insert mode and add text
        session.ProcessKey(ConsoleKey.I, 'i', ConsoleModifiers.None);
        foreach (char c in "Saved Text")
        {
            session.ProcessKey(ConsoleKey.None, c, ConsoleModifiers.None);
        }
        session.ProcessKey(ConsoleKey.Escape, '\0', ConsoleModifiers.None);

        session.ExecuteColonCommand("wq");
        Assert.True(session.IsExited);
        Assert.True(File.Exists(testFile));
        Assert.Equal("Saved Text", File.ReadAllText(testFile).Trim());
    }

    [Fact]
    public void VimSession_QuitUnsaved_PreventsExitWithoutBang()
    {
        var session = new VimSession(null);
        session.Buffer.InsertText("Unsaved Changes");

        // Attempt :q should fail because buffer is modified
        session.ExecuteColonCommand("q");
        Assert.False(session.IsExited);
        Assert.Contains("E37", session.StatusMessage);

        // :q! overrides and exits
        session.ExecuteColonCommand("q!");
        Assert.True(session.IsExited);
    }

    [Fact]
    public void CommandRegistry_ContainsNanoAndVimCommands()
    {
        var registry = CommandRegistry.CreateDefault();
        Assert.True(registry.HasCommand("nano"));
        Assert.True(registry.HasCommand("vim"));
        Assert.True(registry.HasCommand("vi"));

        var nano = registry.GetCommand("nano");
        Assert.NotNull(nano);
        Assert.Equal("nano", nano.Name);

        var vim = registry.GetCommand("vim");
        Assert.NotNull(vim);
        Assert.Equal("vim", vim.Name);

        var vi = registry.GetCommand("vi");
        Assert.NotNull(vi);
        Assert.Equal("vi", vi.Name);
    }

    [Fact]
    public async Task EditorCommands_InvokeGuiHandler_WhenConfigured()
    {
        var context = new ShellContext(_tempDir);
        bool nanoInvoked = false;
        bool vimInvoked = false;

        context.NanoGuiHandler = (session, ct) =>
        {
            nanoInvoked = true;
            return Task.FromResult(0);
        };

        context.VimGuiHandler = (session, ct) =>
        {
            vimInvoked = true;
            return Task.FromResult(0);
        };

        var nano = new NanoCommand();
        int nanoCode = await nano.ExecuteAsync(new[] { "test.txt" }, context, TextReader.Null, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        Assert.Equal(0, nanoCode);
        Assert.True(nanoInvoked);

        var vim = new VimCommand();
        int vimCode = await vim.ExecuteAsync(new[] { "test.txt" }, context, TextReader.Null, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        Assert.Equal(0, vimCode);
        Assert.True(vimInvoked);
    }
}
