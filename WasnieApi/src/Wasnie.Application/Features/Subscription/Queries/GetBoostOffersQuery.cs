using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Subscription.Queries;

/// <summary>The boost packs this tenant can buy right now (KAN-83).</summary>
public sealed record GetBoostOffersQuery : IRequest<Result<IReadOnlyList<BoostOfferDto>>>;

/// <param name="Tokens">How many assistant tokens the pack grants.</param>
/// <param name="AmountCents">Price in the currency's smallest unit — formatted by the client, never here.</param>
public sealed record BoostOfferDto(string PriceId, long Tokens, long AmountCents, string Currency);

public sealed class GetBoostOffersHandler(IStripeBoostService boosts)
    : IRequestHandler<GetBoostOffersQuery, Result<IReadOnlyList<BoostOfferDto>>>
{
    public async Task<Result<IReadOnlyList<BoostOfferDto>>> Handle(
        GetBoostOffersQuery request, CancellationToken cancellationToken)
    {
        var offers = await boosts.GetOffersAsync(cancellationToken);

        // ★ AN EMPTY LIST IS A VALID ANSWER, not a failure: boosts may simply not be configured yet. The client hides
        // the buy button rather than showing an error for something the user did nothing wrong to hit.
        return Result<IReadOnlyList<BoostOfferDto>>.Success(
            offers.Select(o => new BoostOfferDto(o.PriceId, o.Tokens, o.AmountCents, o.Currency)).ToList());
    }
}
