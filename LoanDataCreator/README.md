# CSV PD Generator (CsvPdGen)

A production-ready, config-driven console application that synthesizes large, realistic PD (Probability of Default) input CSV files. Built with .NET 8 and C# 12, this tool generates deterministic, internally consistent data across multiple periods and frequencies.

## Features

- **Deterministic Generation**: Same seed produces identical outputs
- **Multiple Frequencies**: Yearly, Quarterly, and Monthly data generation
- **Realistic Data**: Statistically consistent PD data with proper business rules
- **Memory Efficient**: Streams output without loading millions of rows in memory
- **Configurable**: Extensive configuration via `appsettings.json` and CLI arguments
- **Fast Performance**: Optimized for high-throughput generation
- **Compression Support**: Optional gzip compression for output files
- **Proper CSV Handling**: RFC 4180 compliant with proper escaping

## Quick Start

### Build and Run

```bash
# Build the project
dotnet build

# Run with default settings (development profile - 1000 rows)
dotnet run

# Run with production settings
dotnet run --environment Production --all

# Generate specific configuration via CLI
dotnet run -- --freq yearly --years 2024,2025 --rows-per-file 1000000 --out "D:\PDData" --seed 12345
```

### CLI Arguments

- `--all`: Read all settings from appsettings.json (ignores other CLI args)
- `--freq`: Frequency type (`yearly`, `quarterly`, `monthly`)
- `--years`: Comma-separated years (e.g., `2022,2023,2024`)
- `--start`: Start month for monthly frequency (`YYYY-MM`)
- `--months`: Number of months for monthly frequency
- `--files-per-period`: Files per period (`1..3` for range or `5` for exact)
- `--rows-per-file`: Rows per file (default: 1,000,000)
- `--out`: Output base path
- `--seed`: Random seed for deterministic generation
- `--gzip`: Enable gzip compression (`true`/`false`)

## Configuration

### Main Configuration (appsettings.json)

The application supports extensive configuration through `appsettings.json`:

#### Generation Settings
```json
{
  "Generation": {
    "OutputBasePath": "Output",
    "RowsPerFile": 1000000,
    "EmitBom": false,
    "EnableGzip": false,
    "Seed": 424242
  }
}
```

#### Frequency Configuration
```json
{
  "Frequencies": {
    "Yearly": {
      "Enabled": true,
      "Years": [2022, 2023, 2024, 2025],
      "FilesPerPeriodMin": 1,
      "FilesPerPeriodMax": 3
    },
    "Quarterly": {
      "Enabled": false,
      "Years": [2024, 2025],
      "FilesPerPeriodMin": 1,
      "FilesPerPeriodMax": 2
    },
    "Monthly": {
      "Enabled": false,
      "StartMonth": "2024-01",
      "MonthCount": 24,
      "FilesPerPeriodMin": 1,
      "FilesPerPeriodMax": 1
    }
  }
}
```

### Development Profile

For quick testing, use the development profile which generates smaller files:

```bash
dotnet run --environment Development
```

This uses `appsettings.Development.json` with 1,000 rows per file and 100 customers.

## Output Structure

The generated files follow this directory structure:

```
Output/
??? Yearly/
?   ??? 2024/
?   ?   ??? PD_2024_01.csv
?   ?   ??? PD_2024_02.csv
?   ??? 2025/
?       ??? PD_2025_01.csv
??? Quarterly/
?   ??? 2024Q1/
?   ?   ??? PD_2024Q1_01.csv
?   ??? 2024Q2/
?       ??? PD_2024Q2_01.csv
??? Monthly/
    ??? 2024-01/
    ?   ??? PD_2024-01_01.csv
    ??? 2024-02/
        ??? PD_2024-02_01.csv
```

## CSV Schema

The generated CSV files contain the following columns in exact order:

1. Customer Number
2. Facility number
3. Branch
4. Product category
5. Segment
6. Industry
7. Earning Type
8. Nature
9. Grant date (yyyy-MM-dd)
10. Maturity date/ Expiry Date (yyyy-MM-dd)
11. Interest Rate
12. Installment Type (Monthly/ Quarterly/ Weekly/ Daily/ Annually/ Bullet)
13. Days Past Due
14. Limit
15. Total OS
16. Undisbursed Amount
17. Interest in Suspense
18. Collateral Type
19. Collateral Value
20. Rescheduled (Yes/No)
21. Restructured (Yes/No)
22. No. of Times Restructured
23. Upgraded to delinquency bucket (Yes/No)
24. Individually Impaired (Yes/No)
25. Bucketing in Individual Assessment
26. Period

## Data Consistency Rules

The generator ensures the following business rules:

### Customer Consistency
- Customer attributes (Segment, Industry, Earning Type, Nature) remain constant across all periods
- Facility numbers are stable for each customer's facilities
- Customer numbers follow format: `CUST########`

### Financial Constraints
- Grant Date ? Maturity Date
- Limit ? Total OS + Undisbursed Amount
- Interest rates vary by segment with controlled volatility
- Days Past Due follows realistic distribution with shock scenarios

### Risk Assessment
- Risk flags (Rescheduled, Restructured, etc.) probability increases with DPD
- Bucketing assessment derived from DPD and risk flags
- Interest in Suspense calculated based on outstanding amount and DPD

## Performance Tips

1. **Memory Usage**: The application streams data and uses minimal memory regardless of file size
2. **Disk I/O**: Uses large buffers (64KB) for optimal write performance
3. **Compression**: Enable gzip for 60-80% file size reduction with minimal performance impact
4. **Parallel Generation**: Run multiple instances with different seeds for parallel generation

## Examples

### Generate Yearly Data for 2024-2025
```bash
dotnet run -- --freq yearly --years 2024,2025 --files-per-period 2 --rows-per-file 500000
```

### Generate Monthly Data for 2024
```bash
dotnet run -- --freq monthly --start 2024-01 --months 12 --rows-per-file 100000 --gzip true
```

### Generate with Custom Seed and Output Path
```bash
dotnet run -- --all --seed 987654 --out "C:\PDData"
```

## Architecture

The application follows clean architecture principles:

- **Program.cs**: Entry point with hosting and DI setup
- **Config/**: Configuration options classes
- **Domain/**: Core domain models
- **Services/**: Business logic services
  - `SeedDeriver`: Deterministic seed generation
  - `CustomerFactory`: Customer master data creation
  - `PeriodRowFactory`: Period-specific row generation
  - `PeriodPlanner`: Work item planning
  - `CsvWriterService`: CSV file writing
  - `RunGenerationService`: Main orchestration

## Troubleshooting

### Common Issues

1. **Out of Memory**: Ensure you're not trying to load all data in memory. The application streams by design.
2. **Slow Performance**: Check disk I/O and consider enabling compression for network storage.
3. **Inconsistent Data**: Verify the same seed is used for regeneration.
4. **File Access Errors**: Ensure output directory has write permissions.

### Validation

To validate generated files:
1. Check row count matches configured `RowsPerFile`
2. Verify CSV headers match expected schema
3. Confirm dates are in yyyy-MM-dd format
4. Validate that financial constraints are met
5. Open in Excel to verify proper CSV formatting

## License

This project is provided as-is for educational and development purposes.