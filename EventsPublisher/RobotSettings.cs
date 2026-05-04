namespace EventsPublisher;

public interface IRobotSettings
{
    TimeSpan WaitBetweenLoops { get; }
    int MaxMerchants { get; }
    int MaxCategories { get; }
    int MaxProducts { get; }
    int CategorylessProductProbabilityPercent { get; }
    bool IsEnabled { get; }
}

public interface IRobotSettingsManager : IRobotSettings
{
    RobotSettings GetSnapshot();
    RobotSettings Update(RobotSettings settings);
}

public sealed class RobotSettings
{
    public int WaitBetweenLoopsMs { get; set; } = 3_000;
    public int MaxMerchants { get; set; } = 50;
    public int MaxCategories { get; set; } = 10;
    public int MaxProducts { get; set; } = 30;
    public int CategorylessProductProbabilityPercent { get; set; } = 35;
    public bool IsEnabled { get; set; } = true;
}

public sealed class RobotSettingsStore : IRobotSettingsManager
{
    private readonly object _sync = new();
    private RobotSettings _current = Clone(new RobotSettings());

    public TimeSpan WaitBetweenLoops
    {
        get
        {
            lock (_sync)
            {
                return TimeSpan.FromMilliseconds(_current.WaitBetweenLoopsMs);
            }
        }
    }


    public int MaxCategories
    {
        get
        {
            lock (_sync)
            {
                return _current.MaxCategories;
            }
        }
    }

    public int MaxMerchants
    {
        get
        {
            lock (_sync)
            {
                return _current.MaxMerchants;
            }
        }
    }

    public int MaxProducts
    {
        get
        {
            lock (_sync)
            {
                return _current.MaxProducts;
            }
        }
    }

    public int CategorylessProductProbabilityPercent
    {
        get
        {
            lock (_sync)
            {
                return _current.CategorylessProductProbabilityPercent;
            }
        }
    }

    public bool IsEnabled
    {
        get
        {
            lock (_sync)
            {
                return _current.IsEnabled;
            }
        }
    }

    public RobotSettings GetSnapshot()
    {
        lock (_sync)
        {
            return Clone(_current);
        }
    }

    public RobotSettings Update(RobotSettings settings)
    {
        lock (_sync)
        {
            _current = Clone(settings);
            return Clone(_current);
        }
    }

    public static Dictionary<string, string[]> Validate(RobotSettings settings)
    {
        return [];
    }

    private static RobotSettings Clone(RobotSettings settings) =>
        new()
        {
            WaitBetweenLoopsMs = settings.WaitBetweenLoopsMs,
            MaxMerchants = settings.MaxMerchants,
            MaxCategories = settings.MaxCategories,
            MaxProducts = settings.MaxProducts,
            CategorylessProductProbabilityPercent = settings.CategorylessProductProbabilityPercent,
            IsEnabled = settings.IsEnabled
        };
}