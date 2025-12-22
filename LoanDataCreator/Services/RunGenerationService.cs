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
    private readonly FacilityLifecycleManager _lifecycleManager;
    private readonly LifecycleRowFactory _lifecycleRowFactory;
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
        FacilityLifecycleManager lifecycleManager,
        LifecycleRowFactory lifecycleRowFactory,
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
        _lifecycleManager = lifecycleManager;
        _lifecycleRowFactory = lifecycleRowFactory;
        _periodPlanner = periodPlanner;
        _csvWriter = csvWriter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Starting CSV PD generation with seed {Seed}", _generation.Seed);
            _logger.LogInformation("Using lifecycle-consistent data generation");

            // Get all periods in chronological order for lifecycle processing
            var allPeriods = _periodPlanner.GetAllPeriodsOrdered();
            _logger.LogInformation("Processing {PeriodCount} periods in chronological order", allPeriods.Count);

            // Initialize lifecycle for the first period
            if (allPeriods.Count > 0)
            {
                var firstPeriod = allPeriods[0];
                _logger.LogInformation("Initializing facilities for first period: {Period}", firstPeriod.PeriodKey);
                _lifecycleManager.InitializeFirstPeriod(
                    firstPeriod, 
                    _customers.CustomerCount,
                    _customers.FacilitiesPerCustomerMin,
                    _customers.FacilitiesPerCustomerMax);
            }

            // Evolve lifecycle for subsequent periods
            for (var i = 1; i < allPeriods.Count; i++)
            {
                var currentPeriod = allPeriods[i];
                var previousPeriod = allPeriods[i - 1];
                
                _logger.LogInformation("Evolving facilities from {PrevPeriod} to {CurrPeriod}", 
                    previousPeriod.PeriodKey, currentPeriod.PeriodKey);
                
                _lifecycleManager.EvolveToPeriod(currentPeriod, previousPeriod, _customers.CustomerCount);
            }

            // Plan all work items (files to generate)
            var workItems = _periodPlanner.PlanGeneration();
            _logger.LogInformation("Planned {Count} files to generate", workItems.Count);

            var totalRows = 0L;
            var startTime = DateTime.UtcNow;

            // Group work items by period for lifecycle-aware generation
            var workItemsByPeriod = workItems
                .GroupBy(w => w.PeriodKey)
                .OrderBy(g => allPeriods.FindIndex(p => p.PeriodKey == g.Key))
                .ToList();

            // Process each period's work items
            foreach (var periodGroup in workItemsByPeriod)
            {
                stoppingToken.ThrowIfCancellationRequested();

                var periodKey = periodGroup.Key;
                var periodIndex = allPeriods.FindIndex(p => p.PeriodKey == periodKey);
                var period = allPeriods[periodIndex];
                var previousPeriodKey = periodIndex > 0 ? allPeriods[periodIndex - 1].PeriodKey : null;
                
                _logger.LogInformation("Generating data for period: {Period}", periodKey);

                foreach (var workItem in periodGroup)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    _logger.LogInformation("Generating {FilePath} with {Rows:N0} rows", 
                        workItem.FilePath, workItem.Rows);

                    await GenerateWorkItemLifecycle(workItem, period, previousPeriodKey, stoppingToken);
                    totalRows += workItem.Rows;

                    _logger.LogInformation("Completed {FilePath}", workItem.FilePath);
                }
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

    /// <summary>
    /// Generates a work item using lifecycle-consistent data.
    /// Uses active facilities from the lifecycle manager and creates rows with evolved state.
    /// </summary>
    private async Task GenerateWorkItemLifecycle(WorkItem workItem, PeriodInfo period, string? previousPeriodKey, CancellationToken cancellationToken)
    {
        var rows = GenerateLifecycleRows(workItem, period, previousPeriodKey, cancellationToken);
        await _csvWriter.WriteCsvFileAsync(workItem.FilePath, rows, cancellationToken);
    }

    private IEnumerable<PeriodRow> GenerateLifecycleRows(WorkItem workItem, PeriodInfo period, string? previousPeriodKey, CancellationToken cancellationToken)
    {
        // Get all active facilities for this period
        var activeFacilities = _lifecycleManager.GetActiveFacilities(workItem.PeriodKey).ToList();
        
        if (activeFacilities.Count == 0)
        {
            _logger.LogWarning("No active facilities for period {Period}", workItem.PeriodKey);
            yield break;
        }

        var random = _seedDeriver.CreateRandom($"file:{workItem.PeriodKey}:{workItem.FileIndex}");
        
        // FIX: Shuffle facilities and iterate to ensure each facility appears at most once
        var shuffledFacilities = activeFacilities.OrderBy(_ => random.Next()).ToList();
        
        var generatedRows = 0;
        var facilityIndex = 0;

        while (generatedRows < workItem.Rows && facilityIndex < shuffledFacilities.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Get next facility from shuffled list (no duplicates)
            var facilityNumber = shuffledFacilities[facilityIndex];
            facilityIndex++;
            
            // Get previous period state for lifecycle continuity
            var previousState = _lifecycleManager.GetPreviousPeriodState(facilityNumber, workItem.PeriodKey, previousPeriodKey);
            
            // Create row using lifecycle data
            var row = _lifecycleRowFactory.CreateLifecycleRow(facilityNumber, period, previousState);
            
            // Store state for next period
            _lifecycleRowFactory.StorePeriodState(row);
            
            yield return row;
            generatedRows++;

            // Log progress periodically
            if (generatedRows % _generation.LogProgressEveryNRows == 0)
            {
                _logger.LogInformation("Generated {GeneratedRows:N0} / {TotalRows:N0} rows for {FilePath}",
                    generatedRows, workItem.Rows, workItem.FilePath);
            }
        }
        
        // Warn if we ran out of facilities before reaching target row count
        if (generatedRows < workItem.Rows)
        {
            _logger.LogWarning("Only generated {GeneratedRows} rows out of {TargetRows} for {Period} - ran out of unique facilities. " +
                              "Consider reducing RowsPerFile or increasing CustomerCount/FacilitiesPerCustomer.",
                              generatedRows, workItem.Rows, workItem.PeriodKey);
        }
    }

    // Keep old generation method for backward compatibility (not used by default)
    private IEnumerable<PeriodRow> GenerateRowsForWorkItem(WorkItem workItem, CancellationToken cancellationToken)
    {
        var random = _seedDeriver.CreatePeriodRandom(workItem.PeriodKey, workItem.FileIndex);
        var generatedRows = 0;

        while (generatedRows < workItem.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var customerId = random.Next(1, _customers.CustomerCount + 1);
            var customer = _customerFactory.CreateCustomer(customerId);

            for (var facilityIndex = 1; facilityIndex <= customer.FacilityCount && generatedRows < workItem.Rows; facilityIndex++)
            {
                var row = _periodRowFactory.CreatePeriodRow(customer, facilityIndex, workItem.PeriodKey);
                yield return row;

                generatedRows++;

                if (generatedRows % _generation.LogProgressEveryNRows == 0)
                {
                    _logger.LogInformation("Generated {GeneratedRows:N0} / {TotalRows:N0} rows for {FilePath}",
                        generatedRows, workItem.Rows, workItem.FilePath);
                }
            }
        }
    }
}