using FluentAssertions;
using Wasnie.Domain.BackgroundJobs;

namespace Wasnie.UnitTests.Domain;

public sealed class BackgroundJobRecordTests
{
    private static BackgroundJobRecord PendingRecord() =>
        BackgroundJobRecord.CreatePending(Guid.NewGuid(), Guid.NewGuid(), "SomeHandler");

    [Fact]
    public void RequestCancellation_FromPending_SetsCancelling()
    {
        var record = PendingRecord();

        record.RequestCancellation();

        record.State.Should().Be(JobState.Cancelling);
    }

    [Fact]
    public void RequestCancellation_FromRunning_SetsCancelling()
    {
        var record = PendingRecord();
        record.MarkRunning();

        record.RequestCancellation();

        record.State.Should().Be(JobState.Cancelling);
    }

    [Fact]
    public void RequestCancellation_FromSucceeded_NoOp()
    {
        var record = PendingRecord();
        record.MarkRunning();
        record.MarkCompleted();

        record.RequestCancellation();

        record.State.Should().Be(JobState.Succeeded);
    }

    [Fact]
    public void RequestCancellation_FromFailed_NoOp()
    {
        var record = PendingRecord();
        record.MarkRunning();
        record.MarkFailed("boom");

        record.RequestCancellation();

        record.State.Should().Be(JobState.Failed);
    }

    [Fact]
    public void MarkCancelled_SetsCancelledStateAndCompletedAt()
    {
        var record = PendingRecord();
        record.MarkRunning();
        record.RequestCancellation();

        record.MarkCancelled();

        record.State.Should().Be(JobState.Cancelled);
        record.CompletedAtUtc.Should().NotBeNull();
    }
}
