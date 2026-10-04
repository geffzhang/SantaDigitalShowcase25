using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Models;
using Moq;
using Services;
using Xunit;

namespace UnitTests;

public class AgentProviderWiringTests
{
    [Fact]
    public async Task MultiAgentOrchestrator_UsesInjectedChatClientWithoutAzureConfiguration()
    {
        var childProfileService = new Mock<IChildProfileService>();
        childProfileService
            .Setup(service => service.GetChildProfileAsync("child-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile("child-1", "Sam", 8, ["trains"], null, null, NiceStatus.Nice));

        var recommendationService = new Mock<IRecommendationService>();
        recommendationService
            .Setup(service => service.GetTopNAsync("child-1", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Recommendation>());

        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Injected provider response")));

        var toolLibrary = new AgentToolLibrary(
            recommendationService.Object,
            childProfileService.Object,
            new Mock<IAvailabilityService>().Object,
            new Mock<IDrasiViewClient>().Object,
            new Mock<IWishlistRepository>().Object,
            new ConfigurationBuilder().AddInMemoryCollection().Build(),
            NullLogger<AgentToolLibrary>.Instance);
        var orchestrator = new MultiAgentOrchestrator(
            childProfileService.Object,
            recommendationService.Object,
            toolLibrary,
            chatClient.Object,
            NullLogger<MultiAgentOrchestrator>.Instance);

        var result = await orchestrator.RunCollaborativeRecommendationAsync("child-1", NiceStatus.Nice);

        Assert.Contains("Multi-Agent Collaborative Recommendation", result, StringComparison.Ordinal);
        Assert.Contains("Injected provider response", result, StringComparison.Ordinal);
        chatClient.Verify(
            client => client.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }
}
