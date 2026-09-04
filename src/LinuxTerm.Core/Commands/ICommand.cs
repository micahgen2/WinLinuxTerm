using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LinuxTerm.Core.Common;

namespace LinuxTerm.Core.Commands;

public interface ICommand
{
    string Name { get; }
    string Description { get; }
    string Synopsis { get; }

    /// <summary>
    /// Executes the command with arguments, standard I/O, and session context.
    /// </summary>
    /// <returns>Process exit code (0 for success, non-zero for failure)</returns>
    Task<int> ExecuteAsync(
        string[] args,
        ShellContext context,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct);
}
