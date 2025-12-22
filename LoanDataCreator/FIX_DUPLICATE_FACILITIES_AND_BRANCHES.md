# Fix: Duplicate Facilities and Inconsistent Branches

## Problems Identified

### Problem 1: Facility Numbers Repeated in One File
**Issue**: The same facility appeared multiple times in a single CSV file because facilities were selected using random sampling WITH replacement.

**Example**:
```csv
CUST00000001,FAC0000000101,Colombo Main,...  ? Row 1
CUST00000001,FAC0000000101,Colombo Main,...  ? Row 50 (DUPLICATE!)
CUST00000001,FAC0000000101,Colombo Main,...  ? Row 150 (DUPLICATE!)
```

### Problem 2: Customers Appearing in Different Branches
**Issue**: Each facility of a customer was assigned a branch independently, causing customers to appear in different branches.

**Example**:
```csv
CUST00000001,FAC0000000101,Colombo Main,...   ? Customer 1, Facility 1
CUST00000001,FAC0000000102,Kandy,...          ? Customer 1, Facility 2 (WRONG BRANCH!)
CUST00000001,FAC0000000103,Galle,...          ? Customer 1, Facility 3 (WRONG BRANCH!)
```

## Root Causes

### Root Cause 1: Random Sampling Strategy
In `RunGenerationService.GenerateLifecycleRows()`:
```csharp
// BAD: Random sampling WITH replacement
while (generatedRows < workItem.Rows)
{
    var facilityNumber = activeFacilities[random.Next(activeFacilities.Count)];  // ? Can repeat!
    // Generate row...
}
```

This allowed the same facility to be selected multiple times for the same file.

### Root Cause 2: Branch Assigned at Facility Level
In `FacilityLifecycleManager.CreateFacilityMaster()`:
```csharp
// BAD: Each facility gets its own branch
var branch = SampleFromDistribution(_distributions.Branches, random);  // ? Different for each facility
```

Branch was facility-specific instead of customer-specific.

## Solutions Implemented

### Solution 1: Shuffle and Iterate (No Duplicates)

**Changed From**: Random sampling WITH replacement
**Changed To**: Shuffle facilities once, iterate sequentially

```csharp
// GOOD: Shuffle and iterate WITHOUT replacement
var shuffledFacilities = activeFacilities.OrderBy(_ => random.Next()).ToList();

var facilityIndex = 0;
while (generatedRows < workItem.Rows && facilityIndex < shuffledFacilities.Count)
{
    var facilityNumber = shuffledFacilities[facilityIndex];  // ? Each facility used at most once
    facilityIndex++;
    // Generate row...
}
```

**Benefits**:
- ? Each facility appears **at most once** per file
- ? No duplicate rows for the same facility
- ? Still randomized order (shuffled)
- ? Warning if not enough facilities for target row count

### Solution 2: Branch at Customer Level

**Step 1: Add Branch to CustomerMaster**
```csharp
public record CustomerMaster(
    string CustomerNumber,
    string Branch,           // ? NEW: Branch at customer level
    string Segment,
    string Industry,
    string EarningType,
    string Nature,
    int FacilityCount);
```

**Step 2: Assign Branch in CustomerFactory**
```csharp
public CustomerMaster CreateCustomer(int customerId)
{
    var random = _seedDeriver.CreateCustomerRandom(customerId);
    
    var branch = SampleFromDistribution(_distributions.Branches, random);  // ? Assigned once per customer
    
    return new CustomerMaster(
        customerNumber,
        branch,  // ? Stored in customer
        segment,
        industry,
        earningType,
        nature,
        facilityCount);
}
```

**Step 3: Use Customer's Branch for All Facilities**
```csharp
private FacilityMaster CreateFacilityMaster(int customerId, int facilityIndex, CustomerMaster customer, PeriodInfo period)
{
    var branch = customer.Branch;  // ? Use customer's branch (not generating new one)
    
    return new FacilityMaster(
        facilityNumber,
        customer.CustomerNumber,
        branch,  // ? All facilities of customer have same branch
        productCategory,
        ...);
}
```

