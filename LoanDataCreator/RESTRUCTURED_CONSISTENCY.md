# Restructured Consistency Implementation

## Overview
This document describes the implementation ensuring that the 'Restructured (Yes/No)' column values and 'No. of Times Restructured' remain consistent across all periods for a given facility, as per the business requirement.

## Business Requirement

**Original Requirement:**
> The 'Restructured (Yes/No)' column should have the values 'Yes', 'No', or empty assigned randomly, and these values should be consistent across all periods for a given facility.

**Additional Requirement:**
> The 'No. of Times Restructured' column should have a value between 1 and 3, but only if 'Rescheduled (Yes/No)' has the value 'Yes' for the given facility. The value should be consistent across all periods for a given facility.

## Implementation Approach

### Key Design Decision
Move `Restructured` and `NoOfTimesRestructured` from **`FacilityState`** (which varies per period) to **`FacilityMaster`** (which is immutable and persists across all periods).

This follows the same pattern successfully used for `Rescheduled` field.

**IMPORTANT:** The `NoOfTimesRestructured` field is tied to the `Rescheduled` field, not the `Restructured` field:
- If `Rescheduled = "Yes"`, then `NoOfTimesRestructured` is between 1-3
- If `Rescheduled = "No"` or empty, then `NoOfTimesRestructured = 0`
- The `Restructured` field is generated independently of `NoOfTimesRestructured`

### Architecture Changes

#### 1. Updated Domain Models (`Models.cs`)

**FacilityMaster - Added Restructured and NoOfTimesRestructured:**
```csharp
public record FacilityMaster(
    // ... existing fields ...
    string Rescheduled,          // Rescheduled status (consistent across all periods)
    string Restructured,         // NEW: Restructured status (consistent across all periods)
    int NoOfTimesRestructured,   // NEW: Number of times restructured (consistent across all periods, based on Rescheduled)
    string StartPeriod);
```

**FacilityState - Removed Restructured and NoOfTimesRestructured:**
```csharp
public record FacilityState(
    string FacilityNumber,
    string Period,
    // ... other fields (Restructured and NoOfTimesRestructured removed) ...
    string UpgradedToDelinquencyBucket,    // Restructured fields were here before
    string IndividuallyImpaired,
    // ... remaining fields ...
    bool IsSettled);
```

#### 2. Facility Lifecycle Manager (`FacilityLifecycleManager.cs`)

**Added `GenerateRestructuredStatus` Method:**
```csharp
/// <summary>
/// Generates the Restructured status and times restructured for a facility.
/// BUSINESS RULE: Restructured can be "Yes", "No", or empty (randomly assigned, consistent across periods).
/// Distribution: ~10% empty, ~20% "Yes", ~70% "No"
/// BUSINESS RULE: NoOfTimesRestructured should be between 1-3 ONLY if Rescheduled = "Yes"
/// Otherwise, NoOfTimesRestructured = 0
/// </summary>
private static (string restructured, int timesRestructured) GenerateRestructuredStatus(Random random, string rescheduled)
{
    var value = random.NextDouble();
    
    // Generate Restructured status
    string restructured;
    if (value < 0.10)
    {
        // ~10% probability of empty
        restructured = string.Empty;
    }
    else if (value < 0.30)
    {
        // ~20% probability of "Yes"
        restructured = "Yes";
    }
    else
    {
        // ~70% probability of "No"
        restructured = "No";
    }
    
    // Generate NoOfTimesRestructured based on Rescheduled status (NOT Restructured)
    int timesRestructured;
    if (rescheduled.Equals("Yes", StringComparison.OrdinalIgnoreCase))
    {
        // If Rescheduled = "Yes", generate times restructured between 1-3
        timesRestructured = random.Next(1, 4); // Returns 1, 2, or 3
    }
    else
    {
        // If Rescheduled = "No" or empty, NoOfTimesRestructured = 0
        timesRestructured = 0;
    }
    
    return (restructured, timesRestructured);
}
```

**Updated `CreateFacilityMaster` Method:**
```csharp
private FacilityMaster CreateFacilityMaster(...)
{
    // ... existing logic ...
    
    // BUSINESS RULE: Generate Rescheduled status (consistent across all periods)
    var rescheduled = GenerateRescheduledStatus(random);

    // BUSINESS RULE: Generate Restructured status (consistent across all periods)
    // NoOfTimesRestructured is based on Rescheduled status (1-3 if Rescheduled="Yes", 0 otherwise)
    var (restructured, timesRestructured) = GenerateRestructuredStatus(random, rescheduled);

    return new FacilityMaster(
        // ... other fields ...
        rescheduled,          // Store in immutable FacilityMaster
        restructured,         // Store in immutable FacilityMaster
        timesRestructured,    // Store in immutable FacilityMaster
        period.PeriodKey);
}
```

