using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ActivityMonitor.Core.Options;

namespace ActivityMonitor.Viewer.Services;

/// <summary>
/// نموذج بيانات إعدادات التطبيق (AppSettings Model) يجمع كافة خيارات التسجيل والتتبع وبدء التشغيل التلقائي مع النظام.
/// </summary>
public class AppSettingsModel
{
    // ==========================================
    // إعدادات تسجيل الشاشة والصوت (Recording)
    // ==========================================

    /// <summary>
    /// هل تسجيل الشاشة بالفيديو مفعل.
    /// </summary>
    public bool ScreenRecordingEnabled { get; set; } = true;

    /// <summary>
    /// هل يتم حصر التسجيل فقط أثناء وجود تفاعل وحركة للمستخدم (تجاوز فترات الخمول).
    /// </summary>
    public bool RecordOnlyWhenUserActive { get; set; } = true;

    /// <summary>
    /// هل تسجيل إدخال الميكروفون الصوتي مفعل.
    /// </summary>
    public bool RecordMicrophone { get; set; } = false;

    /// <summary>
    /// هل تسجيل أصوات النظام الداخلية (Loopback Audio) مفعل.
    /// </summary>
    public bool RecordSystemAudio { get; set; } = false;

    /// <summary>
    /// المعرّف التقني الفريد لجهاز الميكروفون المختار (WASAPI Device ID).
    /// </summary>
    public string? SelectedMicrophoneDeviceName { get; set; } = null;

    /// <summary>
    /// الاسم الودي المعروض للميكروفون المختار في واجهة المستخدم.
    /// </summary>
    public string? SelectedMicrophoneFriendlyName { get; set; } = null;

    /// <summary>
    /// معامل مضاعفة مستوى صوت الميكروفون (من 0.0 إلى 2.0 مثلاً).
    /// </summary>
    public float MicrophoneVolume { get; set; } = 1.0f;

    /// <summary>
    /// هل يتم استخدام نمط MP4 المجزأ (Fragmented MP4) لضمان سلامة الملف في حال الانقطاع المفاجئ.
    /// </summary>
    public bool IsFragmentedMp4Enabled { get; set; } = true;

    /// <summary>
    /// مدة كل مقطع فيديو بالدقائق قبل التدوير وبدء ملف جديد.
    /// </summary>
    public int SegmentMinutes { get; set; } = 10;

    /// <summary>
    /// معدل الإطارات في الثانية لتسجيل الفيديو (FPS).
    /// </summary>
    public int FrameRate { get; set; } = 15;

    /// <summary>
    /// جودة ضغط الفيديو (CRF / Quality Scale).
    /// </summary>
    public int VideoQuality { get; set; } = 60;

    /// <summary>
    /// عدد أيام الاحتفاظ بالتسجيلات والأنشطة قبل الحذف التلقائي لتوفير المساحة.
    /// </summary>
    public int RetentionDays { get; set; } = 14;
    public int ActivityRetentionDays { get; set; } = 90;
    public int ReportRetentionDays { get; set; } = 90;
    public int LogRetentionDays { get; set; } = 30;

    // ==========================================
    // إعدادات تتبع الأنشطة (Activity Tracking)
    // ==========================================

    /// <summary>
    /// هل تتبع النوافذ النشطة والتطبيقات مفعل.
    /// </summary>
    public bool ActivityTrackingEnabled { get; set; } = true;

    /// <summary>
    /// عتبة الخمول بالدقائق التي بعدها يُعتبر المستخدم غير نشط (Idle).
    /// </summary>
    public int IdleThresholdMinutes { get; set; } = 5;

    // ==========================================
    // إعدادات بدء التشغيل التلقائي مع ويندوز
    // ==========================================

    /// <summary>
    /// تشغيل خدمة ويندوز الأساسية (ActivityMonitor.Service) تلقائياً عند إقلاع النظام.
    /// </summary>
    public bool StartServiceWithWindows { get; set; } = true;

    /// <summary>
    /// تشغيل عميل الجلسة (ActivityMonitor.Agent) تلقائياً عند تسجيل دخول المستخدم.
    /// </summary>
    public bool StartAgentAtLogon { get; set; } = true;
}

