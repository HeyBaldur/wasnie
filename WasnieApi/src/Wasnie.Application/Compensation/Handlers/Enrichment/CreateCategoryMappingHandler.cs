using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Enrichment;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enrichment;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Enrichment;

public sealed class CreateCategoryMappingHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    IGuidGenerator guid,
    IAuthorizationService authorizationService)
    : IRequestHandler<CreateCategoryMappingCommand, Result<CategoryMappingDto>>
{
    public async Task<Result<CategoryMappingDto>> Handle(
        CreateCategoryMappingCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.CategoryMappingsManage, cancellationToken);

        var field = request.InputField.Trim();
        var value = request.InputValue.Trim();

        // Collision is a HARD error, not silent precedence: two mappings for the same (field, value)
        // would make enrichment ambiguous — exactly the silence this layer exists to remove. Checked
        // case-insensitively (matching is CI); the unique DB index is the last-line backstop below.
        var exists = await db.CategoryMappings.AnyAsync(
            m => m.InputField == field && m.InputValue == value, cancellationToken);
        if (exists)
            return Result<CategoryMappingDto>.Failure(
                $"A mapping for {field} '{value}' already exists. Edit that mapping instead.");

        CategoryMapping mapping;
        try
        {
            mapping = CategoryMapping.Create(
                guid.NewGuid(), tenantContext.TenantId, field, value, request.Category);
        }
        catch (DomainException ex)
        {
            return Result<CategoryMappingDto>.Failure(ex.Message);
        }

        db.CategoryMappings.Add(mapping);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Backstop for a race that slipped past the check above — the unique index caught it.
            return Result<CategoryMappingDto>.Failure(
                $"A mapping for {field} '{value}' already exists. Edit that mapping instead.");
        }

        return Result<CategoryMappingDto>.Success(ToDto(mapping));
    }

    internal static CategoryMappingDto ToDto(CategoryMapping m) =>
        new(m.Id, m.InputField, m.InputValue, m.Category);
}
