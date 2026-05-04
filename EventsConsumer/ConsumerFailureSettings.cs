namespace EventsConsumer;

public interface IConsumerSettings
{
    int FailBeforeSavePercent { get; }
    int FailAfterSaveBeforeAckPercent { get; }
}

public interface IConsumerSettingsManager : IConsumerSettings
{
    ConsumerSettings GetSnapshot();
    ConsumerSettings Update(ConsumerSettings settings);
}

public sealed class ConsumerSettings
{
    public int FailBeforeSavePercent { get; set; }
    public int FailAfterSaveBeforeAckPercent { get; set; }
}

public sealed class ConsumerSettingsStore : IConsumerSettingsManager
{
    private readonly object _sync = new();
    private ConsumerSettings _current = Clone(new ConsumerSettings());

    public int FailBeforeSavePercent
    {
        get
        {
            lock (_sync)
            {
                return _current.FailBeforeSavePercent;
            }
        }
    }

    public int FailAfterSaveBeforeAckPercent
    {
        get
        {
            lock (_sync)
            {
                return _current.FailAfterSaveBeforeAckPercent;
            }
        }
    }

    public ConsumerSettings GetSnapshot()
    {
        lock (_sync)
        {
            return Clone(_current);
        }
    }

    public ConsumerSettings Update(ConsumerSettings settings)
    {
        lock (_sync)
        {
            _current = Clone(settings);
            return Clone(_current);
        }
    }

    public static Dictionary<string, string[]> Validate(ConsumerSettings settings)
    {
        return [];
    }

    private static ConsumerSettings Clone(ConsumerSettings settings) =>
        new()
        {
            FailBeforeSavePercent = settings.FailBeforeSavePercent,
            FailAfterSaveBeforeAckPercent = settings.FailAfterSaveBeforeAckPercent
        };
}

