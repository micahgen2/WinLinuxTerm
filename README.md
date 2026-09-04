# WinLinuxTerm

> A modern Windows Terminal Emulator written in C# (.NET 10) featuring a built-in POSIX/Linux command emulation engine.

![Terminal Emulator](https://img.shields.io/badge/Platform-Windows-blue)
![C#](https://img.shields.io/badge/Language-C%23%2014%20%2F%20.NET%2010-purple)
![Tests](https://img.shields.io/badge/Tests-27%20Passed-brightgreen)
![License](https://img.shields.io/badge/License-MIT-green.svg)

**WinLinuxTerm** bridges the gap between Windows and Linux environments without requiring WSL or heavy container runtimes. It includes both a sleek, multi-tab WPF desktop GUI and a fast, lightweight interactive CLI runner.

---

## Key Features

- **POSIX/Linux Shell Emulation Engine (`LinuxTerm.Core`)**:
  - **Transparent Path Translation**: Converts `/`, `~`, `/c/...`, `/mnt/c/...`, and relative paths (`..`, `.`) to native Windows paths and back.
  - **Pipes & Streams**: Supports multi-stage Unix pipelines: `cat file.txt | grep "pattern" | wc -l`.
  - **File Redirection**: Output redirection (`>`, `>>`), input redirection (`<`), and error redirection (`2>&1`).
  - **Logical Operators & Chaining**: Execute commands with `&&` (AND), `||` (OR), and `;` (sequence).
  - **Variable Expansion**: Expands `$VAR`, `${VAR}`, `$?` (exit code), `$USER`, `$HOME`, `$PWD`, etc.
  - **Aliases**: Custom alias support (`alias ll='ls -la'`, `alias cls='clear'`).
  - **Command History**: History navigation with Up/Down arrows and the `history` command.
  - **Fallback Execution**: Automatically resolves and launches external Windows executables (`git`, `python`, `node`, `dotnet`, `cmd`, `ping`, etc.) with asynchronous standard I/O streaming.

- **Desktop GUI Terminal Emulator (`LinuxTerm.Gui`)**:
  - **Modern Tabbed Interface**: Open, close, and switch between concurrent terminal sessions (Ctrl+T, Ctrl+W, Ctrl+Tab).
  - **Full ANSI Color Engine**: Parses 16-color, 256-color, and RGB ANSI escape sequences with bold and underline styling.
  - **Customizable Color Themes**:
    - Ubuntu Aubergine
    - VS Code Dark+
    - Monokai Pro
    - Matrix Hacker Green
    - Cyberpunk Neon
  - **Dynamic Linux Prompt**: Displays `user@hostname:path$` with Git branch status indicator and Administrator `#` symbol.
  - **Interactive Auto-Completion**: Tab auto-completes built-in commands, aliases, and filesystem paths with interactive chips.
  - **Font Zooming**: Adjust terminal font sizes with Ctrl++ / Ctrl+- or toolbar buttons.
  - **Terminal Status Bar**: Displays current working directory, Git branch, last exit code, active shell, and UTF-8 encoding.

- **Lightweight Interactive CLI (`LinuxTerm.Cli`)**:
  - Direct console runner with command-line `-c "..."` non-interactive support or rich interactive REPL.

---

## Supported Linux Commands (Pure C#)

| Category | Commands | Description & Options |
|---|---|---|
| **File & Directory** | `ls` | List directory contents (`-l`, `-a`, `-A`, `-h`, `-t`, `-S`, `-r`, `-1`, colored file types) |
| | `cd` | Change directory (supports `~`, `-` for previous dir, `/c/...`, relative paths) |
| | `pwd` | Print working directory (`-P` for physical Windows path) |
| | `mkdir` | Create directories (`-p` create parents) |
| | `rm` | Remove files or directories (`-r`/`-rf` recursive, `-f` force) |
| | `cp` | Copy files and directories (`-r` recursive) |
| | `mv` | Move or rename files and directories |
| | `touch` | Create files or update timestamps |
| | `find` | Search files in hierarchy (`-name`, `-type f\|d`, `-maxdepth`) |
| | `tree` | ASCII directory hierarchy tree (`-L depth`, `-a`) |
| **Text Processing** | `cat` | Concatenate and print files (`-n` line numbers, `-b` non-blank) |
| | `grep` | Pattern matching with ANSI colored highlights (`-i`, `-v`, `-n`, `-r`, `-c`, `-E`) |
| | `head` | Output first N lines of files or stdin (`-n count`) |
| | `tail` | Output last N lines of files or stdin (`-n count`) |
| | `wc` | Word, line, character, and byte count (`-l`, `-w`, `-c`, `-m`) |
| | `echo` | Write arguments to output (`-n` no newline, `-e` interpret `\n`, `\t`, `\e`) |
| | `base64` | Base64 encode or decode (`-d`) files or stdin |
| **System & Status** | `uname` | Print system info (`-a`, `-s`, `-n`, `-r`, `-v`, `-m`, `-o`) |
| | `whoami` | Print current effective username |
| | `hostname` | Print computer hostname |
| | `date` | Formatted POSIX date/time (`+FORMAT`) |
| | `uptime` | System uptime and load averages |
| | `df` | Disk space usage across all drives (`-h` human readable) |
| | `free` | Memory utilization (`-h`, `-m`, `-g`) |
| | `ps` | Snapshot of running processes (`-u` detailed) |
| | `kill` | Terminate processes by PID (`-9`) |
| | `which` | Locate built-ins and binaries on PATH |
| | `clear` | Clear terminal screen |
| **Network & Web** | `curl` | HTTP client (`-I` headers, `-o file`, `-X method`, `-d data`) |
| | `wget` | Downloader (`-O file`, `-q` quiet) |
| **Shell Builtins** | `export` | Set or list environment variables |
| | `env` | Print all environment variables |
| | `alias` | Define or display command aliases |
| | `unalias` | Remove command aliases (`-a` remove all) |
| | `history` | List command history (`-c` clear) |
| | `help` / `man`| Interactive manual and synopsis for commands |
| | `exit` | Exit the shell session |

---

## Project Structure

```
WinLinuxTerm/
├── WinLinuxTerm.slnx
├── src/
│   ├── LinuxTerm.Core/        # Core engine: path mapper, parser, AST, builtins, process runner
│   │   ├── Common/            # PosixPathMapper, ShellContext, AnsiText
│   │   ├── Parser/            # Tokenizer, CommandAst
│   │   ├── Commands/          # FileCommands, TextCommands, SystemCommands, ShellBuiltins, NetCommands
│   │   └── Execution/         # ShellEngine, ProcessRunner
│   ├── LinuxTerm.Gui/         # WPF multi-tab terminal emulator application
│   │   ├── Controls/          # TerminalControl, suggestions, buffer
│   │   ├── Themes/            # TerminalTheme definitions
│   │   └── MainWindow.xaml    # Main window chrome, tab bar, zoom, theme selector
│   └── LinuxTerm.Cli/         # Console REPL and non-interactive runner
└── tests/
    └── LinuxTerm.Tests/       # xUnit test suite (27 unit tests)
```

---

## Getting Started

### Prerequisites
- Windows 10/11
- .NET 10 SDK

### Building the Solution
```powershell
dotnet build WinLinuxTerm.slnx
```

### Running the GUI Terminal Emulator
```powershell
dotnet run --project src/LinuxTerm.Gui/LinuxTerm.Gui.csproj
```

### Running the CLI Shell
```powershell
# Interactive REPL:
dotnet run --project src/LinuxTerm.Cli/LinuxTerm.Cli.csproj

# Non-interactive command string:
dotnet run --project src/LinuxTerm.Cli/LinuxTerm.Cli.csproj -- -c "uname -a && free -h && df -h"
```

### Running Tests
```powershell
dotnet test tests/LinuxTerm.Tests/LinuxTerm.Tests.csproj
```

---

## License

This project is licensed under the [MIT License](LICENSE).

