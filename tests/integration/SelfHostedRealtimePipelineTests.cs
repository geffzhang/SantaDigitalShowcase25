using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Drasicrhsit.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Persistence;
using Realtime;
using Services;
using Xunit;

namespace IntegrationTests;

public sealed class SelfHostedRealtimePipelineTests(PostgresDatabaseFixture fixture)
    : IClassFixture<PostgresDatabaseFixture>
{
    [Theory]
    [InlineData("/api/v1/dapr/drasi/wishlist-updates")]
    [InlineData("/api/v1/drasi/reactions/wishlist-updates")]
    public async Task WishlistReaction_PersistsBeforeAckAndBroadcastsToSseAndSignalR(string route)
    {
        var childId = Guid.NewGuid().ToString();
        await using (var db = CreateDbContext())
        {
            await db.Database.MigrateAsync();
        }

        var persistenceProbe = new NotificationPersistenceProbe();
        var broadcastProbe = new StreamBroadcastProbe();
        var signalRProbe = new HubSignalRProbe();
        await using var app = CreateApp(
            services =>
            {
                services.AddDbContext<AppDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
                services.AddScoped<INotificationRepository>(serviceProvider =>
                    new ProbedNotificationRepository(
                        new PostgresNotificationRepository(serviceProvider.GetRequiredService<AppDbContext>()),
                        persistenceProbe));
            },
            persistenceProbe,
            broadcastProbe,
            signalRProbe);
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var streamTask = client.GetAsync(
            $"/api/v1/notifications/stream/{childId}",
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        await broadcastProbe.Subscribed.Task.WaitAsync(timeout.Token);

        using var reactionRequest = new StringContent(
            JsonSerializer.Serialize(new { data = new { childId, text = "Wind-up train" } }),
            Encoding.UTF8,
            "application/json");
        using var reactionResponse = await client.PostAsync(route, reactionRequest, timeout.Token);

        Assert.Equal(HttpStatusCode.Accepted, reactionResponse.StatusCode);
        Assert.True(persistenceProbe.StoredSuccessfully);
        Assert.True(broadcastProbe.PersistenceConfirmedAtPublish);
        Assert.Equal(1, broadcastProbe.PublishCount);

        using var streamResponse = await streamTask.WaitAsync(timeout.Token);
        Assert.Equal("text/event-stream", streamResponse.Content.Headers.ContentType?.MediaType);
        await using var stream = await streamResponse.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);
        var eventLines = new List<string>();
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.Length == 0 && eventLines.Count > 0)
            {
                break;
            }

            eventLines.Add(line);
        }

        Assert.Contains("event: notification", eventLines);
        var dataLine = Assert.Single(eventLines, line => line.StartsWith("data: ", StringComparison.Ordinal));
        using var eventData = JsonDocument.Parse(dataLine["data: ".Length..]);
        Assert.Equal(childId, eventData.RootElement.GetProperty("childId").GetString());
        Assert.Equal("wishlist", eventData.RootElement.GetProperty("type").GetString());
        Assert.Contains("Wind-up train", eventData.RootElement.GetProperty("message").GetString());

        await using var verificationDb = CreateDbContext();
        var storedNotification = await verificationDb.Notifications.SingleAsync(
            notification => notification.ChildId == childId);
        Assert.Equal("wishlist", storedNotification.Type);
        Assert.Contains("Wind-up train", storedNotification.Message);
        signalRProbe.AssertNotificationBroadcasted(childId);

        timeout.Cancel();
        await app.StopAsync();
    }

    [Fact]
    public async Task WishlistReaction_PersistenceFailureReturnsServerErrorWithoutBroadcast()
    {
        var persistenceProbe = new NotificationPersistenceProbe();
        var broadcastProbe = new StreamBroadcastProbe();
        var signalRProbe = new HubSignalRProbe();
        var notificationRepository = new FailingNotificationRepository();
        await using var app = CreateApp(
            services => services.AddSingleton<INotificationRepository>(notificationRepository),
            persistenceProbe,
            broadcastProbe,
            signalRProbe);
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var request = new StringContent(
            """{"data":{"childId":"child-1","text":"Wind-up train"}}""",
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync("/api/v1/dapr/drasi/wishlist-updates", request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, notificationRepository.StoreCallCount);
        Assert.Equal(0, broadcastProbe.PublishCount);
        signalRProbe.AssertNoBroadcast();
    }

    [Fact]
    public async Task DrasiServerReaction_PersistenceFailureReturnsServerErrorWithoutBroadcast()
    {
        var persistenceProbe = new NotificationPersistenceProbe();
        var broadcastProbe = new StreamBroadcastProbe();
        var signalRProbe = new HubSignalRProbe();
        var notificationRepository = new FailingNotificationRepository();
        await using var app = CreateApp(
            services => services.AddSingleton<INotificationRepository>(notificationRepository),
            persistenceProbe,
            broadcastProbe,
            signalRProbe);
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var request = new StringContent(
            """{"data":{"childId":"child-1","text":"Wind-up train"}}""",
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync("/api/v1/drasi/reactions/wishlist-updates", request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, notificationRepository.StoreCallCount);
        Assert.Equal(0, broadcastProbe.PublishCount);
        signalRProbe.AssertNoBroadcast();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"data":null}""")]
    [InlineData("""{"data":{"childId":"child-1"}}""")]
    [InlineData("{")]
    public async Task DrasiServerReaction_InvalidPayloadReturnsBadRequest(string body)
    {
        var persistenceProbe = new NotificationPersistenceProbe();
        var broadcastProbe = new StreamBroadcastProbe();
        var signalRProbe = new HubSignalRProbe();
        var notificationRepository = new FailingNotificationRepository();
        await using var app = CreateApp(
            services => services.AddSingleton<INotificationRepository>(notificationRepository),
            persistenceProbe,
            broadcastProbe,
            signalRProbe);
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var request = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(
            "/api/v1/drasi/reactions/wishlist-updates",
            request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, notificationRepository.StoreCallCount);
        Assert.Equal(0, broadcastProbe.PublishCount);
        signalRProbe.AssertNoBroadcast();
    }

    [Theory]
    [InlineData("wishlist-updates", "{}")]
    [InlineData("wishlist-updates", """{"data":null}""")]
    [InlineData("wishlist-updates", """{"data":{"childId":"child-1"}}""")]
    [InlineData("wishlist-updates", "{")]
    [InlineData("wishlist-trending-1h", """{"data":{"item":7,"frequency":"invalid"}}""")]
    public async Task Reaction_InvalidCloudEventDataReturnsBadRequest(string queryId, string body)
    {
        var persistenceProbe = new NotificationPersistenceProbe();
        var broadcastProbe = new StreamBroadcastProbe();
        var signalRProbe = new HubSignalRProbe();
        var notificationRepository = new FailingNotificationRepository();
        await using var app = CreateApp(
            services => services.AddSingleton<INotificationRepository>(notificationRepository),
            persistenceProbe,
            broadcastProbe,
            signalRProbe);
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var request = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync($"/api/v1/dapr/drasi/{queryId}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, notificationRepository.StoreCallCount);
        Assert.Equal(0, broadcastProbe.PublishCount);
        signalRProbe.AssertNoBroadcast();
    }

    [Fact]
    public async Task NotificationStream_HistoryFailurePropagates()
    {
        var notificationRepository = new Mock<INotificationRepository>();
        notificationRepository
            .Setup(repository => repository.ListAsync("child-1", 50))
            .Throws<InvalidOperationException>();

        var completedChannel = Channel.CreateUnbounded<SseEvent>();
        completedChannel.Writer.TryComplete();
        var broadcaster = new Mock<IStreamBroadcaster>();
        broadcaster
            .Setup(stream => stream.Subscribe("child-1"))
            .Returns(completedChannel.Reader);

        var service = new SseStreamService(
            Mock.Of<IWishlistRepository>(),
            Mock.Of<IRecommendationRepository>(),
            notificationRepository.Object,
            new InMemoryStreamResumeStore(),
            new InMemoryStreamMetrics(),
            broadcaster.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.StreamNotificationsAsync("child-1", new DefaultHttpContext(), CancellationToken.None));
    }

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options);

    private static WebApplication CreateApp(
        Action<IServiceCollection> registerNotificationRepository,
        NotificationPersistenceProbe persistenceProbe,
        StreamBroadcastProbe broadcastProbe,
        HubSignalRProbe signalRProbe)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddSingleton<IWishlistRepository>(Mock.Of<IWishlistRepository>());
        builder.Services.AddSingleton<IRecommendationRepository>(Mock.Of<IRecommendationRepository>());
        builder.Services.AddSingleton<IStreamEventService>(Mock.Of<IStreamEventService>());
        builder.Services.AddSingleton<IStreamResumeStore, InMemoryStreamResumeStore>();
        builder.Services.AddSingleton<IStreamMetrics, InMemoryStreamMetrics>();
        builder.Services.AddScoped<ISseStreamService, SseStreamService>();
        builder.Services.AddSingleton<NotificationPersistenceProbe>(persistenceProbe);
        builder.Services.AddSingleton<StreamBroadcastProbe>(broadcastProbe);
        builder.Services.AddSingleton(signalRProbe.HubContext);
        registerNotificationRepository(builder.Services);
        builder.Services.AddSingleton<InMemoryStreamBroadcaster>();
        builder.Services.AddSingleton<IStreamBroadcaster>(serviceProvider =>
            new TrackingStreamBroadcaster(
                new HubStreamBroadcaster(
                    serviceProvider.GetRequiredService<InMemoryStreamBroadcaster>(),
                    serviceProvider.GetRequiredService<IHubContext<DrasiEventsHub>>(),
                    serviceProvider.GetRequiredService<ILogger<HubStreamBroadcaster>>()),
                broadcastProbe,
                () => persistenceProbe.StoredSuccessfully));

        var app = builder.Build();
        var v1 = app.MapGroup("/api/v1");
        v1.MapDrasiStreamApi();
        v1.MapDrasiDaprSubscriptions();
        return app;
    }

    private sealed class NotificationPersistenceProbe
    {
        private int _storedSuccessfully;

        public bool StoredSuccessfully => Volatile.Read(ref _storedSuccessfully) == 1;

        public void MarkStored() => Interlocked.Exchange(ref _storedSuccessfully, 1);
    }

    private sealed class StreamBroadcastProbe
    {
        private readonly TaskCompletionSource _subscribed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _publishCount;
        private int _persistenceConfirmedAtPublish;

        public TaskCompletionSource Subscribed => _subscribed;
        public int PublishCount => Volatile.Read(ref _publishCount);
        public bool PersistenceConfirmedAtPublish => Volatile.Read(ref _persistenceConfirmedAtPublish) == 1;

        public void MarkSubscribed() => _subscribed.TrySetResult();

        public void MarkPublished(bool persisted)
        {
            Interlocked.Increment(ref _publishCount);
            if (persisted)
            {
                Interlocked.Exchange(ref _persistenceConfirmedAtPublish, 1);
            }
        }
    }

    private sealed class ProbedNotificationRepository(
        INotificationRepository inner,
        NotificationPersistenceProbe probe) : INotificationRepository
    {
        public async Task StoreAsync(NotificationEntity entity)
        {
            await inner.StoreAsync(entity);
            probe.MarkStored();
        }

        public IAsyncEnumerable<NotificationEntity> ListAsync(string childId, int take = 50) =>
            inner.ListAsync(childId, take);
    }

    private sealed class FailingNotificationRepository : INotificationRepository
    {
        private int _storeCallCount;

        public int StoreCallCount => Volatile.Read(ref _storeCallCount);

        public Task StoreAsync(NotificationEntity entity)
        {
            Interlocked.Increment(ref _storeCallCount);
            return Task.FromException(new InvalidOperationException("Notification persistence unavailable."));
        }

        public async IAsyncEnumerable<NotificationEntity> ListAsync(string childId, int take = 50)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class TrackingStreamBroadcaster(
        IStreamBroadcaster inner,
        StreamBroadcastProbe probe,
        Func<bool> persistenceConfirmed) : IStreamBroadcaster
    {
        public ChannelReader<SseEvent> Subscribe(string childId)
        {
            probe.MarkSubscribed();
            return inner.Subscribe(childId);
        }

        public async Task PublishAsync(
            string childId,
            string eventType,
            object payload,
            CancellationToken ct = default)
        {
            probe.MarkPublished(persistenceConfirmed());
            await inner.PublishAsync(childId, eventType, payload, ct);
        }

        public Task PublishDrasiEventAsync(
            string queryId,
            string operation,
            object payload,
            CancellationToken ct = default) =>
            inner.PublishDrasiEventAsync(queryId, operation, payload, ct);
    }

    private sealed class HubSignalRProbe
    {
        private readonly Mock<IClientProxy> _client = new();
        private readonly Mock<IHubClients> _clients = new();
        private readonly Mock<IHubContext<DrasiEventsHub>> _hubContext = new();

        public HubSignalRProbe()
        {
            _client
                .Setup(client => client.SendCoreAsync(
                    It.IsAny<string>(),
                    It.IsAny<object?[]>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _clients.Setup(clients => clients.Group(It.IsAny<string>())).Returns(_client.Object);
            _hubContext.SetupGet(context => context.Clients).Returns(_clients.Object);
        }

        public IHubContext<DrasiEventsHub> HubContext => _hubContext.Object;

        public void AssertNotificationBroadcasted(string childId)
        {
            _clients.Verify(clients => clients.Group(childId), Times.Once);
            _client.Verify(client => client.SendCoreAsync(
                "stream",
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        public void AssertNoBroadcast()
        {
            _clients.Verify(clients => clients.Group(It.IsAny<string>()), Times.Never);
            _client.Verify(client => client.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
