using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ActivityMonitor.Viewer.ViewModels;

namespace ActivityMonitor.Viewer;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _playbackTimer = new();
    private bool _isUserDraggingSlider;
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;

        _playbackTimer.Interval = TimeSpan.FromMilliseconds(250);
        _playbackTimer.Tick += OnPlaybackTimerTick;

        PlayerMediaElement.MediaOpened += OnMediaOpened;
        PlayerMediaElement.MediaEnded += OnMediaEnded;
        PlayerMediaElement.MediaFailed += OnMediaFailed;

        SeekSlider.PreviewMouseDown += OnSliderMouseDown;
        SeekSlider.PreviewMouseUp += OnSliderMouseUp;
        SeekSlider.ValueChanged += OnSliderValueChanged;

        VolumeSlider.ValueChanged += (_, _) =>
        {
            PlayerMediaElement.Volume = VolumeSlider.Value;
        };
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel = DataContext as MainViewModel;
        if (_viewModel != null)
        {
            _viewModel.RequestMediaPlay += OnRequestMediaPlay;
            _viewModel.RequestMediaPause += OnRequestMediaPause;
            _viewModel.RequestMediaResume += OnRequestMediaResume;
            _viewModel.RequestMediaSeek += OnRequestMediaSeek;
        }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _playbackTimer.Stop();
        PlayerMediaElement.Stop();
        PlayerMediaElement.Close();
    }

    private void OnRequestMediaPlay(string filePath, TimeSpan offset)
    {
        if (!File.Exists(filePath))
        {
            if (_viewModel != null)
            {
                _viewModel.StatusMessage = $"ملف التسجيل غير موجود: {filePath}";
            }
            return;
        }

        PlaceholderBorder.Visibility = Visibility.Collapsed;

        // Load media source
        var uri = new Uri(filePath, UriKind.Absolute);
        if (PlayerMediaElement.Source != uri)
        {
            PlayerMediaElement.Source = uri;
        }

        PlayerMediaElement.Position = offset;
        PlayerMediaElement.Play();
        _playbackTimer.Start();

        PlayPauseButton.Content = "⏸ إيقاف مؤقت";
        if (_viewModel != null)
        {
            _viewModel.IsPlaying = true;
            _viewModel.CurrentVideoPosition = offset;
        }
    }

    private void OnRequestMediaPause()
    {
        PlayerMediaElement.Pause();
        _playbackTimer.Stop();
        PlayPauseButton.Content = "▶ تشغيل";
    }

    private void OnRequestMediaResume()
    {
        PlayerMediaElement.Play();
        _playbackTimer.Start();
        PlayPauseButton.Content = "⏸ إيقاف مؤقت";
    }

    private void OnRequestMediaSeek(TimeSpan target)
    {
        PlayerMediaElement.Position = target;
        if (_viewModel != null)
        {
            _viewModel.CurrentVideoPosition = target;
        }
    }

    private void OnMediaOpened(object sender, RoutedEventArgs e)
    {
        if (PlayerMediaElement.NaturalDuration.HasTimeSpan)
        {
            var duration = PlayerMediaElement.NaturalDuration.TimeSpan;
            SeekSlider.Maximum = duration.TotalSeconds;

            if (_viewModel != null)
            {
                _viewModel.VideoDuration = duration;
                if (_viewModel.RequestedSeekPosition <= duration)
                {
                    PlayerMediaElement.Position = _viewModel.RequestedSeekPosition;
                    _viewModel.CurrentVideoPosition = _viewModel.RequestedSeekPosition;
                }
            }
        }
    }

    private void OnMediaEnded(object sender, RoutedEventArgs e)
    {
        _playbackTimer.Stop();
        if (_viewModel != null)
        {
            _viewModel.IsPlaying = false;
        }
        PlayPauseButton.Content = "▶ تشغيل";
    }

    private void OnMediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        _playbackTimer.Stop();
        if (_viewModel != null)
        {
            _viewModel.StatusMessage = $"خطأ أثناء تشغيل المقطع: {e.ErrorException?.Message}";
            _viewModel.IsPlaying = false;
        }
        PlayPauseButton.Content = "▶ تشغيل";
    }

    private void OnPlaybackTimerTick(object? sender, EventArgs e)
    {
        if (!_isUserDraggingSlider && PlayerMediaElement.NaturalDuration.HasTimeSpan)
        {
            var pos = PlayerMediaElement.Position;
            SeekSlider.Value = pos.TotalSeconds;
            if (_viewModel != null)
            {
                _viewModel.CurrentVideoPosition = pos;
            }
        }
    }

    private void OnSliderMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isUserDraggingSlider = true;
    }

    private void OnSliderMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isUserDraggingSlider = false;
        var target = TimeSpan.FromSeconds(SeekSlider.Value);
        PlayerMediaElement.Position = target;
        if (_viewModel != null)
        {
            _viewModel.CurrentVideoPosition = target;
        }
    }

    private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUserDraggingSlider && _viewModel != null)
        {
            _viewModel.CurrentVideoPosition = TimeSpan.FromSeconds(e.NewValue);
        }
    }
}
