using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using CsvPdGen.Config;
using CsvPdGen.Services;
using CsvPdGen.Tests;
using Microsoft.Extensions.Options;

namespace CsvPdGen;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Check for test flag first
        if (args.Contains("--test"))
        {
            SmokeTests.RunSmokeTest();
            return 0;
        }

        var rootCommand = new RootCommand("CSV PD Generator - Synthesizes realistic PD input CSV files");
        
        var allOption = new Option<bool>("--all", "Read all settings from appsettings.json");
        var freqOption = new Option<string?>("--freq", "Frequency: yearly, quarterly, or monthly");
        var yearsOption = new Option<string?>("--years", "Comma-separated years (e.g., 2022,2023,2024)");
        var startOption = new Option<string?>("--start", "Start month for monthly frequency (YYYY-MM)");
        var monthsOption = new Option<int?>("--months", "Number of months for monthly frequency");
        var filesPerPeriodOption = new Option<string?>("--files-per-period", "Files per period (e.g., 1..3 or 5)");
        var rowsPerFileOption = new Option<int?>("--rows-per-file", "Rows per file (default: 1,000,000)");
        var outputOption = new Option<string?>("--out", "Output base path");
        var seedOption = new Option<int?>("--seed", "Random seed for deterministic generation");
        var gzipOption = new Option<bool?>("--gzip", "Enable gzip compression");

        rootCommand.AddOption(allOption);
        rootCommand.AddOption(freqOption);
        rootCommand.AddOption(yearsOption);
        rootCommand.AddOption(startOption);
        rootCommand.AddOption(monthsOption);
        rootCommand.AddOption(filesPerPeriodOption);
        rootCommand.AddOption(rowsPerFileOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(seedOption);
        rootCommand.AddOption(gzipOption);

        rootCommand.SetHandler(async (context) =>
        {
            var all = context.ParseResult.GetValueForOption(allOption);
            var freq = context.ParseResult.GetValueForOption(freqOption);
            var years = context.ParseResult.GetValueForOption(yearsOption);
            var start = context.ParseResult.GetValueForOption(startOption);
            var months = context.ParseResult.GetValueForOption(monthsOption);
            var filesPerPeriod = context.ParseResult.GetValueForOption(filesPerPeriodOption);
            var rowsPerFile = context.ParseResult.GetValueForOption(rowsPerFileOption);
            var output = context.ParseResult.GetValueForOption(outputOption);
            var seed = context.ParseResult.GetValueForOption(seedOption);
            var gzip = context.ParseResult.GetValueForOption(gzipOption);

            var builder = Host.CreateApplicationBuilder(args);
            
            // Configure logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            
            // Bind configuration options
            builder.Services.Configure<GenerationOptions>(builder.Configuration.GetSection("Generation"));
            builder.Services.Configure<FrequenciesOptions>(builder.Configuration.GetSection("Frequencies"));
            builder.Services.Configure<CustomersOptions>(builder.Configuration.GetSection("Customers"));
            builder.Services.Configure<DistributionsOptions>(builder.Configuration.GetSection("Distributions"));
            builder.Services.Configure<AmountsOptions>(builder.Configuration.GetSection("Amounts"));
            builder.Services.Configure<DpdModelOptions>(builder.Configuration.GetSection("DpdModel"));
            builder.Services.Configure<LifecycleOptions>(builder.Configuration.GetSection("Lifecycle"));
            builder.Services.Configure<DpdEvolutionOptions>(builder.Configuration.GetSection("DpdEvolution"));
            builder.Services.Configure<QaRulesOptions>(builder.Configuration.GetSection("QaRules"));
            
            // Override with command line arguments if not using --all
            if (!all)
            {
                builder.Services.PostConfigure<GenerationOptions>(options =>
                {
                    if (rowsPerFile.HasValue) options.RowsPerFile = rowsPerFile.Value;
                    if (!string.IsNullOrEmpty(output)) options.OutputBasePath = output;
                    if (seed.HasValue) options.Seed = seed.Value;
                    if (gzip.HasValue) options.EnableGzip = gzip.Value;
                });
                
                builder.Services.PostConfigure<FrequenciesOptions>(options =>
                {
                    if (!string.IsNullOrEmpty(freq))
                    {
                        switch (freq.ToLowerInvariant())
                        {
                            case "yearly":
                                options.Yearly.Enabled = true;
                                if (!string.IsNullOrEmpty(years))
                                    options.Yearly.Years = years.Split(',').Select(int.Parse).ToArray();
                                if (!string.IsNullOrEmpty(filesPerPeriod))
                                    ParseFilesPerPeriod(filesPerPeriod, options.Yearly);
                                break;
                            case "quarterly":
                                options.Quarterly.Enabled = true;
                                if (!string.IsNullOrEmpty(years))
                                    options.Quarterly.Years = years.Split(',').Select(int.Parse).ToArray();
                                if (!string.IsNullOrEmpty(filesPerPeriod))
                                    ParseFilesPerPeriod(filesPerPeriod, options.Quarterly);
                                break;
                            case "monthly":
                                options.Monthly.Enabled = true;
                                if (!string.IsNullOrEmpty(start))
                                    options.Monthly.StartMonth = start;
                                if (months.HasValue)
                                    options.Monthly.MonthCount = months.Value;
                                if (!string.IsNullOrEmpty(filesPerPeriod))
                                    ParseFilesPerPeriod(filesPerPeriod, options.Monthly);
                                break;
                        }
                    }
                });
            }
            
            // Register services
            builder.Services.AddSingleton(serviceProvider =>
            {
                var generationOptions = serviceProvider.GetRequiredService<IOptions<GenerationOptions>>();
                return new SeedDeriver(generationOptions.Value.Seed);
            });
            builder.Services.AddSingleton<CustomerFactory>();
            builder.Services.AddSingleton<PeriodRowFactory>();
            builder.Services.AddSingleton<FacilityLifecycleManager>();
            builder.Services.AddSingleton<DpdEvolutionService>();
            builder.Services.AddSingleton<LifecycleRowFactory>();
            builder.Services.AddSingleton<PeriodPlanner>();
            builder.Services.AddSingleton<CsvWriterService>();
            builder.Services.AddHostedService<RunGenerationService>();
            
            var host = builder.Build();
            await host.RunAsync();
        });

        return await rootCommand.InvokeAsync(args);
    }

    private static void ParseFilesPerPeriod(string input, FrequencyConfig config)
    {
        if (input.Contains(".."))
        {
            var parts = input.Split("..");
            config.FilesPerPeriodMin = int.Parse(parts[0]);
            config.FilesPerPeriodMax = int.Parse(parts[1]);
        }
        else
        {
            var exact = int.Parse(input);
            config.FilesPerPeriodMin = exact;
            config.FilesPerPeriodMax = exact;
        }
    }
}
