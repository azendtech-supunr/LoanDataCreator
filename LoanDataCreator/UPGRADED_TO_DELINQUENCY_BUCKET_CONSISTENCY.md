# Upgraded to Delinquency Bucket Consistency Implementation

## Overview
This document describes the implementation ensuring that the 'Upgraded to delinquency bucket' column values remain consistent across all periods for a given facility, as per the business requirement.

## Business Requirement

**Requirement:**
> The 'Upgraded to delinquency bucket' column should be randomly assigned values between 1 and 4, but only for the records where 'Rescheduled (Yes/No)' AND 'Restructured (Yes/No)' is marked as 'Yes'. Not all such records need a value—assign it only to some, randomly and keep others empty. The value should be consistent across all periods for a given facility.

## Implementation Approach

### Key Design Decision
Move `UpgradedToDelinquencyBucket` from **`FacilityState`** (which varies per period) to **`FacilityMaster`** (which is immutable and persists across all periods).

This follows the same pattern successfully used for `Rescheduled` and `Restructured` fields.

**IMPORTANT:** The `UpgradedToDelinquencyBucket` field is only populated when:
- `Rescheduled = "Yes"` AND `Restructured = "Yes"`
- Even when both conditions are met, only ~50% of facilities get a value assigned
- Values are randomly selected from 1, 2, 3, or 4
- The value remains consistent across all periods for the facility

### Architecture Changes

#### 1. Updated Domain Models (`Models.cs`)

**FacilityMaster - Added UpgradedToDelinquencyBucket:**
```csharp
public record FacilityMaster(
    // ... existing fields ...
    string Rescheduled,          // Rescheduled status (consistent across all periods)
    string Restructured,         // Restructured status (consistent across all periods)
    int NoOfTimesRestructured,   // Number of times restructured (consistent across all periods)
    string UpgradedToDelinquencyBucket, // NEW: Upgraded to delinquency bucket (consistent across all periods)
    string StartPeriod);
```

**FacilityState - Removed UpgradedToDelinquencyBucket:**
```csharp
public record FacilityState(
    string FacilityNumber,
    string Period,
    // ... other fields (UpgradedToDelinquencyBucket removed) ...
    string IndividuallyImpaired,    // UpgradedToDelinquencyBucket was here before
    string BucketingInIndividualAssessment,
    // ... remaining fields ...
    bool IsSettled);
```

#### 2. Facility Lifecycle Manager (`FacilityLifecycleManager.cs`)

**Added `GenerateUpgradedToDelinquencyBucket` Method:**
```csharp
/// <summary>
/// Generates the Upgraded to Delinquency Bucket value for a facility.
/// BUSINESS RULE: Value should be between 1-4, but ONLY for facilities where BOTH Rescheduled = "Yes" AND Restructured = "Yes"
/// Not all such records need a value - only ~50% of eligible facilities get a value assigned
/// Distribution: Empty (~50%), or 1, 2, 3, 4 (equal probability for the remaining ~50%)
/// </summary>
private static string GenerateUpgradedToDelinquencyBucket(Random random, string rescheduled, string restructured)
{
    // Only assign value if BOTH Rescheduled = "Yes" AND Restructured = "Yes"
    if (!rescheduled.Equals("Yes", StringComparison.OrdinalIgnoreCase) || 
        !restructured.Equals("Yes", StringComparison.OrdinalIgnoreCase))
    {
        return string.Empty;
    }
    
    // For eligible facilities (both Rescheduled and Restructured are "Yes")
    // Only ~50% will have a value assigned
    if (random.NextDouble() < 0.50)
    {
        // Randomly assign a value between 1-4
        return random.Next(1, 5).ToString(); // Returns "1", "2", "3", or "4"
    }
    
    return string.Empty;
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
    var (restructured, timesRestructured) = GenerateRestructuredStatus(random, rescheduled);

    // BUSINESS RULE: Generate Upgraded to Delinquency Bucket (consistent across all periods)
    // Only populated when BOTH Rescheduled = "Yes" AND Restructured = "Yes"
    // Value is between 1-4, but only ~50% of eligible facilities get a value
    var upgradedToDelinquencyBucket = GenerateUpgradedToDelinquencyBucket(random, rescheduled, restructured);

    return new FacilityMaster(
        // ... other fields ...
        rescheduled,          // Store in immutable FacilityMaster
        restructured,         // Store in immutable FacilityMaster
        timesRestructured,    // Store in immutable FacilityMaster
        upgradedToDelinquencyBucket, // NEW: Store in immutable FacilityMaster
        period.PeriodKey);
}
```

