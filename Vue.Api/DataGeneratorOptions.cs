namespace Vue.Api;

public sealed record DataGeneratorOptions
{
    public int IntervalSeconds { get; init; } = 10;
}
