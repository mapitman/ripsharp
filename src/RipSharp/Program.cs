using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEscapades.Configuration.Yaml;
using Spectre.Console;

namespace RipSharp;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((ctx, cfg) =>
            {
                cfg.Sources.Clear();
                cfg.AddYamlFile("appsettings.yaml", optional: true, reloadOnChange: true);
                cfg.AddEnvironmentVariables();
            })
            .ConfigureServices((ctx, services) =>
            {
                services.Configure<AppConfig>(ctx.Configuration);
                services.AddSingleton<IProgressNotifier, ConsoleProgressNotifier>();
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
                    var notifier = sp.GetRequiredService<IProgressNotifier>();
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
                    var notifier = sp.GetRequiredService<IProgressNotifier>();
                    if (!string.IsNullOrWhiteSpace(tvdbKey))
                        return new TvdbMetadataProvider(new HttpClient(), tvdbKey, notifier);
                    return new NullEpisodeTitleProvider();
                });

                services.AddSingleton<IMetadataService, MetadataService>();
                services.AddSingleton<IEncoderService, EncoderService>();
                services.AddSingleton<IDiscRipper, DiscRipper>();
            })
            .Build();

        var options = RipOptions.ParseArgs(args);
        
        if (options.ShowHelp)
        {
            RipOptions.DisplayHelp();
            return 0;
        }
        
        var ripper = host.Services.GetRequiredService<IDiscRipper>();
        var files = await ripper.ProcessDiscAsync(options);

        if (files.Count > 0)
        {
            AnsiConsole.MarkupLine($"[{ConsoleColors.Success}]Success! Files created:[/]");
            foreach (var f in files)
            {
                AnsiConsole.WriteLine(Markup.Escape(f));
            }
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine($"[{ConsoleColors.Error}]Failed to process disc[/]");
            return 1;
        }
    }
}
