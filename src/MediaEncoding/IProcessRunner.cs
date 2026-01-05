namespace MediaEncoding;

public interface IProcessRunner
{
    Task<int> RunAsync(string fileName, string arguments, 
        System.Action<string>? onOutput = null, System.Action<string>? onError = null,
        System.Threading.CancellationToken ct = default);
}
