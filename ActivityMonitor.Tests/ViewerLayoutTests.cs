using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ActivityMonitor.Core.Options;
using ActivityMonitor.Infrastructure.Data;
using ActivityMonitor.Viewer;
using ActivityMonitor.Viewer.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ActivityMonitor.Tests;

[Collection(ActivityMonitorPathsCollection.Name)]
public class ViewerLayoutTests
{
    [Fact]
    public void Window_ResizesWithoutClippingControls_AndRestoresAfterFullScreen()
    {
        Exception? failure = null;
        var root = Path.Combine(Path.GetTempPath(), "ActivityMonitor_Layout_" + Guid.NewGuid().ToString("N"));
        ActivityMonitorPaths.SetCustomRootDirectory(root);
        var thread = new Thread(() =>
        {
            App? app = null;
            MainWindow? window = null;
            try
            {
                ActivityMonitorPaths.EnsureDirectoriesExist();
                ActivityMonitorDbContext.InitializeDatabaseAsync().GetAwaiter().GetResult();
                app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.InitializeComponent();
                window = new MainWindow { Opacity = 0, ShowInTaskbar = false };
                window.Show();
                PumpLayout(window);
                var viewModel = (MainViewModel)window.DataContext;
                // Finish the initial query while this window's Dispatcher is alive.
                var load = viewModel.LoadDateDataAsync();
                while (!load.IsCompleted) PumpLayout(window);
                load.GetAwaiter().GetResult();

                window.MinWidth = 0;
                window.MinHeight = 0;
                foreach (var size in new[] { new Size(1420, 860), new Size(1280, 680),
                    new Size(1024, 650), new Size(880, 740), new Size(680, 520), new Size(520, 400),
                    new Size(1024, 650), new Size(1420, 860) })
                {
                    window.Width = size.Width;
                    window.Height = size.Height;
                    PumpLayout(window);
                    var controls = Find<Border>(window, "PlayerControlsBorder");
                    var video = Find<Border>(window, "VideoSurface");
                    Assert.True(video.ActualHeight > 0, $"Video has no height at {size}.");
                    foreach (var name in new[] { "PlaybackButtonsPanel", "FullScreenButton", "VolumePanel" })
                    {
                        AssertFits(Find<FrameworkElement>(window, name), controls, size);
                    }
                    var scroll = Find<ScrollViewer>(window, "MainContentScrollViewer");
                    Assert.True(scroll.ActualHeight > 0, $"Content has no height at {size}.");
                    Assert.Equal(size.Width < 900 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                        scroll.VerticalScrollBarVisibility);
                    AssertFits(Find<FrameworkElement>(window, "ActionControlsPanel"),
                        Find<Border>(window, "HeaderBorder"), size);
                    viewModel.IsSettingsOpen = true;
                    PumpLayout(window);
                    var dialog = Find<Border>(window, "SettingsDialog");
                    AssertFits(dialog, Find<Grid>(window, "RootLayout"), size);
                    AssertFits(Find<Button>(window, "SettingsSaveButton"), dialog, size);
                    AssertFits(Find<Button>(window, "SettingsCancelButton"), dialog, size);
                    viewModel.IsSettingsOpen = false;
                }

                var before = new Rect(window.Left, window.Top, window.Width, window.Height);
                var fullScreen = Find<Button>(window, "FullScreenButton");
                fullScreen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpLayout(window);
                Assert.Equal(WindowState.Maximized, window.WindowState);
                fullScreen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpLayout(window);
                Assert.Equal(WindowState.Normal, window.WindowState);
                Assert.Equal(before.Width, window.Width, 0);
                Assert.Equal(before.Height, window.Height, 0);
                Assert.Equal(before.Left, window.Left, 0);
                Assert.Equal(before.Top, window.Top, 0);
                window.Close();
                window = null;
                var message = viewModel.StatusMessage;
                viewModel.CheckStatuses();
                Assert.Equal(message, viewModel.StatusMessage);
                Assert.True(viewModel.LoadDateDataAsync().IsCompletedSuccessfully);
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                window?.Close();
                app?.Shutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "WPF layout validation timed out.");
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
        finally
        {
            ActivityMonitorPaths.SetCustomRootDirectory(null);
            SqliteConnection.ClearAllPools();
            if (!thread.IsAlive && Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static T Find<T>(MainWindow window, string name) where T : FrameworkElement =>
        Assert.IsAssignableFrom<T>(window.FindName(name));

    private static void AssertFits(FrameworkElement element, FrameworkElement parent, Size size)
    {
        var bounds = element.TransformToAncestor(parent).TransformBounds(new Rect(element.RenderSize));
        Assert.True(bounds.Left >= -1 && bounds.Top >= -1 &&
                    bounds.Right <= parent.ActualWidth + 1 && bounds.Bottom <= parent.ActualHeight + 1,
            $"{element.Name} is clipped at {size}: {bounds}, container {parent.RenderSize}.");
    }

    private static void PumpLayout(Window window)
    {
        window.UpdateLayout();
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        window.UpdateLayout();
    }
}
