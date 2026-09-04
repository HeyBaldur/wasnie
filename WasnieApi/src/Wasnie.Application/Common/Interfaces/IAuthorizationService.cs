namespace Wasnie.Application.Common.Interfaces;

public interface IAuthorizationService
{
    Task RequireAsync(string permission, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the caller holds this permission. Asks; does not enforce.
    ///
    /// ★★ IT EXISTS FOR ENDPOINTS THAT ASSEMBLE SEVERAL ANSWERS, where one missing permission must
    /// remove a PART of the reply rather than refuse the whole of it — the sidebar badges being the
    /// case that needed it. Expressing that with RequireAsync means catching ForbiddenException as
    /// control flow, and a caught 403 also swallows the PERMISSION_DENIED audit entry that
    /// RequireAsync writes: the log would fill with denials for permissions nobody was really
    /// refused.
    ///
    /// ★ IT DOES NOT AUDIT, AND THAT IS THE POINT. A denial is an event; a question is not.
    /// Enforcement — the thing worth recording — stays RequireAsync's job.
    /// </summary>
    Task<bool> HasAsync(string permission, CancellationToken cancellationToken = default);
}
