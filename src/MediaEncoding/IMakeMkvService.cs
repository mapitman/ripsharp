using System;
using System.Threading;
using System.Threading.Tasks;

namespace MediaEncoding;

public interface IMakeMkvService
{
    Task<int> RipTitleAsync(string discPath, int titleId, string tempDir,
        Action<string>? onOutput = null,
        Action<string>? onError = null,
        CancellationToken ct = default);
}
