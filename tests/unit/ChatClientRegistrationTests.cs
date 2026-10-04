using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using Services;
using Xunit;

namespace UnitTests;

public class ChatClientRegistrationTests
{
    [Fact]
    public void AddChatClientForRuntime_SelfHostedUsesConfiguredOpenAiCompatibleEndpoint()
    {
        const string endpoint = "http://127.0.0.1:8765/v1";
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["AI:Endpoint"] = endpoint,
            ["AI:Model"] = "local-model",
            ["AI:ApiKey"] = "test-only-key"
        });
        var services = new ServiceCollection();
        services.AddChatClientForRuntime(configuration, RuntimeMode.SelfHosted);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IChatClient>();
        var openAiClient = Assert.IsType<ChatClient>(client.GetService(typeof(ChatClient)));

        Assert.Equal(
            new Uri(endpoint),
            typeof(ChatClient).GetProperty("Endpoint")?.GetValue(openAiClient));
        Assert.Equal(
            "local-model",
            typeof(ChatClient).GetProperty("Model")?.GetValue(openAiClient));
    }

    [Fact]
    public void AddChatClientForRuntime_SelfHostedMissingEndpointFailsWithoutRevealingApiKey()
    {
        const string apiKey = "sensitive-test-only-key";
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["AI:Model"] = "local-model",
            ["AI:ApiKey"] = apiKey
        });
        var services = new ServiceCollection();
        services.AddChatClientForRuntime(configuration, RuntimeMode.SelfHosted);

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AiOptions>>().Value);

        Assert.Contains("AI:Endpoint", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddChatClientForRuntime_SelfHostedMissingModelFailsWithConfigurationKey()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["AI:Endpoint"] = "http://127.0.0.1:8765/v1"
        });
        var services = new ServiceCollection();
        services.AddChatClientForRuntime(configuration, RuntimeMode.SelfHosted);

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AiOptions>>().Value);

        Assert.Contains("AI:Model", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddChatClientForRuntime_AzureUsesAzureCompatibleClient()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["AzureOpenAI:Endpoint"] = "https://example.invalid",
            ["AzureOpenAI:DeploymentName"] = "test-deployment"
        });
        var services = new ServiceCollection();
        services.AddChatClientForRuntime(configuration, RuntimeMode.Azure);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IChatClient>();

        Assert.IsNotType<ChatClient>(client.GetService(typeof(ChatClient)));
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
