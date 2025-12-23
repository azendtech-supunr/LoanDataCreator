# CSV PD Generator (CsvPdGen)

A production-ready, config-driven console application that synthesizes large, realistic PD (Probability of Default) input CSV files with **lifecycle-consistent data generation**. Built with .NET 8 and C# 12, this tool generates deterministic, internally consistent data across multiple periods and frequencies where customers and facilities persist with realistic evolution over time.

## ?? Latest Update: Lifecycle-Consistent Data Generation

**NEW IN THIS VERSION:** The generator now produces realistic loan portfolio data where:
- ? **Customers persist** across periods with stable identities
- ? **Facilities evolve** realistically instead of being regenerated randomly
- ? **DPD progresses** logically from period to period
- ? **Facilities settle** based on maturity, DPD, and product rules
- ? **New facilities** are added to portfolios over time
- ? **All existing configurability preserved**

?? **See [MIGRATION_GUIDE.md](MIGRATION_GUIDE.md) for upgrade details**
?? **See [LIFECYCLE_DESIGN.md](LIFECYCLE_DESIGN.md) for architecture details**

## Features

### Core Capabilities
- **Lifecycle-Consistent Data**: Customers and facilities persist across periods with realistic evolution
- **Deterministic Generation**: Same seed produces identical outputs
- **Multiple Frequencies**: Yearly, Quarterly, and Monthly data generation
- **Realistic DPD Evolution**: DPD improves, worsens, or stabilizes based on configurable probabilities
- **Facility Settlement**: Loans settle based on maturity dates, high DPD, or product type
- **QA Rules Enforcement**: Banking domain rules (limits constant, restructured monotonic, etc.)
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

# Run with default settings (lifecycle-enabled, 10 rows for testing)
dotnet run

# Run with production settings
dotnet run --environment Production --all

# Generate specific configuration via CLI
dotnet run -- --freq monthly --start 2024-01 --months 12 --rows-per-file 10000
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

#### Lifecycle Settings (NEW)
Control how facilities persist and evolve:
```json
{
  "Lifecycle": {
    "FacilitySettlementRate": 0.05,
    "NewFacilityRate": 0.03,
    "MaxNewFacilitiesPerCustomer": 2,
    "ShortTermSettlementProbability": 0.95,
    "SettlementDpdThreshold": 180,
    "HighDpdSettlementProbability": 0.30
  }
}
```

#### DPD Evolution Settings (NEW)
Control realistic DPD progression:
```json
{
  "DpdEvolution": {
    "ImprovementProbability": 0.40,
    "WorseningProbability": 0.30,
    "ImprovementMean": -15.0,
    "ImprovementStdDev": 10.0,
    "WorseningMean": 20.0,
    "WorseningStdDev": 15.0,
    "CurrentStayCurrentProbability": 0.95
  }
}
```

#### QA Rules Settings (NEW)
Enforce banking domain constraints:
```json
{
  "QaRules": {
    "HybridAllowedProducts": ["LEASING"],
    "RevolvingProducts": ["CREDIT CARD", "OVERDRAFT"],
    "ShortTermProducts": ["BULLET", "OVERDRAFT"],
    "InterestInSuspenseDpdThreshold": 90,
    "EnforceLimitConstancy": true,
    "EnforceRestructuredMonotonicity": true
  }
}
```

#### Frequency Configuration

?? **IMPORTANT**: Lifecycle-consistent generation requires **exactly one frequency** to be enabled at a time. Enabling multiple frequencies (e.g., both Monthly and Yearly) will cause the application to fail at startup with a clear error message.

```json
{
  "Frequencies": {
    "Yearly": {
      "Enabled": false,  // Only ONE frequency can be enabled
      "Years": [2021, 2022, 2023, 2024, 2025],
      "FilesPerPeriodMin": 1,
      "FilesPerPeriodMax": 1
    },
    "Quarterly": {
      "Enabled": false,  // Set to true to generate quarterly data (disable others)
      "Years": [2021, 2022, 2023, 2024, 2025],
      "FilesPerPeriodMin": 1,
      "FilesPerPeriodMax": 1
    },
    "Monthly": {
      "Enabled": true,   // Currently enabled - others must be false
      "StartMonth": "2021-01",
      "MonthCount": 60,
      "FilesPerPeriodMin": 1,
      "FilesPerPeriodMax": 1
    }
  }
}
```

