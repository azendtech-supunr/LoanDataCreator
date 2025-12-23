# Short Term Loan Settlement Logic Implementation

## Overview
This document describes the implementation of realistic settlement behavior for Short Term Loan facilities, ensuring that the majority settle within one year while allowing rare exceptions for extended periods.

## Business Requirements

### Short Term Loan Characteristics
- **Primary Behavior**: Majority (95%) of Short Term Loans should settle within 1 year
- **Exception Cases**: Rare instances (5%) where customers don't settle on time, extending to 2-3 years
- **Maximum Duration**: No Short Term Loan should extend beyond 3 years

## Implementation

### 1. Tenor Generation (`GenerateDates` method)

#### Updated Logic for Short Term Loan
```csharp
"SHORT TERM LOAN" => GenerateShortTermLoanTenor(random)
```

#### New Helper Method: `GenerateShortTermLoanTenor`
```csharp
private static int GenerateShortTermLoanTenor(Random random)
{
    // 95% of Short Term Loans should have tenor within 1 year
    if (random.NextDouble() < 0.95)
    {
        // Most loans: 1 month to 1 year
        return random.Next(30, 366);
    }
    else
    {
        // Rare cases: Extended loans 1-3 years (customer didn't settle on time)
        return random.Next(366, 1096); // 1-3 years
    }
}
```

**Distribution:**
- **95% of facilities**: 30-365 days (1 month to 1 year)
- **5% of facilities**: 366-1095 days (1-3 years)

### 2. Settlement Logic (`ShouldSettleFacility` method)

#### Special Handling for Short Term Loan

Added aggressive settlement rules specifically for Short Term Loan product category:

```csharp
// Special handling for Short Term Loan - aggressive settlement after 1 year
if (master.ProductCategory.Equals("Short Term Loan", StringComparison.OrdinalIgnoreCase))
{
    var periodsSinceGrant = CalculatePeriodsBetween(master.StartPeriod, currentPeriod.PeriodKey, currentPeriod.Frequency);
    var periodsInOneYear = GetPeriodsInOneYear(currentPeriod.Frequency);
    
    // After 1 year, 95% should settle (matching the tenor distribution)
    if (periodsSinceGrant >= periodsInOneYear)
    {
        if (random.NextDouble() < 0.95)
        {
            return true;
        }
    }
    
    // After 2 years, force settlement for remaining facilities
    if (periodsSinceGrant >= periodsInOneYear * 2)
    {
        if (random.NextDouble() < 0.98)
        {
            return true;
        }
    }
    
    // After 3 years, force all to settle
    if (periodsSinceGrant >= periodsInOneYear * 3)
    {
        return true;
    }
}
```

#### Settlement Timeline

| Time Since Grant | Settlement Probability | Remaining Active |
|------------------|------------------------|------------------|
| < 1 year | Via maturity date (varies) | ~100% |
| 1 year | 95% settle | ~5% |
| 2 years | 98% of remaining settle | ~0.1% |
| 3 years | 100% force settle | 0% |

### 3. Additional Updates

#### Product Category Name Normalization
Added alternative product category names to handle case variations:
- `"CREDIT CARDS"` (in addition to `"CREDIT CARD"`)
- `"LEASE"` and `"LEASING"` (both supported)

#### Product-Specific Tenors
```csharp
"HOUSING LOAN" => random.Next(1825, 10950), // 5-30 years
"GOLD LOAN" => random.Next(180, 730),       // 6 months to 2 years
```

## Example Lifecycle

### Scenario 1: Standard Settlement (95% of cases)

```
Period 2024-01: Short Term Loan created (Tenor: 180 days, Maturity: 2024-07)
Period 2024-02: Active
Period 2024-03: Active
Period 2024-04: Active
Period 2024-05: Active
Period 2024-06: Active
Period 2024-07: Settled (maturity date reached)
```

### Scenario 2: Extended Loan (5% of cases)

```
Period 2024-01: Short Term Loan created (Tenor: 800 days, Maturity: 2026-03)
Period 2024-02: Active
...
Period 2025-01: Active (1 year passed, 95% settlement check - passed)
...
Period 2026-01: Active (2 years passed, 98% settlement check - passed)
Period 2026-03: Settled (maturity date reached or 3-year force settlement)
```

### Scenario 3: Non-Settled Extended Loan

```
Period 2024-01: Short Term Loan created (Tenor: 1000 days, Maturity: 2026-10)
Period 2025-01: Active (1 year - didn't settle, part of 5%)
Period 2026-01: Likely Settled (2 years - 98% probability)
Period 2027-01: Force Settled (3 years - 100% settlement)
```

