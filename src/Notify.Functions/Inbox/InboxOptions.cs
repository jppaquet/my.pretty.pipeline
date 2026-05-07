namespace Notify.Functions.Inbox;

// Strongly-typed bindings for the app settings the Inbox function reads at
// startup. Configured by infra/modules/functions.bicep; local.settings.json
// mirrors it for local dev. Reuses the same Cosmos values as Archive — both
// read/write the `notifications` container.
public sealed record InboxOptions
{
    public required string CosmosAccountEndpoint { get; init; }
    public string CosmosDatabase { get; init; } = "notify";
    public string CosmosNotificationsContainer { get; init; } = "notifications";

    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;
    public const int MaxSourceLength = 64;
}
