# Nature Field Update - Revolving vs Non-Revolving

## Summary
Updated the `Nature` field to use "Revolving" and "Non-Revolving" values instead of "SECURED" and "UNSECURED", and implemented business logic to ensure that facilities with Revolving nature have empty Installment Type values.

## Changes Made

### 1. Configuration Update (`appsettings.json`)
- **Changed `Natures` distribution:**
  - Before: `"SECURED": 60.0, "UNSECURED": 40.0`
  - After: `"Revolving": 40.0, "Non-Revolving": 60.0`

- **Updated `CollateralMapping` in QA Rules:**
  - Before: `"SECURED": [...], "UNSECURED": [...]`
  - After: `"Non-Revolving": [...], "Revolving": [...]`

### 2. Facility Creation Logic (`FacilityLifecycleManager.cs`)
Updated `CreateFacilityMaster` method:
```csharp
// Generate installment type, but set to empty if Nature is Revolving
var installmentType = customer.Nature == "Revolving" 
    ? string.Empty 
    : SampleFromDistribution(_distributions.InstallmentTypes, random);
```

**Business Rule Enforced:**
- When Nature = "Revolving" ? InstallmentType = "" (empty)
- When Nature = "Non-Revolving" ? InstallmentType = randomly selected from distribution (Monthly, Quarterly, etc.)

### 3. Period Row Factory (`PeriodRowFactory.cs`)
- Updated `CreatePeriodRow` method with same logic as above
- Updated `GenerateCollateral` method to use new nature values:
  - "Non-Revolving" facilities get 100-150% collateral
  - "Revolving" facilities get 10-30% collateral

### 4. Smoke Tests (`SmokeTests.cs`)
- Updated test distribution to use `"Non-Revolving"` instead of `"SECURED"`

## Business Logic Rationale

### Why Nature = Revolving ? Empty Installment Type?
Revolving credit facilities (like credit cards and overdrafts) do not have fixed installment schedules. They allow borrowers to repeatedly borrow and repay within a credit limit without fixed repayment dates. Therefore, the Installment Type field (Monthly, Quarterly, etc.) is not applicable to revolving facilities.

### Nature Semantic Change
- **Old Model:** "SECURED" vs "UNSECURED" indicated collateral presence
- **New Model:** "Revolving" vs "Non-Revolving" indicates repayment structure
  - **Revolving:** Credit available on a continuous basis (e.g., credit cards, overdrafts)
  - **Non-Revolving:** Traditional term loans with fixed repayment schedule

### Collateral Handling
Collateral mapping now aligns with the new nature values:
- **Non-Revolving loans** typically have substantial collateral (REAL_ESTATE, VEHICLE, MACHINERY, etc.)
- **Revolving facilities** may have minimal or other types of collateral (OTHER, CASH)

## Data Validation

After generating data, verify:
1. ? All facilities with Nature = "Revolving" have empty InstallmentType
2. ? All facilities with Nature = "Non-Revolving" have a valid InstallmentType (Monthly, Quarterly, etc.)
3. ? Nature values are only "Revolving" or "Non-Revolving" (no other values)
4. ? Distribution matches configured weights (40% Revolving, 60% Non-Revolving)

## Testing

### Quick Test
```bash
# Generate small test dataset
dotnet run -- --freq monthly --start 2024-01 --months 2 --rows-per-file 100

# Verify Revolving facilities have empty InstallmentType (column 17)
# Nature is column 9, InstallmentType is column 17
awk -F',' '$9=="Revolving" && $17!="" {print "ERROR: " $0}' Output/Monthly/*/PD_*.csv

# Verify Non-Revolving facilities have InstallmentType
awk -F',' '$9=="Non-Revolving" && $17=="" {print "ERROR: " $0}' Output/Monthly/*/PD_*.csv
```

### Sample Data Check
```bash
# Count facilities by Nature and InstallmentType
awk -F',' 'NR>1 {print $9, $17}' Output/Monthly/*/PD_*.csv | sort | uniq -c
```

Expected output pattern:
```
  ### Revolving 
  ### Non-Revolving Annually
  ### Non-Revolving Bullet
  ### Non-Revolving Monthly
  ### Non-Revolving Quarterly
```

## Impact on Existing Data

### Breaking Changes
?? **This is a breaking change** if you have existing configurations or processes that depend on:
- Nature values being "SECURED" or "UNSECURED"
- All facilities having non-empty InstallmentType values

### Migration Path
If you need to maintain old behavior:
1. Revert `Natures` in `appsettings.json` to old values
2. Comment out the installment type empty check in both `FacilityLifecycleManager` and `PeriodRowFactory`

## Files Modified
1. `LoanDataCreator\appsettings.json`
2. `LoanDataCreator\Services\FacilityLifecycleManager.cs`
3. `LoanDataCreator\Services\PeriodRowFactory.cs`
4. `LoanDataCreator\Tests\SmokeTests.cs`

## Future Enhancements

Consider adding:
1. Configuration option to map specific product categories to Revolving nature
2. Validation rule to prevent Revolving products from having InstallmentType values
3. Enhanced collateral logic based on both Nature and ProductCategory
4. Reporting on Revolving vs Non-Revolving portfolio composition
