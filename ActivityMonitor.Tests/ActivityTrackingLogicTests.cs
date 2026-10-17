using System;
using System.Collections.Generic;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Enums;
using ActivityMonitor.Core.Models;
using Xunit;

namespace ActivityMonitor.Tests;

public class ActivityTrackingLogicTests
{
    [Fact]
    public void DurationCalculation_ShouldMatchDifferenceBetweenStartAndEnd()
    {
        var start = new DateTime(2026, 10, 4, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 10, 4, 10, 15, 30, DateTimeKind.Utc);

        var duration = (end - start).TotalSeconds;

        var activity = new ActivityEvent
        {
            StartedAtUtc = start,
            EndedAtUtc = end,
            DurationSeconds = duration
        };

        Assert.Equal(930.0, activity.DurationSeconds);
        Assert.Equal(TimeSpan.FromMinutes(15.5), TimeSpan.FromSeconds(activity.DurationSeconds));
    }

    [Fact]
    public void ActivityMerging_ShouldAccumulateDuration_WhenContextIsSame()
    {
        var start = new DateTime(2026, 10, 4, 10, 0, 0, DateTimeKind.Utc);
        var current = new ActivityEvent
        {
            ProcessId = 1234,
            ProcessName = "devenv",
            WindowTitle = "ActivityMonitor - Visual Studio",
            ContextPath = @"C:\Users\Masoud\source\repos\ActivityMonitor",
            StartedAtUtc = start,
            EndedAtUtc = start,
            DurationSeconds = 1.0
        };

        // Simulate 5 periodic checks where window remains identical
        for (int i = 1; i <= 5; i++)
        {
            var checkTime = start.AddSeconds(i * 10);
            current.EndedAtUtc = checkTime;
            current.DurationSeconds = (checkTime - current.StartedAtUtc).TotalSeconds;
        }

        Assert.Equal(50.0, current.DurationSeconds);
        Assert.Equal(start.AddSeconds(50), current.EndedAtUtc);
    }

    [Fact]
    public void IdleTransition_ShouldTransitionFromActiveToIdleProperly()
    {
        var userActiveTime = new DateTime(2026, 10, 4, 11, 0, 0, DateTimeKind.Utc);
        var activeEvent = new ActivityEvent
        {
            ProcessName = "chrome",
            WindowTitle = "Google Search",
            EventType = ActivityEventType.ActiveWindow,
            StartedAtUtc = userActiveTime,
            EndedAtUtc = userActiveTime.AddMinutes(10),
            DurationSeconds = 600
        };

        // User goes idle for 5 minutes
        var idleThreshold = TimeSpan.FromMinutes(5);
        var idleEvent = new ActivityEvent
        {
            ProcessName = "Idle",
            WindowTitle = "User Idle",
            EventType = ActivityEventType.IdleStart,
            StartedAtUtc = activeEvent.EndedAtUtc.Value,
            EndedAtUtc = activeEvent.EndedAtUtc.Value.Add(idleThreshold),
            DurationSeconds = idleThreshold.TotalSeconds
        };

        Assert.Equal(ActivityEventType.ActiveWindow, activeEvent.EventType);
        Assert.Equal(ActivityEventType.IdleStart, idleEvent.EventType);
        Assert.Equal(activeEvent.EndedAtUtc, idleEvent.StartedAtUtc);
        Assert.Equal(300.0, idleEvent.DurationSeconds);
    }

    [Fact]
    public void IdleStart_ShouldBeCalculatedFromLastInput_WithoutOverlappingActiveTime()
    {
        var observedAtUtc = new DateTime(2026, 10, 4, 11, 5, 0, DateTimeKind.Utc);
        var idleTime = TimeSpan.FromMinutes(5);

        var idleStartUtc = ActivityTimelineMath.CalculateIdleStartUtc(observedAtUtc, idleTime);
        var activeEndUtc = ActivityTimelineMath.ClampEndUtc(observedAtUtc.AddMinutes(-10), idleStartUtc);

        Assert.Equal(new DateTime(2026, 10, 4, 11, 0, 0, DateTimeKind.Utc), idleStartUtc);
        Assert.Equal(idleStartUtc, activeEndUtc);
        Assert.Equal(300, ActivityTimelineMath.CalculateDurationSeconds(idleStartUtc, observedAtUtc));
    }

    [Fact]
    public void DurationCalculation_ShouldClampEndBeforeStart()
    {
        var start = new DateTime(2026, 10, 4, 11, 0, 0, DateTimeKind.Utc);

        var duration = ActivityTimelineMath.CalculateDurationSeconds(start, start.AddSeconds(-10));

        Assert.Equal(0, duration);
    }

    [Fact]
    public void DailyOverlap_ShouldSplitActivityAcrossMidnight()
    {
        var activityStart = new DateTime(2026, 10, 4, 23, 59, 0, DateTimeKind.Utc);
        var activityEnd = new DateTime(2026, 10, 5, 0, 10, 0, DateTimeKind.Utc);
        var firstDayStart = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc);
        var secondDayStart = firstDayStart.AddDays(1);

        var firstDaySeconds = ActivityTimelineMath.CalculateOverlapSeconds(
            activityStart, activityEnd, 660, firstDayStart, secondDayStart);
        var secondDaySeconds = ActivityTimelineMath.CalculateOverlapSeconds(
            activityStart, activityEnd, 660, secondDayStart, secondDayStart.AddDays(1));

        Assert.Equal(60, firstDaySeconds);
        Assert.Equal(600, secondDaySeconds);
    }

    [Fact]
    public void IdleCalculation_TickWrapAroundSafety_ShouldNotReportSpuriousIdle()
    {
        // Simulate tick discrepancy where dwTime is slightly ahead of tickCount
        uint tickCount = 100000;
        uint lastInputTime = 100050; // 50ms ahead due to kernel/system discrepancy

        TimeSpan idleTime;
        if (tickCount >= lastInputTime)
        {
            idleTime = TimeSpan.FromMilliseconds(tickCount - lastInputTime);
        }
        else
        {
            idleTime = TimeSpan.Zero;
        }

        // Verify it never wraps around to 49 days (4,294,967,290 ms)
        Assert.Equal(TimeSpan.Zero, idleTime);
        Assert.True(idleTime < TimeSpan.FromDays(1));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(4.99, false)]
    [InlineData(5, true)]
    [InlineData(30, true)]
    public void ActivityDrivenRecording_ShouldStopAtConfiguredIdleThreshold(
        double idleMinutes,
        bool expectedIdle)
    {
        var configuredThreshold = TimeSpan.FromMinutes(5);
        var idleTime = TimeSpan.FromMinutes(idleMinutes);

        Assert.Equal(expectedIdle, idleTime >= configuredThreshold);
    }

    [Fact]
    public void ApplicationEvent_ShouldRemainInsideRecordingCoverage()
    {
        var segmentStart = new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc);
        var segmentEnd = segmentStart.AddMinutes(5);
        var applicationStart = segmentStart.AddSeconds(20);
        var applicationEnd = segmentStart.AddMinutes(3);

        var coveredSeconds = ActivityTimelineMath.CalculateOverlapSeconds(
            applicationStart,
            applicationEnd,
            (applicationEnd - applicationStart).TotalSeconds,
            segmentStart,
            segmentEnd);

        Assert.Equal((applicationEnd - applicationStart).TotalSeconds, coveredSeconds);
    }
}