**Updated `CreateInitialFacilityState` Method:**
```csharp
// Removed Restructured and NoOfTimesRestructured from GenerateRiskFlags return value
var (upgraded, individuallyImpaired, bucketing) = 
    GenerateRiskFlags(daysPastDue, random);  // No longer returns restructured fields

return new FacilityState(
    // ... other fields ...
    upgraded,              // Restructured fields removed from constructor
    individuallyImpaired,
    // ... remaining fields ...
);
```

**Updated `GenerateRiskFlags` Method:**
```csharp
// Changed return type to exclude Restructured and NoOfTimesRestructured
// Bucketing logic simplified since Restructured is not available in this context
private (string upgraded, string individuallyImpaired, string bucketing) GenerateRiskFlags(
    int daysPastDue, Random random)
{
    // ... logic ...
    // Removed restructured generation
    
    // Simplified bucketing without Restructured
    var bucketing = (daysPastDue, individuallyImpaired) switch
    {
        ( >= 90, _) => "NPL",
        ( >= 30, _) => "Special Mention",
        (_, "Yes") => "Substandard",
        _ => "Standard"
    };
    
    return (upgraded, individuallyImpaired, bucketing);
}
```

#### 3. Lifecycle Row Factory (`LifecycleRowFactory.cs`)

**Updated `CreateLifecycleRow` Method:**
```csharp
// BUSINESS RULE: Rescheduled is stored in FacilityMaster (constant across all periods)
var rescheduled = master.Rescheduled;

// BUSINESS RULE: Restructured is stored in FacilityMaster (constant across all periods)
var restructured = master.Restructured;
var timesRestructured = master.NoOfTimesRestructured;

// Generate or evolve risk flags (excluding Rescheduled and Restructured)
string upgraded, individuallyImpaired, bucketing;

if (previousState == null)
{
    // New facility - generate initial risk flags (passing restructured for bucketing)
    (upgraded, individuallyImpaired, bucketing) = 
        GenerateRiskFlags(daysPastDue, restructured, random);
}
else
{
    // Existing facility - evolve risk flags (passing restructured for bucketing)
    (upgraded, individuallyImpaired, bucketing) = 
        EvolveRiskFlags(daysPastDue, restructured, random);
}
```

**Updated `StorePeriodState` Method:**
```csharp
public void StorePeriodState(PeriodRow row)
{
    var state = new FacilityState(
        row.FacilityNumber,
        row.Period,
        // ... other fields ...
        row.UpgradedToDelinquencyBucket,    // Restructured fields removed
        row.IndividuallyImpaired,
        // ... remaining fields ...
        IsSettled: false);

    _lifecycleManager.StoreFacilityState(row.Period, state);
}
```

**Updated `GenerateRiskFlags` and `EvolveRiskFlags` Methods:**
```csharp
// Both methods updated to take restructured as parameter and return tuple without it
private (string upgraded, string individuallyImpaired, string bucketing) GenerateRiskFlags(
    int daysPastDue, string restructured, Random random)
{
    // ... logic ...
    
    // Use Restructured from FacilityMaster for bucketing
    var bucketing = (daysPastDue, individuallyImpaired, restructured) switch
    {
        ( >= 90, _, _) => "NPL",
        ( >= 30, _, _) => "Special Mention",
        (_, "Yes", _) => "Substandard",
        (_, _, "Yes") => "Doubtful",
        _ => "Standard"
    };
    
    return (upgraded, individuallyImpaired, bucketing);
}

private (string upgraded, string individuallyImpaired, string bucketing) EvolveRiskFlags(
    int daysPastDue, string restructured, Random random)
{
    // ... logic ...
    // Uses Restructured from FacilityMaster for bucketing
    return (upgraded, individuallyImpaired, bucketing);
}
```

## Value Distribution

The `GenerateRestructuredStatus` method produces the following distribution:

### Restructured Status Distribution
| Value | Probability | Description |
|-------|-------------|-------------|
| Empty | ~10% | No restructuring information |
| "Yes" | ~20% | Facility has been restructured |
| "No" | ~70% | Facility has not been restructured |

