using System.Security.Cryptography;
using System.Text;

namespace CsvPdGen.Services;

public class SeedDeriver
{
    private readonly int _rootSeed;

    public SeedDeriver(int rootSeed)
    {
        _rootSeed = rootSeed;
    }

    /// <summary>
    /// Derives a deterministic seed from the root seed and a key string.
    /// Uses SHA256 to ensure good distribution and deterministic results.
    /// </summary>
    public int DeriveSeed(string key)
    {
        var input = $"{_rootSeed}:{key}";
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(inputBytes);
        
        // Convert first 4 bytes to int
        return BitConverter.ToInt32(hashBytes, 0);
    }

    /// <summary>
    /// Creates a Random instance with a derived seed for the given key.
    /// </summary>
    public Random CreateRandom(string key)
    {
        return new Random(DeriveSeed(key));
    }

    /// <summary>
    /// Derives a seed for a specific customer ID.
    /// </summary>
    public Random CreateCustomerRandom(int customerId)
    {
        return CreateRandom($"customer:{customerId}");
    }

    /// <summary>
    /// Derives a seed for a specific period and file combination.
    /// </summary>
    public Random CreatePeriodRandom(string periodKey, int fileIndex)
    {
        return CreateRandom($"period:{periodKey}:file:{fileIndex}");
    }

    /// <summary>
    /// Derives a seed for a specific customer's facilities.
    /// </summary>
    public Random CreateCustomerFacilityRandom(int customerId)
    {
        return CreateRandom($"customer:{customerId}:facilities");
    }

    /// <summary>
    /// Derives a seed for a specific customer and facility in a period.
    /// </summary>
    public Random CreatePeriodCustomerRandom(int customerId, int facilityIndex, string periodKey)
    {
        return CreateRandom($"customer:{customerId}:facility:{facilityIndex}:period:{periodKey}");
    }
}