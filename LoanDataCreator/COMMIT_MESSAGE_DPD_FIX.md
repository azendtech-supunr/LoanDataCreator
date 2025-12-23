# Commit Message

```
fix: Enforce period-based DPD evolution invariant

PROBLEM:
DPD evolution was producing unrealistic transitions between monthly periods:
- 72 ? 73 (only +1 day increase - invalid)
- 263 ? 310 (+47 days - exceeds 30-day monthly period)

ROOT CAUSE:
DpdEvolutionService.EvolveDpd was adding unbounded random deltas on top of
period time progression, violating the fundamental invariant that DPD
snapshots can only increase by at most the period length (30 days monthly).

SOLUTION:
Refactored DPD evolution logic to enforce strict invariant:
  NextDPD ? PreviousDPD + PeriodDaysIncrement

CHANGES:
- Worsening path: Use period increment (30d) with ±10% variation, not unbounded random
- Stability path: Use exact period increment (30d), removed 80%-120% multiplier
- Added explicit upper bound enforcement: Math.Min(newDpd, maxAllowedDpd)
- Currently performing (DPD=0): Bounded initial delinquency to period increment
- Preserved all probability distributions and improvement/cure logic

VALIDATION:
Valid monthly transitions now guaranteed:
? 72 ? 102 (no payment, +30 days)
? 72 ? 0 (full cure)
? 72 ? 15-55 (partial payment or no payment)
? 263 ? 293 (no payment, +30 days)

Invalid transitions eliminated:
? 72 ? 73 (too small)
? 263 ? 310 (exceeds period)

IMPACT:
- DPD evolution now matches period-end snapshot reporting semantics
- All configuration unchanged (backward compatible)
- All lifecycle features preserved
- Localized fix to DpdEvolutionService only

FILES:
- Modified: LoanDataCreator/Services/DpdEvolutionService.cs
- Added: LoanDataCreator/FIX_DPD_EVOLUTION_INVARIANT.md (detailed documentation)
- Added: LoanDataCreator/DPD_TESTING_GUIDE.md (testing guide)
```
