# Region Feature Implementation Summary

## Overview
This document describes the implementation of the Region feature, where branches are organized under regional hierarchies.

## What Changed

### 1. Configuration Structure
**Before:**
```json
"Distributions": {
  "Branches": {
    "Colombo Main": 15.0,
    "Kandy": 12.0,
    "Galle": 8.0,
    ...
  }
}
```

**After:**
```json
"Distributions": {
  "Regions": {
    "Western": {
      "Colombo Main": 15.0,
      "Negombo": 8.0,
      "Chilaw": 4.0
    },
    "Central": {
      "Kandy": 12.0,
      "Kurunegala": 7.0
    },
    "Southern": {
      "Galle": 8.0,
      "Matara": 5.0,
      "Tangalle": 3.0,
      "Hambantota": 5.0
    },
    ...
  }
}
```

### 2. Data Model Updates

#### CustomerMaster
- **Added:** `Region` field
- **Purpose:** Associates each customer with a geographic region based on their branch

#### FacilityMaster
- **Added:** `Region` field
- **Purpose:** Maintains region information as part of facility's immutable master data

#### PeriodRow
- **Added:** `Region` field
- **Purpose:** Includes region in the generated CSV output

### 3. CSV Output
The CSV now includes a "Region" column immediately after the "Branch" column:

**New Column Order:**
1. Customer Number
2. Facility number
3. Branch
4. **Region** ? NEW
5. Product category
6. Segment
7. Industry
... (remaining columns)

### 4. Implementation Details

#### DistributionsOptions
Added two helper methods for backward compatibility:

1. **GetAllBranches()**: Flattens all regions into a single branch distribution
   - If Regions is configured, merges all branch weights from all regions
   - Otherwise, returns the legacy Branches dictionary
   
2. **GetBranchToRegionMapping()**: Creates a lookup dictionary
   - Maps each branch name to its parent region
   - Returns empty if using legacy Branches configuration

#### CustomerFactory
- Uses `GetAllBranches()` to sample branches from the flattened distribution
- Uses `GetBranchToRegionMapping()` to assign the appropriate region
- If no region mapping exists, assigns "UNASSIGNED" as the region

#### Lifecycle Components
- **FacilityLifecycleManager**: Uses customer's region when creating facility masters
- **LifecycleRowFactory**: Includes region in generated period rows
- **PeriodRowFactory**: Updated to use customer's branch and region (consistency)

### 5. Backward Compatibility

The implementation maintains backward compatibility:

? **Old configuration still works**: If you keep using the flat `Branches` dictionary, the code will still work
- `GetAllBranches()` returns the Branches dictionary directly
- Region field will be set to "UNASSIGNED"

? **New configuration recommended**: Using `Regions` provides better organization and meaningful region data

### 6. Regional Distribution

The implemented regions follow Sri Lankan provinces:

| Region | Branches | Total Weight |
|--------|----------|--------------|
| Western | Colombo Main, Negombo, Chilaw | 27.0 |
| Central | Kandy, Kurunegala | 19.0 |
| Southern | Galle, Matara, Tangalle, Hambantota | 21.0 |
| North | Jaffna | 6.0 |
| North Central | Anuradhapura | 5.0 |
| Eastern | Batticaloa, Trincomalee, Kalmunai | 11.0 |
| Uva | Badulla | 5.0 |
| Sabaragamuwa | Ratnapura | 6.0 |

**Total Weight: 100.0** (normalized distribution)

## Testing

### Validation Steps
1. ? Build successful
2. ? All models updated with Region field
3. ? CSV headers include Region column
4. ? CustomerFactory assigns regions correctly
5. ? Lifecycle components propagate region data
6. ? Test suite updated

### Manual Testing
```bash
# Generate test data
dotnet run -- --freq monthly --start 2024-01 --months 3 --rows-per-file 100

# Verify Region column exists in output
head -n 1 Output/Monthly/2024-01/PD_2024-01_01.csv

# Check region values are populated
cut -d',' -f4 Output/Monthly/2024-01/PD_2024-01_01.csv | sort -u
```

Expected output should show regions like: Western, Central, Southern, etc.

## Migration Guide

### For Existing Users

**Option 1: Keep existing configuration (no changes needed)**
```json
"Distributions": {
  "Branches": { ... }  // Your existing branch config
}
```
Result: Region will be "UNASSIGNED" in output

**Option 2: Migrate to regional structure**
```json
"Distributions": {
  "Regions": {
    "YourRegion1": {
      "Branch1": weight,
      "Branch2": weight
    },
    "YourRegion2": {
      "Branch3": weight
    }
  }
}
```
Result: Proper region assignment based on branch

### Configuration Validation

? **Don't mix both:**
```json
"Distributions": {
  "Branches": { ... },  // Don't use both
  "Regions": { ... }    // at the same time
}
```

? **Use one or the other:**
```json
"Distributions": {
  "Regions": { ... }    // Recommended
}
```

## Benefits

1. **Better Data Organization**: Geographic hierarchy is now explicit
2. **Regional Analysis**: Easy to analyze loan portfolios by region
3. **Realistic Scenarios**: Reflects real-world bank branch structures
4. **Backward Compatible**: Existing configurations continue to work
5. **Flexible Weighting**: Different branches can have different weights within regions

## Files Modified

1. `Config/GenerationOptions.cs` - Added Regions property and helper methods
2. `Domain/Models.cs` - Added Region field to CustomerMaster, FacilityMaster, PeriodRow
3. `Services/CustomerFactory.cs` - Region assignment logic
4. `Services/FacilityLifecycleManager.cs` - Region propagation to facilities
5. `Services/LifecycleRowFactory.cs` - Region in row generation
6. `Services/PeriodRowFactory.cs` - Region in legacy row generation
7. `Services/CsvWriterService.cs` - Region column in CSV output
8. `Tests/SmokeTests.cs` - Test configuration with regions
9. `appsettings.json` - New regional structure
10. `REGION_FEATURE_SUMMARY.md` - This documentation

## Future Enhancements

Potential future improvements:
- Regional interest rate variations
- Regional risk profiles (different DPD distributions per region)
- Regional economic cycles (different evolution patterns)
- Regional product preferences
- Regional regulatory differences
