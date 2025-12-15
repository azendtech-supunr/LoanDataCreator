namespace CsvPdGen.Domain;

public record CustomerMaster(
    string CustomerNumber,
    string Segment,
    string Industry,
    string EarningType,
    string Nature,
    int FacilityCount);

public record PeriodRow(
    string CustomerNumber,
    string FacilityNumber,
    string Branch,
    string ProductCategory,
    string Segment,
    string Industry,
    string EarningType,
    string Nature,
    DateTime GrantDate,
    DateTime MaturityDate,
    decimal InterestRate,
    string InstallmentType,
    int DaysPastDue,
    decimal Limit,
    decimal TotalOS,
    decimal UndisbursedAmount,
    decimal InterestInSuspense,
    string CollateralType,
    decimal CollateralValue,
    string Rescheduled,
    string Restructured,
    int NoOfTimesRestructured,
    string UpgradedToDelinquencyBucket,
    string IndividuallyImpaired,
    string BucketingInIndividualAssessment,
    string Period);

public record WorkItem(
    string Frequency,
    string PeriodKey,
    int FileIndex,
    string FilePath,
    int Rows);