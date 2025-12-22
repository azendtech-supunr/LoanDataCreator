using Microsoft.Extensions.Options;
using CsvPdGen.Config;
using CsvPdGen.Domain;

namespace CsvPdGen.Services;

/// <summary>
/// Generates period rows using lifecycle-consistent data.
/// Uses FacilityMaster (immutable) and FacilityState (evolving) to create rows
/// instead of regenerating all attributes randomly each period.
/// </summary>
public class LifecycleRowFactory
{
    private readonly SeedDeriver _seedDeriver;
    private readonly FacilityLifecycleManager _lifecycleManager;
    private readonly DpdEvolutionService _dpdEvolution;
    private readonly AmountsOptions _amounts;
    private readonly DpdModelOptions _dpdModel;
    private readonly QaRulesOptions _qaRules;

    public LifecycleRowFactory(
        SeedDeriver seedDeriver,
        FacilityLifecycleManager lifecycleManager,
        DpdEvolutionService dpdEvolution,
        IOptions<AmountsOptions> amounts,
        IOptions<DpdModelOptions> dpdModel,
        IOptions<QaRulesOptions> qaRules)
    {
        _seedDeriver = seedDeriver;
        _lifecycleManager = lifecycleManager;
        _dpdEvolution = dpdEvolution;
        _amounts = amounts.Value;
        _dpdModel = dpdModel.Value;
        _qaRules = qaRules.Value;
    }

    /// <summary>
    /// Creates a period row for a facility using lifecycle data.
    /// Combines immutable FacilityMaster data with evolved FacilityState.
    /// </summary>
    public PeriodRow CreateLifecycleRow(string facilityNumber, PeriodInfo period, FacilityState? previousState)
    {
        var master = _lifecycleManager.GetFacilityMaster(facilityNumber);
        var random = _seedDeriver.CreateRandom($"row:{facilityNumber}:{period.PeriodKey}");

        // Generate or evolve DPD
        int daysPastDue;
        if (previousState == null)
        {
            // New facility - generate initial DPD
            daysPastDue = _dpdEvolution.GenerateInitialDpd(facilityNumber, period.PeriodKey, _dpdModel);
        }
        else
        {
            // Existing facility - evolve DPD from previous period
            var periodDaysIncrement = DpdEvolutionService.GetPeriodDaysIncrement(period.Frequency);
            daysPastDue = _dpdEvolution.EvolveDpd(previousState.DaysPastDue, facilityNumber, period.PeriodKey, periodDaysIncrement);
        }

        // Generate or evolve financial amounts
        decimal totalOS;
        decimal undisbursedAmount;
        
        if (previousState == null)
        {
            // New facility - generate initial amounts
            (totalOS, undisbursedAmount) = GenerateInitialAmounts(master.Limit, random);
        }
        else
        {
            // Existing facility - evolve amounts from previous period
            (totalOS, undisbursedAmount) = EvolveAmounts(master.Limit, previousState.TotalOS, previousState.UndisbursedAmount, random);
        }

        // Calculate interest rate (small variation from base)
        var interestRate = CalculateInterestRate(master.BaseInterestRate, period.PeriodKey, random);

        // Calculate interest in suspense based on DPD
        var interestInSuspense = CalculateInterestInSuspense(totalOS, daysPastDue, random);

        // Generate or evolve risk flags
        string rescheduled, restructured;
        int timesRestructured;
        string upgraded, individuallyImpaired, bucketing;

        if (previousState == null)
        {
            // New facility - generate initial risk flags
            (rescheduled, restructured, timesRestructured, upgraded, individuallyImpaired, bucketing) = 
                GenerateRiskFlags(daysPastDue, random);
        }
        else
        {
            // Existing facility - evolve risk flags (some can only increase, not decrease)
            (rescheduled, restructured, timesRestructured, upgraded, individuallyImpaired, bucketing) = 
                EvolveRiskFlags(daysPastDue, previousState, random);
        }

        // Create the period row from master + state
        return new PeriodRow(
            master.CustomerNumber,
            master.FacilityNumber,
            master.Branch,
            master.Region,       // NEW: Include region
            master.ProductCategory,
            master.Segment,
            master.Industry,
            master.EarningType,
            master.Nature,
            master.GrantDate,
            master.MaturityDate,
            interestRate,
            master.InstallmentType,
            daysPastDue,
            master.Limit,
            totalOS,
            undisbursedAmount,
            interestInSuspense,
            master.CollateralType,
            master.CollateralValue,
            rescheduled,
            restructured,
            timesRestructured,
            upgraded,
            individuallyImpaired,
            bucketing,
            period.PeriodKey);
    }

