# Individually Impaired Consistency Implementation

## Overview
This document describes the implementation ensuring that the 'Individually Impaired (Yes/No)' column values remain consistent across all periods for a given facility, as per the business requirement.

## Business Requirement

**Requirement:**
> The 'Individually Impaired (Yes/No)' column should be randomly assigned for a few records. For example, if there are 100,000 records, approximately 5,000 should have the value 'Yes'. The possible values for this column are 'Yes', 'No', or empty. The value should be consistent across all periods for a given facility.

## Implementation Approach

### Key Design Decision
Move `IndividuallyImpaired` from **`FacilityState`** (which varies per period) to **`FacilityMaster`** (which is immutable and persists across all periods).

This follows the same pattern successfully used for `Rescheduled`, `Restructured`, and `UpgradedToDelinquencyBucket` fields.

**IMPORTANT:** The `IndividuallyImpaired` field characteristics:
- Approximately 5% of facilities have the value "Yes"
- Approximately 85% of facilities have the value "No"
- Approximately 10% of facilities have empty value
- The value remains consistent across all periods for each facility

### Architecture Changes

#### 1. Updated Domain Models (`Models.cs`)

**FacilityMaster - Added IndividuallyImpaired:**
```csharp
public record FacilityMaster(
    // ... existing fields ...
    string Rescheduled,          // Rescheduled status (consistent across all periods)
    string Restructured,         // Restructured status (consistent across all periods)
    int NoOfTimesRestructured,   // Number of times restructured (consistent across all periods)
    string UpgradedToDelinquencyBucket, // Upgraded to delinquency bucket (consistent across all periods)
    string IndividuallyImpaired, // NEW: Individually Impaired status (consistent across all periods)
    string StartPeriod);
```

**FacilityState - Removed IndividuallyImpaired:**
```csharp
public record FacilityState(
    string FacilityNumber,
    string Period,
    // ... other fields (IndividuallyImpaired removed) ...
    string BucketingInIndividualAssessment,
    // ... remaining fields ...
    bool IsSettled);
```

#### 2. Facility Lifecycle Manager (`FacilityLifecycleManager.cs`)

**Added `GenerateIndividuallyImpairedStatus` Method:**
```csharp
/// <summary>
/// Generates the Individually Impaired status for a facility.
/// BUSINESS RULE: Individually Impaired can be "Yes", "No", or empty (randomly assigned, consistent across periods).
/// Distribution: ~5% "Yes", ~85% "No", ~10% empty
/// Example: For 100,000 records, approximately 5,000 should have the value "Yes"
/// </summary>
private static string GenerateIndividuallyImpairedStatus(Random random)
{
    var value = random.NextDouble();
    
    if (value < 0.05)
    {
        return "Yes"; // ~5% probability of "Yes"
    }
    else if (value < 0.90)
    {
        return "No"; // ~85% probability of "No"
    }
    else
    {
        return string.Empty; // ~10% probability of empty
    }
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
    var upgradedToDelinquencyBucket = GenerateUpgradedToDelinquencyBucket(random, rescheduled, restructured);

    // BUSINESS RULE: Generate Individually Impaired status (consistent across all periods)
    // Values can be "Yes", "No", or empty (~5% "Yes", ~85% "No", ~10% empty)
    var individuallyImpaired = GenerateIndividuallyImpairedStatus(random);

    return new FacilityMaster(
        // ... other fields ...
        rescheduled,          // Store in immutable FacilityMaster
        restructured,         // Store in immutable FacilityMaster
        timesRestructured,    // Store in immutable FacilityMaster
        upgradedToDelinquencyBucket, // Store in immutable FacilityMaster
        individuallyImpaired, // NEW: Store in immutable FacilityMaster
        period.PeriodKey);
}
```

**Updated `CreateInitialFacilityState` Method:**
```csharp
private FacilityState CreateInitialFacilityState(FacilityMaster master, PeriodInfo period)
{
    // ... existing logic ...
    
    // Generate bucketing using IndividuallyImpaired from FacilityMaster
    var bucketing = GenerateBucketing(daysPastDue, master.IndividuallyImpaired, master.Restructured);

    return new FacilityState(
        master.FacilityNumber,
        period.PeriodKey,
        daysPastDue,
        totalOS,
        undisbursedAmount,
        master.BaseInterestRate,
        interestInSuspense,
        bucketing,              // IndividuallyImpaired removed from constructor
        IsSettled: false);
}
```

**Added `GenerateBucketing` Method:**
```csharp
/// <summary>
/// Generates the bucketing based on DPD, Individually Impaired, and Restructured status.
/// BUSINESS RULE: Bucketing depends on DPD thresholds, Individually Impaired status (from FacilityMaster), 
/// and Restructured status (from FacilityMaster).
/// </summary>
private static string GenerateBucketing(int daysPastDue, string individuallyImpaired, string restructured)
{
    return (daysPastDue, individuallyImpaired, restructured) switch
    {
        ( >= 90, _, _) => "NPL",
        ( >= 30, _, _) => "Special Mention",
        (_, "Yes", _) => "Substandard",
        (_, _, "Yes") => "Doubtful",
        _ => "Standard"
    };
}
```

