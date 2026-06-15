using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Common.Interfaces;

public interface IStripeWebhookService
{
    Task<Result<bool>> ProcessAsync(
        string json,
        string stripeSignature,
        CancellationToken cancellationToken = default);
}
