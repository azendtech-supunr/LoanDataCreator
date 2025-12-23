# Lifecycle-Consistent Data Generation

## Overview

This document describes the lifecycle-consistent synthetic data generation model introduced in this version. The generator now produces realistic loan portfolio data where **Customers** and **Facilities** persist across periods with stable identities and evolving states, instead of being randomly regenerated each period.

## Key Features

### 1. Stable Customer Identity
- **Customer Numbers** remain constant across all periods
- Customer attributes (Branch, Region, Industry, Earning Type, Segment) persist unchanged
- Same customers appear across multiple periods with consistent master data

### 2. Facility Lifecycle Management
- **Facility Numbers** are stable and unique per customer
- Each facility has a defined **start period** and optional **end period** (settlement)
- Facilities persist across periods unless settled
- Master data (Product Category, Segments, Nature, Collateral Type, Grant Date, Maturity Date, Limit) remains constant per facility

### 3. Realistic DPD Evolution
- **First period**: DPD generated using mixture model (normal + shock scenarios)
- **Subsequent periods**: DPD evolves based on previous period's value:
  - **40% probability** of improvement (DPD decreases)
  - **30% probability** of worsening (DPD increases)
  - **30% probability** of stability (DPD changes with time progression)
  - **Special handling** for current loans (DPD=0) with 95% chance of staying current
- Facilities can move from high DPD (61-90, 90+) to settled status

### 4. Settlement Logic
Facilities settle (are removed) based on:
- **Maturity date** reached (applies to all products)
- **Short Term Loan only**: Can settle early after 1 year (95% probability)
- **All other products**: Cannot settle before completing at least 1 year
- **After 1 year** (for non-Short Term Loan products):
  - **Short-term products** (BULLET, OVERDRAFT) settling after one year (95% probability)
  - **High DPD** (?180 days) facilities settling with 30% probability
  - **Random settlement** at 2.5% base rate per period
- **Important**: Facilities **never** settle in the same period they are created

### 5. New Facility Creation
- Each period, ~3% of customers receive new facilities
- New facilities added to existing customer portfolios
- Maximum 2 new facilities per customer per period

### 6. Financial Field Evolution
- **Limit**: Remains constant across periods (enforced by QA rules)
- **Interest Rate**: Remains constant across periods (uses BaseInterestRate from FacilityMaster)
- **Total OS**: Evolves with small changes (±15% max per period)
- **Rare negative OS** values allowed (0.1% probability, max 5% of limit)
- **Undisbursed Amount**: Recalculated based on remaining limit
- **Interest in Suspense**: Depends on DPD, not random

### 7. QA Rules Enforcement
- **Hybrid segments**: Only allowed for LEASING products
- **Revolving products**: CREDIT CARD, OVERDRAFT
- **Short-term products**: BULLET, OVERDRAFT (settle within one year)
- **Collateral mapping**: Appropriate collateral types per nature/product
- **Restructured flags**: Can only increase, never decrease (monotonicity)
- **Interest in Suspense**: Non-zero for DPD ? 90 days

## Configuration

All lifecycle behavior is fully configurable via `appsettings.json`. **No existing configuration options have been removed.**

### New Configuration Sections

#### Lifecycle Options
Controls facility lifecycle behavior:

```json
{
  "Lifecycle": {
    "FacilitySettlementRate": 0.05,          // 5% base settlement rate per period
    "NewFacilityRate": 0.03,                 // 3% of customers get new facilities per period
    "MaxNewFacilitiesPerCustomer": 2,        // Max new facilities per customer per period
    "ShortTermSettlementProbability": 0.95,  // 95% of short-term loans settle within 1 year
    "SettlementDpdThreshold": 180,           // DPD threshold for high-DPD settlement
    "HighDpdSettlementProbability": 0.30     // 30% of high-DPD facilities settle
  }
}
```

#### DPD Evolution Options
Controls realistic DPD progression:

```json
{
  "DpdEvolution": {
    "ImprovementProbability": 0.40,          // 40% chance DPD improves
    "WorseningProbability": 0.30,            // 30% chance DPD worsens
    "ImprovementMean": -15.0,                // Mean DPD decrease when improving
    "ImprovementStdDev": 10.0,               // Std dev for improvement
    "WorseningMean": 20.0,                   // Mean DPD increase when worsening
    "WorseningStdDev": 15.0,                 // Std dev for worsening
    "CurrentStayCurrentProbability": 0.95,   // 95% of current loans stay current
    "PeriodDaysIncrement": 30                // Days to add for time progression
  }
}
```

#### QA Rules Options
Enforces banking domain rules:

```json
{
  "QaRules": {
    "HybridAllowedProducts": ["LEASING"],
    "RevolvingProducts": ["CREDIT CARD", "OVERDRAFT"],
    "ShortTermProducts": ["BULLET", "OVERDRAFT"],
    "CollateralMapping": {
      "SECURED": ["REAL_ESTATE", "VEHICLE", "MACHINERY", "GOLD_JEWELRY", "FIXED_DEPOSITS"],
      "UNSECURED": ["OTHER", "CASH"]
    },
    "InterestInSuspenseDpdThreshold": 90,
    "EnforceLimitConstancy": true,
    "EnforceRestructuredMonotonicity": true
  }
}
```