**Benefits**:
- ? All facilities of a customer have the **same branch**
- ? Branch is consistent across all periods
- ? Branch is deterministic (same seed = same branch for customer)
- ? Matches real-world banking (customers don't switch branches per facility)

## Verification

### Verify No Duplicate Facilities in File
```bash
# Generate test data
dotnet run --all

# Check for duplicates in a file
cut -d',' -f2 Output/Monthly/2021-01/PD_2021-01_01.csv | sort | uniq -d

# Should return EMPTY (no duplicates)
```

### Verify Customer Branch Consistency
```bash
# Get all rows for customer 1
grep "CUST00000001" Output/Monthly/2021-01/PD_2021-01_01.csv | cut -d',' -f3 | sort -u

# Should return ONLY ONE branch (e.g., "Colombo Main")
```

### Verify Across Periods
```bash
# Check customer 1's branch across all periods
grep "CUST00000001" Output/Monthly/*/PD_*.csv | cut -d',' -f3 | sort -u

# Should return ONLY ONE branch
```

## Data Quality Rules

### Rule 1: Unique Facilities Per File
**Rule**: Each facility appears at most once in each CSV file
**Enforced By**: Sequential iteration through shuffled facility list
**Violation**: Previously allowed; now prevented

### Rule 2: Consistent Customer Branch
**Rule**: All facilities of a customer have the same branch across all periods
**Enforced By**: Branch assigned at customer level, stored in CustomerMaster
**Violation**: Previously violated; now enforced

## Impact on File Size

### Before Fix
With 250 customers × 4 facilities avg = 1000 unique facilities:
- **RowsPerFile**: 1000
- **Rows generated**: 1000 (with duplicates)
- **Unique facilities**: ~632 (following Poisson distribution)
- **Duplicate rate**: ~37%

### After Fix
With 250 customers × 4 facilities avg = 1000 unique facilities:
- **RowsPerFile**: 1000
- **Rows generated**: 1000 (NO duplicates)
- **Unique facilities**: 1000 (if enough facilities exist)
- **Duplicate rate**: 0% ?

### Warning When Insufficient Facilities
If RowsPerFile > active facilities:
```
warn: Only generated 950 rows out of 1000 for 2021-01 - ran out of unique facilities.
      Consider reducing RowsPerFile or increasing CustomerCount/FacilitiesPerCustomer.
```

## Configuration Recommendations

### For File Generation
To ensure files can be filled without running out of facilities:

```json
{
  "Customers": {
    "CustomerCount": 250,              // 250 customers
    "FacilitiesPerCustomerMin": 2,     // Min 2 facilities
    "FacilitiesPerCustomerMax": 6      // Max 6 facilities
  },
  "Generation": {
    "RowsPerFile": 1000                // Should be ? total facilities
  }
}
```

**Calculation**:
- Expected facilities: 250 customers × 4 avg = **1000 facilities**
- RowsPerFile: **1000** ? (matches, will use all unique facilities)

**Safe Range**:
- Min facilities: 250 × 2 = 500
- Max facilities: 250 × 6 = 1500
- **Recommended RowsPerFile**: 500 - 1500

### For Testing (Small Files)
```json
{
  "Customers": {
    "CustomerCount": 50,
    "FacilitiesPerCustomerMin": 2,
    "FacilitiesPerCustomerMax": 6
  },
  "Generation": {
    "RowsPerFile": 100    // Much smaller than ~200 facilities
  }
}
```

## Files Modified

| File | Change | Lines |
|------|--------|-------|
| `Domain/Models.cs` | Added Branch to CustomerMaster | +1 line |
| `Services/CustomerFactory.cs` | Assign branch at customer creation | +2 lines |
| `Services/FacilityLifecycleManager.cs` | Use customer's branch for facilities | ~3 lines |
| `Services/RunGenerationService.cs` | Shuffle facilities, iterate sequentially | ~15 lines |

## Build Status

? **Build Successful** - No errors, no warnings

## Testing Checklist

- [x] No duplicate facility numbers in single file
- [x] All facilities of a customer have same branch
- [x] Branch consistent across all periods
- [x] Warning shown when not enough facilities
- [x] Random distribution still maintained (via shuffle)
- [x] Performance not degraded

---

**Status**: ? **FIXED AND VERIFIED**

**Root Causes**: 
1. Random sampling WITH replacement
2. Branch assigned per facility instead of per customer

**Solutions**: 
1. Shuffle + sequential iteration
2. Branch at customer level

**Impact**: 
- No duplicate facilities per file
- Customer branch consistency enforced
- Better data quality

**Date Fixed**: 2024

**Branch**: `feature/synthetic-data-lifecycle-consistency`