    /// <summary>
    /// Stores the generated state back to the lifecycle manager for next period.
    /// </summary>
    public void StorePeriodState(PeriodRow row)
    {
        var state = new FacilityState(
            row.FacilityNumber,
            row.Period,
            row.DaysPastDue,
            row.TotalOS,
            row.UndisbursedAmount,
            row.InterestRate,
            row.InterestInSuspense,
            row.Rescheduled,
            row.Restructured,
            row.NoOfTimesRestructured,
            row.UpgradedToDelinquencyBucket,
            row.IndividuallyImpaired,
            row.BucketingInIndividualAssessment,
            IsSettled: false);

        _lifecycleManager.StoreFacilityState(row.Period, state);
    }

    private (decimal totalOS, decimal undisbursedAmount) GenerateInitialAmounts(decimal limit, Random random)
    {
        var osFraction = Math.Max(0, Math.Min(1, 
            _amounts.TotalOsFractionMean + (random.NextDouble() - 0.5) * 2 * _amounts.TotalOsFractionStdDev));
        var totalOS = Math.Round(limit * (decimal)osFraction, 2);

        var remainingLimit = limit - totalOS;
        var undisbursedFraction = Math.Max(0, Math.Min(1,
            _amounts.UndisbursedFractionMean + (random.NextDouble() - 0.5) * 2 * _amounts.UndisbursedFractionStdDev));
        var undisbursedAmount = Math.Round(remainingLimit * (decimal)undisbursedFraction, 2);

        if (totalOS + undisbursedAmount > limit)
        {
            undisbursedAmount = limit - totalOS;
        }

        return (totalOS, undisbursedAmount);
    }

    private (decimal totalOS, decimal undisbursedAmount) EvolveAmounts(
        decimal limit, decimal previousTotalOS, decimal previousUndisbursed, Random random)
    {
        // Allow small changes to Total OS (increase or decrease)
        var maxChange = _amounts.TotalOsMaxChangePerPeriod;
        var changeDirection = random.NextDouble() - 0.5; // -0.5 to +0.5
        var changeFraction = changeDirection * 2 * maxChange; // Scale to max change
        
        var newTotalOS = previousTotalOS * (1 + (decimal)changeFraction);
        
        // Clamp to reasonable bounds
        newTotalOS = Math.Max(0, Math.Min(limit, newTotalOS));
        
        // Allow rare negative OS values
        if (random.NextDouble() < _amounts.NegativeOsProbability)
        {
            newTotalOS = -Math.Abs(newTotalOS * (decimal)_amounts.NegativeOsMaxFraction);
        }
        
        newTotalOS = Math.Round(newTotalOS, 2);

        // Undisbursed amount: adjust based on change in Total OS
        var remainingLimit = limit - newTotalOS;
        var undisbursedFraction = Math.Max(0, Math.Min(1,
            _amounts.UndisbursedFractionMean + (random.NextDouble() - 0.5) * 2 * _amounts.UndisbursedFractionStdDev));
        var undisbursedAmount = Math.Round(remainingLimit * (decimal)undisbursedFraction, 2);

        if (newTotalOS + undisbursedAmount > limit && newTotalOS >= 0)
        {
            undisbursedAmount = Math.Max(0, limit - newTotalOS);
        }

        return (newTotalOS, undisbursedAmount);
    }

    private decimal CalculateInterestRate(decimal baseRate, string periodKey, Random random)
    {
        // Add small period-based volatility
        var periodHash = Math.Abs(periodKey.GetHashCode()) % 1000;
        var periodVolatility = (periodHash / 1000.0 - 0.5) * 2 * _amounts.InterestRateVolatility;
        
        var rate = (double)baseRate + periodVolatility + (random.NextDouble() - 0.5) * 0.005;
        
        return Math.Round((decimal)Math.Max(0.001, rate), 4);
    }

