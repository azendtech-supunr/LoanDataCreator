# Migration Guide: Lifecycle-Consistent Data Generation

## Summary of Changes

This update introduces **lifecycle-consistent data generation** where customers and facilities persist across periods with stable identities and evolving states. All existing configuration options have been preserved, and new lifecycle behavior is fully configurable.

## ? What You Don't Need to Change

### Configuration
- **All existing appsettings.json sections still work**
- Frequencies (Yearly, Quarterly, Monthly)
- Generation options (RowsPerFile, OutputBasePath, Seed, etc.)
- Customers (CustomerCount, FacilitiesPerCustomer)
- Distributions (Branches, Products, Segments, etc.)
- Amounts (Limits, Interest Rates, etc.)
- DpdModel (existing DPD generation)

### Command-Line Usage
```bash
# All existing commands still work exactly as before
dotnet run --all
dotnet run --freq monthly --start 2024-01 --months 12
dotnet run --rows-per-file 10000 --out CustomOutput
```

### Output Format
- CSV file structure unchanged
- Same 26 columns in same order
- File naming convention preserved
- Gzip compression option still available

## ?? What's New

### New Configuration Sections (Optional)

Three new sections added to `appsettings.json` with sensible defaults:

#### 1. Lifecycle Configuration
Controls how facilities persist and evolve across periods:

```json
{
  "Lifecycle": {
    "FacilitySettlementRate": 0.05,
    "NewFacilityRate": 0.03,
    "MaxNewFacilitiesPerCustomer": 2,
    "ShortTermSettlementProbability": 0.95,
    "SettlementDpdThreshold": 180,
    "HighDpdSettlementProbability": 0.30
  }
}
```

**If you don't add this section**, the defaults shown above will be used.

#### 2. DPD Evolution Configuration
Controls how DPD changes realistically across periods:

```json
{
  "DpdEvolution": {
    "ImprovementProbability": 0.40,
    "WorseningProbability": 0.30,
    "ImprovementMean": -15.0,
    "ImprovementStdDev": 10.0,
    "WorseningMean": 20.0,
    "WorseningStdDev": 15.0,
    "CurrentStayCurrentProbability": 0.95,
    "PeriodDaysIncrement": 30
  }
}
```

**If you don't add this section**, the defaults shown above will be used.

#### 3. QA Rules Configuration
Enforces banking domain rules and constraints:

```json
{
  "QaRules": {
    "HybridAllowedProducts": ["LEASING"],
    "RevolvingProducts": ["CREDIT CARD", "OVERDRAFT"],
    "ShortTermProducts": ["BULLET", "OVERDRAFT"],
    "CollateralMapping": {
      "SECURED": ["REAL_ESTATE", "VEHICLE", "MACHINERY", "GOLD_JEWELRY", "FIXED_DEPOSITS"],
      "UNSECURED": ["OTHER", "CASH"]
    },
    "InterestInSuspenseDpdThreshold": 90,
    "EnforceLimitConstancy": true,
    "EnforceRestructuredMonotonicity": true
  }
}
```

**If you don't add this section**, the defaults shown above will be used.

#### 4. Enhanced Amounts Section
Three new optional fields added to existing `Amounts` section:

```json
{
  "Amounts": {
    // ...all existing fields...
    "NegativeOsProbability": 0.001,
    "NegativeOsMaxFraction": 0.05,
    "TotalOsMaxChangePerPeriod": 0.15
  }
}
```

**If you don't add these fields**, the defaults shown above will be used.

## ?? Behavioral Changes

### Before (Old Behavior)
- Each period generated completely random data
- Same customer number in Period 1 and Period 2 were **different customers**
- Same facility number in Period 1 and Period 2 were **different facilities**
- DPD randomly generated each period
- No relationship between periods

### After (New Behavior)
- Customers persist across all periods with same attributes
- Facilities persist across periods (until settled)
- DPD evolves realistically from previous period
- Facilities can settle (end) based on maturity, DPD, or product type
- New facilities added over time

### Impact on Generated Data

#### Customer Numbers
- **Old**: Customer CUST00000001 in 2024-01 is unrelated to CUST00000001 in 2024-02
- **New**: Customer CUST00000001 is the same person across all periods with consistent attributes

#### Facility Numbers
- **Old**: Facility FAC0000000101 in 2024-01 is unrelated to FAC0000000101 in 2024-02
- **New**: Facility FAC0000000101 is the same loan across periods (until settled)

#### DPD (Days Past Due)
- **Old**: Randomly generated each period (could be 0, then 180, then 5)
- **New**: Evolves logically (e.g., 0 ? 30 ? 60 ? settled, or 30 ? 15 ? 0)

#### Field Consistency
- **Limit**: Now constant across periods (enforced)
- **Total OS**: Evolves with small changes (not random jumps)
- **Grant Date, Maturity Date**: Remain constant per facility
- **Product Category, Segment, Nature**: Remain constant per facility
- **Restructured flags**: Can only increase, never decrease

## ?? Use Cases

### For PD/LGD Model Training
**Old approach:**
- Treat each period independently
- No temporal relationships
- Random DPD makes it hard to learn delinquency progression

**New approach (better for ML):**
- Track customer/facility over time
- Learn delinquency progression patterns
- Understand settlement triggers
- Model portfolio aging dynamics

### For Testing Data Pipelines
**Old approach:**
- Test with independent snapshots

**New approach:**
- Test with realistic temporal data
- Validate period-over-period logic
- Test settlement/closure scenarios
- Verify master data consistency

