using Wasnie.Application.Features.Users.DTOs;
using Wasnie.Domain.Identity;

namespace Wasnie.Application.Features.Users.Handlers;

/// <summary>
/// The one place an <see cref="Invitation"/> becomes an <see cref="InvitationDto"/>.
///
/// ★ ONE MAPPER, BECAUSE THE STATUS IS COMPUTED. Three handlers return an invitation, and each of
/// them working out the status for itself is three chances to forget the clock and ship a row that
/// says Pending a week after it expired.
/// </summary>
internal static class InvitationMapper
{
    public static InvitationDto ToDto(Invitation invitation, DateTimeOffset now, string? invitedByEmail) =>
        new(
            invitation.Id,
            invitation.Email,
            invitation.Role,
            invitation.StatusAt(now),
            invitation.CreatedAt,
            invitation.ExpiresAt,
            invitation.AcceptedAt,
            invitation.ResendCount,
            invitedByEmail);
}
