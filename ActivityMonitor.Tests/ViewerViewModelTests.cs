using System;
using System.IO;
using ActivityMonitor.Viewer.Services;
using ActivityMonitor.Viewer.ViewModels;
using Xunit;

namespace ActivityMonitor.Tests;

public class ViewerViewModelTests
{
    [Fact]
    public void ActivityItemViewModel_DisplaysCorrectDurationAndOffset()
    {
        var startUtc = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2026, 10, 4, 12, 13, 4, DateTimeKind.Utc);

        var item = new ActivityItemViewModel
        {
            Id = 1,
            StartedAtUtc = startUtc,
            EndedAtUtc = endUtc,
            ProcessName = "chrome",
            WindowTitle = "GitHub - PR #42",
            DurationSeconds = 784, // 13m 4s
            VideoOffset = TimeSpan.FromSeconds(176), // 02:56
            RecordingFileName = "2026-10-04_12-00-00.mp4"
        };

        Assert.Equal("13د 4ث", item.DurationDisplay);
        Assert.Equal("02:56", item.OffsetDisplay);
        Assert.Equal("#38BDF8", item.AppBadgeColor);
    }

    [Fact]
    public void ActivityItemViewModel_IdleAppBadge_ShouldBeSlate()
    {
        var item = new ActivityItemViewModel
        {
            ProcessName = "Idle",
            DurationSeconds = 300,
            VideoOffset = TimeSpan.Zero
        };

        Assert.Equal("#94A3B8", item.AppBadgeColor);
        Assert.Equal("5د 0ث", item.DurationDisplay);
        Assert.Equal("00:00", item.OffsetDisplay);
    }

    [Fact]
    public void RecordingSegmentViewModel_CalculatesDisplayPropertiesProperly()
    {
        var start = new DateTime(2026, 10, 4, 14, 0, 0, DateTimeKind.Utc);
        var end = start.AddMinutes(10);

        var segment = new RecordingSegmentViewModel
        {
            Id = 1,
            StartedAtUtc = start,
            EndedAtUtc = end,
            FileName = "segment1.mp4",
            FilePath = @"C:\Fake\segment1.mp4",
            DurationSeconds = 600,
            FileSizeBytes = 15 * 1024 * 1024 // 15 MB
        };

        Assert.Equal("10:00", segment.DurationDisplay);
        Assert.Equal("15.00 MB", segment.FileSizeDisplay);
        Assert.False(segment.ExistsOnDisk);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 5)]
    [InlineData(3, 10)]
    [InlineData(4, 15)]
    public void SettingsViewModel_SegmentIndexMapsToDisplayedMinutes(int selectedIndex, int expectedMinutes)
    {
        var settings = new SettingsViewModel(new WindowsServiceManager());

        settings.SelectedSegmentMinutesIndex = selectedIndex;

        Assert.Equal(expectedMinutes, settings.SegmentMinutes);
        Assert.Equal(selectedIndex, settings.SelectedSegmentMinutesIndex);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 5)]
    [InlineData(4, 10)]
    [InlineData(5, 15)]
    [InlineData(6, 30)]
    public void SettingsViewModel_IdleIndexControlsRecordingStopDelay(int selectedIndex, int expectedMinutes)
    {
        var settings = new SettingsViewModel(new WindowsServiceManager());

        settings.SelectedIdleMinutesIndex = selectedIndex;

        Assert.Equal(expectedMinutes, settings.IdleThresholdMinutes);
        Assert.Equal(selectedIndex, settings.SelectedIdleMinutesIndex);
    }

    [Fact]
    public void SettingsViewModel_ActivityDrivenRecordingIsEnabledByDefault()
    {
        var settings = new SettingsViewModel(new WindowsServiceManager());

        Assert.True(settings.RecordOnlyWhenUserActive);
        Assert.Equal(5, settings.IdleThresholdMinutes);
    }
}
