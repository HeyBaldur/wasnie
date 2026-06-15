using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Onboarding.Commands;

public sealed record CompleteQualificationCommand(
    string Country,
    string PhoneNumber,
    string HowHeardAboutUs,
    string SalesVolumeRange,
    string CurrentSystem,
    bool LegalAccepted) : IRequest<Result<bool>>;