### NoOfTimesRestructured Distribution (based on Rescheduled status)

**IMPORTANT:** The `NoOfTimesRestructured` field is controlled by the `Rescheduled` field, NOT the `Restructured` field:

| Rescheduled Value | NoOfTimesRestructured | Description |
|-------------------|----------------------|-------------|
| "Yes" | 1, 2, or 3 (random) | Facility has been rescheduled, may be restructured 1-3 times |
| "No" | 0 | Facility has not been rescheduled, no restructuring count |
| Empty | 0 | Unknown rescheduling status, no restructuring count |

This distribution ensures:
- Realistic mix where most facilities are not restructured (~70% have Restructured = "No")
- Small percentage with restructuring history (~20% have Restructured = "Yes")
- Some facilities with missing/unknown restructuring status (~10% have Restructured = empty)
- **NoOfTimesRestructured is between 1-3 when Rescheduled = "Yes" (~45% of facilities)**
- **NoOfTimesRestructured is 0 when Rescheduled = "No" or empty (~55% of facilities)**

## Consistency Mechanism

### Storage in FacilityMaster

The Restructured and NoOfTimesRestructured values are stored in the `FacilityMaster` record, which is **immutable** and persists across all periods:

```csharp
// Generation at facility creation
var rescheduled = GenerateRescheduledStatus(random);  // Generated first
var (restructured, timesRestructured) = GenerateRestructuredStatus(random, rescheduled);  // Generated with rescheduled context

// Stored in immutable FacilityMaster
return new FacilityMaster(..., rescheduled, restructured, timesRestructured, ...);
```

### Usage in Row Generation

The `LifecycleRowFactory` retrieves the Restructured values from `FacilityMaster` for all periods:

```csharp
// Period 1
var master = _lifecycleManager.GetFacilityMaster(facilityNumber);
var rescheduled = master.Rescheduled;                // e.g., "Yes"
var restructured = master.Restructured;              // e.g., "No"
var timesRestructured = master.NoOfTimesRestructured; // e.g., 2 (because Rescheduled = "Yes")

// Period 2 (same facility)
var master = _lifecycleManager.GetFacilityMaster(facilityNumber);
var rescheduled = master.Rescheduled;                // Still "Yes"
var restructured = master.Restructured;              // Still "No"
var timesRestructured = master.NoOfTimesRestructured; // Still 2

// Period N (same facility)
var master = _lifecycleManager.GetFacilityMaster(facilityNumber);
var rescheduled = master.Rescheduled;                // Always "Yes"
var restructured = master.Restructured;              // Always "No"
var timesRestructured = master.NoOfTimesRestructured; // Always 2
```

## Impact on QA Rules

### EnforceRestructuredMonotonicity Configuration

The `appsettings.json` configuration option `EnforceRestructuredMonotonicity` is now **deprecated** since:
- Restructured values are immutable and stored in FacilityMaster
- There is no evolution logic for Restructured anymore
- The "monotonicity" (can only increase, not decrease) is automatically enforced by immutability

**Old Behavior (Before):**
- Restructured could evolve from "No" to "Yes" based on DPD thresholds
- `EnforceRestructuredMonotonicity` controlled whether it could change

**New Behavior (After):**
- Restructured is generated once and never changes
- Always consistent across all periods
- No evolution or monotonicity logic needed

## Benefits

1. **Business Rule Compliance**: 
   - Restructured values are randomly assigned ("Yes", "No", or empty)
   - Values remain consistent across all periods for each facility
   - NoOfTimesRestructured is consistent with Rescheduled status

2. **Lifecycle Consistency**: 
   - Once a facility is created, its Restructured status never changes
   - No evolution logic needed for Restructured
   - Simplified codebase

3. **Data Quality**: 
   - No inconsistencies across periods
   - Predictable and repeatable data generation
   - Logical relationship between Rescheduled and NoOfTimesRestructured

4. **Deterministic Generation**: 
   - Same facility will always have the same Restructured values (given the same seed)
   - Reproducible for testing and debugging

5. **Simplified Logic**:
   - Removed complex evolution logic for Restructured from `EvolveRiskFlags`
   - Single source of truth in `FacilityMaster`
   - No need for `EnforceRestructuredMonotonicity` configuration

## Testing Recommendations

