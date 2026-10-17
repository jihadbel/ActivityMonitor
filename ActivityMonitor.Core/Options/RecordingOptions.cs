namespace ActivityMonitor.Core.Options;

/// <summary>
/// خيارات وإعدادات تسجيل الشاشة بالفيديو وإعدادات الصوت ومعدل الإطارات وضبط المساحة.
/// </summary>
public class RecordingOptions
{
    /// <summary>
    /// اسم قسم الإعدادات في ملف التكوين appsettings.json.
    /// </summary>
    public const string SectionName = "Recording";

    /// <summary>
    /// تفعيل أو تعطيل ميزة تسجيل شاشة الفيديو.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// اقتصار تسجيل الفيديو على الفترات التي يكون فيها المستخدم نشطاً فقط (إيقاف التسجيل في فترات الخمول).
    /// </summary>
    public bool RecordOnlyWhenUserActive { get; set; } = true;

    /// <summary>
    /// مدة مقطع الفيديو الواحد بالدقائق قبل التبديل لمقطع جديد (الافتراضي: 10 دقائق).
    /// </summary>
    public int SegmentMinutes { get; set; } = 10;

    /// <summary>
    /// معدل الإطارات في الثانية لتسجيل الفيديو (Frames Per Second - الافتراضي: 15 إطاراً).
    /// </summary>
    public int FrameRate { get; set; } = 15;

    /// <summary>
    /// معدل نقل بيانات الفيديو بالكيلوبت في الثانية (Bitrate - الافتراضي: 1200 Kbps).
    /// </summary>
    public int VideoBitrateKbps { get; set; } = 1200;

    /// <summary>
    /// جودة الفيديو المضغوط (من 1 إلى 100 - الافتراضي: 60).
    /// </summary>
    public int VideoQuality { get; set; } = 60;

    /// <summary>
    /// عدد أيام الاحتفاظ بملفات تسجيل الفيديو قبل حذفها تلقائياً (الافتراضي: 14 يوماً).
    /// </summary>
    public int RetentionDays { get; set; } = 14;

    /// <summary>
    /// الحد الأقصى للسعة التخزينية المسموح باستهلاكها لمقاطع الفيديو بالجيجابايت (الافتراضي: 50 GB).
    /// </summary>
    public long MaxStorageSizeGB { get; set; } = 50;

    /// <summary>
    /// الحد الأدنى للمساحة الحرة المطلوبة على القرص للاستمرار في التسجيل بالجيجابايت (الافتراضي: 2 GB).
    /// </summary>
    public long MinimumFreeDiskSpaceGB { get; set; } = 2;

    /// <summary>
    /// تفعيل تسجيل الصوت مع الفيديو.
    /// </summary>
    public bool RecordAudio { get; set; } = false;

    /// <summary>
    /// تفعيل تسجيل صوت الميكروفون المباشر.
    /// </summary>
    public bool RecordMicrophone { get; set; } = false;

    /// <summary>
    /// تفعيل تسجيل صوت النظام الداخلي (مخرجات السماعات).
    /// </summary>
    public bool RecordSystemAudio { get; set; } = false;

    /// <summary>
    /// معرّف أو اسم جهاز الميكروفون المختار للتسجيل.
    /// </summary>
    public string? SelectedMicrophoneDeviceName { get; set; } = null;

    /// <summary>
    /// الاسم الودي المعروض لجهاز الميكروفون في الواجهة.
    /// </summary>
    public string? SelectedMicrophoneFriendlyName { get; set; } = null;

    /// <summary>
    /// مستوى صوت الميكروفون المسجل (من 0.0 إلى 1.0).
    /// </summary>
    public float MicrophoneVolume { get; set; } = 1.0f;

    /// <summary>
    /// معدل نقل بيانات الصوت المسجل بالكيلوبت في الثانية (الافتراضي: 128 Kbps).
    /// </summary>
    public int AudioBitrateKbps { get; set; } = 128;

    /// <summary>
    /// تمكين صيغة MP4 المجزأة (Fragmented MP4) لضمان قابلية تشغيل الفيديو حتى في حال انقطاع التيار الكهربائي أو الإغلاق المفاجئ.
    /// </summary>
    public bool IsFragmentedMp4Enabled { get; set; } = true;
}
