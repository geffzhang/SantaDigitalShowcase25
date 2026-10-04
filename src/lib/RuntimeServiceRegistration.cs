using Azure.Identity;
using Drasicrhsit.Infrastructure;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Services;

public static class RuntimeServiceRegistration
{
    public static IServiceCollection AddApplicationRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var configuredMode = configuration[$"{RuntimeModeOptions.SectionName}:Mode"];
        var mode = configuredMode?.Trim().ToLowerInvariant() switch
        {
            null or "" or "azure" => RuntimeMode.Azure,
            "selfhosted" => RuntimeMode.SelfHosted,
            _ => throw new InvalidOperationException(
                $"Unsupported {RuntimeModeOptions.SectionName}:Mode value '{configuredMode}'. Supported values are 'Azure' and 'SelfHosted'.")
        };

        services.AddSingleton(new RuntimeModeOptions { Mode = mode });

        if (mode == RuntimeMode.Azure)
        {
            services.AddAzureRuntime(configuration);
        }
        else
        {
            services.AddSelfHostedRuntime(configuration);
        }

        return services;
    }

    private static IServiceCollection AddAzureRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddRuntimeStreamServices();
        services.AddSingleton<ISecretProvider, KeyVaultSecretProvider>();
        services.AddSingleton<CosmosSetup>();

        services.AddSingleton<CosmosClient>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var endpoint = ConfigurationHelper.GetOptionalValue(
                cfg,
                "Cosmos:Endpoint",
                "COSMOS_ENDPOINT");
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                endpoint = cfg["Cosmos:Endpoint"];
            }

            var key = cfg["Cosmos:Key"];

            bool KeyLooksValid(string? candidate)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    return false;
                }

                try
                {
                    _ = Convert.FromBase64String(candidate);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            CosmosClient client;
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                var cosmosOptions = new CosmosClientOptions
                {
                    SerializerOptions = new CosmosSerializationOptions
                    {
                        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                    }
                };

                client = KeyLooksValid(key)
                    ? new CosmosClient(endpoint, key, cosmosOptions)
                    : new CosmosClient(endpoint, new DefaultAzureCredential(), cosmosOptions);
            }
            else
            {
                var setup = sp.GetRequiredService<CosmosSetup>();
                var created = setup.TryCreateClientAsync().GetAwaiter().GetResult();
                if (created is null)
                {
                    throw new InvalidOperationException(
                        "Cosmos configuration missing: no endpoint in config and secrets could not produce a client.");
                }

                client = created;
            }

            return client;
        });

        services.AddSingleton<ICosmosRepository>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var client = sp.GetRequiredService<CosmosClient>();
            var dbName = cfg["Cosmos:DatabaseName"] ?? "elves_demo";
            return new CosmosRepository(client, dbName);
        });

        services.AddScoped<IWishlistRepository, WishlistRepository>();
        services.AddScoped<IRecommendationRepository, RecommendationRepository>();
        services.AddScoped<IProfileSnapshotRepository, ProfileSnapshotRepository>();
        services.AddScoped<ILogisticsAssessmentRepository, LogisticsAssessmentRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();

        services.AddChatClientForRuntime(configuration, RuntimeMode.Azure);
        services.AddSingleton<IEventPublisher, EventHubPublisher>();
        services.AddHostedService<CosmosWishlistChangeFeedPublisher>();
        services.AddHostedService<CosmosRecommendationChangeFeedPublisher>();
        services.AddHostedService<DrasiHubCacheSeeder>();
        return services;
    }

    private static IServiceCollection AddSelfHostedRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("elves");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:elves must be configured when Runtime:Mode is SelfHosted.");
        }

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IWishlistRepository, PostgresWishlistRepository>();
        services.AddScoped<IProfileSnapshotRepository, PostgresProfileSnapshotRepository>();
        services.AddScoped<IRecommendationRepository, PostgresRecommendationRepository>();
        services.AddScoped<INotificationRepository, PostgresNotificationRepository>();
        services.AddChatClientForRuntime(configuration, RuntimeMode.SelfHosted);
        services.AddRuntimeStreamServices();
        return services;
    }

    private static IServiceCollection AddRuntimeStreamServices(this IServiceCollection services)
    {
        services.AddSingleton<IStreamResumeStore, InMemoryStreamResumeStore>();
        services.AddSingleton<IStreamMetrics, InMemoryStreamMetrics>();
        return services;
    }
}
