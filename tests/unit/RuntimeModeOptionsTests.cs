using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Services;
using System.Text.Json;
using Xunit;

namespace UnitTests;

public class RuntimeModeOptionsTests
{
    [Fact]
    public void AppSettings_ExplicitlyDefaultsRuntimeModeToAzure()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? settingsPath = null;
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "appsettings.json");
            if (File.Exists(candidate))
            {
                settingsPath = candidate;
                break;
            }

            directory = directory.Parent;
        }

        Assert.NotNull(settingsPath);
        using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
        var mode = settings.RootElement
            .GetProperty(RuntimeModeOptions.SectionName)
            .GetProperty(nameof(RuntimeModeOptions.Mode))
            .GetString();

        Assert.Equal(nameof(RuntimeMode.Azure), mode);
    }

    [Fact]
    public void AddApplicationRuntime_DefaultsToAzureWhenModeIsNotConfigured()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();

        services.AddApplicationRuntime(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<RuntimeModeOptions>();
        Assert.Equal(RuntimeMode.Azure, options.Mode);
    }

    [Fact]
    public void AddApplicationRuntime_BindsSelfHostedMode()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Runtime:Mode"] = "SelfHosted",
                ["ConnectionStrings:elves"] = "Host=localhost;Database=elves;Username=postgres;Password=postgres"
            })
            .Build();

        services.AddApplicationRuntime(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<RuntimeModeOptions>();
        Assert.Equal(RuntimeMode.SelfHosted, options.Mode);
    }

    [Fact]
    public void AddApplicationRuntime_RejectsUnsupportedModeWithConfigurationKey()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Runtime:Mode"] = "Local"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddApplicationRuntime(configuration));

        Assert.Contains("Runtime:Mode", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Local", exception.Message, StringComparison.Ordinal);
    }
}
