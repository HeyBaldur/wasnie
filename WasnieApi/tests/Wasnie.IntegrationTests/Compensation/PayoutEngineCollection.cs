namespace Wasnie.IntegrationTests.Compensation;

[CollectionDefinition(Name)]
public sealed class PayoutEngineCollection : ICollectionFixture<PayoutEngineFixture>
{
    public const string Name = "PayoutEngine";
}
