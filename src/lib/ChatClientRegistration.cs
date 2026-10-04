using Azure.Identity;
using Drasicrhsit.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Models;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace Services;

public static class ChatClientRegistration
{
    public static IServiceCollection AddChatClientForRuntime(
        this IServiceCollection services,
        IConfiguration configuration,
        RuntimeMode mode)
    {
        if (mode == RuntimeMode.SelfHosted)
        {
            services.AddOptions<AiOptions>()
                .Bind(configuration.GetSection(AiOptions.SectionName))
                .Validate(
                    options => Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) &&
                               (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps),
                    "AI:Endpoint must be an absolute HTTP or HTTPS URL.")
                .Validate(
                    options => !string.IsNullOrWhiteSpace(options.Model),
                    "AI:Model must be configured.")
                .ValidateOnStart();

            services.AddSingleton<IChatClient>(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<AiOptions>>().Value;
                var endpoint = new Uri(options.Endpoint);
                var credential = new ApiKeyCredential(options.ApiKey ?? string.Empty);
                var clientOptions = new OpenAIClientOptions { Endpoint = endpoint };
                return new ChatClient(options.Model, credential, clientOptions).AsIChatClient();
            });
        }
        else
        {
            services.AddSingleton<IChatClient>(serviceProvider =>
            {
                var endpoint = ConfigurationHelper.GetRequiredValue(
                    configuration,
                    "AzureOpenAI:Endpoint",
                    "AZURE_OPENAI_ENDPOINT");
                var deploymentName = ConfigurationHelper.GetRequiredValue(
                    configuration,
                    "AzureOpenAI:DeploymentName",
                    "AZURE_OPENAI_DEPLOYMENT_NAME");
                var apiKey = configuration["AzureOpenAI:ApiKey"] ?? configuration["AZURE_OPENAI_API_KEY"];
                var clientOptions = new Azure.AI.OpenAI.AzureOpenAIClientOptions
                {
                    NetworkTimeout = TimeSpan.FromSeconds(60)
                };
                var azureClient = !string.IsNullOrEmpty(apiKey)
                    ? new Azure.AI.OpenAI.AzureOpenAIClient(
                        new Uri(endpoint),
                        new Azure.AzureKeyCredential(apiKey),
                        clientOptions)
                    : new Azure.AI.OpenAI.AzureOpenAIClient(
                        new Uri(endpoint),
                        new DefaultAzureCredential(),
                        clientOptions);
                return azureClient.GetChatClient(deploymentName).AsIChatClient();
            });
        }

        services.AddSingleton<AIAgent>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ElfRecommendationAgentOptions>>().Value;
            var instructions = options.SystemPromptOverride ?? ElfAgentPrompts.ElfRecommendationAgentSystemPrompt;
            return serviceProvider.GetRequiredService<IChatClient>()
                .AsAIAgent(name: "ElfRecommendationAgent", instructions: instructions);
        });

        return services;
    }
}