#### 3. Lifecycle Row Factory (`LifecycleRowFactory.cs`)

**Updated `CreateLifecycleRow` Method:**
```csharp
// BUSINESS RULE: Individually Impaired is stored in FacilityMaster (constant across all periods)
var individuallyImpaired = master.IndividuallyImpaired;

// Generate bucketing based on DPD and immutable flags
string bucketing;

if (previousState == null)
{
    // New facility - generate initial bucketing
    bucketing = GenerateBucketing(daysPastDue, individuallyImpaired, restructured);
}
else
{
    // Existing facility - update bucketing based on current DPD
    bucketing = GenerateBucketing(daysPastDue, individuallyImpaired, restructured);
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
        row.BucketingInIndividualAssessment,  // IndividuallyImpaired removed
        // ... remaining fields ...
        IsSettled: false);

    _lifecycleManager.StoreFacilityState(row.Period, state);
}
```

**Added `GenerateBucketing` Method:**
```csharp
/// <summary>
/// Generates the bucketing based on DPD, Individually Impaired, and Restructured status.
/// BUSINESS RULE: Bucketing depends on DPD thresholds, Individually Impaired status (from FacilityMaster), 
/// and Restructured status (from FacilityMaster).
/// </summary>
private static string GenerateBucketing(int daysPastDue, string individuallyImpaired, string restructured)
{
    return (daysPastDue, individuallyImpaired, restructured) switch
    {
        ( >= 90, _, _) => "NPL",
        ( >= 30, _, _) => "Special Mention",
        (_, "Yes", _) => "Substandard",
        (_, _, "Yes") => "Doubtful",
        _ => "Standard"
    };
}
```

## Value Distribution

The `GenerateIndividuallyImpairedStatus` method produces the following distribution:

| Value | Probability | Description |
|-------|-------------|-------------|
| "Yes" | ~5% | Facility is individually impaired |
| "No" | ~85% | Facility is not individually impaired |
| Empty | ~10% | No impairment information |

**Example for 100,000 records:**
- Approximately 5,000 records will have "Yes"
- Approximately 85,000 records will have "No"
- Approximately 10,000 records will have empty value

This distribution ensures:
- Realistic representation of impaired facilities (~5%)
- Majority of facilities are not impaired (~85%)
- Some facilities with missing/unknown impairment status (empty ~10%)

## Consistency Mechanism

### Storage in FacilityMaster

The IndividuallyImpaired value is stored in the `FacilityMaster` record, which is **immutable** and persists across all periods:

```csharp
// Generation at facility creation
var individuallyImpaired = GenerateIndividuallyImpairedStatus(random);  // Generated once

// Stored in immutable FacilityMaster
return new FacilityMaster(..., individuallyImpaired, ...);
```

### Usage in Row Generation

The `LifecycleRowFactory` retrieves the IndividuallyImpaired value from `FacilityMaster` for all periods:

```csharp
// Period 1
var master = _lifecycleManager.GetFacilityMaster(facilityNumber);
var individuallyImpaired = master.IndividuallyImpaired;  // e.g., "Yes"

// Period 2 (same facility)
var master = _lifecycleManager.GetFacilityMaster(facilityNumber);
var individuallyImpaired = master.IndividuallyImpaired;  // Still "Yes"

// Period N (same facility)
var master = _lifecycleManager.GetFacilityMaster(facilityNumber);
var individuallyImpaired = master.IndividuallyImpaired;  // Always "Yes"
```

### Impact on Bucketing

The `IndividuallyImpaired` status affects the `BucketingInIndividualAssessment` column:
- If `IndividuallyImpaired = "Yes"`, the bucketing is set to "Substandard" (unless DPD >= 90 which gives "NPL", or DPD >= 30 which gives "Special Mention")
- The bucketing logic uses the consistent `IndividuallyImpaired` value from `FacilityMaster` across all periods

## Benefits

1. **Business Rule Compliance**: 
   - Individually Impaired values are randomly assigned ("Yes", "No", or empty)
   - Approximately 5% of facilities have "Yes" value
   - Values remain consistent across all periods for each facility

2. **Lifecycle Consistency**: 
   - Once a facility is created, its Individually Impaired status never changes
   - No evolution logic needed for Individually Impaired

3. **Data Quality**: 
   - No inconsistencies across periods
   - Predictable and repeatable data generation

4. **Deterministic Generation**: 
   - Same facility will always have the same Individually Impaired value (given the same seed)
   - Reproducible for testing and debugging

5. **Simplified Logic**:
   - Removed complex DPD-based generation logic from period-to-period evolution
   - Single source of truth in `FacilityMaster`
   - Bucketing now uses immutable flags for consistent categorization