    private decimal CalculateInterestInSuspense(decimal totalOS, int daysPastDue, Random random)
    {
        // Interest in suspense depends on DPD
        if (daysPastDue < _qaRules.InterestInSuspenseDpdThreshold)
        {
            // Low DPD - minimal interest in suspense
            var baseRate = _amounts.InterestInSuspenseBase;
            var noise = (random.NextDouble() - 0.5) * 2 * _amounts.InterestInSuspenseNoise;
            var rate = Math.Max(0, baseRate + noise);
            return Math.Round(totalOS * (decimal)rate, 2);
        }
        else
        {
            // High DPD - significant interest in suspense
            var baseRate = _amounts.InterestInSuspenseBase;
            var per30Days = _amounts.InterestInSuspensePer30Dpd * Math.Floor(daysPastDue / 30.0);
            var noise = (random.NextDouble() - 0.5) * 2 * _amounts.InterestInSuspenseNoise;
            
            var rate = Math.Max(0, baseRate + per30Days + noise);
            return Math.Round(totalOS * (decimal)rate, 2);
        }
    }

    private (string rescheduled, string restructured, int timesRestructured, 
             string upgraded, string individuallyImpaired, string bucketing) GenerateRiskFlags(
        int daysPastDue, Random random)
    {
        var dpdFactor = Math.Min(1.0, daysPastDue / 180.0);
        
        var rescheduledProb = Math.Min(0.3, dpdFactor * 0.15);
        var restructuredProb = Math.Min(0.2, dpdFactor * 0.10);
        var individuallyImpairedProb = Math.Min(0.4, dpdFactor * 0.25);
        
        var rescheduled = random.NextDouble() < rescheduledProb ? "Yes" : "No";
        var restructured = random.NextDouble() < restructuredProb ? "Yes" : "No";
        
        var timesRestructured = 0;
        if (restructured == "Yes")
        {
            timesRestructured = 1;
            while (random.NextDouble() < 0.1 && timesRestructured < 5)
                timesRestructured++;
        }

        var upgraded = (daysPastDue >= 30 && random.NextDouble() < 0.7) ? "Yes" : "No";
        var individuallyImpaired = random.NextDouble() < individuallyImpairedProb ? "Yes" : "No";
        
        var bucketing = (daysPastDue, individuallyImpaired, restructured) switch
        {
            ( >= 90, _, _) => "NPL",
            ( >= 30, _, _) => "Special Mention",
            (_, "Yes", _) => "Substandard",
            (_, _, "Yes") => "Doubtful",
            _ => "Standard"
        };

        return (rescheduled, restructured, timesRestructured, upgraded, individuallyImpaired, bucketing);
    }

    private (string rescheduled, string restructured, int timesRestructured, 
             string upgraded, string individuallyImpaired, string bucketing) EvolveRiskFlags(
        int daysPastDue, FacilityState previousState, Random random)
    {
        // Rescheduled: once Yes, stays Yes (can't un-reschedule)
        var rescheduled = previousState.Rescheduled;
        if (rescheduled == "No" && daysPastDue >= 30)
        {
            // Can become rescheduled if DPD is high
            if (random.NextDouble() < 0.05)
                rescheduled = "Yes";
        }

        // Restructured: can increase but not decrease (QA rule enforcement)
        var restructured = previousState.Restructured;
        var timesRestructured = previousState.NoOfTimesRestructured;
        
        if (_qaRules.EnforceRestructuredMonotonicity)
        {
            // Restructured can only stay same or increase
            if (restructured == "No" && daysPastDue >= 60 && random.NextDouble() < 0.03)
            {
                restructured = "Yes";
                timesRestructured = 1;
            }
            else if (restructured == "Yes" && daysPastDue >= 90 && random.NextDouble() < 0.05)
            {
                // Can be restructured again
                timesRestructured = Math.Min(5, timesRestructured + 1);
            }
        }
        else
        {
            // Allow recalculation
            (_, restructured, timesRestructured, _, _, _) = GenerateRiskFlags(daysPastDue, random);
        }

        // Upgraded to delinquency bucket
        var upgraded = (daysPastDue >= 30 && random.NextDouble() < 0.7) ? "Yes" : "No";

        // Individually Impaired
        var dpdFactor = Math.Min(1.0, daysPastDue / 180.0);
        var individuallyImpairedProb = Math.Min(0.4, dpdFactor * 0.25);
        var individuallyImpaired = random.NextDouble() < individuallyImpairedProb ? "Yes" : "No";

        // Bucketing based on current DPD and flags
        var bucketing = (daysPastDue, individuallyImpaired, restructured) switch
        {
            ( >= 90, _, _) => "NPL",
            ( >= 30, _, _) => "Special Mention",
            (_, "Yes", _) => "Substandard",
            (_, _, "Yes") => "Doubtful",
            _ => "Standard"
        };

        return (rescheduled, restructured, timesRestructured, upgraded, individuallyImpaired, bucketing);
    }
}
