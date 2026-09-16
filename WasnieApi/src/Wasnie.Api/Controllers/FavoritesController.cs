using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Features.Favorites;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Settings;

namespace Wasnie.Api.Controllers;

/// <summary>
/// The signed-in user's favorites (KAN-64). ONE controller for every entity type — the type is part of the route, not
/// of the class name. Always scoped to the caller: there is no route that names another user.
///
/// ★ 404 FOR "NOT YOURS" AND "DOES NOT EXIST" ALIKE, like the assistant pins: a 403 would confirm that an entity the
/// caller may not see exists at that id. A missing PERMISSION is still a 403, from the section's own check.
/// </summary>
[ApiController]
[Route("api/favorites")]
[Authorize]
public sealed class FavoritesController(IMediator mediator) : ControllerBase
{
    /// <summary>The caller's favorites of one type, resolved for display (name, code/version, status).</summary>
    [HttpGet("{entityType}")]
    public async Task<IActionResult> List(string entityType, CancellationToken cancellationToken)
    {
        if (!TryParseType(entityType, out var type))
            return NotFound(new { messageKey = FavoriteErrors.NotFound });

        var result = await mediator.Send(new ListFavoritesQuery(type), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result.Error);
    }

    /// <summary>Stars an entity. Idempotent: starring what is already starred is 204.</summary>
    [HttpPut("{entityType}/{entityId:guid}")]
    public async Task<IActionResult> Add(string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        if (!TryParseType(entityType, out var type))
            return NotFound(new { messageKey = FavoriteErrors.NotFound });

        var result = await mediator.Send(new AddFavoriteCommand(type, entityId), cancellationToken);
        return result.IsSuccess ? NoContent() : ToError(result.Error);
    }

    /// <summary>Un-stars an entity. Idempotent: removing what is not starred is 204.</summary>
    [HttpDelete("{entityType}/{entityId:guid}")]
    public async Task<IActionResult> Remove(string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        if (!TryParseType(entityType, out var type))
            return NotFound(new { messageKey = FavoriteErrors.NotFound });

        var result = await mediator.Send(new RemoveFavoriteCommand(type, entityId), cancellationToken);
        return result.IsSuccess ? NoContent() : ToError(result.Error);
    }

    /// <summary>
    /// "payee" / "plan", case-insensitive, by NAME only. <c>Enum.TryParse</c> alone would also accept "0" or "7" — a
    /// numeric string must not become a type, so digits are refused before parsing.
    /// </summary>
    private static bool TryParseType(string value, out FavoriteEntityType type)
    {
        type = default;
        return !string.IsNullOrWhiteSpace(value)
            && value.All(char.IsLetter)
            && Enum.TryParse(value, ignoreCase: true, out type)
            && Enum.IsDefined(type);
    }

    private IActionResult ToError(string? error) => error switch
    {
        FavoriteErrors.NotFound => NotFound(new { messageKey = error }),
        // The limit travels as a key WITH its parameter, so the client never hard-codes the number (§C1).
        FavoriteErrors.LimitReached => UnprocessableEntity(new { messageKey = error, parameters = new { max = Favorite.MaxPerType } }),
        _ => BadRequest(new { messageKey = error }),
    };
}
