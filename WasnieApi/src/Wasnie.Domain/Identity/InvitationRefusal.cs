namespace Wasnie.Domain.Identity;

/// <summary>
/// WHY AN INVITATION WAS REFUSED, AS A CODE THE FRONT END TRANSLATES (KAN-32).
///
/// ★★ CODES, NOT SENTENCES (§C1). Two of these are read by somebody who has no account yet and
/// therefore no language preference on record — the accept page picks the browser's. A backend that
/// answered in English prose would be answering in the one language the reader may not have.
///
/// ★ THE TWO TOKEN REFUSALS ARE DELIBERATELY DIFFERENT. "Already used" and "expired" lead the reader
/// to different actions: the first means they are probably already able to sign in, the second means
/// they should ask for a new link. Folding them into one message would make both of them useless.
///
/// ★ BUT "NO SUCH TOKEN" IS NOT ONE OF THEM. A token that matches nothing gets
/// <see cref="TokenInvalid"/>, the same answer as a malformed one, because distinguishing them would
/// let somebody test tokens for existence.
/// </summary>
public static class InvitationRefusal
{
    /// <summary>The token matched nothing, or was not a token at all.</summary>
    public const string TokenInvalid = "INVITATION_TOKEN_INVALID";

    /// <summary>The invitation was already accepted — the link is single use.</summary>
    public const string TokenAlreadyUsed = "INVITATION_TOKEN_ALREADY_USED";

    /// <summary>The deadline passed. The admin can send a new one.</summary>
    public const string TokenExpired = "INVITATION_TOKEN_EXPIRED";

    /// <summary>An admin withdrew it before it was used.</summary>
    public const string TokenRevoked = "INVITATION_TOKEN_REVOKED";

    /// <summary>That address already belongs to somebody in this tenant.</summary>
    public const string EmailAlreadyMember = "INVITATION_EMAIL_ALREADY_MEMBER";

    /// <summary>That address already has an invitation outstanding — resend it instead.</summary>
    public const string EmailAlreadyInvited = "INVITATION_EMAIL_ALREADY_INVITED";

    /// <summary>The tenant has no seat free. Carries <c>used</c> and <c>limit</c>.</summary>
    public const string NoSeatsAvailable = "INVITATION_NO_SEATS_AVAILABLE";

    /// <summary>The role asked for is not one this product has.</summary>
    public const string RoleUnknown = "INVITATION_ROLE_UNKNOWN";

    /// <summary>
    /// The last remaining administrator cannot be deactivated or demoted.
    ///
    /// ★ THE ONE RULE THAT PROTECTS THE TENANT FROM ITSELF. Without it a single misclick locks
    /// everybody out of user administration for good, and no amount of support can undo it from
    /// inside the product.
    /// </summary>
    public const string LastAdmin = "USER_LAST_ADMIN";

    /// <summary>Somebody tried to act on their own access — deactivating or demoting themselves.</summary>
    public const string CannotActOnSelf = "USER_CANNOT_ACT_ON_SELF";
}
