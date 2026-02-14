using System.Reflection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


namespace BugZapperLabs.RipSharp.Core;

public class Program
{
    private static CancellationTokenSource? _cancellationTokenSource;

    public static async Task<int> Main(string[] args)
    {
        // Manage cursor visibility for entire application lifetime
        using var cursorManager = new CursorManager();
        _cancellationTokenSource = new CancellationTokenSource();

        // Register handlers for graceful shutdown
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;  // Prevent immediate termination, allow graceful shutdown
            cursorManager.RestoreCursor();
            _cancellationTokenSource?.Cancel();
        };

        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            cursorManager.RestoreCursor();
        };

        return await RunAsync(args, cursorManager);
    }

    private static async Task<int> RunAsync(string[] args, CursorManager cursorManager)
    {
        var options = RipOptions.ParseArgs(args);

        if (options.ShowHelp)
        {
            RipOptions.DisplayHelp(new ConsoleWriter());
            return 0;
        }

        if (options.ShowVersion)
        {
            Console.WriteLine($"ripsharp {GetVersion()}");
            return 0;
        }

        var missingTools = PrerequisiteChecker.GetMissingTools(
            Environment.GetEnvironmentVariable("PATH"),
            OperatingSystem.IsWindows(),
            File.Exists);

        if (missingTools.Count > 0)
        {
            var prereqWriter = new ConsoleWriter();
            prereqWriter.Error("Missing required prerequisites:");
            foreach (var tool in missingTools)
            {
                prereqWriter.Error($"  - {tool}");
            }

            prereqWriter.Plain("");
            prereqWriter.Info("Install instructions:");
            if (OperatingSystem.IsWindows())
            {
                prereqWriter.Plain("  - winget install --id Gyan.FFmpeg");
                prereqWriter.Plain("  - Download MakeMKV: https://www.makemkv.com/");
            }
            else if (OperatingSystem.IsMacOS())
            {
                prereqWriter.Plain("  - brew install ffmpeg");
                prereqWriter.Plain("  - Download MakeMKV: https://www.makemkv.com/");
            }
            else
            {
                prereqWriter.Plain("  - Ubuntu/Debian: sudo apt install ffmpeg");
                prereqWriter.Plain("  - Fedora: sudo dnf install ffmpeg");
                prereqWriter.Plain("  - Arch: sudo pacman -S ffmpeg");
                prereqWriter.Plain("  - openSUSE: sudo zypper install ffmpeg");
                prereqWriter.Plain("  - Alpine: sudo apk add ffmpeg");
                prereqWriter.Plain("  - MakeMKV: https://www.makemkv.com/");
            }

            return 2;
        }

        using var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((ctx, cfg) =>
            {
                cfg.Sources.Clear();
                var configContext = ConfigFileLocator.CreateContext();
                var configPath = ConfigFileLocator.ResolveConfigPath(
                    configContext,
                    ctx.HostingEnvironment.IsDevelopment(),
                    File.Exists,
                    File.WriteAllText,
                    path => Directory.CreateDirectory(path));

                if (!string.IsNullOrWhiteSpace(configPath))
                {
                    cfg.AddYamlFile(configPath, optional: false, reloadOnChange: true);
                }

                cfg.AddEnvironmentVariables();
            })
            .ConfigureServices((ctx, services) =>
            {
                services.Configure<AppConfig>(ctx.Configuration);
                services.AddSingleton<IConsoleWriter, ConsoleWriter>();
                services.AddSingleton<IProgressDisplay, SpectreProgressDisplay>();
                services.AddSingleton<IUserPrompt, ConsoleUserPrompt>();
                services.AddSingleton<IProcessRunner, ProcessRunner>();
                services.AddSingleton<IMakeMkvService, MakeMkvService>();
                services.AddSingleton<IDiscScanner, DiscScanner>();
                services.AddSingleton<IDiscTypeDetector, DiscTypeDetector>();

                // Register metadata providers
                var omdbKey = Environment.GetEnvironmentVariable("OMDB_API_KEY");
                var tmdbKey = Environment.GetEnvironmentVariable("TMDB_API_KEY");
                var tvdbKey = Environment.GetEnvironmentVariable("TVDB_API_KEY");

                services.AddSingleton<IEnumerable<IMetadataProvider>>(sp =>
                {
                    var notifier = sp.GetRequiredService<IConsoleWriter>();
                    var httpClient = new HttpClient();
                    var providers = new List<IMetadataProvider>();

                    if (!string.IsNullOrWhiteSpace(omdbKey))
                        providers.Add(new OmdbMetadataProvider(httpClient, omdbKey, notifier));
                    if (!string.IsNullOrWhiteSpace(tmdbKey))
                        providers.Add(new TmdbMetadataProvider(httpClient, tmdbKey, notifier));
                    if (!string.IsNullOrWhiteSpace(tvdbKey))
                        providers.Add(new TvdbMetadataProvider(httpClient, tvdbKey, notifier));

                    return providers;
                });

                services.AddSingleton<ITvEpisodeTitleProvider>(sp =>
                {
                    var notifier = sp.GetRequiredService<IConsoleWriter>();
                    if (!string.IsNullOrWhiteSpace(tvdbKey))
                        return new TvdbMetadataProvider(new HttpClient(), tvdbKey, notifier);
                    return new NullEpisodeTitleProvider();
                });

                services.AddSingleton<IMetadataService, MetadataService>();
                services.AddSingleton<IEncoderService, EncoderService>();
                services.AddSingleton<IDiscRipper, DiscRipper>();
            })
            .Build();

        var ripper = host.Services.GetRequiredService<IDiscRipper>();
        var writer = host.Services.GetRequiredService<IConsoleWriter>();

        List<string> files;
        try
        {
            files = await ripper.ProcessDiscAsync(options, _cancellationTokenSource!.Token);
        }
        catch (OperationCanceledException)
        {
            writer.Warning("\n⚠️  Operation interrupted by user");
            return 130;
        }

        if (files.Count > 0)
        {
            return 0;
        }
        else
        {
            writer.Error("Failed to process disc");
            return 1;
        }
    }

    private static string GetVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(infoVersion))
        {
            return infoVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
