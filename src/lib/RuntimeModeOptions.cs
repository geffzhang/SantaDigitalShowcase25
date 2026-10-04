namespace Services;

public enum RuntimeMode
{
    Azure,
    SelfHosted
}

public sealed class RuntimeModeOptions
{
    public const string SectionName = "Runtime";

    public RuntimeMode Mode { get; init; } = RuntimeMode.Azure;
}
