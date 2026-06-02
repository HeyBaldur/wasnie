namespace Wasnie.IntegrationTests.Compensation;

[CollectionDefinition(Name)]
public sealed class CreditAllocationServiceCollection : ICollectionFixture<CreditAllocationServiceFixture>
{
    public const string Name = "CreditAllocationService";
}
