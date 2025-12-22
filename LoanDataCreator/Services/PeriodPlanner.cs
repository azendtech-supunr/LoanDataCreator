using Microsoft.Extensions.Options;
using CsvPdGen.Config;
using CsvPdGen.Domain;
using System.Globalization;

namespace CsvPdGen.Services;

public class PeriodPlanner
{
    private readonly SeedDeriver _seedDeriver;
    private readonly GenerationOptions _generation;
    private readonly FrequenciesOptions _frequencies;

    public PeriodPlanner(
        SeedDeriver seedDeriver,
        IOptions<GenerationOptions> generation,
        IOptions<FrequenciesOptions> frequencies)
    {
        _seedDeriver = seedDeriver;
        _generation = generation.Value;
        _frequencies = frequencies.Value;
        
        // Early validation: Ensure only one frequency is enabled
        ValidateSingleFrequency();
    }

    /// <summary>
    /// Validates that exactly one frequency is enabled for lifecycle-consistent generation.
    /// Throws InvalidOperationException if multiple frequencies or no frequencies are enabled.
    /// </summary>
    private void ValidateSingleFrequency()
    {
        var enabledCount = 0;
        var enabledFrequencies = new List<string>();

        if (_frequencies.Yearly.Enabled)
        {
            enabledCount++;
            enabledFrequencies.Add("Yearly");
        }

        if (_frequencies.Quarterly.Enabled)
        {
            enabledCount++;
            enabledFrequencies.Add("Quarterly");
        }

        if (_frequencies.Monthly.Enabled)
        {
            enabledCount++;
            enabledFrequencies.Add("Monthly");
        }

        if (enabledCount == 0)
        {
            throw new InvalidOperationException(
                "Lifecycle-consistent generation requires exactly one frequency to be enabled in appsettings.json. " +
                "Currently, no frequencies are enabled. " +
                "Please enable exactly one of: Yearly, Quarterly, or Monthly in the Frequencies configuration section.");
        }

        if (enabledCount > 1)
        {
            throw new InvalidOperationException(
                $"Lifecycle-consistent generation requires exactly one frequency to be enabled in appsettings.json. " +
                $"Currently, {enabledCount} frequencies are enabled: {string.Join(", ", enabledFrequencies)}. " +
                $"Mixed period frequencies are not supported because lifecycle evolution requires sequential periods of the same granularity. " +
                $"Please disable all but one frequency in the Frequencies configuration section. " +
                $"For example, if generating monthly data, set Yearly.Enabled=false and Quarterly.Enabled=false.");
        }
    }

    /// <summary>
    /// Gets the single enabled frequency type.
    /// </summary>
    private string GetEnabledFrequency()
    {
        if (_frequencies.Yearly.Enabled) return "Yearly";
        if (_frequencies.Quarterly.Enabled) return "Quarterly";
        if (_frequencies.Monthly.Enabled) return "Monthly";
        
        throw new InvalidOperationException("No frequency is enabled. This should have been caught by ValidateSingleFrequency.");
    }

    /// <summary>
    /// Plans all work items based on the frequency configurations.
    /// Only processes the single enabled frequency.
    /// </summary>
    public List<WorkItem> PlanGeneration()
    {
        var enabledFrequency = GetEnabledFrequency();

        return enabledFrequency switch
        {
            "Yearly" => PlanYearly(),
            "Quarterly" => PlanQuarterly(),
            "Monthly" => PlanMonthly(),
            _ => throw new InvalidOperationException($"Unknown frequency: {enabledFrequency}")
        };
    }

    /// <summary>
    /// Gets all periods for the single enabled frequency in chronological order.
    /// This is the single authority for period generation and ensures lifecycle consistency.
    /// CRITICAL: Returns periods for ONLY the enabled frequency - no mixing allowed.
    /// </summary>
    public List<PeriodInfo> GetAllPeriodsOrdered()
    {
        var enabledFrequency = GetEnabledFrequency();

        var periods = enabledFrequency switch
        {
            "Yearly" => GenerateYearlyPeriods(),
            "Quarterly" => GenerateQuarterlyPeriods(),
            "Monthly" => GenerateMonthlyPeriods(),
            _ => throw new InvalidOperationException($"Unknown frequency: {enabledFrequency}")
        };

        // Defensive validation: Ensure all periods have the same frequency
        ValidatePeriodFrequencyConsistency(periods, enabledFrequency);

        return periods;
    }