## Testing Recommendations

### 1. Value Distribution Tests
- Verify approximately 5% of facilities have IndividuallyImpaired = "Yes"
- Verify approximately 85% of facilities have IndividuallyImpaired = "No"
- Verify approximately 10% of facilities have empty IndividuallyImpaired
- For 100,000 records, verify approximately 5,000 have "Yes"

### 2. Consistency Tests
- Generate multiple periods for the same facility
- Verify IndividuallyImpaired value remains identical across all periods
- Verify no random variation or changes across periods

### 3. Bucketing Impact Tests
- Verify facilities with IndividuallyImpaired = "Yes" and DPD < 30 have bucketing = "Substandard"
- Verify facilities with IndividuallyImpaired = "Yes" and DPD >= 30 have bucketing = "Special Mention"
- Verify facilities with IndividuallyImpaired = "Yes" and DPD >= 90 have bucketing = "NPL"
- Verify bucketing remains consistent when IndividuallyImpaired is constant

### 4. Edge Case Tests
- New facilities created in later periods: IndividuallyImpaired is generated and remains constant
- Settled facilities: IndividuallyImpaired remains constant even after settlement
- Multiple facilities for same customer: Each facility can have different IndividuallyImpaired values

## Example Output

### Facility with IndividuallyImpaired = "Yes"
```
Period  | Facility Number | Individually Impaired | DPD | Bucketing
2021-01 | FAC0000000101   | Yes                   | 15  | Substandard
2021-02 | FAC0000000101   | Yes                   | 18  | Substandard
2021-03 | FAC0000000101   | Yes                   | 22  | Substandard
2021-04 | FAC0000000101   | Yes                   | 35  | Special Mention
2021-05 | FAC0000000101   | Yes                   | 95  | NPL
```

### Facility with IndividuallyImpaired = "No"
```
Period  | Facility Number | Individually Impaired | DPD | Bucketing
2021-01 | FAC0000000202   | No                    | 0   | Standard
2021-02 | FAC0000000202   | No                    | 5   | Standard
2021-03 | FAC0000000202   | No                    | 10  | Standard
2021-04 | FAC0000000202   | No                    | 35  | Special Mention
2021-05 | FAC0000000202   | No                    | 95  | NPL
```

### Facility with IndividuallyImpaired = Empty
```
Period  | Facility Number | Individually Impaired | DPD | Bucketing
2021-01 | FAC0000000303   | (empty)               | 10  | Standard
2021-02 | FAC0000000303   | (empty)               | 12  | Standard
2021-03 | FAC0000000303   | (empty)               | 8   | Standard
2021-04 | FAC0000000303   | (empty)               | 35  | Special Mention
2021-05 | FAC0000000303   | (empty)               | 95  | NPL
```

### Facility with IndividuallyImpaired = "Yes" and Restructured = "Yes"
```
Period  | Facility Number | Individually Impaired | Restructured | DPD | Bucketing
2021-01 | FAC0000000404   | Yes                   | Yes          | 15  | Doubtful
2021-02 | FAC0000000404   | Yes                   | Yes          | 18  | Doubtful
2021-03 | FAC0000000404   | Yes                   | Yes          | 22  | Doubtful
2021-04 | FAC0000000404   | Yes                   | Yes          | 35  | Special Mention
2021-05 | FAC0000000404   | Yes                   | Yes          | 95  | NPL
```

**Note:** When Restructured = "Yes", the bucketing is "Doubtful" (unless DPD >= 30), which takes precedence over the "Substandard" bucketing from IndividuallyImpaired = "Yes".

## Migration Notes

For existing users:
- This change affects the data model and requires regeneration of data
- No configuration changes needed (distribution is hardcoded in the logic)
- Existing logic for bucketing has been updated to use the immutable IndividuallyImpaired field
- The change is backward compatible with the CSV output structure
- The field is now deterministic and consistent across all periods

## Configuration (Optional Enhancement)

While the current implementation uses hardcoded probabilities (~5% "Yes", ~85% "No", ~10% empty), future enhancements could add configuration support in `appsettings.json`:

```json
"IndividuallyImpaired": {
  "YesProbability": 0.05,
  "NoProbability": 0.85,
  "EmptyProbability": 0.10
}
```

This would allow users to adjust the distribution based on their specific requirements.

## Related Documentation
- See `LIFECYCLE_DESIGN.md` for overall lifecycle consistency architecture
- See `RESCHEDULED_CONSISTENCY.md` for similar implementation pattern (Rescheduled field)
- See `RESTRUCTURED_CONSISTENCY.md` for similar implementation pattern (Restructured field)
- See `UPGRADED_TO_DELINQUENCY_BUCKET_CONSISTENCY.md` for similar implementation pattern (Upgraded field)
- See `COLLATERAL_TYPE_CONSISTENCY.md` for other immutable field patterns
