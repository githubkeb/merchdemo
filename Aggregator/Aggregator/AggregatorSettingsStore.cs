namespace Aggregator;

public interface IAggregatorSettings
{
    int FailDuringAggregationPercent { get; }
}

public interface IAggregatorSettingsManager : IAggregatorSettings
{
    AggregatorSettings GetSnapshot();
    AggregatorSettings Update(AggregatorSettings settings);
}

public sealed class AggregatorSettings
{
    public int FailDuringAggregationPercent { get; set; }
}

public sealed class AggregatorSettingsStore : IAggregatorSettingsManager
{
    private readonly object _sync = new();
    private AggregatorSettings _current = Clone(new AggregatorSettings());

    public int FailDuringAggregationPercent
    {
        get
        {
            lock (_sync)
            {
                return _current.FailDuringAggregationPercent;
            }
        }
    }

    public AggregatorSettings GetSnapshot()
    {
        lock (_sync)
        {
            return Clone(_current);
        }
    }

    public AggregatorSettings Update(AggregatorSettings settings)
    {
        lock (_sync)
        {
            _current = Clone(settings);
            return Clone(_current);
        }
    }

    private static AggregatorSettings Clone(AggregatorSettings settings) =>
        new()
        {
            FailDuringAggregationPercent = settings.FailDuringAggregationPercent
        };
}


