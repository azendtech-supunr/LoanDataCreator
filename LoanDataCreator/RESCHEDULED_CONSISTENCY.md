# Rescheduled Consistency Implementation

## Overview
This document describes the implementation ensuring that the 'Rescheduled (Yes/No)' column values remain consistent across all periods for a given facility, as per the business requirement.

## Business Requirement

**Original Requirement:**
> The 'Rescheduled (Yes/No)' column should have the values 'Yes', 'No', or empty assigned randomly, and these values should be consistent across all periods for a given facility.

## Implementation Approach

### Key Design Decision
Move `Rescheduled` from **`FacilityState`** (which varies per period) to **`FacilityMaster`** (which is immutable and persists across all periods).

### Architecture Changes

#### 1. Updated Domain Models (`Models.cs`)

**FacilityMaster - Added Rescheduled:**
```csharp
public record FacilityMaster(
    // ... existing fields ...
    string Rescheduled,      // NEW: Rescheduled status (consistent across all periods)
    string StartPeriod);
```

**FacilityState - Removed Rescheduled:**
```csharp
public record FacilityState(
    string FacilityNumber,
    string Period,
    // ... other fields (Rescheduled removed) ...
    string Restructured,     // Rescheduled was here before
    int NoOfTimesRestructured,
    // ... remaining fields ...
    bool IsSettled);
```

#### 2. Facility Lifecycle Manager (`FacilityLifecycleManager.cs`)

**Added `GenerateRescheduledStatus` Method:**
```csharp
/// <summary>
/// Generates the Rescheduled status for a facility.
/// BUSINESS RULE: Rescheduled can be "Yes", "No", or empty (randomly assigned, consistent across periods).
/// Distribution: ~10% empty, ~45% "Yes", ~45% "No"
/// </summary>
private static string GenerateRescheduledStatus(Random random)
{
    var value = random.NextDouble();
    
    if (value < 0.10)
    {
        return string.Empty; // ~10% probability of empty
    }
    else if (value < 0.55)
    {
        return "Yes"; // ~45% probability of "Yes"
    }
    else
    {
        return "No"; // ~45% probability of "No"
    }
}
```

**Updated `CreateFacilityMaster` Method:**
```csharp
private FacilityMaster CreateFacilityMaster(...)
{
    // ... existing logic ...
    
    // BUSINESS RULE: Generate Rescheduled status (consistent across all periods for the facility)
    // Values can be "Yes", "No", or empty (empty has ~10% probability)
    var rescheduled = GenerateRescheduledStatus(random);

    return new FacilityMaster(
        // ... other fields ...
        rescheduled,       // Store in immutable FacilityMaster
        period.PeriodKey);
}
```

**Updated `CreateInitialFacilityState` Method:**
```csharp
// Removed Rescheduled from GenerateRiskFlags return value
var (restructured, timesRestructured, upgraded, individuallyImpaired, bucketing) = 
    GenerateRiskFlags(daysPastDue, random);  // No longer returns rescheduled

return new FacilityState(
    // ... other fields ...
    restructured,        // Rescheduled removed from constructor
    timesRestructured,
    // ... remaining fields ...
);
```

**Updated `GenerateRiskFlags` Method:**
```csharp
// Changed return type to exclude Rescheduled
private (string restructured, int timesRestructured, 
         string upgraded, string individuallyImpaired, string bucketing) GenerateRiskFlags(
    int daysPastDue, Random random)
{
    // ... logic ...
    // Removed rescheduled generation
    
    return (restructured, timesRestructured, upgraded, individuallyImpaired, bucketing);
}
```

#### 3. Lifecycle Row Factory (`LifecycleRowFactory.cs`)

**Updated `CreateLifecycleRow` Method:**
```csharp
// BUSINESS RULE: Rescheduled is stored in FacilityMaster (constant across all periods)
var rescheduled = master.Rescheduled;

// Generate or evolve risk flags (excluding Rescheduled which is now in FacilityMaster)
string restructured;
int timesRestructured;
string upgraded, individuallyImpaired, bucketing;

if (previousState == null)
{
    // New facility - generate initial risk flags (without rescheduled)
    (restructured, timesRestructured, upgraded, individuallyImpaired, bucketing) = 
        GenerateRiskFlags(daysPastDue, random);
}
else
{
    // Existing facility - evolve risk flags (without rescheduled)
    (restructured, timesRestructured, upgraded, individuallyImpaired, bucketing) = 
        EvolveRiskFlags(daysPastDue, previousState, random);
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
        row.Restructured,            // Rescheduled removed
        row.NoOfTimesRestructured,
        // ... remaining fields ...
        IsSettled: false);

    _lifecycleManager.StoreFacilityState(row.Period, state);
}
```

