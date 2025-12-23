# DPD Evolution Testing Guide

## Quick Test Commands

### Generate Test Data (3 months, 100 facilities)
```bash
dotnet run -- --freq monthly --start 2024-01 --months 3 --rows-per-file 100
```

### Extract DPD Values for Specific Facility
```bash
# View all fields for a facility across periods
grep "FAC0000000101" Output/Monthly/*/PD_*.csv

# Extract just DPD column (column 18 in CSV - after header row)
grep "FAC0000000101" Output/Monthly/*/PD_*.csv | cut -d',' -f18
```

### Batch Verify All Facilities
```powershell
# PowerShell script to check DPD transitions
$files = Get-ChildItem -Path "Output\Monthly\*\*.csv" | Sort-Object Name
$facilities = @{}

foreach ($file in $files) {
    $period = $file.Directory.Name
    Get-Content $file | Select-Object -Skip 1 | ForEach-Object {
        $fields = $_ -split ','
        $facNum = $fields[1]
        $dpd = [int]$fields[17]  # DPD is 18th column (0-indexed: 17)
        
        if (-not $facilities.ContainsKey($facNum)) {
            $facilities[$facNum] = @()
        }
        $facilities[$facNum] += @{Period = $period; DPD = $dpd}
    }
}

# Check for invalid transitions (>30 day increases for monthly)
foreach ($fac in $facilities.Keys) {
    $history = $facilities[$fac] | Sort-Object Period
    for ($i = 1; $i -lt $history.Count; $i++) {
        $prev = $history[$i-1].DPD
        $curr = $history[$i].DPD
        $increase = $curr - $prev
        
        if ($increase -gt 30) {
            Write-Host "INVALID: $fac - $($history[$i-1].Period): $prev -> $($history[$i].Period): $curr (+$increase)"
        }
    }
}
```

## Expected Patterns

### Valid Monthly Transitions

#### Pattern 1: Currently Performing Stays Current
```
2024-01: 0
2024-02: 0
2024-03: 0
2024-04: 0
```
**Probability:** 95% per period

#### Pattern 2: Current to Delinquent
```
2024-01: 0
2024-02: 0
2024-03: 15    # Became delinquent (9-21 days typical)
2024-04: 45    # No payment (+30)
```

#### Pattern 3: Worsening (No Payment)
```
2024-01: 50
2024-02: 80    # +30 days
2024-03: 110   # +30 days
2024-04: 140   # +30 days
```
**Note:** Increases may be 27-33 days (±10% variation)

#### Pattern 4: Improvement (Payment Made)
```
2024-01: 120
2024-02: 100   # Paid ~20 days worth
2024-03: 80    # Paid another ~20 days
2024-04: 50    # Paid another ~30 days
```

#### Pattern 5: Full Cure
```
2024-01: 60
2024-02: 30    # Partial payment
2024-03: 0     # Full cure
2024-04: 0     # Stayed current
```

#### Pattern 6: Mixed Pattern
```
2024-01: 0     # Current
2024-02: 0     # Stayed current
2024-03: 18    # Missed payment
2024-04: 48    # No payment (+30)
2024-05: 35    # Partial payment (-13)
2024-06: 65    # No payment (+30)
2024-07: 40    # Partial payment (-25)
2024-08: 0     # Full cure
```

### Invalid Transitions (SHOULD NOT OCCUR)

? **Small Random Increments**
```
2024-01: 72
2024-02: 73    # +1 day - INVALID for monthly
```

? **Excessive Increases**
```
2024-01: 263
2024-02: 310   # +47 days - EXCEEDS monthly period
```

? **Out-of-Bound Variations**
```
2024-01: 100
2024-02: 150   # +50 days - EXCEEDS 30-day period
```

## Validation Criteria

### ? Pass Conditions

For **consecutive monthly periods**, all transitions must satisfy:
```
IF DPD_current > DPD_previous THEN
    DPD_current - DPD_previous ? 30
```

