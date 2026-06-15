namespace Wasnie.Application.Common.Options;

public sealed class ResendOptions
{
    public const string SectionName = "Resend";

    public string ApiKey { get; init; } = string.Empty;
    public string FromAddress { get; init; } = "Wasnie <noreply@wasnie.com>";
    public string FrontendBaseUrl { get; init; } = "http://localhost:4200";
}