**Updated `CreateInitialFacilityState` Method:**
```csharp
// Removed UpgradedToDelinquencyBucket from GenerateRiskFlags return value
var (individuallyImpaired, bucketing) = 
    GenerateRiskFlags(daysPastDue, random);  // No longer returns upgraded field

return new FacilityState(
    // ... other fields ...
    individuallyImpaired,    // UpgradedToDelinquencyBucket removed from constructor
    bucketing,
    // ... remaining fields ...
);
```

**Updated `GenerateRiskFlags` Method:**
```csharp
// Changed return type to exclude UpgradedToDelinquencyBucket
private (string individuallyImpaired, string bucketing) GenerateRiskFlags(
    int daysPastDue, Random random)
{
    // ... logic ...
    // Removed upgraded generation
    
    // Bucketing logic without Upgraded
    var bucketing = (daysPastDue, individuallyImpaired) switch
    {
        ( >= 90, _) => "NPL",
        ( >= 30, _) => "Special Mention",
        (_, "Yes") => "Substandard",
        _ => "Standard"
    };
    
    return (individuallyImpaired, bucketing);
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

// BUSINESS RULE: Upgraded to Delinquency Bucket is stored in FacilityMaster (constant across all periods)
var upgraded = master.UpgradedToDelinquencyBucket;

// Generate or evolve risk flags (excluding Rescheduled, Restructured, and Upgraded)
string individuallyImpaired, bucketing;

if (previousState == null)
{
    // New facility - generate initial risk flags (passing restructured for bucketing)
    (individuallyImpaired, bucketing) = 
        GenerateRiskFlags(daysPastDue, restructured, random);
}
else
{
    // Existing facility - evolve risk flags (passing restructured for bucketing)
    (individuallyImpaired, bucketing) = 
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
        row.IndividuallyImpaired,    // UpgradedToDelinquencyBucket removed
        // ... remaining fields ...
        IsSettled: false);

    _lifecycleManager.StoreFacilityState(row.Period, state);
}
```

**Updated `GenerateRiskFlags` and `EvolveRiskFlags` Methods:**
```csharp
// Both methods updated to remove upgraded from return tuple
private (string individuallyImpaired, string bucketing) GenerateRiskFlags(
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
    
    return (individuallyImpaired, bucketing);
}

private (string individuallyImpaired, string bucketing) EvolveRiskFlags(
    int daysPastDue, string restructured, Random random)
{
    // ... logic ...
    // Uses Restructured from FacilityMaster for bucketing
    return (individuallyImpaired, bucketing);
}
```

## Value Distribution

The `GenerateUpgradedToDelinquencyBucket` method produces the following distribution:

### Eligibility Criteria
Only facilities where **BOTH** of the following conditions are met are eligible:
- `Rescheduled = "Yes"` (~45% of all facilities)
- `Restructured = "Yes"` (~20% of all facilities)

Estimated eligible facilities: ~9% of all facilities (45% × 20% = 9%)

### Value Distribution for Eligible Facilities
| Value | Probability | Description |
|-------|-------------|-------------|
| Empty | ~50% | No delinquency bucket assigned |
| "1" | ~12.5% | Delinquency bucket 1 |
| "2" | ~12.5% | Delinquency bucket 2 |
| "3" | ~12.5% | Delinquency bucket 3 |
| "4" | ~12.5% | Delinquency bucket 4 |