**Why only one frequency?** Lifecycle evolution requires sequential periods of the same granularity. Mixing monthly, quarterly, and yearly periods in a single run would break facility state tracking and DPD evolution logic. To generate data at different frequencies, run the application multiple times with different configurations.

**Example - Generating both Monthly and Yearly data:**
```bash
# Step 1: Configure Monthly and generate
# Edit appsettings.json: Monthly.Enabled=true, others=false
dotnet run --all

# Step 2: Configure Yearly and generate  
# Edit appsettings.json: Yearly.Enabled=true, others=false
dotnet run --all
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
4. Region
5. Product category
6. Segment
7. Segment for LGD
8. Industry
9. Earning Type
10. Nature
11. Grant date (yyyy-MM-dd)
12. Maturity date/ Expiry Date (yyyy-MM-dd)
13. Interest Rate
14. No. of Installments in Arrears
15. Total Remaining Installments (Including Installments in Arrears)
16. Installments Value
17. Installment Type (Monthly/ Quarterly/ Weekly/ Daily/ Annually/ Bullet)
18. Days Past Due
19. Limit
20. Total OS
21. Undisbursed Amount
22. Interest in Suspense
23. Collateral Type
24. Collateral Value
25. Rescheduled (Yes/No)
26. Restructured (Yes/No)
27. No. of Times Restructured
28. Upgraded to delinquency bucket (Yes/No)
29. Individually Impaired (Yes/No)
30. Bucketing in Individual Assessment
31. Period

## Data Consistency Rules

### Lifecycle Consistency (NEW)
- **Customer Numbers** remain constant across all periods
- **Facility Numbers** remain constant and linked to same customer
- **Master Data** (Product, Segment, Grant Date, Maturity Date, Limit) constant per facility
- **DPD** evolves realistically from previous period (not randomly regenerated)
- **Facilities** settle based on maturity, DPD, or product type
- **New facilities** added to portfolios over time

### Customer Consistency
- Customer attributes (Segment, Industry, Earning Type, Nature) remain constant across all periods
- Facility numbers are stable for each customer's facilities
- Customer numbers follow format: `CUST########`
- Same customer appears across periods with same attributes

### Financial Constraints
- **Limit** remains constant across periods for same facility
- **Interest Rate** remains constant across periods for same facility
- Grant Date ? Maturity Date
- Limit ? Total OS + Undisbursed Amount (in most cases)
- Interest rates vary by segment but remain constant per facility
- **Total OS** evolves with small changes (max ±15% per period)
- **Rare negative OS** values allowed (0.1% probability)
- **Interest in Suspense** depends on DPD (not random)

### Risk Assessment
- **DPD** evolves: 40% improvement, 30% worsening, 30% stability
- Risk flags probability increases with DPD
- **Restructured flags** can only increase (never decrease)
- Bucketing assessment derived from DPD and risk flags
- Facilities with DPD ? 180 may settle (30% probability)

## Lifecycle Features Deep Dive

### Customer & Facility Persistence
```
Period 2024-01:
  Customer CUST00000001 has facilities: FAC0000000101, FAC0000000102
  
Period 2024-02:
  Customer CUST00000001 still has: FAC0000000101, FAC0000000102
  (Same customer, same facilities, but evolved state)
  
Period 2024-03:
  FAC0000000101 settles (DPD was 200)
  Customer CUST00000001 now has: FAC0000000102
  Gets new facility: FAC0000000103
```

### DPD Evolution Example
```
Facility FAC0000000101:
  2024-01: DPD = 0    (current)
  2024-02: DPD = 0    (stayed current - 95% probability)
  2024-03: DPD = 15   (small delinquency)
  2024-04: DPD = 45   (worsened - 30% probability)
  2024-05: DPD = 30   (improved - 40% probability)
  2024-06: DPD = 15   (improved again)
  2024-07: DPD = 0    (back to current)
```

### Settlement Scenarios
1. **Maturity**: Facility reaches maturity date ? settles
2. **Short-term**: BULLET loan after 1 year ? 95% chance to settle
3. **High DPD**: Facility DPD ? 180 ? 30% chance to settle
4. **Random**: Any facility ? 5% base settlement rate

## Performance Tips

