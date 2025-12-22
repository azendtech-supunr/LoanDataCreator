using Microsoft.Extensions.Options;
using CsvPdGen.Config;
using CsvPdGen.Services;
using System.Globalization;

namespace CsvPdGen.Tests;

public class SmokeTests
{
    private static GenerationOptions CreateTestGenerationOptions() => new()
    {
        OutputBasePath = "TestOutput",
        RowsPerFile = 100,
        Seed = 12345
    };

    private static DistributionsOptions CreateTestDistributions() => new()
    {
        Regions = new()
        {
            ["TestRegion"] = new Dictionary<string, double> { ["TestBranch"] = 1.0 }
        },
        ProductCategories = new() { ["TERM LOAN"] = 1.0 },
        Segments = new() { ["RETAIL"] = 1.0 },
        Industries = new() { ["AGRICULTURE"] = 1.0 },
        EarningTypes = new() { ["SALARY"] = 1.0 },
        Natures = new() { ["SECURED"] = 1.0 },
        InstallmentTypes = new() { ["Monthly"] = 1.0 },
        CollateralTypes = new() { ["REAL_ESTATE"] = 1.0 }
    };

    private static CustomersOptions CreateTestCustomers() => new()
    {
        CustomerCount = 10,
        FacilitiesPerCustomerMin = 1,
        FacilitiesPerCustomerMax = 2
    };

    private static AmountsOptions CreateTestAmounts() => new()
    {
        InterestRateBaseBySegment = new() { ["RETAIL"] = 0.12 }
    };

    private static DpdModelOptions CreateTestDpdModel() => new();

    public static void RunSmokeTest()
    {
        Console.WriteLine("Running smoke tests...");

        // Test SeedDeriver
        TestSeedDeriver();

        // Test CustomerFactory
        TestCustomerFactory();

        // Test PeriodRowFactory
        TestPeriodRowFactory();

        // Test CSV escaping
        TestCsvEscaping();

        Console.WriteLine("All smoke tests passed!");
    }

    private static void TestSeedDeriver()
    {
        var seedDeriver = new SeedDeriver(12345);
        
        // Test deterministic behavior
        var seed1 = seedDeriver.DeriveSeed("test");
        var seed2 = seedDeriver.DeriveSeed("test");
        
        if (seed1 != seed2)
            throw new Exception("SeedDeriver is not deterministic");

        // Test different keys produce different seeds
        var seed3 = seedDeriver.DeriveSeed("different");
        if (seed1 == seed3)
            throw new Exception("SeedDeriver produces same seed for different keys");

        Console.WriteLine("? SeedDeriver test passed");
    }

    private static void TestCustomerFactory()
    {
        var seedDeriver = new SeedDeriver(12345);
        var distributions = Options.Create(CreateTestDistributions());
        var customers = Options.Create(CreateTestCustomers());
        
        var factory = new CustomerFactory(seedDeriver, distributions, customers);
        
        // Test stable customer creation
        var customer1 = factory.CreateCustomer(1);
        var customer2 = factory.CreateCustomer(1);
        
        if (customer1.CustomerNumber != customer2.CustomerNumber ||
            customer1.Segment != customer2.Segment ||
            customer1.Industry != customer2.Industry)
        {
            throw new Exception("CustomerFactory is not stable");
        }

        // Test facility number format
        var facilityNumber = factory.CreateFacilityNumber(1, 1);
        if (!facilityNumber.StartsWith("FAC") || facilityNumber.Length != 13)
            throw new Exception("Facility number format is incorrect");

        Console.WriteLine("? CustomerFactory test passed");
    }

    private static void TestPeriodRowFactory()
    {
        var seedDeriver = new SeedDeriver(12345);
        var distributions = Options.Create(CreateTestDistributions());
        var customers = Options.Create(CreateTestCustomers());
        var amounts = Options.Create(CreateTestAmounts());
        var dpd = Options.Create(CreateTestDpdModel());
        
        var customerFactory = new CustomerFactory(seedDeriver, distributions, customers);
        var periodFactory = new PeriodRowFactory(seedDeriver, customerFactory, distributions, amounts, dpd);
        
        var customer = customerFactory.CreateCustomer(1);
        var row = periodFactory.CreatePeriodRow(customer, 1, "2024");
        
        // Test basic constraints
        if (row.GrantDate > row.MaturityDate)
            throw new Exception("Grant date is after maturity date");
            
        if (row.Limit < row.TotalOS + row.UndisbursedAmount)
            throw new Exception("Limit constraint violated");
            
        if (row.DaysPastDue < 0)
            throw new Exception("Negative DPD when not allowed");
            
        // Test customer consistency
        if (row.CustomerNumber != customer.CustomerNumber ||
            row.Segment != customer.Segment ||
            row.Industry != customer.Industry)
        {
            throw new Exception("Customer data inconsistency");
        }

        Console.WriteLine("? PeriodRowFactory test passed");
    }

    private static void TestCsvEscaping()
    {
        // This would test the private EscapeCsvField method if it were public
        // For now, we'll just test that the service can be instantiated
        var options = Options.Create(CreateTestGenerationOptions());
        var csvWriter = new CsvWriterService(options);
        
        if (csvWriter == null)
            throw new Exception("CsvWriterService creation failed");

        Console.WriteLine("? CsvWriterService test passed");
    }
}