    /// <summary>
    /// Validates that all periods in the list have the same frequency.
    /// This is a defensive check to prevent lifecycle bugs from mixed periods.
    /// </summary>
    private void ValidatePeriodFrequencyConsistency(List<PeriodInfo> periods, string expectedFrequency)
    {
        if (periods.Count == 0)
        {
            throw new InvalidOperationException(
                $"No periods were generated for frequency '{expectedFrequency}'. " +
                $"Please check the configuration for {expectedFrequency} in appsettings.json.");
        }

        var inconsistentPeriods = periods.Where(p => p.Frequency != expectedFrequency).ToList();
        
        if (inconsistentPeriods.Any())
        {
            var examples = string.Join(", ", inconsistentPeriods.Take(3).Select(p => $"{p.PeriodKey} ({p.Frequency})"));
            throw new InvalidOperationException(
                $"Period frequency consistency violation detected! " +
                $"Expected all periods to be '{expectedFrequency}', but found {inconsistentPeriods.Count} periods with different frequencies. " +
                $"Examples: {examples}. " +
                $"This is a critical bug in period generation logic. " +
                $"Lifecycle evolution requires all periods to have the same frequency.");
        }

        // Validate chronological ordering
        for (var i = 1; i < periods.Count; i++)
        {
            if (periods[i].PeriodEndDate < periods[i - 1].PeriodEndDate)
            {
                // Show more context about the ordering issue
                var contextStart = Math.Max(0, i - 3);
                var contextEnd = Math.Min(periods.Count, i + 3);
                var context = string.Join(" ? ", 
                    periods.Skip(contextStart).Take(contextEnd - contextStart).Select(p => p.PeriodKey));
                
                throw new InvalidOperationException(
                    $"Period ordering violation detected! " +
                    $"Period {periods[i].PeriodKey} (ending {periods[i].PeriodEndDate:yyyy-MM-dd}) " +
                    $"comes after period {periods[i - 1].PeriodKey} (ending {periods[i - 1].PeriodEndDate:yyyy-MM-dd}) " +
                    $"but has an earlier end date. Periods must be in strict chronological order.\n" +
                    $"Context around error (positions {contextStart}-{contextEnd-1}): {context}");
            }
        }
    }

    /// <summary>
    /// Generates periods for Yearly frequency only.
    /// </summary>
    private List<PeriodInfo> GenerateYearlyPeriods()
    {
        var periods = new List<PeriodInfo>();
        var config = _frequencies.Yearly;

        var sequenceIndex = 0;
        // Remove duplicates and sort years
        foreach (var year in config.Years.Distinct().OrderBy(y => y))
        {
            var periodKey = year.ToString();
            var periodEndDate = new DateTime(year, 12, 31);
            periods.Add(new PeriodInfo(periodKey, periodEndDate, "Yearly", sequenceIndex++));
        }

        return periods;
    }

    /// <summary>
    /// Generates periods for Quarterly frequency only.
    /// </summary>
    private List<PeriodInfo> GenerateQuarterlyPeriods()
    {
        var periods = new List<PeriodInfo>();
        var config = _frequencies.Quarterly;

        var sequenceIndex = 0;
        
        // Sort years and remove duplicates to ensure chronological order
        var sortedYears = config.Years.Distinct().OrderBy(y => y).ToList();
        
        // Debug: Log what years we're processing
        Console.WriteLine($"DEBUG: Original years from config: {string.Join(", ", config.Years)}");
        Console.WriteLine($"DEBUG: After deduplication: {string.Join(", ", sortedYears)}");
        Console.WriteLine($"DEBUG: Total unique years: {sortedYears.Count}");
        
        foreach (var year in sortedYears)
        {
            for (var quarter = 1; quarter <= 4; quarter++)
            {
                var periodKey = $"{year}Q{quarter}";
                var month = quarter * 3;
                var periodEndDate = new DateTime(year, month, DateTime.DaysInMonth(year, month));
                periods.Add(new PeriodInfo(periodKey, periodEndDate, "Quarterly", sequenceIndex++));
            }
        }
        
        Console.WriteLine($"DEBUG: Generated {periods.Count} quarterly periods (expected: {sortedYears.Count * 4})");
        Console.WriteLine($"DEBUG: First 5: {string.Join(", ", periods.Take(5).Select(p => p.PeriodKey))}");
        Console.WriteLine($"DEBUG: Last 5: {string.Join(", ", periods.TakeLast(5).Select(p => p.PeriodKey))}");

        return periods;
    }

