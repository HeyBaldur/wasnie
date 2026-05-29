using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.BackgroundJobs.Queries;

public sealed record GetJobStatusQuery(Guid JobId) : IRequest<Result<JobStatusDto>>;

public sealed class GetJobStatusQueryHandler(
    IBackgroundJobService jobService,
    ITenantContext tenantContext)
    : IRequestHandler<GetJobStatusQuery, Result<JobStatusDto>>
{
    public async Task<Result<JobStatusDto>> Handle(
        GetJobStatusQuery request,
        CancellationToken cancellationToken)
    {
        var status = await jobService.GetJobStatusAsync(
            request.JobId,
            tenantContext.TenantId,
            cancellationToken);

        return status is not null
            ? Result<JobStatusDto>.Success(status)
            : Result<JobStatusDto>.Failure("Job not found.");
    }
}
