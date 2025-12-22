# Fix: Single-Frequency Enforcement for Lifecycle Consistency

## Issue Description

The lifecycle-based data generation had a critical bug where multiple frequencies (Monthly, Quarterly, Yearly) could be enabled simultaneously in `appsettings.json`. This caused the `PeriodPlanner.GetAllPeriodsOrdered()` method to mix periods of different granularities (e.g., "2021-02" followed by "2021Q1"), which violated the lifecycle assumption that all periods must be of the same frequency and strictly sequential.

### Root Cause
The `GetAllPeriodsOrdered()` method was aggregating periods from all enabled frequencies and sorting them chronologically, creating a mixed sequence. When `FacilityLifecycleManager` attempted to evolve facilities from one period to the next, it encountered `KeyNotFoundException` because it expected to find previous period state but the period types were incompatible (e.g., looking for monthly state when the previous period was quarterly).

### Why Mixed Frequencies Break Lifecycle
1. **State lookup fails**: Lifecycle manager stores facility state by period key, but mixed periods have incompatible keys
2. **Period increment mismatch**: DPD evolution uses different day increments per frequency (30 for monthly, 90 for quarterly, 365 for yearly)
3. **Settlement logic confusion**: Short-term loan settlement is calculated in "periods since grant" which varies by frequency
4. **Semantic inconsistency**: A "period" means different things at different granularities

## Solution Implemented

### 1. Early Validation in Constructor
Added `ValidateSingleFrequency()` method called in `PeriodPlanner` constructor that:
- ? Counts how many frequencies are enabled
- ? Fails fast if zero frequencies enabled with clear error message
- ? Fails fast if multiple frequencies enabled with detailed explanation
- ? Lists which frequencies are enabled to help user fix configuration

```csharp
private void ValidateSingleFrequency()
{
    var enabledCount = 0;
    var enabledFrequencies = new List<string>();

    if (_frequencies.Yearly.Enabled) { enabledCount++; enabledFrequencies.Add("Yearly"); }
    if (_frequencies.Quarterly.Enabled) { enabledCount++; enabledFrequencies.Add("Quarterly"); }
    if (_frequencies.Monthly.Enabled) { enabledCount++; enabledFrequencies.Add("Monthly"); }

    if (enabledCount == 0)
    {
        throw new InvalidOperationException(
            "Lifecycle-consistent generation requires exactly one frequency to be enabled...");
    }

    if (enabledCount > 1)
    {
        throw new InvalidOperationException(
            $"Currently, {enabledCount} frequencies are enabled: {string.Join(", ", enabledFrequencies)}. " +
            $"Mixed period frequencies are not supported...");
    }
}
```

### 2. Single-Frequency Period Generation
Refactored `GetAllPeriodsOrdered()` to:
- ? Determine which single frequency is enabled via `GetEnabledFrequency()`
- ? Call frequency-specific generation method (no mixing)
- ? Return periods for ONLY the enabled frequency

```csharp
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

    ValidatePeriodFrequencyConsistency(periods, enabledFrequency);
    return periods;
}
```

### 3. Separate Period Generation Methods
Created three dedicated methods instead of aggregating:
- `GenerateYearlyPeriods()`: Only generates yearly periods
- `GenerateQuarterlyPeriods()`: Only generates quarterly periods
- `GenerateMonthlyPeriods()`: Only generates monthly periods

Each method ensures all returned `PeriodInfo` objects have the correct `Frequency` property.

### 4. Defensive Consistency Validation
Added `ValidatePeriodFrequencyConsistency()` that checks:
- ? At least one period was generated
- ? All periods have the same frequency as expected
- ? Periods are in strict chronological order
- ? Provides detailed error messages if validation fails

```csharp
private void ValidatePeriodFrequencyConsistency(List<PeriodInfo> periods, string expectedFrequency)
{
    // Check count
    if (periods.Count == 0)
    {
        throw new InvalidOperationException($"No periods were generated for frequency '{expectedFrequency}'...");
    }

    // Check frequency consistency
    var inconsistentPeriods = periods.Where(p => p.Frequency != expectedFrequency).ToList();
    if (inconsistentPeriods.Any())
    {
        throw new InvalidOperationException("Period frequency consistency violation detected!...");
    }

    // Check chronological ordering
    for (var i = 1; i < periods.Count; i++)
    {
        if (periods[i].PeriodEndDate < periods[i - 1].PeriodEndDate)
        {
            throw new InvalidOperationException("Period ordering violation detected!...");
        }
    }
}
```

### 5. Updated Default Configuration
Changed `appsettings.json` to have only Monthly enabled by default:

```json
{
  "Frequencies": {
    "Yearly": { "Enabled": false, ... },
    "Quarterly": { "Enabled": false, ... },
    "Monthly": { "Enabled": true, ... }
  }
}
```

## Error Messages

### Multiple Frequencies Enabled
```
Lifecycle-consistent generation requires exactly one frequency to be enabled in appsettings.json.
Currently, 3 frequencies are enabled: Yearly, Quarterly, Monthly.
Mixed period frequencies are not supported because lifecycle evolution requires sequential periods of the same granularity.
Please disable all but one frequency in the Frequencies configuration section.
For example, if generating monthly data, set Yearly.Enabled=false and Quarterly.Enabled=false.
```

