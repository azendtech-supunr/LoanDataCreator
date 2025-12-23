using Microsoft.Extensions.Options;
using CsvPdGen.Config;
using CsvPdGen.Domain;
using System.Globalization;

namespace CsvPdGen.Services;

public class PeriodRowFactory
{
    private readonly SeedDeriver _seedDeriver;
    private readonly CustomerFactory _customerFactory;
    private readonly DistributionsOptions _distributions;
    private readonly AmountsOptions _amounts;
    private readonly DpdModelOptions _dpdModel;

    public PeriodRowFactory(
        SeedDeriver seedDeriver,
        CustomerFactory customerFactory,
        IOptions<DistributionsOptions> distributions,
        IOptions<AmountsOptions> amounts,
        IOptions<DpdModelOptions> dpdModel)
    {
        _seedDeriver = seedDeriver;
        _customerFactory = customerFactory;
        _distributions = distributions.Value;
        _amounts = amounts.Value;
        _dpdModel = dpdModel.Value;
    }

    /// <summary>
    /// Creates a fully populated period row for a customer and facility.
    /// </summary>
    public PeriodRow CreatePeriodRow(CustomerMaster customer, int facilityIndex, string periodKey)
    {
        var random = _seedDeriver.CreatePeriodCustomerRandom(
            int.Parse(customer.CustomerNumber[4..]), facilityIndex, periodKey);

        var facilityNumber = _customerFactory.CreateFacilityNumber(
            int.Parse(customer.CustomerNumber[4..]), facilityIndex);

        // Use customer's branch and region instead of generating new ones
        var branch = customer.Branch;
        var region = customer.Region;
        
        var productCategory = SampleFromDistribution(_distributions.ProductCategories, random);
        
        // Determine nature based on product category
        var nature = DetermineNature(productCategory);
        
        // Generate installment type, but set to empty if Nature is Revolving
        var installmentType = nature == "Revolving" 
            ? string.Empty 
            : SampleFromDistribution(_distributions.InstallmentTypes, random);

        // Generate dates based on product category
        var (grantDate, maturityDate) = GenerateDates(productCategory, periodKey, random);

        // Generate amounts with consistency constraints
        var limit = GenerateLimit(random);
        var (totalOS, undisbursedAmount) = GenerateAmounts(limit, random);

        // Generate interest rate based on segment
        var interestRate = GenerateInterestRate(customer.Segment, periodKey, random);

        // Generate DPD using mixture model
        var daysPastDue = GenerateDaysPastDue(random);

        // Calculate interest in suspense
        var interestInSuspense = CalculateInterestInSuspense(totalOS, daysPastDue, random);

        // Generate collateral info
        var (collateralType, collateralValue) = GenerateCollateral(nature, productCategory, limit, random);

        // Generate risk flags based on DPD and other factors
        var (rescheduled, restructured, timesRestructured, upgraded, individuallyImpaired, bucketing) = 
            GenerateRiskFlags(daysPastDue, random);

        return new PeriodRow(
            customer.CustomerNumber,
            facilityNumber,
            branch,
            region,
            productCategory,
            customer.Segment,
            customer.SegmentForLGD,
            customer.Industry,
            customer.EarningType,
            nature,
            grantDate,
            maturityDate,
            interestRate,
            string.Empty, // No. of Installments in Arrears
            string.Empty, // Total Remaining Installments (Including Installments in Arrears)
            string.Empty, // Installments Value
            installmentType,
            daysPastDue,
            limit,
            totalOS,
            undisbursedAmount,
            interestInSuspense,
            collateralType,
            collateralValue,
            rescheduled,
            restructured,
            timesRestructured,
            upgraded,
            individuallyImpaired,
            bucketing,
            periodKey);
    }

