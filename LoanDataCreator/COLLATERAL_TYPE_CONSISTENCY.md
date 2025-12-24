# Collateral Type and Value Consistency Implementation

## Overview
This document describes the implementation of business rules for Collateral Type and Collateral Value assignment, ensuring that:
1. Collateral types are correctly determined based on product category and segment for LGD
2. Collateral values follow banking business rules
3. Both collateral type and value remain consistent across all periods for each facility

## Business Rules Implemented

### Collateral Type Rules

#### Priority Order
The collateral type determination follows this priority order:
1. **Product Category specific rules** (highest priority)
2. **Segment for LGD rules**
3. **Special combinations for Lease products**

#### Specific Rules

**Product Category Rules:**
- **Overdraft**: Collateral Type = empty (no collateral)
- **Short Term Loan**: Collateral Type = empty (no collateral)
- **Credit Cards**: 
  - 70% probability: Collateral Type = empty
  - 30% probability: Collateral Type = "Fixed Deposit (Cash Collateral)"
- **Gold Loan**: Collateral Type = "Gold"
- **Housing Loan**: Collateral Type = "Property"

**Lease Product Rules:**
For products with category "Lease" or "Leasing", the collateral type is determined by the Segment for LGD:
- **Non-Hybrid**: Collateral Type = "Car Non-Hybrid"
- **Hybrid**: Collateral Type = "Car Hybrid"
- **LE-Other**: Collateral Type = "Machinery"
- **Default**: Collateral Type = "Machinery"

**Segment for LGD Rules (fallback for other products):**
- **Secured**: Collateral Type = "Fixed Deposit (Cash Collateral)"
- **Unsecured**: Collateral Type = "Personal Guarantee"

### Collateral Value Rules

#### Critical Business Rules

1. **Personal Guarantee or Empty Collateral Type:**
   - If Collateral Type is "Personal Guarantee" OR empty
   - Then Collateral Value = **0** (rendered as empty in CSV)

2. **Non-Empty Collateral Types (except Personal Guarantee):**
   - Collateral Value MUST be **higher than** the Total OS of the facility
   - Since Total OS can be up to 100% of Limit (typically ~75%)
   - Collateral Value is generated as a multiplier > 1.0 of Limit
   - This guarantees: **Collateral Value > Limit >= Total OS**

#### Multiplier Ranges by Product Type

- **Non-Revolving products**: 1.1x to 1.8x of Limit
  - Ensures collateral always exceeds Total OS
  - Typical range: 110% to 180% of limit

- **Mortgage/Housing Loan**: 1.2x to 1.6x of Limit
  - Real estate typically has higher collateral value
  - Typical range: 120% to 160% of limit

- **Revolving products**: 1.05x to 1.3x of Limit
  - Lower Total OS typical for revolving facilities
  - Typical range: 105% to 130% of limit

- **Other products**: 1.1x to 1.5x of Limit
  - General case for unlisted product types
  - Typical range: 110% to 150% of limit

#### Defensive Safety Margin

- After calculation, if Collateral Value <= Limit, it's adjusted to Limit × 1.05
- This provides an extra safety margin: **Collateral Value > Limit >= Total OS**

## Implementation Details

### Location: `FacilityLifecycleManager.cs`

#### Method: `GenerateCollateral`

**Signature:**
```csharp
private (string collateralType, decimal collateralValue) GenerateCollateral(
    string nature, 
    string productCategory, 
    string segmentForLGD, 
    decimal limit, 
    Random random)
```

**Key Logic:**

1. **Determine Collateral Type** (hierarchical rules)
2. **Calculate Collateral Value:**
   ```csharp
   if (collateralType is empty or "Personal Guarantee")
       collateralValue = 0
   else
       // Generate multiplier > 1.0 to ensure value > Total OS
       multiplier = random(1.05 to 1.8) depending on product
       collateralValue = limit × multiplier
       
       // Safety check
       if (collateralValue <= limit)
           collateralValue = limit × 1.05
   ```

### Consistency Mechanism

#### Storage in FacilityMaster

The collateral type and value are stored in the `FacilityMaster` record, which is **immutable** and persists across all periods:

```csharp
public record FacilityMaster(
    // ... other fields ...
    string CollateralType,    // Determined at facility creation
    decimal CollateralValue,  // Calculated based on business rules
    // ... other fields ...
);
```

#### Usage in LifecycleRowFactory

The `LifecycleRowFactory.CreateLifecycleRow` method retrieves the collateral information from `FacilityMaster`:

```csharp
public PeriodRow CreateLifecycleRow(string facilityNumber, PeriodInfo period, FacilityState? previousState)
{
    var master = _lifecycleManager.GetFacilityMaster(facilityNumber);
    
    // ... generate/evolve other fields ...
    
    return new PeriodRow(
        // ... other fields ...
        master.CollateralType,    // ? Consistent across all periods
        master.CollateralValue,   // ? Consistent across all periods
        // ... other fields ...
    );
}
```

## Validation Logic

### Ensuring Collateral Value > Total OS

The implementation ensures this constraint is **always** satisfied:

1. **At Generation Time:**
   - Collateral Value = Limit × Multiplier (where Multiplier > 1.0)
   - Since Total OS <= Limit (by definition)
   - Therefore: Collateral Value > Limit >= Total OS ?

2. **Across All Periods:**
   - Collateral Value is stored in immutable FacilityMaster
   - Total OS may vary across periods (but always <= Limit)
   - The inequality Collateral Value > Total OS remains valid ?

3. **Edge Cases:**
   - Negative Total OS: Collateral Value > 0 > Negative Total OS ?
   - Maximum Total OS (= Limit): Collateral Value > Limit ?
   - Zero collateral (Personal Guarantee/empty): Rule doesn't apply ?

