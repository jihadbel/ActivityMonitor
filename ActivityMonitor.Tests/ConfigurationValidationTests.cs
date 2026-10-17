using System;
using ActivityMonitor.Core.Options;
using ActivityMonitor.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
        Assert.Equal(2000, options.PollingIntervalMs);
    }

    [Fact]
    public void RecordingOptions_DefaultValues_ShouldMatchSpecification()
    {
        var options = new RecordingOptions();
        Assert.True(options.Enabled);
        Assert.True(options.RecordOnlyWhenUserActive);
        Assert.Equal(10, options.SegmentMinutes); // Default 10 minutes segment per spec
        Assert.Equal(14, options.RetentionDays);
        Assert.Equal(50, options.MaxStorageSizeGB);
        Assert.Equal(2, options.MinimumFreeDiskSpaceGB);
        Assert.False(options.RecordAudio);
        Assert.False(options.RecordMicrophone);
        Assert.False(options.RecordSystemAudio);
        Assert.Equal(128, options.AudioBitrateKbps);
        Assert.True(options.IsFragmentedMp4Enabled);
    }

    [Fact]
    public void ReportsOptions_DefaultValues_ShouldMatchSpecification()
    {
        var options = new ReportsOptions();
        Assert.True(options.ExcelEnabled);
        Assert.True(options.CsvEnabled);
        Assert.True(options.TxtEnabled);
        Assert.True(options.DailyReportEnabled);
        Assert.Equal(TimeSpan.FromMinutes(5), options.DailyReportScheduleTime);
        Assert.Equal(7, options.BackfillDays);
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

    [Fact]
    public void InvalidStorageConfiguration_ShouldFailOptionsValidation()
    {
        var values = new Dictionary<string, string?>
        {
            ["Storage:RecordingRetentionDays"] = "0",
            ["Storage:MaxStorageSizeGB"] = "-1"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructureServices(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<StorageOptions>>().Value);
    }

    [Fact]
    public void InvalidReportsConfiguration_ShouldFailOptionsValidation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Reports:DailyReportScheduleTime"] = "25:00:00",
                ["Reports:BackfillDays"] = "0"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructureServices(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<ReportsOptions>>().Value);
    }
}