    /// <summary>
    /// Generates periods for Monthly frequency only.
    /// </summary>
    private List<PeriodInfo> GenerateMonthlyPeriods()
    {
        var periods = new List<PeriodInfo>();
        var config = _frequencies.Monthly;

        var startDate = DateTime.ParseExact(config.StartMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        
        for (var i = 0; i < config.MonthCount; i++)
        {
            var monthDate = startDate.AddMonths(i);
            var periodKey = monthDate.ToString("yyyy-MM");
            var periodEndDate = new DateTime(monthDate.Year, monthDate.Month, DateTime.DaysInMonth(monthDate.Year, monthDate.Month));
            periods.Add(new PeriodInfo(periodKey, periodEndDate, "Monthly", i));
        }

        return periods;
    }

    private List<WorkItem> PlanYearly()
    {
        var workItems = new List<WorkItem>();
        var config = _frequencies.Yearly;

        foreach (var year in config.Years.Distinct().OrderBy(y => y))  // ? FIX: Remove duplicates and sort
        {
            var periodKey = year.ToString();
            var fileCount = DetermineFileCount(config, periodKey);
            var baseDir = Path.Combine(_generation.OutputBasePath, "Yearly", periodKey);

            for (var fileIndex = 1; fileIndex <= fileCount; fileIndex++)
            {
                var fileName = $"PD_{periodKey}_{fileIndex:D2}";
                fileName += _generation.EnableGzip ? ".csv.gz" : ".csv";
                var filePath = Path.Combine(baseDir, fileName);

                workItems.Add(new WorkItem(
                    "Yearly",
                    periodKey,
                    fileIndex,
                    filePath,
                    _generation.RowsPerFile));
            }
        }

        return workItems;
    }

    private List<WorkItem> PlanQuarterly()
    {
        var workItems = new List<WorkItem>();
        var config = _frequencies.Quarterly;

        foreach (var year in config.Years.Distinct().OrderBy(y => y))  // ? FIX: Remove duplicates and sort
        {
            for (var quarter = 1; quarter <= 4; quarter++)
            {
                var periodKey = $"{year}Q{quarter}";
                var fileCount = DetermineFileCount(config, periodKey);
                var baseDir = Path.Combine(_generation.OutputBasePath, "Quarterly", periodKey);

                for (var fileIndex = 1; fileIndex <= fileCount; fileIndex++)
                {
                    var fileName = $"PD_{periodKey}_{fileIndex:D2}";
                    fileName += _generation.EnableGzip ? ".csv.gz" : ".csv";
                    var filePath = Path.Combine(baseDir, fileName);

                    workItems.Add(new WorkItem(
                        "Quarterly",
                        periodKey,
                        fileIndex,
                        filePath,
                        _generation.RowsPerFile));
                }
            }
        }

        return workItems;
    }

    private List<WorkItem> PlanMonthly()
    {
        var workItems = new List<WorkItem>();
        var config = _frequencies.Monthly;

        var startDate = DateTime.ParseExact(config.StartMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        
        for (var i = 0; i < config.MonthCount; i++)
        {
            var monthDate = startDate.AddMonths(i);
            var periodKey = monthDate.ToString("yyyy-MM");
            var fileCount = DetermineFileCount(config, periodKey);
            var baseDir = Path.Combine(_generation.OutputBasePath, "Monthly", periodKey);

            for (var fileIndex = 1; fileIndex <= fileCount; fileIndex++)
            {
                var fileName = $"PD_{periodKey}_{fileIndex:D2}";
                fileName += _generation.EnableGzip ? ".csv.gz" : ".csv";
                var filePath = Path.Combine(baseDir, fileName);

                workItems.Add(new WorkItem(
                    "Monthly",
                    periodKey,
                    fileIndex,
                    filePath,
                    _generation.RowsPerFile));
            }
        }

        return workItems;
    }

    private int DetermineFileCount(FrequencyConfig config, string periodKey)
    {
        if (config.FilesPerPeriodMin == config.FilesPerPeriodMax)
        {
            return config.FilesPerPeriodMin;
        }

        var random = _seedDeriver.CreateRandom($"filecount:{periodKey}");
        return random.Next(config.FilesPerPeriodMin, config.FilesPerPeriodMax + 1);
    }
}
