using Microsoft.Extensions.Options;
using CsvPdGen.Config;
using CsvPdGen.Domain;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace CsvPdGen.Services;

public class CsvWriterService
{
    private readonly GenerationOptions _options;
    private static readonly string[] CsvHeaders = [
        "Customer Number",
        "Facility number",
        "Branch",
        "Region",        // NEW: Add Region column
        "Product category",
        "Segment",
        "Segment for LGD",
        "Industry",
        "Earning Type",
        "Nature",
        "Grant date",
        "Maturity date/ Expiry Date",
        "Interest Rate",
        "Installment Type (Monthly/ Quarterly/ Weekly/ Daily/ Annually/ Bullet)",
        "Days Past Due",
        "Limit",
        "Total OS",
        "Undisbursed Amount",
        "Interest in Suspense",
        "Collateral Type",
        "Collateral Value",
        "Rescheduled (Yes/No)",
        "Restructured (Yes/No)",
        "No. of Times Restructured",
        "Upgraded to delinquency bucket (Yes/No)",
        "Individually Impaired (Yes/No)",
        "Bucketing in Individual Assessment",
        "Period"
    ];

    public CsvWriterService(IOptions<GenerationOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Writes period rows to a CSV file with proper encoding and optional compression.
    /// </summary>
    public async Task WriteCsvFileAsync(string filePath, IEnumerable<PeriodRow> rows, CancellationToken cancellationToken = default)
    {
        // Ensure directory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var encoding = new UTF8Encoding(_options.EmitBom);

        if (_options.EnableGzip)
        {
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            await using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal);
            await using var streamWriter = new StreamWriter(gzipStream, encoding, bufferSize: 65536);
            
            await WriteDataAsync(streamWriter, rows, cancellationToken);
        }
        else
        {
            await using var streamWriter = new StreamWriter(filePath, false, encoding, bufferSize: 65536);
            await WriteDataAsync(streamWriter, rows, cancellationToken);
        }
    }

    private static async Task WriteDataAsync(StreamWriter writer, IEnumerable<PeriodRow> rows, CancellationToken cancellationToken)
    {
        // Write header
        await writer.WriteLineAsync(string.Join(",", CsvHeaders));

        // Write data rows
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var csvRow = FormatRowAsCsv(row);
            await writer.WriteLineAsync(csvRow);
        }
    }

    private static string FormatRowAsCsv(PeriodRow row)
    {
        var fields = new string[]
        {
            EscapeCsvField(row.CustomerNumber),
            EscapeCsvField(row.FacilityNumber),
            EscapeCsvField(row.Branch),
            EscapeCsvField(row.Region),
            EscapeCsvField(row.ProductCategory),
            EscapeCsvField(row.Segment),
            EscapeCsvField(row.SegmentForLGD),
            EscapeCsvField(row.Industry),
            EscapeCsvField(row.EarningType),
            EscapeCsvField(row.Nature),
            row.GrantDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            row.MaturityDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            row.InterestRate.ToString(CultureInfo.InvariantCulture),
            EscapeCsvField(row.InstallmentType),
            row.DaysPastDue.ToString(CultureInfo.InvariantCulture),
            row.Limit.ToString(CultureInfo.InvariantCulture),
            row.TotalOS.ToString(CultureInfo.InvariantCulture),
            row.UndisbursedAmount.ToString(CultureInfo.InvariantCulture),
            row.InterestInSuspense.ToString(CultureInfo.InvariantCulture),
            EscapeCsvField(row.CollateralType),
            row.CollateralValue.ToString(CultureInfo.InvariantCulture),
            EscapeCsvField(row.Rescheduled),
            EscapeCsvField(row.Restructured),
            row.NoOfTimesRestructured.ToString(CultureInfo.InvariantCulture),
            EscapeCsvField(row.UpgradedToDelinquencyBucket),
            EscapeCsvField(row.IndividuallyImpaired),
            EscapeCsvField(row.BucketingInIndividualAssessment),
            EscapeCsvField(row.Period)
        };

        return string.Join(",", fields);
    }

    /// <summary>
    /// Escapes a CSV field by quoting it if it contains special characters.
    /// </summary>
    private static string EscapeCsvField(string? field)
    {
        if (string.IsNullOrEmpty(field))
            return string.Empty;

        // Check if field needs escaping (contains comma, quote, newline, or leading/trailing spaces)
        var needsEscaping = field.Contains(',') || 
                           field.Contains('"') || 
                           field.Contains('\n') || 
                           field.Contains('\r') ||
                           field.StartsWith(' ') ||
                           field.EndsWith(' ');

        if (!needsEscaping)
            return field;

        // Escape quotes by doubling them and wrap in quotes
        var escapedField = field.Replace("\"", "\"\"");
        return $"\"{escapedField}\"";
    }
}