1. **Memory Usage**: The application streams data and uses minimal memory regardless of file size
2. **Disk I/O**: Uses large buffers (64KB) for optimal write performance
3. **Compression**: Enable gzip for 60-80% file size reduction with minimal performance impact
4. **Parallel Generation**: Run multiple instances with different seeds for parallel generation
5. **Lifecycle Overhead**: Minimal - lifecycle tracking uses efficient in-memory dictionaries

## Examples

### Generate Monthly Data with Lifecycle
```bash
dotnet run -- --freq monthly --start 2024-01 --months 12 --rows-per-file 100000
```

This will:
- Initialize facilities in 2024-01
- Evolve facilities month-by-month
- Show realistic DPD progression
- Settle some facilities, add new ones

### Generate Yearly Data for 2021-2025
```bash
dotnet run -- --freq yearly --years 2021,2022,2023,2024,2025 --files-per-period 1 --rows-per-file 10000
```

### Verify Lifecycle Consistency
```bash
# Generate test data
dotnet run -- --freq monthly --start 2024-01 --months 3 --rows-per-file 100

# Check same customer appears across periods
grep "CUST00000001" Output/Monthly/*/PD_*.csv

# Check same facility appears across periods
grep "FAC0000000101" Output/Monthly/*/PD_*.csv

# Check DPD evolution (column 18)
grep "FAC0000000101" Output/Monthly/*/PD_*.csv | cut -d',' -f18

# Check interest rate remains constant (column 13)
grep "FAC0000000101" Output/Monthly/*/PD_*.csv | cut -d',' -f13 | sort -u
# Should return only ONE value (same interest rate across all periods)
```

## Architecture

The application follows clean architecture principles with lifecycle extensions:

- **Program.cs**: Entry point with hosting and DI setup
- **Config/**: Configuration options classes
- **Domain/**: Core domain models (Customer, Facility, PeriodRow, FacilityMaster, FacilityState)
- **Services/**: Business logic services
  - `SeedDeriver`: Deterministic seed generation
  - `CustomerFactory`: Customer master data creation
  - `FacilityLifecycleManager`: **NEW** - Manages facility lifecycle across periods
  - `DpdEvolutionService`: **NEW** - Handles realistic DPD evolution
  - `LifecycleRowFactory`: **NEW** - Creates rows using lifecycle data
  - `PeriodRowFactory`: Original row factory (kept for reference)
  - `PeriodPlanner`: Work item planning with chronological ordering
  - `CsvWriterService`: CSV file writing
  - `RunGenerationService`: Main orchestration with lifecycle logic

## Documentation

- **[LIFECYCLE_DESIGN.md](LIFECYCLE_DESIGN.md)**: Comprehensive architecture and design documentation
- **[MIGRATION_GUIDE.md](MIGRATION_GUIDE.md)**: Upgrade guide from previous version
- **[IMPLEMENTATION_SUMMARY.md](IMPLEMENTATION_SUMMARY.md)**: Complete implementation details

## Troubleshooting

### Common Issues

1. **Out of Memory**: Ensure you're not trying to load all data in memory. The application streams by design.
2. **Slow Performance**: Check disk I/O and consider enabling compression for network storage.
3. **Inconsistent Data**: Verify the same seed is used for regeneration.
4. **File Access Errors**: Ensure output directory has write permissions.
5. **No Active Facilities**: Check lifecycle settings - settlement rate may be too high.

### Validation

To validate generated files:
1. ? Check row count matches configured `RowsPerFile`
2. ? Verify CSV headers match expected schema
3. ? Confirm dates are in yyyy-MM-dd format
4. ? Validate that financial constraints are met
5. ? **NEW**: Verify same customer/facility appears across periods
6. ? **NEW**: Check DPD evolves logically (not random)
7. ? **NEW**: Confirm Limit remains constant per facility
8. ? **NEW**: Confirm Interest Rate remains constant per facility

## Testing

### Smoke Tests
```bash
dotnet run --test
```

### Lifecycle Verification
```bash
# Generate small test dataset
dotnet run -- --freq monthly --start 2024-01 --months 3 --rows-per-file 50

# Verify customer persistence
cut -d',' -f1 Output/Monthly/2024-01/*.csv | sort -u > customers_jan.txt
cut -d',' -f1 Output/Monthly/2024-02/*.csv | sort -u > customers_feb.txt
diff customers_jan.txt customers_feb.txt  # Should be mostly identical

# Verify facility evolution
grep "FAC0000000101" Output/Monthly/*/PD_*.csv
```

## License

This project is provided as-is for educational and development purposes.