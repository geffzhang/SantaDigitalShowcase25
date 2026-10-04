using System.Text.Json;
using System.Text.Json.Nodes;
using Drasicrhsit.Infrastructure;

namespace Services;

public sealed class DrasiServerViewClient(
    HttpClient httpClient,
    ILogger<DrasiServerViewClient> logger,
    IConfiguration configuration) : IDrasiViewClient
{
    public async Task<List<JsonNode>> GetCurrentResultAsync(
        string queryContainerId,
        string queryId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryContainerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);

        var baseUrl = ConfigurationHelper.GetRequiredValue(
            configuration,
            "Drasi:ServerBaseUrl",
            "DRASI_SERVER_BASE_URL");
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Drasi:ServerBaseUrl must be an absolute HTTP or HTTPS URL.");
        }

        var requestUri =
            $"{baseUrl.TrimEnd('/')}/api/v1/instances/{Uri.EscapeDataString(queryContainerId)}" +
            $"/queries/{Uri.EscapeDataString(queryId)}/results";

        logger.LogDebug("Querying Drasi Server instance {InstanceId}, query {QueryId}", queryContainerId, queryId);

        using var response = await httpClient.GetAsync(
            requestUri,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("success", out var success) ||
            success.ValueKind != JsonValueKind.True)
        {
            var error = root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("error", out var errorNode) &&
                errorNode.ValueKind != JsonValueKind.Null
                    ? errorNode.ToString()
                    : "missing or false success flag";
            throw new InvalidOperationException(
                $"Drasi Server query '{queryId}' failed: {error}");
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"Drasi Server query '{queryId}' returned no result array.");
        }

        var results = new List<JsonNode>(data.GetArrayLength());
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Drasi Server query '{queryId}' returned a result that is not a JSON object.");
            }

            results.Add(JsonNode.Parse(item.GetRawText())!);
        }

        logger.LogDebug(
            "Retrieved {ResultCount} results from Drasi Server query {QueryId}",
            results.Count,
            queryId);
        return results;
    }
}