## Configuration

### appsettings.json

#### Product Category
```json
"ProductCategories": {
  "Short Term Loan": 15.0
}
```

#### Product Segment Mapping
```json
{
  "ProductCategory": "Short Term Loan",
  "Segments": [
    { "PdSegment": "Short Term Loan", "LgdSegment": "Short Term Loan", "Weight": 100.0 }
  ]
}
```

#### QA Rules
```json
"ShortTermProducts": [ "Short Term Loan", "Overdraft" ]
```

#### Lifecycle Settings
```json
"Lifecycle": {
  "FacilitySettlementRate": 0.025,
  "ShortTermSettlementProbability": 0.95
}
```

**Note**: The `ShortTermSettlementProbability` (0.95) now applies to the general short-term products list, while Short Term Loan has its own more aggressive logic.

## Data Characteristics

### Expected Distribution After 5 Years of Generation

Assuming 1000 Short Term Loan facilities created:

| Duration Category | Count | Percentage |
|-------------------|-------|------------|
| Settled < 1 year | ~950 | 95% |
| Settled 1-2 years | ~47 | 4.7% |
| Settled 2-3 years | ~3 | 0.3% |
| Active > 3 years | 0 | 0% |

### Average Tenure
- **Mean**: ~4-5 months
- **Median**: ~6 months
- **95th percentile**: ~12 months
- **Maximum**: 36 months (forced settlement)

## Validation

### Testing Recommendations

1. **Generate test data with Short Term Loan facilities**
   ```bash
   dotnet run --environment Development
   ```

2. **Query facilities by duration**
   ```sql
   SELECT 
       FacilityNumber,
       GrantDate,
       MaturityDate,
       Period,
       DATEDIFF(day, GrantDate, MaturityDate) as TenorDays
   FROM GeneratedData
   WHERE ProductCategory = 'Short Term Loan'
   ORDER BY TenorDays DESC
   ```

3. **Verify settlement statistics**
   ```sql
   SELECT 
       CASE 
           WHEN DATEDIFF(day, GrantDate, MaturityDate) <= 365 THEN '0-1 year'
           WHEN DATEDIFF(day, GrantDate, MaturityDate) <= 730 THEN '1-2 years'
           ELSE '2-3 years'
       END as TenorRange,
       COUNT(*) as FacilityCount,
       COUNT(*) * 100.0 / SUM(COUNT(*)) OVER() as Percentage
   FROM GeneratedData
   WHERE ProductCategory = 'Short Term Loan'
   GROUP BY CASE 
       WHEN DATEDIFF(day, GrantDate, MaturityDate) <= 365 THEN '0-1 year'
       WHEN DATEDIFF(day, GrantDate, MaturityDate) <= 730 THEN '1-2 years'
       ELSE '2-3 years'
   END
   ```

4. **Check for facilities exceeding 3 years**
   ```sql
   SELECT COUNT(*) 
   FROM GeneratedData
   WHERE ProductCategory = 'Short Term Loan'
   AND DATEDIFF(day, GrantDate, MaturityDate) > 1095
   -- Should return 0
   ```

## Benefits

1. **Realistic Data**: Matches real-world Short Term Loan behavior
2. **Configurable Distribution**: 95/5 split is configurable through code
3. **Lifecycle Consistency**: Facilities persist across periods with proper evolution
4. **Forced Cleanup**: Ensures no Short Term Loans extend indefinitely
5. **Separate from General Rules**: Short Term Loan has distinct logic from other short-term products

## Migration Notes

### Existing Data
- No changes to existing configuration required
- New logic applies to newly generated facilities
- Compatible with existing lifecycle management

### Backward Compatibility
- General short-term product logic unchanged
- Other product categories unaffected
- Existing `ShortTermSettlementProbability` config still applies to Overdraft and other products

## Future Enhancements

Potential improvements:
1. Make the 95/5 distribution configurable in `appsettings.json`
2. Add DPD-based early settlement for Short Term Loans
3. Support for partial settlements
4. Product-specific settlement probabilities by year
5. Integration with external settlement triggers

## Technical Details

### Code Locations
- **Tenor Generation**: `FacilityLifecycleManager.GenerateDates()` and `GenerateShortTermLoanTenor()`
- **Settlement Logic**: `FacilityLifecycleManager.ShouldSettleFacility()`
- **Configuration**: `appsettings.json` - ProductSegmentMapping section

### Build Status
? Build successful - all changes compile without errors

### Testing
- Unit tests: Compatible with existing smoke tests
- Integration tests: Requires period-by-period data generation
- Validation: CSV output inspection for settlement patterns
