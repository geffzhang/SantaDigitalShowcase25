using System.Net;
using System.Net.Sockets;
using System.Globalization;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var apiPort = ReadPort(builder.Configuration, "DEV_API_PORT", 8081);
var postgresPort = ReadPort(builder.Configuration, "DEV_POSTGRES_PORT", 5433);
var drasiPort = ReadPort(builder.Configuration, "DEV_DRASI_PORT", 8080);
EnsurePortAvailable(apiPort, "API", "DEV_API_PORT");
EnsurePortAvailable(postgresPort, "PostgreSQL", "DEV_POSTGRES_PORT");
EnsurePortAvailable(drasiPort, "Drasi Server", "DEV_DRASI_PORT");

var modelEndpoint = RequiredConfiguration(builder.Configuration, "LLM_BASE_URL");
if (!Uri.TryCreate(modelEndpoint, UriKind.Absolute, out var endpointUri) ||
    (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
{
    throw new InvalidOperationException("LLM_BASE_URL must be an absolute HTTP or HTTPS URL.");
}

var modelName = RequiredConfiguration(builder.Configuration, "LLM_MODEL_NAME");
_ = RequiredConfiguration(builder.Configuration, "LLM_API_KEY");
var modelKey = builder.AddParameterFromConfiguration("llm-api-key", "LLM_API_KEY", secret: true);
var postgresUser = builder.AddParameter("postgres-user", "postgres");
var postgresPassword = builder.AddParameter("postgres-password", secret: true);

var postgres = builder.AddPostgres("postgres", postgresUser, postgresPassword, postgresPort)
    .WithImageTag("18.3")
    .WithEndpointProxySupport(false)
    .WithDataVolume()
    .WithArgs(
        "-c", "wal_level=logical",
        "-c", "max_replication_slots=10",
        "-c", "max_wal_senders=10");
var database = postgres.AddDatabase("elves");

var api = builder.AddDockerfile("api", "..", "src/Dockerfile")
    .WithHttpEndpoint(targetPort: 80, port: apiPort, name: "http", isProxied: false)
    .WithEnvironment("ASPNETCORE_URLS", "http://0.0.0.0:80")
    .WithEnvironment("Runtime__Mode", "SelfHosted")
    .WithEnvironment("AI__Endpoint", endpointUri.ToString())
    .WithEnvironment("AI__Model", modelName)
    .WithEnvironment("AI__ApiKey", modelKey)
    .WithReference(database)
    .WaitFor(database)
    .WithHttpHealthCheck("/healthz");

var drasiServer = builder.AddContainer(
        "drasi-server",
        "ghcr.io/drasi-project/drasi-server",
        "0.2.3")
    .WithArgs("--config", "/app/config/server.yaml", "--plugins-dir", "/tmp/drasi-plugins")
    .WithBindMount("../drasi/selfhosted", "/app/config", isReadOnly: true)
    .WithHttpEndpoint(targetPort: 8080, port: drasiPort, name: "http", isProxied: false)
    .WithEnvironment("DB_HOST", postgres.Resource.Name)
    .WithEnvironment("DB_PORT", "5432")
    .WithEnvironment("DB_NAME", "elves")
    .WithEnvironment("DB_USER", postgresUser)
    .WithEnvironment("DB_PASSWORD", postgresPassword)
    .WithEnvironment("API_BASE_URL", api.GetEndpoint("http"))
    .WithReference(database)
    .WaitFor(api);

api.WithEnvironment("DRASI_SERVER_BASE_URL", drasiServer.GetEndpoint("http"));

builder.AddViteApp("frontend", "../frontend", "dev")
    .WithEnvironment("VITE_API_URL", "http://localhost:" + apiPort.ToString(CultureInfo.InvariantCulture));

builder.Build().Run();

static string RequiredConfiguration(IConfiguration configuration, string key)
{
    var value = configuration[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"{key} must be configured.");
    }

    return value;
}

static int ReadPort(IConfiguration configuration, string key, int defaultPort)
{
    var configured = configuration[key];
    if (string.IsNullOrWhiteSpace(configured))
    {
        return defaultPort;
    }

    if (!int.TryParse(configured, out var port) || port is < 1 or > 65535)
    {
        throw new InvalidOperationException($"{key} must be a valid TCP port.");
    }

    return port;
}

static void EnsurePortAvailable(int port, string resource, string configurationKey)
{
    try
    {
        using var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
    }
    catch (SocketException ex)
    {
        throw new InvalidOperationException(
            $"The configured {resource} port {port} is already in use. Set {configurationKey} to an available port.",
            ex);
    }
}
