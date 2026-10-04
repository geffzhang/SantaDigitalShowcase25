using Drasicrhsit.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Services;
using Xunit;

namespace UnitTests;

public class RuntimeServiceRegistrationTests
{
    [Fact]
    public void AddApplicationRuntime_AzureRegistersAzureServices()
    {
        var services = CreateServices("Azure");

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(CosmosClient));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(CosmosSetup));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ISecretProvider));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IEventPublisher));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(AIAgent));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IChatClient));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(CosmosWishlistChangeFeedPublisher));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(CosmosRecommendationChangeFeedPublisher));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(DrasiHubCacheSeeder));
    }

    [Fact]
    public void AddApplicationRuntime_AzureUsesDrasiPlatformClient()
    {
        using var provider = CreateServices("Azure").BuildServiceProvider();

        Assert.IsType<DrasiViewClient>(provider.GetRequiredService<IDrasiViewClient>());
    }

    [Fact]
    public void AddApplicationRuntime_SelfHostedExcludesAzureServices()
    {
        var services = CreateServices("SelfHosted");

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWishlistRepository) &&
            descriptor.ImplementationType == typeof(PostgresWishlistRepository));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IProfileSnapshotRepository) &&
            descriptor.ImplementationType == typeof(PostgresProfileSnapshotRepository));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRecommendationRepository) &&
            descriptor.ImplementationType == typeof(PostgresRecommendationRepository));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(INotificationRepository) &&
            descriptor.ImplementationType == typeof(PostgresNotificationRepository));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ILogisticsAssessmentRepository) &&
            descriptor.ImplementationType?.Name == "PostgresLogisticsAssessmentRepository");
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IChatClient));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(CosmosClient));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(CosmosSetup));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ISecretProvider));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IEventPublisher));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(CosmosWishlistChangeFeedPublisher));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(CosmosRecommendationChangeFeedPublisher));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(DrasiHubCacheSeeder));
    }

    [Fact]
    public void AddApplicationRuntime_SelfHostedUsesDrasiServerClient()
    {
        using var provider = CreateServices("SelfHosted").BuildServiceProvider();

        Assert.IsType<DrasiServerViewClient>(provider.GetRequiredService<IDrasiViewClient>());
    }

    private static IServiceCollection CreateServices(string mode)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Runtime:Mode"] = mode,
                ["ConnectionStrings:elves"] = "Host=localhost;Database=elves;Username=postgres;Password=postgres"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        return services.AddApplicationRuntime(configuration);
    }
}
