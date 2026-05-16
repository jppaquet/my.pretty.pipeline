using Notify.Functions.Push;
using Notify.Shared;
using Notify.Shared.CloudEvents;

namespace Notify.Functions.Push.Tests;

public class PushHandlerTests
{
    private static CloudEventEnvelope EnvelopeFor(string source, Priority priority = Priority.Normal, IReadOnlyList<string>? tags = null)
    {
        var data = new NotifyCreatedV1
        {
            Source = source,
            Title = "title",
            Body = "body",
            Priority = priority,
            Tags = tags,
            Id = "id-1",
        };
        return CloudEventEnvelope.From(data, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Returns_tracking_id_from_outcome()
    {
        var sender = new RecordingSender { TrackingIdToReturn = "track-xyz" };
        var handler = new PushHandler(sender);

        var trackingId = await handler.HandleAsync(EnvelopeFor("home-pipeline"));

        Assert.Equal("track-xyz", trackingId);
    }

    [Fact]
    public async Task Sends_with_correct_tag_expression()
    {
        var sender = new RecordingSender();
        var handler = new PushHandler(sender);

        await handler.HandleAsync(EnvelopeFor("home-pipeline", tags: new[] { "urgent" }));

        var sent = Assert.Single(sender.Sent);
        Assert.Equal("source:home-pipeline || global || tag:home-pipeline:urgent", sent.TagExpression);
    }

    [Fact]
    public async Task Maps_priority_high_to_apns_10_in_payload()
    {
        var sender = new RecordingSender();
        var handler = new PushHandler(sender);

        await handler.HandleAsync(EnvelopeFor("home-pipeline", Priority.High));

        Assert.Equal(10, sender.Sent.Single().Payload.ApnsPriority);
    }

    [Fact]
    public async Task Bubbles_up_sender_failure_for_function_runtime_to_handle_retry()
    {
        // Functions runtime auto-retries EG-trigger failures and dead-letters
        // after maxDeliveryAttempts (set to 30 in eventgrid.bicep). The handler
        // should not swallow exceptions.
        var sender = new ThrowingSender(new InvalidOperationException("NH transient"));
        var handler = new PushHandler(sender);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(EnvelopeFor("home-pipeline")));
    }
}
