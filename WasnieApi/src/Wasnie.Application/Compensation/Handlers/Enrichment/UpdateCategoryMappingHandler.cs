using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Enrichment;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Enrichment;

public sealed class UpdateCategoryMappingHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<UpdateCategoryMappingCommand, Result<CategoryMappingDto>>
{
    public async Task<Result<CategoryMappingDto>> Handle(
        UpdateCategoryMappingCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.CategoryMappingsManage, cancellationToken);

        var mapping = await db.CategoryMappings
            .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken);
        if (mapping is null)
            return Result<CategoryMappingDto>.Failure("Category mapping not found.");

        var field = request.InputField.Trim();
        var value = request.InputValue.Trim();

        // Same HARD-collision rule as create, excluding this row: editing a mapping onto an existing
        // (field, value) must be rejected, not silently create a duplicate the DB would then block.
        var clashes = await db.CategoryMappings.AnyAsync(
            m => m.Id != request.Id && m.InputField == field && m.InputValue == value, cancellationToken);
        if (clashes)
            return Result<CategoryMappingDto>.Failure(
                $"A mapping for {field} '{value}' already exists. Edit that mapping instead.");

        try
        {
            mapping.Update(field, value, request.Category);
        }
        catch (DomainException ex)
        {
            return Result<CategoryMappingDto>.Failure(ex.Message);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result<CategoryMappingDto>.Failure(
                $"A mapping for {field} '{value}' already exists. Edit that mapping instead.");
        }

        return Result<CategoryMappingDto>.Success(CreateCategoryMappingHandler.ToDto(mapping));
    }
}