### 1. Value Distribution Tests
- Verify approximately 10% of facilities have empty Restructured
- Verify approximately 20% of facilities have Restructured = "Yes"
- Verify approximately 70% of facilities have Restructured = "No"
- **Verify NoOfTimesRestructured is between 1-3 when Rescheduled = "Yes"**
- **Verify NoOfTimesRestructured = 0 when Rescheduled = "No" or empty**

### 2. Consistency Tests
- Generate multiple periods for the same facility
- Verify Restructured value remains identical across all periods
- Verify NoOfTimesRestructured value remains identical across all periods
- Verify no random variation or changes across periods

### 3. Logical Relationship Tests
- **When Rescheduled = "Yes", NoOfTimesRestructured is between 1-3**
- **When Rescheduled = "No" or empty, NoOfTimesRestructured = 0**
- Restructured and NoOfTimesRestructured are independent (can have any combination)
- NoOfTimesRestructured never exceeds 3

### 4. Edge Case Tests
- New facilities created in later periods: Restructured is generated and remains constant
- Settled facilities: Restructured remains constant even after settlement
- Multiple facilities for same customer: Each facility can have different Restructured values
- **Facility with Rescheduled = "Yes" and Restructured = "No" should have NoOfTimesRestructured between 1-3**
- **Facility with Rescheduled = "No" and Restructured = "Yes" should have NoOfTimesRestructured = 0**

## Example Output

### Facility with Rescheduled = "Yes", Restructured = "Yes", NoOfTimesRestructured = 3
```
Period  | Facility Number | Rescheduled | Restructured | No. of Times | DPD | Bucketing
2021-01 | FAC0000000101   | Yes         | Yes          | 3            | 15  | Doubtful
2021-02 | FAC0000000101   | Yes         | Yes          | 3            | 18  | Doubtful
2021-03 | FAC0000000101   | Yes         | Yes          | 3            | 22  | Doubtful
```

### Facility with Rescheduled = "Yes", Restructured = "No", NoOfTimesRestructured = 2
```
Period  | Facility Number | Rescheduled | Restructured | No. of Times | DPD | Bucketing
2021-01 | FAC0000000202   | Yes         | No           | 2            | 0   | Standard
2021-02 | FAC0000000202   | Yes         | No           | 2            | 5   | Standard
2021-03 | FAC0000000202   | Yes         | No           | 2            | 10  | Standard
```

### Facility with Rescheduled = "No", Restructured = "Yes", NoOfTimesRestructured = 0
```
Period  | Facility Number | Rescheduled | Restructured | No. of Times | DPD | Bucketing
2021-01 | FAC0000000303   | No          | Yes          | 0            | 10  | Doubtful
2021-02 | FAC0000000303   | No          | Yes          | 0            | 12  | Doubtful
2021-03 | FAC0000000303   | No          | Yes          | 0            | 8   | Doubtful
```

### Facility with Rescheduled = "No", Restructured = "No", NoOfTimesRestructured = 0
```
Period  | Facility Number | Rescheduled | Restructured | No. of Times | DPD | Bucketing
2021-01 | FAC0000000404   | No          | No           | 0            | 5   | Standard
2021-02 | FAC0000000404   | No          | No           | 0            | 8   | Standard
2021-03 | FAC0000000404   | No          | No           | 0            | 3   | Standard
```

### Facility with Rescheduled = Empty, Restructured = Empty, NoOfTimesRestructured = 0
```
Period  | Facility Number | Rescheduled | Restructured | No. of Times | DPD | Bucketing
2021-01 | FAC0000000505   | (empty)     | (empty)      | 0            | 5   | Standard
2021-02 | FAC0000000505   | (empty)     | (empty)      | 0            | 8   | Standard
2021-03 | FAC0000000505   | (empty)     | (empty)      | 0            | 3   | Standard
```

## Migration Notes

For existing users:
- This change affects the data model and requires regeneration of data
- No configuration changes needed (but `EnforceRestructuredMonotonicity` is now deprecated)
- Existing logic for other risk flags (Upgraded, IndividuallyImpaired) remains unchanged
- The change is backward compatible with the CSV output structure
- Bucketing logic now uses the consistent Restructured value from FacilityMaster

## Related Documentation
- See `LIFECYCLE_DESIGN.md` for overall lifecycle consistency architecture
- See `RESCHEDULED_CONSISTENCY.md` for similar implementation pattern
- See `COLLATERAL_TYPE_CONSISTENCY.md` for other immutable field patterns
- See `PRODUCT_SEGMENT_MAPPING_IMPLEMENTATION.md` for related immutable field patterns
