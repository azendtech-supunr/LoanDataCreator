namespace CsvPdGen.Config;

public class GenerationOptions
{
    public string OutputBasePath { get; set; } = "Output";
    public int RowsPerFile { get; set; } = 1_000_000;
    public bool EmitBom { get; set; } = false;
    public bool EnableGzip { get; set; } = false;
    public int Seed { get; set; } = 424242;
    public int LogProgressEveryNRows { get; set; } = 100_000;
}

public class FrequenciesOptions
{
    public YearlyFrequencyConfig Yearly { get; set; } = new();
    public QuarterlyFrequencyConfig Quarterly { get; set; } = new();
    public MonthlyFrequencyConfig Monthly { get; set; } = new();
}

public class FrequencyConfig
{
    public int FilesPerPeriodMin { get; set; } = 1;
    public int FilesPerPeriodMax { get; set; } = 3;
    public bool Enabled { get; set; } = false;
}

public class YearlyFrequencyConfig : FrequencyConfig
{
    public int[] Years { get; set; } = [2022, 2023, 2024, 2025];
}

public class QuarterlyFrequencyConfig : FrequencyConfig
{
    public int[] Years { get; set; } = [2024, 2025];
}

public class MonthlyFrequencyConfig : FrequencyConfig
{
    public string StartMonth { get; set; } = "2024-01";
    public int MonthCount { get; set; } = 24;
}

public class CustomersOptions
{
    public int CustomerCount { get; set; } = 50_000;
    public int FacilitiesPerCustomerMin { get; set; } = 1;
    public int FacilitiesPerCustomerMax { get; set; } = 3;
}

public class DistributionsOptions
{
    public Dictionary<string, double> Branches { get; set; } = new();
    public Dictionary<string, double> ProductCategories { get; set; } = new();
    public Dictionary<string, double> Segments { get; set; } = new();
    public Dictionary<string, double> Industries { get; set; } = new();
    public Dictionary<string, double> EarningTypes { get; set; } = new();
    public Dictionary<string, double> Natures { get; set; } = new();
    public Dictionary<string, double> InstallmentTypes { get; set; } = new();
    public Dictionary<string, double> CollateralTypes { get; set; } = new();
}

public class AmountsOptions
{
    public decimal LimitMin { get; set; } = 10_000m;
    public decimal LimitMax { get; set; } = 10_000_000m;
    public double TotalOsFractionMean { get; set; } = 0.75;
    public double TotalOsFractionStdDev { get; set; } = 0.15;
    public double UndisbursedFractionMean { get; set; } = 0.1;
    public double UndisbursedFractionStdDev { get; set; } = 0.05;
    public Dictionary<string, double> InterestRateBaseBySegment { get; set; } = new();
    public double InterestRateVolatility { get; set; } = 0.002;
    public double InterestInSuspenseBase { get; set; } = 0.001;
    public double InterestInSuspensePer30Dpd { get; set; } = 0.01;
    public double InterestInSuspenseNoise { get; set; } = 0.0001;
    public double NegativeOsProbability { get; set; } = 0.001;
    public double NegativeOsMaxFraction { get; set; } = 0.05;
    public double TotalOsMaxChangePerPeriod { get; set; } = 0.15;
}

public class DpdModelOptions
{
    public double BaseMean { get; set; } = 2.0;
    public double BaseStdDev { get; set; } = 5.0;
    public double ShockProbability { get; set; } = 0.05;
    public double ShockMean { get; set; } = 180.0;
    public double ShockStdDev { get; set; } = 60.0;
    public bool AllowNegatives { get; set; } = false;
}

/// <summary>
/// Configuration for lifecycle-consistent facility management across periods.
/// Controls how facilities persist, settle, and evolve over time.
/// </summary>
public class LifecycleOptions
{
    /// <summary>
    /// Percentage of facilities that should settle (end) each period (0.0 to 1.0).
    /// Default: 0.05 (5% of facilities settle per period).
    /// </summary>
    public double FacilitySettlementRate { get; set; } = 0.05;

    /// <summary>
    /// Percentage of customers that should receive new facilities each period (0.0 to 1.0).
    /// Default: 0.03 (3% of customers get new facilities per period).
    /// </summary>
    public double NewFacilityRate { get; set; } = 0.03;

    /// <summary>
    /// Maximum number of new facilities a customer can receive in a single period.
    /// Default: 2.
    /// </summary>
    public int MaxNewFacilitiesPerCustomer { get; set; } = 2;

    /// <summary>
    /// Probability that a short-term loan settles within one year (0.0 to 1.0).
    /// Default: 0.95 (95% settle within a year, 5% may extend).
    /// </summary>
    public double ShortTermSettlementProbability { get; set; } = 0.95;

    /// <summary>
    /// DPD threshold above which facilities are likely to settle (write-off/closure).
    /// Default: 180 days.
    /// </summary>
    public int SettlementDpdThreshold { get; set; } = 180;

