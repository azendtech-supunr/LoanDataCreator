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

        // BUSINESS RULE: Limit should only be populated when Nature is 'Revolving' OR Product Category is 'Housing Loan'
        // Otherwise, it should be 0 (which will be rendered as empty in CSV)
        var limit = ShouldPopulateLimit(master.Nature, master.ProductCategory) 
            ? master.Limit 
            : 0m;

        // Generate or evolve financial amounts
        decimal totalOS;
        decimal undisbursedAmount;
        
        if (previousState == null)
        {
            // New facility - generate initial amounts
            // Use actual limit for calculations, but display limit may be 0
            (totalOS, undisbursedAmount) = GenerateInitialAmounts(master.Limit, random);
        }
        else
        {
            // Existing facility - evolve amounts from previous period
            // Use actual limit for calculations, but display limit may be 0
            (totalOS, undisbursedAmount) = EvolveAmounts(master.Limit, previousState.TotalOS, previousState.UndisbursedAmount, random);
        }

        // BUSINESS RULE: Undisbursed Amount should only be populated for Housing Loan
        // For all other products, it should be 0 (which will be rendered as empty in CSV)
        // BUSINESS RULE: For a given facility, Undisbursed Amount must remain the same across all periods
        // Therefore, we take it from the FacilityState (which stores the initial value)
        undisbursedAmount = ShouldPopulateUndisbursedAmount(master.ProductCategory)
            ? (previousState?.UndisbursedAmount ?? undisbursedAmount) // Use previous value or initial generated value
            : 0m;

        // Use the constant BaseInterestRate from FacilityMaster
        // This ensures the same facility has the same interest rate across all periods
        var interestRate = master.BaseInterestRate;

        // Calculate interest in suspense based on DPD
        var interestInSuspense = CalculateInterestInSuspense(totalOS, daysPastDue, random);

        // BUSINESS RULE: Rescheduled is stored in FacilityMaster (constant across all periods)
        var rescheduled = master.Rescheduled;

        // BUSINESS RULE: Restructured is stored in FacilityMaster (constant across all periods)
        var restructured = master.Restructured;
        var timesRestructured = master.NoOfTimesRestructured;

        // BUSINESS RULE: Upgraded to Delinquency Bucket is stored in FacilityMaster (constant across all periods)
        var upgraded = master.UpgradedToDelinquencyBucket;

        // BUSINESS RULE: Individually Impaired is stored in FacilityMaster (constant across all periods)
        var individuallyImpaired = master.IndividuallyImpaired;

        // Generate bucketing based on DPD and immutable flags
        string bucketing;

        if (previousState == null)
        {
            // New facility - generate initial bucketing
            bucketing = GenerateBucketing(daysPastDue, individuallyImpaired, restructured);
        }
        else
        {
            // Existing facility - update bucketing based on current DPD
            bucketing = GenerateBucketing(daysPastDue, individuallyImpaired, restructured);
        }

        // Create the period row from master + state
        return new PeriodRow(
            master.CustomerNumber,
            master.FacilityNumber,
            master.Branch,
            master.Region,
            master.ProductCategory,
            master.Segment,
            master.SegmentForLGD,
            master.Industry,
            master.EarningType,
            master.Nature,
            master.GrantDate,
            master.MaturityDate,
            interestRate,
            string.Empty, // No. of Installments in Arrears
            string.Empty, // Total Remaining Installments (Including Installments in Arrears)
            string.Empty, // Installments Value
            master.InstallmentType,
            daysPastDue,
            limit, // Conditionally populated based on Nature or Product Category
            totalOS,
            undisbursedAmount, // Conditionally populated based on Product Category, constant across periods
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
            row.BucketingInIndividualAssessment,
            IsSettled: false);

        _lifecycleManager.StoreFacilityState(row.Period, state);
    }

    /// <summary>
    /// Determines if the Limit field should be populated based on business rules.
    /// BUSINESS RULE: Limit should only be populated when Nature is 'Revolving' OR Product Category is 'Housing Loan'.
    /// </summary>
    private static bool ShouldPopulateLimit(string nature, string productCategory)
    {
        // Check if Nature is Revolving
        if (nature.Equals("Revolving", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check if Product Category is Housing Loan
        if (productCategory.Equals("Housing Loan", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Determines if the Undisbursed Amount field should be populated based on business rules.
    /// BUSINESS RULE: Undisbursed Amount should only be populated when Product Category is 'Housing Loan'.
    /// </summary>
    private static bool ShouldPopulateUndisbursedAmount(string productCategory)
    {
        return productCategory.Equals("Housing Loan", StringComparison.OrdinalIgnoreCase);
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

    private decimal CalculateInterestInSuspense(decimal totalOS, int daysPastDue, Random random)
    {
        // BUSINESS RULE: Interest in Suspense should only be populated when DPD > 90
        // Otherwise, it should be 0 (which will be rendered as empty in CSV)
        if (daysPastDue <= _qaRules.InterestInSuspenseDpdThreshold)
        {
            return 0m;
        }

        // DPD is above threshold - calculate interest in suspense
        var baseRate = _amounts.InterestInSuspenseBase;
        var per30Days = _amounts.InterestInSuspensePer30Dpd * Math.Floor(daysPastDue / 30.0);
        var noise = (random.NextDouble() - 0.5) * 2 * _amounts.InterestInSuspenseNoise;
        
        var rate = Math.Max(0, baseRate + per30Days + noise);
        var interestInSuspense = Math.Round(totalOS * (decimal)rate, 2);

        // BUSINESS RULE: Interest in Suspense must be less than Total OS
        // If Total OS is negative or zero, interest in suspense should be 0
        if (totalOS <= 0)
        {
            return 0m;
        }

        // Ensure interest in suspense is strictly less than Total OS
        // Cap at 99% of Total OS to maintain the constraint
        var maxAllowedInterestInSuspense = totalOS * 0.99m;
        if (interestInSuspense >= totalOS)
        {
            interestInSuspense = Math.Round(maxAllowedInterestInSuspense, 2);
        }

        return interestInSuspense;
    }

    /// <summary>
    /// Generates the bucketing based on DPD, Individually Impaired, and Restructured status.
    /// BUSINESS RULE: Bucketing depends on DPD thresholds, Individually Impaired status (from FacilityMaster), and Restructured status (from FacilityMaster).
    /// </summary>
    private static string GenerateBucketing(int daysPastDue, string individuallyImpaired, string restructured)
    {
        return (daysPastDue, individuallyImpaired, restructured) switch
        {
            ( >= 90, _, _) => "NPL",
            ( >= 30, _, _) => "Special Mention",
            (_, "Yes", _) => "Substandard",
            (_, _, "Yes") => "Doubtful",
            _ => "Standard"
        };
    }
}
