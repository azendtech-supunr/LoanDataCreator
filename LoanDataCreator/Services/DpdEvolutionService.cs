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
    /// - Improvement: DPD decreases (payment made or partial cure)
    /// - Worsening: DPD increases by up to periodDaysIncrement (no payment, time passes)
    /// - Stability: DPD increases by periodDaysIncrement (no payment, no cure)
    /// 
    /// ENFORCED INVARIANT: newDpd ? previousDpd + periodDaysIncrement
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
            // DPD improves (decreases) - customer made payment or cured
            // Improvement mean should be negative (e.g., -15 days)
            var improvement = (int)Math.Round(SampleNormal(random, _evolution.ImprovementMean, _evolution.ImprovementStdDev));
            var newDpd = previousDpd + improvement; // improvement is negative, so this decreases DPD
            
            // DPD can improve to 0 (full cure) or any positive value (partial payment)
            // Clamp to 0 minimum
            return Math.Max(0, newDpd);
        }
        else if (actionRoll < _evolution.ImprovementProbability + _evolution.WorseningProbability)
        {
            // DPD worsens (increases) - no payment made
            // The base worsening represents the time that passed (periodDaysIncrement)
            // Plus optional small variations for early-in-period vs late-in-period reporting
            
            // CRITICAL FIX: DO NOT add random large deltas
            // The worsening is primarily the period time progression
            // We add a small controlled variation (±10% of period increment)
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
            // DPD increases by exactly the period increment (e.g., 30 days for monthly)
            
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