    /// <summary>
    /// Probability that a facility with DPD above threshold settles in the next period (0.0 to 1.0).
    /// Default: 0.30 (30% of severely delinquent facilities settle).
    /// </summary>
    public double HighDpdSettlementProbability { get; set; } = 0.30;
}

/// <summary>
/// Configuration for realistic DPD evolution across periods.
/// Controls how DPD values change over time instead of being randomly regenerated.
/// </summary>
public class DpdEvolutionOptions
{
    /// <summary>
    /// Probability that DPD improves (decreases) from one period to the next (0.0 to 1.0).
    /// Default: 0.40 (40% chance of improvement).
    /// </summary>
    public double ImprovementProbability { get; set; } = 0.40;

    /// <summary>
    /// Probability that DPD worsens (increases) from one period to the next (0.0 to 1.0).
    /// Default: 0.30 (30% chance of worsening).
    /// </summary>
    public double WorseningProbability { get; set; } = 0.30;

    /// <summary>
    /// Probability that DPD remains stable from one period to the next (0.0 to 1.0).
    /// Calculated as: 1.0 - ImprovementProbability - WorseningProbability.
    /// </summary>
    public double StabilityProbability => 1.0 - ImprovementProbability - WorseningProbability;

    /// <summary>
    /// Mean DPD change when improving (should be negative).
    /// Default: -15 days.
    /// </summary>
    public double ImprovementMean { get; set; } = -15.0;

    /// <summary>
    /// Standard deviation for DPD improvement.
    /// Default: 10 days.
    /// </summary>
    public double ImprovementStdDev { get; set; } = 10.0;

    /// <summary>
    /// Mean DPD change when worsening (should be positive).
    /// Default: +20 days.
    /// </summary>
    public double WorseningMean { get; set; } = 20.0;

    /// <summary>
    /// Standard deviation for DPD worsening.
    /// Default: 15 days.
    /// </summary>
    public double WorseningStdDev { get; set; } = 15.0;

    /// <summary>
    /// Probability that current-period performing loans (DPD=0) remain current (0.0 to 1.0).
    /// Default: 0.95 (95% of current loans stay current).
    /// </summary>
    public double CurrentStayCurrentProbability { get; set; } = 0.95;

    /// <summary>
    /// Number of periods (months/quarters/years) to add to DPD for time progression.
    /// Monthly: 30 days, Quarterly: 90 days, Yearly: 365 days.
    /// This is frequency-dependent and calculated based on period type.
    /// </summary>
    public int PeriodDaysIncrement { get; set; } = 30;
}

/// <summary>
/// Configuration for QA rules and domain constraints.
/// Ensures generated data follows banking business rules.
/// </summary>
public class QaRulesOptions
{
    /// <summary>
    /// Product categories that support Hybrid segments for LGD.
    /// Default: Only "LEASING" allows Hybrid.
    /// </summary>
    public string[] HybridAllowedProducts { get; set; } = ["LEASING"];

    /// <summary>
    /// Product categories considered revolving (no fixed repayment schedule).
    /// Default: CREDIT CARD, OVERDRAFT.
    /// </summary>
    public string[] RevolvingProducts { get; set; } = ["CREDIT CARD", "OVERDRAFT"];

    /// <summary>
    /// Product categories considered short-term (typically settle within one year).
    /// Default: BULLET, OVERDRAFT.
    /// </summary>
    public string[] ShortTermProducts { get; set; } = ["BULLET", "OVERDRAFT"];

    /// <summary>
    /// Mapping of collateral types that should be used for specific natures/products.
    /// Key format: "Nature|ProductCategory" or just "Nature" or "ProductCategory".
    /// </summary>
    public Dictionary<string, string[]> CollateralMapping { get; set; } = new()
    {
        ["SECURED|VEHICLE"] = ["VEHICLE"],
        ["SECURED|MORTGAGE"] = ["REAL_ESTATE"],
        ["SECURED"] = ["REAL_ESTATE", "VEHICLE", "MACHINERY", "GOLD_JEWELRY", "FIXED_DEPOSITS"],
        ["UNSECURED"] = ["OTHER", "CASH"]
    };

    /// <summary>
    /// DPD threshold above which Interest in Suspense must be non-zero.
    /// Default: 90 days.
    /// </summary>
    public int InterestInSuspenseDpdThreshold { get; set; } = 90;

    /// <summary>
    /// Whether to enforce that Limit must remain constant across periods for the same facility.
    /// Default: true.
    /// </summary>
    public bool EnforceLimitConstancy { get; set; } = true;

    /// <summary>
    /// Whether to enforce that restructured flags can only increase (never decrease) across periods.
    /// Default: true.
    /// </summary>
    public bool EnforceRestructuredMonotonicity { get; set; } = true;
}