using Xunit;

namespace ActivityMonitor.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ActivityMonitorPathsCollection
{
    public const string Name = "ActivityMonitorPaths";
}
