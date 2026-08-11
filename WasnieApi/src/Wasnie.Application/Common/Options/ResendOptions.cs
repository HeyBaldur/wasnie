namespace Wasnie.Application.Common.Options;

public sealed class ResendOptions
{
    public const string SectionName = "Resend";

    public string ApiKey { get; init; } = string.Empty;
    public string FromAddress { get; init; } = "Incentra <noreply@incentra.work>";
    public string FrontendBaseUrl { get; init; } = "http://localhost:4200";
}