**Updated `GenerateRiskFlags` and `EvolveRiskFlags` Methods:**
```csharp
// Both methods updated to return tuple without Rescheduled
private (string restructured, int timesRestructured, 
         string upgraded, string individuallyImpaired, string bucketing) GenerateRiskFlags(...)
{
    // ... logic without rescheduled ...
    return (restructured, timesRestructured, upgraded, individuallyImpaired, bucketing);
}

private (string restructured, int timesRestructured, 
         string upgraded, string individuallyImpaired, string bucketing) EvolveRiskFlags(...)
{
    // ... logic without rescheduled ...
    return (restructured, timesRestructured, upgraded, individuallyImpaired, bucketing);
}
```

## Value Distribution

The `GenerateRescheduledStatus` method produces the following distribution:

| Value | Probability | Description |
|-------|-------------|-------------|
| Empty | ~10% | No rescheduling information |
| "Yes" | ~45% | Facility has been rescheduled |
| "No" | ~45% | Facility has not been rescheduled |

This distribution ensures:
- Realistic mix of rescheduled and non-rescheduled facilities
- Some facilities with missing/unknown rescheduling status (empty)
- Balanced distribution between "Yes" and "No"

## Consistency Mechanism

### Storage in FacilityMaster

The Rescheduled value is stored in the `FacilityMaster` record, which is **immutable** and persists across all periods:

```csharp
// Generation at facility creation
var rescheduled = GenerateRescheduledStatus(random);  // Generated once

// Stored in immutable FacilityMaster
return new FacilityMaster(..., rescheduled, ...);
```

### Usage in Row Generation

The `LifecycleRowFactory` retrieves the Rescheduled value from `FacilityMaster` for all periods:

```csharp
// Period 1
var master = _lifecycleManager.GetFacilityMaster(facilityNumber);
var rescheduled = master.Rescheduled;  // e.g., "Yes"

// Period 2 (same facility)
var master = _lifecycleManager.GetFacilityMaster(facilityNumber);
var rescheduled = master.Rescheduled;  // Still "Yes"

// Period N (same facility)
var master = _lifecycleManager.GetFacilityMaster(facilityNumber);
var rescheduled = master.Rescheduled;  // Always "Yes"
```

## Benefits

1. **Business Rule Compliance**: 
   - Rescheduled values are randomly assigned ("Yes", "No", or empty)
   - Values remain consistent across all periods for each facility

2. **Lifecycle Consistency**: 
   - Once a facility is created, its Rescheduled status never changes
   - No evolution logic needed for Rescheduled

3. **Data Quality**: 
   - No inconsistencies across periods
   - Predictable and repeatable data generation

4. **Deterministic Generation**: 
   - Same facility will always have the same Rescheduled value (given the same seed)
   - Reproducible for testing and debugging

5. **Simplified Logic**:
   - Removed complex evolution logic for Rescheduled from `EvolveRiskFlags`
   - Single source of truth in `FacilityMaster`

## Testing Recommendations

### 1. Value Distribution Tests
- Verify approximately 10% of facilities have empty Rescheduled
- Verify approximately 45% of facilities have Rescheduled = "Yes"
- Verify approximately 45% of facilities have Rescheduled = "No"

### 2. Consistency Tests
- Generate multiple periods for the same facility
- Verify Rescheduled value remains identical across all periods
- Verify no random variation or changes across periods

### 3. Edge Case Tests
- New facilities created in later periods: Rescheduled is generated and remains constant
- Settled facilities: Rescheduled remains constant even after settlement
- Multiple facilities for same customer: Each facility can have different Rescheduled values

## Example Output

### Facility with Rescheduled = "Yes"
```
Period  | Facility Number | Rescheduled | DPD | Restructured
2021-01 | FAC0000000101   | Yes         | 15  | No
2021-02 | FAC0000000101   | Yes         | 18  | No
2021-03 | FAC0000000101   | Yes         | 22  | No
```

### Facility with Rescheduled = "No"
```
Period  | Facility Number | Rescheduled | DPD | Restructured
2021-01 | FAC0000000202   | No          | 0   | No
2021-02 | FAC0000000202   | No          | 0   | No
2021-03 | FAC0000000202   | No          | 5   | No
```

### Facility with Rescheduled = Empty
```
Period  | Facility Number | Rescheduled | DPD | Restructured
2021-01 | FAC0000000303   | (empty)     | 10  | No
2021-02 | FAC0000000303   | (empty)     | 12  | No
2021-03 | FAC0000000303   | (empty)     | 8   | No
```

## Migration Notes

For existing users:
- This change affects the data model and requires regeneration of data
- No configuration changes needed
- Existing logic for other risk flags (Restructured, etc.) remains unchanged
- The change is backward compatible with the CSV output structure

## Related Documentation
- See `LIFECYCLE_DESIGN.md` for overall lifecycle consistency architecture
- See `COLLATERAL_TYPE_CONSISTENCY.md` for similar consistency patterns
- See `PRODUCT_SEGMENT_MAPPING_IMPLEMENTATION.md` for related immutable field patterns