    private static string SampleFromDistribution(Dictionary<string, double> distribution, Random random)
    {
        if (distribution.Count == 0)
            throw new InvalidOperationException("Distribution cannot be empty");

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

    private (DateTime grantDate, DateTime? maturityDate) GenerateDates(string productCategory, string periodKey, Random random)
    {
        // Parse period to get portfolio date (period end date)
        var portfolioDate = ParsePeriodToDate(periodKey);
        
        // Generate maturity based on product category (tenor in days)
        // CONSTRAINT: Maximum tenor is 10 years (3650 days)
        var tenorDays = productCategory.ToUpperInvariant() switch
        {
            "TERM LOAN" => random.Next(365, 3650), // 1-10 years
            "MORTGAGE" => random.Next(1825, 3650), // 5-10 years (capped at 10 years)
            "PERSONAL LOAN" => random.Next(180, 1095), // 6 months - 3 years
            "CREDIT CARD" or "CREDIT CARDS" => 0, // Revolving
            "OVERDRAFT" => random.Next(30, 365), // 1 month - 1 year
            "BULLET" => random.Next(90, 365), // 3 months - 1 year
            "SHORT TERM LOAN" => random.Next(30, 365), // 1 month - 1 year
            "LEASE" or "LEASING" => random.Next(365, 1825), // 1-5 years
            "HOUSING LOAN" => random.Next(1825, 3650), // 5-10 years (capped at 10 years)
            "GOLD LOAN" => random.Next(180, 730), // 6 months - 2 years
            _ => random.Next(365, 1825) // Default 1-5 years
        };

        // For revolving products, set maturity to Grant Date + 10 years (maximum allowed)
        // BUT: 1% of Revolving facilities should have empty maturity date
        if (tenorDays == 0)
        {
            // Grant date: 1-5 years before portfolio date
            var daysBeforePortfolio = random.Next(365, 1825);
            var grantDate = portfolioDate.AddDays(-daysBeforePortfolio);
            
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

        // Calculate grant date to ensure: Grant Date < Maturity Date <= Portfolio Date
        // We want grant date to be reasonably before the portfolio date
        // Minimum: tenor + 30 days before portfolio date to ensure maturity fits
        var minDaysBeforePortfolio = tenorDays + 30;
        var maxDaysBeforePortfolio = Math.Min(3650, tenorDays + 1825); // Up to tenor + 5 years, max 10 years total

        // Ensure we have a valid range
        if (minDaysBeforePortfolio >= maxDaysBeforePortfolio)
        {
            maxDaysBeforePortfolio = minDaysBeforePortfolio + 365; // Add at least 1 year range
        }

        // Calculate grant date
        var daysBeforeGrant = random.Next(minDaysBeforePortfolio, maxDaysBeforePortfolio);
        var grantDate2 = portfolioDate.AddDays(-daysBeforeGrant);
        var maturityDate2 = grantDate2.AddDays(tenorDays);

        // Safety check: Ensure maturity date is on or before portfolio date
        if (maturityDate2 > portfolioDate)
        {
            // Adjust grant date backwards to fit the constraint
            var daysToAdjust = (maturityDate2 - portfolioDate).Days + 1;
            grantDate2 = grantDate2.AddDays(-daysToAdjust);
            maturityDate2 = grantDate2.AddDays(tenorDays);
        }

        // Final validation: Ensure Maturity Date <= Grant Date + 10 years
        var maxMaturityDate = grantDate2.AddYears(10);
        if (maturityDate2 > maxMaturityDate)
        {
            maturityDate2 = maxMaturityDate;
        }

        return (grantDate2, maturityDate2);
    }

    private static DateTime ParsePeriodToDate(string periodKey)
    {
        if (periodKey.Contains('Q'))
        {
            // Quarterly: 2024Q1 -> 2024-03-31
            var parts = periodKey.Split('Q');
            var year = int.Parse(parts[0]);
            var quarter = int.Parse(parts[1]);
            return new DateTime(year, quarter * 3, DateTime.DaysInMonth(year, quarter * 3));
        }
        else if (periodKey.Contains('-'))
        {
            // Monthly: 2024-03 -> 2024-03-31
            var date = DateTime.ParseExact(periodKey + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
        }
        else
        {
            // Yearly: 2024 -> 2024-12-31
            var year = int.Parse(periodKey);
            return new DateTime(year, 12, 31);
        }
    }

    private decimal GenerateLimit(Random random)
    {
        var logMin = Math.Log((double)_amounts.LimitMin);
        var logMax = Math.Log((double)_amounts.LimitMax);
        var logValue = logMin + random.NextDouble() * (logMax - logMin);
        var value = Math.Exp(logValue);
        
        return Math.Round((decimal)value, 2);
    }

    private (decimal totalOS, decimal undisbursedAmount) GenerateAmounts(decimal limit, Random random)
    {
        // Generate total OS as fraction of limit
        var osFraction = Math.Max(0, Math.Min(1, 
            _amounts.TotalOsFractionMean + (random.NextDouble() - 0.5) * 2 * _amounts.TotalOsFractionStdDev));
        var totalOS = Math.Round(limit * (decimal)osFraction, 2);

        // Generate undisbursed as fraction of remaining limit
        var remainingLimit = limit - totalOS;
        var undisbursedFraction = Math.Max(0, Math.Min(1,
            _amounts.UndisbursedFractionMean + (random.NextDouble() - 0.5) * 2 * _amounts.UndisbursedFractionStdDev));
        var undisbursedAmount = Math.Round(remainingLimit * (decimal)undisbursedFraction, 2);

        // Ensure constraint: Limit >= TotalOS + Undisbursed
        if (totalOS + undisbursedAmount > limit)
        {
            undisbursedAmount = limit - totalOS;
        }

        return (totalOS, undisbursedAmount);
    }

    private decimal GenerateInterestRate(string segment, string periodKey, Random random)
    {
        var baseRate = _amounts.InterestRateBaseBySegment.GetValueOrDefault(segment, 0.08);
        
        // Add period-based volatility (random walk simulation)
        var periodHash = Math.Abs(periodKey.GetHashCode()) % 1000;
        var periodVolatility = (periodHash / 1000.0 - 0.5) * 2 * _amounts.InterestRateVolatility;
        
        var rate = baseRate + periodVolatility + (random.NextDouble() - 0.5) * 0.01;
        
        return Math.Round((decimal)Math.Max(0.001, rate), 4);
    }

    private int GenerateDaysPastDue(Random random)
    {
        int dpd;
        
        if (random.NextDouble() < _dpdModel.ShockProbability)
        {
            // Shock scenario - high DPD
            dpd = (int)Math.Round(SampleNormal(random, _dpdModel.ShockMean, _dpdModel.ShockStdDev));
        }
        else
        {
            // Normal scenario - low DPD
            dpd = (int)Math.Round(SampleNormal(random, _dpdModel.BaseMean, _dpdModel.BaseStdDev));
        }

        if (!_dpdModel.AllowNegatives && dpd < 0)
            dpd = 0;

        return dpd;
    }

    private static double SampleNormal(Random random, double mean, double stdDev)
    {
        // Box-Muller transformation
        static double NextGaussian(Random rand)
        {
            var u1 = 1.0 - rand.NextDouble();
            var u2 = 1.0 - rand.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        }

        return mean + stdDev * NextGaussian(random);
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
            ("Non-Revolving", _) => random.NextDouble() * 0.5 + 1.0,
            (_, "MORTGAGE") => random.NextDouble() * 0.3 + 1.2,
            ("Revolving", _) => random.NextDouble() * 0.2 + 0.1,
            _ => random.NextDouble() * 0.3 + 0.5
        };

        var collateralValue = Math.Round(limit * (decimal)multiplier, 2);
        return (collateralType, collateralValue);
    }

    private (string rescheduled, string restructured, int timesRestructured, 
             string upgraded, string individuallyImpaired, string bucketing) GenerateRiskFlags(
        int daysPastDue, Random random)
    {
        // Risk probabilities increase with DPD
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
            // Small chance of multiple restructurings
            while (random.NextDouble() < 0.1 && timesRestructured < 5)
                timesRestructured++;
        }

        // Upgraded to delinquency bucket approximation
        var upgraded = (daysPastDue >= 30 && random.NextDouble() < 0.7) ? "Yes" : "No";
        
        var individuallyImpaired = random.NextDouble() < individuallyImpairedProb ? "Yes" : "No";
        
        // Bucketing derived from other flags and DPD
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