# Fix: Data Consistency Issue in Lifecycle Evolution

## Problem Identified

```
System.InvalidOperationException: Facility FAC0000003203 was in active facilities 
for period 2021-02 but not found in period states. This indicates a data consistency issue.
```

## Root Cause

In `FacilityLifecycleManager.EvolveToPeriod()`, there were TWO places where states were not being created:

### Issue #1: Continuing Facilities
When a facility **continued** (didn't settle) from one period to the next:
1. ? Facility was added to `currentActiveFacilities` (HashSet)
2. ? **Facility state was NOT added to `currentStates` (Dictionary)**
3. Comment said: "State will be created during generation"

### Issue #2: New Facilities (Main Problem)
When a **new facility** was created in a period:
1. ? Facility master was created and added to `_facilityMasters`
2. ? Facility was added to `currentActiveFacilities`
3. ? **Initial state was NOT created for the facility**
4. Comment said: "State for new facilities will be created during first row generation"

### The Problem Flow

```
Period 2021-01:
  - FAC0000003202 created for customer 32 (facility 2)
  - Added to _activeFacilitiesByPeriod["2021-01"]
  - Added to _facilityStatesByPeriod["2021-01"]
  ? OK

Evolving to Period 2021-02:
  - FAC0000003202 continues (doesn't settle)
  - ? State copied forward (Fix #1)
  
  - FAC0000003203 created as NEW facility (customer 32, facility 3)
  - Added to _facilityMasters
  - Added to _activeFacilitiesByPeriod["2021-02"]
  - NOT added to _facilityStatesByPeriod["2021-02"]  ? BUG!
  
Evolving to Period 2021-03:
  - Tries to read previousStates["2021-02"][FAC0000003203]
  - KeyNotFoundException! ? CRASH
```

### Why This Happened

The original code assumed that states would be "created during generation" (i.e., when generating CSV rows). However:

1. **Evolution happens BEFORE generation** - All periods evolve first, then files are generated
2. **Next evolution needs state** - Period 2021-03's evolution needs period 2021-02's state
3. **State never created** - If state isn't created during evolution, it's missing when needed

## Solution Implemented

### Fix #1: Continuing Facilities

**Before** (Broken):
```csharp
else
{
    // Facility continues - add to active set
    currentActiveFacilities.Add(facilityNumber);
    // State will be created during generation  ? WRONG!
}
```

**After** (Fixed):
```csharp
else
{
    // Facility continues - add to active set
    currentActiveFacilities.Add(facilityNumber);
    
    // CRITICAL FIX: Copy the previous state to current period
    var continuedState = previousState with { Period = currentPeriod.PeriodKey };
    currentStates[facilityNumber] = continuedState;  ? FIXED!
}
```

### Fix #2: New Facilities (Main Fix)

**Before** (Broken):
```csharp
// Create new facility master
var newFacilityMaster = CreateFacilityMaster(customerId, nextIndex, customer, currentPeriod);
_facilityMasters[facilityNumber] = newFacilityMaster;

// Add to active facilities
currentActiveFacilities.Add(facilityNumber);

// Note: State for new facilities will be created during first row generation  ? WRONG!
```

**After** (Fixed):
```csharp
// Create new facility master
var newFacilityMaster = CreateFacilityMaster(customerId, nextIndex, customer, currentPeriod);
_facilityMasters[facilityNumber] = newFacilityMaster;

// CRITICAL FIX: Create initial state for new facility
// New facilities need an initial state just like in InitializeFirstPeriod
var initialState = CreateInitialFacilityState(newFacilityMaster, currentPeriod);
currentStates[facilityNumber] = initialState;  ? FIXED!

// Add to active facilities
currentActiveFacilities.Add(facilityNumber);
```

## How It Works Now

```
Period 2021-01 (Initialization):
  - FAC0000003202 created
  - _activeFacilitiesByPeriod["2021-01"].Add(FAC0000003202)
  - _facilityStatesByPeriod["2021-01"][FAC0000003202] = initialState
  ? State exists

Evolve to Period 2021-02:
  - FAC0000003202 continues (doesn't settle)
  - _activeFacilitiesByPeriod["2021-02"].Add(FAC0000003202)
  - _facilityStatesByPeriod["2021-02"][FAC0000003202] = continuedState
  ? State copied forward
  
  - FAC0000003203 created as NEW facility
  - _facilityMasters[FAC0000003203] = newMaster
  - initialState = CreateInitialFacilityState(newMaster, period)
  - _facilityStatesByPeriod["2021-02"][FAC0000003203] = initialState
  - _activeFacilitiesByPeriod["2021-02"].Add(FAC0000003203)
  ? New facility has initial state!

Evolve to Period 2021-03:
  - Reads _facilityStatesByPeriod["2021-02"][FAC0000003203]
  - ? State found! Evolution proceeds successfully
```

## Benefits

1. **? No More Crashes**: State always exists for ALL active facilities (continuing AND new)
2. **? Evolution Works**: Each period's evolution can read previous period's state
3. **? Consistency Guaranteed**: Active facilities always have corresponding states
4. **? New Facilities Work**: New facilities created mid-evolution have proper initial state
5. **? Minimal Change**: Simple fix, no architectural changes needed

## Data Consistency Rule

**Golden Rule**: For every facility in `_activeFacilitiesByPeriod[period]`, there MUST be a corresponding entry in `_facilityStatesByPeriod[period]`.

This applies to:
- ? Initial facilities (created in InitializeFirstPeriod)
- ? Continuing facilities (carried forward from previous period)
- ? New facilities (created during EvolveToPeriod)
- ? Settled facilities (marked as settled but state preserved)

## Files Modified

| File | Change | Lines |
|------|--------|-------|
| `Services/FacilityLifecycleManager.cs` | Fixed `EvolveToPeriod()` - two fixes | ~10 lines total |

## Build Status

? **Build Successful** - No errors, no warnings

## Verification

The fix ensures:
- [x] Every active facility has a corresponding state in every period
- [x] Continuing facilities get state copied forward
- [x] New facilities get initial state created
- [x] Evolution can safely read previous period's state
- [x] No KeyNotFoundException during lifecycle evolution
- [x] State updates during generation work correctly
- [x] Lifecycle integrity maintained

---

**Status**: ? **FIXED AND VERIFIED**

**Root Cause**: States not created for continuing facilities AND new facilities

**Solution**: 
1. Copy previous state forward for continuing facilities
2. Create initial state for new facilities during evolution

**Impact**: Fixes data consistency issue, enables proper lifecycle evolution

**Date Fixed**: 2024

**Branch**: `feature/synthetic-data-lifecycle-consistency`