### Overall Distribution (All Facilities)
| Condition | Upgraded Value | Estimated % of All Facilities |
|-----------|----------------|-------------------------------|
| Rescheduled ? "Yes" OR Restructured ? "Yes" | Empty | ~91% |
| Both "Yes" AND randomly empty | Empty | ~4.5% |
| Both "Yes" AND value assigned | "1", "2", "3", or "4" | ~4.5% total (each bucket ~1.125%) |

This distribution ensures:
- Only facilities with both Rescheduled and Restructured as "Yes" can have a value
- Not all eligible facilities get a value (~50% remain empty)
- Values 1-4 are equally distributed among facilities that get a value
- The value remains consistent across all periods for each facility

## Consistency Mechanism

### Storage in FacilityMaster

The UpgradedToDelinquencyBucket value is stored in the `FacilityMaster` record, which is **immutable** and persists across all periods:

```csharp
// Generation at facility creation
var rescheduled = GenerateRescheduledStatus(random);  // Generated first
var (restructured, timesRestructured) = GenerateRestructuredStatus(random, rescheduled);  // Generated second
var upgradedToDelinquencyBucket = GenerateUpgradedToDelinquencyBucket(random, rescheduled, restructured);  // Generated based on both

// Stored in immutable FacilityMaster
return new FacilityMaster(..., rescheduled, restructured, timesRestructured, upgradedToDelinquencyBucket, ...);
```

### Usage in Row Generation

The `LifecycleRowFactory` retrieves the UpgradedToDelinquencyBucket value from `FacilityMaster` for all periods:

```csharp
// Period 1
var master = _lifecycleManager.GetFacilityMaster(facilityNumber);
var rescheduled = master.Rescheduled;                        // e.g., "Yes"
var restructured = master.Restructured;                      // e.g., "Yes"
var timesRestructured = master.NoOfTimesRestructured;        // e.g., 2
var upgraded = master.UpgradedToDelinquencyBucket;           // e.g., "3"

// Period 2 (same facility)
var master = _lifecycleManager.GetFacilityMaster(facilityNumber);
var rescheduled = master.Rescheduled;                        // Still "Yes"
var restructured = master.Restructured;                      // Still "Yes"
var timesRestructured = master.NoOfTimesRestructured;        // Still 2
var upgraded = master.UpgradedToDelinquencyBucket;           // Still "3"

// Period N (same facility)
var master = _lifecycleManager.GetFacilityMaster(facilityNumber);
var rescheduled = master.Rescheduled;                        // Always "Yes"
var restructured = master.Restructured;                      // Always "Yes"
var timesRestructured = master.NoOfTimesRestructured;        // Always 2
var upgraded = master.UpgradedToDelinquencyBucket;           // Always "3"
```

## Benefits

1. **Business Rule Compliance**: 
   - Values are only assigned when both Rescheduled and Restructured are "Yes"
   - Values are randomly selected from 1-4
   - Not all eligible facilities get a value (~50% randomness)
   - Values remain consistent across all periods for each facility

2. **Lifecycle Consistency**: 
   - Once a facility is created, its Upgraded value never changes
   - No evolution logic needed
   - Simplified codebase

3. **Data Quality**: 
   - No inconsistencies across periods
   - Predictable and repeatable data generation
   - Logical relationship with Rescheduled and Restructured fields

4. **Deterministic Generation**: 
   - Same facility will always have the same Upgraded value (given the same seed)
   - Reproducible for testing and debugging

5. **Simplified Logic**:
   - Removed complex evolution logic from `EvolveRiskFlags`
   - Single source of truth in `FacilityMaster`
   - No need for period-specific logic

## Testing Recommendations

### 1. Eligibility Tests
- Verify Upgraded is empty when Rescheduled ? "Yes"
- Verify Upgraded is empty when Restructured ? "Yes"
- Verify Upgraded is empty when both Rescheduled and Restructured are not "Yes"
- Verify Upgraded can only have values when BOTH Rescheduled = "Yes" AND Restructured = "Yes"

