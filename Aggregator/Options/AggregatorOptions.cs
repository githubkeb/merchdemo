namespace Aggregator.Options;

public sealed class AggregatorOptions
{
    public const string SectionName = "Aggregator";

    public int BatchSize { get; set; } = 200;
    public int PollIntervalMs { get; set; } = 2000;
    public int FailDuringAggregationPercent { get; set; } = 0;
}