#### Enhanced Amounts Options
New fields added to existing section:

```json
{
  "Amounts": {
    // ...existing fields...
    "InterestRateVolatility": 0.002,         // NOTE: No longer used - interest rates are now constant per facility
    "NegativeOsProbability": 0.001,          // 0.1% chance of negative OS
    "NegativeOsMaxFraction": 0.05,           // Max negative OS = 5% of limit
    "TotalOsMaxChangePerPeriod": 0.15        // Max ±15% change per period
  }
}
```

**Note**: The `InterestRateVolatility` configuration is no longer used in lifecycle-consistent generation. Interest rates remain constant for each facility across all periods, using the `BaseInterestRate` from `FacilityMaster`. This ensures consistency in line with real banking operations where loan interest rates are fixed at origination.

## Architecture

### New Services

1. **FacilityLifecycleManager**
   - Tracks all facility masters (immutable data)
   - Manages facility states per period
   - Handles facility creation, continuation, and settlement
   - Ensures customer-facility relationships persist

2. **DpdEvolutionService**
   - Evolves DPD from previous period
   - Handles improvement, worsening, and stability scenarios
   - Manages time progression (30/90/365 days for Monthly/Quarterly/Yearly)

3. **LifecycleRowFactory**
   - Creates period rows using lifecycle data
   - Combines FacilityMaster (immutable) with FacilityState (evolving)
   - Evolves financial fields and risk flags
   - Stores state for next period

### Domain Models

1. **FacilityMaster**: Immutable facility attributes (product, segment, dates, limit, collateral)
2. **FacilityState**: Period-specific state (DPD, Total OS, interest, flags, settlement status)
3. **PeriodInfo**: Period metadata (key, date, frequency, sequence)

## Frequency-Agnostic Design

The lifecycle logic operates in abstract "periods" and adapts to the configured frequency:

- **Monthly**: Periods are months (e.g., "2024-01"), DPD increments by ~30 days
- **Quarterly**: Periods are quarters (e.g., "2024Q1"), DPD increments by ~90 days
- **Yearly**: Periods are years (e.g., "2024"), DPD increments by ~365 days

The same lifecycle rules work for all frequencies without duplication.

## Generation Process

1. **Plan periods**: Get all periods in chronological order
2. **Initialize first period**: Create facilities for all customers
3. **Evolve subsequent periods**: 
   - Determine settlements based on rules
   - Create new facilities for some customers
   - Track active facilities per period
4. **Generate files**:
   - Process periods chronologically
   - For each file, sample from active facilities
   - Generate rows using lifecycle data (not random)
   - Evolve state from previous period
   - Store state for next period

## Backward Compatibility

### Preserved Functionality
? All existing configuration options remain
? Yearly, Quarterly, Monthly frequency support unchanged
? RowsPerFile, FilesPerPeriod, CustomerCount fully configurable
? All distributions (Branches, Products, Segments, etc.) preserved
? Command-line argument overrides still work
? Output format, file naming unchanged
? Gzip compression option preserved

### What Changed
- Generation now processes periods **chronologically** (required for lifecycle)
- Facilities are **sampled** to fill files (instead of iterating all customers)
- Data **evolves** instead of being randomly regenerated
- New services added, but old PeriodRowFactory kept for reference

## Example Scenarios

### Scenario 1: Short-Term Loan Settlement
- BULLET loan granted in 2024-01
- Appears in 2024-01 through 2024-12
- In 2025-01, has 95% chance of settling (removed from data)

### Scenario 2: DPD Evolution to Settlement
- Facility has DPD=150 in Period 1
- Period 2: Worsens to DPD=200
- Period 3: Exceeds SettlementDpdThreshold (180), has 30% chance of settlement

### Scenario 3: Customer with Evolving Portfolio
- Customer CUST00000042 exists in all periods
- Period 1: Has 3 facilities (FAC0000004201, FAC0000004202, FAC0000004203)
- Period 5: FAC0000004201 settles
- Period 8: Gets new facility FAC0000004204
- Period 10: Has 3 facilities (FAC0000004202, FAC0000004203, FAC0000004204)

## Testing

Run smoke tests to verify lifecycle logic:

```bash
dotnet run --test
```

Generate test data:

```bash
# Generate with default settings (appsettings.json)
dotnet run --all

# Generate specific frequency
dotnet run --freq monthly --start 2024-01 --months 12 --rows-per-file 1000
```

## Performance Considerations

- Lifecycle tracking uses in-memory dictionaries (efficient for 50K-100K customers)
- Chronological processing ensures correct evolution
- File generation remains parallelizable within a period
- Deterministic seeding ensures reproducibility

## Future Enhancements

Potential extensions (not implemented yet):
- Cross-frequency lifecycle (e.g., monthly evolving from quarterly)
- Customer-level lifecycle events (bankruptcy, mergers)
- Seasonal DPD patterns
- Macroeconomic shock scenarios
- Portfolio-wide correlation effects