For **quarterly periods**:
```
IF DPD_current > DPD_previous THEN
    DPD_current - DPD_previous ? 90
```

For **yearly periods**:
```
IF DPD_current > DPD_previous THEN
    DPD_current - DPD_previous ? 365
```

### ? Fail Conditions

- Any single-period DPD increase > PeriodDaysIncrement
- Negative DPD values
- DPD increasing by exactly 1-10 days (too small for monthly)

## Statistical Checks

Run on 1000+ facilities across 12+ months:

```powershell
# Count transition types
$improvements = 0
$worsenings = 0
$stable = 0
$stayedCurrent = 0

# ... (calculate from data)

Write-Host "Improvements: $($improvements / $total * 100)% (expect ~40%)"
Write-Host "Worsenings: $($worsenings / $total * 100)% (expect ~30%)"  
Write-Host "Stable: $($stable / $total * 100)% (expect ~30%)"
Write-Host "Stayed Current: $($stayedCurrent / $totalCurrent * 100)% (expect ~95%)"
```

## Spot Check Script

```bash
#!/bin/bash
# Bash version for Linux/Mac

echo "Checking for invalid DPD transitions..."

prev_file=""
for file in Output/Monthly/*/PD_*.csv; do
    if [ -n "$prev_file" ]; then
        # Compare same facilities across periods
        while IFS=',' read -r line; do
            fac=$(echo "$line" | cut -d',' -f2)
            dpd_curr=$(echo "$line" | cut -d',' -f18)
            
            dpd_prev=$(grep "^[^,]*,$fac," "$prev_file" | cut -d',' -f18)
            
            if [ -n "$dpd_prev" ] && [ "$dpd_curr" -gt $((dpd_prev + 30)) ]; then
                echo "INVALID: $fac increased by more than 30 days: $dpd_prev -> $dpd_curr"
            fi
        done < <(tail -n +2 "$file")  # Skip header
    fi
    prev_file="$file"
done

echo "Validation complete."
```

## Example Test Session

```bash
# 1. Build and generate test data
dotnet build
dotnet run -- --freq monthly --start 2024-01 --months 6 --rows-per-file 200

# 2. Pick a random facility
FACILITY="FAC0000000150"

# 3. View its DPD evolution
echo "DPD Evolution for $FACILITY:"
grep "$FACILITY" Output/Monthly/*/PD_*.csv | cut -d',' -f1,18 | sed 's/.*\///' | sed 's/\/PD.*//'

# 4. Verify no excessive increases
# (Manual inspection or use script above)

# 5. Check statistics
echo "Total facilities with DPD > 0:"
grep -h "" Output/Monthly/2024-01/*.csv | tail -n +2 | cut -d',' -f18 | awk '$1 > 0' | wc -l
```

## Expected Output Example

```
Period      DPD
2024-01     0
2024-02     0
2024-03     0
2024-04     21
2024-05     51      # +30 from previous
2024-06     35      # -16 (improvement)
2024-07     65      # +30 from previous
2024-08     45      # -20 (improvement)
2024-09     0       # Full cure
2024-10     0
2024-11     0
2024-12     0
```

All transitions are valid: ?

## Troubleshooting

### If you see invalid transitions:

1. **Verify the fix was applied:**
   ```bash
   grep "ENFORCE UPPER BOUND" LoanDataCreator/Services/DpdEvolutionService.cs
   ```
   Should return a match.

2. **Check build succeeded:**
   ```bash
   dotnet build
   ```

3. **Clear old output:**
   ```bash
   rm -rf Output/Monthly/*
   ```

4. **Regenerate with new code:**
   ```bash
   dotnet run -- --freq monthly --start 2024-01 --months 3 --rows-per-file 100
   ```

### If all facilities have DPD=0:

Check configuration:
```bash
grep -A5 "DpdModel" appsettings.json
```

Ensure ShockProbability > 0 and BaseMean/ShockMean are reasonable.