## Benefits

1. **Business Rule Compliance**: 
   - Collateral types follow banking business rules based on product categories and segments
   - Collateral values respect the "must be higher than Total OS" constraint
   - Personal Guarantee and empty collateral have zero value (as expected)

2. **Lifecycle Consistency**: 
   - Once a facility is created, its collateral type and value remain constant across all periods
   - No random variation or recalculation in subsequent periods

3. **Data Quality**: 
   - Mathematically guaranteed that Collateral Value > Total OS
   - No need for post-generation validation or corrections

4. **Deterministic Generation**: 
   - Same facility will always have the same collateral (given the same seed)
   - Reproducible data generation for testing and debugging

## Testing Recommendations

### 1. Collateral Type Tests

- Verify Overdraft and Short Term Loan have empty collateral
- Verify Gold Loan has "Gold" collateral
- Verify Housing Loan has "Property" collateral
- Verify Credit Cards have 70/30 split between empty and Fixed Deposit
- Verify Lease products have correct collateral based on LGD segment
- Verify Secured segments ? "Fixed Deposit (Cash Collateral)"
- Verify Unsecured segments ? "Personal Guarantee"

### 2. Collateral Value Tests

**Zero Value Cases:**
- Personal Guarantee ? Collateral Value = 0
- Empty Collateral Type ? Collateral Value = 0

**Non-Zero Value Cases:**
- Collateral Value > Total OS (for all periods of the same facility)
- Collateral Value > Limit (defensive check)
- Multiplier ranges are correct by product type

### 3. Consistency Tests

- Generate multiple periods for the same facility
- Verify collateral type remains identical across all periods
- Verify collateral value remains identical across all periods
- Verify Collateral Value > Total OS in every period

### 4. Edge Case Tests

- Facilities with maximum Total OS (= Limit): Collateral Value > Limit
- Facilities with negative Total OS: Collateral Value > 0
- Revolving products with low utilization: Collateral Value still valid
- Personal Guarantee with high Total OS: Collateral Value = 0 (correct)

## Example Output

### Housing Loan Facility
```
Period  | Product       | Segment LGD  | Collateral Type | Collateral Value | Total OS    | Valid?
2021-01 | Housing Loan  | Housing Loan | Property        | 1,250,000.00     | 950,000.00  | ? (1.25M > 0.95M)
2021-02 | Housing Loan  | Housing Loan | Property        | 1,250,000.00     | 945,000.00  | ? (1.25M > 0.945M)
2021-03 | Housing Loan  | Housing Loan | Property        | 1,250,000.00     | 938,500.00  | ? (1.25M > 0.939M)
```

### Term Loan (Unsecured - Personal Guarantee)
```
Period  | Product    | Segment LGD | Collateral Type      | Collateral Value | Total OS    | Valid?
2021-01 | Term Loan  | Unsecured   | Personal Guarantee   | 0.00             | 780,000.00  | ? (rule doesn't apply)
2021-02 | Term Loan  | Unsecured   | Personal Guarantee   | 0.00             | 795,000.00  | ? (rule doesn't apply)
2021-03 | Term Loan  | Unsecured   | Personal Guarantee   | 0.00             | 802,000.00  | ? (rule doesn't apply)
```

### Overdraft Facility (No Collateral)
```
Period  | Product    | Segment LGD | Collateral Type | Collateral Value | Total OS   | Valid?
2021-01 | Overdraft  | Overdraft   | (empty)         | 0.00             | 45,000.00  | ? (rule doesn't apply)
2021-02 | Overdraft  | Overdraft   | (empty)         | 0.00             | 47,500.00  | ? (rule doesn't apply)
2021-03 | Overdraft  | Overdraft   | (empty)         | 0.00             | 43,200.00  | ? (rule doesn't apply)
```

### Lease Facility (Hybrid - with collateral)
```
Period  | Product | Segment PD     | Segment LGD | Collateral Type | Collateral Value | Total OS    | Valid?
2021-01 | Lease   | LE- Motor Car  | Hybrid      | Car Hybrid      | 1,150,000.00     | 850,000.00  | ? (1.15M > 0.85M)
2021-02 | Lease   | LE- Motor Car  | Hybrid      | Car Hybrid      | 1,150,000.00     | 865,000.00  | ? (1.15M > 0.865M)
2021-03 | Lease   | LE- Motor Car  | Hybrid      | Car Hybrid      | 1,150,000.00     | 880,000.00  | ? (1.15M > 0.88M)
```

### Term Loan (Secured - with Fixed Deposit)
```
Period  | Product    | Segment LGD | Collateral Type                    | Collateral Value | Total OS      | Valid?
2021-01 | Term Loan  | Secured     | Fixed Deposit (Cash Collateral)    | 2,750,000.00     | 1,950,000.00  | ? (2.75M > 1.95M)
2021-02 | Term Loan  | Secured     | Fixed Deposit (Cash Collateral)    | 2,750,000.00     | 1,985,000.00  | ? (2.75M > 1.985M)
2021-03 | Term Loan  | Secured     | Fixed Deposit (Cash Collateral)    | 2,750,000.00     | 2,020,000.00  | ? (2.75M > 2.02M)
```

## Migration Notes

For existing data:
- This change affects how collateral is generated at facility creation time
- Existing facilities will need to be regenerated to reflect the new rules
- No changes needed to configuration files
- The change is backward compatible with existing data structures

## Related Documentation
- See `LIFECYCLE_DESIGN.md` for overall lifecycle consistency architecture
- See `PRODUCT_SEGMENT_MAPPING_IMPLEMENTATION.md` for product-segment mapping details
