using Microsoft.Extensions.Options;
using CsvPdGen.Config;
using CsvPdGen.Domain;
using System.Globalization;

namespace CsvPdGen.Services;

/// <summary>
/// Manages the lifecycle of facilities across periods.
/// Tracks which facilities exist, when they settle, and when new facilities are created.
/// Ensures consistency: same facility number stays with same customer, master data persists.
/// </summary>
public class FacilityLifecycleManager
{
    private readonly SeedDeriver _seedDeriver;
    private readonly CustomerFactory _customerFactory;
    private readonly DistributionsOptions _distributions;
    private readonly AmountsOptions _amounts;
    private readonly LifecycleOptions _lifecycle;
    private readonly QaRulesOptions _qaRules;

    // Track all facility masters (immutable data) keyed by FacilityNumber
    private readonly Dictionary<string, FacilityMaster> _facilityMasters = new();

    // Track facility states per period: [PeriodKey][FacilityNumber] -> FacilityState
    private readonly Dictionary<string, Dictionary<string, FacilityState>> _facilityStatesByPeriod = new();

    // Track which facilities are active (not settled) in each period
    private readonly Dictionary<string, HashSet<string>> _activeFacilitiesByPeriod = new();

    // Track next facility index per customer for generating new facilities
    private readonly Dictionary<int, int> _nextFacilityIndexByCustomer = new();

    public FacilityLifecycleManager(
        SeedDeriver seedDeriver,
        CustomerFactory customerFactory,
        IOptions<DistributionsOptions> distributions,
        IOptions<AmountsOptions> amounts,
        IOptions<LifecycleOptions> lifecycle,
        IOptions<QaRulesOptions> qaRules)
    {
        _seedDeriver = seedDeriver;
        _customerFactory = customerFactory;
        _distributions = distributions.Value;
        _amounts = amounts.Value;
        _lifecycle = lifecycle.Value;
        _qaRules = qaRules.Value;
    }

    /// <summary>
    /// Initializes facilities for the first period.
    /// Creates initial facilities for all customers based on CustomerCount and FacilitiesPerCustomer config.
    /// </summary>
    public void InitializeFirstPeriod(PeriodInfo period, int customerCount, int facilitiesPerCustomerMin, int facilitiesPerCustomerMax)
    {
        var activeFacilities = new HashSet<string>();
        var periodStates = new Dictionary<string, FacilityState>();

        for (var customerId = 1; customerId <= customerCount; customerId++)
        {
            var customer = _customerFactory.CreateCustomer(customerId);
            
            // Create initial facilities for this customer
            for (var facilityIndex = 1; facilityIndex <= customer.FacilityCount; facilityIndex++)
            {
                var facilityNumber = _customerFactory.CreateFacilityNumber(customerId, facilityIndex);
                
                // Create facility master (immutable data)
                var facilityMaster = CreateFacilityMaster(customerId, facilityIndex, customer, period);
                _facilityMasters[facilityNumber] = facilityMaster;
                
                // Create initial state for this facility
                var initialState = CreateInitialFacilityState(facilityMaster, period);
                periodStates[facilityNumber] = initialState;
                activeFacilities.Add(facilityNumber);
            }

            // Track next facility index for this customer
            _nextFacilityIndexByCustomer[customerId] = customer.FacilityCount + 1;
        }

        _activeFacilitiesByPeriod[period.PeriodKey] = activeFacilities;
        _facilityStatesByPeriod[period.PeriodKey] = periodStates;
    }

