using BugZapperLabs.RipSharp.Models;

namespace BugZapperLabs.RipSharp.MakeMkv;

public class MakeMkvService : IMakeMkvService
{
    private readonly IProcessRunner _runner;
    public MakeMkvService(IProcessRunner runner) => _runner = runner;

    public async Task<ProcessResult> RipTitleAsync(string discPath, int titleId, string tempDir,
        Action<string>? onOutput = null,
        Action<string>? onError = null,
        CancellationToken ct = default)
    {
        var args = $"-r --robot mkv {discPath} {titleId} \"{tempDir}\"";
        var command = $"makemkvcon {args}";
        var errorLines = new List<string>();
        void wrappedOnError(string line)
        {
            if (line.StartsWith("PRGV:") || line.StartsWith("PRGC:"))
                return;

            errorLines.Add(line);
            onError?.Invoke(line);
        }
        var exitCode = await _runner.RunAsync("makemkvcon", args, onOutput, wrappedOnError, ct);
        return new ProcessResult(exitCode == 0, exitCode, errorLines, command);
    }
}
