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
    private readonly ProductSegmentMappingOptions _productSegmentMapping;
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

    // Track the highest customer ID used so far (for adding new customers)
    private int _maxCustomerId = 0;

    public FacilityLifecycleManager(
        SeedDeriver seedDeriver,
        CustomerFactory customerFactory,
        IOptions<DistributionsOptions> distributions,
        IOptions<ProductSegmentMappingOptions> productSegmentMapping,
        IOptions<AmountsOptions> amounts,
        IOptions<LifecycleOptions> lifecycle,
        IOptions<QaRulesOptions> qaRules)
    {
        _seedDeriver = seedDeriver;
        _customerFactory = customerFactory;
        _distributions = distributions.Value;
        _productSegmentMapping = productSegmentMapping.Value;
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

        // Track the highest customer ID used
        _maxCustomerId = customerCount;

        _activeFacilitiesByPeriod[period.PeriodKey] = activeFacilities;
        _facilityStatesByPeriod[period.PeriodKey] = periodStates;
    }

    /// <summary>
    /// Evolves facilities from previous period to current period.
    /// Handles facility settlements, new facility creation, and state transitions.
    /// Adds 2 new customers each year during January.
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

        // Step 2: Add 2 new customers each year during January
        if (IsJanuaryPeriod(currentPeriod))
        {
            for (var i = 0; i < 2; i++)
            {
                _maxCustomerId++; // Increment to get new customer ID
                var newCustomerId = _maxCustomerId;
                var newCustomer = _customerFactory.CreateCustomer(newCustomerId);
                
                // Create facilities for the new customer
                for (var facilityIndex = 1; facilityIndex <= newCustomer.FacilityCount; facilityIndex++)
                {
                    var facilityNumber = _customerFactory.CreateFacilityNumber(newCustomerId, facilityIndex);
                    
                    // Create new facility master
                    var newFacilityMaster = CreateFacilityMaster(newCustomerId, facilityIndex, newCustomer, currentPeriod);
                    _facilityMasters[facilityNumber] = newFacilityMaster;
                    
                    // Create initial state for new facility
                    var initialState = CreateInitialFacilityState(newFacilityMaster, currentPeriod);
                    currentStates[facilityNumber] = initialState;
                    
                    // Add to active facilities
                    currentActiveFacilities.Add(facilityNumber);
                }
                
                // Track next facility index for this new customer
                _nextFacilityIndexByCustomer[newCustomerId] = newCustomer.FacilityCount + 1;
            }
        }

        // Step 3: Create new facilities for existing customers (if configured)
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
    /// Determines if the current period is January (for any frequency).
    /// </summary>
    private static bool IsJanuaryPeriod(PeriodInfo period)
    {
        return period.Frequency switch
        {
            "Monthly" => period.PeriodKey.EndsWith("-01"), // e.g., "2021-01", "2022-01"
            "Quarterly" => period.PeriodKey.EndsWith("Q1"), // e.g., "2021Q1", "2022Q1"
            "Yearly" => true, // All yearly periods count as January for this purpose
            _ => false
        };
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

        var branch = customer.Branch;
        var productCategory = SampleFromDistribution(_distributions.ProductCategories, random);
        
        // Determine nature based on product category
        var nature = DetermineNature(productCategory);
        
        // Get segments for this product category from mapping
        var segmentMapping = _productSegmentMapping.GetSegmentsForProduct(productCategory);
        SegmentInfo selectedSegment;
        
        if (segmentMapping.Count > 0)
        {
            // Sample from the product-specific segments using their weights
            var segmentWeights = segmentMapping.ToDictionary(s => s, s => s.Weight);
            selectedSegment = SampleFromWeightedList(segmentWeights, random);
        }
        else
        {
            // Fallback: use default segments if no mapping exists
            var pdSegment = customer.Segment;
            selectedSegment = new SegmentInfo 
            { 
                PdSegment = pdSegment, 
                LgdSegment = pdSegment 
            };
        }
        
        // Generate installment type, but set to empty if Nature is Revolving
        var installmentType = nature == "Revolving" 
            ? string.Empty 
            : SampleFromDistribution(_distributions.InstallmentTypes, random);

        var (grantDate, maturityDate) = GenerateDates(productCategory, period.PeriodEndDate, random);
        var limit = GenerateLimit(random);
        var (collateralType, collateralValue) = GenerateCollateral(nature, productCategory, selectedSegment.LgdSegment, limit, random);
        
        var baseInterestRate = GenerateBaseInterestRate(selectedSegment.PdSegment, random);

        // BUSINESS RULE: Generate Rescheduled status (consistent across all periods for the facility)
        // Values can be "Yes", "No", or empty (empty has ~10% probability)
        var rescheduled = GenerateRescheduledStatus(random);

        // BUSINESS RULE: Generate Restructured status (consistent across all periods for the facility)
        // Values can be "Yes", "No", or empty (empty has ~10% probability)
        // NoOfTimesRestructured is based on Rescheduled status (1-3 if Rescheduled="Yes", 0 otherwise)
        var (restructured, timesRestructured) = GenerateRestructuredStatus(random, rescheduled);

        // BUSINESS RULE: Generate Upgraded to Delinquency Bucket (consistent across all periods for the facility)
        // Only populated when BOTH Rescheduled = "Yes" AND Restructured = "Yes"
        // Value is between 1-4, but only ~50% of eligible facilities get a value
        var upgradedToDelinquencyBucket = GenerateUpgradedToDelinquencyBucket(random, rescheduled, restructured);

        // BUSINESS RULE: Generate Individually Impaired status (consistent across all periods for the facility)
        // Values can be "Yes", "No", or empty (~5% "Yes", ~85% "No", ~10% empty)
        var individuallyImpaired = GenerateIndividuallyImpairedStatus(random);

        return new FacilityMaster(
            facilityNumber,
            customer.CustomerNumber,
            branch,
            customer.Region,
            productCategory,
            selectedSegment.PdSegment,
            selectedSegment.LgdSegment,
            customer.Industry,
            customer.EarningType,
            nature,
            grantDate,
            maturityDate,
            installmentType,
            limit,
            collateralType,
            collateralValue,
            baseInterestRate,
            rescheduled,          // Store in immutable FacilityMaster
            restructured,         // Store in immutable FacilityMaster
            timesRestructured,    // Store in immutable FacilityMaster
            upgradedToDelinquencyBucket, // Store in immutable FacilityMaster
            individuallyImpaired, // NEW: Store in immutable FacilityMaster
            period.PeriodKey);
    }

    /// <summary>
    /// Determines the nature based on product category.
    /// Returns 'Revolving' for Credit Cards and Overdraft, 'Non-Revolving' for all others.
    /// </summary>
    private static string DetermineNature(string productCategory)
    {
        var upperCategory = productCategory.ToUpperInvariant();
        return upperCategory is "CREDIT CARD" or "CREDIT CARDS" or "OVERDRAFT" 
            ? "Revolving" 
            : "Non-Revolving";
    }

    private static (DateTime grantDate, DateTime? maturityDate) GenerateDates(string productCategory, DateTime referenceDate, Random random)
    {
        var portfolioDate = referenceDate; // Reference date is the portfolio date (period end date)
        
        // Generate maturity based on product category (tenor in days)
        // CONSTRAINT: Maximum tenor is 10 years (3650 days)
        var tenorDays = productCategory.ToUpperInvariant() switch
        {
            "TERM LOAN" => random.Next(365, 3650), // 1-10 years
            "MORTGAGE" => random.Next(1825, 3650), // 5-10 years (capped at 10 years)
            "PERSONAL LOAN" => random.Next(180, 1095), // 6 months - 3 years
            "CREDIT CARD" => 0, // Revolving
            "CREDIT CARDS" => 0, // Revolving (alternative name)
            "OVERDRAFT" => random.Next(30, 365), // 1 month - 1 year
            "BULLET" => random.Next(90, 365), // 3 months - 1 year
            "LEASE" => random.Next(365, 1825), // 1-5 years
            "LEASING" => random.Next(365, 1825), // 1-5 years (alternative name)
            "HOUSING LOAN" => random.Next(1825, 3650), // 5-10 years (capped at 10 years)
            "GOLD LOAN" => random.Next(180, 730), // 6 months - 2 years
            "SHORT TERM LOAN" => GenerateShortTermLoanTenor(random), // Special handling
            _ => random.Next(365, 1825) // Default 1-5 years
        };

        // For revolving products, set maturity to Grant Date + 10 years (maximum allowed)
        // BUT: 1% of Revolving facilities should have empty maturity date
        if (tenorDays == 0)
        {
            // Grant date: 1-5 years before portfolio date
            var revolvingDaysBeforePortfolio = random.Next(365, 1825);
            var grantDate = portfolioDate.AddDays(-revolvingDaysBeforePortfolio);
            
            // 1% of Revolving facilities have empty maturity date
            if (random.NextDouble() < 0.01)
            {
                return (grantDate, null);
            }
            
            var maturityDate = grantDate.AddYears(10); // Cap at 10 years instead of 99
            return (grantDate, maturityDate);
        }

        // Ensure tenor does not exceed 10 years (3650 days)
        tenorDays = Math.Min(tenorDays, 3650);

        // CRITICAL FIX: Grant date should be in the past, but maturity date should extend into the future
        // Calculate grant date: between 6 months and 3 years before portfolio date
        var minDaysBeforePortfolio = 180; // At least 6 months before portfolio date
        var maxDaysBeforePortfolio = Math.Min(1095, 3650); // Up to 3 years before portfolio date
        
        // Calculate grant date
        var daysBeforePortfolio = random.Next(minDaysBeforePortfolio, maxDaysBeforePortfolio + 1);
        var grantDate2 = portfolioDate.AddDays(-daysBeforePortfolio);
        
        // Maturity date = Grant date + Tenor
        var maturityDate2 = grantDate2.AddDays(tenorDays);

        // Final validation: Ensure Maturity Date <= Grant Date + 10 years
        var maxMaturityDate = grantDate2.AddYears(10);
        if (maturityDate2 > maxMaturityDate)
        {
            maturityDate2 = maxMaturityDate;
        }

        return (grantDate2, maturityDate2);
    }

    /// <summary>
    /// Generates tenor for Short Term Loans with realistic distribution:
    /// - Majority (95%) settle within 1 year (30-365 days)
    /// - Rare cases (5%) extend to 2-3 years due to non-settlement
    /// </summary>
    private static int GenerateShortTermLoanTenor(Random random)
    {
        // 95% of Short Term Loans should have tenor within 1 year
        if (random.NextDouble() < 0.95)
        {
            // Most loans: 1 month to 1 year
            return random.Next(30, 366);
        }
        else
        {
            // Rare cases: Extended loans 1-3 years (customer didn't settle on time)
            // But cap at 10 years maximum
            return random.Next(366, Math.Min(1096, 3650)); // 1-3 years, max 10 years
        }
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

    /// <summary>
    /// Creates initial facility state for the first period.
    /// </summary>
    private FacilityState CreateInitialFacilityState(FacilityMaster master, PeriodInfo period)
    {
        var random = _seedDeriver.CreateRandom($"initial:{master.FacilityNumber}:{period.PeriodKey}");

        var (totalOS, undisbursedAmount) = GenerateInitialAmounts(master.Limit, random);
        
        // BUSINESS RULE: Undisbursed Amount should only be populated for Housing Loan
        // For all other products, it should be 0 (which will be rendered as empty in CSV)
        // This value will remain constant across all periods for the facility
        if (!master.ProductCategory.Equals("Housing Loan", StringComparison.OrdinalIgnoreCase))
        {
            undisbursedAmount = 0m;
        }
        
        var daysPastDue = GenerateInitialDpd(random);
        var interestInSuspense = CalculateInterestInSuspense(totalOS, daysPastDue, random);
        
        var bucketing = GenerateBucketing(daysPastDue, master.IndividuallyImpaired, master.Restructured);

        return new FacilityState(
            master.FacilityNumber,
            period.PeriodKey,
            daysPastDue,
            totalOS,
            undisbursedAmount,
            master.BaseInterestRate,
            interestInSuspense,
            bucketing,
            IsSettled: false);
    }

    /// <summary>
    /// Determines if a facility should settle in the current period based on lifecycle rules.
    /// SPECIAL RULE: Short Term Loans MUST settle after exactly 1 year (maximum).
    /// OTHER FACILITIES: Must remain active for at least 5 years before any settlement.
    /// </summary>
    private bool ShouldSettleFacility(FacilityMaster master, FacilityState previousState, PeriodInfo currentPeriod, Random random)
    {
        // CRITICAL: Facilities should NEVER settle in the same period they were created
        if (master.StartPeriod == currentPeriod.PeriodKey)
        {
            return false;
        }

        // Calculate periods since grant for settlement checks
        var periodsSinceGrant = CalculatePeriodsBetween(master.StartPeriod, currentPeriod.PeriodKey, currentPeriod.Frequency);
        var periodsInOneYear = GetPeriodsInOneYear(currentPeriod.Frequency);
        
        // SPECIAL BUSINESS RULE: Short Term Loans MUST settle after completing 1 year
        // Check both ProductCategory AND PdSegment to ensure it's truly a Short Term Loan
        var isShortTermLoan = master.ProductCategory.Equals("Short Term Loan", StringComparison.OrdinalIgnoreCase) &&
                              master.Segment.Equals("Short Term Loan", StringComparison.OrdinalIgnoreCase);
        
        if (isShortTermLoan)
        {
            // Short Term Loans settle after exactly 1 year (12 months for monthly frequency)
            if (periodsSinceGrant >= periodsInOneYear)
            {
                return true; // MUST SETTLE - no exceptions
            }
            else
            {
                return false; // Cannot settle before 1 year
            }
        }
        
        // For all OTHER facilities: 5-year minimum retention rule applies
        var periodsInFiveYears = periodsInOneYear * 5; // 60 months for monthly frequency
        var hasCompletedFiveYears = periodsSinceGrant >= periodsInFiveYears;
        
        if (!hasCompletedFiveYears)
        {
            return false; // BLOCK ALL SETTLEMENTS before 5 years (except Short Term Loans)
        }

        // Beyond this point, non-Short Term Loan facility has completed at least 5 years
        // Now normal settlement rules apply

        // Check if maturity date has passed (if maturity date exists)
        if (master.MaturityDate.HasValue && currentPeriod.PeriodEndDate >= master.MaturityDate.Value)
        {
            return true;
        }

        // Short-term products (general rule) have higher settlement probability after 5 years
        if (_qaRules.ShortTermProducts.Contains(master.ProductCategory, StringComparer.OrdinalIgnoreCase))
        {
            if (random.NextDouble() < _lifecycle.ShortTermSettlementProbability)
            {
                return true;
            }
        }

        // High DPD facilities are likely to settle (write-off) - only after 5 years
        if (previousState.DaysPastDue >= _lifecycle.SettlementDpdThreshold)
        {
            if (random.NextDouble() < _lifecycle.HighDpdSettlementProbability)
            {
                return true;
            }
        }

        // Random settlement based on configured rate - only after 5 years
        if (random.NextDouble() < _lifecycle.FacilitySettlementRate)
        {
            return true;
        }

        return false;
    }

    private static double SampleNormal(Random random, double mean, double stdDev)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = 1.0 - random.NextDouble();
        var randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + stdDev * randStdNormal;
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

    /// <summary>
    /// Generates the Rescheduled status for a facility.
    /// BUSINESS RULE: Rescheduled can be "Yes", "No", or empty (randomly assigned, consistent across periods).
    /// Distribution: ~10% empty, ~45% "Yes", ~45% "No"
    /// </summary>
    private static string GenerateRescheduledStatus(Random random)
    {
        var value = random.NextDouble();
        
        if (value < 0.10)
        {
            return string.Empty; // ~10% probability of empty
        }
        else if (value < 0.55)
        {
            return "Yes"; // ~45% probability of "Yes"
        }
        else
        {
            return "No"; // ~45% probability of "No"
        }
    }

    /// <summary>
    /// Generates the Restructured status and times restructured for a facility.
    /// BUSINESS RULE: Restructured can be "Yes", "No", or empty (randomly assigned, consistent across periods).
    /// Distribution: ~10% empty, ~20% "Yes", ~70% "No"
    /// BUSINESS RULE: NoOfTimesRestructured should be between 1-3 ONLY if Rescheduled = "Yes"
    /// Otherwise, NoOfTimesRestructured = 0
    /// </summary>
    private static (string restructured, int timesRestructured) GenerateRestructuredStatus(Random random, string rescheduled)
    {
        var value = random.NextDouble();
        
        // Generate Restructured status
        string restructured;
        if (value < 0.10)
        {
            // ~10% probability of empty
            restructured = string.Empty;
        }
        else if (value < 0.30)
        {
            // ~20% probability of "Yes"
            restructured = "Yes";
        }
        else
        {
            // ~70% probability of "No"
            restructured = "No";
        }
        
        // Generate NoOfTimesRestructured based on Rescheduled status
        int timesRestructured;
        if (rescheduled.Equals("Yes", StringComparison.OrdinalIgnoreCase))
        {
            // If Rescheduled = "Yes", generate times restructured between 1-3
            timesRestructured = random.Next(1, 4); // Returns 1, 2, or 3
        }
        else
        {
            // If Rescheduled = "No" or empty, NoOfTimesRestructured = 0
            timesRestructured = 0;
        }
        
        return (restructured, timesRestructured);
    }

    /// <summary>
    /// Generates the Upgraded to Delinquency Bucket value for a facility.
    /// BUSINESS RULE: Value should be between 1-4, but ONLY for facilities where BOTH Rescheduled = "Yes" AND Restructured = "Yes"
    /// Not all such records need a value - only ~50% of eligible facilities get a value assigned
    /// Distribution: Empty (~50%), or 1, 2, 3, 4 (equal probability for the remaining ~50%)
    /// </summary>
    private static string GenerateUpgradedToDelinquencyBucket(Random random, string rescheduled, string restructured)
    {
        // Only assign value if BOTH Rescheduled = "Yes" AND Restructured = "Yes"
        if (!rescheduled.Equals("Yes", StringComparison.OrdinalIgnoreCase) || 
            !restructured.Equals("Yes", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }
        
        // For eligible facilities (both Rescheduled and Restructured are "Yes")
        // Only ~50% will have a value assigned
        if (random.NextDouble() < 0.50)
        {
            // Randomly assign a value between 1-4
            return random.Next(1, 5).ToString(); // Returns "1", "2", "3", or "4"
        }
        
        return string.Empty;
    }

    /// <summary>
    /// Generates the Individually Impaired status for a facility.
    /// BUSINESS RULE: Individually Impaired can be "Yes", "No", or empty (randomly assigned, consistent across periods).
    /// Distribution: ~5% "Yes", ~95% "No" or empty
    /// Example: For 100,000 records, approximately 5,000 should have the value "Yes"
    /// </summary>
    private static string GenerateIndividuallyImpairedStatus(Random random)
    {
        var value = random.NextDouble();
        
        if (value < 0.05)
        {
            return "Yes"; // ~5% probability of "Yes"
        }
        else if (value < 0.90)
        {
            return "No"; // ~85% probability of "No"
        }
        else
        {
            return string.Empty; // ~10% probability of empty
        }
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

    private (string collateralType, decimal collateralValue) GenerateCollateral(
        string nature, string productCategory, string segmentForLGD, decimal limit, Random random)
    {
        string collateralType;
        
        // Apply business rules for Collateral Type based on priority:
        // 1. Product Category specific rules
        // 2. Segment for LGD rules
        // 3. Special combinations for Lease
        
        var upperProductCategory = productCategory.ToUpperInvariant();
        
        // Rule: Overdraft and Short Term Loan should have empty Collateral Type
        if (upperProductCategory == "OVERDRAFT" || upperProductCategory == "SHORT TERM LOAN")
        {
            collateralType = string.Empty;
        }
        // Rule: Credit Cards can be either empty (70%) or Fixed Deposit (Cash Collateral) (30%)
        else if (upperProductCategory == "CREDIT CARD" || upperProductCategory == "CREDIT CARDS")
        {
            collateralType = random.NextDouble() < 0.70 
                ? string.Empty 
                : "Fixed Deposit (Cash Collateral)";
        }
        // Rule: Gold Loan should be Gold
        else if (upperProductCategory == "GOLD LOAN")
        {
            collateralType = "Gold";
        }
        // Rule: Housing Loan should be Property
        else if (upperProductCategory == "HOUSING LOAN")
        {
            collateralType = "Property";
        }
        // Rule: Lease products with specific combinations
        else if (upperProductCategory == "LEASE" || upperProductCategory == "LEASING")
        {
            // Special combinations based on Segment for LGD
            collateralType = segmentForLGD.ToUpperInvariant() switch
            {
                "NON-HYBRID" => "Car Non-Hybrid",
                "HYBRID" => "Car Hybrid",
                "LE-OTHER" => "Machinery",
                _ => "Machinery" // Default for Lease if segment doesn't match
            };
        }
        // Rule: Segment for LGD = 'Secured' ? Fixed Deposit (Cash Collateral)
        else if (segmentForLGD.Equals("Secured", StringComparison.OrdinalIgnoreCase))
        {
            collateralType = "Fixed Deposit (Cash Collateral)";
        }
        // Rule: Segment for LGD = 'Unsecured' ? Personal Guarantee
        else if (segmentForLGD.Equals("Unsecured", StringComparison.OrdinalIgnoreCase))
        {
            collateralType = "Personal Guarantee";
        }
        // Fallback: Sample from distribution (for any edge cases not covered)
        else
        {
            collateralType = SampleFromDistribution(_distributions.CollateralTypes, random);
        }

        // BUSINESS RULE: Calculate collateral value based on collateral type
        // - If Collateral Type is "Personal Guarantee" or empty ? Collateral Value = 0
        // - Otherwise ? Collateral Value must be higher than Total OS
        decimal collateralValue;
        
        // Rule 1: Personal Guarantee or empty collateral type ? value is 0
        if (string.IsNullOrEmpty(collateralType) || 
            collateralType.Equals("Personal Guarantee", StringComparison.OrdinalIgnoreCase))
        {
            collateralValue = 0m;
        }
        // Rule 2: For other collateral types, value must be > Total OS
        // Since Total OS can be up to 100% of Limit (and typically averages around 75%),
        // we need to ensure Collateral Value is always higher than the maximum possible Total OS
        // We use multipliers > 1.0 to ensure Collateral Value > Total OS
        else
        {
            double multiplier;
            
            // Generate multipliers that ensure collateral value > limit (which is always >= Total OS)
            // This guarantees collateral value will be > Total OS for all cases
            multiplier = (nature, upperProductCategory) switch
            {
                // Non-Revolving products: 1.1x to 1.8x of Limit (always > Total OS)
                ("Non-Revolving", _) => random.NextDouble() * 0.7 + 1.1,
                
                // Mortgage/Housing Loan: 1.2x to 1.6x of Limit (always > Total OS)
                (_, "MORTGAGE") or (_, "HOUSING LOAN") => random.NextDouble() * 0.4 + 1.2,
                
                // Revolving products: Total OS is typically low, so 1.0x to 1.3x is sufficient
                // But we ensure minimum 1.05x to guarantee collateral > Total OS
                ("Revolving", _) => random.NextDouble() * 0.25 + 1.05,
                
                // Other products: 1.1x to 1.5x of Limit (always > Total OS)
                _ => random.NextDouble() * 0.4 + 1.1
            };

            collateralValue = Math.Round(limit * (decimal)multiplier, 2);
            
            // DEFENSIVE: Ensure collateral value is always at least marginally higher than limit
            // This provides an extra safety margin since Total OS <= Limit
            if (collateralValue <= limit)
            {
                collateralValue = Math.Round(limit * 1.05m, 2);
            }
        }

        return (collateralType, collateralValue);
    }

    private (string individuallyImpaired, string bucketing) GenerateRiskFlags(
        int daysPastDue, Random random)
    {
        var dpdFactor = Math.Min(1.0, daysPastDue / 180.0);
        
        var individuallyImpairedProb = Math.Min(0.4, dpdFactor * 0.25);
        
        var individuallyImpaired = random.NextDouble() < individuallyImpairedProb ? "Yes" : "No";
        
        // Note: bucketing now uses a simplified logic since Restructured is no longer available here
        // It's stored in FacilityMaster and will be used in row generation
        var bucketing = (daysPastDue, individuallyImpaired) switch
        {
            ( >= 90, _) => "NPL",
            ( >= 30, _) => "Special Mention",
            (_, "Yes") => "Substandard",
            _ => "Standard"
        };

        return (individuallyImpaired, bucketing);
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

    private static T SampleFromWeightedList<T>(Dictionary<T, double> weightedItems, Random random) where T : notnull
    {
        var totalWeight = weightedItems.Values.Sum();
        var randomValue = random.NextDouble() * totalWeight;
        
        var cumulativeWeight = 0.0;
        foreach (var (item, weight) in weightedItems)
        {
            cumulativeWeight += weight;
            if (randomValue <= cumulativeWeight)
                return item;
        }
        
        return weightedItems.Keys.Last();
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
