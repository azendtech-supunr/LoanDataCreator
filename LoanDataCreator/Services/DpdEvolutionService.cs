using Microsoft.Extensions.Options;
using CsvPdGen.Config;
using CsvPdGen.Domain;

namespace CsvPdGen.Services;

/// <summary>
/// Handles realistic DPD evolution across periods.
/// Instead of randomly generating DPD each period, evolves DPD from previous period
/// based on configured probabilities of improvement, worsening, or stability.
/// 
/// CRITICAL INVARIANT: For monthly periods, NextDPD ? PreviousDPD + PeriodDaysIncrement
/// This ensures realistic period-end snapshots and prevents invalid transitions.
/// </summary>
public class DpdEvolutionService
{
    private readonly SeedDeriver _seedDeriver;
    private readonly DpdEvolutionOptions _evolution;

    public DpdEvolutionService(
        SeedDeriver seedDeriver,
        IOptions<DpdEvolutionOptions> evolution)
    {
        _seedDeriver = seedDeriver;
        _evolution = evolution.Value;
    }

    /// <summary>
    /// Evolves DPD from previous period to current period.
    /// Returns new DPD value based on evolution rules.
    /// 
    /// Evolution logic:
    /// - Improvement: DPD decreases (payment made clearing overdue days)
    /// - Worsening: DPD increases by periodDaysIncrement (no payment, time passes)
    /// - Stability: DPD increases by periodDaysIncrement (no payment, no change)
    /// 
    /// ENFORCED INVARIANT: newDpd ? previousDpd + periodDaysIncrement
    /// 
    /// Example (Monthly with 30-day periods):
    /// - Month 01 DPD: 23
    /// - Month 02 DPD: 23 + 30 = 53 (no payment)
    /// 
    /// Example (Payment scenario):
    /// - Month 01 DPD: 120
    /// - Payment clears 50 overdue days
    /// - Month 02 DPD: 120 - 50 + 30 = 100
    /// </summary>
    public int EvolveDpd(int previousDpd, string facilityNumber, string currentPeriod, int periodDaysIncrement)
    {
        var random = _seedDeriver.CreateRandom($"dpd:{facilityNumber}:{currentPeriod}");

        // Special case: DPD = 0 (current/performing) - high chance of staying current
        if (previousDpd == 0)
        {
            if (random.NextDouble() < _evolution.CurrentStayCurrentProbability)
            {
                return 0; // Stay current
            }
            else
            {
                // Become delinquent - use a small portion of period increment
                // This represents missing first payment in the period
                var initialDelinquency = (int)(periodDaysIncrement * (0.3 + random.NextDouble() * 0.4)); // 30%-70% of period
                return Math.Max(0, Math.Min(periodDaysIncrement, initialDelinquency));
            }
        }

        // For non-zero DPD, determine if it improves, worsens, or stays stable
        var actionRoll = random.NextDouble();

        if (actionRoll < _evolution.ImprovementProbability)
        {
            // DPD improves (decreases) - customer made payment clearing overdue days
            // The payment amount reduces DPD, but time still passes (adding periodDaysIncrement)
            
            // Improvement mean should be negative (e.g., -15 days represents payment amount)
            // This represents the number of overdue days actually paid
            var paymentAmount = (int)Math.Round(Math.Abs(SampleNormal(random, Math.Abs(_evolution.ImprovementMean), _evolution.ImprovementStdDev)));
            
            // Apply the formula: New DPD = Previous DPD - Payment Amount + Period Days
            // Example: 120 - 50 + 30 = 100
            var newDpd = previousDpd - paymentAmount + periodDaysIncrement;
            
            // DPD can improve to 0 (full cure) if payment covers all overdue days plus current period
            // Clamp to 0 minimum
            return Math.Max(0, newDpd);
        }
        else if (actionRoll < _evolution.ImprovementProbability + _evolution.WorseningProbability)
        {
            // DPD worsens (increases) - no payment made, time passes
            // Standard time progression: DPD increases by period increment
            // Small variation (±10%) accounts for reporting date variations
            
            var variationFactor = (random.NextDouble() - 0.5) * 0.2; // -10% to +10%
            var timeProgression = (int)(periodDaysIncrement * (1.0 + variationFactor));
            
            var newDpd = previousDpd + timeProgression;
            
            // ENFORCE UPPER BOUND: Cannot increase by more than periodDaysIncrement
            var maxAllowedDpd = previousDpd + periodDaysIncrement;
            newDpd = Math.Min(newDpd, maxAllowedDpd);
            
            return Math.Max(0, newDpd);
        }
        else
        {
            // DPD stays stable - no payment, standard time progression
            // This is the default case: customer remains delinquent, time passes
            // DPD increases by exactly the period increment
            // Example: Month 01 DPD: 23 ? Month 02 DPD: 23 + 30 = 53
            
            var newDpd = previousDpd + periodDaysIncrement;
            
            return Math.Max(0, newDpd);
        }
    }

    /// <summary>
    /// Generates initial DPD for a new facility using the original mixture model.
    /// This is only used for NEW facilities in their first period.
    /// After the first period, EvolveDpd is used instead.
    /// </summary>
    public int GenerateInitialDpd(string facilityNumber, string period, DpdModelOptions dpdModel)
    {
        var random = _seedDeriver.CreateRandom($"dpd:initial:{facilityNumber}:{period}");

        int dpd;
        
        if (random.NextDouble() < dpdModel.ShockProbability)
        {
            dpd = (int)Math.Round(SampleNormal(random, dpdModel.ShockMean, dpdModel.ShockStdDev));
        }
        else
        {
            dpd = (int)Math.Round(SampleNormal(random, dpdModel.BaseMean, dpdModel.BaseStdDev));
        }

        if (!dpdModel.AllowNegatives && dpd < 0)
            dpd = 0;

        return dpd;
    }

    /// <summary>
    /// Determines the period days increment based on frequency.
    /// Monthly: ~30 days, Quarterly: ~90 days, Yearly: ~365 days.
    /// This represents the maximum possible DPD increase per period.
    /// </summary>
    public static int GetPeriodDaysIncrement(string frequency)
    {
        return frequency switch
        {
            "Monthly" => 30,
            "Quarterly" => 90,
            "Yearly" => 365,
            _ => 30
        };
    }

    private static double SampleNormal(Random random, double mean, double stdDev)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = 1.0 - random.NextDouble();
        var randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + stdDev * randStdNormal;
    }
}