    /// <summary>
    /// Evolves facilities from previous period to current period.
    /// Handles facility settlements, new facility creation, and state transitions.
    /// </summary>
    public void EvolveToPeriod(PeriodInfo currentPeriod, PeriodInfo previousPeriod, int customerCount)
    {
        var previousActiveFacilities = _activeFacilitiesByPeriod[previousPeriod.PeriodKey];
        var previousStates = _facilityStatesByPeriod[previousPeriod.PeriodKey];
        
        var currentActiveFacilities = new HashSet<string>();
        var currentStates = new Dictionary<string, FacilityState>();

        var random = _seedDeriver.CreateRandom($"lifecycle:{currentPeriod.PeriodKey}");

        // Step 1: Process existing facilities - most should continue, some should settle
        foreach (var facilityNumber in previousActiveFacilities)
        {
            // Defensive check: Ensure facility exists in states dictionary
            if (!previousStates.TryGetValue(facilityNumber, out var previousState))
            {
                throw new InvalidOperationException(
                    $"Facility {facilityNumber} was in active facilities for period {previousPeriod.PeriodKey} " +
                    $"but not found in period states. This indicates a data consistency issue.");
            }

            // Defensive check: Ensure facility master exists
            if (!_facilityMasters.TryGetValue(facilityNumber, out var facilityMaster))
            {
                throw new InvalidOperationException(
                    $"Facility {facilityNumber} was in active facilities for period {previousPeriod.PeriodKey} " +
                    $"but facility master not found in _facilityMasters dictionary. " +
                    $"Total facility masters: {_facilityMasters.Count}, " +
                    $"Active facilities in {previousPeriod.PeriodKey}: {previousActiveFacilities.Count}");
            }

            // Determine if facility should settle this period
            if (ShouldSettleFacility(facilityMaster, previousState, currentPeriod, random))
            {
                // Facility is settled - mark as settled but keep in state for historical tracking
                var settledState = previousState with { IsSettled = true, Period = currentPeriod.PeriodKey };
                currentStates[facilityNumber] = settledState;
                // Don't add to activeFacilities - it's settled
            }
            else
            {
                // Facility continues - add to active set
                currentActiveFacilities.Add(facilityNumber);
                
                // CRITICAL FIX: Copy the previous state to current period
                // The state will be updated during row generation with evolved values
                // But we need a placeholder here to maintain consistency
                var continuedState = previousState with { Period = currentPeriod.PeriodKey };
                currentStates[facilityNumber] = continuedState;
            }
        }

        // Step 2: Create new facilities for some customers
        var newFacilityCount = (int)(customerCount * _lifecycle.NewFacilityRate);
        var customerIdsForNewFacilities = Enumerable.Range(1, customerCount)
            .OrderBy(_ => random.Next())
            .Take(newFacilityCount)
            .ToList();

        foreach (var customerId in customerIdsForNewFacilities)
        {
            var customer = _customerFactory.CreateCustomer(customerId);
            var newFacilitiesCount = random.Next(1, _lifecycle.MaxNewFacilitiesPerCustomer + 1);

            for (var i = 0; i < newFacilitiesCount; i++)
            {
                var nextIndex = _nextFacilityIndexByCustomer.GetValueOrDefault(customerId, 1);
                var facilityNumber = _customerFactory.CreateFacilityNumber(customerId, nextIndex);
                
                // Create new facility master
                var newFacilityMaster = CreateFacilityMaster(customerId, nextIndex, customer, currentPeriod);
                _facilityMasters[facilityNumber] = newFacilityMaster;
                
                // CRITICAL FIX: Create initial state for new facility
                // New facilities need an initial state just like in InitializeFirstPeriod
                var initialState = CreateInitialFacilityState(newFacilityMaster, currentPeriod);
                currentStates[facilityNumber] = initialState;
                
                // Add to active facilities
                currentActiveFacilities.Add(facilityNumber);
                
                // Update next index
                _nextFacilityIndexByCustomer[customerId] = nextIndex + 1;
            }
        }

        _activeFacilitiesByPeriod[currentPeriod.PeriodKey] = currentActiveFacilities;
        _facilityStatesByPeriod[currentPeriod.PeriodKey] = currentStates;
    }

    /// <summary>
    /// Gets the list of active facility numbers for a period.
    /// </summary>
    public IReadOnlyCollection<string> GetActiveFacilities(string periodKey)
    {
        return _activeFacilitiesByPeriod.GetValueOrDefault(periodKey, new HashSet<string>());
    }

    /// <summary>
    /// Gets the facility master (immutable data) for a facility.
    /// </summary>
    public FacilityMaster GetFacilityMaster(string facilityNumber)
    {
        return _facilityMasters[facilityNumber];
    }

    /// <summary>
    /// Gets the facility state from the previous period (if exists).
    /// Returns null if this is a new facility or first period.
    /// </summary>
    public FacilityState? GetPreviousPeriodState(string facilityNumber, string currentPeriodKey, string? previousPeriodKey = null)
    {
        // If previous period key is provided, use it directly for efficiency
        if (previousPeriodKey != null && _facilityStatesByPeriod.TryGetValue(previousPeriodKey, out var prevStates))
        {
            if (prevStates.TryGetValue(facilityNumber, out var state))
            {
                return state;
            }
        }
        
        // Fallback: Search through all periods (less efficient but works for edge cases)
        // Find the most recent period before current period that has this facility
        FacilityState? mostRecentState = null;
        DateTime? mostRecentDate = null;
        
        foreach (var (periodKey, states) in _facilityStatesByPeriod)
        {
            if (periodKey == currentPeriodKey) continue;
            
            if (states.TryGetValue(facilityNumber, out var state))
            {
                // Try to parse period date to find most recent
                // This is a simplified approach - could be improved
                if (mostRecentState == null)
                {
                    mostRecentState = state;
                }
                else
                {
                    // For now, just return the first found state
                    // In production, you'd want to properly compare dates
                    mostRecentState = state;
                }
            }
        }
        
        return mostRecentState;
    }

