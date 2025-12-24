# Bucketing in Individual Assessment Consistency Implementation

## Overview
This document describes the implementation ensuring that the 'Bucketing in Individual Assessment' column values remain consistent across all periods for a given facility, as per the business requirement.

## Business Requirement

**Requirement:**
> The 'Bucketing in Individual Assessment' column should be consistent across all periods for a given facility. Assign a value between 3 and 5 to this column only if the particular facility (record) has the value 'Yes' or 'No' in the 'Individually Impaired (Yes/No)' column. If 'Individually Impaired' is empty, then 'Bucketing in Individual Assessment' should also be empty.

## Implementation Approach

### Key Design Decision
Add a new field `BucketingInIndividualAssessmentValue` to **`FacilityMaster`** (which is immutable and persists across all periods).

This follows the same pattern successfully used for `Rescheduled`, `Restructured`, `UpgradedToDelinquencyBucket`, and `IndividuallyImpaired` fields.

**IMPORTANT:** The `BucketingInIndividualAssessmentValue` field characteristics:
- Values can be 3, 4, or 5 (equal probability)
- Value is only assigned when `IndividuallyImpaired` = "Yes" or "No"
- When `IndividuallyImpaired` is empty, this field is also empty
- The value remains consistent across all periods for each facility

### Architecture Changes

#### 1. Updated Domain Models (`Models.cs`)

**FacilityMaster - Added BucketingInIndividualAssessmentValue:**
```csharp
public record FacilityMaster(
    // ... existing fields ...
    string IndividuallyImpaired, // Individually Impaired status (consistent across all periods)
    int BucketingInIndividualAssessmentValue, // NEW: Bucketing value (3-5, or 0 for empty)
    string StartPeriod);
```

**Note:** We use `int` to store the value (0 for empty, 3-5 for actual values) which will be converted to string during row generation.

#### 2. Facility Lifecycle Manager (`FacilityLifecycleManager.cs`)

**Added `GenerateBucketingInIndividualAssessmentValue` Method:**
```csharp
/// <summary>
/// Generates the Bucketing in Individual Assessment value for a facility.
/// BUSINESS RULE: Value should be between 3-5, but ONLY when Individually Impaired is "Yes" or "No"
/// When Individually Impaired is empty, this value should also be empty (0)
/// Distribution: Equal probability (33.33%) for values 3, 4, and 5
/// </summary>
private static int GenerateBucketingInIndividualAssessmentValue(Random random, string individuallyImpaired)
{
    // Only assign value if Individually Impaired is "Yes" or "No"
    if (string.IsNullOrEmpty(individuallyImpaired))
    {
        return 0; // Empty value (0 represents empty)
    }
    
    // For facilities with Individually Impaired = "Yes" or "No"
    // Randomly assign a value between 3-5 (equal probability)
    return random.Next(3, 6); // Returns 3, 4, or 5
}
```

**Updated `CreateFacilityMaster` Method:**
```csharp
private FacilityMaster CreateFacilityMaster(...)
{
    // ... existing logic ...
    
    // BUSINESS RULE: Generate Individually Impaired status (consistent across all periods)
    var individuallyImpaired = GenerateIndividuallyImpairedStatus(random);

    // BUSINESS RULE: Generate Bucketing in Individual Assessment value (consistent across all periods)
    // Values are 3, 4, or 5 when Individually Impaired is "Yes" or "No"
    // Empty when Individually Impaired is empty
    var bucketingValue = GenerateBucketingInIndividualAssessmentValue(random, individuallyImpaired);

    return new FacilityMaster(
        // ... other fields ...
        individuallyImpaired,     // Store in immutable FacilityMaster
        bucketingValue,           // NEW: Store in immutable FacilityMaster
        period.PeriodKey);
}
```

#### 3. Lifecycle Row Factory (`LifecycleRowFactory.cs`)

**Updated `CreateLifecycleRow` Method:**
```csharp
// BUSINESS RULE: Bucketing in Individual Assessment value is stored in FacilityMaster (constant across all periods)
// Convert the int value to string (0 becomes empty, 3-5 becomes "3", "4", "5")
var bucketingInIndividualAssessment = master.BucketingInIndividualAssessmentValue == 0 
    ? string.Empty 
    : master.BucketingInIndividualAssessmentValue.ToString();
```

**Note:** The field name in `PeriodRow` remains `BucketingInIndividualAssessment` (existing field), we just populate it with the consistent value from `FacilityMaster`.

## Value Generation Logic

The `GenerateBucketingInIndividualAssessmentValue` method produces the following distribution:

| Individually Impaired | Bucketing Value | Probability | Description |
|----------------------|-----------------|-------------|-------------|
| "Yes" | 3 | ~33.33% | Equal probability |
| "Yes" | 4 | ~33.33% | Equal probability |
| "Yes" | 5 | ~33.33% | Equal probability |
| "No" | 3 | ~33.33% | Equal probability |
| "No" | 4 | ~33.33% | Equal probability |
| "No" | 5 | ~33.33% | Equal probability |
| Empty | Empty (0) | 100% | No value when Individually Impaired is empty |

**Distribution across all facilities:**
- Approximately 90% of facilities have Individually Impaired = "Yes" or "No" (~5% "Yes", ~85% "No")
  - Of these 90%, values are distributed equally: ~30% get "3", ~30% get "4", ~30% get "5"
- Approximately 10% of facilities have empty Individually Impaired
  - All of these have empty Bucketing value

