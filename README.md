# WinLinuxTerm

> A modern Windows Terminal Emulator written in C# (.NET 10) featuring a built-in POSIX/Linux command emulation engine.

![Terminal Emulator](https://img.shields.io/badge/Platform-Windows-blue)
![C#](https://img.shields.io/badge/Language-C%23%2014%20%2F%20.NET%2010-purple)
![Tests](https://img.shields.io/badge/Tests-82%20Passed-brightgreen)
![License](https://img.shields.io/badge/License-MIT-green.svg)

**WinLinuxTerm** bridges the gap between Windows and Linux environments without requiring WSL or heavy container runtimes. It includes both a sleek, multi-tab WPF desktop GUI and a fast, lightweight interactive CLI runner.

---

## Key Features

- **Built-in Linux Visual Text Editors (`nano` & `vim` / `vi`)**:
  - Pure C# full-screen interactive editors running seamlessly in both GUI and CLI.
  - **GNU Nano**: Modeless editing, status bar, prompt dialogues, and shortcuts (`^O` WriteOut, `^X` Exit, `^K` Cut, `^U` Uncut/Paste, `^W` Where Is, `^C` Cursor location, `^G` Help).
  - **Vim / Vi**: Modal state machine (`Normal`, `Insert`, `CommandLine`, `Search`):
    - Motions: `h`, `j`, `k`, `l`, `w`, `b`, `0`, `$`, `^`, `gg`, `G`
    - Quick Edits: `x`, `D`, `dd`, `yy`, `p`, `P`, `u` (undo), `Ctrl+R` (redo)
    - Ex / Colon Commands: `:w`, `:w <file>`, `:q`, `:q!`, `:wq`, `:x`, `:set nu`, `:set nonu`, `:<number>` line jump
    - In-buffer search: `/pattern` and `n` / `N` navigation
  - Themed GUI overlay with line numbers, custom block cursor caret, and smooth keyboard capture.

- **POSIX/Linux Shell Emulation Engine (`LinuxTerm.Core`)**:
  - **Command Substitution**: Evaluates nested commands via `$(command)` and `` `command` `` (e.g. `echo "Kernel: $(uname -r)"`).
  - **Transparent Path Translation**: Converts `/`, `~`, `/c/...`, `/mnt/c/...`, and relative paths (`..`, `.`) to native Windows paths and back.
  - **Pipes & Streams**: Supports multi-stage Unix pipelines: `cat file.txt | grep "pattern" | wc -l`.
  - **File Redirection**: Output redirection (`>`, `>>`), input redirection (`<`), and error redirection (`2>&1`).
  - **Logical Operators & Chaining**: Execute commands with `&&` (AND), `||` (OR), and `;` (sequence).
  - **History Expansion**: Fast recall with `!!` (repeat last command) and `!n` (repeat command by index).
  - **Variable Expansion**: Expands `$VAR`, `${VAR}`, `$?` (exit code), `$USER`, `$HOME`, `$PWD`, etc.
  - **Aliases**: Custom alias support (`alias ll='ls -la'`, `alias cls='clear'`).
  - **Command History**: History navigation with Up/Down arrows and the `history` command.
  - **Fallback Execution**: Automatically resolves and launches external Windows executables (`git`, `python`, `node`, `dotnet`, `cmd`, `ping`, etc.) with asynchronous standard I/O streaming.

- **Desktop GUI Terminal Emulator (`LinuxTerm.Gui`)**:
  - **Multi-Tab & Split Panes**:
    - Open, close, and switch concurrent tabs (Ctrl+T, Ctrl+W, Ctrl+Tab).
    - **Split Vertically** (Ctrl+Shift+E) for side-by-side terminal sessions.
    - **Split Horizontally** (Ctrl+Shift+O) for stacked terminal sessions.
  - **In-Buffer Search Bar (Ctrl+F)**:
    - Interactive search overlay with match count badge, Next/Previous navigation, and match highlighting.
  - **Right-Click Context Menu**: Copy, Paste, Clear Screen, Split Panes, and Open in File Explorer.
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
| **Archive & Compression** | `tar` | Tape archive tool (`-c` create, `-x` extract, `-t` list, `-z` gzip, `-v` verbose, `-f`, `-C`) |
| | `gzip` | Compress files using DEFLATE/gzip format (`-d` decompress, `-k` keep, `-f` force, `-c` stdout) |
| | `gunzip` | Decompress `.gz` files (`-k` keep original, `-f` force, `-c` stdout) |
| | `zip` | Package and compress files into a ZIP archive (`-r` recursive, `-q` quiet) |
| | `unzip` | List, test and extract files from a ZIP archive (`-l` list, `-o` overwrite, `-d` target dir) |
| **Path & File Metadata** | `file` | Determine file type via magic byte inspection and text analysis (`-b` brief, `-i` mime) |
| | `stat` | Display file or filesystem status (`-c` format, `-t` terse, permissions, timestamps, size, blocks) |
| | `basename` | Strip directory and suffix from filenames (`-a` multiple, `-s` suffix) |
| | `dirname` | Strip non-directory trailing component from filenames (`-z` zero-terminated) |
| | `realpath` | Print resolved canonical POSIX paths (`-q` quiet, `-m`, `-e`, `--relative-to`) |
| | `readlink` | Print resolved symbolic link targets or canonical filenames (`-f`, `-e`, `-m`, `-n`, `-q`) |
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
| **Text Editors** | `nano` | GNU nano modeless interactive editor (`^O` save, `^X` exit, `^K` cut, `^U` paste, `^W` find) |
| | `vim` / `vi` | Vi IMproved modal editor (`Normal`, `Insert`, `CommandLine`, `Search`, motions, `:w`, `:q`, `:wq`) |
| **Text & Stream Processing** | `tac` | Concatenate and print files in reverse line order (`-s` custom separator) |
| | `rev` | Reverse lines characterwise |
| | `nl` | Number lines of files (`-b` body style, `-s` separator, `-w` width, `-v` start) |
| | `fold` | Wrap each input line to fit specified column width (`-w` width, `-s` break spaces) |
| | `paste` | Merge lines of files side-by-side (`-d` delimiters, `-s` serial) |
| | `comm` | Compare two sorted files line by line (`-1`, `-2`, `-3` column suppression) |
| | `cmp` | Compare two files byte by byte (`-l` verbose differing bytes, `-s` silent) |
| | `expand` | Convert tabs to spaces (`-t` tab size, `-i` initial only) |
| | `unexpand` | Convert spaces to tabs (`-t` tab size, `-a` all spaces) |
| | `sed` | Stream editor substitution (`s/find/replace/g`, `s/find/replace/i`) |
| | `awk` | Pattern scanning and column printing (`{print $1, $2}`, `-F delim`, `$NF`) |
| | `cut` | Remove/extract fields from lines (`-d delim`, `-f fields`) |
| | `sort` | Sort text lines (`-r` reverse, `-n` numeric, `-u` unique, `-k key`) |
| | `uniq` | Report or omit duplicate lines (`-c` count, `-d` duplicates only, `-i` ignore case) |
| | `tr` | Translate or delete characters (`tr 'a-z' 'A-Z'`, `tr -d 'chars'`) |
| | `tee` | Write stdin to stdout and files simultaneously (`-a` append) |
| | `xargs` | Build and execute command lines from stdin items |
| | `diff` | Line-by-line file comparison with colored additions and deletions |
| | `cat` | Concatenate and print files (`-n` line numbers, `-b` non-blank) |
| | `grep` | Pattern matching with ANSI colored highlights (`-i`, `-v`, `-n`, `-r`, `-c`, `-E`) |
| | `head` | Output first N lines of files or stdin (`-n count`) |
| | `tail` | Output last N lines of files or stdin (`-n count`) |
| | `wc` | Word, line, character, and byte count (`-l`, `-w`, `-c`, `-m`) |
| | `echo` | Write arguments to output (`-n` no newline, `-e` interpret `\n`, `\t`, `\e`) |
| | `base64` | Base64 encode or decode (`-d`) files or stdin |
| **Checksum & Security** | `sha256sum` | Compute and check SHA-256 cryptographic message digests |
| | `md5sum` | Compute and check MD5 message digests |
| **System & Status** | `htop` | Interactive process viewer with colored CPU meters, memory gauges, and F1-F10 keys (`-d`, `-u`, `-p`, `-s`, `-b`) |
| | `top` | Dynamic / snapshot process and resource monitor (`-b` batch, `-n` iterations, `-d` delay) |
| | `neofetch` | Iconic ASCII art system info badge (OS, Kernel, Uptime, CPU, RAM) |
| | `uname` | Print system info (`-a`, `-s`, `-n`, `-r`, `-v`, `-m`, `-o`) |
| | `whoami` | Print current effective username |
| | `id` | Print real and effective user and group IDs (`-u`, `-g`, `-G`, `-n`) |
| | `groups` | Print user security group memberships |
| | `arch` | Print machine hardware architecture (x86_64, aarch64, etc.) |
| | `printenv` | Print environment variables (`-0` null-terminated) |
| | `seq` | Print sequences of numbers (`-w` equal width, `-s` separator, `-f` format) |
| | `yes` | Output a string repeatedly until killed |
| | `true` | Return success exit code 0 |
| | `false` | Return failure exit code 1 |
| | `cal` | Displays formatted monthly or yearly ASCII calendar grids (`-3`, `-y`) |
| | `shuf` | Generate random permutations or sample lines (`-e`, `-i LO-HI`, `-n count`, `-o file`) |
| | `hostname` | Print computer hostname |
| | `date` | Formatted POSIX date/time (`+FORMAT`) |
| | `uptime` | System uptime and load averages |
| | `df` | Disk space usage across all drives (`-h` human readable) |
| | `du` | Estimate file and directory space usage (`-h`, `-s`) |
| | `free` | Memory utilization (`-h`, `-m`, `-g`) |
| | `ps` | Snapshot of running processes (`-u` detailed) |
| | `kill` | Terminate processes by PID (`-9`) |
| | `which` | Locate built-ins and binaries on PATH |
| | `netstat` | Print active network connections and listening ports |
| | `sleep` | Delay execution for specified seconds |
| | `clear` | Clear terminal screen |
| **Network Diagnostics & Web** | `ping` | Send ICMP ECHO_REQUEST packets to network hosts (`-c count`, `-i interval`, `-W timeout`) |
| | `ifconfig` | Configure or display network interfaces with IP, MAC, MTU, and RX/TX packet counters |
| | `ip` | Show / manipulate routing and network devices (`ip a`, `ip link`, `ip route`) |
| | `nslookup` | Query DNS name servers for host IP addresses and aliases |
| | `curl` | HTTP client (`-I` headers, `-o file`, `-X method`, `-d data`) |
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
├── WinLinuxTerm.sln
├── src/
│   ├── LinuxTerm.Core/        # Core engine: path mapper, parser, AST, builtins, text editors
│   │   ├── Common/            # PosixPathMapper, ShellContext, AnsiText
│   │   ├── Parser/            # Tokenizer, CommandAst
│   │   ├── Commands/          # FileCommands, TextCommands, StreamCommands, EditorCommands,
│   │   │                      # ArchiveCommands, PathUtilCommands, MoreTextCommands,
│   │   │                      # MoreSystemCommands, MoreNetCommands, DiagnosticCommands, HtopCommand
│   │   ├── Editors/           # TextBuffer, NanoSession, VimSession
│   │   └── Execution/         # ShellEngine, ProcessRunner
│   ├── LinuxTerm.Gui/         # WPF multi-tab terminal emulator application
│   │   ├── Controls/          # TerminalControl, full-screen editor overlay, search overlay, suggestions
│   │   ├── Themes/            # TerminalTheme definitions
│   │   └── MainWindow.xaml    # Main window chrome, tab bar, split panes, theme selector
│   └── LinuxTerm.Cli/         # Console REPL and non-interactive runner
└── tests/
    └── LinuxTerm.Tests/       # xUnit test suite (82 unit tests)
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

