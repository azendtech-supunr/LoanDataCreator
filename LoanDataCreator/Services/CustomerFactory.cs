using Microsoft.Extensions.Options;
using CsvPdGen.Config;
using CsvPdGen.Domain;

namespace CsvPdGen.Services;

public class CustomerFactory
{
    private readonly SeedDeriver _seedDeriver;
    private readonly DistributionsOptions _distributions;
    private readonly CustomersOptions _customers;

    public CustomerFactory(
        SeedDeriver seedDeriver,
        IOptions<DistributionsOptions> distributions,
        IOptions<CustomersOptions> customers)
    {
        _seedDeriver = seedDeriver;
        _distributions = distributions.Value;
        _customers = customers.Value;
    }

    /// <summary>
    /// Creates a stable CustomerMaster for the given customer ID.
    /// All attributes will be the same for this customer ID regardless of when this method is called.
    /// </summary>
    public CustomerMaster CreateCustomer(int customerId)
    {
        var random = _seedDeriver.CreateCustomerRandom(customerId);
        var facilityRandom = _seedDeriver.CreateCustomerFacilityRandom(customerId);

        var customerNumber = $"CUST{customerId:D8}";
        
        // Get all branches (flattened from regions if configured)
        var allBranches = _distributions.GetAllBranches();
        var branch = SampleFromDistribution(allBranches, random);
        
        // Get region for this branch
        var branchToRegion = _distributions.GetBranchToRegionMapping();
        var region = branchToRegion.GetValueOrDefault(branch, "UNASSIGNED");
        
        var segment = SampleFromDistribution(_distributions.Segments, random);
        var industry = SampleFromDistribution(_distributions.Industries, random);
        var earningType = SampleFromDistribution(_distributions.EarningTypes, random);
        var nature = SampleFromDistribution(_distributions.Natures, random);
        
        var facilityCount = facilityRandom.Next(_customers.FacilitiesPerCustomerMin, _customers.FacilitiesPerCustomerMax + 1);

        return new CustomerMaster(
            customerNumber,
            branch,
            region,              // NEW: Include region
            segment,
            industry,
            earningType,
            nature,
            facilityCount);
    }

    /// <summary>
    /// Generates a stable facility number for a customer and facility index.
    /// Format preserves leading zeros and is consistent for the same customer/facility combination.
    /// </summary>
    public string CreateFacilityNumber(int customerId, int facilityIndex)
    {
        return $"FAC{customerId:D8}{facilityIndex:D2}";
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
        
        // Fallback to last item (shouldn't happen with proper weights)
        return distribution.Keys.Last();
    }
}