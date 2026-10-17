using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ActivityMonitor.Viewer.Services;
using ActivityMonitor.Viewer.ViewModels;

namespace ActivityMonitor.Viewer;

/// <summary>
/// نافذة التطبيق الرئيسية (Main Application Window).
/// تتولى عرض الواجهة التفاعلية، والتصميم المتجاوب التكيفي (Responsive Layout)، والتحكم في مشغل الفيديو
/// MediaElement وشريط التقديم والترجيع، ودعم وضع ملء الشاشة الكامل (Full Screen).
/// </summary>
public partial class MainWindow : Window
{
    // نقطة التحول لعرض الشاشات المدمجة/الصغيرة بالبكسل (Breakpoint)
    private const double CompactLayoutBreakpoint = 1100;

    // مؤقت مزامنة موضع تشغيل الفيديو مع شريط التمرير (Seek Slider)
    private readonly DispatcherTimer _playbackTimer = new();

    // مؤشرات حالة السحب والتخطيط وملء الشاشة
    private bool _isUserDraggingSlider;
    private bool _isCompactLayout;
    private bool _isShortLayout;
    private bool _isStackedLayout;
    private bool _layoutInitialized;
    private bool _isFullScreen;

    // حفظ خصائص النافذة السابقة لاستعادتها بعد الخروج من ملء الشاشة
    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;
    private ResizeMode _previousResizeMode;
    private bool _previousTopmost;
    private Rect _previousBounds;
    private HwndSource? _windowSource;

    // مرجع نموذج العرض الرئيسي
    private MainViewModel? _viewModel;

    /// <summary>
    /// تهيئة النافذة الرئيسية وعناصر واجهة المستخدم وربط معالجات الأحداث والمؤقتات.
    /// </summary>
    public MainWindow()
    {
        // تهيئة مكونات XAML
        InitializeComponent();

        // ربط أحداث دورة حياة النافذة والحجم
        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        MainContentScrollViewer.SizeChanged += (_, _) => UpdateContentHeight();
        PlayerPane.SizeChanged += (_, _) => UpdatePlayerControlsLayout();
        PreviewKeyDown += OnWindowPreviewKeyDown;

        // تهيئة مؤقت تشغيل الوسائط لتحديث شريط الوقت كل ربع ثانية (250 ميلي ثانية)
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(250);
        _playbackTimer.Tick += OnPlaybackTimerTick;

        // ربط أحداث مشغل الوسائط MediaElement
        PlayerMediaElement.MediaOpened += OnMediaOpened;
        PlayerMediaElement.MediaEnded += OnMediaEnded;
        PlayerMediaElement.MediaFailed += OnMediaFailed;

        // ربط أحداث الفأرة لشريط التقديم والتأخير لتجنب التضارب أثناء السحب
        SeekSlider.PreviewMouseDown += OnSliderMouseDown;
        SeekSlider.PreviewMouseUp += OnSliderMouseUp;
        SeekSlider.ValueChanged += OnSliderValueChanged;

        // ربط زر الشاشة الكاملة والنقر المزدوج على سطح الفيديو
        FullScreenButton.Click += (_, _) => ToggleFullScreen();
        VideoSurface.PreviewMouseLeftButtonDown += OnVideoSurfaceMouseLeftButtonDown;

        // ربط شريط مستوى الصوت بعنصر MediaElement
        VolumeSlider.ValueChanged += (_, _) =>
        {
            PlayerMediaElement.Volume = VolumeSlider.Value;
        };
        PlayerMediaElement.Volume = VolumeSlider.Value;
    }

    /// <summary>
    /// معالج حدث اكتمال تحميل النافذة لضبط الأبعاد وربط أحداث المشغل بنموذج العرض.
    /// </summary>
    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // ملائمة حجم النافذة مع مساحة عمل الشاشة الحالية
        WindowPlacementService.FitWindow(this, center: true);
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(OnWindowMessage);

        // تطبيق التخطيط المتجاوب الأولي
        UpdateResponsiveLayout(force: true);

