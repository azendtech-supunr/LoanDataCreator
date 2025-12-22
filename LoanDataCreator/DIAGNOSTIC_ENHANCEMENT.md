# Diagnostic Fix: Enhanced Error Messages for Lifecycle Evolution

## Problem

`KeyNotFoundException` when accessing `_facilityMasters[facilityNumber]` during `EvolveToPeriod()`:
```
The given key 'FAC0000000101' was not present in the dictionary.
at line 103
```

## Diagnostic Enhancement

Added defensive checks with detailed error messages to identify which dictionary is missing the key:

### Check 1: Previous States Dictionary
```csharp
if (!previousStates.TryGetValue(facilityNumber, out var previousState))
{
    throw new InvalidOperationException(
        $"Facility {facilityNumber} was in active facilities for period {previousPeriod.PeriodKey} " +
        $"but not found in period states. This indicates a data consistency issue.");
}
```

### Check 2: Facility Masters Dictionary
```csharp
if (!_facilityMasters.TryGetValue(facilityNumber, out var facilityMaster))
{
    throw new InvalidOperationException(
        $"Facility {facilityNumber} was in active facilities for period {previousPeriod.PeriodKey} " +
        $"but facility master not found in _facilityMasters dictionary. " +
        $"Total facility masters: {_facilityMasters.Count}, " +
        $"Active facilities in {previousPeriod.PeriodKey}: {previousActiveFacilities.Count}");
}
```

## Expected Output

The next run will provide detailed diagnostics:
- Which facility number is missing
- From which period
- Whether it's missing from states or masters
- Total counts for debugging

## Next Steps

Once we see the enhanced error message, we can determine:
1. If it's a tracking inconsistency (facility in active set but not in master)
2. If it's a timing issue (facility not yet created)
3. If it's a settlement issue (facility incorrectly marked as settled)

## Files Modified

- `Services/FacilityLifecycleManager.cs` - Added defensive checks in `EvolveToPeriod()`

---

**Status**: ? Diagnostic enhancement applied
**Build**: ? Successful  
**Next**: Run application to see detailed error message
