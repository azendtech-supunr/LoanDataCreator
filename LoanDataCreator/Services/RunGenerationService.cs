using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CsvPdGen.Config;
using CsvPdGen.Domain;

namespace CsvPdGen.Services;

public class RunGenerationService : BackgroundService
{
    private readonly ILogger<RunGenerationService> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly GenerationOptions _generation;
    private readonly CustomersOptions _customers;
    private readonly SeedDeriver _seedDeriver;
    private readonly CustomerFactory _customerFactory;
    private readonly PeriodRowFactory _periodRowFactory;
    private readonly PeriodPlanner _periodPlanner;
    private readonly CsvWriterService _csvWriter;

    public RunGenerationService(
        ILogger<RunGenerationService> logger,
        IHostApplicationLifetime lifetime,
        IOptions<GenerationOptions> generation,
        IOptions<CustomersOptions> customers,
        SeedDeriver seedDeriver,
        CustomerFactory customerFactory,
        PeriodRowFactory periodRowFactory,
        PeriodPlanner periodPlanner,
        CsvWriterService csvWriter)
    {
        _logger = logger;
        _lifetime = lifetime;
        _generation = generation.Value;
        _customers = customers.Value;
        _seedDeriver = seedDeriver;
        _customerFactory = customerFactory;
        _periodRowFactory = periodRowFactory;
        _periodPlanner = periodPlanner;
        _csvWriter = csvWriter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Starting CSV PD generation with seed {Seed}", _generation.Seed);

            // Initialize the seed deriver
            var seedDeriver = new SeedDeriver(_generation.Seed);

            // Plan all work items
            var workItems = _periodPlanner.PlanGeneration();
            _logger.LogInformation("Planned {Count} files to generate", workItems.Count);

            var totalRows = 0L;
            var startTime = DateTime.UtcNow;

            // Process each work item
            foreach (var workItem in workItems)
            {
                stoppingToken.ThrowIfCancellationRequested();

                _logger.LogInformation("Generating {FilePath} with {Rows:N0} rows", 
                    workItem.FilePath, workItem.Rows);

                await GenerateWorkItem(workItem, stoppingToken);
                totalRows += workItem.Rows;

                _logger.LogInformation("Completed {FilePath}", workItem.FilePath);
            }

            var elapsed = DateTime.UtcNow - startTime;
            var rowsPerSecond = totalRows / elapsed.TotalSeconds;

            _logger.LogInformation("Generation completed! Generated {TotalRows:N0} rows in {Elapsed} ({RowsPerSecond:N0} rows/sec)",
                totalRows, elapsed, rowsPerSecond);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during generation");
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }

    private async Task GenerateWorkItem(WorkItem workItem, CancellationToken cancellationToken)
    {
        var rows = GenerateRowsForWorkItem(workItem, cancellationToken);
        await _csvWriter.WriteCsvFileAsync(workItem.FilePath, rows, cancellationToken);
    }

    private IEnumerable<PeriodRow> GenerateRowsForWorkItem(WorkItem workItem, CancellationToken cancellationToken)
    {
        var random = _seedDeriver.CreatePeriodRandom(workItem.PeriodKey, workItem.FileIndex);
        var generatedRows = 0;

        while (generatedRows < workItem.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Select a random customer
            var customerId = random.Next(1, _customers.CustomerCount + 1);
            var customer = _customerFactory.CreateCustomer(customerId);

            // Generate rows for each facility of this customer
            for (var facilityIndex = 1; facilityIndex <= customer.FacilityCount && generatedRows < workItem.Rows; facilityIndex++)
            {
                var row = _periodRowFactory.CreatePeriodRow(customer, facilityIndex, workItem.PeriodKey);
                yield return row;

                generatedRows++;

                // Log progress periodically
                if (generatedRows % _generation.LogProgressEveryNRows == 0)
                {
                    _logger.LogInformation("Generated {GeneratedRows:N0} / {TotalRows:N0} rows for {FilePath}",
                        generatedRows, workItem.Rows, workItem.FilePath);
                }
            }
        }
    }
}