## Consistency Mechanism

### Storage in FacilityMaster

The `BucketingInIndividualAssessmentValue` is stored in the `FacilityMaster` record, which is **immutable** and persists across all periods:

```csharp
// Generation at facility creation
var individuallyImpaired = GenerateIndividuallyImpairedStatus(random);  // e.g., "Yes"
var bucketingValue = GenerateBucketingInIndividualAssessmentValue(random, individuallyImpaired);  // e.g., 4

// Stored in immutable FacilityMaster
return new FacilityMaster(..., individuallyImpaired, bucketingValue, ...);
```

### Usage in Row Generation

The `LifecycleRowFactory` retrieves the value from `FacilityMaster` for all periods:

```csharp
// Period 1
var master = _lifecycleManager.GetFacilityMaster(facilityNumber);
var bucketing = master.BucketingInIndividualAssessmentValue == 0 
    ? string.Empty 
    : master.BucketingInIndividualAssessmentValue.ToString();  // e.g., "4"

// Period 2 (same facility)
var master = _lifecycleManager.GetFacilityMaster(facilityNumber);
var bucketing = master.BucketingInIndividualAssessmentValue == 0 
    ? string.Empty 
    : master.BucketingInIndividualAssessmentValue.ToString();  // Still "4"

// Period N (same facility)
var master = _lifecycleManager.GetFacilityMaster(facilityNumber);
var bucketing = master.BucketingInIndividualAssessmentValue == 0 
    ? string.Empty 
    : master.BucketingInIndividualAssessmentValue.ToString();  // Always "4"
```

## Benefits

1. **Business Rule Compliance**: 
   - Bucketing values are only assigned when Individually Impaired is "Yes" or "No"
   - Values are empty when Individually Impaired is empty
   - Values remain consistent across all periods for each facility

2. **Lifecycle Consistency**: 
   - Once a facility is created, its Bucketing value never changes
   - No evolution logic needed for Bucketing values

3. **Data Quality**: 
   - No inconsistencies across periods
   - Predictable and repeatable data generation

4. **Deterministic Generation**: 
   - Same facility will always have the same Bucketing value (given the same seed)
   - Reproducible for testing and debugging

5. **Simplified Logic**:
   - Single source of truth in `FacilityMaster`
   - Direct dependency on Individually Impaired status

## Testing Recommendations

### 1. Value Assignment Tests
- Verify that all facilities with IndividuallyImpaired = "Yes" have Bucketing values 3, 4, or 5
- Verify that all facilities with IndividuallyImpaired = "No" have Bucketing values 3, 4, or 5
- Verify that all facilities with empty IndividuallyImpaired have empty Bucketing values
- Verify approximately equal distribution of 3, 4, and 5 among facilities with non-empty IndividuallyImpaired

### 2. Consistency Tests
- Generate multiple periods for the same facility
- Verify Bucketing value remains identical across all periods
- Verify no random variation or changes across periods

### 3. Dependency Tests
- Verify relationship between IndividuallyImpaired and Bucketing values is maintained
- Test edge cases where IndividuallyImpaired changes (should not happen in production)

### 4. Edge Case Tests
- New facilities created in later periods: Bucketing value is generated and remains constant
- Settled facilities: Bucketing value remains constant even after settlement
- Multiple facilities for same customer: Each facility can have different Bucketing values

## Example Output

### Facility with IndividuallyImpaired = "Yes"
```
Period  | Facility Number | Individually Impaired | Bucketing in Individual Assessment
2021-01 | FAC0000000101   | Yes                   | 4
2021-02 | FAC0000000101   | Yes                   | 4
2021-03 | FAC0000000101   | Yes                   | 4
2021-04 | FAC0000000101   | Yes                   | 4
2021-05 | FAC0000000101   | Yes                   | 4
```

### Facility with IndividuallyImpaired = "No"
```
Period  | Facility Number | Individually Impaired | Bucketing in Individual Assessment
2021-01 | FAC0000000202   | No                    | 3
2021-02 | FAC0000000202   | No                    | 3
2021-03 | FAC0000000202   | No                    | 3
2021-04 | FAC0000000202   | No                    | 3
2021-05 | FAC0000000202   | No                    | 3
```

### Facility with IndividuallyImpaired = Empty
```
Period  | Facility Number | Individually Impaired | Bucketing in Individual Assessment
2021-01 | FAC0000000303   | (empty)               | (empty)
2021-02 | FAC0000000303   | (empty)               | (empty)
2021-03 | FAC0000000303   | (empty)               | (empty)
2021-04 | FAC0000000303   | (empty)               | (empty)
2021-05 | FAC0000000303   | (empty)               | (empty)
```

## Migration Notes

For existing users:
- This change affects the data model and requires regeneration of data
- No configuration changes needed (logic is hardcoded)
- The change is backward compatible with the CSV output structure
- The field is now deterministic and consistent across all periods

## Related Documentation
- See `LIFECYCLE_DESIGN.md` for overall lifecycle consistency architecture
- See `INDIVIDUALLY_IMPAIRED_CONSISTENCY.md` for the Individually Impaired field implementation
- See `RESCHEDULED_CONSISTENCY.md` for similar implementation pattern
- See `RESTRUCTURED_CONSISTENCY.md` for similar implementation pattern
- See `UPGRADED_TO_DELINQUENCY_BUCKET_CONSISTENCY.md` for similar implementation pattern