    /// <summary>
    /// Stores the facility state for a period.
    /// </summary>
    public void StoreFacilityState(string periodKey, FacilityState state)
    {
        if (!_facilityStatesByPeriod.ContainsKey(periodKey))
        {
            _facilityStatesByPeriod[periodKey] = new Dictionary<string, FacilityState>();
        }
        _facilityStatesByPeriod[periodKey][state.FacilityNumber] = state;
    }

    /// <summary>
    /// Creates a new facility master with stable attributes.
    /// </summary>
    private FacilityMaster CreateFacilityMaster(int customerId, int facilityIndex, CustomerMaster customer, PeriodInfo period)
    {
        var random = _seedDeriver.CreateCustomerRandom(customerId * 1000 + facilityIndex);
        var facilityNumber = _customerFactory.CreateFacilityNumber(customerId, facilityIndex);

        // ? FIX: Use customer's branch instead of generating a new one
        var branch = customer.Branch;
        var productCategory = SampleFromDistribution(_distributions.ProductCategories, random);
        var installmentType = SampleFromDistribution(_distributions.InstallmentTypes, random);

        var (grantDate, maturityDate) = GenerateDates(productCategory, period.PeriodEndDate, random);
        var limit = GenerateLimit(random);
        var (collateralType, collateralValue) = GenerateCollateral(customer.Nature, productCategory, limit, random);
        
        var baseInterestRate = GenerateBaseInterestRate(customer.Segment, random);

        return new FacilityMaster(
            facilityNumber,
            customer.CustomerNumber,
            branch,
            customer.Region,  // NEW: Include region from customer
            productCategory,
            customer.Segment,
            customer.Industry,
            customer.EarningType,
            customer.Nature,
            grantDate,
            maturityDate,
            installmentType,
            limit,
            collateralType,
            collateralValue,
            baseInterestRate,
            period.PeriodKey);
    }

    /// <summary>
    /// Creates initial facility state for the first period.
    /// </summary>
    private FacilityState CreateInitialFacilityState(FacilityMaster master, PeriodInfo period)
    {
        var random = _seedDeriver.CreateRandom($"initial:{master.FacilityNumber}:{period.PeriodKey}");

        var (totalOS, undisbursedAmount) = GenerateInitialAmounts(master.Limit, random);
        var daysPastDue = GenerateInitialDpd(random);
        var interestInSuspense = CalculateInterestInSuspense(totalOS, daysPastDue, random);
        
        var (rescheduled, restructured, timesRestructured, upgraded, individuallyImpaired, bucketing) = 
            GenerateRiskFlags(daysPastDue, random);

        return new FacilityState(
            master.FacilityNumber,
            period.PeriodKey,
            daysPastDue,
            totalOS,
            undisbursedAmount,
            master.BaseInterestRate,
            interestInSuspense,
            rescheduled,
            restructured,
            timesRestructured,
            upgraded,
            individuallyImpaired,
            bucketing,
            IsSettled: false);
    }

    /// <summary>
    /// Determines if a facility should settle in the current period based on lifecycle rules.
    /// </summary>
    private bool ShouldSettleFacility(FacilityMaster master, FacilityState previousState, PeriodInfo currentPeriod, Random random)
    {
        // Check if maturity date has passed
        if (currentPeriod.PeriodEndDate >= master.MaturityDate)
        {
            return true;
        }

        // Short-term products have higher settlement probability
        if (_qaRules.ShortTermProducts.Contains(master.ProductCategory.ToUpperInvariant()))
        {
            var periodsSinceGrant = CalculatePeriodsBetween(master.StartPeriod, currentPeriod.PeriodKey, currentPeriod.Frequency);
            if (periodsSinceGrant >= GetPeriodsInOneYear(currentPeriod.Frequency))
            {
                if (random.NextDouble() < _lifecycle.ShortTermSettlementProbability)
                {
                    return true;
                }
            }
        }

        // High DPD facilities are likely to settle (write-off)
        if (previousState.DaysPastDue >= _lifecycle.SettlementDpdThreshold)
        {
            if (random.NextDouble() < _lifecycle.HighDpdSettlementProbability)
            {
                return true;
            }
        }

        // Random settlement based on configured rate
        if (random.NextDouble() < _lifecycle.FacilitySettlementRate)
        {
            return true;
        }

        return false;
    }

