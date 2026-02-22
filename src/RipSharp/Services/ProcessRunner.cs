using System.Diagnostics;

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
        using var proc = new Process { StartInfo = psi };

        proc.OutputDataReceived += (_, e) => { if (e.Data != null) onOutput?.Invoke(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) onError?.Invoke(e.Data); };

        if (!proc.Start()) throw new InvalidOperationException($"Failed to start {fileName}");
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            // WaitForExitAsync alone does not guarantee all async output events have fired.
            // A synchronous WaitForExit() call afterward drains the redirected streams.
            proc.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        return proc.ExitCode;
    }
}
