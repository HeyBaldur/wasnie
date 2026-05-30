namespace Wasnie.IntegrationTests.Serialization;

[CollectionDefinition(Name)]
public sealed class MoneyRoundTripCollection : ICollectionFixture<MoneyRoundTripFixture>
{
    public const string Name = "Money Round-Trip Tests";
}
