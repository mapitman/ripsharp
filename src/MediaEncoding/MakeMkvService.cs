using System;
using System.Threading;
using System.Threading.Tasks;

namespace MediaEncoding;

public class MakeMkvService : IMakeMkvService
{
    private readonly IProcessRunner _runner;
    public MakeMkvService(IProcessRunner runner) => _runner = runner;

    public Task<int> RipTitleAsync(string discPath, int titleId, string tempDir,
        Action<string>? onOutput = null,
        Action<string>? onError = null,
        CancellationToken ct = default)
    {
        var args = $"-r --robot mkv {discPath} {titleId} \"{tempDir}\"";
        return _runner.RunAsync("makemkvcon", args, onOutput, onError, ct);
    }
}
