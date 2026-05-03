namespace Notify.Archive;

public sealed record ArchiveOptions
{
    public required string CosmosAccountEndpoint { get; init; }
    public string CosmosDatabase { get; init; } = "notify";
    public string CosmosNotificationsContainer { get; init; } = "notifications";
}
