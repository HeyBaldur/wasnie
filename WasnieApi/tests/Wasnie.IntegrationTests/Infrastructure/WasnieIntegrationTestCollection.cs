namespace Wasnie.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class WasnieIntegrationTestCollection : ICollectionFixture<TestDatabaseFixture>
{
    public const string Name = "Wasnie Integration Tests";
}
