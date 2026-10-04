using System.ComponentModel;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Models;
using Moq;
using Services;
using Xunit;

namespace IntegrationTests;

public sealed class LocalAiFactAttribute : FactAttribute
{
    public LocalAiFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("LOCAL_AI_TESTS"), "1", StringComparison.Ordinal))
        {
            Skip = "Set LOCAL_AI_TESTS=1 to call the configured self-hosted model service.";
        }
    }
}

public class LocalAiAgentTests
{
    [LocalAiFact]
    public async Task SelfHostedAgent_InvokesBudgetToolWithExpectedResult()
    {
        var configuration = CreateLocalAiConfiguration();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddChatClientForRuntime(configuration, RuntimeMode.SelfHosted);

        using var provider = services.BuildServiceProvider();
        var chatClient = provider.GetRequiredService<IChatClient>();
        var toolLibrary = CreateToolLibrary();
        var toolCallCount = 0;
        string? toolResult = null;

        [Description("Validates if a list of gift suggestions fits within a specified budget")]
        async Task<string> CheckBudgetConstraints(
            [Description("Comma-separated list of gift names")] string giftNames,
            [Description("Total budget in dollars")] decimal budget)
        {
            Interlocked.Increment(ref toolCallCount);
            toolResult = await toolLibrary.CheckBudgetConstraints(giftNames, budget);
            return toolResult;
        }

        var budgetTool = AIFunctionFactory.Create(CheckBudgetConstraints);
        var agent = chatClient.AsAIAgent(
            name: "BudgetValidator",
            instructions: """
                You are a tool-call test agent. Always call CheckBudgetConstraints exactly once
                with giftNames set to "Lego, book" and budget set to 60. Report the tool result.
                Do not calculate prices yourself.
                """,
            tools: [budgetTool]);

        await agent.RunAsync(
            "Validate Lego and book against a budget of 60, then report the result.");

        Assert.Equal(1, toolCallCount);
        Assert.NotNull(toolResult);
        Assert.Contains("Total Budget: $60.00", toolResult, StringComparison.Ordinal);
        Assert.Contains("Estimated Cost: $54.98", toolResult, StringComparison.Ordinal);
        Assert.Contains("Within Budget", toolResult, StringComparison.Ordinal);
    }

    [LocalAiFact]
    public async Task AgUiAgentRun_EmitsSseContentAndTerminalEvent()
    {
        var configuration = CreateLocalAiConfiguration();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddChatClientForRuntime(builder.Configuration, RuntimeMode.SelfHosted);

        var drasiClient = new Mock<IDrasiViewClient>();
        drasiClient
            .Setup(client => client.GetCurrentResultAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JsonNode>());
        builder.Services.AddSingleton(drasiClient.Object);

        var profileService = new Mock<IChildProfileService>();
        profileService
            .Setup(service => service.GetChildProfileAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile("child-1", "Sam", 8, ["trains"], null, null, NiceStatus.Nice));
        builder.Services.AddSingleton(profileService.Object);

        await using var app = builder.Build();
        app.MapAgUi();
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var request = new StringContent(
            """{"messages":[{"role":"user","content":"Suggest one friendly gift idea."}]}""",
            Encoding.UTF8,
            "application/json");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var response = await client.PostAsync(
            "/agents/elf-agent-child-1/run",
            request,
            timeout.Token);
        var eventStream = await response.Content.ReadAsStringAsync(timeout.Token);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("text/event-stream", response.Content.Headers.ContentType?.MediaType, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TEXT_MESSAGE_CONTENT", eventStream, StringComparison.Ordinal);
        Assert.Contains("RUN_FINISHED", eventStream, StringComparison.Ordinal);

        await app.StopAsync();
    }

    private static IConfiguration CreateLocalAiConfiguration()
    {
        var endpoint = Environment.GetEnvironmentVariable("LLM_BASE_URL");
        var model = Environment.GetEnvironmentVariable("LLM_MODEL_NAME");
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException(
                "LOCAL_AI_TESTS=1 requires LLM_BASE_URL and LLM_MODEL_NAME to be configured.");
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Endpoint"] = endpoint,
                ["AI:Model"] = model,
                ["AI:ApiKey"] = Environment.GetEnvironmentVariable("LLM_API_KEY")
            })
            .Build();
    }

    private static AgentToolLibrary CreateToolLibrary() => new(
        new Mock<IRecommendationService>().Object,
        new Mock<IChildProfileService>().Object,
        new Mock<IAvailabilityService>().Object,
        new Mock<IDrasiViewClient>().Object,
        new Mock<IWishlistRepository>().Object,
        new ConfigurationBuilder().AddInMemoryCollection().Build(),
        NullLogger<AgentToolLibrary>.Instance);
}
