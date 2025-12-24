# Collateral Type Consistency Implementation

## Overview
This document describes the implementation of business rules for Collateral Type assignment, ensuring that collateral types are correctly determined based on product category, segment for LGD, and remain consistent across all periods for each facility.

## Business Rules Implemented

### Priority Order
The collateral type determination follows this priority order:
1. **Product Category specific rules** (highest priority)
2. **Segment for LGD rules**
3. **Special combinations for Lease products**

### Specific Rules

#### Product Category Rules
- **Overdraft**: Collateral Type = empty (no collateral)
- **Short Term Loan**: Collateral Type = empty (no collateral)
- **Credit Cards**: 
  - 70% probability: Collateral Type = empty
  - 30% probability: Collateral Type = "Fixed Deposit (Cash Collateral)"
- **Gold Loan**: Collateral Type = "Gold"
- **Housing Loan**: Collateral Type = "Property"

#### Lease Product Rules
For products with category "Lease" or "Leasing", the collateral type is determined by the Segment for LGD:
- **Non-Hybrid**: Collateral Type = "Car Non-Hybrid"
- **Hybrid**: Collateral Type = "Car Hybrid"
- **LE-Other**: Collateral Type = "Machinery"
- **Default**: Collateral Type = "Machinery"

#### Segment for LGD Rules (fallback for other products)
- **Secured**: Collateral Type = "Fixed Deposit (Cash Collateral)"
- **Unsecured**: Collateral Type = "Personal Guarantee"

### Collateral Value Rules
- If Collateral Type is **empty**, Collateral Value = 0
- Otherwise, Collateral Value is calculated as a multiplier of the Limit:
  - **Non-Revolving** products: 1.0x to 1.5x of Limit
  - **Mortgage/Housing Loan**: 1.2x to 1.5x of Limit
  - **Revolving** products: 0.1x to 0.3x of Limit
  - **Other** products: 0.5x to 0.8x of Limit

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

**Key Changes:**
1. Added `segmentForLGD` parameter to determine collateral type based on LGD segment
2. Implemented hierarchical business rules with proper priority
3. Set collateral value to 0 when collateral type is empty

### Consistency Mechanism

#### Storage in FacilityMaster
The collateral type and value are stored in the `FacilityMaster` record, which is **immutable** and persists across all periods:

```csharp
public record FacilityMaster(
    // ... other fields ...
    string CollateralType,
    decimal CollateralValue,
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

## Benefits

1. **Business Rule Compliance**: Collateral types follow banking business rules based on product categories and segments
2. **Lifecycle Consistency**: Once a facility is created, its collateral type and value remain constant across all periods
3. **Deterministic Generation**: Same facility will always have the same collateral (given the same seed)
4. **Maintainable Logic**: Clear hierarchy of rules makes it easy to update or add new rules

## Testing Recommendations

1. **Product Category Tests**:
   - Verify Overdraft and Short Term Loan have empty collateral
   - Verify Gold Loan has "Gold" collateral
   - Verify Housing Loan has "Property" collateral
   - Verify Credit Cards have 70/30 split between empty and Fixed Deposit

2. **Lease Product Tests**:
   - Verify LE-Motor Car/Non-Hybrid ? "Car Non-Hybrid"
   - Verify LE-Motor Car/Hybrid ? "Car Hybrid"
   - Verify LE-Other/LE-Other ? "Machinery"

3. **Segment for LGD Tests**:
   - Verify Secured segments ? "Fixed Deposit (Cash Collateral)"
   - Verify Unsecured segments ? "Personal Guarantee"

4. **Consistency Tests**:
   - Generate multiple periods for the same facility
   - Verify collateral type and value remain identical across all periods
   - Verify no random variation in collateral across periods

## Example Output

### Housing Loan Facility
```
Period      | Product       | Segment LGD  | Collateral Type | Collateral Value
2021-01     | Housing Loan  | Housing Loan | Property        | 1,250,000.00
2021-02     | Housing Loan  | Housing Loan | Property        | 1,250,000.00
2021-03     | Housing Loan  | Housing Loan | Property        | 1,250,000.00
```

### Credit Card Facility (with collateral)
```
Period      | Product       | Segment LGD   | Collateral Type                    | Collateral Value
2021-01     | Credit Cards  | CC- Gold      | Fixed Deposit (Cash Collateral)    | 15,000.00
2021-02     | Credit Cards  | CC- Gold      | Fixed Deposit (Cash Collateral)    | 15,000.00
2021-03     | Credit Cards  | CC- Gold      | Fixed Deposit (Cash Collateral)    | 15,000.00
```

### Overdraft Facility (no collateral)
```
Period      | Product       | Segment LGD | Collateral Type | Collateral Value
2021-01     | Overdraft     | Overdraft   | (empty)         | 0.00
2021-02     | Overdraft     | Overdraft   | (empty)         | 0.00
2021-03     | Overdraft     | Overdraft   | (empty)         | 0.00
```

### Lease Facility (Hybrid)
```
Period      | Product | Segment PD     | Segment LGD | Collateral Type | Collateral Value
2021-01     | Lease   | LE- Motor Car  | Hybrid      | Car Hybrid      | 850,000.00
2021-02     | Lease   | LE- Motor Car  | Hybrid      | Car Hybrid      | 850,000.00
2021-03     | Lease   | LE- Motor Car  | Hybrid      | Car Hybrid      | 850,000.00
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
