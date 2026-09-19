using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Commands;

/// <summary>
/// "I forgot my Organization identifier" — tenant discovery by email (KAN-93).
///
/// ★★ WITHOUT IT AN ACCOUNT COULD BECOME PERMANENTLY UNREACHABLE. The identifier is the workspace's
/// slug, typed into the sign-in form, and it is the one credential the product never sent anybody a
/// way to recover: a forgotten password has a reset link, a forgotten identifier had nothing. An
/// administrator who could not remember theirs was locked out of their own tenant for good, with the
/// data intact on the other side of a field they could not fill in.
///
/// ★★ IT IS SENT BY EMAIL AND NEVER RETURNED ON SCREEN. Answering in the response would turn this
/// into an oracle: anybody could feed it addresses and learn which companies use the product and who
/// works for them. The mailbox is the proof of identity, exactly as it is for a password reset.
///
/// ★★ ADMINISTRATORS ONLY (Rodolfo's decision). A non-admin address triggers no email at all; the
/// screen tells everybody the same thing and points the rest at their administrator. It narrows who
/// can pull a tenant's identifier out of the system to the people who are supposed to know it
/// already, and the response does not change, so the restriction discloses nothing either.
/// </summary>
public sealed record RequestOrganizationIdentifierCommand(string Email) : IRequest<Result<bool>>;
