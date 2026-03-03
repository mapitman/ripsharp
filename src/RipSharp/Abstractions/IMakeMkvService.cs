using BugZapperLabs.RipSharp.Models;

namespace BugZapperLabs.RipSharp.Abstractions;

public interface IMakeMkvService
{
    Task<ProcessResult> RipTitleAsync(string discPath, int titleId, string tempDir,
        Action<string>? onOutput = null,
        Action<string>? onError = null,
        CancellationToken ct = default);
}
