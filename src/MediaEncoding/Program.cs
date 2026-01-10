using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEscapades.Configuration.Yaml;
using Spectre.Console;

namespace MediaEncoding;

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
                services.AddSingleton<IProcessRunner, ProcessRunner>();
                services.AddSingleton<IMakeMkvService, MakeMkvService>();
                services.AddSingleton<IDiscScanner, DiscScanner>();
                services.AddSingleton<IMetadataService, MetadataService>();
                services.AddSingleton<IEncoderService, EncoderService>();
                services.AddSingleton<IDiscRipper, DiscRipper>();
            })
            .Build();

        var ripper = host.Services.GetRequiredService<IDiscRipper>();
        var options = RipOptions.ParseArgs(args);
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
