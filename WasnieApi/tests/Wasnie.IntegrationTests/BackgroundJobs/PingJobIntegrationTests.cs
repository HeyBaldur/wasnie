using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Infrastructure.BackgroundJobs;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.BackgroundJobs;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class PingJobIntegrationTests(TestDatabaseFixture fixture)
{
    private static readonly Guid TenantA = TestConstants.TenantA;
    private static readonly Guid TenantB = TestConstants.TenantB;

    [Fact]
    public async Task PingJob_EnqueueAndExecute_ReachesSucceededState()
    {
        // Arrange: enqueue the PingJob from a service scope (no HTTP context — no query filter
        // is triggered during EnqueueAsync since it only does INSERT/UPDATE, not SELECT).
        Guid jobId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var jobService = scope.ServiceProvider.GetRequiredService<IBackgroundJobService>();
            jobId = await jobService.EnqueueAsync(
                new PingPayload("integration-test"),
                TenantA,
                "test-user-id",
                "test@wasnie.test");
        }

        // Act: poll GET /api/jobs/{id} via authenticated HTTP until Succeeded or timeout.
        var client = fixture.Factory.CreateClient()
            .WithAuth(TenantA);

        JobStatusResponse? status = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/api/jobs/{jobId}");
            if (response.IsSuccessStatusCode)
            {
                status = await response.Content.ReadFromJsonAsync<JobStatusResponse>();
                if (status?.State is "Succeeded" or "Failed")
                    break;
            }
            await Task.Delay(200);
        }

        // Assert: job completed successfully.
        status.Should().NotBeNull("job status should be retrievable within 30 seconds");
        status!.State.Should().Be("Succeeded");
        status.ProgressCurrent.Should().Be(100);
        status.ProgressTotal.Should().Be(100);
        status.ErrorMessage.Should().BeNull();

        // Assert: cross-tenant isolation — TenantB cannot see TenantA's job.
        var clientB = fixture.Factory.CreateClient()
            .WithAuth(TenantB);
        var isolationResponse = await clientB.GetAsync($"/api/jobs/{jobId}");
        isolationResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed record JobStatusResponse(
        Guid Id,
        string State,
        int ProgressCurrent,
        int ProgressTotal,
        string? ErrorMessage
    );
}
