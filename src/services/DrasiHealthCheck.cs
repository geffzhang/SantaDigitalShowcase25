using Microsoft.Extensions.Diagnostics.HealthChecks;
using Drasicrhsit.Infrastructure;

namespace Services;

/// <summary>
/// Health check for the active runtime's Drasi query service.
/// Returns Degraded when Drasi is unreachable so API health remains available independently.
/// </summary>
public class DrasiHealthCheck : IHealthCheck
{
    private readonly IDrasiViewClient _drasiClient;
    private readonly IConfiguration _config;
    private readonly ILogger<DrasiHealthCheck> _logger;

    public DrasiHealthCheck(
        IDrasiViewClient drasiClient,
        IConfiguration config,
        ILogger<DrasiHealthCheck> logger)
    {
        _drasiClient = drasiClient;
        _config = config;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isSelfHosted = string.Equals(
                _config["Runtime:Mode"],
                "SelfHosted",
                StringComparison.OrdinalIgnoreCase);
            var endpointSetting = isSelfHosted
                ? "DRASI_SERVER_BASE_URL"
                : "DRASI_VIEW_SERVICE_BASE_URL";
            var serviceUrl = isSelfHosted
                ? ConfigurationHelper.GetOptionalValue(
                    _config,
                    "Drasi:ServerBaseUrl",
                    endpointSetting)
                : ConfigurationHelper.GetOptionalValue(
                    _config,
                    "Drasi:ViewServiceBaseUrl",
                    endpointSetting);

            var queryContainer = isSelfHosted
                ? _config["Drasi:ServerInstanceId"] ?? "default"
                : ConfigurationHelper.GetValue(
                    _config,
                    "Drasi:QueryContainer",
                    "DRASI_QUERY_CONTAINER",
                    "default");

            if (string.IsNullOrWhiteSpace(serviceUrl))
            {
                _logger.LogWarning("{EndpointSetting} not configured - Drasi health check skipped.", endpointSetting);
                return HealthCheckResult.Degraded(
                    $"Drasi service URL not configured ({endpointSetting})",
                    data: new Dictionary<string, object>
                    {
                        { "configured", false },
                        { "runtime", isSelfHosted ? "SelfHosted" : "Azure" },
                        { "endpointSetting", endpointSetting },
                        { "timestamp", DateTime.UtcNow }
                    });
            }

            // Test with a known query (use smallest/fastest one)
            var testQuery = "wishlist-trending-1h";

            var results = await _drasiClient.GetCurrentResultAsync(
                queryContainer,
                testQuery,
                cancellationToken);

            var data = new Dictionary<string, object>
            {
                { "queryContainer", queryContainer },
                { "testQuery", testQuery },
                { "resultCount", results.Count },
                { "runtime", isSelfHosted ? "SelfHosted" : "Azure" },
                { "timestamp", DateTime.UtcNow }
            };

            return HealthCheckResult.Healthy(
                "Drasi query service is accessible",
                data);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Drasi query service health check failed due to an HTTP connectivity issue.");
            return HealthCheckResult.Degraded(
                "Drasi query service is unreachable",
                ex,
                new Dictionary<string, object>
                {
                    { "error", ex.Message }
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Drasi query service health check failed");
            return HealthCheckResult.Degraded(
                "Drasi query service check failed",
                ex,
                new Dictionary<string, object> { { "error", ex.Message } });
        }
    }
}