### For QA/Auditing
**New capabilities:**
- Verify same facility has consistent master data
- Check that limits don't change unexpectedly
- Validate DPD progression is realistic
- Ensure restructured flags are monotonic

## ??? Tuning Guide

### High Churn Scenario (Many Settlements/New Facilities)
```json
{
  "Lifecycle": {
    "FacilitySettlementRate": 0.15,        // 15% settlement rate
    "NewFacilityRate": 0.10,               // 10% new facility rate
    "HighDpdSettlementProbability": 0.60   // 60% high-DPD settlements
  }
}
```

### Stable Portfolio Scenario (Low Churn)
```json
{
  "Lifecycle": {
    "FacilitySettlementRate": 0.02,        // 2% settlement rate
    "NewFacilityRate": 0.01,               // 1% new facility rate
    "HighDpdSettlementProbability": 0.10   // 10% high-DPD settlements
  }
}
```

### Optimistic DPD Evolution (More Improvements)
```json
{
  "DpdEvolution": {
    "ImprovementProbability": 0.60,        // 60% improvement
    "WorseningProbability": 0.20,          // 20% worsening
    "CurrentStayCurrentProbability": 0.98  // 98% stay current
  }
}
```

### Pessimistic DPD Evolution (More Delinquencies)
```json
{
  "DpdEvolution": {
    "ImprovementProbability": 0.25,        // 25% improvement
    "WorseningProbability": 0.50,          // 50% worsening
    "CurrentStayCurrentProbability": 0.85  // 85% stay current
  }
}
```

## ?? Testing Your Configuration

### Step 1: Generate Small Test Set
```bash
dotnet run --freq monthly --start 2024-01 --months 3 --rows-per-file 100
```

### Step 2: Verify Lifecycle Consistency

Check that the same customer appears across periods:
```bash
# Extract all rows for customer CUST00000001
grep "CUST00000001" Output/Monthly/2024-01/*.csv
grep "CUST00000001" Output/Monthly/2024-02/*.csv
grep "CUST00000001" Output/Monthly/2024-03/*.csv
```

Check that facility attributes are consistent:
```bash
# Should have same Limit, Grant Date, Product Category across periods
grep "FAC0000000101" Output/Monthly/2024-01/*.csv
grep "FAC0000000101" Output/Monthly/2024-02/*.csv
```

Check DPD evolution:
```bash
# DPD should change logically, not randomly
grep "FAC0000000101" Output/Monthly/*/PD_*.csv | cut -d',' -f13
```

### Step 3: Verify Settlement Logic

Count facilities per period (should decrease slightly due to settlements):
```bash
# Count unique facilities per period
cut -d',' -f2 Output/Monthly/2024-01/*.csv | sort -u | wc -l
cut -d',' -f2 Output/Monthly/2024-02/*.csv | sort -u | wc -l
cut -d',' -f2 Output/Monthly/2024-03/*.csv | sort -u | wc -l
```

## ? FAQ

### Q: Will my existing pipelines break?
**A:** No. Output format is identical. However, **data semantics have changed** - same IDs now mean same entities across periods.

### Q: Can I disable lifecycle logic and get old random behavior?
**A:** Not directly, but you can simulate it by:
- Setting `FacilitySettlementRate` to 1.0 (settle everything each period)
- Setting `NewFacilityRate` to match your customer count
- This would make each period mostly independent

### Q: How do I know if lifecycle is working?
**A:** Check logs during generation:
```
Initializing facilities for first period: 2024-01
Evolving facilities from 2024-01 to 2024-02
```
Or verify same facility numbers appear across periods in output files.

### Q: What if I only want to generate one period?
**A:** Lifecycle still applies - it will initialize facilities for that period. The difference is internal (stable seeding), but since there's no "next period", you won't see evolution.

### Q: Can I mix frequencies (e.g., monthly evolving from yearly)?
**A:** Not in this version. Each frequency is independent. If you enable both Yearly and Monthly, they will have separate lifecycle tracks.

### Q: How does deterministic seeding work with lifecycle?
**A:** Seed still controls all randomness. Same seed = same facilities, same evolution, same output. Facilities are seeded based on customer ID, not period, ensuring consistency.

## ?? Troubleshooting

### Issue: No data generated
**Check:**
- At least one frequency is enabled in `appsettings.json`
- `CustomerCount` > 0
- `RowsPerFile` > 0

### Issue: Files are empty or very small
**Possible causes:**
- All facilities settled (check `FacilitySettlementRate` - should be < 1.0)
- Very low `NewFacilityRate` and high `SettlementRate`

**Solution:**
- Reduce `FacilitySettlementRate` to 0.05
- Increase `NewFacilityRate` to 0.03
- Or increase `FacilitiesPerCustomerMax`

### Issue: DPD values seem too stable
**Check:** `DpdEvolution.ImprovementProbability` + `WorseningProbability` should be > 0.3

### Issue: Too many settlements
**Solution:** Reduce settlement rates:
```json
{
  "Lifecycle": {
    "FacilitySettlementRate": 0.02,
    "HighDpdSettlementProbability": 0.10,
    "ShortTermSettlementProbability": 0.70
  }
}
```

## ?? Support

For issues or questions:
1. Check this migration guide
2. Review `LIFECYCLE_DESIGN.md` for detailed architecture
3. Examine example configuration in `appsettings.json`
4. Run smoke tests: `dotnet run --test`

## ?? Rollback (If Needed)

If you need to revert to the previous version:
```bash
git checkout <previous-commit-hash>
```

Note: This refactoring is in a feature branch (`feature/synthetic-data-lifecycle-consistency`), so main/master branch is unchanged.
