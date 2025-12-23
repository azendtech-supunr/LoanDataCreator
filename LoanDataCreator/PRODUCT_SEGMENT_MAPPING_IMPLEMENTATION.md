# Product-Segment Mapping Implementation

## Overview
This document describes the implementation of product category to segment mapping, which allows each product category to have its own set of PD and LGD segments with configurable weights.

## Changes Made

### 1. Configuration (appsettings.json)

#### Updated Product Categories
Replaced the old "Real Estate Loan" category with 7 new product categories:
- **Term Loan** (20%)
- **Overdraft** (15%)
- **Credit Cards** (10%)
- **Short Term Loan** (15%)
- **Lease** (15%)
- **Housing Loan** (10%)
- **Gold Loan** (15%)

#### Added ProductSegmentMapping Section
New configuration section that maps each product category to its specific segments:

```json
"ProductSegmentMapping": [
  {
    "ProductCategory": "Credit Cards",
    "Segments": [
      { "PdSegment": "CC- Classic", "LgdSegment": "CC- Classic", "Weight": 33.33 },
      { "PdSegment": "CC- Gold", "LgdSegment": "CC- Gold", "Weight": 33.33 },
      { "PdSegment": "CC- Platinum", "LgdSegment": "CC- Platinum", "Weight": 33.34 }
    ]
  },
  // ... more mappings
]
```

#### Segment Details by Product

**Credit Cards:**
- CC- Classic (PD/LGD: CC- Classic) - 33.33%
- CC- Gold (PD/LGD: CC- Gold) - 33.33%
- CC- Platinum (PD/LGD: CC- Platinum) - 33.34%

**Gold Loan:**
- Gold Loan (PD/LGD: Gold Loan) - 100%

**Housing Loan:**
- Housing Loan (PD/LGD: Housing Loan) - 100%

**Lease:**
- LE- Motor Car / Non-Hybrid - 40%
- LE- Other / LE- Other - 20%
- LE- Motor Car / Hybrid - 40%

**Overdraft:**
- Overdraft (PD/LGD: Overdraft) - 100%

**Short Term Loan:**
- Short Term Loan (PD/LGD: Short Term Loan) - 100%

**Term Loan:**
- Term Loan / Unsecured - 83.33%
- Term Loan / Secured - 16.67%

#### Updated Interest Rate Base by Segment
```json
"InterestRateBaseBySegment": {
  "CC- Classic": 0.18,
  "CC- Gold": 0.16,
  "CC- Platinum": 0.14,
  "Gold Loan": 0.10,
  "Housing Loan": 0.08,
  "LE- Motor Car": 0.12,
  "LE- Other": 0.13,
  "Overdraft": 0.15,
  "Short Term Loan": 0.14,
  "Term Loan": 0.12
}
```

#### Updated QA Rules
Updated product names to match new categories:
- HybridAllowedProducts: ["Lease"]
- RevolvingProducts: ["Credit Cards", "Overdraft"]
- ShortTermProducts: ["Short Term Loan", "Overdraft"]

### 2. Code Changes

#### Config/GenerationOptions.cs
**Added new configuration classes:**
- `ProductSegmentMappingOptions`: Main configuration container
- `ProductCategorySegments`: Maps a product category to its segments
- `SegmentInfo`: Defines PD segment, LGD segment, and weight

**Key Method:**
```csharp
public List<SegmentInfo> GetSegmentsForProduct(string productCategory)
```
Returns the available segments for a given product category.

#### Services/FacilityLifecycleManager.cs

**Constructor Update:**
Added `IOptions<ProductSegmentMappingOptions>` parameter to inject the product-segment mapping configuration.

**CreateFacilityMaster Method:**
Updated to:
1. Select a product category (existing logic)
2. Get available segments for that product category from the mapping
3. Sample a segment based on configured weights
4. Assign PdSegment and LgdSegment from the selected segment
5. Use PdSegment for interest rate calculation

**New Helper Method:**
```csharp
private static T SampleFromWeightedList<T>(Dictionary<T, double> weightedItems, Random random)
```
Generic method to sample from weighted items (used for segment selection).

#### Services/CustomerFactory.cs

**CreateCustomer Method:**
- Removed segment assignment at customer level
- Segments are now empty strings at customer level
- Segments are determined per facility based on product category

**Rationale:** Since each product category has its own segments, and a customer can have multiple facilities with different product categories, segments must be determined at the facility level, not the customer level.

#### Program.cs

**Configuration Registration:**
Added:
```csharp
builder.Services.Configure<ProductSegmentMappingOptions>(builder.Configuration);
```

## Architecture

### Old Approach
```
Customer ? Segment (single, random)
  ?? Facility 1 ? Uses customer's segment
  ?? Facility 2 ? Uses customer's segment
  ?? Facility 3 ? Uses customer's segment
```

### New Approach
```
Customer ? No segment
  ?? Facility 1 ? Product: "Credit Cards" ? Segment: "CC- Gold" (from product mapping)
  ?? Facility 2 ? Product: "Housing Loan" ? Segment: "Housing Loan" (from product mapping)
  ?? Facility 3 ? Product: "Lease" ? Segment: "LE- Motor Car/Hybrid" (from product mapping)
```

## Benefits

1. **Product-Specific Segments**: Each product category can have its own unique set of segments
2. **Flexible Mapping**: Different PD and LGD segments can be assigned (e.g., "LE- Motor Car" for PD, "Hybrid" for LGD)
3. **Weighted Distribution**: Control the distribution of segments within each product category
4. **Realistic Data**: Matches banking domain where segments are product-specific
5. **Maintains Lifecycle Consistency**: Segments remain constant for each facility across periods

## Data Consistency

### Preserved
- ? Facility segments remain constant across periods (stored in FacilityMaster)
- ? Customer identity remains constant
- ? Product category remains constant per facility
- ? DPD evolution continues to work
- ? Facility settlement logic unchanged

### Changed
- ? Customer-level segments are now empty (not used)
- ? Facility-level segments are product-specific

## Testing

### Build Status
? Build successful - all changes compile without errors

### Recommended Tests
1. Generate sample data and verify segments match product categories
2. Verify weights are distributed correctly (e.g., 33% each for Credit Card tiers)
3. Check that interest rates are applied based on PD segment
4. Confirm segments remain constant across periods for same facility
5. Validate CSV output includes correct segment values

## Example Output

For a customer with 3 facilities:

| Facility | Product | PD Segment | LGD Segment | Interest Rate Base |
|----------|---------|------------|-------------|-------------------|
| FAC0000000101 | Credit Cards | CC- Platinum | CC- Platinum | 14% |
| FAC0000000102 | Housing Loan | Housing Loan | Housing Loan | 8% |
| FAC0000000103 | Lease | LE- Motor Car | Hybrid | 12% |

## Migration Notes

For existing users:
1. Update `appsettings.json` with new `ProductSegmentMapping` section
2. Update product categories to new names
3. Update `InterestRateBaseBySegment` to use new segment names
4. Update `QaRules` product names if needed
5. Rebuild and test

## Future Enhancements

Potential improvements:
- Add validation to ensure all product categories have segment mappings
- Add configuration to allow customer-level segment fallback
- Support for time-varying segment mappings (e.g., new segments introduced in later periods)
- Validation that weights sum to 100% or normalize automatically
