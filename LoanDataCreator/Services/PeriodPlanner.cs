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
    }

    /// <summary>
    /// Plans all work items based on the frequency configurations.
    /// </summary>
    public List<WorkItem> PlanGeneration()
    {
        var workItems = new List<WorkItem>();

        if (_frequencies.Yearly.Enabled)
        {
            workItems.AddRange(PlanYearly());
        }

        if (_frequencies.Quarterly.Enabled)
        {
            workItems.AddRange(PlanQuarterly());
        }

        if (_frequencies.Monthly.Enabled)
        {
            workItems.AddRange(PlanMonthly());
        }

        return workItems;
    }

    private List<WorkItem> PlanYearly()
    {
        var workItems = new List<WorkItem>();
        var config = _frequencies.Yearly;

        foreach (var year in config.Years)
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

        foreach (var year in config.Years)
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