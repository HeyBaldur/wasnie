namespace Wasnie.IntegrationTests.Transactions;

[CollectionDefinition(Name)]
public sealed class CompensationTransactionCollection : ICollectionFixture<CompensationTransactionFixture>
{
    public const string Name = "Compensation Transaction Tests";
}
