using System.Text.Json;
using Dapr.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Drasicrhsit.Infrastructure;

namespace Services;

// Minimal subscriber that bridges Drasi Reaction pubsub messages to SSE broadcaster
public static class DrasiDaprSubscriber
{
    public static IEndpointRouteBuilder MapDrasiDaprSubscriptions(this IEndpointRouteBuilder app)
    {
        // Dapr subscribes by calling this endpoint at startup to discover topics
        app.MapGet("dapr/subscribe", (IConfiguration config) =>
        {
            // Configure expected topics (packed CloudEvents) from Drasi Reaction
            // Topics follow pattern: <queryId>-results
            var pubsub = ConfigurationHelper.GetValue(
                config,
                "Drasi:DaprPubSubName",
                "DRASI_DAPR_PUBSUB_NAME",
                "rg-pubsub");
            // Allow override via configuration array: Drasi:DaprTopics: [ "queryId1", "queryId2" ]
            var configured = config.GetSection("Drasi:DaprTopics").Get<string[]>() ?? Array.Empty<string>();
            var queryIds = configured.Length > 0
                ? configured
                : new[] { "wishlist-trending-1h", "wishlist-duplicates-by-child", "wishlist-inactive-children-3d" };

            var topics = queryIds.Select(q => new { pubsubName = pubsub, topic = $"{q}-results", route = $"/dapr/drasi/{q}" }).ToArray();

            return Results.Json(topics);
        })
        .WithName("DaprSubscribe");

        app.MapPost("dapr/drasi/{queryId}", HandleReactionAsync)
            .WithName("DrasiDaprTopicHandler");

        app.MapPost("drasi/reactions/{queryId}", HandleReactionAsync)
            .WithName("DrasiServerReactionHandler");

        return app;
    }

    private static async Task<IResult> HandleReactionAsync(
        string queryId,
        HttpRequest req,
        IStreamBroadcaster broadcaster,
        INotificationRepository notifications,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("DrasiReactionHandler");
        try
        {
            using var doc = await JsonDocument.ParseAsync(req.Body, cancellationToken: ct);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Object)
            {
                logger.LogWarning(
                    "Reaction for query {QueryId} was rejected with status {StatusCode}: missing or invalid data",
                    queryId,
                    StatusCodes.Status400BadRequest);
                return Results.BadRequest();
            }

            string childId;
            string message;
            if (queryId == "wishlist-updates")
            {
                if (!TryGetNonEmptyString(data, "childId", out childId) ||
                    !TryGetNonEmptyString(data, "text", out var text))
                {
                    logger.LogWarning(
                        "Reaction for query {QueryId} was rejected with status {StatusCode}: invalid wishlist data",
                        queryId,
                        StatusCodes.Status400BadRequest);
                    return Results.BadRequest();
                }

                message = $"Wishlist update: {text}";
            }
            else
            {
                childId = queryId is "wishlist-duplicates-by-child" or "wishlist-inactive-children-3d"
                    ? GetRequiredString(data, "childId")
                    : TryGetNonEmptyString(data, "childId", out var projectedChildId)
                        ? projectedChildId
                        : "unknown";
                message = BuildMessage(queryId, data);
            }

            var entity = new NotificationEntity
            {
                ChildId = childId,
                Type = MapType(queryId),
                Message = message,
                RelatedId = null,
                State = "unread"
            };

            await notifications.StoreAsync(entity);

            await broadcaster.PublishAsync(childId, "notification", new
            {
                entity.id,
                childId = entity.ChildId,
                type = entity.Type,
                message = entity.Message,
                relatedId = entity.RelatedId,
                state = entity.State,
                timestamp = entity.CreatedAt.ToString("o")
            }, ct);

            return Results.Accepted();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "Reaction for query {QueryId} was rejected with status {StatusCode}: invalid JSON data",
                queryId,
                StatusCodes.Status400BadRequest);
            return Results.BadRequest();
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to process reaction for query {QueryId}; returning status {StatusCode}",
                queryId,
                StatusCodes.Status500InternalServerError);
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private static string MapType(string queryId) => queryId switch
    {
        "wishlist-updates" => "wishlist",
        "wishlist-trending-1h" => "wishlist",
        "wishlist-duplicates-by-child" => "recommendation",
        "wishlist-inactive-children-3d" => "behavior",
        _ => "info"
    };

    private static bool TryGetNonEmptyString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var candidate = property.GetString();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private static string GetRequiredString(JsonElement element, string name) =>
        TryGetNonEmptyString(element, name, out var value)
            ? value
            : throw new JsonException($"CloudEvent data requires a non-empty string '{name}'.");

    private static int GetRequiredInt32(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out var value))
        {
            throw new JsonException($"CloudEvent data requires an integer '{name}'.");
        }

        return value;
    }

    private static string BuildMessage(string queryId, JsonElement data) => queryId switch
    {
        "wishlist-trending-1h" =>
            $"Trending update: {GetRequiredString(data, "item")} ({GetRequiredInt32(data, "frequency")})",
        "wishlist-duplicates-by-child" =>
            $"Duplicate wishlist item detected: {GetRequiredString(data, "item")}",
        "wishlist-inactive-children-3d" =>
            $"Inactive child detected: {GetRequiredString(data, "childId")}",
        _ => $"Drasi update for {queryId}"
    };
}