### 2. Value Distribution Tests
- For eligible facilities (both "Yes"), verify ~50% have empty Upgraded
- For eligible facilities with non-empty Upgraded, verify equal distribution of 1, 2, 3, 4
- Verify no values outside the range 1-4 (excluding empty)

### 3. Consistency Tests
- Generate multiple periods for the same facility
- Verify Upgraded value remains identical across all periods
- Verify no random variation or changes across periods
- Test facilities with different combinations of Rescheduled and Restructured

### 4. Edge Case Tests
- New facilities created in later periods: Upgraded is generated and remains constant
- Settled facilities: Upgraded remains constant even after settlement
- Multiple facilities for same customer: Each facility can have different Upgraded values
- Facility with Rescheduled = "Yes" and Restructured = "No" must have empty Upgraded
- Facility with Rescheduled = "No" and Restructured = "Yes" must have empty Upgraded
- Facility with both "Yes" and empty Upgraded should remain empty across all periods
- Facility with both "Yes" and Upgraded = "3" should remain "3" across all periods

## Example Output

### Facility with Both "Yes" and Upgraded = "3"
```
Period  | Facility Number | Rescheduled | Restructured | Upgraded Bucket | DPD | Bucketing
2021-01 | FAC0000000101   | Yes         | Yes          | 3               | 15  | Doubtful
2021-02 | FAC0000000101   | Yes         | Yes          | 3               | 18  | Doubtful
2021-03 | FAC0000000101   | Yes         | Yes          | 3               | 22  | Doubtful
```

### Facility with Both "Yes" but Empty Upgraded
```
Period  | Facility Number | Rescheduled | Restructured | Upgraded Bucket | DPD | Bucketing
2021-01 | FAC0000000202   | Yes         | Yes          | (empty)         | 0   | Doubtful
2021-02 | FAC0000000202   | Yes         | Yes          | (empty)         | 5   | Doubtful
2021-03 | FAC0000000202   | Yes         | Yes          | (empty)         | 10  | Doubtful
```

### Facility with Rescheduled "Yes" but Restructured "No"
```
Period  | Facility Number | Rescheduled | Restructured | Upgraded Bucket | DPD | Bucketing
2021-01 | FAC0000000303   | Yes         | No           | (empty)         | 10  | Standard
2021-02 | FAC0000000303   | Yes         | No           | (empty)         | 12  | Standard
2021-03 | FAC0000000303   | Yes         | No           | (empty)         | 8   | Standard
```

### Facility with Rescheduled "No" but Restructured "Yes"
```
Period  | Facility Number | Rescheduled | Restructured | Upgraded Bucket | DPD | Bucketing
2021-01 | FAC0000000404   | No          | Yes          | (empty)         | 5   | Doubtful
2021-02 | FAC0000000404   | No          | Yes          | (empty)         | 8   | Doubtful
2021-03 | FAC0000000404   | No          | Yes          | (empty)         | 3   | Doubtful
```

### Facility with Both "No"
```
Period  | Facility Number | Rescheduled | Restructured | Upgraded Bucket | DPD | Bucketing
2021-01 | FAC0000000505   | No          | No           | (empty)         | 5   | Standard
2021-02 | FAC0000000505   | No          | No           | (empty)         | 8   | Standard
2021-03 | FAC0000000505   | No          | No           | (empty)         | 3   | Standard
```

## Migration Notes

For existing users:
- This change affects the data model and requires regeneration of data
- No configuration changes needed
- Existing logic for other risk flags (IndividuallyImpaired, Bucketing) remains unchanged
- The change is backward compatible with the CSV output structure
- The field is now deterministic and consistent across all periods

## Related Documentation
- See `LIFECYCLE_DESIGN.md` for overall lifecycle consistency architecture
- See `RESCHEDULED_CONSISTENCY.md` for similar implementation pattern (Rescheduled field)
- See `RESTRUCTURED_CONSISTENCY.md` for similar implementation pattern (Restructured field)
- See `COLLATERAL_TYPE_CONSISTENCY.md` for other immutable field patterns
