using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Services;
using Xunit;

namespace UnitTests;

public sealed class DrasiServerViewClientTests
{
    [Fact]
    public async Task GetCurrentResultAsync_ReturnsObjectsFromSuccessEnvelope()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(JsonResponse(
            """{"success":true,"data":[{"item":"Wind-up train","frequency":3}],"error":null}""")));
        using var fixture = CreateClient(handler);

        var results = await fixture.Client.GetCurrentResultAsync("default", "wishlist-trending-1h");

        var result = Assert.Single(results);
        Assert.Equal("Wind-up train", result["item"]!.GetValue<string>());
        Assert.Equal(3, result["frequency"]!.GetValue<int>());
    }

    [Fact]
    public async Task GetCurrentResultAsync_EscapesInstanceAndQueryIdsInPath()
    {
        Uri? requestedUri = null;
        using var handler = new StubHandler((request, _) =>
        {
            requestedUri = request.RequestUri;
            return Task.FromResult(JsonResponse("""{"success":true,"data":[],"error":null}"""));
        });
        using var fixture = CreateClient(handler);

        await fixture.Client.GetCurrentResultAsync("instance /one", "wishlist query/1");

        Assert.Equal(
            "/api/v1/instances/instance%20%2Fone/queries/wishlist%20query%2F1/results",
            requestedUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetCurrentResultAsync_ThrowsForNonSuccessHttpStatus()
    {
        using var handler = new StubHandler((_, _) =>
            Task.FromResult(JsonResponse("unavailable", HttpStatusCode.ServiceUnavailable)));
        using var fixture = CreateClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => fixture.Client.GetCurrentResultAsync("default", "wishlist-trending-1h"));
    }

    [Fact]
    public async Task GetCurrentResultAsync_ThrowsForFailureEnvelope()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(JsonResponse(
            """{"success":false,"data":[],"error":"source unavailable"}""")));
        using var fixture = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Client.GetCurrentResultAsync("default", "wishlist-trending-1h"));
    }

    [Theory]
    [InlineData("""{"success":true}""")]
    [InlineData("""{"success":true,"data":{}}""")]
    [InlineData("""{"data":[]}""")]
    public async Task GetCurrentResultAsync_ThrowsForInvalidEnvelope(string body)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(JsonResponse(body)));
        using var fixture = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Client.GetCurrentResultAsync("default", "wishlist-trending-1h"));
    }

    [Fact]
    public async Task GetCurrentResultAsync_ThrowsForMalformedJson()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(JsonResponse("{")));
        using var fixture = CreateClient(handler);

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(
            () => fixture.Client.GetCurrentResultAsync("default", "wishlist-trending-1h"));
    }

    [Fact]
    public async Task GetCurrentResultAsync_PropagatesCancellation()
    {
        using var handler = new StubHandler((_, ct) =>
            Task.FromException<HttpResponseMessage>(new OperationCanceledException(ct)));
        using var fixture = CreateClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Client.GetCurrentResultAsync("default", "wishlist-trending-1h", cancellation.Token));
    }

    private static ClientFixture CreateClient(HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Drasi:ServerBaseUrl"] = "http://drasi.example"
            })
            .Build();
        var httpClient = new HttpClient(handler, disposeHandler: false);

        return new ClientFixture(
            new DrasiServerViewClient(httpClient, NullLogger<DrasiServerViewClient>.Instance, configuration),
            httpClient);
    }

    private static HttpResponseMessage JsonResponse(string body, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            sendAsync(request, cancellationToken);
    }

    private sealed class ClientFixture(DrasiServerViewClient client, HttpClient httpClient) : IDisposable
    {
        public DrasiServerViewClient Client { get; } = client;

        public void Dispose() => httpClient.Dispose();
    }
}
