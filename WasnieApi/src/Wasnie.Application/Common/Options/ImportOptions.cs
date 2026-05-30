namespace Wasnie.Application.Common.Options;

public sealed class ImportOptions
{
    public const string SectionName = "Imports";

    public int TransactionMaxRows { get; init; } = 10_000;
    public int PayeeMaxRows { get; init; } = 300;
}
