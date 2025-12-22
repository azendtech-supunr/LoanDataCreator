using Microsoft.Extensions.Options;
using CsvPdGen.Config;
using CsvPdGen.Domain;

namespace CsvPdGen.Services;

/// <summary>
/// Handles realistic DPD evolution across periods.
/// Instead of randomly generating DPD each period, evolves DPD from previous period
/// based on configured probabilities of improvement, worsening, or stability.
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
                // Small chance of becoming delinquent
                return Math.Max(0, (int)Math.Round(SampleNormal(random, 5.0, 3.0)));
            }
        }

        // For non-zero DPD, determine if it improves, worsens, or stays stable
        var actionRoll = random.NextDouble();

        if (actionRoll < _evolution.ImprovementProbability)
        {
            // DPD improves (decreases)
            var change = (int)Math.Round(SampleNormal(random, _evolution.ImprovementMean, _evolution.ImprovementStdDev));
            var newDpd = previousDpd + change; // change is negative, so this decreases DPD
            
            // DPD can improve to 0 or even become negative (which we'll clamp to 0)
            return Math.Max(0, newDpd);
        }
        else if (actionRoll < _evolution.ImprovementProbability + _evolution.WorseningProbability)
        {
            // DPD worsens (increases)
            var change = (int)Math.Round(SampleNormal(random, _evolution.WorseningMean, _evolution.WorseningStdDev));
            var newDpd = previousDpd + change;
            
            // Add period time progression (e.g., if 30 days pass, DPD naturally increases by ~30)
            newDpd += periodDaysIncrement;
            
            return Math.Max(0, newDpd);
        }
        else
        {
            // DPD stays relatively stable
            // Add time progression but with some variability
            var timeChange = (int)(periodDaysIncrement * (0.8 + random.NextDouble() * 0.4)); // 80%-120% of period days
            var newDpd = previousDpd + timeChange;
            
            return Math.Max(0, newDpd);
        }
    }

    /// <summary>
    /// Generates initial DPD for a new facility using the original mixture model.
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
