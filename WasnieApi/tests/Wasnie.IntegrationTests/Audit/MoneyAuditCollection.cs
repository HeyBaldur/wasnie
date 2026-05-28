namespace Wasnie.IntegrationTests.Audit;

[CollectionDefinition(Name)]
public sealed class MoneyAuditCollection : ICollectionFixture<MoneyAuditTestFixture>
{
    public const string Name = "Money Audit Tests";
}
