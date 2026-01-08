namespace MediaEncoding;

public interface IProcessRunner
{
    Task<int> RunAsync(string fileName, string arguments, 
        Action<string>? onOutput = null, Action<string>? onError = null,
        CancellationToken ct = default);
}
