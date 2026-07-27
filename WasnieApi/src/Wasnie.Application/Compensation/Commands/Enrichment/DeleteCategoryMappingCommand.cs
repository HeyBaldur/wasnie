using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Enrichment;

public sealed record DeleteCategoryMappingCommand(Guid Id) : IRequest<Result<bool>>;