    private static (DateTime grantDate, DateTime maturityDate) GenerateDates(string productCategory, DateTime referenceDate, Random random)
    {
        var daysBeforeGrant = random.Next(30, 1825);
        var grantDate = referenceDate.AddDays(-daysBeforeGrant);

        var tenorDays = productCategory.ToUpperInvariant() switch
        {
            "TERM LOAN" => random.Next(365, 3650),
            "MORTGAGE" => random.Next(1825, 10950),
            "PERSONAL LOAN" => random.Next(180, 1095),
            "CREDIT CARD" => 0,
            "OVERDRAFT" => random.Next(30, 365),
            "BULLET" => random.Next(90, 365),
            "LEASING" => random.Next(365, 1825),
            _ => random.Next(365, 1825)
        };

        var maturityDate = tenorDays == 0 ? grantDate.AddYears(99) : grantDate.AddDays(tenorDays);
        return (grantDate, maturityDate);
    }

    private decimal GenerateLimit(Random random)
    {
        var logMin = Math.Log((double)_amounts.LimitMin);
        var logMax = Math.Log((double)_amounts.LimitMax);
        var logValue = logMin + random.NextDouble() * (logMax - logMin);
        return Math.Round((decimal)Math.Exp(logValue), 2);
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

    private int GenerateInitialDpd(Random random)
    {
        // Use the same DPD model from original implementation
        int dpd;
        var shockProb = 0.05; // From DpdModel config, could be injected
        
        if (random.NextDouble() < shockProb)
        {
            dpd = (int)Math.Round(SampleNormal(random, 180.0, 60.0));
        }
        else
        {
            dpd = (int)Math.Round(SampleNormal(random, 2.0, 5.0));
        }

        return Math.Max(0, dpd);
    }

    private decimal GenerateBaseInterestRate(string segment, Random random)
    {
        var baseRate = _amounts.InterestRateBaseBySegment.GetValueOrDefault(segment, 0.08);
        var rate = baseRate + (random.NextDouble() - 0.5) * 0.01;
        return Math.Round((decimal)Math.Max(0.001, rate), 4);
    }

    private decimal CalculateInterestInSuspense(decimal totalOS, int daysPastDue, Random random)
    {
        var baseRate = _amounts.InterestInSuspenseBase;
        var per30Days = _amounts.InterestInSuspensePer30Dpd * Math.Floor(daysPastDue / 30.0);
        var noise = (random.NextDouble() - 0.5) * 2 * _amounts.InterestInSuspenseNoise;
        
        var rate = Math.Max(0, baseRate + per30Days + noise);
        return Math.Round(totalOS * (decimal)rate, 2);
    }

    private (string collateralType, decimal collateralValue) GenerateCollateral(
        string nature, string productCategory, decimal limit, Random random)
    {
        var collateralType = SampleFromDistribution(_distributions.CollateralTypes, random);
        
        var multiplier = (nature, productCategory.ToUpperInvariant()) switch
        {
            ("SECURED", _) => random.NextDouble() * 0.5 + 1.0,
            (_, "MORTGAGE") => random.NextDouble() * 0.3 + 1.2,
            _ => random.NextDouble() * 0.2 + 0.1
        };

        var collateralValue = Math.Round(limit * (decimal)multiplier, 2);
        return (collateralType, collateralValue);
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

    private static double SampleNormal(Random random, double mean, double stdDev)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = 1.0 - random.NextDouble();
        var randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + stdDev * randStdNormal;
    }

    private static string SampleFromDistribution(Dictionary<string, double> distribution, Random random)
    {
        var totalWeight = distribution.Values.Sum();
        var randomValue = random.NextDouble() * totalWeight;
        
        var cumulativeWeight = 0.0;
        foreach (var (key, weight) in distribution)
        {
            cumulativeWeight += weight;
            if (randomValue <= cumulativeWeight)
                return key;
        }
        
        return distribution.Keys.Last();
    }

    private static int CalculatePeriodsBetween(string startPeriod, string endPeriod, string frequency)
    {
        // Simple approximation - could be enhanced
        if (frequency == "Monthly")
        {
            var start = DateTime.ParseExact(startPeriod + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = DateTime.ParseExact(endPeriod + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return ((end.Year - start.Year) * 12) + end.Month - start.Month;
        }
        else if (frequency == "Quarterly")
        {
            var startParts = startPeriod.Split('Q');
            var endParts = endPeriod.Split('Q');
            var startYear = int.Parse(startParts[0]);
            var startQ = int.Parse(startParts[1]);
            var endYear = int.Parse(endParts[0]);
            var endQ = int.Parse(endParts[1]);
            return ((endYear - startYear) * 4) + (endQ - startQ);
        }
        else // Yearly
        {
            return int.Parse(endPeriod) - int.Parse(startPeriod);
        }
    }

    private static int GetPeriodsInOneYear(string frequency)
    {
        return frequency switch
        {
            "Monthly" => 12,
            "Quarterly" => 4,
            "Yearly" => 1,
            _ => 12
        };
    }
}
