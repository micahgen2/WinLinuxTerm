using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LinuxTerm.Core.Editors;

/// <summary>
/// Core text buffer model supporting line manipulation, cursor navigation, undo history, and file I/O.
/// </summary>
public class TextBuffer
{
    public List<StringBuilder> Lines { get; } = new();
    public int CursorRow { get; set; }
    public int CursorCol { get; set; }
    public string? FilePath { get; set; }
    public bool IsModified { get; set; }
    public string? ClipboardLine { get; set; }

    private readonly Stack<List<string>> _undoStack = new();
    private readonly Stack<List<string>> _redoStack = new();

    public int LineCount => Lines.Count;

    public TextBuffer(string? filePath = null)
    {
        FilePath = filePath;
        Lines.Add(new StringBuilder());
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            LoadFromFile(filePath);
        }
    }

    public void LoadFromFile(string path)
    {
        FilePath = path;
        Lines.Clear();
        var contentLines = File.ReadAllLines(path);
        if (contentLines.Length == 0)
        {
            Lines.Add(new StringBuilder());
        }
        else
        {
            foreach (var line in contentLines)
            {
                Lines.Add(new StringBuilder(line));
            }
        }
        CursorRow = 0;
        CursorCol = 0;
        IsModified = false;
        _undoStack.Clear();
        _redoStack.Clear();
    }

    public void SaveToFile(string? path = null)
    {
        var target = path ?? FilePath;
        if (string.IsNullOrEmpty(target))
            throw new InvalidOperationException("No file name specified");

        var dir = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllLines(target, Lines.Select(l => l.ToString()));
        FilePath = target;
        IsModified = false;
    }

    public void PushUndo()
    {
        _redoStack.Clear();
        if (_undoStack.Count > 50)
        {
            // Limit stack depth
            var list = _undoStack.ToList();
            list.RemoveAt(list.Count - 1);
            _undoStack.Clear();
            for (int i = list.Count - 1; i >= 0; i--) _undoStack.Push(list[i]);
        }
        _undoStack.Push(Lines.Select(l => l.ToString()).ToList());
    }

    public bool Undo()
    {
        if (_undoStack.Count == 0) return false;

        _redoStack.Push(Lines.Select(l => l.ToString()).ToList());
        var snapshot = _undoStack.Pop();
        Lines.Clear();
        foreach (var l in snapshot) Lines.Add(new StringBuilder(l));
        ClampCursor();
        IsModified = true;
        return true;
    }

    public bool Redo()
    {
        if (_redoStack.Count == 0) return false;

        _undoStack.Push(Lines.Select(l => l.ToString()).ToList());
        var snapshot = _redoStack.Pop();
        Lines.Clear();
        foreach (var l in snapshot) Lines.Add(new StringBuilder(l));
        ClampCursor();
        IsModified = true;
        return true;
    }

    public string GetLineText(int row)
    {
        if (row >= 0 && row < Lines.Count)
            return Lines[row].ToString();
        return string.Empty;
    }

    public void InsertChar(char c)
    {
        PushUndo();
        var line = Lines[CursorRow];
        CursorCol = Math.Clamp(CursorCol, 0, line.Length);
        line.Insert(CursorCol, c);
        CursorCol++;
        IsModified = true;
    }

    public void InsertText(string text)
    {
        PushUndo();
        var line = Lines[CursorRow];
        CursorCol = Math.Clamp(CursorCol, 0, line.Length);
        line.Insert(CursorCol, text);
        CursorCol += text.Length;
        IsModified = true;
    }

    public void InsertNewline()
    {
        PushUndo();
        var currentLine = Lines[CursorRow];
        CursorCol = Math.Clamp(CursorCol, 0, currentLine.Length);

        var remainder = currentLine.ToString()[CursorCol..];
        currentLine.Remove(CursorCol, currentLine.Length - CursorCol);

        CursorRow++;
        Lines.Insert(CursorRow, new StringBuilder(remainder));
        CursorCol = 0;
        IsModified = true;
    }

    public void DeleteBackwards()
    {
        var line = Lines[CursorRow];
        if (CursorCol > 0)
        {
            PushUndo();
            CursorCol = Math.Clamp(CursorCol, 0, line.Length);
            line.Remove(CursorCol - 1, 1);
            CursorCol--;
            IsModified = true;
        }
        else if (CursorRow > 0)
        {
            PushUndo();
            var prevLine = Lines[CursorRow - 1];
            int prevLen = prevLine.Length;
            prevLine.Append(line.ToString());
            Lines.RemoveAt(CursorRow);
            CursorRow--;
            CursorCol = prevLen;
            IsModified = true;
        }
    }

    public void DeleteForwards()
    {
        var line = Lines[CursorRow];
        if (CursorCol < line.Length)
        {
            PushUndo();
            line.Remove(CursorCol, 1);
            IsModified = true;
        }
        else if (CursorRow < Lines.Count - 1)
        {
            PushUndo();
            var nextLine = Lines[CursorRow + 1];
            line.Append(nextLine.ToString());
            Lines.RemoveAt(CursorRow + 1);
            IsModified = true;
        }
    }

    public void DeleteLine(int row)
    {
        if (row < 0 || row >= Lines.Count) return;

        PushUndo();
        ClipboardLine = Lines[row].ToString();
        Lines.RemoveAt(row);
        if (Lines.Count == 0) Lines.Add(new StringBuilder());
        ClampCursor();
        IsModified = true;
    }

    public void YankLine(int row)
    {
        if (row >= 0 && row < Lines.Count)
            ClipboardLine = Lines[row].ToString();
    }

    public void PasteLine(bool below)
    {
        if (ClipboardLine == null) return;

        PushUndo();
        int targetRow = below ? CursorRow + 1 : CursorRow;
        targetRow = Math.Clamp(targetRow, 0, Lines.Count);
        Lines.Insert(targetRow, new StringBuilder(ClipboardLine));
        CursorRow = targetRow;
        CursorCol = 0;
        IsModified = true;
    }

    public void MoveLeft()
    {
        if (CursorCol > 0) CursorCol--;
        else if (CursorRow > 0)
        {
            CursorRow--;
            CursorCol = Lines[CursorRow].Length;
        }
    }

    public void MoveRight()
    {
        var line = Lines[CursorRow];
        if (CursorCol < line.Length) CursorCol++;
        else if (CursorRow < Lines.Count - 1)
        {
            CursorRow++;
            CursorCol = 0;
        }
    }

    public void MoveUp()
    {
        if (CursorRow > 0)
        {
            CursorRow--;
            CursorCol = Math.Min(CursorCol, Lines[CursorRow].Length);
        }
    }

    public void MoveDown()
    {
        if (CursorRow < Lines.Count - 1)
        {
            CursorRow++;
            CursorCol = Math.Min(CursorCol, Lines[CursorRow].Length);
        }
    }

    public void MoveLineStart() => CursorCol = 0;
    public void MoveLineEnd() => CursorCol = Lines[CursorRow].Length;
    public void MoveFirstNonWhitespace()
    {
        var line = Lines[CursorRow].ToString();
        int idx = 0;
        while (idx < line.Length && char.IsWhiteSpace(line[idx])) idx++;
        CursorCol = Math.Min(idx, line.Length);
    }
    public void MoveFileStart() { CursorRow = 0; CursorCol = 0; }
    public void MoveFileEnd() { CursorRow = Math.Max(0, Lines.Count - 1); CursorCol = Lines[CursorRow].Length; }

    public void DeleteToLineEnd()
    {
        PushUndo();
        var line = Lines[CursorRow];
        CursorCol = Math.Clamp(CursorCol, 0, line.Length);
        if (CursorCol < line.Length)
        {
            ClipboardLine = line.ToString()[CursorCol..];
            line.Remove(CursorCol, line.Length - CursorCol);
            IsModified = true;
        }
    }

    public void MoveWordForward()
    {
        var line = Lines[CursorRow].ToString();
        int idx = CursorCol;
        while (idx < line.Length && !char.IsWhiteSpace(line[idx])) idx++;
        while (idx < line.Length && char.IsWhiteSpace(line[idx])) idx++;
        if (idx < line.Length)
        {
            CursorCol = idx;
        }
        else if (CursorRow < Lines.Count - 1)
        {
            CursorRow++;
            CursorCol = 0;
        }
    }

    public void MoveWordBackward()
    {
        if (CursorCol > 0)
        {
            var line = Lines[CursorRow].ToString();
            int idx = CursorCol - 1;
            while (idx > 0 && char.IsWhiteSpace(line[idx])) idx--;
            while (idx > 0 && !char.IsWhiteSpace(line[idx - 1])) idx--;
            CursorCol = Math.Max(0, idx);
        }
        else if (CursorRow > 0)
        {
            CursorRow--;
            CursorCol = Lines[CursorRow].Length;
        }
    }

    public bool Search(string query, bool forward = true)
    {
        if (string.IsNullOrEmpty(query)) return false;

        int startRow = CursorRow;
        int step = forward ? 1 : -1;
        int r = startRow;

        for (int count = 0; count < Lines.Count; count++)
        {
            var line = Lines[r].ToString();
            int startCol = (r == startRow) ? (forward ? CursorCol + 1 : CursorCol - 1) : (forward ? 0 : line.Length);

            if (forward)
            {
                int idx = startCol < line.Length ? line.IndexOf(query, startCol, StringComparison.OrdinalIgnoreCase) : -1;
                if (idx != -1)
                {
                    CursorRow = r;
                    CursorCol = idx;
                    return true;
                }
            }
            else
            {
                int idx = startCol >= 0 ? line.LastIndexOf(query, Math.Min(startCol, line.Length - 1), StringComparison.OrdinalIgnoreCase) : -1;
                if (idx != -1)
                {
                    CursorRow = r;
                    CursorCol = idx;
                    return true;
                }
            }

            r = (r + step + Lines.Count) % Lines.Count;
        }

        return false;
    }

    public void ClampCursor()
    {
        CursorRow = Math.Clamp(CursorRow, 0, Math.Max(0, Lines.Count - 1));
        CursorCol = Math.Clamp(CursorCol, 0, Lines[CursorRow].Length);
    }
}