/// <summary>
/// خدمة إدارة وتخزين إعدادات التطبيق المشتركة بين الخدمة والعميل والعارض عبر ملف JSON المركزي.
/// </summary>
public class SettingsService
{
    // المسار الموحد لملف إعدادات النظام المشترك في ProgramData
    private static string SharedConfigPath => ActivityMonitorPaths.SettingsPath;

    // مدير خدمات ويندوز لضبط خصائص الإقلاع التلقائي للخدمة والعميل
    private readonly WindowsServiceManager _serviceManager;

    /// <summary>
    /// تهيئة خدمة الإعدادات وربطها بمدير خدمات ويندوز.
    /// </summary>
    /// <param name="serviceManager">نسخة مدير خدمات ويندوز.</param>
    public SettingsService(WindowsServiceManager serviceManager)
    {
        // حفظ مرجع مدير الخدمات
        _serviceManager = serviceManager;
    }

    /// <summary>
    /// قراءة الإعدادات الحالية من ملف JSON المشترك وتحديد حالة بدء التشغيل مع ويندوز.
    /// </summary>
    /// <returns>كائن نموذج الإعدادات محملاً بالقيم المحفوظة أو الافتراضية.</returns>
    public async Task<AppSettingsModel> LoadSettingsAsync()
    {
        // إنشاء نموذج افتراضي للقيم
        var model = new AppSettingsModel();

        try
        {
            // التحقق من وجود ملف الإعدادات المشترك
            var loadPath = File.Exists(SharedConfigPath) ? SharedConfigPath : Path.Combine(ActivityMonitorPaths.SharedRootDirectory, "appsettings.json");
            if (File.Exists(loadPath))
            {
                var json = await File.ReadAllTextAsync(loadPath);

                // تحليل بنية JSON
                var doc = JsonNode.Parse(json);
                if (doc != null)
                {
                    // 1. قراءة قسم إعدادات التسجيل (Recording)
                    var rec = doc["Recording"];
                    if (rec != null)
                    {
                        model.ScreenRecordingEnabled = rec["Enabled"]?.GetValue<bool>() ?? model.ScreenRecordingEnabled;
                        model.RecordOnlyWhenUserActive = rec["RecordOnlyWhenUserActive"]?.GetValue<bool>() ?? model.RecordOnlyWhenUserActive;
                        model.RecordMicrophone = rec["RecordMicrophone"]?.GetValue<bool>() ?? model.RecordMicrophone;
                        model.RecordSystemAudio = rec["RecordSystemAudio"]?.GetValue<bool>() ?? model.RecordSystemAudio;
                        model.SelectedMicrophoneDeviceName = rec["SelectedMicrophoneDeviceName"]?.GetValue<string>() ?? model.SelectedMicrophoneDeviceName;
                        model.SelectedMicrophoneFriendlyName = rec["SelectedMicrophoneFriendlyName"]?.GetValue<string>() ?? model.SelectedMicrophoneFriendlyName;
                        model.MicrophoneVolume = rec["MicrophoneVolume"]?.GetValue<float>() ?? model.MicrophoneVolume;
                        model.IsFragmentedMp4Enabled = rec["IsFragmentedMp4Enabled"]?.GetValue<bool>() ?? model.IsFragmentedMp4Enabled;
                        model.SegmentMinutes = rec["SegmentMinutes"]?.GetValue<int>() ?? model.SegmentMinutes;
                        model.FrameRate = rec["FrameRate"]?.GetValue<int>() ?? model.FrameRate;
                        model.VideoQuality = rec["VideoQuality"]?.GetValue<int>() ?? model.VideoQuality;
                        model.RetentionDays = rec["RetentionDays"]?.GetValue<int>() ?? model.RetentionDays;
                    }

                    var storage = doc["Storage"];
                    model.RetentionDays = storage?["RecordingRetentionDays"]?.GetValue<int>() ?? model.RetentionDays;
                    model.ActivityRetentionDays = storage?["ActivityRetentionDays"]?.GetValue<int>() ?? model.ActivityRetentionDays;
                    model.ReportRetentionDays = storage?["ReportRetentionDays"]?.GetValue<int>() ?? model.ReportRetentionDays;
                    model.LogRetentionDays = storage?["LogRetentionDays"]?.GetValue<int>() ?? model.LogRetentionDays;

                    // 2. قراءة قسم إعدادات تتبع الأنشطة (ActivityTracking)
                    var tracking = doc["ActivityTracking"];
                    if (tracking != null)
                    {
                        model.ActivityTrackingEnabled = tracking["Enabled"]?.GetValue<bool>() ?? model.ActivityTrackingEnabled;
                        model.IdleThresholdMinutes = tracking["IdleThresholdMinutes"]?.GetValue<int>() ?? model.IdleThresholdMinutes;
                        if (tracking["StartAgentAtLogon"] != null)
                        {
                            model.StartAgentAtLogon = tracking["StartAgentAtLogon"]!.GetValue<bool>();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("تعذر قراءة ملف الإعدادات. لم يتم استبداله بقيم افتراضية.", ex);
        }

        // الاستعلام عن الحالة الحالية لبدء تشغيل الخدمة والعميل مع ويندوز من النظام
        model.StartServiceWithWindows = _serviceManager.IsServiceAutoStart();
        model.StartAgentAtLogon = _serviceManager.IsAgentAutoStart();

        return model;
    }

    /// <summary>
    /// حفظ الإعدادات المعدلة في ملف JSON المشترك ذرياً (Atomically) وتطبيق خيارات التشغيل التلقائي وإعادة تشغيل العميل عند الطلب.
    /// </summary>
    /// <param name="model">النموذج الذي يحتوي على الإعدادات الجديدة.</param>
    /// <param name="restartAgentIfNeeded">ما إذا كان يجب إعادة تشغيل العميل لتطبيق التغييرات فوراً في حال كان قيد العمل.</param>
    /// <returns>رسالة نصية توضح نتيجة الحفظ وأي تحذيرات قد طرأت.</returns>
    public async Task<string> SaveSettingsAsync(AppSettingsModel model, bool restartAgentIfNeeded = true)
    {
        try
        {
            if (model.SegmentMinutes is < 1 or > 1440 || model.FrameRate is < 5 or > 60 ||
                model.VideoQuality is < 10 or > 100 || model.IdleThresholdMinutes is < 1 or > 1440 ||
                model.RetentionDays is < 1 or > 3650 || model.ActivityRetentionDays is < 1 or > 3650 ||
                model.ReportRetentionDays is < 1 or > 3650 || model.LogRetentionDays is < 1 or > 3650 ||
                model.MicrophoneVolume is < 0.1f or > 3.0f || !float.IsFinite(model.MicrophoneVolume))
                throw new ArgumentOutOfRangeException(nameof(model), "إعدادات التسجيل أو الاحتفاظ خارج الحدود المسموحة.");

            // التأكد من وجود المجلد الحاضن للملف المشترك
            var dir = Path.GetDirectoryName(SharedConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // تحميل البنية الحالية أو إنشاء جذر جديد للحفاظ على المفاتيح الأخرى
            JsonNode root;
            if (File.Exists(SharedConfigPath))
            {
                var existingJson = await File.ReadAllTextAsync(SharedConfigPath);
                root = JsonNode.Parse(existingJson) ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            // التأكد من وجود الأقسام الرئيسية في شجرة JSON
            root = ApplySettings(root, model);

            // ضبط خيارات التنسيق مع إزاحة جمالية
            var options = new JsonSerializerOptions { WriteIndented = true };

            // الكتابة الذرية عبر ملف مؤقت منعاً لتلف الملف الأصلي في حال انقطاع التيار
            var temporaryPath = SharedConfigPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporaryPath, root.ToJsonString(options));
                File.Move(temporaryPath, SharedConfigPath, true);
            }
            finally
            {
                // تنظيف الملف المؤقت إن بقي موجوداً
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch { }
            }

            // تطبيق تفضيلات بدء التشغيل التلقائي عبر مدير خدمات ويندوز
            var serviceStartupResult = model.StartServiceWithWindows == _serviceManager.IsServiceAutoStart()
                ? string.Empty : await _serviceManager.SetServiceAutoStartAsync(model.StartServiceWithWindows);
            var agentStartupResult = await _serviceManager.SetAgentAutoStartAsync(model.StartAgentAtLogon);

            // تجميع أي تحذيرات تتعلق بأذونات بدء التشغيل
            var startupWarning = IsFailureResult(serviceStartupResult) || IsFailureResult(agentStartupResult)
                ? $"تم حفظ الإعدادات، لكن تعذر تطبيق بعض إعدادات بدء التشغيل: {serviceStartupResult} {agentStartupResult}"
                : null;

            // إعادة تشغيل العميل إن كان نشطاً ومطلوباً لتطبيق إعدادات التسجيل ومعدل الإطارات فوراً
            if (restartAgentIfNeeded && _serviceManager.IsAgentRunning())
            {
                var restartResult = await _serviceManager.RestartAgentAsync();
                if (IsFailureResult(restartResult))
                {
                    startupWarning = $"{startupWarning ?? "تم حفظ الإعدادات، لكن"} تعذر إعادة تشغيل العميل: {restartResult}";
                }
            }

            return startupWarning ?? "تم حفظ الإعدادات وتطبيقها بنجاح.";
        }
        catch (Exception ex)
        {
            return $"حدث خطأ أثناء حفظ الإعدادات: {ex.Message}";
        }
    }

    /// <summary>
    /// فحص ما إذا كانت النتيجة النصية المعادة تدل على وقوع فشل أو خطأ.
    /// </summary>
    /// <param name="result">النص المراد فحصه.</param>
    /// <returns>صحيح (true) إذا تضمن النص مؤشر فشل؛ وإلا خطأ (false).</returns>
    public static JsonNode ApplySettings(JsonNode root, AppSettingsModel model)
    {
        root = root.DeepClone();
            if (root["Recording"] == null) root["Recording"] = new JsonObject();
            if (root["ActivityTracking"] == null) root["ActivityTracking"] = new JsonObject();
            if (root["Storage"] == null) root["Storage"] = new JsonObject();

            // 1. تحديث قيم قسم التسجيل
            var rec = root["Recording"]!;
            rec["Enabled"] = model.ScreenRecordingEnabled;
            rec["RecordOnlyWhenUserActive"] = model.RecordOnlyWhenUserActive;
            rec["RecordAudio"] = model.RecordMicrophone || model.RecordSystemAudio;
            rec["RecordMicrophone"] = model.RecordMicrophone;
            rec["RecordSystemAudio"] = model.RecordSystemAudio;
            rec["SelectedMicrophoneDeviceName"] = model.SelectedMicrophoneDeviceName ?? string.Empty;
            rec["SelectedMicrophoneFriendlyName"] = model.SelectedMicrophoneFriendlyName ?? string.Empty;
            rec["MicrophoneVolume"] = model.MicrophoneVolume;
            rec["IsFragmentedMp4Enabled"] = model.IsFragmentedMp4Enabled;
            rec["SegmentMinutes"] = model.SegmentMinutes;
            rec["FrameRate"] = model.FrameRate;
            rec["VideoQuality"] = model.VideoQuality;
            rec["RetentionDays"] = model.RetentionDays;

            // 2. تحديث قيم قسم تتبع الأنشطة
            var tracking = root["ActivityTracking"]!;
            tracking["Enabled"] = model.ActivityTrackingEnabled;
            tracking["IdleThresholdMinutes"] = model.IdleThresholdMinutes;
            tracking["StartAgentAtLogon"] = model.StartAgentAtLogon;

            // 3. تحديث قيم قسم التخزين والاحتفاظ
            var storage = root["Storage"]!;
            storage["RecordingRetentionDays"] = model.RetentionDays;
            storage["ActivityRetentionDays"] = model.ActivityRetentionDays;
            storage["ReportRetentionDays"] = model.ReportRetentionDays;
            storage["LogRetentionDays"] = model.LogRetentionDays;

        return root;
    }

    private static bool IsFailureResult(string result) =>
        result.Contains("فشل", StringComparison.OrdinalIgnoreCase) ||
        result.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
        result.Contains("تعذر", StringComparison.OrdinalIgnoreCase);
}
