using System.Net;
using System.Net.Http.Json;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Notify.Shared.Cosmos;
using Notify.Shared.Json;

namespace Notify.E2E;

// Black-box pipeline test against a deployed dev/staging slot. Driven by the
// `e2e.yml` workflow after `cd-deploy` publishes functions but before the
// staging→production swap.
//
// Required env (set by e2e.yml or out-of-band):
//   E2E_TARGET_HOSTNAME           hostname of the Function App slot under test
//   E2E_API_KEY                   x-api-key for an existing CI-only project
//   E2E_PROJECT_ID                source/project id matching that key
//   E2E_COSMOS_ACCOUNT_ENDPOINT   https://<acct>.documents.azure.com:443/
//
// Without these the SkippableFact skips with a clear reason — preferable to a
// silent pass. The CI-only API key minting will land in a follow-up PR.
[Trait("Category", "E2E")]
public class PipelineTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [SkippableFact]
    public async Task Posted_message_lands_in_cosmos_within_5s()
    {
        var hostname = Environment.GetEnvironmentVariable("E2E_TARGET_HOSTNAME");
        var apiKey   = Environment.GetEnvironmentVariable("E2E_API_KEY");
        var project  = Environment.GetEnvironmentVariable("E2E_PROJECT_ID");
        var cosmos   = Environment.GetEnvironmentVariable("E2E_COSMOS_ACCOUNT_ENDPOINT");

        Skip.If(string.IsNullOrWhiteSpace(hostname), "E2E_TARGET_HOSTNAME not set");
        Skip.If(string.IsNullOrWhiteSpace(apiKey),   "E2E_API_KEY not set");
        Skip.If(string.IsNullOrWhiteSpace(project),  "E2E_PROJECT_ID not set");
        Skip.If(string.IsNullOrWhiteSpace(cosmos),   "E2E_COSMOS_ACCOUNT_ENDPOINT not set");

        var baseUri = hostname!.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? new Uri(hostname)
            : new Uri($"https://{hostname}");

        var dedup = $"e2e-{Guid.NewGuid():N}";
        var body = new
        {
            source = project,
            title = "e2e",
            body = "pipeline check",
            deduplicationKey = dedup,
        };

        using var http = new HttpClient { BaseAddress = baseUri };
        http.DefaultRequestHeaders.Add("x-api-key", apiKey);

        var resp = await http.PostAsJsonAsync("/v1/notifications", body, NotifyJson.Options);
        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);

        using var cosmosClient = new CosmosClient(cosmos, new DefaultAzureCredential(), new CosmosClientOptions
        {
            UseSystemTextJsonSerializerWithOptions = NotifyJson.Options,
        });
        var container = cosmosClient.GetContainer("notify", "notifications");

        var expectedId = Notify.Shared.Hashing.DedupKeyHasher.Hash(project!, dedup);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < Deadline)
        {
            try
            {
                var read = await container.ReadItemAsync<NotificationDocument>(
                    expectedId, new PartitionKey(project));
                Assert.Equal(expectedId, read.Resource.Id);
                Assert.Equal(project, read.Resource.Source);
                return;
            }
            catch (CosmosException ce) when (ce.StatusCode == HttpStatusCode.NotFound)
            {
                await Task.Delay(250);
            }
        }

        Assert.Fail($"Document {expectedId} not visible in Cosmos within {Deadline.TotalSeconds}s");
    }
}
