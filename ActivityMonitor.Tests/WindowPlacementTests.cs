using System.Windows;
using ActivityMonitor.Viewer.Services;
using Xunit;

namespace ActivityMonitor.Tests;

public class WindowPlacementTests
{
    [Theory]
    [InlineData(0, 0, 1366, 728, 1420, 860)]
    [InlineData(0, 40, 1280, 984, 1420, 860)]
    [InlineData(-1920, 0, 1920, 1040, 1420, 860)]
    [InlineData(1920, -200, 2560, 1400, 2130, 1290)]
    [InlineData(0, 0, 640, 480, 1420, 860)]
    public void StartupBounds_FitAndCenterOnTheSelectedMonitor(
        double left, double top, double width, double height, double requestedWidth, double requestedHeight)
    {
        var workArea = new Rect(left, top, width, height);
        var result = WindowPlacementService.FitBounds(workArea,
            new Rect(500, 500, requestedWidth, requestedHeight), center: true);

        Assert.True(workArea.Contains(result));
        Assert.Equal(workArea.Left + workArea.Width / 2, result.Left + result.Width / 2, 6);
        Assert.Equal(workArea.Top + workArea.Height / 2, result.Top + result.Height / 2, 6);
    }

    [Fact]
    public void DisplayChange_PreservesUserPlacementWhenItFits()
    {
        var requested = new Rect(-1700, 100, 900, 600);
        var result = WindowPlacementService.FitBounds(new Rect(-1920, 0, 1920, 1040), requested, center: false);
        Assert.Equal(requested, result);
    }

    [Fact]
    public void DisplayChange_BringsAnOffscreenWindowBackIntoTheWorkArea()
    {
        var result = WindowPlacementService.FitBounds(new Rect(0, 40, 1280, 680),
            new Rect(1800, -200, 900, 600), center: false);

        Assert.Equal(new Rect(380, 40, 900, 600), result);
    }
}
