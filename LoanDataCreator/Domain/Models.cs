namespace CsvPdGen.Domain;

public record CustomerMaster(
    string CustomerNumber,
    string Branch,           // ? ADD: Branch assigned at customer level
    string Region,       // NEW: Region associated with branch
    string Segment,
    string Industry,
    string EarningType,
    string Nature,
    int FacilityCount);

/// <summary>
/// Represents a facility's master data that persists across periods.
/// Includes all attributes that should remain constant for the facility's lifetime.
/// </summary>
public record FacilityMaster(
    string FacilityNumber,
    string CustomerNumber,
    string Branch,
    string Region,       // NEW: Region associated with branch
    string ProductCategory,
    string Segment,
    string Industry,
    string EarningType,
    string Nature,
    DateTime GrantDate,
    DateTime MaturityDate,
    string InstallmentType,
    decimal Limit,
    string CollateralType,
    decimal CollateralValue,
    decimal BaseInterestRate,
    string StartPeriod);

/// <summary>
/// Represents the state of a facility in a specific period.
/// Contains fields that can change between periods.
/// </summary>
public record FacilityState(
    string FacilityNumber,
    string Period,
    int DaysPastDue,
    decimal TotalOS,
    decimal UndisbursedAmount,
    decimal InterestRate,
    decimal InterestInSuspense,
    string Rescheduled,
    string Restructured,
    int NoOfTimesRestructured,
    string UpgradedToDelinquencyBucket,
    string IndividuallyImpaired,
    string BucketingInIndividualAssessment,
    bool IsSettled);

public record PeriodRow(
    string CustomerNumber,
    string FacilityNumber,
    string Branch,
    string Region,       // NEW: Region associated with branch
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

/// <summary>
/// Represents an ordered period in the generation sequence.
/// Used to maintain chronological ordering for lifecycle logic.
/// </summary>
public record PeriodInfo(
    string PeriodKey,
    DateTime PeriodEndDate,
    string Frequency,
    int SequenceIndex);