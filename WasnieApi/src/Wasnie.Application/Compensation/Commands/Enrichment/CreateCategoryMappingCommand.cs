using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Enrichment;

public sealed record CreateCategoryMappingCommand(
    string InputField,
    string InputValue,
    string Category) : IRequest<Result<CategoryMappingDto>>;