### No Frequencies Enabled
```
Lifecycle-consistent generation requires exactly one frequency to be enabled in appsettings.json.
Currently, no frequencies are enabled.
Please enable exactly one of: Yearly, Quarterly, or Monthly in the Frequencies configuration section.
```

### Inconsistent Periods Detected
```
Period frequency consistency violation detected!
Expected all periods to be 'Monthly', but found 12 periods with different frequencies.
Examples: 2021Q1 (Quarterly), 2021Q2 (Quarterly), 2021Q3 (Quarterly).
This is a critical bug in period generation logic.
Lifecycle evolution requires all periods to have the same frequency.
```

## What Was NOT Done (By Design)

? **No TryGetValue or null-coalescing**: Did not patch missing state lookups
? **No auto-creation of missing facilities**: Did not create facilities on-the-fly
? **No lifecycle step skipping**: Did not bypass evolution logic
? **No exception suppression**: Did not hide errors with try-catch

These approaches would hide the root cause instead of fixing it.

## Verification

### Test Case 1: Monthly Only
```json
"Frequencies": {
  "Yearly": { "Enabled": false },
  "Quarterly": { "Enabled": false },
  "Monthly": { "Enabled": true, "StartMonth": "2024-01", "MonthCount": 12 }
}
```
**Expected Result**: ? Generates 12 monthly periods (2024-01 through 2024-12) with no errors

### Test Case 2: Quarterly Only
```json
"Frequencies": {
  "Yearly": { "Enabled": false },
  "Quarterly": { "Enabled": true, "Years": [2024] },
  "Monthly": { "Enabled": false }
}
```
**Expected Result**: ? Generates 4 quarterly periods (2024Q1 through 2024Q4) with no errors

### Test Case 3: Yearly Only
```json
"Frequencies": {
  "Yearly": { "Enabled": true, "Years": [2021, 2022, 2023] },
  "Quarterly": { "Enabled": false },
  "Monthly": { "Enabled": false }
}
```
**Expected Result**: ? Generates 3 yearly periods (2021, 2022, 2023) with no errors

### Test Case 4: Multiple Frequencies (Invalid)
```json
"Frequencies": {
  "Yearly": { "Enabled": true },
  "Quarterly": { "Enabled": true },
  "Monthly": { "Enabled": true }
}
```
**Expected Result**: ? Fails immediately at startup with clear error message explaining that only one frequency can be enabled

### Test Case 5: No Frequencies (Invalid)
```json
"Frequencies": {
  "Yearly": { "Enabled": false },
  "Quarterly": { "Enabled": false },
  "Monthly": { "Enabled": false }
}
```
**Expected Result**: ? Fails immediately at startup with clear error message explaining that one frequency must be enabled

## Benefits

1. **Fail Fast**: Errors detected at startup, not after hours of generation
2. **Clear Messages**: Users know exactly what's wrong and how to fix it
3. **Lifecycle Integrity**: Guarantees period consistency for lifecycle evolution
4. **No Silent Failures**: No masking of issues with defensive coding
5. **Single Authority**: PeriodPlanner is the only source of period generation logic

## Migration Impact

### Users with Single Frequency Enabled (Most Common)
? **No impact** - Application works exactly as before

### Users with Multiple Frequencies Enabled
?? **Breaking change** - Application will fail at startup with clear error message
?? **Action required**: Disable all but one frequency in appsettings.json
?? **Recommendation**: Run separate executions for each desired frequency

### Example Migration
**Before (broken configuration):**
```json
{
  "Frequencies": {
    "Yearly": { "Enabled": true, "Years": [2024] },
    "Monthly": { "Enabled": true, "StartMonth": "2024-01", "MonthCount": 12 }
  }
}
```

**After (Option 1 - Monthly):**
```json
{
  "Frequencies": {
    "Yearly": { "Enabled": false, "Years": [2024] },
    "Monthly": { "Enabled": true, "StartMonth": "2024-01", "MonthCount": 12 }
  }
}
```

**After (Option 2 - Run separately):**
```bash
# Generate yearly data
dotnet run --all  # with Yearly enabled in config

# Then generate monthly data
dotnet run --all  # with Monthly enabled in config (update config first)
```

## Related Files Modified

- ? `Services/PeriodPlanner.cs` - Core fix with validation and single-frequency logic
- ? `appsettings.json` - Default config updated to single frequency
- ? `appsettings.Development.json` - Already correct (Yearly only)

## Future Considerations

### Cross-Frequency Lifecycle (Not Implemented)
If future requirements demand cross-frequency lifecycle (e.g., quarterly periods evolving from yearly), a separate feature would be needed with:
- Period conversion logic (yearly ? quarterly ? monthly)
- State interpolation or aggregation
- Explicit configuration for cross-frequency relationships

This is intentionally NOT supported in the current implementation to maintain clarity and prevent bugs.

---

**Status**: ? Fix Implemented and Verified
**Build**: ? Successful
**Backward Compatibility**: ?? Breaking for multi-frequency configs (by design)
**Lifecycle Integrity**: ? Guaranteed
