# Implementation Summary: Bucketing in Individual Assessment Consistency

## Overview
This document summarizes the implementation of the "Bucketing in Individual Assessment" consistency rule as per the business requirement.

## Business Requirement
> The 'Bucketing in Individual Assessment' column should be consistent across all periods for a given facility. Assign a value between 3 and 5 to this column only if the particular facility (record) has the value 'Yes' or 'No' in the 'Individually Impaired (Yes/No)' column. If 'Individually Impaired' is empty, then 'Bucketing in Individual Assessment' should also be empty.

## Changes Made

### 1. Domain Model Changes (`LoanDataCreator/Domain/Models.cs`)
- **Added** `BucketingInIndividualAssessmentValue` field to `FacilityMaster` record
- Type: `int` (0 for empty, 3-5 for actual values)
- This field is immutable and persists across all periods for a facility

### 2. Facility Lifecycle Manager (`LoanDataCreator/Services/FacilityLifecycleManager.cs`)
- **Added** `GenerateBucketingInIndividualAssessmentValue` method:
  - Returns 0 (empty) when `IndividuallyImpaired` is empty
  - Returns 3, 4, or 5 (equal probability) when `IndividuallyImpaired` is "Yes" or "No"
- **Updated** `CreateFacilityMaster` method:
  - Generates the bucketing value when facility is created
  - Stores the value in `FacilityMaster` for consistency across all periods

### 3. Lifecycle Row Factory (`LoanDataCreator/Services/LifecycleRowFactory.cs`)
- **Updated** `CreateLifecycleRow` method:
  - Retrieves `BucketingInIndividualAssessmentValue` from `FacilityMaster`
  - Converts int to string (0 ? empty, 3-5 ? "3", "4", "5")
  - Uses this consistent value for all periods of the facility

### 4. Documentation
- **Created** `BUCKETING_IN_INDIVIDUAL_ASSESSMENT_CONSISTENCY.md`:
  - Detailed explanation of the implementation
  - Business rule documentation
  - Examples and testing recommendations

## Implementation Details

### Value Generation Logic
```csharp
private static int GenerateBucketingInIndividualAssessmentValue(Random random, string individuallyImpaired)
{
    if (string.IsNullOrEmpty(individuallyImpaired))
    {
        return 0; // Empty when Individually Impaired is empty
    }
    
    return random.Next(3, 6); // Returns 3, 4, or 5 with equal probability
}
```

### Consistency Mechanism
1. **Generation**: Value is generated once when facility is created
2. **Storage**: Stored in immutable `FacilityMaster` record
3. **Usage**: Retrieved from `FacilityMaster` for all periods
4. **Result**: Same facility always has the same bucketing value across all periods

## Distribution

| Individually Impaired | Bucketing Value | Overall Probability |
|----------------------|-----------------|---------------------|
| "Yes" (~5%)          | 3, 4, or 5      | ~1.67% each         |
| "No" (~85%)          | 3, 4, or 5      | ~28.33% each        |
| Empty (~10%)         | Empty           | ~10%                |

**Summary**:
- ~30% of all facilities have bucketing value "3"
- ~30% of all facilities have bucketing value "4"
- ~30% of all facilities have bucketing value "5"
- ~10% of all facilities have empty bucketing value

## Benefits
1. ? **Business Rule Compliance**: Values only assigned when Individually Impaired is "Yes" or "No"
2. ? **Period Consistency**: Same value across all periods for each facility
3. ? **Data Quality**: No inconsistencies or random variations
4. ? **Deterministic**: Reproducible with same seed
5. ? **Maintainable**: Single source of truth in `FacilityMaster`

## Testing Recommendations

### Consistency Tests
```
1. Generate data for multiple periods
2. Verify bucketing value remains constant for each facility
3. Check no random variations between periods
```

### Value Assignment Tests
```
1. Facilities with IndividuallyImpaired = "Yes" ? bucketing in {3, 4, 5}
2. Facilities with IndividuallyImpaired = "No" ? bucketing in {3, 4, 5}
3. Facilities with IndividuallyImpaired = empty ? bucketing = empty
4. Approximately equal distribution of 3, 4, and 5
```

### Edge Cases
```
1. New facilities in later periods: Value generated and remains constant
2. Settled facilities: Value persists even after settlement
3. Multiple facilities per customer: Each can have different values
```

## Example Output

### Example 1: Facility with IndividuallyImpaired = "Yes"
```
Period   | Facility     | Individually Impaired | Bucketing in Individual Assessment
---------|--------------|----------------------|---------------------------------
2021-01  | FAC00000001  | Yes                  | 4
2021-02  | FAC00000001  | Yes                  | 4
2021-03  | FAC00000001  | Yes                  | 4
```

### Example 2: Facility with IndividuallyImpaired = "No"
```
Period   | Facility     | Individually Impaired | Bucketing in Individual Assessment
---------|--------------|----------------------|---------------------------------
2021-01  | FAC00000002  | No                   | 3
2021-02  | FAC00000002  | No                   | 3
2021-03  | FAC00000002  | No                   | 3
```

### Example 3: Facility with IndividuallyImpaired = Empty
```
Period   | Facility     | Individually Impaired | Bucketing in Individual Assessment
---------|--------------|----------------------|---------------------------------
2021-01  | FAC00000003  | (empty)              | (empty)
2021-02  | FAC00000003  | (empty)              | (empty)
2021-03  | FAC00000003  | (empty)              | (empty)
```

## Related Files
- `LoanDataCreator/Domain/Models.cs` - Domain model with new field
- `LoanDataCreator/Services/FacilityLifecycleManager.cs` - Value generation logic
- `LoanDataCreator/Services/LifecycleRowFactory.cs` - Value usage in row generation
- `BUCKETING_IN_INDIVIDUAL_ASSESSMENT_CONSISTENCY.md` - Detailed documentation

## Migration Notes
- **Impact**: Data model changed, requires data regeneration
- **Configuration**: No configuration changes needed
- **Backward Compatibility**: CSV output structure remains unchanged
- **Deterministic**: Given same seed, generates same values

## Status
? **COMPLETED** - Implementation finished and build successful
