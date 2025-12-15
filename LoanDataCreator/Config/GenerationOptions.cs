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