        // جلب نموذج العرض والاشتراك في أحداث تشغيل الوسائط
        _viewModel = DataContext as MainViewModel;
        if (_viewModel != null)
        {
            _viewModel.RequestMediaPlay += OnRequestMediaPlay;
            _viewModel.RequestMediaPause += OnRequestMediaPause;
            _viewModel.RequestMediaResume += OnRequestMediaResume;
            _viewModel.RequestMediaStop += OnRequestMediaStop;
            _viewModel.RequestMediaSeek += OnRequestMediaSeek;
        }
    }

    /// <summary>
    /// التأكد من عدم تجاوز أبعاد النافذة لمساحة الشاشة المتاحة (WorkArea).
    /// </summary>
    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Refit after Windows has applied a DPI, display, or taskbar change.
        if (message is 0x02E0 or 0x007E or 0x001A or 0x0232)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!_isFullScreen && IsLoaded)
                {
                    WindowPlacementService.FitWindow(this, center: false);
                    UpdateResponsiveLayout(force: true);
                }
            }));
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// معالج حدث إغلاق النافذة لتنظيف الموارد وإلغاء الاشتراكات في الأحداث وإيقاف الجلسات الصوتية.
    /// </summary>
    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _windowSource?.RemoveHook(OnWindowMessage);
        // إلغاء الاشتراك من أحداث نموذج العرض
        if (_viewModel != null)
        {
            _viewModel.RequestMediaPlay -= OnRequestMediaPlay;
            _viewModel.RequestMediaPause -= OnRequestMediaPause;
            _viewModel.RequestMediaResume -= OnRequestMediaResume;
            _viewModel.RequestMediaStop -= OnRequestMediaStop;
            _viewModel.RequestMediaSeek -= OnRequestMediaSeek;
            _viewModel.Dispose();
        }

        // إيقاف مؤقت التشغيل وإغلاق مشغل الوسائط
        _playbackTimer.Stop();
        PlayerMediaElement.Stop();
        PlayerMediaElement.Close();
    }

    /// <summary>
    /// تحديث وتكييف تخطيط عناصر واجهة المستخدم ديناميكياً استناداً لعرض وارتفاع النافذة الفعلي (Adaptive Responsive Layout).
    /// </summary>
    /// <param name="force">إجبار إعادة بناء التخطيط حتى وإن لم تتغير نقاط التوقف.</param>
    private void UpdateResponsiveLayout(bool force = false)
    {
        // تجاوز التحديث أثناء وضع ملء الشاشة أو إذا كانت النافذة غير مهيأة
        if (_isFullScreen || ActualWidth <= 0)
        {
            return;
        }

        var compact = ActualWidth < CompactLayoutBreakpoint;
        var shortLayout = ActualHeight < 700;
        var stacked = ActualWidth < 900;

        // تجنب المعالجة المتكررة إذا لم تتغير حالة التخطيط
        if (!force &&
            _layoutInitialized &&
            compact == _isCompactLayout &&
            shortLayout == _isShortLayout &&
            stacked == _isStackedLayout)
        {
            return;
        }

        _layoutInitialized = true;
        _isCompactLayout = compact;
        _isShortLayout = shortLayout;
        _isStackedLayout = stacked;

        // تعديل الهوامش والمسافات
        DashboardRoot.Margin = compact ? new Thickness(10) : new Thickness(18);
        HeaderBorder.Padding = compact ? new Thickness(12, 10, 12, 10) : new Thickness(16, 12, 16, 12);
        HeaderBorder.Margin = compact ? new Thickness(0, 0, 0, 10) : new Thickness(0, 0, 0, 14);
        KpiPanel.Columns = compact ? 3 : 6;
        KpiPanel.Margin = compact ? new Thickness(0, 0, 0, 10) : new Thickness(0, 0, 0, 14);
        KpiPanel.Visibility = shortLayout ? Visibility.Collapsed : Visibility.Visible;
        FooterVersionText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SettingsDialog.Margin = compact ? new Thickness(12) : new Thickness(24);
        SettingsDialog.Padding = compact ? new Thickness(14) : new Thickness(24);

        if (compact)
        {
            // ==========================================
            // التخطيط المدمج (الشاشات الصغيرة / النوافذ الضيقة)
            // ترتيب رأسي مكدس لقائمة الأنشطة ومشغل الفيديو
            // ==========================================
            HeaderLayout.RowDefinitions[2].Height = GridLength.Auto;

            Grid.SetRow(AppTitlePanel, 0);
            Grid.SetColumn(AppTitlePanel, 0);
            Grid.SetColumnSpan(AppTitlePanel, 3);

            Grid.SetRow(DateControlsPanel, 1);
            Grid.SetColumn(DateControlsPanel, 0);
            Grid.SetColumnSpan(DateControlsPanel, 3);
            DateControlsPanel.Margin = new Thickness(0, 10, 0, 0);

            Grid.SetRow(ActionControlsPanel, 2);
            ActionControlsPanel.Margin = new Thickness(0, 10, 0, 0);

            // إعادة توجيه حقول الفلترة
            FilterGrid.RowDefinitions[1].Height = GridLength.Auto;
            Grid.SetRow(SearchTextBox, 0);
            Grid.SetColumn(SearchTextBox, 0);
            Grid.SetColumnSpan(SearchTextBox, 4);
            SearchTextBox.Margin = new Thickness(0, 0, 0, 8);

            Grid.SetRow(ActivityFilterComboBox, 1);
            Grid.SetColumn(ActivityFilterComboBox, 0);
            Grid.SetColumnSpan(ActivityFilterComboBox, 2);
            ActivityFilterComboBox.Margin = new Thickness(0);
            Grid.SetRow(FilteredCountText, 1);
            Grid.SetColumn(FilteredCountText, 2);
            Grid.SetRow(ClearFiltersButton, 1);
            Grid.SetColumn(ClearFiltersButton, 3);
        }
        else
        {
            // ==========================================
            // التخطيط العريض (الشاشات الكبيرة / الوضع الطبيعي)
            // ترتيب أفقي: قائمة الأنشطة على اليمين ومشغل الفيديو على اليسار
            // ==========================================
            HeaderLayout.RowDefinitions[2].Height = new GridLength(0);

            Grid.SetRow(AppTitlePanel, 0);
            Grid.SetColumn(AppTitlePanel, 0);
            Grid.SetColumnSpan(AppTitlePanel, 1);

            Grid.SetRow(DateControlsPanel, 0);
            Grid.SetColumn(DateControlsPanel, 1);
            Grid.SetColumnSpan(DateControlsPanel, 1);
            DateControlsPanel.Margin = new Thickness(0);

            Grid.SetRow(ActionControlsPanel, 1);
            ActionControlsPanel.Margin = new Thickness(0, 12, 0, 0);

            FilterGrid.RowDefinitions[1].Height = new GridLength(0);
            Grid.SetRow(SearchTextBox, 0);
            Grid.SetColumn(SearchTextBox, 0);
            Grid.SetColumnSpan(SearchTextBox, 1);
            SearchTextBox.Margin = new Thickness(0);

            Grid.SetRow(ActivityFilterComboBox, 0);
            Grid.SetColumn(ActivityFilterComboBox, 1);
            Grid.SetColumnSpan(ActivityFilterComboBox, 1);
            ActivityFilterComboBox.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetRow(FilteredCountText, 0);
            Grid.SetColumn(FilteredCountText, 2);
            Grid.SetRow(ClearFiltersButton, 0);
            Grid.SetColumn(ClearFiltersButton, 3);
        }

        // Keep both panes side by side on laptops; stack only on narrow windows.
        MainContentScrollViewer.VerticalScrollBarVisibility = stacked
            ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        MainContentGrid.ColumnDefinitions[0].Width = new GridLength(stacked ? 1 : 1.2, GridUnitType.Star);
        MainContentGrid.ColumnDefinitions[1].Width = new GridLength(stacked ? 0 : 10);
        MainContentGrid.ColumnDefinitions[2].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        MainContentGrid.RowDefinitions[0].Height = new GridLength(stacked ? 1.15 : 1, GridUnitType.Star);
        MainContentGrid.RowDefinitions[1].Height = new GridLength(stacked ? 10 : 0);
        MainContentGrid.RowDefinitions[2].Height = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetRow(ActivitiesPane, 0);
        Grid.SetColumn(ActivitiesPane, 0);
        Grid.SetRow(MainGridSplitter, stacked ? 1 : 0);
        Grid.SetColumn(MainGridSplitter, stacked ? 0 : 1);
        Grid.SetRow(PlayerPane, stacked ? 2 : 0);
        Grid.SetColumn(PlayerPane, stacked ? 0 : 2);
        MainGridSplitter.ResizeDirection = stacked ? GridResizeDirection.Rows : GridResizeDirection.Columns;
        MainGridSplitter.ResizeBehavior = GridResizeBehavior.PreviousAndNext;
        MainGridSplitter.Cursor = stacked ? Cursors.SizeNS : Cursors.SizeWE;
        MainGridSplitter.Width = stacked ? double.NaN : 10;
        MainGridSplitter.Height = stacked ? 10 : double.NaN;
        MainGridSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
        MainGridSplitter.VerticalAlignment = VerticalAlignment.Stretch;
        UpdateContentHeight();
        UpdatePlayerControlsLayout();
    }

    private void UpdateContentHeight()
    {
        if (_isFullScreen) return;
        MainContentGrid.Height = ActualWidth < 900
            ? Math.Max(600, MainContentScrollViewer.ActualHeight) : double.NaN;
    }

    private void UpdatePlayerControlsLayout()
    {
        var narrow = PlayerPane.ActualWidth < 520;
        Grid.SetColumnSpan(PlaybackButtonsPanel, narrow ? 2 : 1);
        Grid.SetColumn(FullScreenButton, narrow ? 2 : 1);
        Grid.SetRow(VolumePanel, narrow ? 1 : 0);
        Grid.SetColumn(VolumePanel, narrow ? 0 : 2);
        Grid.SetColumnSpan(VolumePanel, narrow ? 3 : 1);
        VolumePanel.HorizontalAlignment = HorizontalAlignment.Right;
        VolumePanel.Margin = narrow ? new Thickness(0, 8, 0, 0) : new Thickness(0);
        Grid.SetColumnSpan(NowPlayingTitlePanel, narrow ? 2 : 1);
        Grid.SetRow(RecordingPartsPanel, narrow ? 1 : 0);
        Grid.SetColumn(RecordingPartsPanel, narrow ? 0 : 1);
        Grid.SetColumnSpan(RecordingPartsPanel, narrow ? 2 : 1);
        RecordingPartsPanel.Margin = narrow ? new Thickness(0, 6, 0, 0) : new Thickness(12, 0, 0, 0);
    }

    /// <summary>
    /// تبديل وضع ملء الشاشة الكامل لمشغل الفيديو.
    /// </summary>
    private void ToggleFullScreen()
    {
        if (_isFullScreen)
        {
            ExitFullScreen();
        }
        else
        {
            EnterFullScreen();
        }
    }

    /// <summary>
    /// الانتقال إلى وضع ملء الشاشة بنقل لوحة المشغل (PlayerPane) إلى طبقة التراكب العليا (Overlay).
    /// </summary>
    private void EnterFullScreen()
    {
        if (_isFullScreen)
        {
            return;
        }

        // حفظ خصائص وحالة النافذة الحالية
        _previousWindowStyle = WindowStyle;
        _previousWindowState = WindowState;
        _previousResizeMode = ResizeMode;
        _previousTopmost = Topmost;
        _previousBounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _isFullScreen = true;

        // فصل لوحة المشغل من الشبكة الرئيسية وإضافتها لطبقة التراكب
        MainContentGrid.Children.Remove(PlayerPane);
        FullScreenOverlay.Children.Add(PlayerPane);
        Grid.SetRow(PlayerPane, 0);
        Grid.SetColumn(PlayerPane, 0);

        // تعديل خواص المشغل ليناسب شاشة العرض الكاملة
        PlayerPane.Padding = new Thickness(0);
        PlayerPane.BorderThickness = new Thickness(0);
        PlayerPane.CornerRadius = new CornerRadius(0);
        PlayerPane.Background = System.Windows.Media.Brushes.Black;
        NowPlayingHeader.Visibility = Visibility.Collapsed;
        VideoSurface.Margin = new Thickness(0);
        VideoSurface.CornerRadius = new CornerRadius(0);
        PlayerControlsBorder.Margin = new Thickness(12);
        FullScreenButton.Content = "⛶ خروج";
        FullScreenButton.Width = double.NaN;
        FullScreenButton.ToolTip = "الخروج من ملء الشاشة (Esc أو F11)";
        FullScreenOverlay.Visibility = Visibility.Visible;

        // تكبير النافذة وإخفاء الحواف والشريط العلوي لنظام ويندوز
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        WindowState = WindowState.Maximized;
        FullScreenButton.Focus();
    }

    /// <summary>
    /// الخروج من وضع ملء الشاشة واستعادة مظهر وحجم النافذة الأصلي.
    /// </summary>
    private void ExitFullScreen()
    {
        if (!_isFullScreen)
        {
            return;
        }

        // إرجاع لوحة المشغل إلى الشبكة الرئيسية
        FullScreenOverlay.Children.Remove(PlayerPane);
        MainContentGrid.Children.Add(PlayerPane);

        // استعادة مظهر الحواف والألوان الأصلية للمشغل
        PlayerPane.Padding = new Thickness(14);
        PlayerPane.BorderThickness = new Thickness(1);
        PlayerPane.CornerRadius = new CornerRadius(10);
        PlayerPane.Background = (System.Windows.Media.Brush)FindResource("SurfaceLightBrush");
        NowPlayingHeader.Visibility = Visibility.Visible;
        VideoSurface.Margin = new Thickness(0, 0, 0, 12);
        VideoSurface.CornerRadius = new CornerRadius(8);
        PlayerControlsBorder.Margin = new Thickness(0);
        FullScreenButton.Content = "⛶";
        FullScreenButton.Width = 38;
        FullScreenButton.ToolTip = "عرض الفيديو بملء الشاشة (F11)";
        FullScreenOverlay.Visibility = Visibility.Collapsed;

        // استعادة خصائص وحجم النافذة السابق
        WindowState = WindowState.Normal;
        WindowStyle = _previousWindowStyle;
        ResizeMode = _previousResizeMode;
        Topmost = _previousTopmost;
        Left = _previousBounds.Left;
        Top = _previousBounds.Top;
        Width = _previousBounds.Width;
        Height = _previousBounds.Height;
        WindowState = _previousWindowState;

        _isFullScreen = false;
        WindowPlacementService.FitWindow(this, center: false);
        UpdateResponsiveLayout(force: true);
    }

    /// <summary>
    /// معالج ضغطات المفاتيح العالمية (F11 للتبديل، و Esc للخروج من ملء الشاشة).
    /// </summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _isFullScreen)
        {
            ExitFullScreen();
            e.Handled = true;
        }
    }

    /// <summary>
    /// معالج النقر المزدوج على سطح الفيديو لتبديل وضع ملء الشاشة بسرعة.
    /// </summary>
    private void OnVideoSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
    }

    /// <summary>
    /// معالج حدث طلب تشغيل الفيديو من نموذج العرض وتعيين مصدر الوسائط والإزاحة الزمنية.
    /// </summary>
    /// <param name="filePath">مسار ملف الفيديو على القرص.</param>
    /// <param name="offset">نقطة البداية الزمنية.</param>
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

        // إخفاء الواجهة البديلة (Placeholder)
        PlaceholderBorder.Visibility = Visibility.Collapsed;

        // تحميل مسار الوسائط في مشغل MediaElement
        var uri = new Uri(filePath, UriKind.Absolute);
        if (PlayerMediaElement.Source != uri)
        {
            PlayerMediaElement.Source = uri;
        }

        // تعيين موضع البداية وتشغيل المقطع
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

    /// <summary>
    /// معالج حدث طلب الإيقاف المؤقت للوسائط.
    /// </summary>
    private void OnRequestMediaPause()
    {
        PlayerMediaElement.Pause();
        _playbackTimer.Stop();
        PlayPauseButton.Content = "▶ تشغيل";
    }

    /// <summary>
    /// معالج حدث طلب استئناف تشغيل الوسائط.
    /// </summary>
    private void OnRequestMediaResume()
    {
        PlayerMediaElement.Play();
        _playbackTimer.Start();
        PlayPauseButton.Content = "⏸ إيقاف مؤقت";
    }

    /// <summary>
    /// معالج حدث طلب إيقاف المشغل وتصفيره بالكامل.
    /// </summary>
    private void OnRequestMediaStop()
    {
        _playbackTimer.Stop();
        PlayerMediaElement.Stop();
        PlayerMediaElement.Close();
        PlaceholderBorder.Visibility = Visibility.Visible;
        PlayPauseButton.Content = "▶ تشغيل";
        SeekSlider.Value = 0;
    }

    /// <summary>
    /// معالج حدث طلب القفز إلى وقت محدد في الفيديو.
    /// </summary>
    /// <param name="target">الهدف الزمني المطلوب.</param>
    private void OnRequestMediaSeek(TimeSpan target)
    {
        PlayerMediaElement.Position = target;
        if (_viewModel != null)
        {
            _viewModel.CurrentVideoPosition = target;
        }
    }

    /// <summary>
    /// معالج حدث فتح ملف الفيديو بنجاح وقراءة مدته الكلية وضبط الحد الأقصى لشريط التمرير.
    /// </summary>
    private void OnMediaOpened(object sender, RoutedEventArgs e)
    {
        TimeSpan duration = TimeSpan.Zero;
        // قراءة المدة الطبيعية للمقطع
        if (PlayerMediaElement.NaturalDuration.HasTimeSpan && PlayerMediaElement.NaturalDuration.TimeSpan.TotalSeconds > 0)
        {
            duration = PlayerMediaElement.NaturalDuration.TimeSpan;
        }
        else if (_viewModel?.SelectedActivity != null)
        {
            // قيمة بديلة في حال تأخر برنامج فك التشفير عن تزويد الرأس بالمدة
            duration = TimeSpan.FromSeconds(Math.Max(300, _viewModel.SelectedActivity.DurationSeconds + 60));
        }

        if (duration.TotalSeconds > 0)
        {
            SeekSlider.Maximum = duration.TotalSeconds;
            if (_viewModel != null)
            {
                _viewModel.VideoDuration = duration;
            }
        }

        // تطبيق موضع القفز المطلوب إذا كان ضمن حدود المقطع
        if (_viewModel != null && (_viewModel.RequestedSeekPosition <= duration || duration == TimeSpan.Zero))
        {
            PlayerMediaElement.Position = _viewModel.RequestedSeekPosition;
            _viewModel.CurrentVideoPosition = _viewModel.RequestedSeekPosition;
        }
    }

    /// <summary>
    /// معالج حدث انتهاء تشغيل المقطع الحالي ومحاولة الانتقال التلقائي للجزء التالي.
    /// </summary>
    private void OnMediaEnded(object sender, RoutedEventArgs e)
    {
        _playbackTimer.Stop();

        // محاولة الانتقال للجزء التالي من النشاط متعدد الأجزاء تلقائياً
        if (_viewModel?.TryPlayNextRecordingPart() == true)
        {
            return;
        }

        if (_viewModel != null)
        {
            _viewModel.IsPlaying = false;
        }
        PlayPauseButton.Content = "▶ تشغيل";
    }

    /// <summary>
    /// معالج حدث فشل تشغيل ملف الوسائط وعرض رسالة الخطأ للمستخدم.
    /// </summary>
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

    /// <summary>
    /// نبضة مؤقت تحديث موضع شريط التقديم أثناء تشغيل الفيديو بصورة مستمرة.
    /// </summary>
    private void OnPlaybackTimerTick(object? sender, EventArgs e)
    {
        // لا نحدث قيمة الشريط إذا كان المستخدم يقوم بسحبه بيده حالياً
        if (!_isUserDraggingSlider)
        {
            var pos = PlayerMediaElement.Position;
            SeekSlider.Value = pos.TotalSeconds;
            if (_viewModel != null)
            {
                _viewModel.CurrentVideoPosition = pos;
            }
        }
    }

    /// <summary>
    /// معالج بدء سحب شريط التقديم بالفأرة لمنع تحديثه التلقائي من المؤقت.
    /// </summary>
    private void OnSliderMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isUserDraggingSlider = true;
    }

    /// <summary>
    /// معالج إفلات شريط التقديم وتطبيق الموضع الزمني المختار على المشغل.
    /// </summary>
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

    /// <summary>
    /// معالج تغيير قيمة شريط التقديم لتحديث نص الوقت اللحظي أثناء السحب.
    /// </summary>
    private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUserDraggingSlider && _viewModel != null)
        {
            _viewModel.CurrentVideoPosition = TimeSpan.FromSeconds(e.NewValue);
        }
    }
}
