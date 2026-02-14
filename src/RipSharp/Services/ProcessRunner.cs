using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BugZapperLabs.RipSharp.Services;

public class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(string fileName, string arguments, Action<string>? onOutput = null, Action<string>? onError = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        proc.OutputDataReceived += (_, e) => { if (e.Data != null) onOutput?.Invoke(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) onError?.Invoke(e.Data); };
        proc.Exited += (_, __) => tcs.TrySetResult(proc.ExitCode);

        if (!proc.Start()) throw new InvalidOperationException($"Failed to start {fileName}");
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using (ct.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch { } }))
        {
            var exit = await tcs.Task.ConfigureAwait(false);
            return exit;
        }
    }
}
