using System;
using ActivityMonitor.Core.Options;
using Xunit;

namespace ActivityMonitor.Tests;

public class ConfigurationValidationTests
{
    [Fact]
    public void ActivityTrackingOptions_DefaultValues_ShouldMatchSpecification()
    {
        var options = new ActivityTrackingOptions();
        Assert.True(options.Enabled);
        Assert.Equal(5, options.IdleThresholdMinutes);
        Assert.Equal(500, options.PollingIntervalMs);
    }

    [Fact]
    public void RecordingOptions_DefaultValues_ShouldMatchSpecification()
    {
        var options = new RecordingOptions();
        Assert.True(options.Enabled);
        Assert.Equal(10, options.SegmentMinutes); // Default 10 minutes segment per spec
        Assert.Equal(14, options.RetentionDays);
        Assert.Equal(50, options.MaxStorageSizeGB);
        Assert.Equal(2, options.MinimumFreeDiskSpaceGB);
    }

    [Fact]
    public void ReportsOptions_DefaultValues_ShouldMatchSpecification()
    {
        var options = new ReportsOptions();
        Assert.True(options.ExcelEnabled);
        Assert.True(options.CsvEnabled);
        Assert.True(options.TxtEnabled);
        Assert.True(options.DailyReportEnabled);
    }

    [Fact]
    public void StorageOptions_DefaultValues_ShouldMatchSpecification()
    {
        var options = new StorageOptions();
        Assert.Equal(14, options.RecordingRetentionDays);
        Assert.Equal(90, options.ActivityRetentionDays);
        Assert.Equal(90, options.ReportRetentionDays);
        Assert.Equal(30, options.LogRetentionDays);
        Assert.Equal(50, options.MaxStorageSizeGB);
        Assert.Equal(2, options.MinimumFreeDiskSpaceGB);
    }
}
