using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;

namespace Wasnie.Infrastructure.Services.Email;

public sealed class ResendEmailService(
    IHttpClientFactory httpClientFactory,
    IOptions<ResendOptions> options,
    ILogger<ResendEmailService> logger)
    : IEmailService
{
    private const string ResendApiUrl = "https://api.resend.com/emails";

    public Task SendEmailConfirmationAsync(
        string to,
        string firstName,
        string confirmationUrl,
        string language,
        CancellationToken cancellationToken = default)
    {
        var (subject, html) = EmailTemplates.Confirmation(firstName, confirmationUrl, language);
        return SendAsync(to, subject, html, cancellationToken);
    }

    public Task SendPasswordResetAsync(
        string to,
        string firstName,
        string resetUrl,
        string language,
        CancellationToken cancellationToken = default)
    {
        var (subject, html) = EmailTemplates.PasswordReset(firstName, resetUrl, language);
        return SendAsync(to, subject, html, cancellationToken);
    }

    public Task SendEmailChangeConfirmationAsync(
        string to,
        string firstName,
        string confirmationUrl,
        string language,
        CancellationToken cancellationToken = default)
    {
        var (subject, html) = EmailTemplates.EmailChangeConfirmation(firstName, confirmationUrl, language);
        return SendAsync(to, subject, html, cancellationToken);
    }

    private async Task SendAsync(string to, string subject, string html, CancellationToken cancellationToken)
    {
        var opts = options.Value;

        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            logger.LogWarning("Resend:ApiKey is not configured. Email to {To} was NOT sent.", to);
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            from = opts.FromAddress,
            to = new[] { to },
            subject,
            html,
        });

        var client = httpClientFactory.CreateClient("Resend");
        using var req = new HttpRequestMessage(HttpMethod.Post, ResendApiUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiKey);
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(req, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError(
                "Resend API returned {StatusCode} for {To}: {Body}",
                (int)response.StatusCode, to, body);
            throw new InvalidOperationException($"Email delivery failed ({(int)response.StatusCode}).");
        }

        logger.LogInformation("Email sent via Resend to {To} — subject: {Subject}", to, subject);
    }
}
